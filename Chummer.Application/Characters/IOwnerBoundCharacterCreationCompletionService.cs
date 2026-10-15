using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface IOwnerBoundCharacterCreationCompletionService
{
    CharacterCreationFoundationResult<CharacterCreationCompletionApplyReview> Review(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CharacterCreationStartingCashChoice startingCash,
        CancellationToken cancellationToken = default);

    CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> Apply(
        OwnerContextStamp expectedOwner, CharacterCreationCompletionApplyRequest request,
        CancellationToken cancellationToken = default);
}
