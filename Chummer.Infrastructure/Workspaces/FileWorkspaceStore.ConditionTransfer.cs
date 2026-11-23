using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Session;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using Chummer.Contracts.Workspaces;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    private static void CheckConditionOwner(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected, IOwnerContextLease lease)
        => SessionPlayEncoding.Conflict(expected.IsValid && owners.Capture() == expected && lease.Stamp == expected, "session-owner-lease-changed");

    private SessionConditionTransferCanonicalReceipt? LookupConditionOriginalUnderLease(string path, CharacterWorkspaceId id,
        string owner, string operationId)
    {
        // No current rules, Get(), migration, archive repair or source admission is needed to inspect an original.
        var archived = ArchivedConditionOriginal5472(path, id, owner, operationId);
        if (archived is { Committed: true }) return archived.Receipt; // Exact retired original precedes today's incarnation/source.
        PersistedWorkspaceRecord record;
        try { record = ReadPrivateRecord(path); }
        catch (FileNotFoundException)
        {
            if (archived is { Committed: true }) return archived.Receipt;
            if (archived is not null) throw new IOException("condition_original_requires_reconciliation");
            return null;
        }
        var state = record.AuxiliaryState ?? WorkspaceDocumentAuxiliaryState.Empty;
        SessionPlayEncoding.Conflict(SessionConditionTransferRules.IsValidLedger(id, record.ContentRevision ?? 0, state), "condition-transfer-ledger-corrupt");
        var original = state.SessionConditionTransfers?.SingleOrDefault(r => r.Intent.Review.OperationId == operationId);
        if (original is not null)
            SessionPlayEncoding.Conflict(original.Intent.Review.OwnerId == owner && original.Intent.Review.CharacterId == id.Value
                && original.Intent.Review.Canonical.IncarnationId == record.LocalHistory?.IncarnationId,
                "condition-transfer-original-owner-or-incarnation-changed");
        if (archived is not null)
        {
            if (original is null && archived.Committed) return archived.Receipt;
            if (original is null || original.Digest != archived.Receipt.Digest)
                throw new IOException("condition_original_requires_reconciliation");
        }
        return original;
    }

    internal SessionConditionTransferCanonicalReceipt? LookupConditionTransfer5460(IOwnerContextLeaseAccessor owners,
        OwnerContextStamp expected, IOwnerContextLease lease, CharacterWorkspaceId id, string operationId)
    {
        CheckConditionOwner(owners, expected, lease); SessionPlayEncoding.Identity(operationId, "operation-id-required");
        string? path = TryGetPath(expected.Owner, id);
        if (path is null) throw new SessionPlayException(SessionPlayFailures.Unavailable("owner-workspace-unavailable"));
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        var original = LookupConditionOriginalUnderLease(path, id, expected.Owner.NormalizedValue, operationId);
        CheckConditionOwner(owners, expected, lease); return original;
    }

    private static string ConditionEnvironmentDigest(GearCurrentEnvironment current) => SessionPlayEncoding.Digest(new {
        current.Capture.IntegrityDigest, current.Capture.EffectivePolicyDigest,
        current.Execution.ExecutionProfile, current.Execution.ExecutionProfileFingerprint });

    private SessionConditionTransferBinding ConditionBinding(string path, WorkspaceStoredDocument saved, GearCurrentEnvironment current, string ownerAuthorityDigest,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string, SessionPlayAuthority> project)
    {
        SessionPlayEncoding.Conflict(saved.ContentRevision == saved.SavedRevision && !HasPendingGearEquipment(saved), "condition-transfer-unsaved-or-pending");
        if (saved.Document.AuxiliaryState.Sr5NaturalRecovery?.Operations.LastOrDefault()?.Activity.Phase is
            Chummer.Contracts.Characters.Sr5RecoveryPhase.Reserved or Chummer.Contracts.Characters.Sr5RecoveryPhase.Started
            or Chummer.Contracts.Characters.Sr5RecoveryPhase.RollUnresolved or Chummer.Contracts.Characters.Sr5RecoveryPhase.Rolled)
            throw new SessionPlayException(SessionPlayFailures.Conflict("condition-transfer-recovery-activity-pending"));
        var sources = current.Capture.CreateCurrentEnvironmentResolution(current.Execution.ExecutionProfile,
            current.Execution.ExecutionProfileFingerprint)?.Resolver
            ?? throw new SessionPlayException(SessionPlayFailures.Unavailable("session-current-source-resolution-unavailable"));
        var authority = project(saved, sources, ConditionEnvironmentDigest(current));
        SessionPlayInitialConditionSource5412.Validate(authority);
        return new(authority, saved.ContentRevision, saved.SavedRevision, saved.LocalHistory!.IncarnationId,
            ReadContinuationSlotUnderLease(path) ?? throw new SessionPlayException(SessionPlayFailures.Unavailable("condition-transfer-slot-missing")),
            saved.Document.AuxiliaryStateDigest, ownerAuthorityDigest) {
                PlayCheckpoint5712 = SessionAmmunitionSettlementRules5712.Identity(saved.Document.AuxiliaryState.SessionPlaySettlement5712)
            };
    }

    internal SessionConditionTransferBinding ReadConditionTransfer5460(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, CharacterWorkspaceId id,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string, SessionPlayAuthority> project,
        SessionConditionTransferCanonicalReceipt? committed = null)
    {
        CheckConditionOwner(owners, expected, lease);
        string? path = TryGetPath(expected.Owner, id);
        if (path is null) throw new SessionPlayException(SessionPlayFailures.Unavailable("owner-workspace-unavailable"));
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        if (committed is not null)
            SessionPlayEncoding.Conflict(SessionPlayEncoding.Digest(LookupConditionOriginalUnderLease(path, id, expected.Owner.NormalizedValue, committed.Intent.Review.OperationId))
                == SessionPlayEncoding.Digest(committed), "condition-transfer-original-divergence");
        if (committed is not null && (!File.Exists(path)
            || ReadPrivateRecord(path).LocalHistory?.IncarnationId != committed.Intent.Review.Canonical.IncarnationId))
        {
            var retired = ReadRetiredConditionBinding5472(path, id, expected.Owner.NormalizedValue, committed)
                ?? throw new IOException("condition_original_authority_unavailable");
            CheckConditionOwner(owners, expected, lease); return retired;
        }
        var read = ReadGearWorkspaceUnderLease(expected.Owner, id, path, owners, lease, expected, out _, out var environment);
        using var current = environment;
        if (!read.Success || read.Value is not { } saved || current is null)
            throw new SessionPlayException(SessionPlayFailures.Unavailable("condition-transfer-retained-source-required"));
        if (committed is not null)
            SessionPlayEncoding.Conflict(saved.ContentRevision == committed.RecordedRevision && saved.SavedRevision == committed.RecordedRevision
                && saved.LocalHistory?.IncarnationId == committed.Intent.Review.Canonical.IncarnationId
                && ReadContinuationSlotUnderLease(path) == committed.Intent.Review.Canonical.SlotGeneration
                && SessionConditionTransferRules.PostimageDigest(saved.Document) == committed.PostimageDigest
                && ConditionEnvironmentDigest(current) == committed.EnvironmentDigest
                && saved.Document.AuxiliaryState.SessionConditionTransfers?.LastOrDefault()?.Digest == committed.Digest,
                "condition-transfer-committed-state-changed");
        var binding = ConditionBinding(path, saved, current, SessionPlayEncoding.Digest(expected), project);
        current.ValidateReadObservation(); CheckConditionOwner(owners, expected, lease); return binding;
    }

    internal SessionConditionTransferCanonicalReceipt CommitConditionTransfer5460(IOwnerContextLeaseAccessor owners,
        OwnerContextStamp expected, IOwnerContextLease lease, CharacterWorkspaceId id, SessionConditionTransferCommit admissionToken,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string, SessionPlayAuthority> project)
    {
        var intent = admissionToken.Intent;
        CheckConditionOwner(owners, expected, lease);
        SessionConditionTransferRules.ValidateReview(intent.Review, expected.Owner, id.Value);
        string? path = TryGetPath(expected.Owner, id);
        if (path is null) throw new SessionPlayException(SessionPlayFailures.Unavailable("owner-workspace-unavailable"));
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        var original = LookupConditionOriginalUnderLease(path, id, expected.Owner.NormalizedValue, intent.Review.OperationId);
        // A new publication grant cannot adopt an unrelated previously committed identity.
        // Recovery uses Lookup and never reaches this mutation entry point.
        SessionPlayEncoding.Conflict(original is null, "condition-transfer-original-needs-recovery");
        var read = ReadGearWorkspaceUnderLease(expected.Owner, id, path, owners, lease, expected, out var delegated, out var environment);
        using var current = environment;
        if (!read.Success || read.Value is not { } saved || current is null)
            throw new SessionPlayException(SessionPlayFailures.Unavailable("condition-transfer-retained-source-required"));
        SessionPlayEncoding.Conflict(SessionPlayEncoding.Digest(ConditionBinding(path, saved, current, SessionPlayEncoding.Digest(expected), project)) == SessionPlayEncoding.Digest(intent.Review.Canonical),
            "condition-transfer-canonical-review-stale");
        var (document, receipt) = SessionConditionTransferRules.Apply(saved, intent, admissionToken.Continuation5712);
        // Capture identity depends on complete XML and retained rules, not on the receipt that will contain it.
        var proposedCapture = current.Capture.RecaptureCurrentEnvironment(document.Content,
            current.Execution.ExecutionProfile, current.Execution.ExecutionProfileFingerprint)
            ?? throw new SessionPlayException(SessionPlayFailures.Unavailable("condition-transfer-after-capture-unavailable"));
        receipt = receipt with { EnvironmentDigest = SessionPlayEncoding.Digest(new {
            IntegrityDigest = proposedCapture.IntegrityDigest, EffectivePolicyDigest = proposedCapture.EffectivePolicyDigest,
            current.Execution.ExecutionProfile, current.Execution.ExecutionProfileFingerprint }), Digest = "" };
        receipt = receipt with { Digest = SessionPlayEncoding.Digest(receipt) };
        document = document with { State = document.State with { AuxiliaryState = document.AuxiliaryState with {
            SessionConditionTransfers = (saved.Document.AuxiliaryState.SessionConditionTransfers ?? []).Append(receipt).ToArray() } } };
        var after = saved with { Document = document, ContentRevision = receipt.RecordedRevision, SavedRevision = receipt.RecordedRevision };
        var admitted = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture, _retainedCharacterQueries!, after);
        if (admitted.AfterCapture is not { } capture || admitted.AfterExecution is not { } execution || admitted.Blockers.Count != 0)
            throw new SessionPlayException(SessionPlayFailures.Unavailable("condition-transfer-after-admission-unavailable"));
        SessionPlayEncoding.Conflict(receipt.EnvironmentDigest == SessionPlayEncoding.Digest(new { capture.IntegrityDigest, capture.EffectivePolicyDigest,
            execution.ExecutionProfile, execution.ExecutionProfileFingerprint }), "condition-transfer-after-environment-changed");
        current.Recovery.RetainAdmittedCapture(new(capture, _retainedCharacterQueries!), capture.IntegrityDigest);
        var snapshot = new WorkspaceContinuationSnapshot(expected.Owner.NormalizedValue,
            new(saved.Id, document, saved.LastUpdatedUtc, receipt.RecordedRevision, receipt.RecordedRevision), []);
        var environmentAfter = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(snapshot, capture,
            execution.ExecutionProfile, execution.ExecutionProfileFingerprint, []);
        var record = BuildPersistedRecord(document, receipt.RecordedRevision, receipt.RecordedRevision,
            saved.LocalHistory!, delegated, saved.DelegatedGmHistorySegmentStarts) with {
            RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion,
            RetainedRuleSources = current.Record.RetainedRuleSources! with {
                References = current.Recovery.SnapshotReferences(), CurrentEnvironment = environmentAfter } };
        using var admission = new ConditionTransferWriteAdmission(this, path, record, current, after, admitted, receipt);
        admission.ValidateFinalFence(record);
        ReserveConditionOriginal5472(path, receipt, after, record, admitted, project);
        admissionToken.PublishIntent();
        WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () => {
            CheckConditionOwner(owners, expected, lease);
            SessionPlayEncoding.Conflict(SessionPlayEncoding.Digest(ConditionBinding(path, saved, current, SessionPlayEncoding.Digest(expected), project)) == SessionPlayEncoding.Digest(intent.Review.Canonical),
                "condition-transfer-source-changed-before-replace");
        });
        // Lost acknowledgement deliberately escapes. The caller must reconcile this exact receipt, never repeat Commit.
        return receipt;
    }

    [ThreadStatic] private static ConditionTransferWriteAdmission? _conditionTransferAdmission;
    private sealed class ConditionTransferWriteAdmission : IDisposable
    {
        private readonly ConditionTransferWriteAdmission? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal SessionConditionTransferCanonicalReceipt Receipt { get; }
        private readonly string _recordDigest;
        private readonly GearCurrentEnvironment _current;
        private readonly WorkspaceStoredDocument _after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult _readmission;
        internal ConditionTransferWriteAdmission(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            GearCurrentEnvironment current, WorkspaceStoredDocument after, CharacterCurrentRuleEnvironmentReadmissionResult readmission,
            SessionConditionTransferCanonicalReceipt receipt)
        {
            _previous = _conditionTransferAdmission; Store = store; Path = System.IO.Path.GetFullPath(path);
            _recordDigest = CharacterCreationFinalizationDigest.Compute(record); _current = current; _after = after;
            _readmission = readmission; Receipt = receipt; _conditionTransferAdmission = this;
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            if (CharacterCreationFinalizationDigest.Compute(record) != _recordDigest) throw new IOException("condition_transfer_record_changed");
            _current.ValidateFinalFence();
            if (_readmission.AfterCapture is not { } capture || _readmission.AfterExecution is not { } execution
                || !execution.Revalidate(_after, capture, Store._retainedCharacterQueries!))
                throw new IOException("condition_transfer_environment_changed");
        }
        public void Dispose() => _conditionTransferAdmission = _previous;
    }
    private ConditionTransferWriteAdmission? ConditionTransferAdmissionFor(string path)
        => _conditionTransferAdmission is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, Path.GetFullPath(path)) ? admission : null;

    private void ValidateConditionTransferWrite(string path, PersistedWorkspaceRecord record, WorkspaceWriteDisposition disposition)
    {
        var previous = disposition == WorkspaceWriteDisposition.ReplaceExisting ? ReadPrivateRecord(path) : null;
        var before = previous?.AuxiliaryState ?? WorkspaceDocumentAuxiliaryState.Empty;
        var after = record.AuxiliaryState ?? WorkspaceDocumentAuxiliaryState.Empty;
        if (ConditionTransferAdmissionFor(path) is { } admitted)
        {
            if (previous is null || !SessionConditionTransferRules.IsValidLedger(new(admitted.Receipt.Intent.Review.CharacterId), record.ContentRevision ?? 0, after)
                || !CharacterCareerGearEquipmentTransaction.Same(after.SessionConditionTransfers,
                    (before.SessionConditionTransfers ?? []).Append(admitted.Receipt).ToArray())
                || !CharacterCareerGearEquipmentTransaction.Same(after with { SessionConditionTransfers = before.SessionConditionTransfers,
                    SessionCondition = before.SessionCondition, SessionPlaySettlement5712 = before.SessionPlaySettlement5712 }, before)) throw new IOException("condition_transfer_history_changed");
            admitted.ValidateFinalFence(record); return;
        }
        if (ConditionHealingAdmissionFor5460(path) is { } healing)
        {
            healing.Validate(record); return;
        }
        // Imported history is preserved as history. It cannot become a locally replayable original.
        if (previous is null && (after.SessionConditionTransfers is not null || after.SessionCondition is not null))
        {
            if (record.LocalHistory is not { } history
                || (after.SessionConditionTransfers ?? []).Any(r => !history.IsImportedRevision(r.RecordedRevision))
                || after.SessionCondition is not { } imported || !history.IsImportedRevision(imported.RecordedRevision)
                || !SessionConditionTransferRules.IsValidLedger(new(after.SessionConditionTransfers?.LastOrDefault()?.Intent.Review.CharacterId ?? ""),
                    record.ContentRevision ?? 0, after)
                || !SessionConditionTransferRules.MatchesCanonicalDamage(record.Envelope!.Payload, imported.State))
                throw new IOException("condition_transfer_import_not_history");
            return;
        }
        // All writers converge here, including typed lanes and checkpoint/import. No generic caller can forge a receipt.
        if (!CharacterCareerGearEquipmentTransaction.Same(before.SessionConditionTransfers, after.SessionConditionTransfers)
            || !CharacterCareerGearEquipmentTransaction.Same(before.SessionCondition, after.SessionCondition)
            || !CharacterCareerGearEquipmentTransaction.Same(before.SessionPlaySettlement5712, after.SessionPlaySettlement5712))
            throw new IOException("condition_transfer_requires_typed_admission");
        if (after.SessionCondition is { } condition)
        {
            if (!SessionConditionTransferRules.MatchesCanonicalDamage(record.Envelope!.Payload, condition.State))
                throw new IOException("condition_transfer_canonical_facts_require_typed_transition");
        }
    }
}
