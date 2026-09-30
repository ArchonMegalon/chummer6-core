using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterCreationMagicResonanceReReviewService
{
    CharacterCreationFoundationResult<CharacterCreationMagicResonanceReReviewState> LoadReReview(
        CharacterCreationMagicResonanceLoadRequest request);
    CharacterCreationFoundationResult<CharacterCreationMagicResonanceReReviewPreview> PreviewReReview(
        CharacterCreationMagicResonanceReReviewPreviewRequest request);
    CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> ConfirmReReview(
        CharacterCreationMagicResonanceReReviewConfirmRequest request);
}
