using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Uses the existing rules and atomic auxiliary checkpoint in the admitted
/// owner's partition. No legacy fallback, XML mutation or blind write retry.
/// </summary>
public sealed class OwnerBoundCharacterCreationSkillsService(
    IWorkspaceStore store,
    IOwnerContextAccessor ownerContext,
    ICharacterSourceDataResolver sourceResolver) : IOwnerBoundCharacterCreationSkillsService,
    IOwnerBoundCharacterCreationSkillsReReviewService
{
    public CharacterCreationFoundationResult<CharacterCreationSkillsState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.Load(request));
    }

    public CharacterCreationFoundationResult<CharacterCreationSkillsPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Preview(request));
    }

    public CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Confirm(request));
    }

    public CharacterCreationFoundationResult<CharacterCreationSkillsReReviewState> LoadReReview(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.LoadReReview(request));
    }

    public CharacterCreationFoundationResult<CharacterCreationSkillsReReviewPreview> PreviewReReview(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsReReviewPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        ArgumentNullException.ThrowIfNull(request.Binding.Current);
        return Invoke(expectedOwner, request.Binding.Current.WorkspaceId, service => service.PreviewReReview(request));
    }

    public CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> ConfirmReReview(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsReReviewConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        ArgumentNullException.ThrowIfNull(request.Binding.Current);
        return Invoke(expectedOwner, request.Binding.Current.WorkspaceId, service => service.ConfirmReReview(request));
    }

    private CharacterCreationFoundationResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId,
        Func<CharacterCreationSkillsService, CharacterCreationFoundationResult<T>> action)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationSkillsBlockers.WorkspaceUnavailable]);

        using (lease)
        {
            // Synchronous and thread-affine, including the atomic checkpoint.
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId);
            return action(new CharacterCreationSkillsService(view, sourceResolver));
        }
    }
}
