#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Chummer.Contracts.AI;
using Chummer.Contracts.Content;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using Chummer.Session.Web;
using Chummer.Session.Web.Components.Pages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BunitContext = Bunit.BunitContext;

namespace Chummer.Tests.Presentation;

[TestClass]
public sealed class SessionWebComponentTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void Home_renders_live_session_catalog_runtime_state_and_rulepack_inventory()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;

        FakeSessionOfflineCacheService cache = new()
        {
            StorageQuota = new ClientStorageQuotaEstimate(
                UsageBytes: 8_192,
                QuotaBytes: 65_536,
                IndexedDbAvailable: true,
                OpfsAvailable: true,
                PersistenceSupported: true,
                IsPersistent: true,
                CapturedAtUtc: new DateTimeOffset(2026, 03, 07, 10, 00, 00, TimeSpan.Zero))
        };

        RegisterSessionHeadServices(context, cache);
        SetupCoachSidecarResponses(context, "char-1", "sha256:runtime-live");
        SetupJsonResponse(
            context,
            "/api/session/characters",
            new SessionCharacterCatalog(
                [
                    new SessionCharacterListItem("char-1", "Neon Ghost", "sr5", "sha256:runtime-live")
                ]));
        SetupJsonResponse(
            context,
            "/api/session/profiles",
            new SessionProfileCatalog(
                [
                    new SessionProfileListItem("profile.street", "Street Session", "sr5", "sha256:runtime-live", "stable", true, "street")
                ],
                ActiveProfileId: "profile.street"));
        SetupJsonResponse(
            context,
            "/api/session/rulepacks",
            new RulePackCatalog(
                [
                    CreateRulePackManifest("pack.alpha", "Alpha Pack", "1.0.0")
                ]));
        SetupJsonResponse(
            context,
            "/api/session/characters/char-1/runtime-state",
            new SessionRuntimeStatusProjection(
                CharacterId: "char-1",
                SelectionState: SessionRuntimeSelectionStates.Selected,
                ProfileId: "profile.street",
                ProfileTitle: "Street Session",
                RulesetId: "sr5",
                RuntimeFingerprint: "sha256:runtime-live",
                SessionReady: true,
                BundleFreshness: SessionRuntimeBundleFreshnessStates.Current,
                BundleId: "bundle-live",
                BundleDeliveryMode: SessionRuntimeBundleDeliveryModes.Inline,
                BundleTrustState: SessionRuntimeBundleTrustStates.Trusted,
                BundleSignedAtUtc: new DateTimeOffset(2026, 03, 07, 9, 00, 00, TimeSpan.Zero),
                BundleExpiresAtUtc: new DateTimeOffset(2026, 03, 08, 9, 00, 00, TimeSpan.Zero),
                RequiresBundleRefresh: false));
        SetupJsonResponse(
            context,
            "/api/session/characters/char-1/runtime-bundle",
            new SessionRuntimeBundleIssueReceipt(
                Outcome: SessionRuntimeBundleIssueOutcomes.Issued,
                Bundle: new SessionRuntimeBundle(
                    BundleId: "bundle-live",
                    BaseCharacterVersion: new CharacterVersionReference("char-1", "ver-1", "sr5", "sha256:runtime-live"),
                    EngineApiVersion: "1.0.0",
                    SignedAtUtc: new DateTimeOffset(2026, 03, 07, 9, 00, 00, TimeSpan.Zero),
                    Signature: "sig-1",
                    QuickActions: [],
                    Trackers: [],
                    ReducerBindings: new Dictionary<string, string>()),
                SignatureEnvelope: new SessionRuntimeBundleSignatureEnvelope(
                    BundleId: "bundle-live",
                    KeyId: "key-1",
                    Signature: "sig-1",
                    SignedAtUtc: new DateTimeOffset(2026, 03, 07, 9, 00, 00, TimeSpan.Zero),
                    ExpiresAtUtc: new DateTimeOffset(2026, 03, 08, 9, 00, 00, TimeSpan.Zero)),
                DeliveryMode: SessionRuntimeBundleDeliveryModes.Inline,
                Diagnostics: [new SessionRuntimeBundleTrustDiagnostic(SessionRuntimeBundleTrustStates.Trusted, "Trusted bundle")]));

        IRenderedComponent<Home> cut = context.Render<Home>();

        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Street Session");
            StringAssert.Contains(cut.Markup, "sha256:runtime-live");
            StringAssert.Contains(cut.Markup, "bundle-live");
            StringAssert.Contains(cut.Markup, "Alpha Pack");
            StringAssert.Contains(cut.Markup, "IndexedDB");
            StringAssert.Contains(cut.Markup, "OPFS");
            StringAssert.Contains(cut.Markup, "persistent");
            StringAssert.Contains(cut.Markup, "Coach Sidecar");
            StringAssert.Contains(cut.Markup, "Grounded Guidance");
            StringAssert.Contains(cut.Markup, "AI Magicx");
            StringAssert.Contains(cut.Markup, "Transport: ready · base yes · model yes · keys primary 1 / fallback 0 · route coach · binding primary / slot 0");
            StringAssert.Contains(cut.Markup, "Recent Coach Guidance");
            StringAssert.Contains(cut.Markup, "cache hit");
            StringAssert.Contains(cut.Markup, "Keep your ammo ledger honest, chummer. Then line up the next spend.");
            StringAssert.Contains(cut.Markup, "Budget snapshot: 24 / 400 chummer-ai-units");
            StringAssert.Contains(cut.Markup, "Structured summary: Keep the session overlay current, then preview advancement against the pinned runtime.");
            StringAssert.Contains(cut.Markup, "Recommendations: 1 · Preview Sneaking spend");
            StringAssert.Contains(cut.Markup, "Evidence: 1 · Session tracker state");
            StringAssert.Contains(cut.Markup, "Risks: 1 · Sync before apply");
            StringAssert.Contains(cut.Markup, "Sources: 1 sources / 1 action drafts");
            StringAssert.Contains(cut.Markup, "data-testid=\"open-session-coach\"");
            StringAssert.Contains(cut.Markup, "data-testid=\"session-coach-provider-transport\"");
            StringAssert.Contains(cut.Markup, "data-testid=\"open-session-coach-thread\"");
            StringAssert.Contains(cut.Markup, "/coach/?routeType=coach&amp;conversationId=conv.session-coach-1&amp;runtimeFingerprint=sha256%3Aruntime-live&amp;characterId=char-1&amp;rulesetId=sr5");
            StringAssert.Contains(cut.Markup, "/coach/?routeType=coach&amp;runtimeFingerprint=sha256%3Aruntime-live&amp;characterId=char-1&amp;rulesetId=sr5");
        });

        cut.FindAll("button").Single(button => button.TextContent.Contains("Issue Runtime Bundle", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "offline:char-1");
            StringAssert.Contains(cut.Markup, "Ledger Events");
            StringAssert.Contains(cut.Markup, "Replica Pending Ops");
            Assert.IsTrue(cache.CachedLedgers.ContainsKey("offline:char-1"));
            Assert.IsTrue(cache.CachedReplicaStates.ContainsKey("offline:char-1"));
        });

        Assert.IsNotNull(cache.CachedCharacterCatalog);
        Assert.IsNotNull(cache.CachedProfileCatalog);
        Assert.IsNotNull(cache.CachedRulePackCatalog);
        Assert.IsNotNull(cache.CachedRuntimeStates["char-1"]);
    }

    [TestMethod]
    public void Home_queues_local_overlay_mutations_into_browser_cache_before_sync_routes_exist()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;

        FakeSessionOfflineCacheService cache = new()
        {
            StorageQuota = new ClientStorageQuotaEstimate(
                UsageBytes: 10_240,
                QuotaBytes: 65_536,
                IndexedDbAvailable: true,
                OpfsAvailable: false,
                PersistenceSupported: true,
                IsPersistent: true,
                CapturedAtUtc: new DateTimeOffset(2026, 03, 07, 12, 00, 00, TimeSpan.Zero))
        };

        RegisterSessionHeadServices(context, cache);
        SetupCoachSidecarResponses(context, "char-1", "sha256:runtime-live");
        SetupJsonResponse(
            context,
            "/api/session/characters",
            new SessionCharacterCatalog(
                [
                    new SessionCharacterListItem("char-1", "Neon Ghost", "sr5", "sha256:runtime-live")
                ]));
        SetupJsonResponse(
            context,
            "/api/session/profiles",
            new SessionProfileCatalog(
                [
                    new SessionProfileListItem("profile.street", "Street Session", "sr5", "sha256:runtime-live", "stable", true, "street")
                ],
                ActiveProfileId: "profile.street"));
        SetupJsonResponse(
            context,
            "/api/session/rulepacks",
            new RulePackCatalog(
                [
                    CreateRulePackManifest("pack.alpha", "Alpha Pack", "1.0.0")
                ]));
        SetupJsonResponse(
            context,
            "/api/session/characters/char-1/runtime-state",
            new SessionRuntimeStatusProjection(
                CharacterId: "char-1",
                SelectionState: SessionRuntimeSelectionStates.Selected,
                ProfileId: "profile.street",
                ProfileTitle: "Street Session",
                RulesetId: "sr5",
                RuntimeFingerprint: "sha256:runtime-live",
                SessionReady: true,
                BundleFreshness: SessionRuntimeBundleFreshnessStates.Current,
                BundleId: "bundle-live",
                BundleDeliveryMode: SessionRuntimeBundleDeliveryModes.Inline,
                BundleTrustState: SessionRuntimeBundleTrustStates.Trusted,
                BundleSignedAtUtc: new DateTimeOffset(2026, 03, 07, 9, 00, 00, TimeSpan.Zero),
                BundleExpiresAtUtc: new DateTimeOffset(2026, 03, 08, 9, 00, 00, TimeSpan.Zero),
                RequiresBundleRefresh: false));
        SetupJsonResponse(
            context,
            "/api/session/characters/char-1/runtime-bundle",
            new SessionRuntimeBundleIssueReceipt(
                Outcome: SessionRuntimeBundleIssueOutcomes.Issued,
                Bundle: new SessionRuntimeBundle(
                    BundleId: "bundle-live",
                    BaseCharacterVersion: new CharacterVersionReference("char-1", "ver-1", "sr5", "sha256:runtime-live"),
                    EngineApiVersion: "1.0.0",
                    SignedAtUtc: new DateTimeOffset(2026, 03, 07, 9, 00, 00, TimeSpan.Zero),
                    Signature: "sig-1",
                    QuickActions: [],
                    Trackers: [],
                    ReducerBindings: new Dictionary<string, string>()),
                SignatureEnvelope: new SessionRuntimeBundleSignatureEnvelope(
                    BundleId: "bundle-live",
                    KeyId: "key-1",
                    Signature: "sig-1",
                    SignedAtUtc: new DateTimeOffset(2026, 03, 07, 9, 00, 00, TimeSpan.Zero),
                    ExpiresAtUtc: new DateTimeOffset(2026, 03, 08, 9, 00, 00, TimeSpan.Zero)),
                DeliveryMode: SessionRuntimeBundleDeliveryModes.Inline,
                Diagnostics: [new SessionRuntimeBundleTrustDiagnostic(SessionRuntimeBundleTrustStates.Trusted, "Trusted bundle")]));

        IRenderedComponent<Home> cut = context.Render<Home>();

        cut.Find("[data-testid='issue-runtime-bundle']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.IsTrue(cache.CachedLedgers.ContainsKey("offline:char-1"));
            Assert.IsTrue(cache.CachedReplicaStates.ContainsKey("offline:char-1"));
        });

        cut.Find("[data-testid='tracker-key']").Input("stun");
        cut.Find("[data-testid='queue-tracker-increment']").Click();
        cut.Find("[data-testid='note-text']").Input("Spent Edge at the table.");
        cut.Find("[data-testid='queue-note']").Click();
        cut.Find("[data-testid='pin-action-id']").Input("action.reload");
        cut.Find("[data-testid='pin-action-label']").Input("Reload");
        cut.Find("[data-testid='pin-action-capability']").Input("session.quick-actions");
        cut.Find("[data-testid='queue-pin']").Click();

        cut.WaitForAssertion(() =>
        {
            SessionLedger ledger = cache.CachedLedgers["offline:char-1"].Payload;
            SessionReplicaState replicaState = cache.CachedReplicaStates["offline:char-1"].Payload;

            Assert.HasCount(3, ledger.Events);
            Assert.AreEqual(3L, ledger.NextSequence);
            Assert.AreEqual(3, replicaState.PendingOperationCount);
            Assert.AreEqual(SessionEventTypes.TrackerIncrement, ledger.Events[0].EventType);
            Assert.AreEqual(SessionEventTypes.NoteAppend, ledger.Events[1].EventType);
            Assert.AreEqual(SessionEventTypes.QuickActionPin, ledger.Events[2].EventType);
            StringAssert.Contains(cut.Markup, "Local Overlay Actions");
            StringAssert.Contains(cut.Markup, "Recent Local Events");
            StringAssert.Contains(cut.Markup, "tracker.increment");
            StringAssert.Contains(cut.Markup, "note.append");
            StringAssert.Contains(cut.Markup, "quickaction.pin");
            StringAssert.Contains(cut.Markup, "tracker:stun");
            StringAssert.Contains(cut.Markup, "pins");
        });
    }

    [TestMethod]
    public void Home_falls_back_to_indexeddb_cache_when_live_session_requests_fail()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;

        FakeSessionOfflineCacheService cache = new()
        {
            StorageQuota = new ClientStorageQuotaEstimate(
                UsageBytes: 4_096,
                QuotaBytes: 65_536,
                IndexedDbAvailable: true,
                OpfsAvailable: false,
                PersistenceSupported: true,
                IsPersistent: false,
                CapturedAtUtc: new DateTimeOffset(2026, 03, 07, 11, 00, 00, TimeSpan.Zero)),
            CachedCharacterCatalog = new CachedClientPayload<SessionCharacterCatalog>(
                SessionClientCacheAreas.CharacterCatalog,
                SessionClientCacheKeys.Global,
                new SessionCharacterCatalog(
                    [
                        new SessionCharacterListItem("char-1", "Cached Ghost", "sr6", "sha256:runtime-cached")
                    ]),
                new DateTimeOffset(2026, 03, 07, 8, 30, 00, TimeSpan.Zero)),
            CachedProfileCatalog = new CachedClientPayload<SessionProfileCatalog>(
                SessionClientCacheAreas.ProfileCatalog,
                SessionClientCacheKeys.Global,
                new SessionProfileCatalog(
                    [
                        new SessionProfileListItem("profile.cached", "Cached Session", "sr6", "sha256:runtime-cached", "beta", true, "cached")
                    ],
                    ActiveProfileId: "profile.cached"),
                new DateTimeOffset(2026, 03, 07, 8, 31, 00, TimeSpan.Zero)),
            CachedRulePackCatalog = new CachedClientPayload<RulePackCatalog>(
                SessionClientCacheAreas.RulePackCatalog,
                SessionClientCacheKeys.Global,
                new RulePackCatalog(
                    [
                        CreateRulePackManifest("pack.cached", "Cached Pack", "2.0.0")
                    ]),
                new DateTimeOffset(2026, 03, 07, 8, 32, 00, TimeSpan.Zero))
        };
        cache.CachedRuntimeStates["char-1"] = new CachedClientPayload<SessionRuntimeStatusProjection>(
            SessionClientCacheAreas.RuntimeState,
            "char-1",
            new SessionRuntimeStatusProjection(
                CharacterId: "char-1",
                SelectionState: SessionRuntimeSelectionStates.Selected,
                ProfileId: "profile.cached",
                ProfileTitle: "Cached Session",
                RulesetId: "sr6",
                RuntimeFingerprint: "sha256:runtime-cached",
                SessionReady: true,
                BundleFreshness: SessionRuntimeBundleFreshnessStates.RefreshRequired,
                BundleId: "bundle-cached",
                BundleDeliveryMode: SessionRuntimeBundleDeliveryModes.Cached,
                BundleTrustState: SessionRuntimeBundleTrustStates.ExpiringSoon,
                BundleSignedAtUtc: new DateTimeOffset(2026, 03, 06, 12, 00, 00, TimeSpan.Zero),
                BundleExpiresAtUtc: new DateTimeOffset(2026, 03, 07, 12, 00, 00, TimeSpan.Zero),
                RequiresBundleRefresh: true),
            new DateTimeOffset(2026, 03, 07, 8, 33, 00, TimeSpan.Zero));

        RegisterSessionHeadServices(context, cache);
        SetupCoachSidecarResponses(context, "char-1", "sha256:runtime-cached");
        SetupFailureResponse(context, "/api/session/characters", "Live character catalog unavailable.");
        SetupFailureResponse(context, "/api/session/profiles", "Live profile catalog unavailable.");
        SetupFailureResponse(context, "/api/session/rulepacks", "Live RulePack inventory unavailable.");
        SetupFailureResponse(context, "/api/session/characters/char-1/runtime-state", "Live runtime state unavailable.");

        IRenderedComponent<Home> cut = context.Render<Home>();

        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Cached Ghost");
            StringAssert.Contains(cut.Markup, "Cached Session");
            StringAssert.Contains(cut.Markup, "Cached Pack");
            StringAssert.Contains(cut.Markup, "bundle-cached");
            StringAssert.Contains(cut.Markup, "Showing IndexedDB cache from");
            StringAssert.Contains(cut.Markup, "best-effort");
        });
    }

    private static void RegisterSessionHeadServices(BunitContext context, ISessionOfflineCacheService cache)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        BrowserSessionApiClient client = new(context.JSInterop.JSRuntime, configuration);
        BrowserSessionCoachApiClient coachClient = new(context.JSInterop.JSRuntime, configuration);

        context.Services.AddSingleton(client);
        context.Services.AddSingleton(coachClient);
        context.Services.AddSingleton(cache);
    }

    private static void SetupCoachSidecarResponses(BunitContext context, string characterId, string runtimeFingerprint)
    {
        SetupJsonResponse(
            context,
            "/api/ai/status",
            new AiGatewayStatusProjection(
                Status: "scaffolded",
                Routes: [AiRouteTypes.Chat, AiRouteTypes.Coach, AiRouteTypes.Build, AiRouteTypes.Docs, AiRouteTypes.Recap],
                Providers:
                [
                    new AiProviderDescriptor(
                        ProviderId: AiProviderIds.AiMagicx,
                        DisplayName: "AI Magicx",
                        SupportsToolCalling: true,
                        SupportsStreaming: true,
                        SupportsAttachments: false,
                        SupportsConversationMemory: true,
                        AllowedRouteTypes: [AiRouteTypes.Coach, AiRouteTypes.Build],
                        AdapterKind: AiProviderAdapterKinds.RemoteHttp,
                        LiveExecutionEnabled: true,
                        AdapterRegistered: true,
                        IsConfigured: true,
                        PrimaryCredentialCount: 1,
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
                        FallbackCredentialCount: 1,
                        TransportBaseUrlConfigured: true,
                        TransportModelConfigured: false,
                        TransportMetadataConfigured: false)
                ],
                Tools: [],
                RoutePolicies: [],
                RouteBudgets:
                [
                    new AiRouteBudgetPolicyDescriptor(
                        RouteType: AiRouteTypes.Coach,
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        MonthlyAllowance: 400,
                        BurstLimitPerMinute: 6,
                        Notes: "Coach route policy")
                ],
                RetrievalCorpora: [],
                Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 24, 6),
                PromptPolicy: "decker-contact evidence-first",
                RouteBudgetStatuses:
                [
                    new AiRouteBudgetStatusProjection(
                        RouteType: AiRouteTypes.Coach,
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        MonthlyAllowance: 400,
                        MonthlyConsumed: 24,
                        MonthlyRemaining: 376,
                        BurstLimitPerMinute: 6,
                        CurrentBurstConsumed: 1,
                        BurstRemaining: 5,
                        Notes: "Coach route budget")
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
                    AllowedRouteTypes: [AiRouteTypes.Coach, AiRouteTypes.Build],
                    CircuitState: AiProviderCircuitStates.Closed,
                    LastRouteType: AiRouteTypes.Coach,
                    LastCredentialTier: AiProviderCredentialTiers.Primary,
                    LastCredentialSlotIndex: 0,
                    IsConfigured: true,
                    PrimaryCredentialCount: 1,
                    FallbackCredentialCount: 0,
                    TransportBaseUrlConfigured: true,
                    TransportModelConfigured: true,
                    TransportMetadataConfigured: true,
                    LastSuccessAtUtc: new DateTimeOffset(2026, 03, 07, 12, 15, 00, TimeSpan.Zero)),
                new AiProviderHealthProjection(
                    ProviderId: AiProviderIds.OneMinAi,
                    DisplayName: "1minAI",
                    AdapterKind: AiProviderAdapterKinds.RemoteHttp,
                    AdapterRegistered: true,
                    LiveExecutionEnabled: true,
                    AllowedRouteTypes: [AiRouteTypes.Chat, AiRouteTypes.Docs, AiRouteTypes.Recap],
                    CircuitState: AiProviderCircuitStates.Degraded,
                    ConsecutiveFailureCount: 1,
                    LastRouteType: AiRouteTypes.Docs,
                    LastCredentialTier: AiProviderCredentialTiers.Fallback,
                    LastCredentialSlotIndex: 0,
                    IsConfigured: true,
                    PrimaryCredentialCount: 1,
                    FallbackCredentialCount: 1,
                    TransportBaseUrlConfigured: true,
                    TransportModelConfigured: true,
                    TransportMetadataConfigured: true,
                    LastSuccessAtUtc: new DateTimeOffset(2026, 03, 07, 12, 10, 00, TimeSpan.Zero),
                    LastFailureAtUtc: new DateTimeOffset(2026, 03, 07, 12, 14, 00, TimeSpan.Zero),
                    LastFailureMessage: "recent timeout")
            });
        SetupJsonResponse(
            context,
            $"/api/ai/conversation-audits?routeType=coach&characterId={Uri.EscapeDataString(characterId)}&runtimeFingerprint={Uri.EscapeDataString(runtimeFingerprint)}&maxCount=3",
            new AiConversationAuditCatalogPage(
            [
                new AiConversationAuditSummary(
                    ConversationId: "conv.session-coach-1",
                    RouteType: AiRouteTypes.Coach,
                    MessageCount: 3,
                    LastUpdatedAtUtc: new DateTimeOffset(2026, 03, 07, 12, 16, 00, TimeSpan.Zero),
                    RuntimeFingerprint: runtimeFingerprint,
                    CharacterId: characterId,
                    LastAssistantAnswer: "Keep the ammo tracker honest, then spend Karma on Sneaking.",
                    LastProviderId: AiProviderIds.AiMagicx,
                    Cache: new AiCacheMetadata(
                        Status: AiCacheStatuses.Hit,
                        CacheKey: "cache::session::coach",
                        CachedAtUtc: new DateTimeOffset(2026, 03, 07, 12, 15, 30, TimeSpan.Zero),
                        NormalizedPrompt: "what next",
                        RuntimeFingerprint: runtimeFingerprint,
                        CharacterId: characterId),
                    RouteDecision: new AiProviderRouteDecision(
                        RouteType: AiRouteTypes.Coach,
                        ProviderId: AiProviderIds.AiMagicx,
                        Reason: "Grounded coach route stayed on AI Magicx.",
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        ToolingEnabled: true,
                        CredentialTier: AiProviderCredentialTiers.Primary,
                        CredentialSlotIndex: 0),
                    GroundingCoverage: new AiGroundingCoverage(
                        ScorePercent: 100,
                        Summary: "runtime, character, and community evidence present.",
                        PresentSignals: ["runtime", "character", "retrieved"],
                        MissingSignals: [],
                        RetrievedCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community]),
                    FlavorLine: "Keep your ammo ledger honest, chummer. Then line up the next spend.",
                    Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 24, 6, CurrentBurstConsumed: 1),
                    StructuredAnswer: new AiStructuredAnswer(
                        Summary: "Keep the session overlay current, then preview advancement against the pinned runtime.",
                        Recommendations:
                        [
                            new AiRecommendation(
                                RecommendationId: "rec.session.preview-sneaking",
                                Title: "Preview Sneaking spend",
                                Reason: "The active session context points to stealth pressure.",
                                ExpectedEffect: "Non-mutating preview shows downstream dice-pool changes.")
                        ],
                        Evidence:
                        [
                            new AiEvidenceEntry(
                                Title: "Session tracker state",
                                Summary: "Damage and ammo deltas are already recorded in the local-first overlay.")
                        ],
                        Risks:
                        [
                            new AiRiskEntry(
                                Severity: "info",
                                Title: "Sync before apply",
                                Summary: "Unsynced overlay changes can skew follow-up planning if ignored.")
                        ],
                        Confidence: "high",
                        RuntimeFingerprint: runtimeFingerprint,
                        Sources:
                        [
                            new AiSourceReference(
                                Kind: "session",
                                Title: "Current session runtime",
                                ReferenceId: runtimeFingerprint,
                                Source: "session")
                        ],
                        ActionDrafts:
                        [
                            new AiActionDraft(
                                ActionId: AiSuggestedActionIds.PreviewKarmaSpend,
                                Title: "Preview Karma Spend",
                                Description: "Inspect the guarded Karma preview before you sync.",
                                RuntimeFingerprint: runtimeFingerprint,
                                CharacterId: characterId)
                        ]))
            ],
            1));
    }

    private static void SetupJsonResponse<T>(BunitContext context, string path, T payload, string method = "GET")
    {
        context.JSInterop
            .Setup<string>(
                "chummerSessionApi.send",
                invocation => invocation.Arguments.Count >= 2
                    && string.Equals(invocation.Arguments[0]?.ToString(), path, StringComparison.Ordinal)
                    && string.Equals(invocation.Arguments[1]?.ToString(), method, StringComparison.Ordinal))
            .SetResult(CreateEnvelope(200, JsonSerializer.Serialize(payload, JsonOptions)));
    }

    private static void SetupFailureResponse(BunitContext context, string path, string message, string method = "GET")
    {
        context.JSInterop
            .Setup<string>(
                "chummerSessionApi.send",
                invocation => invocation.Arguments.Count >= 2
                    && string.Equals(invocation.Arguments[0]?.ToString(), path, StringComparison.Ordinal)
                    && string.Equals(invocation.Arguments[1]?.ToString(), method, StringComparison.Ordinal))
            .SetResult(CreateEnvelope(500, JsonSerializer.Serialize(new { message }, JsonOptions)));
    }

    private static string CreateEnvelope(int status, string text)
        => JsonSerializer.Serialize(new
        {
            status,
            text
        }, JsonOptions);

    private static RulePackManifest CreateRulePackManifest(string packId, string title, string version)
    {
        return new RulePackManifest(
            PackId: packId,
            Version: version,
            Title: title,
            Author: "agent",
            Description: $"{title} description.",
            Targets: ["sr5"],
            EngineApiVersion: "1.0.0",
            DependsOn: [],
            ConflictsWith: [],
            Visibility: ArtifactVisibilityModes.Private,
            TrustTier: ArtifactTrustTiers.Private,
            Assets: [],
            Capabilities:
            [
                new RulePackCapabilityDescriptor("session.quick-actions", RulePackAssetKinds.Lua, RulePackAssetModes.WrapProvider, Explainable: true, SessionSafe: true)
            ],
            ExecutionPolicies: []);
    }

    private sealed class FakeSessionOfflineCacheService : ISessionOfflineCacheService
    {
        public CachedClientPayload<SessionCharacterCatalog>? CachedCharacterCatalog { get; set; }

        public CachedClientPayload<SessionProfileCatalog>? CachedProfileCatalog { get; set; }

        public CachedClientPayload<RulePackCatalog>? CachedRulePackCatalog { get; set; }

        public Dictionary<string, CachedClientPayload<SessionRuntimeStatusProjection>> CachedRuntimeStates { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, CachedClientPayload<SessionRuntimeBundleIssueReceipt>> CachedRuntimeBundles { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, CachedClientPayload<SessionLedger>> CachedLedgers { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, CachedClientPayload<SessionReplicaState>> CachedReplicaStates { get; } = new(StringComparer.Ordinal);

        public ClientStorageQuotaEstimate StorageQuota { get; set; } = new(
            UsageBytes: 0,
            QuotaBytes: 0,
            IndexedDbAvailable: true,
            OpfsAvailable: false,
            PersistenceSupported: true,
            IsPersistent: false,
            CapturedAtUtc: DateTimeOffset.UtcNow);

        public Task<CachedClientPayload<SessionCharacterCatalog>?> GetCharacterCatalogAsync(CancellationToken ct = default)
            => Task.FromResult(CachedCharacterCatalog);

        public Task<CachedClientPayload<SessionCharacterCatalog>> CacheCharacterCatalogAsync(SessionCharacterCatalog catalog, CancellationToken ct = default)
            => Task.FromResult(CachedCharacterCatalog = new CachedClientPayload<SessionCharacterCatalog>(
                SessionClientCacheAreas.CharacterCatalog,
                SessionClientCacheKeys.Global,
                catalog,
                DateTimeOffset.UtcNow));

        public Task<CachedClientPayload<SessionProfileCatalog>?> GetProfileCatalogAsync(CancellationToken ct = default)
            => Task.FromResult(CachedProfileCatalog);

        public Task<CachedClientPayload<SessionProfileCatalog>> CacheProfileCatalogAsync(SessionProfileCatalog catalog, CancellationToken ct = default)
            => Task.FromResult(CachedProfileCatalog = new CachedClientPayload<SessionProfileCatalog>(
                SessionClientCacheAreas.ProfileCatalog,
                SessionClientCacheKeys.Global,
                catalog,
                DateTimeOffset.UtcNow));

        public Task<CachedClientPayload<RulePackCatalog>?> GetRulePackCatalogAsync(CancellationToken ct = default)
            => Task.FromResult(CachedRulePackCatalog);

        public Task<CachedClientPayload<RulePackCatalog>> CacheRulePackCatalogAsync(RulePackCatalog catalog, CancellationToken ct = default)
            => Task.FromResult(CachedRulePackCatalog = new CachedClientPayload<RulePackCatalog>(
                SessionClientCacheAreas.RulePackCatalog,
                SessionClientCacheKeys.Global,
                catalog,
                DateTimeOffset.UtcNow));

        public Task<CachedClientPayload<SessionRuntimeStatusProjection>?> GetRuntimeStateAsync(string characterId, CancellationToken ct = default)
        {
            CachedRuntimeStates.TryGetValue(characterId.Trim(), out CachedClientPayload<SessionRuntimeStatusProjection>? payload);
            return Task.FromResult(payload);
        }

        public Task<CachedClientPayload<SessionRuntimeStatusProjection>> CacheRuntimeStateAsync(
            string characterId,
            SessionRuntimeStatusProjection runtimeState,
            CancellationToken ct = default)
        {
            CachedClientPayload<SessionRuntimeStatusProjection> payload = new(
                SessionClientCacheAreas.RuntimeState,
                characterId.Trim(),
                runtimeState,
                DateTimeOffset.UtcNow);
            CachedRuntimeStates[characterId.Trim()] = payload;
            return Task.FromResult(payload);
        }

        public Task<CachedClientPayload<SessionRuntimeBundleIssueReceipt>?> GetRuntimeBundleAsync(string characterId, CancellationToken ct = default)
        {
            CachedRuntimeBundles.TryGetValue(characterId.Trim(), out CachedClientPayload<SessionRuntimeBundleIssueReceipt>? payload);
            return Task.FromResult(payload);
        }

        public Task<CachedClientPayload<SessionRuntimeBundleIssueReceipt>> CacheRuntimeBundleAsync(
            string characterId,
            SessionRuntimeBundleIssueReceipt receipt,
            CancellationToken ct = default)
        {
            CachedClientPayload<SessionRuntimeBundleIssueReceipt> payload = new(
                SessionClientCacheAreas.RuntimeBundle,
                characterId.Trim(),
                receipt,
                DateTimeOffset.UtcNow);
            CachedRuntimeBundles[characterId.Trim()] = payload;
            return Task.FromResult(payload);
        }

        public Task<CachedClientPayload<SessionLedger>?> GetLedgerAsync(string overlayId, CancellationToken ct = default)
        {
            CachedLedgers.TryGetValue(overlayId.Trim(), out CachedClientPayload<SessionLedger>? payload);
            return Task.FromResult(payload);
        }

        public Task<CachedClientPayload<SessionLedger>> CacheLedgerAsync(SessionLedger ledger, CancellationToken ct = default)
        {
            CachedClientPayload<SessionLedger> payload = new(
                SessionClientCacheAreas.Ledger,
                ledger.OverlayId.Trim(),
                ledger,
                DateTimeOffset.UtcNow);
            CachedLedgers[ledger.OverlayId.Trim()] = payload;
            return Task.FromResult(payload);
        }

        public Task RemoveLedgerAsync(string overlayId, CancellationToken ct = default)
        {
            CachedLedgers.Remove(overlayId.Trim());
            return Task.CompletedTask;
        }

        public Task<CachedClientPayload<SessionReplicaState>?> GetReplicaStateAsync(string overlayId, CancellationToken ct = default)
        {
            CachedReplicaStates.TryGetValue(overlayId.Trim(), out CachedClientPayload<SessionReplicaState>? payload);
            return Task.FromResult(payload);
        }

        public Task<CachedClientPayload<SessionReplicaState>> CacheReplicaStateAsync(SessionReplicaState state, CancellationToken ct = default)
        {
            CachedClientPayload<SessionReplicaState> payload = new(
                SessionClientCacheAreas.Replica,
                state.OverlayId.Trim(),
                state,
                DateTimeOffset.UtcNow);
            CachedReplicaStates[state.OverlayId.Trim()] = payload;
            return Task.FromResult(payload);
        }

        public Task RemoveReplicaStateAsync(string overlayId, CancellationToken ct = default)
        {
            CachedReplicaStates.Remove(overlayId.Trim());
            return Task.CompletedTask;
        }

        public Task<ClientStorageQuotaEstimate> GetStorageQuotaAsync(CancellationToken ct = default)
            => Task.FromResult(StorageQuota);
    }
}
