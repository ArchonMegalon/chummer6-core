using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Finalization and receipt recovery admitted by the original display/review owner.
/// Retain that complete stamp across asynchronous waits; never recapture the current
/// account to reuse an old review. The stamp is not a portable authorization token.
/// </summary>
public interface IOwnerBoundCharacterCreationFinalizationService
{
    CharacterCreationFinalizationResult<CharacterCreationFinalizationState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationLoadRequest request);

    CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> Review(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationReviewRequest request);

    CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationConfirmRequest request);

    CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> LookupReceipt(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationReceiptLookupRequest request);
}
