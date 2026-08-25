using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Draft-only SR5 Standard Priority Skills authority. Confirmation atomically advances
/// the auxiliary creation ledger and never writes character XML.
/// </summary>
public interface ICharacterCreationSkillsService
{
    CharacterCreationFoundationResult<CharacterCreationSkillsState> Load(
        CharacterCreationSkillsLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationSkillsPreview> Preview(
        CharacterCreationSkillsPreviewRequest request);

    CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> Confirm(
        CharacterCreationSkillsConfirmRequest request);
}
