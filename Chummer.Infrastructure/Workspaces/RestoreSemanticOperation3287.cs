using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;

namespace Chummer.Infrastructure.Workspaces;

internal enum RestoreSemanticRole3287 { Incoming, Original, Local }

// A synchronous service stack local, never attached to a review, store, receipt,
// resolver, AsyncLocal or static cache. It replaces deterministic computation only.
internal sealed class RestoreSemanticOperation3287 : IDisposable
{
    private const int MaximumRoles = 3;
    private const int MaximumSnapshotBytes = 32 * 1024 * 1024;
    private const long MaximumRetainedInputBytes = 256L * 1024 * 1024;
    private readonly IOwnerContextLease lease;
    private readonly OwnerContextStamp owner;
    private readonly TimeProvider clock;
    private readonly DateTimeOffset expires;
    private readonly CancellationToken cancellation;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly CultureInfo culture = CultureInfo.CurrentCulture, uiCulture = CultureInfo.CurrentUICulture;
    private readonly Dictionary<RestoreSemanticRole3287, Observation> observations = [];
    private readonly HashSet<RestoreSemanticRole3287> cold = [];
    private int poisoned, busy;
    private long retainedBytes;
    private CharacterWorkspaceId? workspaceId;
    internal int SemanticEvaluations { get; private set; }

    internal RestoreSemanticOperation3287(IOwnerContextLease lease, OwnerContextStamp owner,
        TimeProvider clock, DateTimeOffset expires, CancellationToken cancellation)
        => (this.lease, this.owner, this.clock, this.expires, this.cancellation)
            = (lease, owner, clock, expires, cancellation);

    internal Observation? Observe(RestoreSemanticRole3287 role, WorkspaceContinuationSnapshot snapshot,
        CharacterCreationRuleSourceCapture capture, WorkspaceCurrentRuleEnvironmentBinding environment,
        ICharacterFileQueries queries, ICharacterCreationRuleSourceRecovery? recovery = null) => Guard(() =>
    {
        if (!Enum.IsDefined(role) || snapshot.OwnerId != owner.Owner.NormalizedValue
            || workspaceId is { } id && id != snapshot.Workspace.Id) throw WorkspacePrivateRuleSourceClosure.Rejected();
        workspaceId ??= snapshot.Workspace.Id;
        if (cold.Contains(role)) return null;
        if (observations.TryGetValue(role, out var existing))
        {
            existing.AddWitness(snapshot, capture, environment, queries);
            return existing;
        }
        // Ineligibility chooses the original independent path BEFORE issuance.
        // An issued observation may never fall back after a mismatch.
        if (!FileSystemCharacterSourceDataResolver.CanOwnOperationInputs3287(capture, queries)
            || !FileSystemCharacterSourceDataResolver.HasIsolatedOperationEnvironment3287
            || !ImmutableCulture(culture) || !ImmutableCulture(uiCulture))
        { cold.Add(role); return null; }
        byte[] snapshotBytes = [], archive = [], metadata = [], environmentBytes = [];
        Observation? observation = null;
        bool retained = false;
        try
        {
            snapshotBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
            archive = capture.ExportPrivateInputs();
            metadata = Metadata(capture);
            environmentBytes = JsonSerializer.SerializeToUtf8Bytes(environment);
            long bytes = (long)snapshotBytes.Length + archive.Length + metadata.Length + environmentBytes.Length;
            if (snapshotBytes.Length > MaximumSnapshotBytes || observations.Count >= MaximumRoles
                || bytes > MaximumRetainedInputBytes - retainedBytes)
            {
                cold.Add(role);
                return null;
            }
            observation = new(this, snapshot, capture, environment, queries,
                snapshotBytes, archive, metadata, environmentBytes);
            observation.ValidateInputs();
            if (!ReferenceEquals(culture, CultureInfo.CurrentCulture) || !ReferenceEquals(uiCulture, CultureInfo.CurrentUICulture))
                throw WorkspacePrivateRuleSourceClosure.Rejected();
            observation.Evaluate(recovery);
            observation.ValidateInputs();
            observations.Add(role, observation);
            retainedBytes += bytes;
            retained = true;
            return observation;
        }
        finally
        {
            if (!retained)
            {
                observation?.Dispose();
                Clear(snapshotBytes, archive, metadata, environmentBytes);
            }
        }
    });

    internal void Validate(Observation observation, CancellationToken ct) => Guard(() =>
    {
        ct.ThrowIfCancellationRequested();
        RequireOwned(observation);
        return true;
    });

    internal CharacterCurrentRuleEffectComparison? Compare(Observation after, Observation before,
        CancellationToken ct) => Guard(() =>
    {
        ct.ThrowIfCancellationRequested();
        RequireOwned(after); RequireOwned(before);
        if (!FileSystemCharacterSourceDataResolver.HasIsolatedOperationEnvironment3287)
            throw WorkspacePrivateRuleSourceClosure.Rejected();
        return after.Execution?.CompareOwnedOperationEffects3287(before.Execution!, ct)
            ?? throw WorkspacePrivateRuleSourceClosure.Rejected();
    });

