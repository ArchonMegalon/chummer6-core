using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Runs the existing whole-build finalizer and all its nested evaluators against
/// one explicitly scoped store while the actual owner authority excludes transitions.
/// This does not owner-bind the separate domain editing services.
/// </summary>
public sealed class OwnerBoundCharacterCreationFinalizationService(
    IWorkspaceStore store,
    IOwnerContextAccessor ownerContext,
    ICharacterFileQueries characterQueries,
    ICharacterSourceDataResolver sourceResolver) : IOwnerBoundCharacterCreationFinalizationService
{
    public CharacterCreationFinalizationResult<CharacterCreationFinalizationState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.Load(request));
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> Review(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Review(request));
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Confirm(request));
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> LookupReceipt(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationReceiptLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.LookupReceipt(request));
    }

    private CharacterCreationFinalizationResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId,
        Func<CharacterCreationFinalizationService, CharacterCreationFinalizationResult<T>> action)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationFinalizationOutcomes.Unavailable, null,
                [CharacterCreationFinalizationBlockers.WorkspaceUnavailable]);

        using (lease)
        {
            // Never cache this graph or resolve unbound singleton domain services:
            // Skills/Magic construct Attributes internally and Qualities calls both
            // Prerequisite and Attributes. Every one must share this same view.
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId);
            var prerequisites = new CharacterCreationPrerequisiteService(view, characterQueries, sourceResolver);
            var attributes = new CharacterCreationAttributesService(view, sourceResolver);
            var service = new CharacterCreationFinalizationService(view, characterQueries,
                prerequisites, attributes,
                new CharacterCreationSkillsService(view, sourceResolver),
                new CharacterCreationQualitiesService(view, sourceResolver, prerequisites, attributes),
                new CharacterCreationMagicResonanceService(view, sourceResolver),
                new CharacterCreationResourcesService(view, sourceResolver),
                new CharacterCreationGearService(view, sourceResolver));
            // Includes idempotency reads, the durable CAS and postcommit receipt
            // observation. No await or postcommit owner recapture may split it.
            return action(service);
        }
    }

}
