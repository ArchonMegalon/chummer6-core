using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public IWorkspaceCurrentDisplayObservation? BeginCurrentDisplayObservation(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, CharacterWorkspaceId id,
        IRulesetWorkspaceCodecResolver unboundCodecs)
        => _retainedCharacterQueries is XmlCharacterFileQueries { HasImmutableEvaluationQueries2764: true }
            ? new CurrentDisplayObservation6178(this, owners, expectedOwner, id, unboundCodecs)
            : null;

    public IWorkspaceStartupDisplayObservation? BeginWorkspaceRefreshObservation(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        IRulesetWorkspaceCodecResolver unboundCodecs, CharacterWorkspaceId id)
        => _retainedCharacterQueries is XmlCharacterFileQueries { HasImmutableEvaluationQueries2764: true }
            ? new StartupDisplayObservation8159(this, owners, expectedOwner, unboundCodecs,
                id, selectedWorkspaceFirst: true)
            : null;

    public IWorkspaceStartupDisplayObservation? BeginStartupDisplayObservation(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        IRulesetWorkspaceCodecResolver unboundCodecs, CharacterWorkspaceId? preferredId = null)
        => _retainedCharacterQueries is XmlCharacterFileQueries { HasImmutableEvaluationQueries2764: true }
            ? new StartupDisplayObservation8159(this, owners, expectedOwner, unboundCodecs, preferredId)
            : null;

    // This object is owned by one chooser invocation, never a store/global/TTL
    // cache. Only display reads can receive it. The existing CurrentReadEvaluation
    // also binds the exact character, environment, pure queries and retained archive.
    private sealed class CurrentDisplayObservation6178(
        FileWorkspaceStore store, IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        CharacterWorkspaceId id, IRulesetWorkspaceCodecResolver codecs) : IWorkspaceCurrentDisplayObservation,
        IWorkspaceCurrentConditionMonitorObservation
    {
        private readonly object gate = new();
        private bool retired;
        private int reads;
        private string? recordDigest;
        private string? continuation;
        private DateTimeOffset timestamp;
        private PrivateCurrentRuleEnvironmentAdmission.CurrentReadEvaluation? evaluation;
        private const int MaximumReads = 128;

        internal PrivateCurrentRuleEnvironmentAdmission.CurrentReadEvaluation? BindRecord(
            string record, string? slot, WorkspaceDocumentSnapshot snapshot,
            WorkspaceCurrentRuleEnvironmentBinding? environment, ICharacterFileQueries? queries)
        {
            // Called synchronously under gate and the actual owner/workspace leases.
            if (retired || snapshot.Id != id) throw WorkspacePrivateRuleSourceClosure.Rejected();
            if (recordDigest is null)
            {
                recordDigest = record;
                continuation = slot;
                timestamp = snapshot.LastUpdatedUtc;
                if (environment is not null && queries is not null)
                    evaluation = new(expectedOwner.Owner.NormalizedValue, snapshot, environment, queries, record);
            }
            else if (recordDigest != record || continuation != slot || timestamp != snapshot.LastUpdatedUtc)
            {
                Retire();
                throw WorkspacePrivateRuleSourceClosure.Rejected();
            }
            evaluation?.ValidateRecord(expectedOwner.Owner.NormalizedValue, id, record);
            return evaluation;
        }

        public CommandResult<WorkspaceDocumentSnapshot> GetWorkspace(
            IOwnerContextLease ownerLease, CancellationToken cancellationToken = default)
            => Read(ownerLease, false, false, cancellationToken, out _, out _, out _);

        public CommandResult<WorkspaceOverviewProjection> GetOverview(
            IOwnerContextLease ownerLease, CancellationToken cancellationToken = default)
        {
            var result = Read(ownerLease, true, false, cancellationToken, out var overview, out _, out _);
            return result.Success && overview is not null
                ? new(true, overview, null, WorkspaceOperationOutcome.Success)
                : new(false, null, result.Error, result.Outcome);
        }

        public bool SupportsConditionMonitor => true;

        public CommandResult<CharacterConditionMonitorSection> GetConditionMonitor(
            IOwnerContextLease lease, CancellationToken cancellationToken = default)
        {
            var result = Read(lease, false, false, cancellationToken, out _, out _, out var monitor,
                includeConditionMonitor: true);
            return result.Success && monitor is not null
                ? new(true, monitor, null, WorkspaceOperationOutcome.Success)
                : new(false, null, result.Error, result.Outcome);
        }

        internal CommandResult<WorkspaceDocumentSnapshot> GetRosterRow8159(IOwnerContextLease ownerLease,
            CancellationToken cancellationToken, out WorkspaceListItem? row)
            => Read(ownerLease, false, true, cancellationToken, out _, out row, out _);

        private CommandResult<WorkspaceDocumentSnapshot> Read(IOwnerContextLease ownerLease,
            bool includeOverview, bool includeRoster, CancellationToken cancellationToken,
            out WorkspaceOverviewProjection? overview, out WorkspaceListItem? row,
            out CharacterConditionMonitorSection? monitor, bool includeConditionMonitor = false)
        {
            lock (gate)
            {
                overview = null;
                row = null;
                monitor = null;
                if (retired || ++reads > MaximumReads)
                {
                    Retire();
                    return CurrentDisplayRefused();
                }
                try
                {
                    var result = store.ReadCurrentDisplay(owners, expectedOwner, ownerLease, id, codecs,
                        includeOverview, out overview, out row, out monitor, cancellationToken, includeRoster,
                        observation: this, includeConditionMonitor: includeConditionMonitor);
                    if (!result.Success || result.Value is null || result.Outcome != WorkspaceOperationOutcome.Success
                        || includeOverview && overview is null || includeRoster && row is null
                        || includeConditionMonitor && monitor is null)
                    {
                        Retire();
                        overview = null;
                        row = null;
                        monitor = null;
                        return CurrentDisplayRefused(result.Outcome == WorkspaceOperationOutcome.Success
                            ? WorkspaceOperationOutcome.Unavailable : result.Outcome);
                    }
                    return result;
                }
                catch
                {
                    Retire();
                    throw;
                }
            }
        }

        private void Retire()
        {
            retired = true;
            evaluation?.Dispose();
            evaluation = null;
        }

        public void Dispose()
        {
            lock (gate) Retire();
        }
    }
    // One startup/refresh invocation owns at most one workspace evaluation.
    // Startup uses the first live roster admission. Explicit refresh may instead
    // start with its selected workspace's live display read. Other workspaces
    // retain the ordinary closed read path.
    private sealed class StartupDisplayObservation8159(
        FileWorkspaceStore store, IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        IRulesetWorkspaceCodecResolver codecs, CharacterWorkspaceId? preferredId,
        bool selectedWorkspaceFirst = false)
        : IWorkspaceStartupDisplayObservation
    {
        private readonly object gate = new();
        private bool retired;
        private int rosterReads;
        // Construction admits no data: the first borrowed read must bind the
        // actual record, owner, continuation and source through ReadCurrentDisplay.
        // Startup keeps its existing first-successful-roster semantics.
        private CharacterWorkspaceId? selectedId = selectedWorkspaceFirst ? preferredId : null;
        private CurrentDisplayObservation6178? selected = selectedWorkspaceFirst && preferredId is { } id
            ? new CurrentDisplayObservation6178(store, owners, expectedOwner, id, codecs) : null;
        private const int MaximumRosterReads = 8;

        public CommandResult<IReadOnlyList<WorkspaceListItem>> ListWorkspaces(
            IOwnerContextLease ownerLease, int? maxCount = null, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                try
                {
                    RequireActive();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++rosterReads > MaximumRosterReads)
                        throw new InvalidOperationException("The startup display observation read limit was reached.");
                    var result = store.ListCurrentDisplayWorkspaces8159(owners, expectedOwner, ownerLease,
                        codecs, maxCount, cancellationToken, this);
                    if (!result.Success || result.Value is null || result.Outcome != WorkspaceOperationOutcome.Success)
                        Retire();
                    else RequireActive();
                    return result;
                }
                catch { Retire(); throw; }
            }
        }

        internal CommandResult<WorkspaceDocumentSnapshot> ReadRosterRow(
            CharacterWorkspaceId id, IOwnerContextLease ownerLease,
            CancellationToken cancellationToken, out WorkspaceListItem? row)
        {
            lock (gate)
            {
                RequireActive();
                // A first-successful-row candidate is not yet the selection.
                // Report unknown until the existing selected/preferred input is known.
                var diagnosticSelection = selectedId ?? preferredId;
                using var rosterTiming = StartupActivities8910.StartActivity(diagnosticSelection is null
                    ? "roster-selection-unknown-read" : diagnosticSelection == id
                        ? "roster-selected-read" : "roster-non-selected-read");
                if (selected is not null && selectedId != id
                    || selected is null && preferredId is { } preferred && preferred != id)
                    return store.ReadCurrentDisplay(owners, expectedOwner, ownerLease, id, codecs,
                        false, out _, out row, out _, cancellationToken, includeRoster: true);

                var candidate = selected ?? new CurrentDisplayObservation6178(store, owners, expectedOwner, id, codecs);
                try
                {
                    var result = candidate.GetRosterRow8159(ownerLease, cancellationToken, out row);
                    if (result.Success && result.Value is not null && row is not null
                        && result.Outcome == WorkspaceOperationOutcome.Success)
                    {
                        selected = candidate;
                        selectedId = id;
                    }
                    else if (selected is not null)
                        Retire();
                    else
                    {
                        // No shared result has been issued. Preserve the roster's
                        // existing exclusion of an unreadable row, without retrying
                        // it or retaining its failed candidate as another authority.
                        candidate.Dispose();
                    }
                    return result;
                }
                catch { candidate.Dispose(); Retire(); throw; }
            }
        }

        public IWorkspaceCurrentDisplayObservation? BorrowWorkspace(CharacterWorkspaceId id)
        {
            lock (gate)
            {
                RequireActive();
                return selected is not null && selectedId == id ? new BorrowedDisplay(this) : null;
            }
        }

        private CommandResult<WorkspaceDocumentSnapshot> ReadWorkspace(
            IOwnerContextLease ownerLease, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                try
                {
                    RequireActive();
                    var result = selected!.GetWorkspace(ownerLease, cancellationToken);
                    if (!result.Success || result.Value is null || result.Outcome != WorkspaceOperationOutcome.Success)
                        Retire();
                    return result;
                }
                catch { Retire(); throw; }
            }
        }

        private CommandResult<WorkspaceOverviewProjection> ReadOverview(
            IOwnerContextLease ownerLease, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                try
                {
                    RequireActive();
                    var result = selected!.GetOverview(ownerLease, cancellationToken);
                    if (!result.Success || result.Value is null || result.Outcome != WorkspaceOperationOutcome.Success)
                        Retire();
                    return result;
                }
                catch { Retire(); throw; }
            }
        }

        private sealed class BorrowedDisplay(StartupDisplayObservation8159 parent) : IWorkspaceCurrentDisplayObservation
        {
            private int retired;

            public CommandResult<WorkspaceDocumentSnapshot> GetWorkspace(
                IOwnerContextLease ownerLease, CancellationToken cancellationToken = default)
            {
                RequireActive();
                return parent.ReadWorkspace(ownerLease, cancellationToken);
            }

            public CommandResult<WorkspaceOverviewProjection> GetOverview(
                IOwnerContextLease ownerLease, CancellationToken cancellationToken = default)
            {
                RequireActive();
                return parent.ReadOverview(ownerLease, cancellationToken);
            }

            private void RequireActive()
            {
                if (Volatile.Read(ref retired) != 0)
                    throw new InvalidOperationException("The borrowed display observation has ended.");
            }

            // End only this borrowed view. The startup owner keeps its bounded
            // evaluation for the final fresh roster read, then disposes the root.
            public void Dispose() => Interlocked.Exchange(ref retired, 1);
        }

        private void RequireActive()
        {
            if (retired) throw new InvalidOperationException("The startup display observation has ended.");
        }

        private void Retire()
        {
            retired = true;
            selected?.Dispose();
            selected = null;
        }

        public void Dispose()
        {
            lock (gate) Retire();
        }
    }

}
