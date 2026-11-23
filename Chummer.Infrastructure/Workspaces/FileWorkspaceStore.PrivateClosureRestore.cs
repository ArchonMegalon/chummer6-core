using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public bool SupportsWorkspacePrivateRuleSourceClosureRestore
        => _retainedRuleSourcesEnabled && _retainedCharacterQueries is not null;

    internal sealed record PrivateClosureRestoreTarget(WorkspaceContinuationRestoreResult Result,
        string? RecordDigest = null, WorkspacePrivateRuleSourceClosureBinding? Binding = null,
        bool HasCurrentEnvironment = false, WorkspaceContinuationSnapshot? Snapshot = null)
    {
        // A portable snapshot has no parent/common-base proof. Local receipts prove
        // earlier restores, not incoming ancestry; a larger revision proves neither.
        // Keep V1 custody-only succession, but never replace a live V2 branch (or
        // activate V2 over an existing branch) without a separately proven base.
        internal bool HasUnprovenCurrentEnvironmentBase(bool incomingCurrentEnvironment)
            => Result.Target is { Exists: true } && (HasCurrentEnvironment || incomingCurrentEnvironment);

        internal bool IsRoamingCheckpointAdvance(WorkspaceContinuationSnapshot candidate)
        {
            // Save can advance only SavedRevision. A source-bound, proven common
            // base may adopt that checkpoint only when every other portable byte
            // (including auxiliary state and opaque history) is unchanged.
            if (Snapshot is null || candidate.Workspace.ContentRevision != Snapshot.Workspace.ContentRevision
                || candidate.Workspace.SavedRevision <= Snapshot.Workspace.SavedRevision) return false;
            var comparable = candidate with { Workspace = candidate.Workspace with
                { SavedRevision = Snapshot.Workspace.SavedRevision, LastUpdatedUtc = Snapshot.Workspace.LastUpdatedUtc } };
            return WorkspaceContinuationSnapshotDigest.Compute(comparable) == Result.Target?.SnapshotDigest;
        }
    }

    internal PrivateClosureRestoreTarget InspectPrivateClosureRestoreTarget(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (!SupportsWorkspacePrivateRuleSourceClosureRestore || !ValidRestoreOwner(owner)
            || TryGetPath(owner, id) is not { } path) return new(RestoreUnavailable());
        try
        {
            RequireRetainedRuleSourceStorage();
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return IsConfirmedMissingInventoryDirectory(GetWorkspaceDirectory(owner))
                    ? new(MissingRestoreTarget(owner, id), CharacterCreationFinalizationDigest.Compute<object?>(null))
                    : new(RestoreUnavailable());
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            return ReadPrivateClosureTargetUnderLease(owner, id, path, out _);
        }
        catch (Exception e) when (WorkspacePrivateRuleSourceClosure.IsFailure(e)) { return new(RestoreUnavailable()); }
    }

    private PrivateClosureRestoreTarget ReadPrivateClosureTargetUnderLease(OwnerScope owner, CharacterWorkspaceId id,
        string path, out PersistedWorkspaceRecord? record)
    {
        record = null;
        using var history = WorkspacePrivateRuleSourceClosureCompletion.BeginHistoryObservation();
        // Reuse only the detached capture inside this read's recovery. Every recovery
        // still reads exact live bytes, and each target inspection admits canonically
        // again; no display evaluation crosses the restore publication fence.
        var read = ReadWorkspaceUnderLease(owner, id, path, out var ledger, continuationRead: true,
            privateClosureRead: true, reuseCurrentCaptureForRead: true);
        string? slot = ReadContinuationSlotUnderLease(path);
        if (read.Outcome == WorkspaceOperationOutcome.Missing)
            return new(MissingRestoreTarget(owner, id, slot), CharacterCreationFinalizationDigest.Compute<object?>(null));
        if (!read.Success || read.Value?.LocalHistory is null) return new(RestoreUnavailable());
        var stored = read.Value;
        record = ReadPrivateRecord(path);
        var snapshot = new WorkspaceContinuationSnapshot(owner.NormalizedValue,
            new(id, stored.Document, stored.LastUpdatedUtc, stored.ContentRevision, stored.SavedRevision),
            ledger.Select(entry => entry.Receipt).ToArray())
            { DelegatedGmHistorySegmentStarts = stored.DelegatedGmHistorySegmentStarts.ToArray() };
        string digest = WorkspaceContinuationSnapshotDigest.Compute(snapshot);
        var binding = record.RetainedRuleSources?.PrivateClosureBinding;
        if (binding is not null && binding.ContinuationDigest != digest) return new(RestoreUnavailable());
        if (binding is null && record.RetainedRuleSources?.ActivatedClosureBinding is { } activated
            && activated.ContinuationDigest == digest) binding = activated;
        return new(new(WorkspaceContinuationRestoreOutcome.Available, Target: new(owner.NormalizedValue, id, true,
            stored.LocalHistory.IncarnationId, stored.ContentRevision, stored.SavedRevision, digest, slot)),
            CharacterCreationFinalizationDigest.Compute(record), binding,
            record.RetainedRuleSources?.CurrentEnvironment is not null, snapshot);
    }

    internal WorkspaceContinuationRestoreResult RestorePrivateClosure(PrivateClosureRestoreAdmission admission,
        WorkspacePrivateRuleSourceClosure.Decoded decoded)
    {
        bool publicationFencePassed = false;
        try
        {
            admission.ValidateFinalFence();
            if ((decoded.CurrentEnvironment is not null) != (admission.CurrentEnvironment is not null))
                return new(WorkspaceContinuationRestoreOutcome.Rejected, Blockers: ["current-rule-environment-canonical-admission-unavailable"]);
            var candidate = decoded.Snapshot;
            OwnerScope owner = admission.Owner.Owner.IsLocalSingleUser ? admission.Owner.Owner
                : new(admission.Owner.Owner.NormalizedValue);
            var id = candidate.Workspace.Id;
            if (!SupportsWorkspacePrivateRuleSourceClosureRestore || !ValidRestoreOwner(owner)
                || TryGetPath(owner, id) is not { } path || decoded.Binding != admission.Binding
                || candidate.OwnerId != owner.NormalizedValue || admission.Target.OwnerId != owner.NormalizedValue
                || admission.Target.WorkspaceId != id || candidate.Workspace.LastUpdatedUtc.Offset != TimeSpan.Zero
                || WorkspaceContinuationSnapshotDigest.Compute(candidate) != admission.Receipt.SnapshotDigest)
                return RestoreUnavailable();
            RequireRetainedRuleSourceStorage();
            if (admission.UpdateOriginal is not null && !CanUseRuleUpdateDurability())
                return new(WorkspaceContinuationRestoreOutcome.Rejected, Blockers: ["rule-update-durability-unavailable"]);
            EnsureRestoreWorkspaceDirectory(owner);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var observed = ReadPrivateClosureTargetUnderLease(owner, id, path, out var previous);
            if (observed.Result.Outcome != WorkspaceContinuationRestoreOutcome.Available) return observed.Result;
            if (observed.Result.Target != admission.Target || observed.RecordDigest != admission.TargetRecordDigest
                || admission.Target.Exists && candidate.Workspace.ContentRevision <= admission.Target.ContentRevision
                    && !ValidLocalCaptureTarget(observed, admission, candidate)
                    && (admission.RoamingBaseDigest is null || !observed.IsRoamingCheckpointAdvance(candidate)))
                return new(WorkspaceContinuationRestoreOutcome.Conflict, Target: observed.Result.Target,
                    Blockers: ["continuation-target-changed"]);
            if (admission.UpdateOriginal is null && observed.HasUnprovenCurrentEnvironmentBase(admission.CurrentEnvironment is not null)
                && (admission.RoamingBaseDigest is null || observed.Result.Target?.SnapshotDigest != admission.RoamingBaseDigest))
                return new(WorkspaceContinuationRestoreOutcome.Conflict, Target: observed.Result.Target,
                    Blockers: ["private-closure-common-base-unproven"]);
            if (observed.Result.Target is { Exists: true } && observed.Binding is null && admission.CurrentEnvironment is null)
                return new(WorkspaceContinuationRestoreOutcome.Rejected, Target: observed.Result.Target,
                    Blockers: ["private-closure-current-source-admission-required"]);

            RequireIncomingOriginHistory(admission, decoded, previous?.AuthenticatedHistory);

            // Retain original operation proofs across later reviewed revisions. Never prune a proof on save.
            var priorReceipts = previous?.RetainedRuleSources?.RestoreReceipts ?? [];
            var priorEvidence = previous?.RetainedRuleSources?.RestoreEvidence ?? [];
            if (priorEvidence.Length != priorReceipts.Length) throw RetainedRuleSourceRejected();
            if (priorReceipts.Length >= MaximumRetainedRuleSourceReferences)
                return new(WorkspaceContinuationRestoreOutcome.Rejected, Blockers: ["private-closure-recovery-capacity"]);
            if (admission.UpdateOriginal is not null)
            {
                if (previous is null || admission.CurrentEnvironment is null) throw RetainedRuleSourceRejected();
                PreserveRuleUpdateOriginal(owner, id, admission, previous);
            }
            var history = new WorkspaceLocalHistory(admission.Receipt.IncarnationId,
                candidate.Workspace.ContentRevision, admission.Receipt.SnapshotDigest) { LastRestore = admission.Receipt };
            using var recovery = new RetainedRuleSourceWorkspace(this, owner, id, history.IncarnationId,
                _retainedCharacterQueries!, [], observationOnly: true, ruleUpdateDurability: admission.UpdateOriginal is not null);
            foreach (var pair in decoded.Captures.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                admission.ValidateFinalFence();
                recovery.RetainAdmittedCapture(new(pair.Value, _retainedCharacterQueries!), pair.Key);
            }
            string? finalReceiptDigest = candidate.Workspace.Document.AuxiliaryState
                .CharacterCreationFinalizationReceipts?.SingleOrDefault()?.Receipt.ReceiptDigest;
            foreach (var campaign in decoded.Campaigns)
            {
                if (campaign.ReceiptDigest == finalReceiptDigest) recovery.RetainCommittedCampaign(campaign);
                else recovery.RetainCommittedDraftCampaign(campaign);
            }
            var manifest = BuildRetainedManifest(owner, id, history, candidate.Workspace.Document, recovery) with
            {
                PrivateClosureBinding = admission.CurrentEnvironment is null ? decoded.Binding : null,
                CurrentEnvironment = decoded.CurrentEnvironment,
                ActivatedClosureBinding = admission.CurrentEnvironment is null ? null : decoded.Binding,
                RestoreReceipts = [.. priorReceipts, admission.Receipt],
                RestoreEvidence = [.. priorEvidence, new(
                    RestoreEvidenceKind(admission), admission.Receipt)]
            };
            var ledger = candidate.DelegatedGmCharacterEdits.Select(receipt =>
                new DelegatedGmCharacterEditLedgerEntry(receipt.IdempotencyKeySha256, receipt.CommandSha256, receipt)).ToArray();
            var record = BuildPersistedRecord(candidate.Workspace.Document, candidate.Workspace.ContentRevision,
                candidate.Workspace.SavedRevision, history, ledger, candidate.DelegatedGmHistorySegmentStarts) with
                { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion, RetainedRuleSources = manifest,
                    AuthenticatedHistory = decoded.AuthenticatedHistory };
            ValidatePrivateClosureCustody(record, owner, id);
            _ = WriteRecordAtomically(path, record, admission.Target.Exists
                    ? WorkspaceWriteDisposition.ReplaceExisting : WorkspaceWriteDisposition.CreateNew,
                candidate.Workspace.LastUpdatedUtc, beforeTargetReplace: () =>
                {
                    var finalTarget = ReadPrivateClosureTargetUnderLease(owner, id, path, out _);
                    if (finalTarget.Result.Outcome != WorkspaceContinuationRestoreOutcome.Available
                        || finalTarget.Result.Target != admission.Target
                        || finalTarget.RecordDigest != admission.TargetRecordDigest
                        || admission.CaptureOnly && !ValidLocalCaptureTarget(finalTarget, admission, candidate)
                        || admission.UpdateOriginal is null && finalTarget.HasUnprovenCurrentEnvironmentBase(admission.CurrentEnvironment is not null)
                            && (admission.RoamingBaseDigest is null || finalTarget.Result.Target?.SnapshotDigest != admission.RoamingBaseDigest)
                        || (finalTarget.Result.Target is { Exists: true } && finalTarget.Binding is null && admission.CurrentEnvironment is null)
                        || WorkspaceContinuationSnapshotDigest.Compute(candidate) != admission.Receipt.SnapshotDigest)
                        throw new InvalidOperationException("continuation-target-changed");
                    // Assets and candidate record are flushed. Missing V2 targets and V1
                    // custody publish a slot before the record; the slot is not a receipt.
                    if (!recovery.ValidateRetainedRuleSources()) throw RetainedRuleSourceRejected();
                    admission.ValidateFinalFence();
                    RequireIncomingOriginHistory(admission, decoded, previous?.AuthenticatedHistory);
                    publicationFencePassed = true;
                }, closureAdmission: admission);
            try
            {
                var reopened = ReadPrivateClosureTargetUnderLease(owner, id, path, out var stored);
                if (reopened.Binding == admission.Binding && stored?.LocalHistory?.LastRestore == admission.Receipt)
                    return new(WorkspaceContinuationRestoreOutcome.Applied, admission.Receipt, reopened.Result.Target,
                        Blockers: admission.CurrentEnvironment is null ? ["private-closure-current-source-admission-required"] : admission.CurrentEnvironment.Diagnostics);
            }
            catch (Exception e) when (e is OperationCanceledException || WorkspacePrivateRuleSourceClosure.IsFailure(e)) { }
            return new(WorkspaceContinuationRestoreOutcome.Applied, admission.Receipt,
                Blockers: admission.CurrentEnvironment is null
                ? ["private-closure-current-source-admission-required", "continuation-postcommit-reopen-required"]
                : ["continuation-postcommit-reopen-required"],
                ReopenRequired: true);
        }
        catch (OperationCanceledException) when (!publicationFencePassed) { return new(WorkspaceContinuationRestoreOutcome.Canceled); }
        catch (InvalidOperationException) when (!publicationFencePassed)
        {
            return new(WorkspaceContinuationRestoreOutcome.Conflict, Blockers: ["continuation-final-fence-rejected"]);
        }
        catch (Exception error) when (publicationFencePassed
            && (error is OperationCanceledException || WorkspacePrivateRuleSourceClosure.IsFailure(error)))
        {
            // Publication may already have occurred. Reconcile the original operation;
            // an uncertain rename must never become a retryable cancellation.
            throw new IOException("Private closure publication requires original-operation recovery.");
        }
        // Other I/O errors propagate to the service's recovery-only result.
    }

    private static bool ValidLocalCaptureTarget(PrivateClosureRestoreTarget observed,
        PrivateClosureRestoreAdmission admission, WorkspaceContinuationSnapshot candidate)
        => admission.CaptureOnly && admission.CurrentEnvironment is not null
            && observed.Result.Target is { Exists: true } && !observed.HasCurrentEnvironment && observed.Binding is null
            && observed.Result.Target.SnapshotDigest == WorkspaceContinuationSnapshotDigest.Compute(candidate)
            && admission.RoamingBaseDigest == observed.Result.Target.SnapshotDigest;

    internal WorkspaceContinuationRestoreResult RecoverPrivateClosureRestore(OwnerScope owner, CharacterWorkspaceId id,
        Guid operationId, string admissionDigest)
    {
        if (!SupportsWorkspacePrivateRuleSourceClosureRestore || !ValidRestoreOwner(owner) || operationId == Guid.Empty
            || !DelegatedGmCharacterEditLedgerValidator.IsSha256(admissionDigest) || TryGetPath(owner, id) is not { } path)
            return RestoreUnavailable();
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return IsConfirmedMissingInventoryDirectory(GetWorkspaceDirectory(owner))
                    ? new(WorkspaceContinuationRestoreOutcome.RecoveryProofMissing) : RestoreUnavailable();
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var record = File.Exists(path) ? ReadPrivateRecord(path) : null;
            if (record is not null) ValidatePrivateClosureCustody(record, owner, id);
            var progress = RecoverRuleUpdateProgressUnderLease(owner, id, path, operationId, admissionDigest, record);
            // Do not pair a visible receipt with Unknown and let a caller retire
            // its journal. Required sync/identity failure withholds the receipt.
            if (progress?.State == WorkspaceRuleUpdateState.Unknown)
                return new(WorkspaceContinuationRestoreOutcome.Unavailable, Blockers: ["restore-outcome-requires-recovery"])
                    { RuleUpdate = progress };
            var receipt = record?.RetainedRuleSources?.RestoreReceipts?.SingleOrDefault(value =>
                value.OperationId == operationId && value.AdmissionDigest == admissionDigest);
            if (receipt is null)
                return new(WorkspaceContinuationRestoreOutcome.RecoveryProofMissing) { RuleUpdate = progress };
            // The receipt proves the original operation, not the current runner.
            // Observe the current record and slot under this same workspace lease,
            // after reconciliation. Later saves/restores must not acquire an old
            // activation identity from a historical receipt.
            var current = ReadPrivateClosureTargetUnderLease(owner, id, path, out _);
            return new(WorkspaceContinuationRestoreOutcome.Recovered, receipt, current.Result.Target,
                Blockers: record!.RetainedRuleSources?.CurrentEnvironment is null
                    ? ["private-closure-current-source-admission-required"] : ["current-rule-environment-reopen-required"],
                ReopenRequired: record.RetainedRuleSources?.CurrentEnvironment is not null) { RuleUpdate = progress };
        }
        catch (Exception e) when (WorkspacePrivateRuleSourceClosure.IsFailure(e))
        {
            return RestoreUnavailable() with
            {
                // Malformed/missing local classification cannot authorize a receipt,
                // even when no sidecar survives to identify the operation's kind.
                RuleUpdate = new(operationId, admissionDigest, PrivateRuleUpdateOriginal.Decision,
                    WorkspaceRuleUpdateState.Unknown)
            };
        }
    }

    private static void ValidatePrivateClosureCustody(PersistedWorkspaceRecord record, OwnerScope owner, CharacterWorkspaceId id)
    {
        if (record.RetainedRuleSources?.CurrentEnvironment is not null)
        {
            ValidateCurrentEnvironmentRecord(record, owner, id);
            return;
        }
        ValidateRestoreEvidence(record.RetainedRuleSources);
        if (record.RetainedRuleSources?.ActivatedClosureBinding is not null) throw RetainedRuleSourceRejected();
        if (record.RetainedRuleSources?.PrivateClosureBinding is not { } binding)
        {
            if (record.RetainedRuleSources?.RestoreReceipts is not null) throw RetainedRuleSourceRejected();
            return;
        }
        var manifest = record.RetainedRuleSources;
        var receipts = manifest.RestoreReceipts;
        if (record.RecordSchemaVersion != RetainedWorkspaceRecordSchemaVersion || manifest.SchemaVersion != 1
            || record.ContentRevision is not { } revision || record.LocalHistory is not { } history
            || !history.IsValid(revision) || history.LocalAdoption is not null
            || manifest.OwnerId != owner.NormalizedValue || manifest.WorkspaceId != id.Value
            || manifest.IncarnationId != history.IncarnationId || binding.Schema != WorkspacePrivateRuleSourceClosureBinding.SchemaV1
            || binding.CurrentEnvironmentDigest is not null
            || binding.OwnerId != owner.NormalizedValue || binding.WorkspaceId != id
            || binding.ContentRevision != revision || binding.SavedRevision != record.SavedRevision
            || binding.ArchiveCount <= 0 || manifest.References is null || binding.ArchiveCount != manifest.References.Length
            || manifest.References.Any(reference => reference is null)
            || binding.ArchiveBytes != manifest.References.Sum(reference => reference.ByteLength)
            || binding.CampaignCount != (manifest.CommittedDraftCampaigns?.Length ?? 0) + (manifest.CommittedCampaign is null ? 0 : 1)
            || !IsRetainedCaptureDigest(binding.ClosureDigest) || history.LastRestore is not { } latest
            || latest.SourceDigest != binding.ClosureDigest || latest.SnapshotDigest != binding.ContinuationDigest
            || receipts is not { Length: > 0 and <= MaximumRetainedRuleSourceReferences }
            || receipts.Any(receipt => receipt is null) || receipts[^1] != latest
            || receipts.Select(receipt => receipt.OperationId).Distinct().Count() != receipts.Length)
            throw RetainedRuleSourceRejected();
        long priorRevision = 0;
        foreach (var receipt in receipts)
        {
            if (receipt.ContentRevision <= priorRevision || receipt.ContentRevision > revision
                || !(new WorkspaceLocalHistory(receipt.IncarnationId, receipt.ContentRevision, receipt.SnapshotDigest)
                    { LastRestore = receipt }).IsValid(receipt.ContentRevision))
                throw RetainedRuleSourceRejected();
            priorRevision = receipt.ContentRevision;
        }
    }
}
