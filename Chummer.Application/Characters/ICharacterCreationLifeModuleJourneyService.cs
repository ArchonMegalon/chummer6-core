using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterCreationLifeModuleJourneyService
{
    CharacterCreationFoundationResult<CharacterCreationLifeModuleJourneyState> LoadJourney(
        CharacterCreationFoundationLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationLifeModulePreview> PreviewModule(
        CharacterCreationLifeModulePreviewRequest request);

    CharacterCreationFoundationResult<CharacterCreationLifeModuleApplyReceipt> ConfirmModule(
        CharacterCreationLifeModuleConfirmRequest request);
}
