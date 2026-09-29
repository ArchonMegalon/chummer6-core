using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Magic/Resonance drafts admitted by the owner of the original display.</summary>
public interface IOwnerBoundCharacterCreationMagicResonanceService
{
    CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationMagicResonanceLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationMagicResonancePreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationMagicResonancePreviewRequest request);

    CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationMagicResonanceConfirmRequest request);
}
