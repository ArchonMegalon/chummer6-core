using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiHubProjectSearchService
{
    AiHubProjectCatalog SearchProjects(OwnerScope owner, AiHubProjectSearchQuery query);

    AiHubProjectDetailProjection? GetProjectDetail(OwnerScope owner, string kind, string itemId, string? rulesetId = null);
}
