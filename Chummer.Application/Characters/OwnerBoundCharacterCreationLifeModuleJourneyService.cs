using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class OwnerBoundCharacterCreationLifeModuleJourneyService(
    IWorkspaceStore store, IOwnerContextAccessor ownerContext, ICharacterFileQueries characterFiles,
    ICharacterSourceDataResolver sourceResolver, ILifeModulesCatalogService catalog)
    : IOwnerBoundCharacterCreationLifeModuleJourneyService
{
    public CharacterCreationFoundationResult<CharacterCreationLifeModuleJourneyState> LoadJourney(
        OwnerContextStamp expectedOwner, CharacterCreationFoundationLoadRequest request)
        => Invoke(expectedOwner, request.WorkspaceId, service => service.LoadJourney(request));

    public CharacterCreationFoundationResult<CharacterCreationLifeModulePreview> PreviewModule(
        OwnerContextStamp expectedOwner, CharacterCreationLifeModulePreviewRequest request)
        => Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.PreviewModule(request));

    public CharacterCreationFoundationResult<CharacterCreationLifeModuleApplyReceipt> ConfirmModule(
        OwnerContextStamp expectedOwner, CharacterCreationLifeModuleConfirmRequest request)
        => Invoke(expectedOwner, request.Request.Binding.WorkspaceId, service => service.ConfirmModule(request));

    public CharacterCreationFoundationResult<CharacterCreationLifeModuleFinishPreview> PreviewFinishSelection(
        OwnerContextStamp expectedOwner, CharacterCreationLifeModuleFinishRequest request)
        => Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.PreviewFinishSelection(request));

    public CharacterCreationFoundationResult<CharacterCreationLifeModuleFinishReceipt> ConfirmFinishSelection(
        OwnerContextStamp expectedOwner, CharacterCreationLifeModuleFinishConfirmRequest request)
        => Invoke(expectedOwner, request.Request.Binding.WorkspaceId, service => service.ConfirmFinishSelection(request));

    private CharacterCreationFoundationResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId id, Func<CharacterCreationFoundationService, CharacterCreationFoundationResult<T>> action)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationFinalizationBlockers.WorkspaceUnavailable]);
        using (lease)
        {
            // Share parsed inputs only for this admitted synchronous operation.
            // The evaluator and atomic persistence retain their source and revision checks.
            using ICharacterSourceDataResolverOperationScope? sourceScope =
                (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, id);
            return action(new(view, characterFiles, sourceScope ?? sourceResolver, catalog,
                new CharacterCreationFoundationDraftApplyAuthority(view)));
        }
    }
}
