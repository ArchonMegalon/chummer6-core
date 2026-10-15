using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCreationFinalizationAtomicCommitCapability
{
    public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> CommitFinalization(
        OwnerScope owner, CharacterCreationFinalizationConfirmRequest request,
        ICharacterSourceDataResolver resolver, ICharacterFileQueries queries)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(queries);
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)
            || !CharacterCreationFinalizationTransaction.IsConfirmed(request))
            return CharacterCreationFinalizationTransaction.Rejected(CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired);
        try { request = CharacterCreationFinalizationTransaction.Freeze(request); }
        catch (Exception error) when (error is System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        { return CharacterCreationFinalizationTransaction.Rejected(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid); }
        var id = request.Binding.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null)
            return CharacterCreationFinalizationTransaction.Rejected(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        CharacterCreationFinalizationReceipt? committed = null;
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return CharacterCreationFinalizationTransaction.Rejected(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved)
                return CharacterCreationFinalizationTransaction.Rejected(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            var prior = CharacterCreationFinalizationTransaction.Lookup(saved, request);
            if (prior is not null) return prior;
            if (!CharacterCreationFinalizationTransaction.TryBuild(this, owner, saved, resolver, queries, request,
                    out var replacement, out var receipt))
                return CharacterCreationFinalizationTransaction.Rejected(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
            var record = BuildPersistedRecord(replacement!, receipt!.ContentRevision, receipt.SavedRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                if (!CharacterCreationFinalizationTransaction.TryBuild(this, owner, saved, resolver, queries, request,
                        out var current, out var finalReceipt)
                    || finalReceipt!.ReceiptDigest != receipt.ReceiptDigest
                    || current!.Content != replacement!.Content
                    || current.AuxiliaryStateDigest != replacement.AuxiliaryStateDigest)
                    throw new IOException("Finalization source changed before commit.");
            });
            committed = receipt;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // An uncertain acknowledgement is reconciled from durable history,
            // never by repeating the mutation or resetting its idempotency key.
        }
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (observed.Success && observed.Value is { } workspace
            && CharacterCreationFinalizationTransaction.Lookup(workspace, request) is { Value: not null } result)
            return committed is null ? result : result with { Outcome = CharacterCreationFinalizationOutcomes.Applied };
        return committed is not null
            ? new(CharacterCreationFinalizationOutcomes.Applied, committed, [CharacterCreationFinalizationBlockers.PostCommitReopenRequired])
            : CharacterCreationFinalizationTransaction.Rejected(CharacterCreationFinalizationBlockers.AtomicPersistenceRejected);
    }
}
