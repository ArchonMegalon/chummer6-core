using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Creation resources admitted by the original display owner.</summary>
public interface IOwnerBoundCharacterCreationResourcesService
{
    CharacterCreationResourcesResult<CharacterCreationResourcesState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationResourcesLoadRequest request);

    CharacterCreationResourcesResult<CharacterCreationResourcesPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationResourcesPreviewRequest request);

    CharacterCreationResourcesResult<CharacterCreationResourcesReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationResourcesConfirmRequest request);

    CharacterCreationResourcesResult<CharacterCreationResourcesReceipt> LookupReceipt(
        OwnerContextStamp expectedOwner, CharacterCreationResourcesReceiptLookupRequest request);
}
