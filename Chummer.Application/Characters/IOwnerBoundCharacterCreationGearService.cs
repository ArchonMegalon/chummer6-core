using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Creation gear admitted by the original display owner.</summary>
public interface IOwnerBoundCharacterCreationGearService
{
    CharacterCreationGearResult<CharacterCreationGearState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationGearLoadRequest request);

    CharacterCreationGearResult<CharacterCreationGearPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationGearPreviewRequest request);

    CharacterCreationGearResult<CharacterCreationGearReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationGearConfirmRequest request);

    CharacterCreationGearResult<CharacterCreationGearReceipt> LookupReceipt(
        OwnerContextStamp expectedOwner, CharacterCreationGearReceiptLookupRequest request);
}
