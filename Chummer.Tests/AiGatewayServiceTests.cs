#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiGatewayServiceTests
{
    [TestMethod]
    public void Not_implemented_ai_gateway_exposes_structured_status_projection()
    {
        NotImplementedAiGatewayService service = new();

        AiApiResult<AiGatewayStatusProjection> result = service.GetStatus(OwnerScope.LocalSingleUser);

        Assert.IsTrue(result.IsImplemented);
        Assert.IsNotNull(result.Payload);
        Assert.AreEqual("scaffolded", result.Payload.Status);
        CollectionAssert.AreEquivalent(
            new[] { AiRouteTypes.Chat, AiRouteTypes.Coach, AiRouteTypes.Build, AiRouteTypes.Docs, AiRouteTypes.Recap },
            result.Payload.Routes.ToArray());
        Assert.IsTrue(result.Payload.Providers.Any(provider => provider.ProviderId == AiProviderIds.AiMagicx && provider.SupportsToolCalling));
        Assert.IsTrue(result.Payload.Providers.Any(provider => provider.ProviderId == AiProviderIds.OneMinAi && provider.SupportsAttachments));
        Assert.IsTrue(result.Payload.Providers.Any(provider => provider.ProviderId == AiProviderIds.OneMinAi && provider.SessionSafe));
        Assert.IsTrue(result.Payload.Providers.Any(provider => provider.ProviderId == AiProviderIds.AiMagicx && !provider.SessionSafe));
        Assert.IsTrue(result.Payload.Tools.Any(tool => tool.ToolId == AiToolIds.GetRuntimeSummary));
        Assert.IsTrue(result.Payload.Tools.Any(tool => tool.ToolId == AiToolIds.ExplainValue));
        Assert.IsTrue(result.Payload.Tools.Any(tool => tool.ToolId == AiToolIds.CreateApplyPreview));
        Assert.IsTrue(result.Payload.RoutePolicies.Any(policy => policy.RouteType == AiRouteTypes.Coach && policy.PrimaryProviderId == AiProviderIds.AiMagicx));
        Assert.IsTrue(result.Payload.RoutePolicies.Any(policy => policy.RouteType == AiRouteTypes.Coach
            && policy.RouteClassId == AiRouteClassIds.GroundedRulesChat
            && policy.PersonaId == AiPersonaIds.DeckerContact));
        Assert.IsTrue(result.Payload.RoutePolicies.Any(policy => policy.RouteType == AiRouteTypes.Recap
            && policy.ToolingEnabled
            && policy.AllowedTools.Any(tool => tool.ToolId == AiToolIds.DraftHistoryEntries)));
        Assert.IsTrue(result.Payload.RoutePolicies.Any(policy => policy.RouteType == AiRouteTypes.Docs && policy.PrimaryProviderId == AiProviderIds.OneMinAi));
        Assert.IsTrue(result.Payload.RoutePolicies.Any(policy => policy.RouteType == AiRouteTypes.Chat && policy.PrimaryProviderId == AiProviderIds.OneMinAi));
        Assert.IsTrue(result.Payload.RouteBudgets.Any(policy => policy.RouteType == AiRouteTypes.Coach && policy.BudgetUnit == AiBudgetUnits.ChummerAiUnits));
        Assert.IsTrue(result.Payload.RouteBudgets.Any(policy => policy.RouteType == AiRouteTypes.Docs && policy.BudgetUnit == AiBudgetUnits.ChummerAiUnits));
        Assert.AreEqual(5, result.Payload.RouteBudgetStatuses?.Count);
        Assert.IsTrue(result.Payload.RouteBudgetStatuses?.Any(status => status.RouteType == AiRouteTypes.Coach && status.MonthlyRemaining >= 0 && status.BurstRemaining >= 0) ?? false);
        Assert.IsTrue(result.Payload.RetrievalCorpora.Any(corpus => corpus.CorpusId == "runtime" && corpus.StructuredFirst));
        Assert.IsTrue(result.Payload.Personas?.Any(persona => persona.PersonaId == AiPersonaIds.DeckerContact && persona.EvidenceFirst) ?? false);
        Assert.AreEqual(AiPersonaIds.DeckerContact, result.Payload.DefaultPersonaId);
        Assert.AreEqual(AiBudgetUnits.ChummerAiUnits, result.Payload.Budget.BudgetUnit);
        Assert.IsTrue(result.Payload.Providers.All(provider => provider.AdapterRegistered));
        Assert.IsTrue(result.Payload.Providers.All(provider => provider.AdapterKind == AiProviderAdapterKinds.Stub));
        Assert.IsTrue(result.Payload.Providers.All(provider => !provider.LiveExecutionEnabled));
        Assert.IsTrue(result.Payload.Providers.All(provider => !provider.IsConfigured));
        Assert.IsTrue(result.Payload.Providers.All(provider => !provider.TransportBaseUrlConfigured));
        Assert.IsTrue(result.Payload.Providers.All(provider => !provider.TransportModelConfigured));
        Assert.IsTrue(result.Payload.Providers.All(provider => !provider.TransportMetadataConfigured));
        Assert.AreEqual(12, service.ListTools(OwnerScope.LocalSingleUser).Payload?.Count);
        Assert.AreEqual(3, service.ListRetrievalCorpora(OwnerScope.LocalSingleUser).Payload?.Count);
        Assert.AreEqual(5, service.ListRoutePolicies(OwnerScope.LocalSingleUser).Payload?.Count);
        Assert.AreEqual(5, service.ListRouteBudgets(OwnerScope.LocalSingleUser).Payload?.Count);
        Assert.AreEqual(5, service.ListRouteBudgetStatuses(OwnerScope.LocalSingleUser).Payload?.Count);
        Assert.AreEqual(2, service.ListProviderHealth(OwnerScope.LocalSingleUser).Payload?.Count);
        Assert.IsTrue(service.ListProviderHealth(OwnerScope.LocalSingleUser).Payload?.All(item => item.CircuitState == AiProviderCircuitStates.Closed) ?? false);
        Assert.IsTrue(service.ListProviderHealth(OwnerScope.LocalSingleUser).Payload?.All(item => !item.TransportMetadataConfigured) ?? false);
        Assert.IsTrue(service.ListProviderHealth(OwnerScope.LocalSingleUser).Payload?.All(item => item.PrimaryCredentialCount == 0 && item.FallbackCredentialCount == 0) ?? false);
    }

    [TestMethod]
    public void Not_implemented_ai_gateway_exposes_preview_projection_for_chummer_grounded_routes()
    {
        NotImplementedAiGatewayService service = new(new InMemoryAiProviderCredentialCatalog(
            new Dictionary<string, AiProviderCredentialCounts>(StringComparer.Ordinal)
            {
                [AiProviderIds.AiMagicx] = new(PrimaryCredentialCount: 1, FallbackCredentialCount: 0),
                [AiProviderIds.OneMinAi] = new(PrimaryCredentialCount: 1, FallbackCredentialCount: 1)
            }));

        AiConversationTurnPreview preview = service.PreviewTurn(
            OwnerScope.LocalSingleUser,
            AiRouteTypes.Coach,
            new AiConversationTurnRequest(
                Message: "What should I spend 18 Karma on?",
                ConversationId: "conv-preview",
                RuntimeFingerprint: "sha256:preview",
                CharacterId: "char-7",
                WorkspaceId: "ws-7")).Payload
            ?? throw new AssertFailedException("Expected AI preview projection.");

        Assert.AreEqual(AiRouteTypes.Coach, preview.RouteType);
        Assert.AreEqual(AiProviderIds.AiMagicx, preview.RouteDecision.ProviderId);
        StringAssert.Contains(preview.RouteDecision.Reason, "stub provider adapter registered");
        Assert.AreEqual(AiProviderCredentialTiers.Primary, preview.RouteDecision.CredentialTier);
        Assert.AreEqual(0, preview.RouteDecision.CredentialSlotIndex);
        Assert.AreEqual("sha256:preview", preview.Grounding.RuntimeFingerprint);
        Assert.AreEqual("ws-7", preview.Grounding.WorkspaceId);
        Assert.IsNotNull(preview.Grounding.Coverage);
        Assert.AreEqual(100, preview.Grounding.Coverage.ScorePercent);
        CollectionAssert.Contains(preview.Grounding.Coverage.PresentSignals.ToList(), "runtime");
        CollectionAssert.Contains(preview.Grounding.Coverage.RetrievedCorpusIds.ToList(), AiRetrievalCorpusIds.Community);
        Assert.IsTrue(preview.Grounding.AllowedTools.Any(tool => tool.ToolId == AiToolIds.ExplainValue));
        Assert.AreEqual(AiProviderIds.AiMagicx, preview.ProviderRequest.ProviderId);
        Assert.AreEqual("conv-preview", preview.ProviderRequest.ConversationId);
        Assert.AreEqual("ws-7", preview.ProviderRequest.WorkspaceId);
        CollectionAssert.Contains(preview.ProviderRequest.RetrievalCorpusIds.ToList(), "runtime");
        Assert.IsTrue(preview.ProviderRequest.AllowedTools.Any(tool => tool.ToolId == AiToolIds.SearchBuildIdeas));
        Assert.IsTrue(preview.ProviderRequest.AllowedTools.Any(tool => tool.ToolId == AiToolIds.SearchHubProjects));
        Assert.IsTrue(preview.ProviderRequest.AllowedTools.Any(tool => tool.ToolId == AiToolIds.CreateApplyPreview));
        Assert.IsTrue(preview.ProviderRequest.GroundingSections.Any(section => section.SectionId == AiGroundingSectionIds.Character));
        StringAssert.Contains(preview.SystemPrompt, "Structured Chummer data first");
        StringAssert.Contains(preview.SystemPrompt, $"route_class: {AiRouteClassIds.GroundedRulesChat}");
        StringAssert.Contains(preview.SystemPrompt, $"persona: {AiPersonaIds.DeckerContact}");
        StringAssert.Contains(preview.SystemPrompt, "characterId: char-7");
        StringAssert.Contains(preview.SystemPrompt, "workspaceId: ws-7");
    }

    [TestMethod]
    public void Not_implemented_ai_gateway_reports_configured_primary_and_fallback_provider_slots_without_exposing_keys()
    {
        NotImplementedAiGatewayService service = new(new InMemoryAiProviderCredentialCatalog(
            new Dictionary<string, AiProviderCredentialCounts>(StringComparer.Ordinal)
            {
                [AiProviderIds.AiMagicx] = new(PrimaryCredentialCount: 1, FallbackCredentialCount: 0),
                [AiProviderIds.OneMinAi] = new(PrimaryCredentialCount: 1, FallbackCredentialCount: 1)
            }));

        AiGatewayStatusProjection status = service.GetStatus(OwnerScope.LocalSingleUser).Payload
            ?? throw new AssertFailedException("Expected AI gateway status projection.");
        AiProviderDescriptor aiMagicx = status.Providers.Single(provider => provider.ProviderId == AiProviderIds.AiMagicx);
        AiProviderDescriptor oneMinAi = status.Providers.Single(provider => provider.ProviderId == AiProviderIds.OneMinAi);

        Assert.IsTrue(aiMagicx.IsConfigured);
        Assert.IsTrue(aiMagicx.AdapterRegistered);
        Assert.AreEqual(AiProviderAdapterKinds.Stub, aiMagicx.AdapterKind);
        Assert.IsFalse(aiMagicx.LiveExecutionEnabled);
        Assert.IsFalse(aiMagicx.TransportMetadataConfigured);
        Assert.IsFalse(aiMagicx.SessionSafe);
        Assert.AreEqual(1, aiMagicx.PrimaryCredentialCount);
        Assert.AreEqual(0, aiMagicx.FallbackCredentialCount);
        Assert.IsTrue(oneMinAi.IsConfigured);
        Assert.IsTrue(oneMinAi.AdapterRegistered);
        Assert.AreEqual(AiProviderAdapterKinds.Stub, oneMinAi.AdapterKind);
        Assert.IsFalse(oneMinAi.LiveExecutionEnabled);
        Assert.IsFalse(oneMinAi.TransportMetadataConfigured);
        Assert.IsTrue(oneMinAi.SessionSafe);
        Assert.AreEqual(1, oneMinAi.PrimaryCredentialCount);
        Assert.AreEqual(1, oneMinAi.FallbackCredentialCount);
    }

    [TestMethod]
    public void Not_implemented_ai_gateway_distinguishes_remote_transport_registration_from_stub_adapters()
    {
        NotImplementedAiGatewayService service = new(
            providerCredentialCatalog: new InMemoryAiProviderCredentialCatalog(
                new Dictionary<string, AiProviderCredentialCounts>(StringComparer.Ordinal)
                {
                    [AiProviderIds.AiMagicx] = new(PrimaryCredentialCount: 1, FallbackCredentialCount: 0)
                }),
            providerCatalog: new DefaultAiProviderCatalog(
            [
                new RemoteHttpAiProvider(new AiProviderTransportOptions(
                    ProviderId: AiProviderIds.AiMagicx,
                    BaseUrl: "https://beta.aimagicx.com/api/v1",
                    DefaultModelId: "magicx-coach",
                    TransportConfigured: true,
                    RemoteExecutionEnabled: false))
            ]));

        AiConversationTurnPreview preview = service.PreviewTurn(
            OwnerScope.LocalSingleUser,
            AiRouteTypes.Coach,
            new AiConversationTurnRequest("Preview remote transport wiring.")).Payload
            ?? throw new AssertFailedException("Expected AI preview projection.");
        AiProviderDescriptor provider = service.ListProviders(OwnerScope.LocalSingleUser).Payload
            ?.Single(item => item.ProviderId == AiProviderIds.AiMagicx)
            ?? throw new AssertFailedException("Expected AI provider projection.");
        AiProviderHealthProjection providerHealth = service.ListProviderHealth(OwnerScope.LocalSingleUser).Payload
            ?.Single(item => item.ProviderId == AiProviderIds.AiMagicx)
            ?? throw new AssertFailedException("Expected AI provider health projection.");

        StringAssert.Contains(preview.RouteDecision.Reason, "remote provider transport registered");
        Assert.AreEqual(AiProviderAdapterKinds.RemoteHttp, provider.AdapterKind);
        Assert.IsFalse(provider.LiveExecutionEnabled);
        Assert.IsTrue(provider.TransportBaseUrlConfigured);
        Assert.IsTrue(provider.TransportModelConfigured);
        Assert.IsTrue(provider.TransportMetadataConfigured);
        Assert.AreEqual(1, providerHealth.PrimaryCredentialCount);
        Assert.AreEqual(0, providerHealth.FallbackCredentialCount);
        Assert.IsTrue(providerHealth.TransportBaseUrlConfigured);
        Assert.IsTrue(providerHealth.TransportModelConfigured);
        Assert.IsTrue(providerHealth.TransportMetadataConfigured);
    }

    [TestMethod]
    public void Not_implemented_ai_gateway_prefers_live_enabled_fallback_provider_when_primary_is_stub_only()
    {
        NotImplementedAiGatewayService service = new(
            providerCredentialCatalog: new InMemoryAiProviderCredentialCatalog(
                new Dictionary<string, AiProviderCredentialCounts>(StringComparer.Ordinal)
                {
                    [AiProviderIds.AiMagicx] = new(PrimaryCredentialCount: 1, FallbackCredentialCount: 0),
                    [AiProviderIds.OneMinAi] = new(PrimaryCredentialCount: 1, FallbackCredentialCount: 0)
                }),
            providerCatalog: new DefaultAiProviderCatalog(
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

        AiConversationTurnPreview preview = service.PreviewTurn(
            OwnerScope.LocalSingleUser,
            AiRouteTypes.Coach,
            new AiConversationTurnRequest("Who gets the grounded fallback route?")).Payload
            ?? throw new AssertFailedException("Expected AI preview projection.");

        Assert.AreEqual(AiProviderIds.OneMinAi, preview.RouteDecision.ProviderId);
        StringAssert.Contains(preview.RouteDecision.Reason, "fallback provider live execution enabled");
        StringAssert.Contains(preview.RouteDecision.Reason, "live provider adapter registered");
    }

    [TestMethod]
    public void Not_implemented_ai_gateway_routes_turns_through_scaffolded_provider_responses()
    {
        NotImplementedAiGatewayService service = new();

        AiApiResult<AiConversationTurnResponse> result = service.SendCoachTurn(
            OwnerScope.LocalSingleUser,
            new AiConversationTurnRequest(
                Message: "What should I spend 18 Karma on?",
                ConversationId: "conv-1",
                RuntimeFingerprint: "sha256:runtime",
                CharacterId: "char-1",
                WorkspaceId: "ws-1"));

        Assert.IsTrue(result.IsImplemented);
        Assert.IsNotNull(result.Payload);
        Assert.AreEqual("conv-1", result.Payload.ConversationId);
        Assert.AreEqual(AiRouteTypes.Coach, result.Payload.RouteType);
        Assert.AreEqual(AiProviderIds.AiMagicx, result.Payload.ProviderId);
        StringAssert.Contains(result.Payload.Answer, "scaffold stayed server-side");
        Assert.AreEqual("Line's clean. I'm grounding this against your current Chummer runtime.", result.Payload.FlavorLine);
        Assert.IsNotNull(result.Payload.StructuredAnswer);
        AiStructuredAnswer structuredAnswer = result.Payload.StructuredAnswer!;
        StringAssert.Contains(structuredAnswer.Summary, "scaffold stayed server-side");
        Assert.IsNotEmpty(structuredAnswer.Recommendations);
        Assert.IsTrue(structuredAnswer.ActionDrafts.Any(draft => draft.ActionId == AiSuggestedActionIds.PreviewKarmaSpend));
        Assert.IsTrue(structuredAnswer.ActionDrafts.Any(draft => draft.ActionId == AiSuggestedActionIds.PreviewApplyPlan));
        Assert.IsTrue(structuredAnswer.ActionDrafts.Any(draft =>
            draft.ActionId == AiSuggestedActionIds.PreviewKarmaSpend
            && draft.RuntimeFingerprint == "sha256:runtime"
            && draft.CharacterId == "char-1"
            && draft.WorkspaceId == "ws-1"));
        Assert.AreEqual(AiConfidenceLevels.Scaffolded, structuredAnswer.Confidence);
        Assert.IsGreaterThanOrEqualTo(2, result.Payload.Citations.Count);
        Assert.AreEqual(AiCitationKinds.Runtime, result.Payload.Citations[0].Kind);
        Assert.IsTrue(result.Payload.SuggestedActions.Any(action => action.ActionId == AiSuggestedActionIds.PreviewKarmaSpend));
        Assert.IsTrue(result.Payload.SuggestedActions.Any(action =>
            action.ActionId == AiSuggestedActionIds.PreviewKarmaSpend
            && action.RuntimeFingerprint == "sha256:runtime"
            && action.CharacterId == "char-1"
            && action.WorkspaceId == "ws-1"));
        Assert.IsTrue(result.Payload.ToolInvocations.Any(invocation => invocation.ToolId == AiToolIds.ExplainValue));
        Assert.IsTrue(result.Payload.ToolInvocations.Any(invocation => invocation.ToolId == AiToolIds.CreateApplyPreview));
        Assert.IsNotNull(result.Payload.Grounding.Coverage);
        Assert.AreEqual(100, result.Payload.Grounding.Coverage.ScorePercent);

        AiProviderHealthProjection providerHealth = service.ListProviderHealth(OwnerScope.LocalSingleUser).Payload
            ?.Single(item => item.ProviderId == AiProviderIds.AiMagicx)
            ?? throw new AssertFailedException("Expected AI Magicx provider health projection.");
        Assert.AreEqual(AiRouteTypes.Coach, providerHealth.LastRouteType);
        Assert.AreEqual(AiProviderCredentialTiers.None, providerHealth.LastCredentialTier);
        Assert.IsNull(providerHealth.LastCredentialSlotIndex);
    }

    [TestMethod]
    public void Not_implemented_ai_gateway_records_owner_scoped_conversation_snapshots_with_assistant_responses()
    {
        InMemoryConversationStore conversationStore = new();
        NotImplementedAiGatewayService service = new(conversationStore: conversationStore);

        AiApiResult<AiConversationTurnResponse> result = service.SendCoachTurn(
            OwnerScope.LocalSingleUser,
            new AiConversationTurnRequest(
                Message: "What should I spend 18 Karma on?",
                ConversationId: "conv-store",
                RuntimeFingerprint: "sha256:runtime",
                CharacterId: "char-9",
                WorkspaceId: "ws-9"));

        AiApiResult<AiConversationCatalogPage> listedConversations = service.ListConversations(
            OwnerScope.LocalSingleUser,
            new AiConversationCatalogQuery(
                ConversationId: "conv-store",
                RouteType: AiRouteTypes.Coach,
                CharacterId: "char-9",
                RuntimeFingerprint: "sha256:runtime",
                MaxCount: 5,
                WorkspaceId: "ws-9"));
        AiApiResult<AiConversationAuditCatalogPage> listedAudits = service.ListConversationAudits(
            OwnerScope.LocalSingleUser,
            new AiConversationCatalogQuery(
                ConversationId: "conv-store",
                RouteType: AiRouteTypes.Coach,
                CharacterId: "char-9",
                RuntimeFingerprint: "sha256:runtime",
                MaxCount: 5,
                WorkspaceId: "ws-9"));
        AiApiResult<AiConversationSnapshot> storedConversation = service.GetConversation(OwnerScope.LocalSingleUser, "conv-store");

        Assert.AreEqual(1, listedConversations.Payload?.TotalCount);
        Assert.AreEqual(1, listedConversations.Payload?.Items.Count);
        Assert.AreEqual(1, listedAudits.Payload?.TotalCount);
        Assert.AreEqual(1, listedAudits.Payload?.Items.Count);
        Assert.IsTrue(storedConversation.IsImplemented);
        Assert.IsTrue(result.IsImplemented);
        Assert.AreEqual(AiRouteTypes.Coach, storedConversation.Payload?.RouteType);
        Assert.AreEqual("sha256:runtime", storedConversation.Payload?.RuntimeFingerprint);
        Assert.AreEqual("char-9", storedConversation.Payload?.CharacterId);
        Assert.AreEqual("ws-9", storedConversation.Payload?.WorkspaceId);
        Assert.AreEqual(3, storedConversation.Payload?.Messages.Count);
        Assert.AreEqual(1, storedConversation.Payload?.Turns?.Count);
        Assert.AreEqual(AiProviderIds.AiMagicx, storedConversation.Payload?.Turns?[0].ProviderId);
        Assert.AreEqual("ws-9", storedConversation.Payload?.Turns?[0].WorkspaceId);
        Assert.IsTrue(storedConversation.Payload?.Turns?[0].ToolInvocations.Any(invocation => invocation.ToolId == AiToolIds.ExplainValue));
        Assert.IsTrue(storedConversation.Payload?.Turns?[0].ToolInvocations.Any(invocation => invocation.ToolId == AiToolIds.CreateApplyPreview));
        Assert.AreEqual(AiProviderIds.AiMagicx, listedAudits.Payload?.Items[0].LastProviderId);
        Assert.IsNotNull(listedAudits.Payload?.Items[0].RouteDecision);
        Assert.IsNotNull(listedAudits.Payload?.Items[0].GroundingCoverage);
        Assert.IsFalse(string.IsNullOrWhiteSpace(listedAudits.Payload?.Items[0].FlavorLine));
        Assert.IsNotNull(listedAudits.Payload?.Items[0].Budget);
        Assert.IsNotNull(listedAudits.Payload?.Items[0].StructuredAnswer);
        StringAssert.Contains(listedAudits.Payload?.Items[0].StructuredAnswer?.Summary, "scaffold stayed server-side");
        Assert.AreEqual("ws-9", listedAudits.Payload?.Items[0].WorkspaceId);
        Assert.AreEqual(AiConversationRoles.System, storedConversation.Payload?.Messages[0].Role);
        Assert.AreEqual(AiConversationRoles.User, storedConversation.Payload?.Messages[1].Role);
        Assert.AreEqual(AiConversationRoles.Assistant, storedConversation.Payload?.Messages[2].Role);
        StringAssert.Contains(storedConversation.Payload?.Messages[2].Content, "scaffold stayed server-side");
    }

    [TestMethod]
    public void Not_implemented_ai_gateway_routes_docs_turns_through_the_v1_1_docs_route()
    {
        NotImplementedAiGatewayService service = new();

        AiApiResult<AiConversationTurnResponse> result = service.SendDocsTurn(
            OwnerScope.LocalSingleUser,
            new AiConversationTurnRequest(
                Message: "Why is this action unavailable?",
                ConversationId: "conv-docs-1",
                RuntimeFingerprint: "sha256:docs"));

        Assert.IsTrue(result.IsImplemented);
        Assert.IsNotNull(result.Payload);
        Assert.AreEqual(AiRouteTypes.Docs, result.Payload.RouteType);
        Assert.AreEqual(AiProviderIds.OneMinAi, result.Payload.ProviderId);
        Assert.AreEqual("Hold up. I'm keeping the docs line evidence-first and tied to your current Chummer context.", result.Payload.FlavorLine);
        Assert.IsNotNull(result.Payload.StructuredAnswer);
        StringAssert.Contains(result.Payload.StructuredAnswer!.Summary, "docs scaffold stayed server-side");
    }

    [TestMethod]
    public void Not_implemented_ai_gateway_rejects_turns_when_monthly_route_budget_is_exhausted()
    {
        InMemoryAiUsageLedgerStore usageLedgerStore = new();
        usageLedgerStore.RecordUsage(OwnerScope.LocalSingleUser, AiRouteTypes.Coach, 1, DateTimeOffset.UtcNow);
        NotImplementedAiGatewayService service = new(
            routeBudgetPolicyCatalog: new StaticAiRouteBudgetPolicyCatalog(
                new AiRouteBudgetPolicyDescriptor(
                    RouteType: AiRouteTypes.Coach,
                    BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                    MonthlyAllowance: 1,
                    BurstLimitPerMinute: 4,
                    Notes: "Exhausted coach lane.")),
            usageLedgerStore: usageLedgerStore);

        AiApiResult<AiConversationTurnResponse> result = service.SendCoachTurn(
            OwnerScope.LocalSingleUser,
            new AiConversationTurnRequest(
                Message: "Should fail on budget.",
                ConversationId: "conv-quota-1"));

        Assert.IsTrue(result.IsImplemented);
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Payload);
        Assert.IsNotNull(result.QuotaExceeded);
        Assert.AreEqual("ai_quota_exceeded", result.QuotaExceeded.Error);
        Assert.AreEqual(AiRouteTypes.Coach, result.QuotaExceeded.RouteType);
        Assert.AreEqual(1, result.QuotaExceeded.Budget.MonthlyConsumed);
        Assert.AreEqual(1, usageLedgerStore.GetMonthlyConsumed(OwnerScope.LocalSingleUser, AiRouteTypes.Coach, DateTimeOffset.UtcNow));
        Assert.AreEqual(0, service.ListConversations(OwnerScope.LocalSingleUser).Payload?.TotalCount);
    }

    [TestMethod]
    public void Not_implemented_ai_gateway_rejects_turns_when_burst_route_budget_is_exhausted()
    {
        InMemoryAiUsageLedgerStore usageLedgerStore = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        usageLedgerStore.RecordUsage(OwnerScope.LocalSingleUser, AiRouteTypes.Coach, 1, now.AddSeconds(-40));
        usageLedgerStore.RecordUsage(OwnerScope.LocalSingleUser, AiRouteTypes.Coach, 1, now.AddSeconds(-5));
        NotImplementedAiGatewayService service = new(
            routeBudgetPolicyCatalog: new StaticAiRouteBudgetPolicyCatalog(
                new AiRouteBudgetPolicyDescriptor(
                    RouteType: AiRouteTypes.Coach,
                    BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                    MonthlyAllowance: 10,
                    BurstLimitPerMinute: 2,
                    Notes: "Burst limited coach lane.")),
            usageLedgerStore: usageLedgerStore);

        AiApiResult<AiConversationTurnResponse> result = service.SendCoachTurn(
            OwnerScope.LocalSingleUser,
            new AiConversationTurnRequest(
                Message: "Should fail on burst budget.",
                ConversationId: "conv-burst-1"));

        Assert.IsTrue(result.IsImplemented);
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Payload);
        Assert.IsNotNull(result.QuotaExceeded);
        Assert.AreEqual(AiBudgetLimitKinds.BurstLimitPerMinute, result.QuotaExceeded.LimitKind);
        Assert.AreEqual(2, result.QuotaExceeded.Budget.CurrentBurstConsumed);
        Assert.AreEqual(2, usageLedgerStore.GetConsumedBetween(OwnerScope.LocalSingleUser, AiRouteTypes.Coach, now.AddMinutes(-1), now.AddTicks(1)));
        Assert.AreEqual(0, service.ListConversations(OwnerScope.LocalSingleUser).Payload?.TotalCount);
    }

    [TestMethod]
    public void Not_implemented_ai_gateway_reuses_owner_scoped_cached_turns_without_spending_more_budget()
    {
        InMemoryAiUsageLedgerStore usageLedgerStore = new();
        InMemoryConversationStore conversationStore = new();
        NotImplementedAiGatewayService service = new(
            usageLedgerStore: usageLedgerStore,
            responseCacheStore: new InMemoryAiResponseCacheStore(),
            conversationStore: conversationStore);

        AiConversationTurnResponse first = service.SendDocsTurn(
            OwnerScope.LocalSingleUser,
            new AiConversationTurnRequest(
                Message: "Why is  this   action unavailable?",
                ConversationId: "conv-cache-1",
                RuntimeFingerprint: "sha256:cache-runtime",
                CharacterId: "char-cache",
                WorkspaceId: "ws-cache")).Payload
            ?? throw new AssertFailedException("Expected first docs response.");
        AiConversationTurnResponse second = service.SendDocsTurn(
            OwnerScope.LocalSingleUser,
            new AiConversationTurnRequest(
                Message: "why is this action unavailable?",
                ConversationId: "conv-cache-2",
                RuntimeFingerprint: "sha256:cache-runtime",
                CharacterId: "char-cache",
                WorkspaceId: "ws-cache")).Payload
            ?? throw new AssertFailedException("Expected second docs response.");

        Assert.AreEqual(AiCacheStatuses.Miss, first.Cache?.Status);
        Assert.AreEqual(AiCacheStatuses.Hit, second.Cache?.Status);
        Assert.AreEqual(first.Cache?.CacheKey, second.Cache?.CacheKey);
        Assert.AreEqual(first.Answer, second.Answer);
        Assert.AreEqual(1, usageLedgerStore.GetMonthlyConsumed(OwnerScope.LocalSingleUser, AiRouteTypes.Docs, DateTimeOffset.UtcNow));
        Assert.AreEqual(1, first.Budget.MonthlyConsumed);
        Assert.AreEqual(1, second.Budget.MonthlyConsumed);
        Assert.AreEqual(2, conversationStore.List(OwnerScope.LocalSingleUser, new AiConversationCatalogQuery()).TotalCount);
        Assert.AreEqual(AiCacheStatuses.Hit, conversationStore.Get(OwnerScope.LocalSingleUser, "conv-cache-2")?.Turns?[0].Cache?.Status);
        Assert.AreEqual("ws-cache", conversationStore.Get(OwnerScope.LocalSingleUser, "conv-cache-2")?.Turns?[0].Cache?.WorkspaceId);
        Assert.AreEqual(AiProviderIds.OneMinAi, conversationStore.Get(OwnerScope.LocalSingleUser, "conv-cache-2")?.Turns?[0].RouteDecision?.ProviderId);
        StringAssert.Contains(conversationStore.Get(OwnerScope.LocalSingleUser, "conv-cache-2")?.Turns?[0].RouteDecision?.Reason ?? string.Empty, "stub provider adapter registered");
        Assert.IsNotNull(conversationStore.Get(OwnerScope.LocalSingleUser, "conv-cache-2")?.Turns?[0].GroundingCoverage);
    }

    [TestMethod]
    public void Not_implemented_ai_gateway_keeps_workspace_scoped_cache_entries_distinct()
    {
        InMemoryAiUsageLedgerStore usageLedgerStore = new();
        NotImplementedAiGatewayService service = new(
            usageLedgerStore: usageLedgerStore,
            responseCacheStore: new InMemoryAiResponseCacheStore(),
            conversationStore: new InMemoryConversationStore());

        AiConversationTurnResponse first = service.SendDocsTurn(
            OwnerScope.LocalSingleUser,
            new AiConversationTurnRequest(
                Message: "Why is this action unavailable?",
                ConversationId: "conv-cache-ws-1",
                RuntimeFingerprint: "sha256:cache-runtime",
                CharacterId: "char-cache",
                WorkspaceId: "ws-cache-1")).Payload
            ?? throw new AssertFailedException("Expected first docs response.");
        AiConversationTurnResponse second = service.SendDocsTurn(
            OwnerScope.LocalSingleUser,
            new AiConversationTurnRequest(
                Message: "Why is this action unavailable?",
                ConversationId: "conv-cache-ws-2",
                RuntimeFingerprint: "sha256:cache-runtime",
                CharacterId: "char-cache",
                WorkspaceId: "ws-cache-2")).Payload
            ?? throw new AssertFailedException("Expected second docs response.");

        Assert.AreEqual(AiCacheStatuses.Miss, first.Cache?.Status);
        Assert.AreEqual(AiCacheStatuses.Miss, second.Cache?.Status);
        Assert.AreNotEqual(first.Cache?.CacheKey, second.Cache?.CacheKey);
        Assert.AreEqual(2, usageLedgerStore.GetMonthlyConsumed(OwnerScope.LocalSingleUser, AiRouteTypes.Docs, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void Not_implemented_ai_gateway_tracks_provider_failures_and_reroutes_when_circuit_opens()
    {
        InMemoryAiProviderHealthStore providerHealthStore = new();
        NotImplementedAiGatewayService service = new(
            providerCatalog: new DefaultAiProviderCatalog(
            [
                new ThrowingAiProvider(AiProviderIds.AiMagicx)
            ]),
            providerHealthStore: providerHealthStore,
            responseCacheStore: new InMemoryAiResponseCacheStore());

        for (int index = 0; index < 3; index += 1)
        {
            try
            {
                _ = service.SendCoachTurn(
                    OwnerScope.LocalSingleUser,
                    new AiConversationTurnRequest(
                        Message: $"Trigger provider failure {index}",
                        ConversationId: $"conv-provider-failure-{index}",
                        RuntimeFingerprint: "sha256:provider-health"));
                Assert.Fail("Expected the throwing AI provider to raise an exception.");
            }
            catch (InvalidOperationException)
            {
            }
        }

        AiProviderHealthProjection aiMagicxHealth = service.ListProviderHealth(OwnerScope.LocalSingleUser).Payload
            ?.Single(item => item.ProviderId == AiProviderIds.AiMagicx)
            ?? throw new AssertFailedException("Expected AI Magicx provider health projection.");
        Assert.AreEqual(AiProviderCircuitStates.Open, aiMagicxHealth.CircuitState);
        Assert.AreEqual(3, aiMagicxHealth.ConsecutiveFailureCount);
        Assert.IsFalse(aiMagicxHealth.IsRoutable);

        AiConversationTurnPreview reroutedPreview = service.PreviewTurn(
            OwnerScope.LocalSingleUser,
            AiRouteTypes.Coach,
            new AiConversationTurnRequest(
                Message: "Use the rerouted provider",
                ConversationId: "conv-rerouted",
                RuntimeFingerprint: "sha256:provider-health")).Payload
            ?? throw new AssertFailedException("Expected rerouted preview payload.");

        Assert.AreEqual(AiProviderIds.OneMinAi, reroutedPreview.RouteDecision.ProviderId);
        StringAssert.Contains(reroutedPreview.RouteDecision.Reason, "circuit is open");
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
                    FallbackCredentials: Enumerable.Range(0, pair.Value.FallbackCredentialCount).Select(index => $"fallback-{pair.Key}-{index}").ToArray()),
                StringComparer.Ordinal);
    }

    private sealed class StaticAiRouteBudgetPolicyCatalog(params AiRouteBudgetPolicyDescriptor[] overriddenPolicies) : IAiRouteBudgetPolicyCatalog
    {
        private readonly IReadOnlyList<AiRouteBudgetPolicyDescriptor> _policies = AiGatewayDefaults.CreateRouteBudgets()
            .Select(policy => overriddenPolicies.FirstOrDefault(overridePolicy => string.Equals(overridePolicy.RouteType, policy.RouteType, StringComparison.Ordinal)) ?? policy)
            .ToArray();

        public IReadOnlyList<AiRouteBudgetPolicyDescriptor> ListPolicies()
            => _policies;

        public AiRouteBudgetPolicyDescriptor GetPolicy(string routeType)
            => _policies.Single(policy => string.Equals(policy.RouteType, routeType, StringComparison.Ordinal));
    }

    private sealed class ThrowingAiProvider(string providerId) : IAiProvider
    {
        public string ProviderId { get; } = providerId;

        public AiProviderExecutionPolicy ExecutionPolicy { get; } = AiProviderExecutionPolicies.Resolve(providerId);

        public string AdapterKind => AiProviderAdapterKinds.RemoteHttp;

        public bool LiveExecutionEnabled => true;

        public AiConversationTurnResponse CompleteTurn(OwnerScope owner, AiProviderTurnPlan plan)
            => throw new InvalidOperationException($"Provider {providerId} failed.");
    }
}
