using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class OwnerBoundCharacterCreationLifeModuleFinalizationService(
    IWorkspaceStore store, IOwnerContextAccessor ownerContext, ICharacterFileQueries characterFiles,
    ICharacterSourceDataResolver sourceResolver, ILifeModulesCatalogService catalog)
    : IOwnerBoundCharacterCreationLifeModuleFinalizationService
{
    public CharacterCreationFoundationResult<CharacterCreationFoundationState> Load(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId)
        => Invoke(expectedOwner, workspaceId, service => service.Load(new(workspaceId)));

    public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationFoundationFinalizationPreviewRequest request)
        => Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.PreviewFinalization(request));

    public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationFoundationFinalizationConfirmRequest request)
        => Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.ConfirmFinalization(request));

    private CharacterCreationFoundationResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId id, Func<CharacterCreationFoundationService, CharacterCreationFoundationResult<T>> action)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationFinalizationBlockers.WorkspaceUnavailable]);
        using (lease)
        {
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, id);
            return action(new(view, characterFiles, sourceResolver, catalog,
                new CharacterCreationFoundationDraftApplyAuthority(view)));
        }
    }
}
