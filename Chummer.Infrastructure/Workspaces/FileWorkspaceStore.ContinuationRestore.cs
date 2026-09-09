using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : IWorkspaceContinuationRestoreCapability
{
    public bool SupportsWorkspaceContinuationRestore => true;

    public WorkspaceContinuationRestoreResult InspectRestoreTarget(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (!ValidRestoreOwner(owner) || TryGetPath(owner, id) is not { } path)
            return RestoreUnavailable();
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return IsConfirmedMissingInventoryDirectory(GetWorkspaceDirectory(owner))
                    ? MissingRestoreTarget(owner, id) : RestoreUnavailable();
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            return ReadRestoreTargetUnderLease(owner, id, path, out _, out _);
        }
        catch (Exception e) when (RestoreStorageFailure(e)) { return RestoreUnavailable(); }
    }

    public WorkspaceContinuationRestoreResult RestoreContinuation(WorkspaceContinuationRestoreAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        try
        {
            if (!admission.TryOpenSnapshot(out var candidate) || candidate is null)
                return RestoreUnavailable();
            OwnerScope owner = admission.Owner.Owner;
            CharacterWorkspaceId id = candidate.Workspace.Id;
            if (!ValidRestoreOwner(owner) || TryGetPath(owner, id) is not { } path
                || admission.Target.OwnerId != owner.NormalizedValue
                || admission.Target.WorkspaceId != id
                || candidate.Workspace.LastUpdatedUtc.Offset != TimeSpan.Zero)
                return RestoreUnavailable();

            // Never migrate a legacy owner or record as an incidental restore effect.
            EnsureRestoreWorkspaceDirectory(owner);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var observed = ReadRestoreTargetUnderLease(owner, id, path, out _, out _);
            if (observed.Outcome != WorkspaceContinuationRestoreOutcome.Available || observed.Target is null)
                return observed;
            if (observed.Target != admission.Target)
                return observed with { Outcome = WorkspaceContinuationRestoreOutcome.Conflict,
                    Blockers = ["continuation-target-changed"] };
            if (observed.Target.Exists && candidate.Workspace.ContentRevision <= observed.Target.ContentRevision)
                return observed with { Outcome = WorkspaceContinuationRestoreOutcome.Conflict,
                    Blockers = ["continuation-target-revision-conflict"] };

            // Reconstruct private fields only from the complete admitted receipts.
            // They remain imported/unverified and may never authorize a local replay.
            var ledger = candidate.DelegatedGmCharacterEdits.Select(receipt =>
                new DelegatedGmCharacterEditLedgerEntry(receipt.IdempotencyKeySha256,
                    receipt.CommandSha256, receipt)).ToArray();
            if (ledger.Length > MaximumDelegatedEditAuditEntries
                || !DelegatedGmCharacterEditLedgerValidator.IsValidSegmentedLedger(owner, id,
                    candidate.Workspace.ContentRevision, ledger, candidate.DelegatedGmHistorySegmentStarts)
                || !IsValidAuxiliaryState(id, candidate.Workspace.ContentRevision, candidate.Workspace.Document.AuxiliaryState)
                || WorkspaceContinuationSnapshotDigest.Compute(candidate) != admission.Receipt.SnapshotDigest)
                return new(WorkspaceContinuationRestoreOutcome.Rejected, Blockers: ["continuation-state-invalid"]);

            var history = new WorkspaceLocalHistory(admission.Receipt.IncarnationId,
                candidate.Workspace.ContentRevision, admission.Receipt.SnapshotDigest)
                { LastRestore = admission.Receipt };
            var record = BuildPersistedRecord(candidate.Workspace.Document,
                candidate.Workspace.ContentRevision, candidate.Workspace.SavedRevision, history,
                ledger, candidate.DelegatedGmHistorySegmentStarts);
            DateTimeOffset expectedTimestamp = candidate.Workspace.LastUpdatedUtc;
            _ = WriteRecordAtomically(path, record, observed.Target.Exists
                    ? WorkspaceWriteDisposition.ReplaceExisting : WorkspaceWriteDisposition.CreateNew,
                expectedTimestamp, beforeTargetReplace: () =>
                {
                    var finalTarget = ReadRestoreTargetUnderLease(owner, id, path, out _, out _);
                    if (finalTarget.Outcome != WorkspaceContinuationRestoreOutcome.Available
                        || finalTarget.Target != admission.Target)
                        throw new InvalidOperationException("continuation-target-changed");
                    RotateContinuationSlotUnderLease(path);
                    // Keep lifetime/source admission after all intervening reads
                    // and durable slot I/O. Slow storage must not carry an
                    // expired or canceled permit through the runner replacement.
                    // A rejected attempt may conservatively rotate only the slot.
                    admission.ValidateFinalFence();
                });

            // The rename is already a known commit. Reopen trouble must not be
            // reported as permission to repeat the write. Keep the local receipt.
            try
            {
                var reopened = ReadRestoreTargetUnderLease(owner, id, path, out var stored, out _);
                if (reopened.Target?.SnapshotDigest == admission.Receipt.SnapshotDigest
                    && stored?.LocalHistory?.LastRestore == admission.Receipt)
                    return new(WorkspaceContinuationRestoreOutcome.Applied, admission.Receipt, reopened.Target);
            }
            catch (Exception e) when (RestoreStorageFailure(e) || e is InvalidOperationException) { }
            return new(WorkspaceContinuationRestoreOutcome.Applied, admission.Receipt,
                Blockers: ["continuation-postcommit-reopen-required"], ReopenRequired: true);
        }
        catch (OperationCanceledException) { return new(WorkspaceContinuationRestoreOutcome.Canceled); }
        catch (InvalidOperationException)
        {
            return new(WorkspaceContinuationRestoreOutcome.Conflict, Blockers: ["continuation-final-fence-rejected"]);
        }
        catch (Exception e) when (RestoreStorageFailure(e)) { return RestoreUnavailable(); }
    }

    public WorkspaceContinuationRestoreResult RecoverContinuationRestore(OwnerScope owner, CharacterWorkspaceId id,
        Guid operationId, string admissionDigest)
    {
        if (!ValidRestoreOwner(owner) || operationId == Guid.Empty
            || !DelegatedGmCharacterEditLedgerValidator.IsSha256(admissionDigest)
            || TryGetPath(owner, id) is not { } path)
            return RestoreUnavailable();
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return IsConfirmedMissingInventoryDirectory(GetWorkspaceDirectory(owner))
                    ? new(WorkspaceContinuationRestoreOutcome.RecoveryProofMissing) : RestoreUnavailable();
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var observed = ReadRestoreTargetUnderLease(owner, id, path, out var stored, out _);
            if (observed.Outcome != WorkspaceContinuationRestoreOutcome.Available) return observed;
            var receipt = stored?.LocalHistory?.LastRestore;
            if (receipt is null || receipt.OperationId != operationId || receipt.AdmissionDigest != admissionDigest)
                return new(WorkspaceContinuationRestoreOutcome.RecoveryProofMissing, Target: observed.Target);
            return new(WorkspaceContinuationRestoreOutcome.Recovered, receipt, observed.Target);
        }
        catch (Exception e) when (RestoreStorageFailure(e)) { return RestoreUnavailable(); }
    }

    private WorkspaceContinuationRestoreResult ReadRestoreTargetUnderLease(OwnerScope owner,
        CharacterWorkspaceId id, string path, out WorkspaceStoredDocument? stored,
        out WorkspaceContinuationSnapshot? snapshot)
    {
        stored = null; snapshot = null;
        var read = ReadWorkspaceUnderLease(owner, id, path, out var ledger, continuationRead: true);
        string? slotGeneration = ReadContinuationSlotUnderLease(path);
        if (read.Outcome == WorkspaceOperationOutcome.Missing)
            return MissingRestoreTarget(owner, id, slotGeneration);
        if (!read.Success || read.Value?.LocalHistory is null) return RestoreUnavailable();
        stored = read.Value;
        snapshot = new(owner.NormalizedValue, new(id, stored.Document, stored.LastUpdatedUtc,
            stored.ContentRevision, stored.SavedRevision), ledger.Select(entry => entry.Receipt).ToArray())
            { DelegatedGmHistorySegmentStarts = stored.DelegatedGmHistorySegmentStarts.ToArray() };
        return new(WorkspaceContinuationRestoreOutcome.Available, Target: new(owner.NormalizedValue,
            id, true, stored.LocalHistory.IncarnationId, stored.ContentRevision, stored.SavedRevision,
            WorkspaceContinuationSnapshotDigest.Compute(snapshot), slotGeneration));
    }

    private void EnsureRestoreWorkspaceDirectory(OwnerScope owner)
    {
        ValidateStaticAncestorChain(_stateDirectory);
        EnsureSecureDirectory(_stateDirectory, "workspace state root");
        VerifyPrivateStateRoot(_stateDirectory);
        string ownerDirectory = OwnerScopedStatePath.ResolveWorkspaceOwnerDirectory(_stateDirectory, owner);
        if (!PathComparer.Equals(ownerDirectory, _stateDirectory))
        {
            string ownersDirectory = Path.Combine(_stateDirectory, "owners");
            EnsurePathContained(_stateDirectory, ownersDirectory, "workspace owners directory");
            EnsureSecureDirectory(ownersDirectory, "workspace owners directory");
            using var migration = AcquireOwnerMigrationOperation(ownerDirectory, recoverStaleTempFiles: false);
            RefuseLegacyContinuationDirectory(owner, ownerDirectory);
            EnsureSecureDirectory(ownerDirectory, "workspace owner directory");
        }
        EnsureSecureDirectory(Path.Combine(ownerDirectory, "workspaces"), "workspace directory");
    }

    private static bool ValidRestoreOwner(OwnerScope owner) => owner.IsLocalSingleUser
        || (!owner.UsesLocalSingleUserValue && !string.IsNullOrWhiteSpace(owner.NormalizedValue));

    private static WorkspaceContinuationRestoreResult MissingRestoreTarget(OwnerScope owner, CharacterWorkspaceId id,
        string? slotGeneration = null)
        => new(WorkspaceContinuationRestoreOutcome.Available,
            Target: new(owner.NormalizedValue, id, false, null, 0, 0, null, slotGeneration));

    private static WorkspaceContinuationRestoreResult RestoreUnavailable()
        => new(WorkspaceContinuationRestoreOutcome.Unavailable, Blockers: ["continuation-storage-unavailable"]);

    private static bool RestoreStorageFailure(Exception e) => e is IOException or UnauthorizedAccessException
        or JsonException or ArgumentException or NotSupportedException;
}
