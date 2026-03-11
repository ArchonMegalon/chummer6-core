using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Hub;

public interface IHubReviewStore
{
    IReadOnlyList<HubReviewRecord> List(OwnerScope owner, string? kind = null, string? itemId = null, string? rulesetId = null);

    IReadOnlyList<HubReviewRecord> ListAll(string? kind = null, string? itemId = null, string? rulesetId = null);

    HubReviewRecord? Get(OwnerScope owner, string kind, string itemId, string rulesetId);

    HubReviewRecord Upsert(OwnerScope owner, HubReviewRecord record);
}
