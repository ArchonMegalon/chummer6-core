using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCreationLoadoutAtomicCommitCapability
{
    public CharacterCreationFoundationResult<CharacterCreationLoadoutReceipt> CommitLoadout(OwnerScope owner,
        CharacterCreationLoadoutConfirmRequest request, ICharacterSourceDataResolver resolver,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner) || !CharacterCreationLoadoutService.IsConfirmed(request))
            return CharacterCreationLoadoutService.Reject<CharacterCreationLoadoutReceipt>(CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired);
        request = CharacterCreationLoadoutService.Freeze(request);
        var id = request.Request.Binding.WorkspaceId;
        string commandDigest = CharacterCreationLoadoutRules.Compute(request);
        string? path = TryGetPath(owner, id);
        if (path is null) return CharacterCreationLoadoutService.Reject<CharacterCreationLoadoutReceipt>(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return CharacterCreationLoadoutService.Reject<CharacterCreationLoadoutReceipt>(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            cancellationToken.ThrowIfCancellationRequested();
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved)
                return CharacterCreationLoadoutService.Reject<CharacterCreationLoadoutReceipt>(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            var replay = CharacterCreationLoadoutService.Lookup(saved, request.IdempotencyKey, commandDigest);
            if (replay is not null) return replay;
            if (!CharacterCreationLoadoutService.TryBuild(this, saved, resolver, request, cancellationToken, out var replacement, out var receipt))
                return CharacterCreationLoadoutService.Reject<CharacterCreationLoadoutReceipt>(CharacterCreationLoadoutCompletionSchemas.SnapshotConflict);
            var record = BuildPersistedRecord(replacement!, receipt!.ContentRevision, receipt.SavedRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CharacterCreationLoadoutService.TryBuild(this, saved, resolver, request, cancellationToken,
                        out var current, out var currentReceipt)
                    || currentReceipt!.ReceiptDigest != receipt.ReceiptDigest
                    || current!.Content != replacement!.Content || current.AuxiliaryStateDigest != replacement.AuxiliaryStateDigest)
                    throw new IOException("Manual loadout source changed before commit.");
                cancellationToken.ThrowIfCancellationRequested();
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Resolve a lost acknowledgement only from durable history.
        }
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        return observed.Success && observed.Value is { } workspace
            ? CharacterCreationLoadoutService.Lookup(workspace, request.IdempotencyKey, commandDigest)
                ?? CharacterCreationLoadoutService.Reject<CharacterCreationLoadoutReceipt>(CharacterCreationFinalizationBlockers.AtomicPersistenceRejected)
            : CharacterCreationLoadoutService.Reject<CharacterCreationLoadoutReceipt>(CharacterCreationFinalizationBlockers.AtomicPersistenceRejected);
    }
}
