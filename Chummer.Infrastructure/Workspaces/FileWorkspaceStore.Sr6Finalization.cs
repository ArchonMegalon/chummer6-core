using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Rulesets.Sr6;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public CharacterCreationFoundationResult<Sr6CreationFinalizationCommit> CommitSr6Finalization(
        OwnerScope owner, Sr6CreationFinalizationRequest request)
    {
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)
            || !Sr6CreationFinalizationIntegrity.IsConfirmed(request))
            return Sr6CreationFoundationRules.Blocked<Sr6CreationFinalizationCommit>(Sr6CreationFinalizationBlockers.ConfirmationRequired);
        var id = request.Binding.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null) return Sr6CreationFoundationRules.Blocked<Sr6CreationFinalizationCommit>(Sr6CreationFoundationBlockers.WorkspaceUnavailable);
        bool committed = false;
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return Sr6CreationFoundationRules.Blocked<Sr6CreationFinalizationCommit>(Sr6CreationFoundationBlockers.WorkspaceUnavailable);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved)
                return Sr6CreationFoundationRules.Blocked<Sr6CreationFinalizationCommit>(Sr6CreationFoundationBlockers.WorkspaceUnavailable);
            if (Sr6CreationFinalizationRules.Lookup(saved, request) is { } prior) return prior;
            var prepared = Sr6CreationFinalizationRules.Prepare(saved, request, out var replacement);
            if (prepared.Value is not { } result || replacement is null) return prepared;
            var record = BuildPersistedRecord(replacement, result.Receipt.ContentRevision, result.Receipt.SavedRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                var rechecked = Sr6CreationFinalizationRules.Prepare(saved, request, out var finalDocument);
                if (rechecked.Value?.Receipt != result.Receipt || finalDocument?.Content != replacement.Content
                    || finalDocument.AuxiliaryStateDigest != replacement.AuxiliaryStateDigest)
                    throw new IOException("SR6 finalization inputs changed before commit.");
            });
            committed = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Read the durable result once; never repeat an ambiguous write.
        }
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (observed.Success && observed.Value is { } workspace
            && Sr6CreationFinalizationRules.Lookup(workspace, request) is { } recovered)
            return committed && recovered.Value is { } value ? recovered with { Value = value with { Replayed = false } } : recovered;
        return Sr6CreationFoundationRules.Blocked<Sr6CreationFinalizationCommit>(Sr6CreationFoundationBlockers.PersistenceUnavailable);
    }
}
