using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterCreationQualitiesReReviewService
{
    CharacterCreationFoundationResult<CharacterCreationQualitiesReReviewState> LoadReReview(
        CharacterCreationQualitiesLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> ConfirmReReview(
        CharacterCreationQualitiesReReviewConfirmRequest request);
}

public interface IOwnerBoundCharacterCreationQualitiesReReviewService
{
    CharacterCreationFoundationResult<CharacterCreationQualitiesReReviewState> LoadReReview(
        OwnerContextStamp expectedOwner, CharacterCreationQualitiesLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> ConfirmReReview(
        OwnerContextStamp expectedOwner, CharacterCreationQualitiesReReviewConfirmRequest request);
}
