using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>One leased, source-scoped read observation; ordinary Contacts mutation is unchanged.</summary>
public sealed class OwnerBoundCharacterCreationContactsCompletionReader(
    IWorkspaceStore store,
    IOwnerContextAccessor ownerContext,
    ICharacterSourceDataResolver sourceResolver,
    ICharacterFileQueries characterQueries) : IOwnerBoundCharacterCreationContactsCompletionReader
{
    public CharacterCreationContactResult<CharacterCreationContactsCompletionInputs> LoadCompletionInputs(
        OwnerContextStamp expectedOwner, CharacterCreationContactsLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationContactOutcomes.Blocked, null,
                [CharacterCreationContactsBlockers.StaleWorkspaceRevision]);
        using (lease)
        {
            using ICharacterSourceDataResolverOperationScope? sourceScope =
                (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner,
                request.WorkspaceId, reuseReadObservation: true);
            return new CharacterCreationContactsService(view, sourceScope ?? sourceResolver, characterQueries)
                .LoadCompletionInputs(request);
        }
    }
}
