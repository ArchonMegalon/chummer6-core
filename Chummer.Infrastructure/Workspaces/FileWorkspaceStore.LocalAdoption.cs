using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : IWorkspaceLocalAdoptionCapability
{
    public WorkspaceLocalAdoptionResult AdoptLocalWorkspace(WorkspaceLocalAdoptionAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        var receipt = admission.Receipt;
        OwnerScope account = admission.Owner.Owner;
        if (IsInvalidScopedOwner(account) || receipt.OwnerId != account.NormalizedValue
            || !receipt.IsValid(receipt.ContentRevision)
            || TryGetPath(OwnerScope.LocalSingleUser, receipt.WorkspaceId) is not { } sourcePath
            || TryGetPath(account, receipt.WorkspaceId) is not { } targetPath)
            return new(WorkspaceLocalAdoptionOutcome.Rejected);
        bool claimed = false;
        try
        {
            EnsureRestoreWorkspaceDirectory(account);
            // Every adoption locks local first, then its account destination.
            // Ordinary operations hold one workspace lock, never the reverse pair.
            using var sourceLease = AcquireWorkspaceOperation(sourcePath, recoverStaleTempFiles: false);
            using var targetLease = AcquireWorkspaceOperation(targetPath, recoverStaleTempFiles: false);
            var source = ReadRestoreTargetUnderLease(OwnerScope.LocalSingleUser, receipt.WorkspaceId,
                sourcePath, out var stored, out var local);
            var target = ReadRestoreTargetUnderLease(account, receipt.WorkspaceId, targetPath, out _, out _);
            if (source.Target != admission.Source || target.Target != admission.Target
                || source.Outcome != WorkspaceContinuationRestoreOutcome.Available
                || target.Outcome != WorkspaceContinuationRestoreOutcome.Available
                || target.Target is not { Exists: false }
                || local is not { DelegatedGmCharacterEdits.Count: 0, DelegatedGmHistorySegmentStarts.Count: 0 }
                || stored?.LocalHistory is not { ImportedThroughRevision: 0, LastRestore: null, LocalAdoption: null })
                return new(WorkspaceLocalAdoptionOutcome.Conflict);

            // The claim is the durable ownership boundary. Preserve XML, every
            // auxiliary receipt/draft, revisions, incarnation and checkpoint time.
            // Imported execution history cannot be relabelled as account-local.
            var record = BuildPersistedRecord(stored.Document, stored.ContentRevision, stored.SavedRevision,
                stored.LocalHistory with { LocalAdoption = receipt }, [], stored.DelegatedGmHistorySegmentStarts);
            _ = WriteRecordAtomically(sourcePath, record, WorkspaceWriteDisposition.ReplaceExisting,
                stored.LastUpdatedUtc, () =>
                {
                    RotateContinuationSlotUnderLease(sourcePath);
                    RotateContinuationSlotUnderLease(targetPath);
                    admission.ValidateFinalFence();
                });
            claimed = true;
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterLocalAdoptionClaimed, sourcePath, targetPath);
            MoveClaimedLocalRunner(sourcePath, targetPath);
            // No post-commit cancellation or observer error may imply that the
            // user should repeat the claim. Recovery is receipt-bound.
            try { _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterLocalAdoptionMoved, sourcePath, targetPath); }
            catch (Exception) { }
            return new(WorkspaceLocalAdoptionOutcome.Applied, receipt);
        }
        catch (OperationCanceledException)
        {
            return new(claimed ? WorkspaceLocalAdoptionOutcome.RecoveryRequired
                : WorkspaceLocalAdoptionOutcome.Canceled, claimed ? receipt : null);
        }
        catch (Exception e) when (RestoreStorageFailure(e) || e is InvalidOperationException)
        {
            return new(claimed ? WorkspaceLocalAdoptionOutcome.RecoveryRequired
                : WorkspaceLocalAdoptionOutcome.Unavailable, claimed ? receipt : null);
        }
    }

    public WorkspaceLocalAdoptionResult RecoverLocalWorkspaceAdoption(
        OwnerScope account, CharacterWorkspaceId id, Guid operationId)
    {
        if (IsInvalidScopedOwner(account) || operationId == Guid.Empty
            || TryGetPath(OwnerScope.LocalSingleUser, id) is not { } sourcePath
            || TryGetPath(account, id) is not { } targetPath)
            return new(WorkspaceLocalAdoptionOutcome.Rejected);
        try
        {
            EnsureRestoreWorkspaceDirectory(account);
            using var sourceLease = AcquireWorkspaceOperation(sourcePath, recoverStaleTempFiles: false);
            using var targetLease = AcquireWorkspaceOperation(targetPath, recoverStaleTempFiles: false);
            var target = ReadWorkspaceUnderLease(account, id, targetPath, out _, continuationRead: true);
            var source = ReadWorkspaceUnderLease(OwnerScope.LocalSingleUser, id, sourcePath,
                out var ledger, continuationRead: true, localAdoptionRead: true);
            bool Matches(WorkspaceLocalAdoptionReceipt? candidate) => candidate is not null
                && candidate.OperationId == operationId && candidate.OwnerId == account.NormalizedValue
                && candidate.WorkspaceId == id;
            if (source.Outcome == WorkspaceOperationOutcome.Missing
                && target.Success && Matches(target.Value?.LocalHistory?.LocalAdoption))
                return new(WorkspaceLocalAdoptionOutcome.Recovered, target.Value!.LocalHistory!.LocalAdoption);
            if (!source.Success || source.Value?.LocalHistory?.LocalAdoption is not { } receipt
                || !Matches(receipt))
                return new(WorkspaceLocalAdoptionOutcome.Rejected);
            if (target.Outcome != WorkspaceOperationOutcome.Missing)
                return new(WorkspaceLocalAdoptionOutcome.RecoveryRequired, receipt);
            var current = new WorkspaceContinuationSnapshot(OwnerScope.LocalSingleUser.NormalizedValue,
                new(id, source.Value.Document, source.Value.LastUpdatedUtc,
                    source.Value.ContentRevision, source.Value.SavedRevision),
                ledger.Select(entry => entry.Receipt).ToArray())
                { DelegatedGmHistorySegmentStarts = source.Value.DelegatedGmHistorySegmentStarts };
            if (source.Value.LocalHistory.IncarnationId != receipt.IncarnationId
                || WorkspaceContinuationSnapshotDigest.Compute(current) != receipt.LocalSnapshotDigest
                || WorkspaceContinuationSnapshotDigest.Compute(current with { OwnerId = receipt.OwnerId })
                    != receipt.AccountSnapshotDigest)
                return new(WorkspaceLocalAdoptionOutcome.RecoveryRequired, receipt);
            // This completes placement of an already durably claimed record. It
            // does not run rules, rewrite receipts, create a new claim or overwrite.
            MoveClaimedLocalRunner(sourcePath, targetPath);
            return new(WorkspaceLocalAdoptionOutcome.Recovered, receipt);
        }
        catch (Exception e) when (RestoreStorageFailure(e) || e is InvalidOperationException)
        {
            return new(WorkspaceLocalAdoptionOutcome.RecoveryRequired);
        }
    }

    private static void MoveClaimedLocalRunner(string sourcePath, string targetPath)
    {
        ThrowIfLinkOrReparsePoint(sourcePath, "local adoption source");
        ThrowIfLinkOrReparsePoint(targetPath, "local adoption destination");
        File.Move(sourcePath, targetPath, overwrite: false);
    }
}
