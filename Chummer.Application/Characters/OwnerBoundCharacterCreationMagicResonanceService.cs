using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Runs the existing rules and atomic draft checkpoint in the explicitly admitted
/// owner partition. A lease alone does not redirect an unscoped workspace store.
/// </summary>
public sealed class OwnerBoundCharacterCreationMagicResonanceService(
    IWorkspaceStore store,
    IOwnerContextAccessor ownerContext,
    ICharacterSourceDataResolver sourceResolver) : IOwnerBoundCharacterCreationMagicResonanceService
{
    public CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationMagicResonanceLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.Load(request));
    }

    public CharacterCreationFoundationResult<CharacterCreationMagicResonancePreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationMagicResonancePreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Preview(request));
    }

    public CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationMagicResonanceConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Confirm(request));
    }

    private CharacterCreationFoundationResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId,
        Func<CharacterCreationMagicResonanceService, CharacterCreationFoundationResult<T>> action)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision]);

        using (lease)
        {
            // No ambient fallback, duplicated rules, or lease carried across await.
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId);
            return action(new CharacterCreationMagicResonanceService(view, sourceResolver));
        }
    }
}
