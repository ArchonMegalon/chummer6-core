using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Separate capability; ordinary Skills editing never bypasses a stale draft.</summary>
public interface ICharacterCreationSkillsReReviewService
{
    CharacterCreationFoundationResult<CharacterCreationSkillsReReviewState> LoadReReview(
        CharacterCreationSkillsLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationSkillsReReviewPreview> PreviewReReview(
        CharacterCreationSkillsReReviewPreviewRequest request);

    CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> ConfirmReReview(
        CharacterCreationSkillsReReviewConfirmRequest request);
}
