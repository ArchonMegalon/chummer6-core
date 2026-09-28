using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Keeps the existing qualities rules and atomic checkpoint in one explicitly
/// admitted partition, including prerequisite and attribute reads.
/// </summary>
public sealed class OwnerBoundCharacterCreationQualitiesService(
    IWorkspaceStore store,
    IOwnerContextAccessor ownerContext,
    ICharacterFileQueries characterQueries,
    ICharacterSourceDataResolver sourceResolver) : IOwnerBoundCharacterCreationQualitiesService
{
    public CharacterCreationFoundationResult<CharacterCreationQualitiesState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationQualitiesLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.Load(request));
    }

    public CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationQualitiesPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Preview(request));
    }

    public CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationQualitiesConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Confirm(request));
    }

    private CharacterCreationFoundationResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId,
        Func<CharacterCreationQualitiesService, CharacterCreationFoundationResult<T>> action)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationQualitiesBlockers.RevisionConflict]);

        using (lease)
        {
            // No ambient fallback and no lease across await. Every dependent
            // evaluator reads the same exact owner/workspace observation.
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId);
            var prerequisites = new CharacterCreationPrerequisiteService(view, characterQueries, sourceResolver);
            var attributes = new CharacterCreationAttributesService(view, sourceResolver);
            return action(new CharacterCreationQualitiesService(view, sourceResolver, prerequisites, attributes));
        }
    }
}
