using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface IOwnerBoundCharacterCreationLifeModuleJourneyService
{
    CharacterCreationFoundationResult<CharacterCreationLifeModuleJourneyState> LoadJourney(
        OwnerContextStamp expectedOwner, CharacterCreationFoundationLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationLifeModulePreview> PreviewModule(
        OwnerContextStamp expectedOwner, CharacterCreationLifeModulePreviewRequest request);

    CharacterCreationFoundationResult<CharacterCreationLifeModuleApplyReceipt> ConfirmModule(
        OwnerContextStamp expectedOwner, CharacterCreationLifeModuleConfirmRequest request);

    CharacterCreationFoundationResult<CharacterCreationLifeModuleFinishPreview> PreviewFinishSelection(
        OwnerContextStamp expectedOwner, CharacterCreationLifeModuleFinishRequest request);

    CharacterCreationFoundationResult<CharacterCreationLifeModuleFinishReceipt> ConfirmFinishSelection(
        OwnerContextStamp expectedOwner, CharacterCreationLifeModuleFinishConfirmRequest request);
}
