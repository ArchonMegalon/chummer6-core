using Chummer.Application.Hub;
using Chummer.Contracts.AI;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;

namespace Chummer.Application.AI;

public sealed class DefaultAiHubProjectSearchService : IAiHubProjectSearchService
{
    private readonly IHubCatalogService _hubCatalogService;

    public DefaultAiHubProjectSearchService(IHubCatalogService hubCatalogService)
    {
        _hubCatalogService = hubCatalogService;
    }

    public AiHubProjectCatalog SearchProjects(OwnerScope owner, AiHubProjectSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        HubCatalogResultPage page = _hubCatalogService.Search(owner, new BrowseQuery(
            QueryText: query.QueryText ?? string.Empty,
            FacetSelections: BuildFacetSelections(query),
            SortId: HubCatalogSortIds.Title,
            SortDirection: BrowseSortDirections.Ascending,
            Offset: 0,
            Limit: Math.Clamp(query.MaxCount, 1, 25)));

        return new AiHubProjectCatalog(
            page.Items.Select(MapSummary).ToArray(),
            page.TotalCount);
    }

    public AiHubProjectDetailProjection? GetProjectDetail(OwnerScope owner, string kind, string itemId, string? rulesetId = null)
    {
        HubProjectDetailProjection? detail = _hubCatalogService.GetProjectDetail(owner, kind, itemId, rulesetId);
        if (detail is null)
        {
            return null;
        }

        return new AiHubProjectDetailProjection(
            Summary: MapSummary(detail.Summary),
            RuntimeFingerprint: detail.RuntimeFingerprint,
            Facts: detail.Facts
                .Select(static fact => new AiHubProjectFact(fact.Label, fact.Value))
                .ToArray(),
            Dependencies: detail.Dependencies
                .Select(static dependency => new AiHubProjectDependencyProjection(
                    dependency.Kind,
                    dependency.ItemKind,
                    dependency.ItemId,
                    dependency.Version,
                    dependency.Notes))
                .ToArray(),
            Actions: detail.Actions
                .Select(static action => new AiHubProjectActionProjection(
                    action.ActionId,
                    action.Label,
                    action.Kind,
                    action.Enabled,
                    action.DisabledReasonKey,
                    action.DisabledReasonParameters,
                    action.DisabledReason))
                .ToArray());
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildFacetSelections(AiHubProjectSearchQuery query)
    {
        Dictionary<string, IReadOnlyList<string>> facets = new(StringComparer.Ordinal);
        string? normalizedKind = HubCatalogItemKinds.NormalizeOptional(query.Type);
        if (normalizedKind is not null)
        {
            facets[HubCatalogFacetIds.Kind] = [normalizedKind];
        }

        string? normalizedRulesetId = string.IsNullOrWhiteSpace(query.RulesetId)
            ? null
            : query.RulesetId.Trim().ToLowerInvariant();
        if (normalizedRulesetId is not null)
        {
            facets[HubCatalogFacetIds.Ruleset] = [normalizedRulesetId];
        }

        return facets;
    }

    private static AiHubProjectProjection MapSummary(HubCatalogItem item)
        => new(
            ProjectId: item.ItemId,
            Kind: item.Kind,
            Title: item.Title,
            Description: item.Description,
            RulesetId: item.RulesetId,
            Visibility: item.Visibility,
            TrustTier: item.TrustTier,
            Version: item.Version,
            Installable: item.Installable,
            InstallState: item.InstallState,
            Publisher: item.Publisher?.DisplayName);
}
