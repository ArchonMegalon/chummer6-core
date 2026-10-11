using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Creation lifestyles admitted by the original display owner.</summary>
public interface IOwnerBoundCharacterCreationLifestylesService
{
    CharacterCreationLifestyleResult<CharacterCreationLifestylesState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationLifestylesLoadRequest request);

    CharacterCreationLifestyleResult<CharacterCreationLifestylePreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationLifestylePreviewRequest request);

    CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationLifestyleConfirmRequest request);

    CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt> LookupReceipt(
        OwnerContextStamp expectedOwner, CharacterCreationLifestyleReceiptLookupRequest request);
}