    private void RequireOwned(Observation observation)
    {
        if (!observations.Values.Any(value => ReferenceEquals(value, observation)))
            throw WorkspacePrivateRuleSourceClosure.Rejected();
    }

    private T Guard<T>(Func<T> action)
    {
        if (Volatile.Read(ref poisoned) != 0 || thread != Environment.CurrentManagedThreadId
            || Interlocked.CompareExchange(ref busy, 1, 0) != 0)
        { Poison(); throw WorkspacePrivateRuleSourceClosure.Rejected(); }
        try
        {
            Authority();
            foreach (var value in observations.Values) value.Validate();
            var result = action();
            Authority();
            foreach (var value in observations.Values) value.Validate();
            cancellation.ThrowIfCancellationRequested();
            if (Volatile.Read(ref poisoned) != 0 || thread != Environment.CurrentManagedThreadId
                || observations.Count != 0 && (!ReferenceEquals(culture, CultureInfo.CurrentCulture)
                    || !ReferenceEquals(uiCulture, CultureInfo.CurrentUICulture)))
                throw WorkspacePrivateRuleSourceClosure.Rejected();
            return result;
        }
        catch { Poison(); throw; }
        finally { Volatile.Write(ref busy, 0); }
    }

    private void Authority()
    {
        cancellation.ThrowIfCancellationRequested();
        if (Volatile.Read(ref poisoned) != 0 || thread != Environment.CurrentManagedThreadId || !owner.IsValid
            || observations.Count != 0 && (!ReferenceEquals(culture, CultureInfo.CurrentCulture)
                || !ReferenceEquals(uiCulture, CultureInfo.CurrentUICulture))
            || clock.GetUtcNow() >= expires || lease.Stamp != owner)
            throw WorkspacePrivateRuleSourceClosure.Rejected();
        // Lease and TimeProvider are live, possibly callback-capable observations.
        cancellation.ThrowIfCancellationRequested();
        if (Volatile.Read(ref poisoned) != 0) throw WorkspacePrivateRuleSourceClosure.Rejected();
    }

