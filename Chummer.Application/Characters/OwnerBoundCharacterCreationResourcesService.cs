using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Runs existing rules and atomic persistence in one admitted partition.</summary>
public sealed class OwnerBoundCharacterCreationResourcesService(
    IWorkspaceStore store,
    IOwnerContextAccessor ownerContext,
    ICharacterSourceDataResolver sourceResolver) : IOwnerBoundCharacterCreationResourcesService
{
    public CharacterCreationResourcesResult<CharacterCreationResourcesState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationResourcesLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.Load(request));
    }

    public CharacterCreationResourcesResult<CharacterCreationResourcesPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationResourcesPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Preview(request));
    }

    public CharacterCreationResourcesResult<CharacterCreationResourcesReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationResourcesConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Confirm(request));
    }

    public CharacterCreationResourcesResult<CharacterCreationResourcesReceipt> LookupReceipt(
        OwnerContextStamp expectedOwner, CharacterCreationResourcesReceiptLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.LookupReceipt(request));
    }

    private CharacterCreationResourcesResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId,
        Func<CharacterCreationResourcesService, CharacterCreationResourcesResult<T>> action)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationResourcesOutcomes.Blocked, null,
                [CharacterCreationResourcesBlockers.StaleWorkspaceRevision]);

        using (lease)
        {
            // No ambient fallback and no lease across await. Read, evaluation
            // and the existing atomic commit share this exact owner/workspace.
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId);
            return action(new CharacterCreationResourcesService(view, sourceResolver));
        }
    }
}
