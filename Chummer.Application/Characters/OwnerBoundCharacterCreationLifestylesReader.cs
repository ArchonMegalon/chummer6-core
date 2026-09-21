using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Uses the canonical evaluator without granting an unscoped read or an editing API.</summary>
public sealed class OwnerBoundCharacterCreationLifestylesReader(
    IWorkspaceStore store,
    IOwnerContextAccessor ownerContext,
    ICharacterSourceDataResolver sourceResolver) : IOwnerBoundCharacterCreationLifestylesReader
{
    public CharacterCreationLifestyleResult<CharacterCreationLifestylesState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationLifestylesLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationLifestyleOutcomes.Unavailable, null,
                [CharacterCreationLifestylesBlockers.PersistenceAuthorityRequired]);

        using (lease)
        {
            using ICharacterSourceDataResolverOperationScope? sourceScope =
                (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, request.WorkspaceId);
            return new CharacterCreationLifestylesService(view, sourceScope ?? sourceResolver).Load(request);
        }
    }
}
