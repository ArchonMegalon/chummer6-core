using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Runs existing rules and atomic persistence in one admitted partition.</summary>
public sealed class OwnerBoundCharacterCreationGearService(
    IWorkspaceStore store,
    IOwnerContextAccessor ownerContext,
    ICharacterSourceDataResolver sourceResolver) : IOwnerBoundCharacterCreationGearService
{
    public CharacterCreationGearResult<CharacterCreationGearState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationGearLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.Load(request));
    }

    public CharacterCreationGearResult<CharacterCreationGearPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationGearPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Preview(request));
    }

    public CharacterCreationGearResult<CharacterCreationGearReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationGearConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Confirm(request));
    }

    public CharacterCreationGearResult<CharacterCreationGearReceipt> LookupReceipt(
        OwnerContextStamp expectedOwner, CharacterCreationGearReceiptLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.LookupReceipt(request));
    }

    private CharacterCreationGearResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId,
        Func<CharacterCreationGearService, CharacterCreationGearResult<T>> action)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationGearOutcomes.Blocked, null,
                [CharacterCreationGearBlockers.StaleWorkspaceRevision]);

        using (lease)
        {
            // No ambient fallback and no lease across await. Read, evaluation
            // and the existing atomic commit share this exact owner/workspace.
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId);
            return action(new CharacterCreationGearService(view, sourceResolver));
        }
    }
}
