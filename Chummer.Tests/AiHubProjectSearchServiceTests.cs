#nullable enable annotations

using System;
using System.Collections.Generic;
using Chummer.Application.AI;
using Chummer.Application.Hub;
using Chummer.Contracts.AI;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiHubProjectSearchServiceTests
{
    [TestMethod]
    public void Default_ai_hub_project_search_service_projects_catalog_and_detail_results()
    {
        DefaultAiHubProjectSearchService service = new(new StubHubCatalogService());

        AiHubProjectCatalog catalog = service.SearchProjects(
            OwnerScope.LocalSingleUser,
            new AiHubProjectSearchQuery(QueryText: "street", Type: HubCatalogItemKinds.RuleProfile, RulesetId: "sr5", MaxCount: 5));
        AiHubProjectDetailProjection? detail = service.GetProjectDetail(
            OwnerScope.LocalSingleUser,
            HubCatalogItemKinds.RuleProfile,
            "official.sr5.core",
            "sr5");

        Assert.AreEqual(1, catalog.TotalCount);
        Assert.HasCount(1, catalog.Items);
        Assert.AreEqual("official.sr5.core", catalog.Items[0].ProjectId);
        Assert.AreEqual("Official", catalog.Items[0].Publisher);
        Assert.IsNotNull(detail);
        Assert.AreEqual("official.sr5.core", detail.Summary.ProjectId);
        Assert.AreEqual("sha256:coach", detail.RuntimeFingerprint);
        Assert.HasCount(1, detail.Facts);
        Assert.HasCount(1, detail.Dependencies);
        Assert.HasCount(1, detail.Actions);
    }

    private sealed class StubHubCatalogService : IHubCatalogService
    {
        public HubCatalogResultPage Search(OwnerScope owner, BrowseQuery query)
            => new(
                Query: query,
                Items:
                [
                    new HubCatalogItem(
                        ItemId: "official.sr5.core",
                        Kind: HubCatalogItemKinds.RuleProfile,
                        Title: "Official SR5 Core",
                        Description: "Baseline SR5 runtime profile.",
                        RulesetId: "sr5",
                        Visibility: "public",
                        TrustTier: "official",
                        LinkTarget: "/hub/projects/official.sr5.core",
                        Version: "1.0.0",
                        Publisher: new HubPublisherSummary(
                            "publisher-1",
                            "Official",
                            "official",
                            HubPublisherVerificationStates.Official,
                            "/hub/publishers/official"))
                ],
                Facets: [],
                Sorts: [],
                TotalCount: 1);

        public HubProjectDetailProjection? GetProjectDetail(OwnerScope owner, string kind, string itemId, string? rulesetId = null)
            => string.Equals(itemId, "official.sr5.core", StringComparison.Ordinal)
                ? new HubProjectDetailProjection(
                    Summary: new HubCatalogItem(
                        ItemId: "official.sr5.core",
                        Kind: HubCatalogItemKinds.RuleProfile,
                        Title: "Official SR5 Core",
                        Description: "Baseline SR5 runtime profile.",
                        RulesetId: "sr5",
                        Visibility: "public",
                        TrustTier: "official",
                        LinkTarget: "/hub/projects/official.sr5.core",
                        Version: "1.0.0",
                        Publisher: new HubPublisherSummary(
                            "publisher-1",
                            "Official",
                            "official",
                            HubPublisherVerificationStates.Official,
                            "/hub/publishers/official")),
                    OwnerId: OwnerScope.LocalSingleUser.NormalizedValue,
                    CatalogKind: "profile",
                    PublicationStatus: "published",
                    ReviewState: "approved",
                    RuntimeFingerprint: "sha256:coach",
                    OwnerReview: null,
                    AggregateReview: null,
                    Facts:
                    [
                        new HubProjectDetailFact("ruleset", "Ruleset", "SR5")
                    ],
                    Dependencies:
                    [
                        new HubProjectDependency(HubProjectDependencyKinds.IncludesRulePack, HubCatalogItemKinds.RulePack, "official.sr5.core.pack", "1.0.0")
                    ],
                    Actions:
                    [
                        new HubProjectAction("install", "Install", HubProjectActionKinds.Install)
                    ],
                    Publisher: new HubPublisherSummary(
                        "publisher-1",
                        "Official",
                        "official",
                        HubPublisherVerificationStates.Official,
                        "/hub/publishers/official"))
                : null;
    }
}
