#nullable enable annotations

using System;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Blazor;
using Chummer.Contracts.AI;

namespace Chummer.Tests.Presentation;

internal sealed class FakeWorkbenchCoachApiClient : IWorkbenchCoachApiClient
{
    public int StatusCalls { get; private set; }
    public int ProviderHealthCalls { get; private set; }
    public int AuditCalls { get; private set; }
    public string? LastAuditRouteType { get; private set; }
    public string? LastRuntimeFingerprint { get; private set; }
    public int LastMaxCount { get; private set; }

    public WorkbenchCoachApiCallResult<AiGatewayStatusProjection> StatusResult { get; set; } = WorkbenchCoachApiCallResult<AiGatewayStatusProjection>.Success(200, CreateStatus());
    public WorkbenchCoachApiCallResult<AiProviderHealthProjection[]> ProviderHealthResult { get; set; } = WorkbenchCoachApiCallResult<AiProviderHealthProjection[]>.Success(200, CreateProviderHealth());
    public Func<string?, WorkbenchCoachApiCallResult<AiConversationAuditCatalogPage>> AuditResultFactory { get; set; }
        = runtimeFingerprint => WorkbenchCoachApiCallResult<AiConversationAuditCatalogPage>.Success(200, CreateAuditCatalog(runtimeFingerprint));

    public Task<WorkbenchCoachApiCallResult<AiGatewayStatusProjection>> GetStatusAsync(CancellationToken ct = default)
    {
        StatusCalls++;
        return Task.FromResult(StatusResult);
    }

    public Task<WorkbenchCoachApiCallResult<AiProviderHealthProjection[]>> ListProviderHealthAsync(string? routeType = null, CancellationToken ct = default)
    {
        ProviderHealthCalls++;
        return Task.FromResult(ProviderHealthResult);
    }

    public Task<WorkbenchCoachApiCallResult<AiConversationAuditCatalogPage>> ListConversationAuditsAsync(
        string routeType,
        string? runtimeFingerprint = null,
        int maxCount = 3,
        CancellationToken ct = default)
    {
        AuditCalls++;
        LastAuditRouteType = routeType;
        LastRuntimeFingerprint = runtimeFingerprint;
        LastMaxCount = maxCount;
        return Task.FromResult(AuditResultFactory(runtimeFingerprint));
    }

    public static FakeWorkbenchCoachApiClient CreateDefault()
        => new();

    public static FakeWorkbenchCoachApiClient CreateDefault(string runtimeFingerprint)
        => new()
        {
            AuditResultFactory = requestedFingerprint => WorkbenchCoachApiCallResult<AiConversationAuditCatalogPage>.Success(
                200,
                CreateAuditCatalog(requestedFingerprint ?? runtimeFingerprint))
        };

    private static AiGatewayStatusProjection CreateStatus()
        => new(
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
                    TransportMetadataConfigured: true)
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
            Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 18, 6),
            PromptPolicy: "decker-contact evidence-first",
            RouteBudgetStatuses:
            [
                new AiRouteBudgetStatusProjection(
                    RouteType: AiRouteTypes.Coach,
                    BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                    MonthlyAllowance: 400,
                    MonthlyConsumed: 18,
                    MonthlyRemaining: 382,
                    BurstLimitPerMinute: 6,
                    CurrentBurstConsumed: 1,
                    BurstRemaining: 5,
                    Notes: "Coach route budget")
            ]);

    private static AiProviderHealthProjection[] CreateProviderHealth()
        =>
        [
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
                LastSuccessAtUtc: new DateTimeOffset(2026, 03, 07, 15, 20, 00, TimeSpan.Zero))
        ];

    private static AiConversationAuditCatalogPage CreateAuditCatalog(string? runtimeFingerprint)
        => new(
            [
                new AiConversationAuditSummary(
                    ConversationId: "conv.workbench-coach-1",
                    RouteType: AiRouteTypes.Coach,
                    MessageCount: 2,
                    LastUpdatedAtUtc: new DateTimeOffset(2026, 03, 07, 15, 25, 00, TimeSpan.Zero),
                    RuntimeFingerprint: runtimeFingerprint ?? "sha256:runtime-profile",
                    LastAssistantAnswer: "Keep the active runtime pinned before previewing Karma spend.",
                    LastProviderId: AiProviderIds.AiMagicx,
                    Cache: new AiCacheMetadata(
                        Status: AiCacheStatuses.Hit,
                        CacheKey: "cache::workbench::coach",
                        CachedAtUtc: new DateTimeOffset(2026, 03, 07, 15, 24, 00, TimeSpan.Zero),
                        NormalizedPrompt: "what should i spend next",
                        RuntimeFingerprint: runtimeFingerprint),
                    RouteDecision: new AiProviderRouteDecision(
                        RouteType: AiRouteTypes.Coach,
                        ProviderId: AiProviderIds.AiMagicx,
                        Reason: "Workbench coaching stayed on the grounded route.",
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        ToolingEnabled: true,
                        CredentialTier: AiProviderCredentialTiers.Primary,
                        CredentialSlotIndex: 0),
                    GroundingCoverage: new AiGroundingCoverage(
                        ScorePercent: 100,
                        Summary: "runtime and retrieved guidance are present.",
                        PresentSignals: ["runtime", "retrieved"],
                        MissingSignals: [],
                        RetrievedCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community]),
                    FlavorLine: "Signal's clean. Keep the deck on the grounded lane.",
                    Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 18, 6, CurrentBurstConsumed: 1),
                    StructuredAnswer: new AiStructuredAnswer(
                        Summary: "Preview Karma against the pinned runtime before you commit advancement changes.",
                        Recommendations:
                        [
                            new AiRecommendation(
                                RecommendationId: "rec.workbench.preview-karma",
                                Title: "Preview Karma spend",
                                Reason: "The active runtime already exposes the guarded preview lane.",
                                ExpectedEffect: "Shows non-mutating advancement deltas before apply.")
                        ],
                        Evidence:
                        [
                            new AiEvidenceEntry(
                                Title: "Pinned runtime",
                                Summary: "The current workspace is already bound to the active runtime fingerprint.")
                        ],
                        Risks:
                        [
                            new AiRiskEntry(
                                Severity: "info",
                                Title: "Preview first",
                                Summary: "Skipping preview can hide downstream validation deltas.")
                        ],
                        Confidence: "high",
                        RuntimeFingerprint: runtimeFingerprint ?? "sha256:runtime-profile",
                        Sources:
                        [
                            new AiSourceReference(
                                Kind: "runtime",
                                Title: "Active runtime",
                                ReferenceId: runtimeFingerprint ?? "sha256:runtime-profile",
                                Source: "runtime")
                        ],
                        ActionDrafts:
                        [
                            new AiActionDraft(
                                ActionId: AiSuggestedActionIds.PreviewKarmaSpend,
                                Title: "Preview Karma Spend",
                                Description: "Open the non-mutating preview lane.",
                                RuntimeFingerprint: runtimeFingerprint ?? "sha256:runtime-profile")
                        ]))
            ],
            1);
}
