#nullable enable annotations

using System.Linq;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiProviderCatalogTests
{
    [TestMethod]
    public void Default_provider_catalog_exposes_known_stub_provider_descriptors()
    {
        DefaultAiProviderCatalog catalog = new();

        Assert.IsNotNull(catalog.GetProvider(AiProviderIds.AiMagicx));
        Assert.IsNotNull(catalog.GetProvider(AiProviderIds.OneMinAi));
        Assert.IsTrue(catalog.ListProviders().Any(provider => provider.ProviderId == AiProviderIds.AiMagicx
            && provider.SupportsToolCalling
            && provider.AdapterRegistered
            && provider.AdapterKind == AiProviderAdapterKinds.Stub
            && !provider.LiveExecutionEnabled
            && !provider.TransportMetadataConfigured));
        Assert.IsTrue(catalog.ListProviders().Any(provider => provider.ProviderId == AiProviderIds.OneMinAi
            && !provider.SupportsToolCalling
            && provider.AdapterRegistered
            && provider.AdapterKind == AiProviderAdapterKinds.Stub
            && !provider.LiveExecutionEnabled
            && !provider.TransportMetadataConfigured
            && provider.AllowedRouteTypes.Contains(AiRouteTypes.Docs)));
    }

    [TestMethod]
    public void Default_provider_catalog_merges_remote_transport_adapters_with_remaining_stub_defaults()
    {
        DefaultAiProviderCatalog catalog = new(
        [
            new RemoteHttpAiProvider(new AiProviderTransportOptions(
                ProviderId: AiProviderIds.AiMagicx,
                BaseUrl: "https://beta.aimagicx.com/api/v1",
                DefaultModelId: "magicx-coach",
                TransportConfigured: true,
                RemoteExecutionEnabled: false))
        ]);

        AiProviderDescriptor aiMagicx = catalog.ListProviders().Single(provider => provider.ProviderId == AiProviderIds.AiMagicx);
        AiProviderDescriptor oneMinAi = catalog.ListProviders().Single(provider => provider.ProviderId == AiProviderIds.OneMinAi);

        Assert.AreEqual(AiProviderAdapterKinds.RemoteHttp, aiMagicx.AdapterKind);
        Assert.IsFalse(aiMagicx.LiveExecutionEnabled);
        Assert.IsTrue(aiMagicx.TransportBaseUrlConfigured);
        Assert.IsTrue(aiMagicx.TransportModelConfigured);
        Assert.IsTrue(aiMagicx.TransportMetadataConfigured);
        Assert.IsFalse(aiMagicx.SessionSafe);
        Assert.AreEqual(AiProviderAdapterKinds.Stub, oneMinAi.AdapterKind);
        Assert.IsFalse(oneMinAi.LiveExecutionEnabled);
        Assert.IsFalse(oneMinAi.TransportMetadataConfigured);
        Assert.IsTrue(oneMinAi.SessionSafe);
    }

    [TestMethod]
    public void Not_implemented_ai_provider_accepts_typed_turn_plan_contract()
    {
        NotImplementedAiProvider provider = new(AiProviderIds.AiMagicx);
        AiProviderRouteDecision routeDecision = new(
            RouteType: AiRouteTypes.Build,
            ProviderId: AiProviderIds.AiMagicx,
            Reason: "typed stub provider",
            BudgetUnit: AiBudgetUnits.ChummerAiUnits,
            ToolingEnabled: true);
        AiGroundingBundle grounding = new(
            RouteType: AiRouteTypes.Build,
            RuntimeFingerprint: "sha256:provider",
            CharacterId: "char-provider",
            ConversationId: "conv-provider",
            RuntimeFacts: new System.Collections.Generic.Dictionary<string, string> { ["runtimeFingerprint"] = "sha256:provider" },
            CharacterFacts: new System.Collections.Generic.Dictionary<string, string> { ["characterId"] = "char-provider" },
            Constraints: ["No mutation."],
            RetrievedItems: [],
            AllowedTools: [AiGatewayDefaults.ResolveToolDescriptor(AiToolIds.SearchBuildIdeas)]);
        AiBudgetSnapshot budget = new(
            BudgetUnit: AiBudgetUnits.ChummerAiUnits,
            MonthlyAllowance: 120,
            MonthlyConsumed: 0,
            BurstLimitPerMinute: 6);
        AiProviderTurnPlan plan = new(
            ProviderId: AiProviderIds.AiMagicx,
            RouteType: AiRouteTypes.Build,
            ConversationId: "conv-provider",
            UserMessage: "Recommend a build path.",
            SystemPrompt: "Structured Chummer data first.",
            Stream: true,
            AttachmentIds: [],
            RetrievalCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community],
            AllowedTools: [AiGatewayDefaults.ResolveToolDescriptor(AiToolIds.SearchBuildIdeas)],
            GroundingSections:
            [
                new AiGroundingSection(AiGroundingSectionIds.Runtime, "Runtime", ["runtimeFingerprint: sha256:provider"]),
                new AiGroundingSection(AiGroundingSectionIds.AllowedTools, "Allowed Tools", [AiToolIds.SearchBuildIdeas], Structured: false)
            ],
            RouteDecision: routeDecision,
            Grounding: grounding,
            Budget: budget);

        AiConversationTurnResponse response = provider.CompleteTurn(OwnerScope.LocalSingleUser, plan);

        Assert.AreEqual("conv-provider", response.ConversationId);
        Assert.AreEqual(AiProviderIds.AiMagicx, response.ProviderId);
        Assert.AreEqual(AiRouteTypes.Build, response.RouteType);
        Assert.AreEqual("sha256:provider", response.Grounding.RuntimeFingerprint);
        Assert.AreEqual(AiBudgetUnits.ChummerAiUnits, response.Budget.BudgetUnit);
        Assert.AreEqual("AI Magicx", provider.ExecutionPolicy.DisplayName);
        Assert.AreEqual(AiProviderAdapterKinds.Stub, provider.AdapterKind);
        Assert.IsFalse(provider.LiveExecutionEnabled);
        StringAssert.Contains(response.Answer, "scaffold stayed server-side");
        Assert.AreEqual("Line's clean. I'm grounding this against your current Chummer runtime.", response.FlavorLine);
        Assert.IsNotNull(response.StructuredAnswer);
        Assert.AreEqual(AiConfidenceLevels.Scaffolded, response.StructuredAnswer!.Confidence);
    }
}