    private static bool ImmutableCulture(CultureInfo value) => value.GetType() == typeof(CultureInfo)
        && value.IsReadOnly && value.NumberFormat.IsReadOnly && value.DateTimeFormat.IsReadOnly
        && value.DateTimeFormat.Calendar.IsReadOnly;
    internal void Invalidate() => Poison();
    private void Poison() => Interlocked.Exchange(ref poisoned, 1);
    public void Dispose()
    {
        Poison();
        // A foreign/reentrant call only poisons. The owning stack clears graphs on
        // unwind; never mutate its collection while it may be enumerating it.
        if (thread != Environment.CurrentManagedThreadId || Volatile.Read(ref busy) != 0) return;
        foreach (var value in observations.Values) value.Dispose();
        observations.Clear(); cold.Clear(); retainedBytes = 0;
    }
    private static byte[] Metadata(CharacterCreationRuleSourceCapture capture) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        capture.IntegrityDigest, capture.EffectivePolicyDigest, capture.CompatibilityProfile,
        capture.CompatibilityStatus, capture.RequiredPackageSetDigest, capture.RequiredRulePacks,
        capture.RequiredAssets, capture.OrderedCustomSources, capture.CompatibilityDiagnostics, capture.SourcebookSelection
    });
    private static void Clear(params byte[][] arrays)
    { foreach (var bytes in arrays) CryptographicOperations.ZeroMemory(bytes); }

    internal sealed class Observation : IDisposable
    {
        private readonly RestoreSemanticOperation3287 operation;
        private readonly ICharacterFileQueries queries;
        private readonly byte[] snapshotBytes, archive, metadata, environmentBytes;
        private readonly List<(WorkspaceContinuationSnapshot Snapshot, CharacterCreationRuleSourceCapture Capture,
            WorkspaceCurrentRuleEnvironmentBinding Environment)> witnesses = [];
        private WorkspaceContinuationSnapshot? owned;
        private CharacterCreationRuleSourceCapture? ownedCapture;
        internal CharacterCurrentRuleEnvironmentExecution? Execution { get; private set; }
        internal IReadOnlyList<string> Blockers { get; private set; } = [];
        internal RestoreSemanticOperation3287 Operation => operation;

        internal Observation(RestoreSemanticOperation3287 operation, WorkspaceContinuationSnapshot snapshot,
            CharacterCreationRuleSourceCapture capture, WorkspaceCurrentRuleEnvironmentBinding environment,
            ICharacterFileQueries queries, byte[] snapshotBytes, byte[] archive, byte[] metadata, byte[] environmentBytes)
        {
            (this.operation, this.queries, this.snapshotBytes, this.archive, this.metadata, this.environmentBytes)
                = (operation, queries, snapshotBytes, archive, metadata, environmentBytes);
            witnesses.Add((snapshot, capture, environment));
            // Construct sealed outer records explicitly (WorkspaceDocument has
            // multiple constructors). JSON materializes every nested auxiliary and
            // receipt collection; no caller list, array, converter or resolver is retained.
            var source = snapshot.Workspace;
            var state = source.Document.State;
            var auxiliaryBytes = JsonSerializer.SerializeToUtf8Bytes(state.AuxiliaryState);
            var receiptBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot.DelegatedGmCharacterEdits);
            try
            {
                var auxiliary = JsonSerializer.Deserialize<WorkspaceDocumentAuxiliaryState>(auxiliaryBytes)
                    ?? throw WorkspacePrivateRuleSourceClosure.Rejected();
                var receipts = JsonSerializer.Deserialize<DelegatedGmCharacterEditAuditReceipt[]>(receiptBytes)
                    ?? throw WorkspacePrivateRuleSourceClosure.Rejected();
                var document = new WorkspaceDocument(new WorkspaceDocumentState(state.RulesetId,
                    state.SchemaVersion, state.PayloadKind, state.Payload) { AuxiliaryState = auxiliary }, source.Document.Format);
                owned = new(snapshot.OwnerId, new(source.Id, document, source.LastUpdatedUtc,
                    source.ContentRevision, source.SavedRevision), receipts)
                { DelegatedGmHistorySegmentStarts = snapshot.DelegatedGmHistorySegmentStarts.ToArray() };
            }
            finally { Clear(auxiliaryBytes, receiptBytes); }
            ownedCapture = FileSystemCharacterSourceDataResolver.OwnOperationInputs3287(capture, queries, archive);
        }

        internal void AddWitness(WorkspaceContinuationSnapshot snapshot, CharacterCreationRuleSourceCapture capture,
            WorkspaceCurrentRuleEnvironmentBinding environment, ICharacterFileQueries queries)
        {
            if (!ReferenceEquals(queries, this.queries) || witnesses.Count >= 4)
                throw WorkspacePrivateRuleSourceClosure.Rejected();
            Check(snapshot, capture, environment);
            if (!witnesses.Any(w => ReferenceEquals(w.Snapshot, snapshot) && ReferenceEquals(w.Capture, capture)
                && ReferenceEquals(w.Environment, environment))) witnesses.Add((snapshot, capture, environment));
        }

        internal void Evaluate(ICharacterCreationRuleSourceRecovery? recovery)
        {
            var snapshot = owned ?? throw WorkspacePrivateRuleSourceClosure.Rejected();
            var workspace = snapshot.Workspace;
            var current = new WorkspaceStoredDocument(workspace.Id, workspace.Document,
                workspace.ContentRevision, workspace.SavedRevision, workspace.LastUpdatedUtc);
            current = current.WithValidatedCreationReceiptHistory(recovery);
            operation.SemanticEvaluations++;
            var result = CharacterCurrentRuleEnvironmentAdmission.AdmitCurrentEnvironment(
                current, ownedCapture!, queries, operation.cancellation);
            Execution = result.Execution;
            Blockers = result.Blockers;
        }

        private void Check(WorkspaceContinuationSnapshot snapshot, CharacterCreationRuleSourceCapture capture,
            WorkspaceCurrentRuleEnvironmentBinding environment)
        {
            if (!FileSystemCharacterSourceDataResolver.CanOwnOperationInputs3287(capture, queries)
                || !snapshotBytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(snapshot))
                || !archive.AsSpan().SequenceEqual(capture.ExportPrivateInputs())
                || !metadata.AsSpan().SequenceEqual(Metadata(capture))
                || !environmentBytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(environment)))
                throw WorkspacePrivateRuleSourceClosure.Rejected();
        }
        internal void ValidateInputs()
        {
            if (owned is null || ownedCapture is null
                || !snapshotBytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(owned))
                || !archive.AsSpan().SequenceEqual(ownedCapture.ExportPrivateInputs())
                || !metadata.AsSpan().SequenceEqual(Metadata(ownedCapture)))
                throw WorkspacePrivateRuleSourceClosure.Rejected();
            foreach (var witness in witnesses) Check(witness.Snapshot, witness.Capture, witness.Environment);
        }
        internal void Validate()
        {
            ValidateInputs();
            if (Execution is not null && !Execution.ValidateOwnedOperationObservation3287(operation.cancellation))
                throw WorkspacePrivateRuleSourceClosure.Rejected();
            ValidateInputs();
        }
        public void Dispose()
        {
            Clear(snapshotBytes, archive, metadata, environmentBytes);
            if (ownedCapture is not null) FileSystemCharacterSourceDataResolver.RetireOperationInputs3287(ownedCapture);
            Execution = null; Blockers = []; owned = null; ownedCapture = null; witnesses.Clear();
        }
    }
}
