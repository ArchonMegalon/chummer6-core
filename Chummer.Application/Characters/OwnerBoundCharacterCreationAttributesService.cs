using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Uses the existing rules and atomic auxiliary checkpoint in the admitted
/// owner's partition. No legacy fallback, XML mutation or write retry.
/// </summary>
public sealed class OwnerBoundCharacterCreationAttributesService(
    IWorkspaceStore store,
    IOwnerContextAccessor ownerContext,
    ICharacterSourceDataResolver sourceResolver) : IOwnerBoundCharacterCreationAttributesService
{
    public CharacterCreationFoundationResult<CharacterCreationAttributesState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationAttributesLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.Load(request));
    }

    public CharacterCreationFoundationResult<CharacterCreationAttributesPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationAttributesPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Preview(request));
    }

    public CharacterCreationFoundationResult<CharacterCreationAttributesReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationAttributesConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Confirm(request));
    }

    private CharacterCreationFoundationResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId,
        Func<CharacterCreationAttributesService, CharacterCreationFoundationResult<T>> action)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationAttributesBlockers.WorkspaceUnavailable]);

        using (lease)
        {
            // Synchronous and thread-affine, including the atomic checkpoint.
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId);
            return action(new CharacterCreationAttributesService(view, sourceResolver));
        }
    }
}
