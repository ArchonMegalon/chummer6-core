using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface IOwnerBoundCharacterCreationCompletionService
{
    /// <summary>Reads current source terms for the proposed starting lifestyle.
    /// It does not roll dice, save a purchase, or authorize completion.</summary>
    CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource> LoadStartingCash(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(CharacterCreationFoundationOutcomes.Blocked, null,
            [CharacterCreationFinalizationBlockers.StartingCashUnavailable]);
    }

    CharacterCreationFoundationResult<CharacterCreationCompletionApplyReview> Review(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CharacterCreationStartingCashChoice startingCash,
        CancellationToken cancellationToken = default);

    CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> Apply(
        OwnerContextStamp expectedOwner, CharacterCreationCompletionApplyRequest request,
        CancellationToken cancellationToken = default);
}
