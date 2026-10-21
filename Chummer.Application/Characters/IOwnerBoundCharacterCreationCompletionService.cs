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

    /// <summary>Reviews legal proposed Creation choices without dice, carryover or
    /// finalization projection. Success is read-only; it does not authorize a save.</summary>
    CharacterCreationFoundationResult<CharacterCreationCompletionDraftReview> ReviewDraft(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(CharacterCreationFoundationOutcomes.Blocked, null,
            [CharacterCreationCompletionReviewSchemas.AuthorityUnavailable]);
    }

    CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> ApplyDraft(
        OwnerContextStamp expectedOwner, CharacterCreationCompletionDraftApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
    }

    /// <summary>Independent draft review under live Review authority; no cash or finalization.</summary>
    CharacterCreationFoundationResult<CharacterCreationCompletionDraftReview> ReviewDraftWithCampaign(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CharacterCreationCampaignExecutionContext campaign,
        CancellationToken cancellationToken = default)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationCampaignExecutionContext.Blocker]);

    /// <summary>Exact committed recovery precedes live Apply admission for a new draft.</summary>
    CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> ApplyDraftWithCampaign(
        OwnerContextStamp expectedOwner, CharacterCreationCompletionDraftApplyRequest request,
        CharacterCreationCampaignExecutionContext campaign, CancellationToken cancellationToken = default)
        => CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationCampaignExecutionContext.Blocker);

    CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> LookupDraft(
        OwnerContextStamp expectedOwner, CharacterCreationCompletionDraftReceiptLookup request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
    }

    CharacterCreationFoundationResult<CharacterCreationCompletionApplyReview> Review(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CharacterCreationStartingCashChoice startingCash,
        CancellationToken cancellationToken = default);

    CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> Apply(
        OwnerContextStamp expectedOwner, CharacterCreationCompletionApplyRequest request,
        CancellationToken cancellationToken = default);
    CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource> LoadStartingCashWithCampaign(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CharacterCreationCampaignExecutionContext campaign,
        CancellationToken cancellationToken = default)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationCampaignExecutionContext.Blocker]);

    CharacterCreationFoundationResult<CharacterCreationCompletionApplyReview> ReviewWithCampaign(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CharacterCreationStartingCashChoice startingCash,
        CharacterCreationCampaignExecutionContext campaign, CancellationToken cancellationToken = default)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationCampaignExecutionContext.Blocker]);

    CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> ApplyWithCampaign(
        OwnerContextStamp expectedOwner, CharacterCreationCompletionApplyRequest request,
        CharacterCreationCampaignExecutionContext campaign, CancellationToken cancellationToken = default)
        => CharacterCreationCompletionTransaction.Blocked(CharacterCreationCampaignExecutionContext.Blocker);
}
