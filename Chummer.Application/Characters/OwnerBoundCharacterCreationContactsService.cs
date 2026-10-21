using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed class OwnerBoundCharacterCreationContactsService(
    CharacterCreationContactsService service, IOwnerContextAccessor ownerContext)
    : IOwnerBoundCharacterCreationContactsService
{
    public CharacterCreationContactResult<CharacterCreationContactsState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationContactsLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return Unavailable<CharacterCreationContactsState>();
        using (lease)
            return service.InvokeOwner(lease, expectedOwner, request.WorkspaceId, bound => bound.Load(request));
    }

    public CharacterCreationContactResult<CharacterCreationContactPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationContactPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return Unavailable<CharacterCreationContactPreview>();
        using (lease)
            return service.InvokeOwner(lease, expectedOwner, request.Binding?.WorkspaceId ?? default, bound => bound.Preview(request));
    }

    public CharacterCreationContactResult<CharacterCreationContactReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationContactConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return Unavailable<CharacterCreationContactReceipt>();
        using (lease)
            return service.InvokeOwner(lease, expectedOwner, request.Binding?.WorkspaceId ?? default, bound => bound.Confirm(request));
    }

    public CharacterCreationContactResult<CharacterCreationContactReceipt> LookupReceipt(
        OwnerContextStamp expectedOwner, CharacterCreationContactReceiptLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return Unavailable<CharacterCreationContactReceipt>();
        using (lease)
            return service.InvokeOwner(lease, expectedOwner, request.WorkspaceId, bound => bound.LookupReceipt(request));
    }

    private static CharacterCreationContactResult<T> Unavailable<T>() where T : class
        => new(CharacterCreationContactOutcomes.Unavailable, null,
            [CharacterCreationContactsBlockers.PersistenceAuthorityRequired]);
}
