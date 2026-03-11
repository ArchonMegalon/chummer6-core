using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Hub;

public interface IHubReviewService
{
    HubPublicationResult<HubReviewCatalog> ListReviews(OwnerScope owner, string? kind = null, string? itemId = null, string? rulesetId = null);

    HubPublicationResult<HubReviewAggregateSummary> GetAggregateSummary(string kind, string itemId, string? rulesetId = null);

    HubPublicationResult<HubReviewReceipt> UpsertReview(OwnerScope owner, string kind, string itemId, HubUpsertReviewRequest? request);
}
