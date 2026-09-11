using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Runs the unchanged prerequisite evaluator and auxiliary checkpoint against the
/// explicitly admitted workspace partition. No ambient/local fallback or replay
/// is added; canonical rules, preview digests and receipt semantics are unchanged.
/// </summary>
public sealed class OwnerBoundCharacterCreationPrerequisiteService(
    IWorkspaceStore store,
    IOwnerContextAccessor ownerContext,
    ICharacterFileQueries characterQueries,
    ICharacterSourceDataResolver sourceResolver) : IOwnerBoundCharacterCreationPrerequisiteService
{
    public CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationPrerequisiteLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.Load(request));
    }

    public CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationPrerequisitePreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Preview(request));
    }

    public CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationPrerequisiteConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Confirm(request));
    }

    private CharacterCreationFoundationResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId,
        Func<CharacterCreationPrerequisiteService, CharacterCreationFoundationResult<T>> action)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationPrerequisiteBlockers.WorkspaceUnavailable]);

        using (lease)
        {
            // Acquire, read, evaluate, atomically checkpoint and release on this
            // one thread. Never cache this view or split admission across awaits.
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId);
            return action(new CharacterCreationPrerequisiteService(view, characterQueries, sourceResolver));
        }
    }
}
