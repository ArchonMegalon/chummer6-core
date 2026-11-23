using System.Diagnostics;

namespace Chummer.Application.Workspaces;

// Fixed local measurements only. No listener, delegate, payload or I/O.
// Explicit Home recovery and Career loading supply immutable numeric async attempt contexts.
// The Android diagnostic sink alone drains this process-wide bounded buffer.
public enum RecoveryPhase
{
    ActivationWait = 1, ReceiptQueue, ReceiptCore, ResumeQueue, ResumeCore,
    StageValidation, Reconstruction, CurrentAdmission, Writer, JournalRetirement,
    // Additive fixed Career phases; existing recovery values and numeric sidecar schema stay stable.
    CareerLoad = 100, CareerAuthority, CareerFirstProof, CareerQuality,
    CareerKarma, CareerNuyen, CareerKarmaExpense, CareerNuyenExpense,
    CareerAttribute, CareerActiveSkill, CareerKnowledgeSkill, CareerSkillGroup,
    CareerSpecialization, CareerBeforeRun, CareerPlaytime, CareerCalendar,
    CareerFinalProof, CareerProjection, CareerAdmission, CareerCheckpoint,
    CareerMagic, CareerRefresh, CareerCanceled, CareerSuperseded, CareerFailed
}
public enum RecoveryPhaseState { Enter = 1, Returned, Incomplete }
public readonly record struct RecoveryPhaseEntry(long AtUnixMilliseconds, long Sequence, long AttemptSequence,
    RecoveryPhase Phase, RecoveryPhaseState State, long ElapsedMilliseconds);

public static class RecoveryPhaseMeasurement
{
    public const int MaximumEntries = 128;
    private static readonly object Gate = new();
    private static readonly Queue<RecoveryPhaseEntry> Pending = new();
    private static readonly AsyncLocal<AttemptContext?> CurrentAttempt = new();
    private static readonly HashSet<long> ActiveAttempts = new();
    private static bool _enabled; // Fail closed until the saved diagnostic choice is read.
    private static long _generation, _sequence;

    public static void SetEnabled(bool enabled, long afterSequence = 0)
    {
        try
        {
            lock (Gate)
            {
                if (_enabled != enabled) { ++_generation; Pending.Clear(); ActiveAttempts.Clear(); }
                _sequence = Math.Max(_sequence, afterSequence);
                _enabled = enabled;
            }
        }
        catch (Exception) { /* Diagnostic failure never changes recovery. */ }
    }

    // Shares the retained phase-sequence seed; no owner/workspace/process identifier.
    // The active set also fences execution contexts captured by detached/late work.
    public static AttemptScope? BeginAttempt()
    {
        try
        {
            lock (Gate)
            {
                var previous = CurrentAttempt.Value;
                AttemptContext? context = _enabled && _sequence < long.MaxValue
                    && ActiveAttempts.Count < MaximumEntries ? new(++_sequence, _generation) : null;
                var scope = new AttemptScope(context, previous);
                if (context is not null) ActiveAttempts.Add(context.Sequence);
                CurrentAttempt.Value = context;
                return scope;
            }
        }
        catch (Exception) { return null; }
    }

    public static Scope? Begin(RecoveryPhase phase)
    {
        try
        {
            lock (Gate)
            {
                var attempt = CurrentAttempt.Value;
                if (!IsActive(attempt) || !Enum.IsDefined(phase) || _sequence == long.MaxValue) return null;
                var scope = new Scope(phase, ++_sequence, attempt!, Stopwatch.GetTimestamp());
                Append(scope, RecoveryPhaseState.Enter, 0);
                return scope;
            }
        }
        catch (Exception) { return null; }
    }

    public static RecoveryPhaseEntry[] Drain()
    {
        try
        {
            lock (Gate)
            {
                var rows = Pending.ToArray();
                Pending.Clear();
                return rows;
            }
        }
        catch (Exception) { return []; }
    }

    private static bool IsActive(AttemptContext? attempt)
        => _enabled && attempt is not null && attempt.Generation == _generation
            && ActiveAttempts.Contains(attempt.Sequence);

    private static void Append(Scope scope, RecoveryPhaseState state, long elapsed)
    {
        if (!IsActive(scope.Attempt)) return;
        if (Pending.Count == MaximumEntries) Pending.Dequeue();
        Pending.Enqueue(new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), scope.Sequence, scope.Attempt.Sequence,
            scope.Phase, state, Math.Clamp(elapsed, 0, 86400000)));
    }

    internal sealed record AttemptContext(long Sequence, long Generation);

    public sealed class AttemptScope : IDisposable
    {
        private readonly AttemptContext? _context, _previous;
        private int _finished;
        internal AttemptScope(AttemptContext? context, AttemptContext? previous)
            => (_context, _previous) = (context, previous);
        public void Dispose()
        {
            try
            {
                if (Interlocked.Exchange(ref _finished, 1) != 0) return;
                lock (Gate)
                {
                    if (_context is not null) ActiveAttempts.Remove(_context.Sequence);
                    if (ReferenceEquals(CurrentAttempt.Value, _context)) CurrentAttempt.Value = _previous;
                }
            }
            catch (Exception) { /* Diagnostic failure never changes recovery. */ }
        }
    }

    public sealed class Scope : IDisposable
    {
        internal readonly RecoveryPhase Phase;
        internal readonly long Sequence;
        internal readonly AttemptContext Attempt;
        private readonly long _started;
        private int _finished;
        internal Scope(RecoveryPhase phase, long sequence, AttemptContext attempt, long started)
            => (Phase, Sequence, Attempt, _started) = (phase, sequence, attempt, started);
        // Returned means the measured call/segment returned, never product acceptance.
        public void Returned() => Finish(RecoveryPhaseState.Returned);
        public void Dispose() => Finish(RecoveryPhaseState.Incomplete);
        private void Finish(RecoveryPhaseState state)
        {
            try
            {
                if (Interlocked.Exchange(ref _finished, 1) != 0) return;
                lock (Gate) Append(this, state, (long)Stopwatch.GetElapsedTime(_started).TotalMilliseconds);
            }
            catch (Exception) { /* No exception classification or text is captured. */ }
        }
    }
}
