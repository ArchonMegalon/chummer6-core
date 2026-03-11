#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiPreviewPipelineTests
{
    [TestMethod]
    public void Retrieval_service_builds_chummer_grounded_bundle_from_route_policy()
    {
        DefaultRetrievalService retrievalService = new(
            aiDigestService: new StubAiDigestService(
                runtimeSummary: new AiRuntimeSummaryProjection(
                    RuntimeFingerprint: "sha256:runtime",
                    RulesetId: "sr5",
                    Title: "Street-Level Runtime",
                    CatalogKind: "saved",
                    EngineApiVersion: "1.0.0",
                    ContentBundles: ["official.sr5.core@1.0.0"],
                    RulePacks: ["campaign.street-level@2.0.0"],
                    ProviderBindings: new Dictionary<string, string> { ["availability.item"] = "official.sr5.core:availability.item" }),
                characterDigest: new AiCharacterDigestProjection(
                    CharacterId: "char-1",
                    DisplayName: "Cipher (Ghostwire)",
                    RulesetId: "sr5",
                    RuntimeFingerprint: "sha256:runtime",
                    Summary: new CharacterFileSummary(
                        Name: "Cipher",
                        Alias: "Ghostwire",
                        Metatype: "Human",
                        BuildMethod: "Priority",
                        CreatedVersion: "5",
                        AppVersion: "10",
                        Karma: 18m,
                        Nuyen: 1500m,
                        Created: true),
                    LastUpdatedUtc: new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero),
                    HasSavedWorkspace: true),
                sessionDigest: new AiSessionDigestProjection(
                    CharacterId: "char-1",
                    DisplayName: "Cipher (Ghostwire)",
                    RulesetId: "sr5",
                    RuntimeFingerprint: "sha256:runtime",
                    SelectionState: SessionRuntimeSelectionStates.Selected,
                    SessionReady: true,
                    BundleFreshness: SessionRuntimeBundleFreshnessStates.Current,
                    RequiresBundleRefresh: false,
                    ProfileId: "official.sr5.core",
                    ProfileTitle: "Official SR5 Core")));

        AiGroundingBundle grounding = retrievalService.BuildGroundingBundle(
            OwnerScope.LocalSingleUser,
            AiRouteTypes.Coach,
            new AiConversationTurnRequest(
                Message: "Recommend a next upgrade.",
                RuntimeFingerprint: "sha256:runtime",
                CharacterId: "char-1",
                WorkspaceId: "ws-1"));

        Assert.AreEqual(AiRouteTypes.Coach, grounding.RouteType);
        Assert.AreEqual("sha256:runtime", grounding.RuntimeFingerprint);
        Assert.AreEqual("char-1", grounding.CharacterId);
        Assert.AreEqual("ws-1", grounding.WorkspaceId);
        Assert.AreEqual("Street-Level Runtime", grounding.RuntimeFacts["runtimeTitle"]);
        Assert.AreEqual("Cipher (Ghostwire)", grounding.CharacterFacts["displayName"]);
        Assert.AreEqual("ws-1", grounding.CharacterFacts["workspaceId"]);
        Assert.AreEqual(SessionRuntimeSelectionStates.Selected, grounding.CharacterFacts["sessionSelectionState"]);
        Assert.IsTrue(grounding.RetrievedItems.Any(item => item.CorpusId == AiRetrievalCorpusIds.Runtime && item.ItemId == "sha256:runtime"));
        Assert.IsTrue(grounding.RetrievedItems.Any(item => item.CorpusId == AiRetrievalCorpusIds.Private && item.Provenance == "character-digest"));
        Assert.IsTrue(grounding.RetrievedItems.Any(item => item.CorpusId == AiRetrievalCorpusIds.Private && item.Provenance == "session-digest"));
        Assert.IsTrue(grounding.RetrievedItems.Any(item => item.CorpusId == AiRetrievalCorpusIds.Community && item.Provenance == "build-idea-card"));
        Assert.IsTrue(grounding.AllowedTools.Any(tool => tool.ToolId == AiToolIds.SearchBuildIdeas));
        Assert.IsTrue(grounding.AllowedTools.Any(tool => tool.ToolId == AiToolIds.SearchHubProjects));
        Assert.IsTrue(grounding.AllowedTools.Any(tool => tool.ToolId == AiToolIds.SimulateNuyenSpend));
    }

    [TestMethod]
    public void Prompt_assembler_renders_runtime_character_and_constraint_sections()
    {
        DefaultPromptAssembler promptAssembler = new();
        AiGroundingBundle grounding = new(
            RouteType: AiRouteTypes.Chat,
            RuntimeFingerprint: "sha256:test",
            CharacterId: "char-2",
            ConversationId: "conv-2",
            WorkspaceId: "ws-2",
            RuntimeFacts: new Dictionary<string, string> { ["runtimeFingerprint"] = "sha256:test" },
            CharacterFacts: new Dictionary<string, string> { ["characterId"] = "char-2", ["workspaceId"] = "ws-2" },
            Constraints: ["Constraint A", "Constraint B"],
            RetrievedItems: [],
            AllowedTools: [AiGatewayDefaults.ResolveToolDescriptor(AiToolIds.GetCharacterSummary)]);
        AiProviderRouteDecision routeDecision = new(
            RouteType: AiRouteTypes.Chat,
            ProviderId: AiProviderIds.OneMinAi,
            Reason: "primary provider configured",
            BudgetUnit: AiBudgetUnits.ChummerAiUnits);

        string prompt = promptAssembler.AssembleSystemPrompt(AiRouteTypes.Chat, grounding, routeDecision);

        StringAssert.Contains(prompt, "runtime:");
        StringAssert.Contains(prompt, "character:");
        StringAssert.Contains(prompt, "constraints:");
        StringAssert.Contains(prompt, "retrieved_items:");
        StringAssert.Contains(prompt, "allowed_tools:");
        StringAssert.Contains(prompt, "persona:");
        StringAssert.Contains(prompt, "persona_rules:");
        StringAssert.Contains(prompt, "characterId: char-2");
        StringAssert.Contains(prompt, "workspaceId: ws-2");
    }

    [TestMethod]
    public void Prompt_assembler_builds_typed_provider_turn_plan_with_grounding_sections()
    {
        DefaultPromptAssembler promptAssembler = new();
        AiGroundingBundle grounding = new(
            RouteType: AiRouteTypes.Coach,
            RuntimeFingerprint: "sha256:runtime",
            CharacterId: "char-5",
            ConversationId: "conv-5",
            WorkspaceId: "ws-5",
            RuntimeFacts: new Dictionary<string, string> { ["runtimeFingerprint"] = "sha256:runtime" },
            CharacterFacts: new Dictionary<string, string> { ["characterId"] = "char-5", ["workspaceId"] = "ws-5" },
            Constraints: ["No mutations."],
            RetrievedItems: [new AiRetrievedItem(AiRetrievalCorpusIds.Community, "idea-1", "Street Samurai Ladder", "25/50/100 karma progression")],
            AllowedTools:
            [
                AiGatewayDefaults.ResolveToolDescriptor(AiToolIds.GetCharacterDigest),
                AiGatewayDefaults.ResolveToolDescriptor(AiToolIds.SearchBuildIdeas),
                AiGatewayDefaults.ResolveToolDescriptor(AiToolIds.CreateApplyPreview)
            ]);
        AiProviderRouteDecision routeDecision = new(
            RouteType: AiRouteTypes.Coach,
            ProviderId: AiProviderIds.AiMagicx,
            Reason: "primary provider configured",
            BudgetUnit: AiBudgetUnits.ChummerAiUnits,
            ToolingEnabled: true);

        AiProviderTurnPlan plan = promptAssembler.AssembleTurnPlan(
            new AiConversationTurnRequest(
                Message: "What should I spend 18 Karma on next?",
                ConversationId: "conv-5",
                AttachmentIds: ["note-1"],
                Stream: true,
                WorkspaceId: "ws-5"),
            grounding,
            routeDecision,
            new AiBudgetSnapshot(
                BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                MonthlyAllowance: 180,
                MonthlyConsumed: 0,
                BurstLimitPerMinute: 8));

        Assert.AreEqual(AiProviderIds.AiMagicx, plan.ProviderId);
        Assert.AreEqual(AiRouteTypes.Coach, plan.RouteType);
        Assert.AreEqual("conv-5", plan.ConversationId);
        Assert.IsTrue(plan.Stream);
        CollectionAssert.Contains(plan.AttachmentIds.ToList(), "note-1");
        CollectionAssert.Contains(plan.RetrievalCorpusIds.ToList(), AiRetrievalCorpusIds.Community);
        Assert.IsTrue(plan.AllowedTools.Any(tool => tool.ToolId == AiToolIds.SearchBuildIdeas));
        Assert.IsTrue(plan.AllowedTools.Any(tool => tool.ToolId == AiToolIds.CreateApplyPreview));
        Assert.IsTrue(plan.GroundingSections.Any(section => section.SectionId == AiGroundingSectionIds.Runtime));
        Assert.IsTrue(plan.GroundingSections.Any(section => section.SectionId == AiGroundingSectionIds.RetrievedItems));
        Assert.AreEqual(AiBudgetUnits.ChummerAiUnits, plan.Budget.BudgetUnit);
        Assert.AreEqual(AiRouteTypes.Coach, plan.RouteDecision.RouteType);
        Assert.AreEqual("sha256:runtime", plan.Grounding.RuntimeFingerprint);
        Assert.AreEqual("ws-5", plan.WorkspaceId);
        StringAssert.Contains(plan.SystemPrompt, "Structured Chummer data first");
        StringAssert.Contains(plan.SystemPrompt, $"route_class: {AiRouteClassIds.GroundedRulesChat}");
        StringAssert.Contains(plan.SystemPrompt, $"persona: {AiPersonaIds.DeckerContact}");
    }

    private sealed class StubAiDigestService : IAiDigestService
    {
        private readonly AiRuntimeSummaryProjection? _runtimeSummary;
        private readonly AiCharacterDigestProjection? _characterDigest;
        private readonly AiSessionDigestProjection? _sessionDigest;

        public StubAiDigestService(
            AiRuntimeSummaryProjection? runtimeSummary = null,
            AiCharacterDigestProjection? characterDigest = null,
            AiSessionDigestProjection? sessionDigest = null)
        {
            _runtimeSummary = runtimeSummary;
            _characterDigest = characterDigest;
            _sessionDigest = sessionDigest;
        }

        public AiRuntimeSummaryProjection? GetRuntimeSummary(OwnerScope owner, string runtimeFingerprint, string? rulesetId = null)
            => _runtimeSummary;

        public AiCharacterDigestProjection? GetCharacterDigest(OwnerScope owner, string characterId)
            => _characterDigest;

        public AiSessionDigestProjection? GetSessionDigest(OwnerScope owner, string characterId)
            => _sessionDigest;
    }
}
