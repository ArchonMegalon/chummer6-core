using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;

namespace Chummer.Application.Hub;

public interface IHubCatalogService
{
    HubCatalogResultPage Search(OwnerScope owner, BrowseQuery query);

    HubProjectDetailProjection? GetProjectDetail(OwnerScope owner, string kind, string itemId, string? rulesetId = null);
}
