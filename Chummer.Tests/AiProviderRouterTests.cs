#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiProviderRouterTests
{
    [TestMethod]
    public void Router_prefers_primary_provider_when_its_credentials_are_configured()
    {
        DefaultAiProviderRouter router = new(new InMemoryAiProviderCredentialCatalog(
            new Dictionary<string, AiProviderCredentialCounts>
            {
                [AiProviderIds.AiMagicx] = new(PrimaryCredentialCount: 1, FallbackCredentialCount: 0),
                [AiProviderIds.OneMinAi] = new(PrimaryCredentialCount: 1, FallbackCredentialCount: 1)
            }));

        AiProviderRouteDecision decision = router.RouteTurn(
            OwnerScope.LocalSingleUser,
            AiRouteTypes.Coach,
            new AiConversationTurnRequest("What should I spend 18 Karma on next?"));

        Assert.AreEqual(AiProviderIds.AiMagicx, decision.ProviderId);
        Assert.AreEqual(AiProviderCredentialTiers.Primary, decision.CredentialTier);
        Assert.IsTrue(decision.ToolingEnabled);
        Assert.IsTrue(decision.RetrievalEnabled);
        StringAssert.Contains(decision.Reason, "without live execution");
    }

    [TestMethod]
    public void Router_falls_back_when_primary_provider_is_not_configured()
    {
        DefaultAiProviderRouter router = new(new InMemoryAiProviderCredentialCatalog(
            new Dictionary<string, AiProviderCredentialCounts>
            {
                [AiProviderIds.AiMagicx] = new(PrimaryCredentialCount: 0, FallbackCredentialCount: 0),
                [AiProviderIds.OneMinAi] = new(PrimaryCredentialCount: 1, FallbackCredentialCount: 1)
            }));

        AiProviderRouteDecision decision = router.RouteTurn(
            OwnerScope.LocalSingleUser,
            AiRouteTypes.Coach,
            new AiConversationTurnRequest("Summarize the best next upgrades."));

        Assert.AreEqual(AiProviderIds.OneMinAi, decision.ProviderId);
        Assert.AreEqual(AiProviderCredentialTiers.Primary, decision.CredentialTier);
        StringAssert.Contains(decision.Reason, "fallback");
    }

    [TestMethod]
    public void Router_marks_fallback_credential_tier_when_selected_provider_only_has_fallback_keys()
    {
        DefaultAiProviderRouter router = new(new InMemoryAiProviderCredentialCatalog(
            new Dictionary<string, AiProviderCredentialCounts>
            {
                [AiProviderIds.AiMagicx] = new(PrimaryCredentialCount: 0, FallbackCredentialCount: 1),
                [AiProviderIds.OneMinAi] = new(PrimaryCredentialCount: 0, FallbackCredentialCount: 0)
            }));

        AiProviderRouteDecision decision = router.RouteTurn(
            OwnerScope.LocalSingleUser,
            AiRouteTypes.Coach,
            new AiConversationTurnRequest("Summarize the best next upgrades."));

        Assert.AreEqual(AiProviderIds.AiMagicx, decision.ProviderId);
        Assert.AreEqual(AiProviderCredentialTiers.Fallback, decision.CredentialTier);
    }

    [TestMethod]
    public void Router_prefers_live_enabled_fallback_provider_when_primary_is_stub_only()
    {
        DefaultAiProviderRouter router = new(
            new InMemoryAiProviderCredentialCatalog(
                new Dictionary<string, AiProviderCredentialCounts>
                {
                    [AiProviderIds.AiMagicx] = new(PrimaryCredentialCount: 1, FallbackCredentialCount: 0),
                    [AiProviderIds.OneMinAi] = new(PrimaryCredentialCount: 1, FallbackCredentialCount: 0)
                }),
            new DefaultAiProviderCatalog(
            [
                new RemoteHttpAiProvider(new AiProviderTransportOptions(
                    ProviderId: AiProviderIds.AiMagicx,
                    BaseUrl: "https://beta.aimagicx.com/api/v1",
                    DefaultModelId: "magicx-coach",
                    TransportConfigured: true,
                    RemoteExecutionEnabled: false)),
                new RemoteHttpAiProvider(new AiProviderTransportOptions(
                    ProviderId: AiProviderIds.OneMinAi,
                    BaseUrl: "https://api.1min.ai",
                    DefaultModelId: "1min-coach",
                    TransportConfigured: true,
                    RemoteExecutionEnabled: true))
            ]));

        AiProviderRouteDecision decision = router.RouteTurn(
            OwnerScope.LocalSingleUser,
            AiRouteTypes.Coach,
            new AiConversationTurnRequest("Recommend a grounded fallback provider."));

        Assert.AreEqual(AiProviderIds.OneMinAi, decision.ProviderId);
        Assert.AreEqual(AiProviderCredentialTiers.Primary, decision.CredentialTier);
        StringAssert.Contains(decision.Reason, "fallback provider live execution enabled");
    }

    [TestMethod]
    public void Budget_service_returns_route_specific_chummer_ai_unit_snapshot()
    {
        DefaultAiBudgetService budgetService = new();

        AiBudgetSnapshot budget = budgetService.GetBudget(OwnerScope.LocalSingleUser, AiRouteTypes.Build);

        Assert.AreEqual(AiBudgetUnits.ChummerAiUnits, budget.BudgetUnit);
        Assert.AreEqual(120, budget.MonthlyAllowance);
        Assert.AreEqual(6, budget.BurstLimitPerMinute);
    }

    private sealed class InMemoryAiProviderCredentialCatalog(
        IReadOnlyDictionary<string, AiProviderCredentialCounts> providerCredentials)
        : IAiProviderCredentialCatalog
    {
        public IReadOnlyDictionary<string, AiProviderCredentialCounts> GetConfiguredCredentialCounts()
            => providerCredentials;

        public IReadOnlyDictionary<string, AiProviderCredentialSet> GetConfiguredCredentialSets()
            => providerCredentials.ToDictionary(
                static pair => pair.Key,
                static pair => new AiProviderCredentialSet(
                    PrimaryCredentials: Enumerable.Range(0, pair.Value.PrimaryCredentialCount).Select(index => $"primary-{pair.Key}-{index}").ToArray(),
                    FallbackCredentials: Enumerable.Range(0, pair.Value.FallbackCredentialCount).Select(index => $"fallback-{pair.Key}-{index}").ToArray()));
    }
}
