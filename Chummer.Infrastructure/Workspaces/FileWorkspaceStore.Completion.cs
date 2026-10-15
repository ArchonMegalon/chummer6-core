using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitCompletion(
        CharacterCreationCompletionApplyRequest request, ICharacterSourceDataResolver sourceResolver,
        ICharacterFileQueries characterQueries, CancellationToken cancellationToken = default)
        => CommitCompletion(OwnerScope.LocalSingleUser, request, sourceResolver, characterQueries, cancellationToken);

    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitCompletion(
        OwnerScope owner, CharacterCreationCompletionApplyRequest request,
        ICharacterSourceDataResolver sourceResolver, ICharacterFileQueries characterQueries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceResolver);
        ArgumentNullException.ThrowIfNull(characterQueries);
        cancellationToken.ThrowIfCancellationRequested();
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)
            || !CharacterCreationCompletionTransaction.IsConfirmed(request))
            return CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired);
        try { request = CharacterCreationCompletionTransaction.Freeze(request); }
        catch (Exception error) when (error is System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        { return CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid); }
        var id = request.Binding.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null)
            return CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            cancellationToken.ThrowIfCancellationRequested();
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved)
                return CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            var prior = CharacterCreationCompletionTransaction.Lookup(saved, request);
            if (prior is not null) return prior;
            if (!CharacterCreationCompletionTransaction.TryBuild(this, owner, saved, sourceResolver, characterQueries,
                    request, cancellationToken, out var replacement, out var receipt))
                return CharacterCreationCompletionTransaction.Blocked(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
            var record = BuildPersistedRecord(replacement!, receipt!.ContentRevision, receipt.SavedRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            cancellationToken.ThrowIfCancellationRequested();
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting,
                beforeTargetReplace: () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Resolve live sources again after the temporary record is
                    // flushed, under the same actual owner/workspace lease.
                    if (!CharacterCreationCompletionTransaction.TryBuild(this, owner, saved, sourceResolver, characterQueries,
                            request, cancellationToken, out var current, out var finalReceipt)
                        || finalReceipt!.ReceiptDigest != receipt.ReceiptDigest
                        || current!.Content != replacement!.Content
                        || current.AuxiliaryStateDigest != replacement.AuxiliaryStateDigest)
                        throw new IOException("Completion source changed before commit.");
                    cancellationToken.ThrowIfCancellationRequested();
                });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A failed acknowledgement is not permission to replay a mutation.
            // Resolve its outcome only from the already-written durable receipt.
        }
        // Do not check cancellation after rename: the committed result must be
        // reconciled even if the caller departed during the atomic write.
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (observed.Success && observed.Value is { } workspace
            && CharacterCreationCompletionTransaction.Lookup(workspace, request) is { } result) return result;
        return CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.AtomicPersistenceRejected);
    }
}
