using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class OwnerBoundCharacterCreationLoadoutService(IWorkspaceStore store, IOwnerContextAccessor owners,
    ICharacterSourceDataResolver resolver) : IOwnerBoundCharacterCreationLoadoutService
{
    public CharacterCreationFoundationResult<CharacterCreationLoadoutState> Prepare(OwnerContextStamp owner,
        CharacterWorkspaceId workspaceId, CancellationToken cancellationToken = default)
        => Invoke(owner, workspaceId, service => service.Prepare(workspaceId, cancellationToken));

    public CharacterCreationFoundationResult<CharacterCreationLoadoutPreview> Preview(OwnerContextStamp owner,
        CharacterCreationLoadoutPreviewRequest request, CancellationToken cancellationToken = default)
        => Invoke(owner, request.Binding.WorkspaceId, service => service.Preview(request, cancellationToken));

    public CharacterCreationFoundationResult<CharacterCreationLoadoutReceipt> Confirm(OwnerContextStamp owner,
        CharacterCreationLoadoutConfirmRequest request, CancellationToken cancellationToken = default)
        => Invoke(owner, request.Request.Binding.WorkspaceId, service => service.Confirm(request, cancellationToken));

    public CharacterCreationFoundationResult<CharacterCreationLoadoutReceipt> LookupReceipt(OwnerContextStamp owner,
        CharacterWorkspaceId workspaceId, string idempotencyKey)
        => Invoke(owner, workspaceId, service => service.LookupReceipt(workspaceId, idempotencyKey));

    private CharacterCreationFoundationResult<T> Invoke<T>(OwnerContextStamp owner, CharacterWorkspaceId id,
        Func<CharacterCreationLoadoutService, CharacterCreationFoundationResult<T>> action) where T : class
    {
        if (owner.Owner.UsesLocalSingleUserValue && !owner.Owner.IsLocalSingleUser
            || !OwnerContextAdmission.TryAcquire(owners, owner, out var lease))
            return CharacterCreationLoadoutService.Reject<T>(CharacterCreationLoadoutCompletionSchemas.SnapshotConflict);
        using (lease)
            return action(new(new OwnerBoundCreationWorkspaceStore(store, lease, owner, id), resolver));
    }
}
