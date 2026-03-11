#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bunit;
using Chummer.Contracts.AI;
using Chummer.Coach.Web;
using Chummer.Coach.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BunitContext = Bunit.BunitContext;

namespace Chummer.Tests.Presentation;

[TestClass]
public sealed class CoachWebComponentTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void Home_renders_live_gateway_prompt_and_build_idea_data()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;
        RegisterCoachHeadServices(context);
        SetupCoachMetadataResponses(context);
        SetupConversationCatalogResponse(context);

        IRenderedComponent<Home> cut = context.Render<Home>();

        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Chummer Coach");
            StringAssert.Contains(cut.Markup, "AI Magicx");
            StringAssert.Contains(cut.Markup, "Transport");
            StringAssert.Contains(cut.Markup, "Base URL");
            StringAssert.Contains(cut.Markup, "Model");
            StringAssert.Contains(cut.Markup, "Last Route");
            StringAssert.Contains(cut.Markup, "Last Binding");
            StringAssert.Contains(cut.Markup, "coach");
            StringAssert.Contains(cut.Markup, "primary / slot 0");
            Assert.IsFalse(cut.Markup.Contains("1minAI", StringComparison.Ordinal));
            StringAssert.Contains(cut.Markup, "Explain Value");
            StringAssert.Contains(cut.Markup, "Coach Route System Prompt");
            StringAssert.Contains(cut.Markup, "Silent Infiltrator");
            StringAssert.Contains(cut.Markup, "decker-contact");
            StringAssert.Contains(cut.Markup, "Grounded coach lane.");
            StringAssert.Contains(cut.Markup, "388 left");
        });
    }

    [TestMethod]
    public void Home_bootstraps_launch_context_from_query_string()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;
        RegisterCoachHeadServices(context);
        SetupCoachMetadataResponses(context);
        AiConversationSnapshot queryConversation = new(
            ConversationId: "conv.query",
            RouteType: AiRouteTypes.Build,
            Messages:
            [
                new AiConversationMessage("user-query", AiConversationRoles.User, "Plan my next buy", new DateTimeOffset(2026, 03, 07, 10, 00, 00, TimeSpan.Zero)),
                new AiConversationMessage("assistant-query", AiConversationRoles.Assistant, "Start with drone handling before the expensive chassis.", new DateTimeOffset(2026, 03, 07, 10, 00, 06, TimeSpan.Zero), AiProviderIds.AiMagicx)
            ],
            RuntimeFingerprint: "sha256:runtime-query",
            CharacterId: "char-query",
            WorkspaceId: "ws-query");
        SetupConversationCatalogResponse(context, queryConversation);
        SetupJsonResponse(context, "/api/ai/conversations/conv.query", queryConversation);
        SetupJsonResponse(
            context,
            "/api/ai/prompts?routeType=build&personaId=decker-contact&maxCount=6",
            new AiPromptCatalog(
                [
                    new AiPromptDescriptor(
                        PromptId: AiRouteTypes.Build,
                        PromptKind: AiPromptKinds.RouteSystem,
                        RouteType: AiRouteTypes.Build,
                        RouteClassId: AiRouteClassIds.BuildSimulation,
                        PersonaId: AiPersonaIds.DeckerContact,
                        Title: "Build Route System Prompt",
                        Summary: "Grounded build-planning prompt.",
                        BaseInstructions: ["Prefer runtime truth first."],
                        RequiredGroundingSectionIds: [AiGroundingSectionIds.Runtime, AiGroundingSectionIds.Character],
                        RetrievalCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community],
                        AllowedToolIds: [AiToolIds.SearchBuildIdeas],
                        EvidenceFirst: true,
                        MinFlavorPercent: 5,
                        MaxFlavorPercent: 15)
                ],
                1));
        SetupJsonResponse(
            context,
            "/api/ai/build-ideas?routeType=build&queryText=drone%20support&rulesetId=sr6&maxCount=6",
            new AiBuildIdeaCatalog(
                [
                    new BuildIdeaCard(
                        IdeaId: "idea.drone.support",
                        RulesetId: "sr6",
                        Title: "Drone Support",
                        Summary: "Remote support specialist with grounded drone utility.",
                        RoleTags: ["rigger", "support"],
                        CompatibleProfileIds: ["official.sr6.core"],
                        CoreLoop: "Stay mobile and keep drones solving problems.",
                        EarlyPriorities: ["Pilot Ground Craft 5"],
                        KarmaMilestones: ["Raise Engineering"],
                        Strengths: ["Flexible overwatch"],
                        Weaknesses: ["Gear dependent"],
                        TrapChoices: ["Overcommitting to one chassis"],
                        LinkedContentIds: ["gear.drone"],
                        CommunityScore: 4.4)
                ],
                1));
        SetupJsonResponse(
            context,
            "/api/ai/conversation-audits?routeType=build&characterId=char-query&runtimeFingerprint=sha256%3Aruntime-query&workspaceId=ws-query&maxCount=6",
            new AiConversationAuditCatalogPage([], 0));

        NavigationManager navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("http://localhost/coach/?routeType=build&conversationId=conv.query&runtimeFingerprint=sha256%3Aruntime-query&characterId=char-query&workspaceId=ws-query&rulesetId=sr6&message=Plan%20my%20next%20buy&buildIdeaQuery=drone%20support");

        IRenderedComponent<Home> cut = context.Render<Home>();

        cut.WaitForAssertion(() =>
        {
            Assert.AreEqual("build", cut.Find("select[data-coach-route='route-select']").GetAttribute("value"));
            Assert.AreEqual("sha256:runtime-query", cut.Find("input[placeholder='sha256:...']").GetAttribute("value"));
            Assert.AreEqual("char-query", cut.Find("input[placeholder='char-1']").GetAttribute("value"));
            Assert.AreEqual("ws-query", cut.Find("input[placeholder='ws-1']").GetAttribute("value"));
            Assert.AreEqual("sr6", cut.Find("input[placeholder='sr5']").GetAttribute("value"));
            Assert.AreEqual("drone support", cut.Find("input[placeholder='stealth decker, face mage, drone support...']").GetAttribute("value"));
            StringAssert.Contains(cut.Markup, "Plan my next buy");
            StringAssert.Contains(cut.Markup, "data-conversation-detail=\"conv.query\"");
        });
    }

    [TestMethod]
    public void Home_previews_grounded_turns_and_replays_owner_scoped_conversations()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;
        RegisterCoachHeadServices(context);
        SetupCoachMetadataResponses(context);
        SetupConversationCatalogResponse(
            context,
            new AiConversationSnapshot(
                ConversationId: "conv.street-1",
                RouteType: AiRouteTypes.Coach,
                Messages:
                [
                    new AiConversationMessage("user-1", AiConversationRoles.User, "How do I keep stealth intact?", new DateTimeOffset(2026, 03, 07, 11, 00, 00, TimeSpan.Zero)),
                    new AiConversationMessage("assistant-2", AiConversationRoles.Assistant, "Boost Sneaking before expensive ware.", new DateTimeOffset(2026, 03, 07, 11, 00, 05, TimeSpan.Zero), AiProviderIds.AiMagicx)
                ],
                RuntimeFingerprint: "sha256:runtime-profile",
                CharacterId: "char-1",
                WorkspaceId: "ws-stealth"));
        SetupJsonResponse(
            context,
            "/api/ai/preview/coach",
            new AiConversationTurnPreview(
                RouteType: AiRouteTypes.Coach,
                RouteDecision: new AiProviderRouteDecision(
                    RouteType: AiRouteTypes.Coach,
                    ProviderId: AiProviderIds.AiMagicx,
                    Reason: "AI Magicx owns grounded coaching.",
                    BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                    ToolingEnabled: true),
                Grounding: new AiGroundingBundle(
                    RouteType: AiRouteTypes.Coach,
                    RuntimeFingerprint: "sha256:runtime-profile",
                    CharacterId: "char-1",
                    ConversationId: "conv.street-1",
                    WorkspaceId: "ws-stealth",
                    RuntimeFacts: new Dictionary<string, string>
                    {
                        ["profile"] = "official.sr5.core"
                    },
                    CharacterFacts: new Dictionary<string, string>
                    {
                        ["focus"] = "stealth decker"
                    },
                    Constraints: ["18 Karma remaining"],
                    RetrievedItems:
                    [
                        new AiRetrievedItem(AiRetrievalCorpusIds.Community, "idea.stealth.decker", "Silent Infiltrator", "Stay quiet and keep overwatch low.", "community", "sr5")
                    ],
                    AllowedTools:
                    [
                        new AiToolDescriptor(AiToolIds.ExplainValue, "Explain Value", "Resolve grounded Explain API context."),
                        new AiToolDescriptor(AiToolIds.SearchBuildIdeas, "Search Build Ideas", "Search structured build idea cards.")
                    ]),
                Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 18, 6),
                SystemPrompt: "Lead with runtime truth and cite Chummer data before prose.",
                ProviderRequest: new AiProviderTurnPlan(
                    ProviderId: AiProviderIds.AiMagicx,
                    RouteType: AiRouteTypes.Coach,
                    ConversationId: "conv.street-1",
                    UserMessage: "How do I keep stealth intact?",
                    SystemPrompt: "Lead with runtime truth and cite Chummer data before prose.",
                    Stream: false,
                    AttachmentIds: [],
                    RetrievalCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community],
                    AllowedTools:
                    [
                        new AiToolDescriptor(AiToolIds.ExplainValue, "Explain Value", "Resolve grounded Explain API context."),
                        new AiToolDescriptor(AiToolIds.SearchBuildIdeas, "Search Build Ideas", "Search structured build idea cards.")
                    ],
                    GroundingSections:
                    [
                        new AiGroundingSection(AiGroundingSectionIds.Runtime, "Runtime", ["official.sr5.core"]),
                        new AiGroundingSection(AiGroundingSectionIds.Character, "Character", ["stealth decker"])
                    ],
                    RouteDecision: new AiProviderRouteDecision(
                        RouteType: AiRouteTypes.Coach,
                        ProviderId: AiProviderIds.AiMagicx,
                        Reason: "AI Magicx owns grounded coaching.",
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        ToolingEnabled: true),
                    Grounding: new AiGroundingBundle(
                        RouteType: AiRouteTypes.Coach,
                        RuntimeFingerprint: "sha256:runtime-profile",
                        CharacterId: "char-1",
                        ConversationId: "conv.street-1",
                        WorkspaceId: "ws-stealth",
                        RuntimeFacts: new Dictionary<string, string>
                        {
                            ["profile"] = "official.sr5.core"
                        },
                        CharacterFacts: new Dictionary<string, string>
                        {
                            ["focus"] = "stealth decker"
                        },
                        Constraints: ["18 Karma remaining"],
                        RetrievedItems:
                        [
                            new AiRetrievedItem(AiRetrievalCorpusIds.Community, "idea.stealth.decker", "Silent Infiltrator", "Stay quiet and keep overwatch low.", "community", "sr5")
                        ],
                        AllowedTools:
                        [
                            new AiToolDescriptor(AiToolIds.ExplainValue, "Explain Value", "Resolve grounded Explain API context."),
                            new AiToolDescriptor(AiToolIds.SearchBuildIdeas, "Search Build Ideas", "Search structured build idea cards.")
                        ]),
                    Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 18, 6),
                    WorkspaceId: "ws-stealth")),
            "POST");
        SetupJsonResponse(
            context,
            "/api/ai/preview/karma-spend",
            new AiActionPreviewReceipt(
                PreviewId: "preview:karma:ws-stealth",
                Operation: AiActionPreviewApiOperations.PreviewKarmaSpend,
                PreviewKind: AiActionPreviewKinds.KarmaSpend,
                CharacterId: "char-1",
                CharacterDisplayName: "Cipher (Ghostwire)",
                RulesetId: "sr5",
                RuntimeFingerprint: "sha256:runtime-profile",
                State: AiActionPreviewStates.Scaffolded,
                Summary: "Scoped karma preview for the stealth workspace.",
                StepCount: 1,
                TotalRequested: null,
                Unit: "karma",
                PreparedEffects:
                [
                    "Prepared a non-mutating karma-spend preview for 1 step(s).",
                    "Workbench origin preserved from workspace ws-stealth."
                ],
                Evidence:
                [
                    new AiEvidenceEntry("Workspace", "Scoped to the stealth workspace.", "ws-stealth", "workspace")
                ],
                Risks:
                [
                    new AiRiskEntry("warn", "Scaffolded preview", "No mutation path executed.")
                ],
                WorkspaceId: "ws-stealth"),
            "POST");
        SetupJsonResponse(
            context,
            "/api/ai/conversations/conv.street-1",
            new AiConversationSnapshot(
                ConversationId: "conv.street-1",
                RouteType: AiRouteTypes.Coach,
                Messages:
                [
                    new AiConversationMessage("user-1", AiConversationRoles.User, "How do I keep stealth intact?", new DateTimeOffset(2026, 03, 07, 11, 00, 00, TimeSpan.Zero)),
                    new AiConversationMessage("assistant-2", AiConversationRoles.Assistant, "Boost Sneaking before expensive ware.", new DateTimeOffset(2026, 03, 07, 11, 00, 05, TimeSpan.Zero), AiProviderIds.AiMagicx)
                ],
                RuntimeFingerprint: "sha256:runtime-profile",
                CharacterId: "char-1",
                WorkspaceId: "ws-stealth",
                Turns:
                [
                    new AiConversationTurnRecord(
                        TurnId: "turn-1",
                        RouteType: AiRouteTypes.Coach,
                        ProviderId: AiProviderIds.AiMagicx,
                        CreatedAtUtc: new DateTimeOffset(2026, 03, 07, 11, 00, 05, TimeSpan.Zero),
                        UserMessage: "How do I keep stealth intact?",
                        AssistantAnswer: "Boost Sneaking before expensive ware.",
                        ToolInvocations:
                        [
                            new AiToolInvocation(AiToolIds.SearchBuildIdeas, "ok", "Loaded one grounded build idea.")
                        ],
                        Citations:
                        [
                            new AiCitation("build-idea", "Silent Infiltrator", "idea.stealth.decker")
                        ],
                        StructuredAnswer: new AiStructuredAnswer(
                            Summary: "Boost stealth fundamentals before high-cost ware.",
                            Recommendations:
                            [
                                new AiRecommendation("rec-1", "Raise Sneaking", "Your stealth baseline is the weak link.", "Fewer detection spikes.")
                            ],
                            Evidence:
                            [
                                new AiEvidenceEntry("Build Idea", "Silent Infiltrator matches the requested path.", "idea.stealth.decker", "community")
                            ],
                            Risks:
                            [
                                new AiRiskEntry("warn", "Nuyen tightness", "Delaying ware keeps pressure off your budget.")
                            ],
                            Confidence: "high",
                            RuntimeFingerprint: "sha256:runtime-profile",
                            Sources:
                            [
                                new AiSourceReference("build-idea", "Silent Infiltrator", "idea.stealth.decker", "community")
                            ],
                            ActionDrafts:
                            [
                                new AiActionDraft("preview_karma_spend", "Preview Karma Spend", "Review the next stealth purchase.")
                            ]),
                        RuntimeFingerprint: "sha256:runtime-profile",
                        CharacterId: "char-1",
                        WorkspaceId: "ws-stealth",
                        Cache: new AiCacheMetadata(
                            Status: AiCacheStatuses.Hit,
                            CacheKey: "cache::coach::stealth",
                            CachedAtUtc: new DateTimeOffset(2026, 03, 07, 10, 59, 30, TimeSpan.Zero),
                            NormalizedPrompt: "how do i keep stealth intact?",
                            RuntimeFingerprint: "sha256:runtime-profile",
                            CharacterId: "char-1",
                            WorkspaceId: "ws-stealth"),
                        RouteDecision: new AiProviderRouteDecision(
                            RouteType: AiRouteTypes.Coach,
                            ProviderId: AiProviderIds.AiMagicx,
                            Reason: "Replay kept the grounded coaching path.",
                            BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                            ToolingEnabled: true,
                            CredentialTier: AiProviderCredentialTiers.Primary,
                            CredentialSlotIndex: 0),
                        GroundingCoverage: new AiGroundingCoverage(
                            ScorePercent: 100,
                            Summary: "coverage 100%: runtime, character, constraints, and retrieved evidence present.",
                            PresentSignals: ["runtime", "character", "constraints", "retrieved"],
                            MissingSignals: [],
                            RetrievedCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community]))
                ]));

        IRenderedComponent<Home> cut = context.Render<Home>();

        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "conv.street-1");
            StringAssert.Contains(cut.Markup, "Last route:");
            StringAssert.Contains(cut.Markup, "Last coverage:");
        });

        cut.Find("button[data-coach-preview='coach-turn']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "AI Magicx owns grounded coaching.");
            StringAssert.Contains(cut.Markup, "Lead with runtime truth and cite Chummer data before prose.");
            StringAssert.Contains(cut.Markup, "Silent Infiltrator");
            StringAssert.Contains(cut.Markup, "Grounding coverage:");
            StringAssert.Contains(cut.Markup, "Present signals:");
            StringAssert.Contains(cut.Markup, "ws-stealth");
        });

        cut.Find("button[data-conversation-open='conv.street-1']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Boost Sneaking before expensive ware.");
            StringAssert.Contains(cut.Markup, "idea.stealth.decker");
            StringAssert.Contains(cut.Markup, "turn-1");
            StringAssert.Contains(cut.Markup, "cache hit");
            StringAssert.Contains(cut.Markup, "how do i keep stealth intact?");
            StringAssert.Contains(cut.Markup, "Replay kept the grounded coaching path.");
            StringAssert.Contains(cut.Markup, "primary / slot 0");
            StringAssert.Contains(cut.Markup, "coverage 100%:");
            Assert.AreEqual("ws-stealth", cut.Find("input[placeholder='ws-1']").GetAttribute("value"));
        });

        cut.Find("button[data-action-draft-preview='turn-1:preview_karma_spend']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Action Preview");
            StringAssert.Contains(cut.Markup, "Scoped karma preview for the stealth workspace.");
            StringAssert.Contains(cut.Markup, "ws-stealth");
        });
    }

    [TestMethod]
    public void Home_submits_grounded_turns_and_refreshes_owner_scoped_history()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;
        RegisterCoachHeadServices(context);
        SetupCoachMetadataResponses(context);
        SetupConversationCatalogResponse(context);
        SetupJsonResponse(
            context,
            "/api/ai/coach",
            new AiConversationTurnResponse(
                ConversationId: "conv.live-1",
                RouteType: AiRouteTypes.Coach,
                ProviderId: AiProviderIds.AiMagicx,
                Answer: "Boost Sneaking now, then preview a karma spend for Codeslinger.",
                RouteDecision: new AiProviderRouteDecision(
                    RouteType: AiRouteTypes.Coach,
                    ProviderId: AiProviderIds.AiMagicx,
                    Reason: "AI Magicx owns grounded coaching.",
                    BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                    ToolingEnabled: true),
                Grounding: new AiGroundingBundle(
                    RouteType: AiRouteTypes.Coach,
                    RuntimeFingerprint: "sha256:runtime-profile",
                    CharacterId: "char-1",
                    ConversationId: "conv.live-1",
                    WorkspaceId: "ws-live",
                    RuntimeFacts: new Dictionary<string, string>
                    {
                        ["profile"] = "official.sr5.core"
                    },
                    CharacterFacts: new Dictionary<string, string>
                    {
                        ["focus"] = "stealth decker"
                    },
                    Constraints: ["18 Karma remaining"],
                    RetrievedItems:
                    [
                        new AiRetrievedItem(AiRetrievalCorpusIds.Community, "idea.stealth.decker", "Silent Infiltrator", "Stay quiet and keep overwatch low.", "community", "sr5")
                    ],
                    AllowedTools:
                    [
                        new AiToolDescriptor(AiToolIds.SearchBuildIdeas, "Search Build Ideas", "Search structured build idea cards."),
                        new AiToolDescriptor(AiToolIds.SimulateKarmaSpend, "Simulate Karma Spend", "Preview karma-spend outcomes without mutating state.")
                    ]),
                Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 19, 6),
                Citations:
                [
                    new AiCitation("build-idea", "Silent Infiltrator", "idea.stealth.decker")
                ],
                SuggestedActions:
                [
                    new AiSuggestedAction("preview-karma", "Preview Karma Spend", "Open a grounded karma preview.")
                ],
                ToolInvocations:
                [
                    new AiToolInvocation(AiToolIds.SearchBuildIdeas, "ok", "Loaded one grounded build idea."),
                    new AiToolInvocation(AiToolIds.SimulateKarmaSpend, "prepared", "Prepared a karma preview action.")
                ],
                FlavorLine: "Hold up, chummer. Here's the clean line from your active runtime.",
                StructuredAnswer: new AiStructuredAnswer(
                    Summary: "Raise Sneaking first, then queue a Codeslinger preview.",
                    Recommendations:
                    [
                        new AiRecommendation("rec-1", "Raise Sneaking", "It improves your stealth floor immediately.", "Fewer detection spikes."),
                        new AiRecommendation("rec-2", "Preview Codeslinger", "Check the next specialization spend before committing.", "Cleaner next-step planning.")
                    ],
                    Evidence:
                    [
                        new AiEvidenceEntry("Build Idea", "Silent Infiltrator matches the stealth decker lane.", "idea.stealth.decker", "community")
                    ],
                    Risks:
                    [
                        new AiRiskEntry("warn", "Nuyen tightness", "Avoid ware purchases before the karma floor is fixed.")
                    ],
                    Confidence: "high",
                    RuntimeFingerprint: "sha256:runtime-profile",
                    Sources:
                    [
                        new AiSourceReference("build-idea", "Silent Infiltrator", "idea.stealth.decker", "community")
                    ],
                    ActionDrafts:
                    [
                        new AiActionDraft("preview_karma_spend", "Preview Karma Spend", "Review the next stealth purchase.")
                    ]),
                Cache: new AiCacheMetadata(
                    Status: AiCacheStatuses.Miss,
                    CacheKey: "cache::coach::advancement",
                    CachedAtUtc: new DateTimeOffset(2026, 03, 07, 12, 30, 03, TimeSpan.Zero),
                    NormalizedPrompt: "what should i spend 18 karma on next?",
                    RuntimeFingerprint: "sha256:runtime-profile",
                    CharacterId: "char-1")),
            "POST");
        SetupJsonResponse(
            context,
            "/api/ai/conversations/conv.live-1",
            new AiConversationSnapshot(
                ConversationId: "conv.live-1",
                RouteType: AiRouteTypes.Coach,
                Messages:
                [
                    new AiConversationMessage("system-1", AiConversationRoles.System, "Lead with runtime truth.", new DateTimeOffset(2026, 03, 07, 12, 30, 00, TimeSpan.Zero), AiProviderIds.AiMagicx),
                    new AiConversationMessage("user-2", AiConversationRoles.User, "What should I spend 18 Karma on next?", new DateTimeOffset(2026, 03, 07, 12, 30, 01, TimeSpan.Zero)),
                    new AiConversationMessage("assistant-3", AiConversationRoles.Assistant, "Boost Sneaking now, then preview a karma spend for Codeslinger.", new DateTimeOffset(2026, 03, 07, 12, 30, 03, TimeSpan.Zero), AiProviderIds.AiMagicx)
                ],
                RuntimeFingerprint: "sha256:runtime-profile",
                CharacterId: "char-1",
                WorkspaceId: "ws-live",
                Turns:
                [
                    new AiConversationTurnRecord(
                        TurnId: "turn-live-1",
                        RouteType: AiRouteTypes.Coach,
                        ProviderId: AiProviderIds.AiMagicx,
                        CreatedAtUtc: new DateTimeOffset(2026, 03, 07, 12, 30, 03, TimeSpan.Zero),
                        UserMessage: "What should I spend 18 Karma on next?",
                        AssistantAnswer: "Boost Sneaking now, then preview a karma spend for Codeslinger.",
                        ToolInvocations:
                        [
                            new AiToolInvocation(AiToolIds.SearchBuildIdeas, "ok", "Loaded one grounded build idea."),
                            new AiToolInvocation(AiToolIds.SimulateKarmaSpend, "prepared", "Prepared a karma preview action.")
                        ],
                        Citations:
                        [
                            new AiCitation("build-idea", "Silent Infiltrator", "idea.stealth.decker")
                        ],
                        StructuredAnswer: new AiStructuredAnswer(
                            Summary: "Raise Sneaking first, then queue a Codeslinger preview.",
                            Recommendations:
                            [
                                new AiRecommendation("rec-1", "Raise Sneaking", "It improves your stealth floor immediately.", "Fewer detection spikes.")
                            ],
                            Evidence:
                            [
                                new AiEvidenceEntry("Build Idea", "Silent Infiltrator matches the stealth decker lane.", "idea.stealth.decker", "community")
                            ],
                            Risks:
                            [
                                new AiRiskEntry("warn", "Nuyen tightness", "Avoid ware purchases before the karma floor is fixed.")
                            ],
                            Confidence: "high",
                            RuntimeFingerprint: "sha256:runtime-profile",
                            Sources:
                            [
                                new AiSourceReference("build-idea", "Silent Infiltrator", "idea.stealth.decker", "community")
                            ],
                            ActionDrafts:
                            [
                                new AiActionDraft("preview_karma_spend", "Preview Karma Spend", "Review the next stealth purchase.")
                            ]),
                        RuntimeFingerprint: "sha256:runtime-profile",
                        CharacterId: "char-1",
                        WorkspaceId: "ws-live",
                        Cache: new AiCacheMetadata(
                            Status: AiCacheStatuses.Miss,
                            CacheKey: "cache::coach::advancement",
                            CachedAtUtc: new DateTimeOffset(2026, 03, 07, 12, 30, 03, TimeSpan.Zero),
                            NormalizedPrompt: "what should i spend 18 karma on next?",
                            RuntimeFingerprint: "sha256:runtime-profile",
                            CharacterId: "char-1",
                            WorkspaceId: "ws-live"))
                ]));
        SetupJsonResponse(
            context,
            "/api/ai/preview/karma-spend",
            new AiActionPreviewReceipt(
                PreviewId: "preview:karma:ws-live",
                Operation: AiActionPreviewApiOperations.PreviewKarmaSpend,
                PreviewKind: AiActionPreviewKinds.KarmaSpend,
                CharacterId: "char-1",
                CharacterDisplayName: "Cipher (Ghostwire)",
                RulesetId: "sr5",
                RuntimeFingerprint: "sha256:runtime-profile",
                State: AiActionPreviewStates.Scaffolded,
                Summary: "Scoped karma preview for the live workspace.",
                StepCount: 1,
                TotalRequested: null,
                Unit: "karma",
                PreparedEffects:
                [
                    "Prepared a non-mutating karma-spend preview for 1 step(s).",
                    "Workbench origin preserved from workspace ws-live."
                ],
                Evidence:
                [
                    new AiEvidenceEntry("Workspace", "Scoped to the live workspace.", "ws-live", "workspace")
                ],
                Risks:
                [
                    new AiRiskEntry("warn", "Scaffolded preview", "No mutation path executed.")
                ],
                WorkspaceId: "ws-live"),
            "POST");

        IRenderedComponent<Home> cut = context.Render<Home>();

        cut.Find("button[data-coach-send='coach-turn']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Hold up, chummer. Here's the clean line from your active runtime.");
            StringAssert.Contains(cut.Markup, "Raise Sneaking first, then queue a Codeslinger preview.");
            StringAssert.Contains(cut.Markup, "Preview Karma Spend");
            StringAssert.Contains(cut.Markup, "conv.live-1");
            StringAssert.Contains(cut.Markup, "turn-live-1");
            StringAssert.Contains(cut.Markup, "Grounding coverage:");
            StringAssert.Contains(cut.Markup, "Retrieved corpora:");
            StringAssert.Contains(cut.Markup, "cache miss");
            StringAssert.Contains(cut.Markup, "cache::coach::advancement");
            StringAssert.Contains(cut.Markup, "ws-live");
        });

        cut.Find("button[data-action-draft-preview='latest:preview_karma_spend']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Action Preview");
            StringAssert.Contains(cut.Markup, "Scoped karma preview for the live workspace.");
            StringAssert.Contains(cut.Markup, "ws-live");
        });
    }

    [TestMethod]
    public void Home_surfaces_quota_receipts_when_live_turn_submission_hits_budget_limits()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;
        RegisterCoachHeadServices(context);
        SetupCoachMetadataResponses(context);
        SetupConversationCatalogResponse(context);
        SetupQuotaExceededResponse(
            context,
            "/api/ai/coach",
            new AiQuotaExceededReceipt(
                Error: "ai_quota_exceeded",
                Operation: AiApiOperations.SendCoachTurn,
                Message: "The coach route has exhausted its monthly chummer-ai-units allowance for this owner.",
                Budget: new AiBudgetSnapshot(
                    BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                    MonthlyAllowance: 1,
                    MonthlyConsumed: 1,
                    BurstLimitPerMinute: 4),
                RequestedUnits: 1,
                RouteType: AiRouteTypes.Coach,
                OwnerId: "owner@example.com"),
            "POST");

        IRenderedComponent<Home> cut = context.Render<Home>();

        cut.Find("button[data-coach-send='coach-turn']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "The coach route has exhausted its monthly chummer-ai-units allowance for this owner.");
            StringAssert.Contains(cut.Markup, "error");
        });
    }

    [TestMethod]
    public void Home_uses_selected_route_for_refresh_preview_and_send_requests()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;
        RegisterCoachHeadServices(context);
        SetupCoachMetadataResponses(context);
        SetupConversationCatalogResponse(context);
        SetupJsonResponse(
            context,
            "/api/ai/prompts?routeType=build&personaId=decker-contact&maxCount=6",
            new AiPromptCatalog(
            [
                new AiPromptDescriptor(
                    PromptId: "build-route",
                    PromptKind: AiPromptKinds.RouteSystem,
                    RouteType: AiRouteTypes.Build,
                    RouteClassId: AiRouteClassIds.BuildSimulation,
                    PersonaId: AiPersonaIds.DeckerContact,
                    Title: "Build Route System Prompt",
                    Summary: "Grounded build-lab planning prompt.",
                    BaseInstructions: ["Stay grounded in runtime and build ideas."],
                    RequiredGroundingSectionIds: [AiGroundingSectionIds.Runtime, AiGroundingSectionIds.Character],
                    RetrievalCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community],
                    AllowedToolIds: [AiToolIds.SearchBuildIdeas, AiToolIds.SimulateKarmaSpend],
                    EvidenceFirst: true,
                    MinFlavorPercent: 5,
                    MaxFlavorPercent: 15)
            ],
            1));
        SetupJsonResponse(
            context,
            "/api/ai/build-ideas?routeType=build&queryText=stealth&rulesetId=sr5&maxCount=6",
            new AiBuildIdeaCatalog(
            [
                new BuildIdeaCard(
                    IdeaId: "idea.build.street-sam",
                    RulesetId: "sr5",
                    Title: "Street Samurai Ladder",
                    Summary: "A grounded build path for direct-action combat growth.",
                    RoleTags: ["street-samurai", "combat"],
                    CompatibleProfileIds: ["official.sr5.core"],
                    CoreLoop: "Raise baseline combat output before niche upgrades.",
                    EarlyPriorities: ["Automatics 6", "Reaction 5"],
                    KarmaMilestones: ["Improve Reflexes", "Specialize Automatics"],
                    Strengths: ["Immediate combat floor"],
                    Weaknesses: ["Tight early karma"],
                    TrapChoices: ["Buying side-grade gear before core stats"],
                    LinkedContentIds: ["quality.high-pain-tolerance"],
                    CommunityScore: 4.6)
            ],
            1));
        SetupJsonResponse(
            context,
            "/api/ai/build-ideas?routeType=build&queryText=What%20should%20I%20spend%2018%20Karma%20on%20next%3F&rulesetId=sr5&maxCount=6",
            new AiBuildIdeaCatalog(
            [
                new BuildIdeaCard(
                    IdeaId: "idea.build.karma-ladder",
                    RulesetId: "sr5",
                    Title: "Karma Progression Ladder",
                    Summary: "A grounded progression ladder tuned to the next 18 Karma spend.",
                    RoleTags: ["street-samurai", "advancement"],
                    CompatibleProfileIds: ["official.sr5.core"],
                    CoreLoop: "Spend karma on the cleanest floor-raising upgrades first.",
                    EarlyPriorities: ["Raise Automatics", "Bank ware follow-ups for later"],
                    KarmaMilestones: ["Automatics 7", "Improved Reflexes preview", "Reaction 6"],
                    Strengths: ["Clear next buys"],
                    Weaknesses: ["Requires discipline"],
                    TrapChoices: ["Buying chrome before skill floor"],
                    LinkedContentIds: ["quality.high-pain-tolerance"],
                    CommunityScore: 4.8)
            ],
            1));
        SetupJsonResponse(
            context,
            "/api/ai/conversation-audits?routeType=build&maxCount=6",
            new AiConversationAuditCatalogPage(
            [
                new AiConversationAuditSummary(
                    ConversationId: "conv.build-1",
                    RouteType: AiRouteTypes.Build,
                    MessageCount: 1,
                    LastUpdatedAtUtc: new DateTimeOffset(2026, 03, 07, 13, 05, 00, TimeSpan.Zero),
                    RuntimeFingerprint: "sha256:runtime-profile",
                    CharacterId: "char-1",
                    LastAssistantAnswer: "Build path ready.",
                    LastProviderId: AiProviderIds.AiMagicx)
            ],
            1));
        SetupJsonResponse(
            context,
            "/api/ai/preview/build",
            new AiConversationTurnPreview(
                RouteType: AiRouteTypes.Build,
                RouteDecision: new AiProviderRouteDecision(
                    RouteType: AiRouteTypes.Build,
                    ProviderId: AiProviderIds.AiMagicx,
                    Reason: "AI Magicx owns grounded build planning.",
                    BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                    ToolingEnabled: true),
                Grounding: new AiGroundingBundle(
                    RouteType: AiRouteTypes.Build,
                    RuntimeFingerprint: "sha256:runtime-profile",
                    CharacterId: "char-1",
                    ConversationId: "conv.build-1",
                    RuntimeFacts: new Dictionary<string, string>
                    {
                        ["profile"] = "official.sr5.core"
                    },
                    CharacterFacts: new Dictionary<string, string>
                    {
                        ["role"] = "street samurai"
                    },
                    Constraints: ["18 Karma remaining"],
                    RetrievedItems:
                    [
                        new AiRetrievedItem(AiRetrievalCorpusIds.Community, "idea.build.street-sam", "Street Samurai Ladder", "A grounded build path for direct-action combat growth.", "community", "sr5")
                    ],
                    AllowedTools:
                    [
                        new AiToolDescriptor(AiToolIds.SearchBuildIdeas, "Search Build Ideas", "Search structured build idea cards."),
                        new AiToolDescriptor(AiToolIds.SimulateKarmaSpend, "Simulate Karma Spend", "Preview karma-spend outcomes without mutating state.")
                    ]),
                Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 20, 6),
                SystemPrompt: "Build plans must cite runtime facts and structured build cards.",
                ProviderRequest: new AiProviderTurnPlan(
                    ProviderId: AiProviderIds.AiMagicx,
                    RouteType: AiRouteTypes.Build,
                    ConversationId: "conv.build-1",
                    UserMessage: "What should I spend 18 Karma on next?",
                    SystemPrompt: "Build plans must cite runtime facts and structured build cards.",
                    Stream: false,
                    AttachmentIds: [],
                    RetrievalCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community],
                    AllowedTools:
                    [
                        new AiToolDescriptor(AiToolIds.SearchBuildIdeas, "Search Build Ideas", "Search structured build idea cards."),
                        new AiToolDescriptor(AiToolIds.SimulateKarmaSpend, "Simulate Karma Spend", "Preview karma-spend outcomes without mutating state.")
                    ],
                    GroundingSections:
                    [
                        new AiGroundingSection(AiGroundingSectionIds.Runtime, "Runtime", ["official.sr5.core"]),
                        new AiGroundingSection(AiGroundingSectionIds.Character, "Character", ["street samurai"])
                    ],
                    RouteDecision: new AiProviderRouteDecision(
                        RouteType: AiRouteTypes.Build,
                        ProviderId: AiProviderIds.AiMagicx,
                        Reason: "AI Magicx owns grounded build planning.",
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        ToolingEnabled: true),
                    Grounding: new AiGroundingBundle(
                        RouteType: AiRouteTypes.Build,
                        RuntimeFingerprint: "sha256:runtime-profile",
                        CharacterId: "char-1",
                        ConversationId: "conv.build-1",
                        RuntimeFacts: new Dictionary<string, string>
                        {
                            ["profile"] = "official.sr5.core"
                        },
                        CharacterFacts: new Dictionary<string, string>
                        {
                            ["role"] = "street samurai"
                        },
                        Constraints: ["18 Karma remaining"],
                        RetrievedItems:
                        [
                            new AiRetrievedItem(AiRetrievalCorpusIds.Community, "idea.build.street-sam", "Street Samurai Ladder", "A grounded build path for direct-action combat growth.", "community", "sr5")
                        ],
                        AllowedTools:
                        [
                            new AiToolDescriptor(AiToolIds.SearchBuildIdeas, "Search Build Ideas", "Search structured build idea cards."),
                            new AiToolDescriptor(AiToolIds.SimulateKarmaSpend, "Simulate Karma Spend", "Preview karma-spend outcomes without mutating state.")
                        ]),
                    Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 20, 6))),
            "POST");
        SetupJsonResponse(
            context,
            "/api/ai/build-lab/query",
            new AiConversationTurnResponse(
                ConversationId: "conv.build-1",
                RouteType: AiRouteTypes.Build,
                ProviderId: AiProviderIds.AiMagicx,
                Answer: "Raise Automatics first, then preview Improved Reflexes.",
                RouteDecision: new AiProviderRouteDecision(
                    RouteType: AiRouteTypes.Build,
                    ProviderId: AiProviderIds.AiMagicx,
                    Reason: "AI Magicx owns grounded build planning.",
                    BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                    ToolingEnabled: true),
                Grounding: new AiGroundingBundle(
                    RouteType: AiRouteTypes.Build,
                    RuntimeFingerprint: "sha256:runtime-profile",
                    CharacterId: "char-1",
                    ConversationId: "conv.build-1",
                    RuntimeFacts: new Dictionary<string, string>
                    {
                        ["profile"] = "official.sr5.core"
                    },
                    CharacterFacts: new Dictionary<string, string>
                    {
                        ["role"] = "street samurai"
                    },
                    Constraints: ["18 Karma remaining"],
                    RetrievedItems:
                    [
                        new AiRetrievedItem(AiRetrievalCorpusIds.Community, "idea.build.street-sam", "Street Samurai Ladder", "A grounded build path for direct-action combat growth.", "community", "sr5")
                    ],
                    AllowedTools:
                    [
                        new AiToolDescriptor(AiToolIds.SearchBuildIdeas, "Search Build Ideas", "Search structured build idea cards."),
                        new AiToolDescriptor(AiToolIds.SimulateKarmaSpend, "Simulate Karma Spend", "Preview karma-spend outcomes without mutating state.")
                    ]),
                Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 21, 6),
                Citations:
                [
                    new AiCitation("build-idea", "Street Samurai Ladder", "idea.build.street-sam")
                ],
                SuggestedActions:
                [
                    new AiSuggestedAction("preview-karma", "Preview Karma Spend", "Open a grounded karma preview."),
                    new AiSuggestedAction(AiSuggestedActionIds.BrowseBuildIdeas, "Browse Build Ideas", "Open grounded build ideas for the current plan.")
                ],
                ToolInvocations:
                [
                    new AiToolInvocation(AiToolIds.SearchBuildIdeas, "ok", "Loaded one grounded build idea."),
                    new AiToolInvocation(AiToolIds.SimulateKarmaSpend, "prepared", "Prepared a karma preview action.")
                ],
                FlavorLine: "Clean route, chummer. Here's the build lane with the least noise.",
                StructuredAnswer: new AiStructuredAnswer(
                    Summary: "Raise Automatics first, then preview Improved Reflexes.",
                    Recommendations:
                    [
                        new AiRecommendation("rec-build-1", "Raise Automatics", "That lifts your combat floor immediately.", "Cleaner primary attack pools.")
                    ],
                    Evidence:
                    [
                        new AiEvidenceEntry("Build Idea", "Street Samurai Ladder matches the current build lane.", "idea.build.street-sam", "community")
                    ],
                    Risks:
                    [
                        new AiRiskEntry("warn", "Karma tightness", "Do not jump to ware before the skill floor is fixed.")
                    ],
                    Confidence: "high",
                    RuntimeFingerprint: "sha256:runtime-profile",
                    Sources:
                    [
                        new AiSourceReference("build-idea", "Street Samurai Ladder", "idea.build.street-sam", "community")
                    ],
                    ActionDrafts:
                    [
                        new AiActionDraft("preview_karma_spend", "Preview Karma Spend", "Review the next combat purchase."),
                                new AiActionDraft(AiSuggestedActionIds.BrowseBuildIdeas, "Browse Build Ideas", "Open grounded build ideas for the current plan.")
                            ])),
            "POST");
        SetupJsonResponse(
            context,
            "/api/ai/preview/karma-spend",
            new AiActionPreviewReceipt(
                PreviewId: "preview:karma:build",
                Operation: AiActionPreviewApiOperations.PreviewKarmaSpend,
                PreviewKind: AiActionPreviewKinds.KarmaSpend,
                CharacterId: "char-1",
                CharacterDisplayName: "Cipher (Ghostwire)",
                RulesetId: "sr5",
                RuntimeFingerprint: "sha256:runtime-profile",
                State: AiActionPreviewStates.Scaffolded,
                Summary: "Scoped karma preview for the build lane.",
                StepCount: 1,
                TotalRequested: null,
                Unit: "karma",
                PreparedEffects:
                [
                    "Prepared a non-mutating karma-spend preview for 1 step(s)."
                ],
                Evidence:
                [
                    new AiEvidenceEntry("Runtime", "Uses the grounded build runtime.", "sha256:runtime-profile", "runtime")
                ],
                Risks:
                [
                    new AiRiskEntry("warn", "Scaffolded preview", "Preview receipts remain non-mutating.")
                ]),
            "POST");
        SetupJsonResponse(
            context,
            "/api/ai/conversations/conv.build-1",
            new AiConversationSnapshot(
                ConversationId: "conv.build-1",
                RouteType: AiRouteTypes.Build,
                Messages:
                [
                    new AiConversationMessage("user-1", AiConversationRoles.User, "What should I spend 18 Karma on next?", new DateTimeOffset(2026, 03, 07, 13, 10, 00, TimeSpan.Zero)),
                    new AiConversationMessage("assistant-2", AiConversationRoles.Assistant, "Raise Automatics first, then preview Improved Reflexes.", new DateTimeOffset(2026, 03, 07, 13, 10, 03, TimeSpan.Zero), AiProviderIds.AiMagicx)
                ],
                RuntimeFingerprint: "sha256:runtime-profile",
                CharacterId: "char-1",
                Turns:
                [
                    new AiConversationTurnRecord(
                        TurnId: "turn-build-1",
                        RouteType: AiRouteTypes.Build,
                        ProviderId: AiProviderIds.AiMagicx,
                        CreatedAtUtc: new DateTimeOffset(2026, 03, 07, 13, 10, 03, TimeSpan.Zero),
                        UserMessage: "What should I spend 18 Karma on next?",
                        AssistantAnswer: "Raise Automatics first, then preview Improved Reflexes.",
                        ToolInvocations:
                        [
                            new AiToolInvocation(AiToolIds.SearchBuildIdeas, "ok", "Loaded one grounded build idea."),
                            new AiToolInvocation(AiToolIds.SimulateKarmaSpend, "prepared", "Prepared a karma preview action.")
                        ],
                        Citations:
                        [
                            new AiCitation("build-idea", "Street Samurai Ladder", "idea.build.street-sam")
                        ],
                        StructuredAnswer: new AiStructuredAnswer(
                            Summary: "Raise Automatics first, then preview Improved Reflexes.",
                            Recommendations:
                            [
                                new AiRecommendation("rec-build-1", "Raise Automatics", "That lifts your combat floor immediately.", "Cleaner primary attack pools.")
                            ],
                            Evidence:
                            [
                                new AiEvidenceEntry("Build Idea", "Street Samurai Ladder matches the current build lane.", "idea.build.street-sam", "community")
                            ],
                            Risks:
                            [
                                new AiRiskEntry("warn", "Karma tightness", "Do not jump to ware before the skill floor is fixed.")
                            ],
                            Confidence: "high",
                            RuntimeFingerprint: "sha256:runtime-profile",
                            Sources:
                            [
                                new AiSourceReference("build-idea", "Street Samurai Ladder", "idea.build.street-sam", "community")
                            ],
                            ActionDrafts:
                            [
                                new AiActionDraft("preview_karma_spend", "Preview Karma Spend", "Review the next combat purchase."),
                                new AiActionDraft(AiSuggestedActionIds.BrowseBuildIdeas, "Browse Build Ideas", "Open grounded build ideas for the current plan.")
                            ]),
                        RuntimeFingerprint: "sha256:runtime-profile",
                        CharacterId: "char-1",
                        SuggestedActions:
                        [
                            new AiSuggestedAction("preview-karma", "Preview Karma Spend", "Open a grounded karma preview."),
                            new AiSuggestedAction(AiSuggestedActionIds.BrowseBuildIdeas, "Browse Build Ideas", "Open grounded build ideas for the current plan.")
                        ],
                        FlavorLine: "Clean route, chummer. Here's the build lane with the least noise.",
                        Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 21, 6, 1))
                ]));

        IRenderedComponent<Home> cut = context.Render<Home>();
        NavigationManager navigation = context.Services.GetRequiredService<NavigationManager>();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Coach Route System Prompt"));

        cut.Find("select[data-coach-route='route-select']").Change(AiRouteTypes.Build);
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Build Route System Prompt");
            StringAssert.Contains(cut.Markup, "Street Samurai Ladder");
            StringAssert.Contains(cut.Markup, "conv.build-1");
            StringAssert.Contains(cut.Markup, "AI Magicx");
            Assert.IsFalse(cut.Markup.Contains("1minAI", StringComparison.Ordinal));
            StringAssert.Contains(navigation.Uri, "routeType=build");
        });

        cut.Find("button[data-coach-preview='coach-turn']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "AI Magicx owns grounded build planning.");
            StringAssert.Contains(cut.Markup, "Build plans must cite runtime facts and structured build cards.");
        });

        cut.Find("button[data-coach-send='coach-turn']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Clean route, chummer. Here's the build lane with the least noise.");
            StringAssert.Contains(cut.Markup, "Raise Automatics first, then preview Improved Reflexes.");
            StringAssert.Contains(cut.Markup, "turn-build-1");
            StringAssert.Contains(navigation.Uri, "conversationId=conv.build-1");
            StringAssert.Contains(navigation.Uri, "runtimeFingerprint=sha256%3Aruntime-profile");
            StringAssert.Contains(navigation.Uri, "characterId=char-1");
        });

        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Find("[data-turn-flavor='turn-build-1']").InnerHtml, "Clean route, chummer. Here's the build lane with the least noise.");
            StringAssert.Contains(cut.Find("[data-turn-budget='turn-build-1']").InnerHtml, "Budget: 21 / 400 chummer-ai-units");
            string replayMarkup = cut.Find("[data-turn-structured-answer='turn-build-1']").InnerHtml;
            StringAssert.Contains(replayMarkup, "Summary: Raise Automatics first, then preview Improved Reflexes.");
            StringAssert.Contains(replayMarkup, "Recommendations: Raise Automatics");
            StringAssert.Contains(replayMarkup, "Evidence: Build Idea");
            StringAssert.Contains(replayMarkup, "Risks: Karma tightness");
            StringAssert.Contains(replayMarkup, "Sources: idea.build.street-sam");
        });

        cut.Find("button[data-suggested-action-preview='latest:preview-karma']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Action Preview");
            StringAssert.Contains(cut.Markup, "Scoped karma preview for the build lane.");
        });

        cut.Find("button[data-suggested-action-preview='turn-build-1:preview-karma']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Action Preview");
            StringAssert.Contains(cut.Markup, "Scoped karma preview for the build lane.");
        });

        cut.Find("button[data-suggested-action-build='latest:browse_build_ideas']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.AreEqual("What should I spend 18 Karma on next?", cut.Find("input[placeholder='stealth decker, face mage, drone support...']").GetAttribute("value"));
            StringAssert.Contains(cut.Markup, "Karma Progression Ladder");
        });

        cut.Find("button[data-suggested-action-build='turn-build-1:browse_build_ideas']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.AreEqual("What should I spend 18 Karma on next?", cut.Find("input[placeholder='stealth decker, face mage, drone support...']").GetAttribute("value"));
            StringAssert.Contains(cut.Markup, "Karma Progression Ladder");
        });

        cut.Find("button[data-action-draft-build='turn-build-1:browse_build_ideas']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.AreEqual("What should I spend 18 Karma on next?", cut.Find("input[placeholder='stealth decker, face mage, drone support...']").GetAttribute("value"));
            StringAssert.Contains(cut.Markup, "Karma Progression Ladder");
            StringAssert.Contains(cut.Markup, "Loaded 1 build idea card(s) for 'What should I spend 18 Karma on next?'.");
        });

        cut.Find("button[data-action-draft-build='latest:browse_build_ideas']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.AreEqual("What should I spend 18 Karma on next?", cut.Find("input[placeholder='stealth decker, face mage, drone support...']").GetAttribute("value"));
            StringAssert.Contains(cut.Markup, "Karma Progression Ladder");
        });
    }

    [TestMethod]
    public void Home_surfaces_fallback_credential_binding_for_docs_route_execution()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;
        RegisterCoachHeadServices(context);
        SetupCoachMetadataResponses(context);
        SetupConversationCatalogResponse(context);
        SetupJsonResponse(
            context,
            "/api/ai/prompts?routeType=docs&personaId=decker-contact&maxCount=6",
            new AiPromptCatalog(
            [
                new AiPromptDescriptor(
                    PromptId: "docs-route",
                    PromptKind: AiPromptKinds.RouteSystem,
                    RouteType: AiRouteTypes.Docs,
                    RouteClassId: AiRouteClassIds.CheapChat,
                    PersonaId: AiPersonaIds.DeckerContact,
                    Title: "Docs Route System Prompt",
                    Summary: "Evidence-first docs concierge prompt.",
                    BaseInstructions: ["Cite Chummer-owned docs before prose."],
                    RequiredGroundingSectionIds: [AiGroundingSectionIds.Runtime, AiGroundingSectionIds.Character],
                    RetrievalCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community],
                    AllowedToolIds: [AiToolIds.GetRuntimeSummary, AiToolIds.ExplainValue],
                    EvidenceFirst: true,
                    MinFlavorPercent: 5,
                    MaxFlavorPercent: 15)
            ],
            1));
        SetupJsonResponse(
            context,
            "/api/ai/build-ideas?routeType=docs&queryText=stealth&rulesetId=sr5&maxCount=6",
            new AiBuildIdeaCatalog(
            [
                new BuildIdeaCard(
                    IdeaId: "idea.docs.runtime",
                    RulesetId: "sr5",
                    Title: "Runtime Rules Notes",
                    Summary: "Quick-reference evidence cards for runtime-backed answers.",
                    RoleTags: ["docs", "runtime"],
                    CompatibleProfileIds: ["official.sr5.core"],
                    CoreLoop: "Start from runtime facts, then explain in plain language.",
                    EarlyPriorities: ["Load runtime summary", "Review explain traces"],
                    KarmaMilestones: ["n/a"],
                    Strengths: ["Grounded evidence-first answers"],
                    Weaknesses: ["Requires active runtime context"],
                    TrapChoices: ["Answering from prose alone"],
                    LinkedContentIds: ["runtime-summary"],
                    CommunityScore: 4.9)
            ],
            1));
        SetupJsonResponse(
            context,
            "/api/ai/conversation-audits?routeType=docs&maxCount=6",
            new AiConversationAuditCatalogPage(
            [
                new AiConversationAuditSummary(
                    ConversationId: "conv.docs-1",
                    RouteType: AiRouteTypes.Docs,
                    MessageCount: 1,
                    LastUpdatedAtUtc: new DateTimeOffset(2026, 03, 07, 13, 20, 00, TimeSpan.Zero),
                    RuntimeFingerprint: "sha256:runtime-profile",
                    CharacterId: "char-1",
                    LastAssistantAnswer: "Docs lane ready.",
                    LastProviderId: AiProviderIds.OneMinAi)
            ],
            1));
        SetupJsonResponse(
            context,
            "/api/ai/preview/docs",
            new AiConversationTurnPreview(
                RouteType: AiRouteTypes.Docs,
                RouteDecision: new AiProviderRouteDecision(
                    RouteType: AiRouteTypes.Docs,
                    ProviderId: AiProviderIds.OneMinAi,
                    Reason: "1minAI handles cheap evidence-first docs concierge turns.",
                    BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                    ToolingEnabled: true,
                    CredentialTier: AiProviderCredentialTiers.Fallback,
                    CredentialSlotIndex: 0),
                Grounding: new AiGroundingBundle(
                    RouteType: AiRouteTypes.Docs,
                    RuntimeFingerprint: "sha256:runtime-profile",
                    CharacterId: "char-1",
                    ConversationId: "conv.docs-1",
                    RuntimeFacts: new Dictionary<string, string>
                    {
                        ["profile"] = "official.sr5.core"
                    },
                    CharacterFacts: new Dictionary<string, string>
                    {
                        ["focus"] = "docs concierge"
                    },
                    Constraints: ["Evidence first"],
                    RetrievedItems:
                    [
                        new AiRetrievedItem(AiRetrievalCorpusIds.Community, "idea.docs.runtime", "Runtime Rules Notes", "Quick-reference evidence cards for runtime-backed answers.", "community", "sr5")
                    ],
                    AllowedTools:
                    [
                        new AiToolDescriptor(AiToolIds.GetRuntimeSummary, "Get Runtime Summary", "Load runtime facts."),
                        new AiToolDescriptor(AiToolIds.ExplainValue, "Explain Value", "Resolve grounded Explain API context.")
                    ]),
                Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 22, 6),
                SystemPrompt: "Start from runtime truth, then cite docs-owned summaries.",
                ProviderRequest: new AiProviderTurnPlan(
                    ProviderId: AiProviderIds.OneMinAi,
                    RouteType: AiRouteTypes.Docs,
                    ConversationId: "conv.docs-1",
                    UserMessage: "Why is this availability blocked?",
                    SystemPrompt: "Start from runtime truth, then cite docs-owned summaries.",
                    Stream: false,
                    AttachmentIds: [],
                    RetrievalCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community],
                    AllowedTools:
                    [
                        new AiToolDescriptor(AiToolIds.GetRuntimeSummary, "Get Runtime Summary", "Load runtime facts."),
                        new AiToolDescriptor(AiToolIds.ExplainValue, "Explain Value", "Resolve grounded Explain API context.")
                    ],
                    GroundingSections:
                    [
                        new AiGroundingSection(AiGroundingSectionIds.Runtime, "Runtime", ["official.sr5.core"]),
                        new AiGroundingSection(AiGroundingSectionIds.Character, "Character", ["docs concierge"])
                    ],
                    RouteDecision: new AiProviderRouteDecision(
                        RouteType: AiRouteTypes.Docs,
                        ProviderId: AiProviderIds.OneMinAi,
                        Reason: "1minAI handles cheap evidence-first docs concierge turns.",
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        ToolingEnabled: true,
                        CredentialTier: AiProviderCredentialTiers.Fallback,
                        CredentialSlotIndex: 0),
                    Grounding: new AiGroundingBundle(
                        RouteType: AiRouteTypes.Docs,
                        RuntimeFingerprint: "sha256:runtime-profile",
                        CharacterId: "char-1",
                        ConversationId: "conv.docs-1",
                        RuntimeFacts: new Dictionary<string, string>
                        {
                            ["profile"] = "official.sr5.core"
                        },
                        CharacterFacts: new Dictionary<string, string>
                        {
                            ["focus"] = "docs concierge"
                        },
                        Constraints: ["Evidence first"],
                        RetrievedItems:
                        [
                            new AiRetrievedItem(AiRetrievalCorpusIds.Community, "idea.docs.runtime", "Runtime Rules Notes", "Quick-reference evidence cards for runtime-backed answers.", "community", "sr5")
                        ],
                        AllowedTools:
                        [
                            new AiToolDescriptor(AiToolIds.GetRuntimeSummary, "Get Runtime Summary", "Load runtime facts."),
                            new AiToolDescriptor(AiToolIds.ExplainValue, "Explain Value", "Resolve grounded Explain API context.")
                        ]),
                    Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 22, 6))),
            "POST");
        SetupJsonResponse(
            context,
            "/api/ai/docs/query",
            new AiConversationTurnResponse(
                ConversationId: "conv.docs-1",
                RouteType: AiRouteTypes.Docs,
                ProviderId: AiProviderIds.OneMinAi,
                Answer: "The active runtime blocks that availability because the threshold stays above your current legality band.",
                RouteDecision: new AiProviderRouteDecision(
                    RouteType: AiRouteTypes.Docs,
                    ProviderId: AiProviderIds.OneMinAi,
                    Reason: "1minAI handles cheap evidence-first docs concierge turns.",
                    BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                    ToolingEnabled: true,
                    CredentialTier: AiProviderCredentialTiers.Fallback,
                    CredentialSlotIndex: 0),
                Grounding: new AiGroundingBundle(
                    RouteType: AiRouteTypes.Docs,
                    RuntimeFingerprint: "sha256:runtime-profile",
                    CharacterId: "char-1",
                    ConversationId: "conv.docs-1",
                    RuntimeFacts: new Dictionary<string, string>
                    {
                        ["profile"] = "official.sr5.core"
                    },
                    CharacterFacts: new Dictionary<string, string>
                    {
                        ["focus"] = "docs concierge"
                    },
                    Constraints: ["Evidence first"],
                    RetrievedItems:
                    [
                        new AiRetrievedItem(AiRetrievalCorpusIds.Community, "idea.docs.runtime", "Runtime Rules Notes", "Quick-reference evidence cards for runtime-backed answers.", "community", "sr5")
                    ],
                    AllowedTools:
                    [
                        new AiToolDescriptor(AiToolIds.GetRuntimeSummary, "Get Runtime Summary", "Load runtime facts."),
                        new AiToolDescriptor(AiToolIds.ExplainValue, "Explain Value", "Resolve grounded Explain API context.")
                    ]),
                Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 23, 6),
                Citations:
                [
                    new AiCitation("runtime", "official.sr5.core", "runtime-summary")
                ],
                SuggestedActions:
                [
                    new AiSuggestedAction("open-runtime", "Open Runtime Inspector", "Review the active runtime summary.")
                ],
                ToolInvocations:
                [
                    new AiToolInvocation(AiToolIds.GetRuntimeSummary, "ok", "Loaded runtime facts."),
                    new AiToolInvocation(AiToolIds.ExplainValue, "prepared", "Prepared an explain lookup.")
                ],
                FlavorLine: "Signal's clean. Here's the grounded readout from your runtime.",
                StructuredAnswer: new AiStructuredAnswer(
                    Summary: "Availability stays blocked by the current legality threshold.",
                    Recommendations:
                    [
                        new AiRecommendation("rec-docs-1", "Open Runtime Inspector", "The runtime summary shows the exact threshold source.", "You can verify the block without guessing.")
                    ],
                    Evidence:
                    [
                        new AiEvidenceEntry("Runtime", "The current profile keeps the legality threshold above your band.", "runtime-summary", "runtime")
                    ],
                    Risks:
                    [
                        new AiRiskEntry("warn", "Docs drift", "Do not answer from prose alone when runtime context is available.")
                    ],
                    Confidence: "high",
                    RuntimeFingerprint: "sha256:runtime-profile",
                    Sources:
                    [
                        new AiSourceReference("runtime", "official.sr5.core", "runtime-summary", "runtime")
                    ],
                    ActionDrafts:
                    [
                        new AiActionDraft("open_runtime_inspector", "Open Runtime Inspector", "Inspect the active runtime threshold.")
                    ])),
            "POST");
        SetupJsonResponse(
            context,
            "/api/ai/runtime/sha256%3Aruntime-profile/summary?rulesetId=sr5",
            new AiRuntimeSummaryProjection(
                RuntimeFingerprint: "sha256:runtime-profile",
                RulesetId: "sr5",
                Title: "Street-Level Runtime Lock",
                CatalogKind: "saved",
                EngineApiVersion: "1.0.0",
                ContentBundles: ["official.sr5.core@1.0.0"],
                RulePacks: ["campaign.street-level@2.0.0"],
                ProviderBindings: new Dictionary<string, string>
                {
                    ["availability.item"] = "official.sr5.core:availability.item"
                }));
        SetupJsonResponse(
            context,
            "/api/ai/conversations/conv.docs-1",
            new AiConversationSnapshot(
                ConversationId: "conv.docs-1",
                RouteType: AiRouteTypes.Docs,
                Messages:
                [
                    new AiConversationMessage("user-1", AiConversationRoles.User, "Why is this availability blocked?", new DateTimeOffset(2026, 03, 07, 13, 20, 01, TimeSpan.Zero)),
                    new AiConversationMessage("assistant-2", AiConversationRoles.Assistant, "The active runtime blocks that availability because the threshold stays above your current legality band.", new DateTimeOffset(2026, 03, 07, 13, 20, 04, TimeSpan.Zero), AiProviderIds.OneMinAi)
                ],
                RuntimeFingerprint: "sha256:runtime-profile",
                CharacterId: "char-1",
                Turns:
                [
                    new AiConversationTurnRecord(
                        TurnId: "turn-docs-1",
                        RouteType: AiRouteTypes.Docs,
                        ProviderId: AiProviderIds.OneMinAi,
                        CreatedAtUtc: new DateTimeOffset(2026, 03, 07, 13, 20, 04, TimeSpan.Zero),
                        UserMessage: "Why is this availability blocked?",
                        AssistantAnswer: "The active runtime blocks that availability because the threshold stays above your current legality band.",
                        ToolInvocations:
                        [
                            new AiToolInvocation(AiToolIds.GetRuntimeSummary, "ok", "Loaded runtime facts."),
                            new AiToolInvocation(AiToolIds.ExplainValue, "prepared", "Prepared an explain lookup.")
                        ],
                        Citations:
                        [
                            new AiCitation("runtime", "official.sr5.core", "runtime-summary")
                        ],
                        StructuredAnswer: new AiStructuredAnswer(
                            Summary: "Availability stays blocked by the current legality threshold.",
                            Recommendations:
                            [
                                new AiRecommendation("rec-docs-1", "Open Runtime Inspector", "The runtime summary shows the exact threshold source.", "You can verify the block without guessing.")
                            ],
                            Evidence:
                            [
                                new AiEvidenceEntry("Runtime", "The current profile keeps the legality threshold above your band.", "runtime-summary", "runtime")
                            ],
                            Risks:
                            [
                                new AiRiskEntry("warn", "Docs drift", "Do not answer from prose alone when runtime context is available.")
                            ],
                            Confidence: "high",
                            RuntimeFingerprint: "sha256:runtime-profile",
                            Sources:
                            [
                                new AiSourceReference("runtime", "official.sr5.core", "runtime-summary", "runtime")
                            ],
                            ActionDrafts:
                            [
                                new AiActionDraft("open_runtime_inspector", "Open Runtime Inspector", "Inspect the active runtime threshold.")
                            ]),
                        RuntimeFingerprint: "sha256:runtime-profile",
                        CharacterId: "char-1",
                        SuggestedActions:
                        [
                            new AiSuggestedAction("open-runtime", "Open Runtime Inspector", "Review the active runtime summary.")
                        ],
                        FlavorLine: "Signal's clean. Here's the grounded readout from your runtime.",
                        Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 23, 6, 1))
                ]));

        IRenderedComponent<Home> cut = context.Render<Home>();
        NavigationManager navigation = context.Services.GetRequiredService<NavigationManager>();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Chummer Coach"));

        cut.Find("select[data-coach-route='route-select']").Change(AiRouteTypes.Docs);
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Docs Route System Prompt");
            StringAssert.Contains(cut.Markup, "Runtime Rules Notes");
            StringAssert.Contains(cut.Markup, "conv.docs-1");
            StringAssert.Contains(cut.Markup, "1minAI");
            Assert.IsFalse(cut.Markup.Contains("AI Magicx", StringComparison.Ordinal));
            StringAssert.Contains(cut.Markup, "recent timeout");
            StringAssert.Contains(navigation.Uri, "routeType=docs");
        });

        cut.Find("button[data-coach-preview='coach-turn']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "1minAI handles cheap evidence-first docs concierge turns.");
            StringAssert.Contains(cut.Markup, "fallback / slot 0");
        });

        cut.Find("button[data-coach-send='coach-turn']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Signal's clean. Here's the grounded readout from your runtime.");
            StringAssert.Contains(cut.Markup, "fallback / slot 0");
            StringAssert.Contains(cut.Markup, "turn-docs-1");
            StringAssert.Contains(navigation.Uri, "conversationId=conv.docs-1");
            StringAssert.Contains(navigation.Uri, "runtimeFingerprint=sha256%3Aruntime-profile");
            StringAssert.Contains(navigation.Uri, "characterId=char-1");
        });

        cut.Find("button[data-suggested-action-runtime='latest:open-runtime']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Runtime Summary");
            StringAssert.Contains(cut.Markup, "Street-Level Runtime Lock");
            StringAssert.Contains(cut.Markup, "availability.item=official.sr5.core:availability.item");
        });

        cut.Find("button[data-suggested-action-runtime='turn-docs-1:open-runtime']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Runtime Summary");
            StringAssert.Contains(cut.Markup, "Street-Level Runtime Lock");
            StringAssert.Contains(cut.Markup, "availability.item=official.sr5.core:availability.item");
        });

        cut.Find("button[data-action-draft-runtime='turn-docs-1:open_runtime_inspector']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Runtime Summary");
            StringAssert.Contains(cut.Markup, "Street-Level Runtime Lock");
            StringAssert.Contains(cut.Markup, "availability.item=official.sr5.core:availability.item");
        });

        cut.Find("button[data-action-draft-runtime='latest:open_runtime_inspector']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Runtime Summary");
            StringAssert.Contains(cut.Markup, "Street-Level Runtime Lock");
            StringAssert.Contains(cut.Markup, "availability.item=official.sr5.core:availability.item");
        });
    }

    [TestMethod]
    public void Home_surfaces_gateway_errors_when_status_request_fails()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;
        RegisterCoachHeadServices(context);
        SetupFailureResponse(context, "/api/ai/status", "Coach gateway unavailable.");

        IRenderedComponent<Home> cut = context.Render<Home>();

        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Coach gateway unavailable.");
            StringAssert.Contains(cut.Markup, "error");
        });
    }

    private static void RegisterCoachHeadServices(BunitContext context)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        BrowserCoachApiClient client = new(context.JSInterop.JSRuntime, configuration);
        context.Services.AddSingleton(client);
    }

    private static void SetupCoachMetadataResponses(BunitContext context)
    {
        SetupJsonResponse(
            context,
            "/api/ai/status",
            new AiGatewayStatusProjection(
                Status: "ready",
                Routes: [AiRouteTypes.Chat, AiRouteTypes.Coach, AiRouteTypes.Build, AiRouteTypes.Docs, AiRouteTypes.Recap],
                Providers:
                [
                    new AiProviderDescriptor(
                        ProviderId: AiProviderIds.AiMagicx,
                        DisplayName: "AI Magicx",
                        SupportsToolCalling: true,
                        SupportsStreaming: true,
                        SupportsAttachments: true,
                        SupportsConversationMemory: true,
                        AllowedRouteTypes: [AiRouteTypes.Coach, AiRouteTypes.Build],
                        AdapterKind: AiProviderAdapterKinds.RemoteHttp,
                        LiveExecutionEnabled: true,
                        AdapterRegistered: true,
                        IsConfigured: true,
                        PrimaryCredentialCount: 1,
                        FallbackCredentialCount: 0,
                        TransportBaseUrlConfigured: true,
                        TransportModelConfigured: true,
                        TransportMetadataConfigured: true),
                    new AiProviderDescriptor(
                        ProviderId: AiProviderIds.OneMinAi,
                        DisplayName: "1minAI",
                        SupportsToolCalling: false,
                        SupportsStreaming: true,
                        SupportsAttachments: true,
                        SupportsConversationMemory: true,
                        AllowedRouteTypes: [AiRouteTypes.Chat, AiRouteTypes.Docs, AiRouteTypes.Recap],
                        AdapterKind: AiProviderAdapterKinds.RemoteHttp,
                        LiveExecutionEnabled: true,
                        AdapterRegistered: true,
                        IsConfigured: true,
                        PrimaryCredentialCount: 1,
                        FallbackCredentialCount: 1,
                        TransportBaseUrlConfigured: true,
                        TransportModelConfigured: true,
                        TransportMetadataConfigured: true)
                ],
                Tools:
                [
                    new AiToolDescriptor(AiToolIds.ExplainValue, "Explain Value", "Resolve grounded Explain API context."),
                    new AiToolDescriptor(AiToolIds.SearchBuildIdeas, "Search Build Ideas", "Search structured build idea cards.")
                ],
                RoutePolicies:
                [
                    new AiRoutePolicyDescriptor(
                        RouteType: AiRouteTypes.Coach,
                        PrimaryProviderId: AiProviderIds.AiMagicx,
                        FallbackProviderIds: [AiProviderIds.OneMinAi],
                        RetrievalCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Private, AiRetrievalCorpusIds.Community],
                        AllowedTools:
                        [
                            new AiToolDescriptor(AiToolIds.ExplainValue, "Explain Value", "Resolve grounded Explain API context."),
                            new AiToolDescriptor(AiToolIds.SearchBuildIdeas, "Search Build Ideas", "Search structured build idea cards.")
                        ],
                        ToolingEnabled: true,
                        StreamingPreferred: true,
                        CacheByRuntimeFingerprint: true,
                        RouteClassId: AiRouteClassIds.GroundedRulesChat,
                        PersonaId: AiPersonaIds.DeckerContact)
                    ,
                    new AiRoutePolicyDescriptor(
                        RouteType: AiRouteTypes.Docs,
                        PrimaryProviderId: AiProviderIds.OneMinAi,
                        FallbackProviderIds: [AiProviderIds.AiMagicx],
                        RetrievalCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community],
                        AllowedTools:
                        [
                            new AiToolDescriptor(AiToolIds.GetRuntimeSummary, "Get Runtime Summary", "Load runtime facts."),
                            new AiToolDescriptor(AiToolIds.ExplainValue, "Explain Value", "Resolve grounded Explain API context.")
                        ],
                        ToolingEnabled: true,
                        StreamingPreferred: false,
                        CacheByRuntimeFingerprint: true,
                        RouteClassId: AiRouteClassIds.CheapChat,
                        PersonaId: AiPersonaIds.DeckerContact)
                ],
                RouteBudgets:
                [
                    new AiRouteBudgetPolicyDescriptor(
                        RouteType: AiRouteTypes.Coach,
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        MonthlyAllowance: 400,
                        BurstLimitPerMinute: 6,
                        Notes: "Grounded coach lane."),
                    new AiRouteBudgetPolicyDescriptor(
                        RouteType: AiRouteTypes.Docs,
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        MonthlyAllowance: 400,
                        BurstLimitPerMinute: 6,
                        Notes: "Evidence-first docs concierge lane.")
                ],
                RetrievalCorpora:
                [
                    new AiRetrievalCorpusDescriptor(AiRetrievalCorpusIds.Runtime, "Runtime", "runtime"),
                    new AiRetrievalCorpusDescriptor(AiRetrievalCorpusIds.Community, "Community", "public")
                ],
                Budget: new AiBudgetSnapshot(
                    BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                    MonthlyAllowance: 400,
                    MonthlyConsumed: 12,
                    BurstLimitPerMinute: 6,
                    CurrentBurstConsumed: 3),
                PromptPolicy: "evidence-first decker-contact",
                SupportsStreaming: true,
                Personas:
                [
                    new AiPersonaDescriptor(
                        PersonaId: AiPersonaIds.DeckerContact,
                        DisplayName: "Decker Contact",
                        Summary: "Street-level decker contact with evidence-first replies.",
                        EvidenceFirst: true,
                        MinFlavorPercent: 5,
                        MaxFlavorPercent: 15)
                ],
                DefaultPersonaId: AiPersonaIds.DeckerContact,
                RouteBudgetStatuses:
                [
                    new AiRouteBudgetStatusProjection(
                        RouteType: AiRouteTypes.Coach,
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        MonthlyAllowance: 400,
                        MonthlyConsumed: 12,
                        MonthlyRemaining: 388,
                        BurstLimitPerMinute: 6,
                        CurrentBurstConsumed: 2,
                        BurstRemaining: 4,
                        Notes: "Grounded coach lane."),
                    new AiRouteBudgetStatusProjection(
                        RouteType: AiRouteTypes.Docs,
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        MonthlyAllowance: 400,
                        MonthlyConsumed: 3,
                        MonthlyRemaining: 397,
                        BurstLimitPerMinute: 6,
                        CurrentBurstConsumed: 1,
                        BurstRemaining: 5,
                        Notes: "Evidence-first docs lane.")
                ]));
        SetupJsonResponse(
            context,
            "/api/ai/provider-health?routeType=coach",
            new[]
            {
                new AiProviderHealthProjection(
                    ProviderId: AiProviderIds.AiMagicx,
                    DisplayName: "AI Magicx",
                    AdapterKind: AiProviderAdapterKinds.RemoteHttp,
                    AdapterRegistered: true,
                    LiveExecutionEnabled: true,
                    AllowedRouteTypes: new[] { AiRouteTypes.Coach, AiRouteTypes.Build },
                    CircuitState: AiProviderCircuitStates.Closed,
                    ConsecutiveFailureCount: 0,
                    LastSuccessAtUtc: new DateTimeOffset(2026, 03, 07, 11, 10, 00, TimeSpan.Zero),
                    LastFailureAtUtc: null,
                    LastFailureMessage: null,
                    LastRouteType: AiRouteTypes.Coach,
                    LastCredentialTier: AiProviderCredentialTiers.Primary,
                    LastCredentialSlotIndex: 0,
                    IsRoutable: true)
            });
        SetupJsonResponse(
            context,
            "/api/ai/provider-health?routeType=build",
            new[]
            {
                new AiProviderHealthProjection(
                    ProviderId: AiProviderIds.AiMagicx,
                    DisplayName: "AI Magicx",
                    AdapterKind: AiProviderAdapterKinds.RemoteHttp,
                    AdapterRegistered: true,
                    LiveExecutionEnabled: true,
                    AllowedRouteTypes: new[] { AiRouteTypes.Coach, AiRouteTypes.Build },
                    CircuitState: AiProviderCircuitStates.Closed,
                    ConsecutiveFailureCount: 0,
                    LastSuccessAtUtc: new DateTimeOffset(2026, 03, 07, 11, 10, 00, TimeSpan.Zero),
                    LastFailureAtUtc: null,
                    LastFailureMessage: null,
                    LastRouteType: AiRouteTypes.Build,
                    LastCredentialTier: AiProviderCredentialTiers.Primary,
                    LastCredentialSlotIndex: 0,
                    IsRoutable: true)
            });
        SetupJsonResponse(
            context,
            "/api/ai/provider-health?routeType=docs",
            new[]
            {
                new AiProviderHealthProjection(
                    ProviderId: AiProviderIds.OneMinAi,
                    DisplayName: "1minAI",
                    AdapterKind: AiProviderAdapterKinds.RemoteHttp,
                    AdapterRegistered: true,
                    LiveExecutionEnabled: true,
                    AllowedRouteTypes: new[] { AiRouteTypes.Chat, AiRouteTypes.Docs, AiRouteTypes.Recap },
                    CircuitState: AiProviderCircuitStates.Degraded,
                    ConsecutiveFailureCount: 1,
                    LastSuccessAtUtc: new DateTimeOffset(2026, 03, 07, 11, 05, 00, TimeSpan.Zero),
                    LastFailureAtUtc: new DateTimeOffset(2026, 03, 07, 11, 08, 00, TimeSpan.Zero),
                    LastFailureMessage: "recent timeout",
                    LastRouteType: AiRouteTypes.Docs,
                    LastCredentialTier: AiProviderCredentialTiers.Fallback,
                    LastCredentialSlotIndex: 0,
                    IsRoutable: true)
            });
        SetupJsonResponse(
            context,
            "/api/ai/prompts?routeType=coach&personaId=decker-contact&maxCount=6",
            new AiPromptCatalog(
                [
                    new AiPromptDescriptor(
                        PromptId: AiRouteTypes.Coach,
                        PromptKind: AiPromptKinds.RouteSystem,
                        RouteType: AiRouteTypes.Coach,
                        RouteClassId: AiRouteClassIds.GroundedRulesChat,
                        PersonaId: AiPersonaIds.DeckerContact,
                        Title: "Coach Route System Prompt",
                        Summary: "Grounded decker-contact coaching prompt.",
                        BaseInstructions: ["Prefer runtime truth first."],
                        RequiredGroundingSectionIds: [AiGroundingSectionIds.Runtime, AiGroundingSectionIds.Character],
                        RetrievalCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community],
                        AllowedToolIds: [AiToolIds.ExplainValue, AiToolIds.SearchBuildIdeas],
                        EvidenceFirst: true,
                        MinFlavorPercent: 5,
                        MaxFlavorPercent: 15)
                ],
                1));
        SetupJsonResponse(
            context,
            "/api/ai/build-ideas?routeType=coach&queryText=stealth&rulesetId=sr5&maxCount=6",
            new AiBuildIdeaCatalog(
                [
                    new BuildIdeaCard(
                        IdeaId: "idea.stealth.decker",
                        RulesetId: "sr5",
                        Title: "Silent Infiltrator",
                        Summary: "Stealth-first decker with disciplined overwatch control.",
                        RoleTags: ["decker", "stealth"],
                        CompatibleProfileIds: ["official.sr5.core"],
                        CoreLoop: "Breach quietly, stay unseen, and keep the team moving.",
                        EarlyPriorities: ["Sneaking 6", "Cracking 6"],
                        KarmaMilestones: ["Buy Codeslinger", "Raise Logic", "Add Quiet Runner support"],
                        Strengths: ["Low-profile matrix support"],
                        Weaknesses: ["Tight nuyen budget"],
                        TrapChoices: ["Overspending on hot-sim toys early"],
                        LinkedContentIds: ["quality.codeslinger"],
                        CommunityScore: 4.8)
                ],
                1));
    }

    private static void SetupConversationCatalogResponse(BunitContext context, params AiConversationSnapshot[] items)
        => SetupJsonResponse(
            context,
            "/api/ai/conversation-audits?routeType=coach&maxCount=6",
            new AiConversationAuditCatalogPage(items.Select(CreateAuditSummary).ToArray(), items.Length));

    private static AiConversationAuditSummary CreateAuditSummary(AiConversationSnapshot conversation)
    {
        AiConversationTurnRecord? lastTurn = conversation.Turns?.Count > 0
            ? conversation.Turns[^1]
            : null;
        AiConversationMessage? lastAssistantMessage = null;
        if (conversation.Messages.Count > 0)
        {
            for (int index = conversation.Messages.Count - 1; index >= 0; index--)
            {
                AiConversationMessage candidate = conversation.Messages[index];
                if (string.Equals(candidate.Role, AiConversationRoles.Assistant, StringComparison.Ordinal))
                {
                    lastAssistantMessage = candidate;
                    break;
                }
            }
        }

        AiConversationMessage? lastMessage = conversation.Messages.Count > 0
            ? conversation.Messages[^1]
            : null;

        return new AiConversationAuditSummary(
            ConversationId: conversation.ConversationId,
            RouteType: conversation.RouteType,
            MessageCount: conversation.Messages.Count,
            LastUpdatedAtUtc: lastTurn?.CreatedAtUtc ?? lastMessage?.CreatedAtUtc,
            RuntimeFingerprint: conversation.RuntimeFingerprint,
            CharacterId: conversation.CharacterId,
            LastAssistantAnswer: lastTurn?.AssistantAnswer ?? lastAssistantMessage?.Content ?? lastMessage?.Content,
            LastProviderId: lastTurn?.ProviderId ?? lastAssistantMessage?.ProviderId,
            Cache: lastTurn?.Cache,
            RouteDecision: lastTurn?.RouteDecision,
            GroundingCoverage: lastTurn?.GroundingCoverage,
            WorkspaceId: lastTurn?.WorkspaceId ?? conversation.WorkspaceId,
            FlavorLine: lastTurn?.FlavorLine,
            Budget: lastTurn?.Budget,
            StructuredAnswer: lastTurn?.StructuredAnswer);
    }

    private static void SetupJsonResponse<T>(BunitContext context, string path, T payload, string method = "GET")
    {
        context.JSInterop
            .Setup<string>(
                "chummerCoachApi.send",
                invocation => invocation.Arguments.Count >= 2
                    && string.Equals(invocation.Arguments[0]?.ToString(), path, StringComparison.Ordinal)
                    && string.Equals(invocation.Arguments[1]?.ToString(), method, StringComparison.Ordinal))
            .SetResult(CreateEnvelope(200, JsonSerializer.Serialize(payload, JsonOptions)));
    }

    private static void SetupFailureResponse(BunitContext context, string path, string message, string method = "GET")
    {
        context.JSInterop
            .Setup<string>(
                "chummerCoachApi.send",
                invocation => invocation.Arguments.Count >= 2
                    && string.Equals(invocation.Arguments[0]?.ToString(), path, StringComparison.Ordinal)
                    && string.Equals(invocation.Arguments[1]?.ToString(), method, StringComparison.Ordinal))
            .SetResult(CreateEnvelope(500, JsonSerializer.Serialize(new { message }, JsonOptions)));
    }

    private static void SetupQuotaExceededResponse(BunitContext context, string path, AiQuotaExceededReceipt receipt, string method = "POST")
    {
        context.JSInterop
            .Setup<string>(
                "chummerCoachApi.send",
                invocation => invocation.Arguments.Count >= 2
                    && string.Equals(invocation.Arguments[0]?.ToString(), path, StringComparison.Ordinal)
                    && string.Equals(invocation.Arguments[1]?.ToString(), method, StringComparison.Ordinal))
            .SetResult(CreateEnvelope(429, JsonSerializer.Serialize(receipt, JsonOptions)));
    }

    private static string CreateEnvelope(int status, string text)
        => JsonSerializer.Serialize(new
        {
            status,
            text
        }, JsonOptions);
}
