using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> CommitKarmaMetatype(
        CharacterCreationKarmaMetatypeConfirmRequest request, ICharacterSourceDataResolver sourceResolver)
        => CommitKarmaMetatype(OwnerScope.LocalSingleUser, request, sourceResolver);

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> CommitKarmaMetatype(
        OwnerScope owner, CharacterCreationKarmaMetatypeConfirmRequest request,
        ICharacterSourceDataResolver sourceResolver)
    {
        ArgumentNullException.ThrowIfNull(sourceResolver);
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner))
            return CharacterCreationKarmaMetatypeTransaction.Blocked(CharacterCreationKarmaMetatypeBlockers.WorkspaceUnavailable);
        if (!CharacterCreationKarmaMetatypeTransaction.TryFreezeRequest(request, out request))
            return CharacterCreationKarmaMetatypeTransaction.Blocked(CharacterCreationKarmaMetatypeBlockers.ConfirmationRequired);
        var id = request.Binding.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null)
            return CharacterCreationKarmaMetatypeTransaction.Blocked(CharacterCreationKarmaMetatypeBlockers.WorkspaceUnavailable);
        bool committed = false;
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return CharacterCreationKarmaMetatypeTransaction.Blocked(CharacterCreationKarmaMetatypeBlockers.WorkspaceUnavailable);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved)
                return CharacterCreationKarmaMetatypeTransaction.Blocked(CharacterCreationKarmaMetatypeBlockers.WorkspaceUnavailable);
            var prior = CharacterCreationKarmaMetatypeTransaction.Lookup(saved, request);
            if (prior is not null) return prior;
            if (!CharacterCreationKarmaMetatypeTransaction.TryBuild(owner, saved, sourceResolver, request,
                    out var replacement, out var decision)
                || !IsValidAuxiliaryState(id, decision!.CommittedContentRevision, replacement!.AuxiliaryState))
                return CharacterCreationKarmaMetatypeTransaction.Blocked(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            var record = BuildPersistedRecord(replacement, decision.CommittedContentRevision,
                decision.CommittedContentRevision, saved.LocalHistory!, delegatedLedger,
                saved.DelegatedGmHistorySegmentStarts);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting,
                beforeTargetReplace: () =>
                {
                    // Source changes after the flushed temporary file must not
                    // persist a selection under the now-obsolete preview.
                    if (!CharacterCreationKarmaMetatypeTransaction.TryBuild(owner, saved, sourceResolver, request,
                            out _, out var current) || current!.DecisionDigest != decision.DecisionDigest)
                        throw new IOException("Karma metatype source changed before commit.");
                });
            committed = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Recover by durable operation identity. Never replay the write on
            // an ambiguous IO result or report success from an in-memory draft.
        }
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (observed.Success && observed.Value is { } workspace
            && CharacterCreationKarmaMetatypeTransaction.Lookup(workspace, request) is { } recovered)
            return committed && recovered.Value is { } value
                ? recovered with { Value = value with { Replayed = false } } : recovered;
        return CharacterCreationKarmaMetatypeTransaction.Blocked(CharacterCreationKarmaMetatypeBlockers.PersistenceUnavailable);
    }
}
