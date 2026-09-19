using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class OwnerBoundCharacterCreationKarmaMetatypeService(
    IWorkspaceStore store, IOwnerContextAccessor ownerContext, ICharacterSourceDataResolver sourceResolver)
    : IOwnerBoundCharacterCreationKarmaMetatypeService
{
    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> Load(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId id)
        => Invoke(expectedOwner, id, service => service.Load(id));

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaMetatypeBinding binding, string optionId,
        string? talentOptionId = null,
        IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? attributeAllocations = null)
        => Invoke(expectedOwner, binding.WorkspaceId, service => service.Preview(binding, optionId, talentOptionId, attributeAllocations));

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaMetatypeConfirmRequest request)
        => Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Confirm(request));

    private CharacterCreationFoundationResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId id, Func<CharacterCreationKarmaMetatypeService, CharacterCreationFoundationResult<T>> action)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationKarmaMetatypeBlockers.WorkspaceUnavailable]);
        using (lease)
            return action(new(new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, id), sourceResolver));
    }
}
