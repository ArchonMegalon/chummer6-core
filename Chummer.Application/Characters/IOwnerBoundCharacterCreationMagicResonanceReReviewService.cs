using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface IOwnerBoundCharacterCreationMagicResonanceReReviewService
{
    CharacterCreationFoundationResult<CharacterCreationMagicResonanceReReviewState> LoadReReview(
        OwnerContextStamp expectedOwner, CharacterCreationMagicResonanceLoadRequest request);
    CharacterCreationFoundationResult<CharacterCreationMagicResonanceReReviewPreview> PreviewReReview(
        OwnerContextStamp expectedOwner, CharacterCreationMagicResonanceReReviewPreviewRequest request);
    CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> ConfirmReReview(
        OwnerContextStamp expectedOwner, CharacterCreationMagicResonanceReReviewConfirmRequest request);
}
