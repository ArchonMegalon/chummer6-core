using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Draft-only SR5 Standard Priority Magic/Resonance authority. Confirmation advances
/// the auxiliary creation ledger atomically and never mutates character XML.
/// </summary>
public interface ICharacterCreationMagicResonanceService
{
    CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> Load(
        CharacterCreationMagicResonanceLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationMagicResonancePreview> Preview(
        CharacterCreationMagicResonancePreviewRequest request);

    CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> Confirm(
        CharacterCreationMagicResonanceConfirmRequest request);
}
