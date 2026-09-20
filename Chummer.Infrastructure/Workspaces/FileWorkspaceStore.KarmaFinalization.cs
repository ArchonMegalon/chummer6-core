using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitKarmaFinalization(
        CharacterCreationKarmaFinalizationConfirmRequest request, ICharacterSourceDataResolver sourceResolver)
        => CommitKarmaFinalization(OwnerScope.LocalSingleUser, request, sourceResolver);

    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitKarmaFinalization(
        OwnerScope owner, CharacterCreationKarmaFinalizationConfirmRequest request, ICharacterSourceDataResolver sourceResolver)
    {
        ArgumentNullException.ThrowIfNull(sourceResolver);
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)
            || !CharacterCreationKarmaFinalizationTransaction.IsConfirmed(request))
            return CharacterCreationKarmaFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired);
        var id = request.Confirmation.Binding.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null) return CharacterCreationKarmaFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return CharacterCreationKarmaFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved)
                return CharacterCreationKarmaFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            var prior = CharacterCreationKarmaFinalizationTransaction.Lookup(saved, request);
            if (prior is not null) return prior;
            if (!CharacterCreationKarmaFinalizationTransaction.TryBuild(owner, saved, sourceResolver, request,
                    out var replacement, out var receipt))
                return CharacterCreationKarmaFinalizationTransaction.Blocked(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            var record = BuildPersistedRecord(replacement!, receipt!.ContentRevision,
                receipt.SavedRevision, saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting,
                beforeTargetReplace: () =>
                {
                    // Re-admit real source files after the temporary document has
                    // been flushed. No caller-provided XML or historical preview
                    // can bypass source changes at the commit boundary.
                    if (!CharacterCreationKarmaFinalizationTransaction.TryBuild(owner, saved, sourceResolver, request,
                            out var current, out var finalReceipt)
                        || finalReceipt!.ReceiptDigest != receipt.ReceiptDigest
                        || current!.Content != replacement!.Content
                        || current.AuxiliaryStateDigest != replacement.AuxiliaryStateDigest)
                        throw new IOException("Karma finalization source changed before commit.");
                });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Recover only from the durable receipt, including failures after
            // rename. Never automatically reissue an ambiguous mutation.
        }
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (observed.Success && observed.Value is { } workspace
            && CharacterCreationKarmaFinalizationTransaction.Lookup(workspace, request) is { } result) return result;
        return CharacterCreationKarmaFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.AtomicPersistenceRejected);
    }
}
