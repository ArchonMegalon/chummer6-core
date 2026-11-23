using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Session;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    [ThreadStatic] private static RecoveryPublication5472? _recoveryPublication5472;
    private sealed class RecoveryPublication5472 : IDisposable
    {
        private readonly RecoveryPublication5472? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        private readonly string _preHistoryRecord;
        private Action<PersistedWorkspaceRecord>? _prepare;
        internal RecoveryPublication5472(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            Action<PersistedWorkspaceRecord> prepare)
        {
            Store = store; Path = System.IO.Path.GetFullPath(path); _prepare = prepare;
            _preHistoryRecord = CharacterCreationFinalizationDigest.Compute(record with { AuthenticatedHistory = null });
            _previous = _recoveryPublication5472; _recoveryPublication5472 = this;
        }
        internal void Prepare(PersistedWorkspaceRecord published)
        {
            if (CharacterCreationFinalizationDigest.Compute(published with { AuthenticatedHistory = null }) != _preHistoryRecord)
                throw new IOException("recovery_publication_record_changed");
            var prepare = Interlocked.Exchange(ref _prepare, null) ?? throw new IOException("recovery_publication_already_prepared");
            prepare(published);
        }
        public void Dispose() => _recoveryPublication5472 = _previous;
    }
    private RecoveryPublication5472? RecoveryPublicationFor5472(string path)
        => _recoveryPublication5472 is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, System.IO.Path.GetFullPath(path)) ? admission : null;

    // A plan alone is not commit/ACK evidence. Recovery also requires the exact canonical postimage.
    private sealed record RecoveryContinuationOriginal5472(SessionNaturalRecoveryReceipt Receipt, string RecordDigest)
    {
        // Omitted when false to preserve the digest of pre-5485 archives.
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public bool JournalAcknowledged { get; init; }
    }
    private sealed record RecoveryContinuationArchive5472(int Schema, IReadOnlyList<RecoveryContinuationOriginal5472> Entries, string Digest);
    private const string RecoveryContinuationSuffix5472 = ".recovery-continuations.v1.json";
    private RecoveryContinuationArchive5472 ReadRecoveryContinuationArchive5472(string workspacePath)
    {
        string path = workspacePath + RecoveryContinuationSuffix5472;
        EnsurePathContained(_stateDirectory, path, "Recovery continuation originals");
        ThrowIfLinkOrReparsePoint(path, "Recovery continuation originals");
        if (!File.Exists(path)) return SealRecoveryContinuations5472([]);
        using var stream = File.OpenRead(path);
        if (stream.Length > 16 * 1024 * 1024) throw new IOException("recovery_original_capacity");
        var archive = JsonSerializer.Deserialize<RecoveryContinuationArchive5472>(stream, SessionPlayEncoding.Json);
        if (archive is not { Schema: 1, Entries: not null } || archive.Entries.Count > 512
            || archive.Digest != SealRecoveryContinuations5472(archive.Entries).Digest)
            throw new IOException("recovery_original_corrupt");
        var keys = new HashSet<string>();
        foreach (var item in archive.Entries)
        {
            SessionNaturalRecoveryRules.ValidateReceipt(item.Receipt, item.Receipt.Intent);
            string key = item.Receipt.Operation.Command.Request.OperationId + ":" + item.Receipt.Operation.Activity.Phase;
            if (!keys.Add(key) || item.RecordDigest.Length != 64) throw new IOException("recovery_original_corrupt");
        }
        return archive;
    }
    private static RecoveryContinuationArchive5472 SealRecoveryContinuations5472(IReadOnlyList<RecoveryContinuationOriginal5472> entries)
    {
        var value = new RecoveryContinuationArchive5472(1, entries, "");
        return value with { Digest = SessionPlayEncoding.Digest(value) };
    }
    private void PreserveRecoveryContinuationPlan5472(string path, SessionNaturalRecoveryCommit continuation,
        Sr5RecoveryOperation operation, PersistedWorkspaceRecord record, WorkspaceStoredDocument proposed,
        CharacterCurrentRuleEnvironmentReadmissionResult? readmission,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string, SessionPlayAuthority> project)
    {
        if (readmission?.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution)
            throw new IOException("recovery_continuation_readmission_required");
        var resolver = capture.CreateCurrentEnvironmentResolution(execution.ExecutionProfile, execution.ExecutionProfileFingerprint)?.Resolver
            ?? throw new IOException("recovery_continuation_resolution_required");
        string environment = SessionPlayEncoding.Digest(new { capture.IntegrityDigest, capture.EffectivePolicyDigest,
            execution.ExecutionProfile, execution.ExecutionProfileFingerprint });
        var authority = project(proposed, resolver, environment);
        var receipt = new SessionNaturalRecoveryReceipt(continuation.Intent, operation, authority,
            SessionNaturalRecoveryRules.CanonicalCondition(authority), "");
        receipt = receipt with { Digest = SessionPlayEncoding.Digest(receipt) };
        SessionNaturalRecoveryRules.ValidateReceipt(receipt, continuation.Intent);
        var item = new RecoveryContinuationOriginal5472(receipt, CharacterCreationFinalizationDigest.Compute(record));
        var archive = ReadRecoveryContinuationArchive5472(path);
        var prior = archive.Entries.SingleOrDefault(x => x.Receipt.Operation.Command.Request.OperationId == operation.Command.Request.OperationId
            && x.Receipt.Operation.Activity.Phase == operation.Activity.Phase);
        if (prior is not null)
        {
            if (SessionPlayEncoding.Digest(prior with { JournalAcknowledged = false }) != SessionPlayEncoding.Digest(item)) throw new IOException("recovery_original_plan_changed");
            return;
        }
        if (archive.Entries.Count >= 512) throw new IOException("recovery_original_capacity");
        var next = SealRecoveryContinuations5472(archive.Entries.Append(item).ToArray());
        WriteOriginalArchive5472(path + RecoveryContinuationSuffix5472, next, 16 * 1024 * 1024);
        if (ReadRecoveryContinuationArchive5472(path).Digest != next.Digest) throw new IOException("recovery_original_readback_changed");
    }
    private RecoveryContinuationOriginal5472 RequireRecoveryContinuationOriginal5472(string path,
        SessionNaturalRecoveryIntent intent, Sr5RecoveryOperation original)
    {
        var plan = ReadRecoveryContinuationArchive5472(path).Entries.SingleOrDefault(x => x.Receipt.Operation.Command.Request.OperationId == original.Command.Request.OperationId
            && x.Receipt.Operation.Activity.Phase == original.Activity.Phase);
        if (plan is null || SessionPlayEncoding.Digest(plan.Receipt.Intent) != SessionPlayEncoding.Digest(intent)
            || plan.Receipt.Operation.Digest != original.Digest
            || plan.RecordDigest != CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)))
            throw new IOException("recovery_exact_postimage_unavailable");
        return plan;
    }
    internal void ConfirmRecoveryAcknowledgement5485(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterWorkspaceId id, SessionNaturalRecoveryAcknowledgement acknowledgement)
    {
        var stamp = lease.Stamp; RecoveryOwner(owners, lease, stamp);
        var receipt = acknowledgement.Receipt;
        SessionNaturalRecoveryRules.ValidateReceipt(receipt, receipt.Intent);
        if (receipt.Intent.Review.OwnerId != stamp.Owner.NormalizedValue || receipt.Intent.Command.Request.WorkspaceId != id
            || receipt.Operation.Activity.Phase is not (Sr5RecoveryPhase.Completed or Sr5RecoveryPhase.Cancelled))
            throw new IOException("recovery_acknowledgement_identity_changed");
        string path = TryGetPath(stamp.Owner, id) ?? throw new IOException("recovery_workspace_unavailable");
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        var archive = ReadRecoveryContinuationArchive5472(path);
        var original = archive.Entries.SingleOrDefault(x => x.Receipt.Digest == receipt.Digest);
        if (original is null || SessionPlayEncoding.Digest(original.Receipt) != SessionPlayEncoding.Digest(receipt))
            throw new IOException("recovery_acknowledgement_original_changed");
        // An already acknowledged retired original is observation, not authority over today's slot.
        if (original.JournalAcknowledged) { RecoveryOwner(owners, lease, stamp); return; }
        var record = ReadPrivateRecord(path);
        var canonical = receipt.Intent.Review.Canonical;
        var live = record.AuxiliaryState?.Sr5NaturalRecovery?.Operations.SingleOrDefault(x =>
            x.Command.Request.OperationId == receipt.Operation.Command.Request.OperationId);
        if (record.LocalHistory?.IncarnationId != canonical.IncarnationId
            || ReadContinuationSlotUnderLease(path) != canonical.SlotGeneration || live?.Digest != receipt.Operation.Digest)
            throw new IOException("recovery_acknowledgement_custody_changed");
        var next = SealRecoveryContinuations5472(archive.Entries.Select(x => ReferenceEquals(x, original)
            ? x with { JournalAcknowledged = true } : x).ToArray());
        RecoveryOwner(owners, lease, stamp);
        WriteOriginalArchive5472(path + RecoveryContinuationSuffix5472, next, 16 * 1024 * 1024);
        if (ReadRecoveryContinuationArchive5472(path).Digest != next.Digest)
            throw new IOException("recovery_acknowledgement_readback_changed");
        RecoveryOwner(owners, lease, stamp);
    }
    private void GuardRecoveryDeletion5472(string path, WorkspaceStoredDocument saved)
    {
        var ledger = saved.Document.AuxiliaryState.Sr5NaturalRecovery?.Operations ?? [];
        // A rolled result is known, but an active activity must first be completed/cancelled.
        if (CharacterSr5NaturalRecoveryTransaction.Activities(saved.Document.AuxiliaryState.Sr5NaturalRecovery)
            .Any(x => !CharacterSr5NaturalRecoveryTransaction.Terminal(x))) throw new IOException("recovery_activity_requires_reconciliation");
        foreach (var entry in ReadRecoveryContinuationArchive5472(path).Entries.Where(x => x.Receipt.Intent.Review.Canonical.IncarnationId == saved.LocalHistory?.IncarnationId))
        {
            var live = ledger.SingleOrDefault(x => x.Command.Request.OperationId == entry.Receipt.Operation.Command.Request.OperationId);
            if (live is null || (entry.Receipt.Operation.Activity.Phase != Sr5RecoveryPhase.RollUnresolved && live.Digest != entry.Receipt.Operation.Digest))
                throw new IOException("recovery_original_requires_reconciliation");
            if (entry.Receipt.Operation.Activity.Phase is (Sr5RecoveryPhase.Completed or Sr5RecoveryPhase.Cancelled)
                && !entry.JournalAcknowledged)
                throw new IOException("recovery_terminal_acknowledgement_required");
        }
    }
    // Same private, bounded, flushed atomic archive pattern as Gear/Specialization originals.
    private void WriteOriginalArchive5472<T>(string path, T archive, int limit)
    {
        EnsurePathContained(_stateDirectory, path, "Condition/recovery originals");
        ThrowIfLinkOrReparsePoint(path, "Condition/recovery originals");
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(archive, SessionPlayEncoding.Json);
        if (payload.Length > limit) throw new IOException("condition_original_capacity");
        string temporary = path + TempFileMarker + Guid.NewGuid().ToString("N");
        RemoveStaleTempFiles(path);
        try
        {
            using (var stream = OpenNewSecureFile(temporary, FileAccess.Write, FileShare.None, FileOptions.WriteThrough))
            { stream.Write(payload); stream.Flush(flushToDisk: true); }
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterTempFileFlushed, path, temporary);
            ThrowIfLinkOrReparsePoint(path, "Condition/recovery originals");
            File.Move(temporary, path, overwrite: true);
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterTargetReplaced, path, temporary);
            _ = FlushRetainedDirectoryChain(Path.GetDirectoryName(path)!);
        }
        finally { DeleteRegularFileIfPresent(temporary, "Condition/recovery temporary originals"); }
    }
}
