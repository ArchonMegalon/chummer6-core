using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Explicit historical Skills review in the original display owner's
/// partition. Every operation requires that exact owner lifetime.</summary>
public interface IOwnerBoundCharacterCreationSkillsReReviewService
{
    CharacterCreationFoundationResult<CharacterCreationSkillsReReviewState> LoadReReview(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationSkillsReReviewPreview> PreviewReReview(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsReReviewPreviewRequest request);

    CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> ConfirmReReview(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsReReviewConfirmRequest request);
}
