using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> CommitLifeModuleFinalization(
        CharacterCreationFoundationFinalizationConfirmRequest request, ICharacterSourceDataResolver resolver,
        ILifeModulesCatalogService catalog, ICharacterFileQueries characterFiles)
        => CommitLifeModuleFinalization(OwnerScope.LocalSingleUser, request, resolver, catalog, characterFiles);

    public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> CommitLifeModuleFinalization(
        OwnerScope owner, CharacterCreationFoundationFinalizationConfirmRequest request, ICharacterSourceDataResolver resolver,
        ILifeModulesCatalogService catalog, ICharacterFileQueries characterFiles)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(characterFiles);
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)
            || !CharacterCreationLifeModuleFinalizationTransaction.IsConfirmed(request))
            return CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired);
        // Freeze caller-owned collections before acquiring storage authority.
        request = JsonSerializer.SerializeToElement(request).Deserialize<CharacterCreationFoundationFinalizationConfirmRequest>()!;
        if (!CharacterCreationLifeModuleFinalizationTransaction.IsConfirmed(request))
            return CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
        var id = request.Binding.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null) return CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved)
                return CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            var prior = CharacterCreationLifeModuleFinalizationTransaction.Lookup(saved, request);
            if (prior is not null) return prior;
            if (!CharacterCreationLifeModuleFinalizationTransaction.TryBuild(owner, saved, resolver, catalog, characterFiles,
                    request, out var replacement, out var receipt, out var blockers))
                return CharacterCreationLifeModuleFinalizationTransaction.Blocked(blockers);
            var record = BuildPersistedRecord(replacement!, receipt!.ContentRevision, receipt.SavedRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                // Source admission is repeated after flush, immediately before
                // replacing the durable document. No partial effect write exists.
                if (!CharacterCreationLifeModuleFinalizationTransaction.TryBuild(owner, saved, resolver, catalog, characterFiles,
                        request, out var current, out var finalReceipt, out _)
                    || finalReceipt!.ReceiptDigest != receipt.ReceiptDigest
                    || current!.Content != replacement!.Content
                    || current.AuxiliaryStateDigest != replacement.AuxiliaryStateDigest)
                    throw new IOException("Life Modules finalization source changed before commit.");
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A failure after rename is an observation problem, not permission to
            // retry the mutation. The exact durable receipt decides the result.
        }
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (observed.Success && observed.Value is { } workspace
            && CharacterCreationLifeModuleFinalizationTransaction.Lookup(workspace, request) is { } result) return result;
        return CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.AtomicPersistenceRejected);
    }
}
