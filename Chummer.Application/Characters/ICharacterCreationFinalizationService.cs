using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Sole typed boundary that may turn a complete SR5 creation draft graph into
/// one checkpointed Career document.  No individual wizard lane can write the
/// canonical character payload.
/// </summary>
public interface ICharacterCreationFinalizationService
{
    CharacterCreationFinalizationResult<CharacterCreationFinalizationState> Load(
        CharacterCreationFinalizationLoadRequest request);

    CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> Review(
        CharacterCreationFinalizationReviewRequest request);

    CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> Confirm(
        CharacterCreationFinalizationConfirmRequest request);

    CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> LookupReceipt(
        CharacterCreationFinalizationReceiptLookupRequest request);
}
