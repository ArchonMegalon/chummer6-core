using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Typed SR5 creation Gear basket boundary. Confirm persists only an authority-bound
/// finalization contribution; the canonical character XML remains byte-identical.
/// </summary>
public interface ICharacterCreationGearService
{
    CharacterCreationGearResult<CharacterCreationGearState> Load(
        CharacterCreationGearLoadRequest request);

    CharacterCreationGearResult<CharacterCreationGearPreview> Preview(
        CharacterCreationGearPreviewRequest request);

    CharacterCreationGearResult<CharacterCreationGearReceipt> Confirm(
        CharacterCreationGearConfirmRequest request);

    CharacterCreationGearResult<CharacterCreationGearReceipt> LookupReceipt(
        CharacterCreationGearReceiptLookupRequest request);
}
