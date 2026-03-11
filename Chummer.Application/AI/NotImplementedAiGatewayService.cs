using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class NotImplementedAiGatewayService : IAiGatewayService
{
    private readonly IAiProviderCredentialCatalog? _providerCredentialCatalog;
    private readonly IAiProviderCatalog _providerCatalog;
    private readonly IAiProviderCredentialSelector? _providerCredentialSelector;
    private readonly IAiProviderRouter _providerRouter;
    private readonly IAiRouteBudgetPolicyCatalog _routeBudgetPolicyCatalog;
    private readonly IAiUsageLedgerStore _usageLedgerStore;
    private readonly IAiProviderHealthStore _providerHealthStore;
    private readonly IAiBudgetService _budgetService;
    private readonly IRetrievalService _retrievalService;
    private readonly IPromptAssembler _promptAssembler;
    private readonly IAiResponseCacheStore _responseCacheStore;
    private readonly IConversationStore _conversationStore;

    public NotImplementedAiGatewayService(
        IAiProviderCredentialCatalog? providerCredentialCatalog = null,
        IAiProviderCatalog? providerCatalog = null,
        IAiProviderCredentialSelector? providerCredentialSelector = null,
        IAiProviderRouter? providerRouter = null,
        IAiRouteBudgetPolicyCatalog? routeBudgetPolicyCatalog = null,
        IAiUsageLedgerStore? usageLedgerStore = null,
        IAiProviderHealthStore? providerHealthStore = null,
        IAiBudgetService? budgetService = null,
        IRetrievalService? retrievalService = null,
        IPromptAssembler? promptAssembler = null,
        IAiResponseCacheStore? responseCacheStore = null,
        IConversationStore? conversationStore = null)
    {
        _providerCredentialCatalog = providerCredentialCatalog;
        _providerCatalog = providerCatalog ?? new DefaultAiProviderCatalog();
        _providerCredentialSelector = providerCredentialSelector ?? (providerCredentialCatalog is null ? null : new RoundRobinAiProviderCredentialSelector(providerCredentialCatalog));
        _providerRouter = providerRouter ?? new DefaultAiProviderRouter(providerCredentialCatalog, _providerCatalog);
        _routeBudgetPolicyCatalog = routeBudgetPolicyCatalog ?? new DefaultAiRouteBudgetPolicyCatalog();
        _usageLedgerStore = usageLedgerStore ?? new InMemoryAiUsageLedgerStore();
        _providerHealthStore = providerHealthStore ?? new InMemoryAiProviderHealthStore();
        _budgetService = budgetService ?? new DefaultAiBudgetService(_routeBudgetPolicyCatalog, _usageLedgerStore);
        _retrievalService = retrievalService ?? new DefaultRetrievalService();
        _promptAssembler = promptAssembler ?? new DefaultPromptAssembler();
        _responseCacheStore = responseCacheStore ?? new InMemoryAiResponseCacheStore();
        _conversationStore = conversationStore ?? new InMemoryConversationStore();
    }

    public AiApiResult<AiGatewayStatusProjection> GetStatus(OwnerScope owner)
    {
        return AiApiResult<AiGatewayStatusProjection>.Implemented(CreateStatusProjection(owner));
    }

    public AiApiResult<IReadOnlyList<AiProviderDescriptor>> ListProviders(OwnerScope owner)
        => AiApiResult<IReadOnlyList<AiProviderDescriptor>>.Implemented(
            CreateStatusProjection(owner).Providers);

    public AiApiResult<IReadOnlyList<AiProviderHealthProjection>> ListProviderHealth(OwnerScope owner)
        => AiApiResult<IReadOnlyList<AiProviderHealthProjection>>.Implemented(
            CreateProviderHealthProjection(owner));

    public AiApiResult<AiConversationCatalogPage> ListConversations(OwnerScope owner, AiConversationCatalogQuery? query = null)
        => AiApiResult<AiConversationCatalogPage>.Implemented(
            _conversationStore.List(owner, query ?? new AiConversationCatalogQuery()));

    public AiApiResult<AiConversationAuditCatalogPage> ListConversationAudits(OwnerScope owner, AiConversationCatalogQuery? query = null)
    {
        AiConversationCatalogPage catalog = _conversationStore.List(owner, query ?? new AiConversationCatalogQuery());
        AiConversationAuditSummary[] items = catalog.Items
            .Select(static conversation =>
            {
                AiConversationTurnRecord? lastTurn = conversation.Turns?.LastOrDefault();
                DateTimeOffset? lastUpdatedAtUtc = lastTurn?.CreatedAtUtc
                    ?? conversation.Messages.LastOrDefault()?.CreatedAtUtc;
                return new AiConversationAuditSummary(
                    ConversationId: conversation.ConversationId,
                    RouteType: conversation.RouteType,
                    MessageCount: conversation.Messages.Count,
                    LastUpdatedAtUtc: lastUpdatedAtUtc,
                    RuntimeFingerprint: conversation.RuntimeFingerprint,
                    CharacterId: conversation.CharacterId,
                    LastAssistantAnswer: conversation.Messages.LastOrDefault(static message => string.Equals(message.Role, AiConversationRoles.Assistant, StringComparison.Ordinal))?.Content,
                    LastProviderId: lastTurn?.ProviderId,
                    Cache: lastTurn?.Cache,
                    RouteDecision: lastTurn?.RouteDecision,
                    GroundingCoverage: lastTurn?.GroundingCoverage,
                    WorkspaceId: lastTurn?.WorkspaceId ?? conversation.WorkspaceId,
                    FlavorLine: lastTurn?.FlavorLine,
                    Budget: lastTurn?.Budget,
                    StructuredAnswer: lastTurn?.StructuredAnswer);
            })
            .ToArray();

        return AiApiResult<AiConversationAuditCatalogPage>.Implemented(
            new AiConversationAuditCatalogPage(items, catalog.TotalCount));
    }

    public AiApiResult<IReadOnlyList<AiToolDescriptor>> ListTools(OwnerScope owner)
        => AiApiResult<IReadOnlyList<AiToolDescriptor>>.Implemented(
            AiGatewayDefaults.CreateStatus(
                _providerCredentialCatalog?.GetConfiguredCredentialCounts(),
                _providerCatalog.ListProviders()).Tools);

    public AiApiResult<IReadOnlyList<AiRetrievalCorpusDescriptor>> ListRetrievalCorpora(OwnerScope owner)
        => AiApiResult<IReadOnlyList<AiRetrievalCorpusDescriptor>>.Implemented(
            AiGatewayDefaults.CreateStatus(
                _providerCredentialCatalog?.GetConfiguredCredentialCounts(),
                _providerCatalog.ListProviders()).RetrievalCorpora);

    public AiApiResult<IReadOnlyList<AiRoutePolicyDescriptor>> ListRoutePolicies(OwnerScope owner)
        => AiApiResult<IReadOnlyList<AiRoutePolicyDescriptor>>.Implemented(AiGatewayDefaults.CreateRoutePolicies());

    public AiApiResult<IReadOnlyList<AiRouteBudgetPolicyDescriptor>> ListRouteBudgets(OwnerScope owner)
        => AiApiResult<IReadOnlyList<AiRouteBudgetPolicyDescriptor>>.Implemented(_routeBudgetPolicyCatalog.ListPolicies());

    public AiApiResult<IReadOnlyList<AiRouteBudgetStatusProjection>> ListRouteBudgetStatuses(OwnerScope owner)
        => AiApiResult<IReadOnlyList<AiRouteBudgetStatusProjection>>.Implemented(
            CreateStatusProjection(owner).RouteBudgetStatuses ?? []);

    public AiApiResult<AiConversationTurnPreview> PreviewTurn(OwnerScope owner, string routeType, AiConversationTurnRequest? request)
    {
        AiConversationTurnRequest effectiveRequest = request ?? new AiConversationTurnRequest(string.Empty);
        (AiProviderRouteDecision routeDecision, AiGroundingBundle grounding, AiBudgetSnapshot budget, string systemPrompt, AiProviderTurnPlan providerRequest) =
            BuildPreviewArtifacts(owner, routeType, effectiveRequest);

        return AiApiResult<AiConversationTurnPreview>.Implemented(
            new AiConversationTurnPreview(
                RouteType: routeType,
                RouteDecision: routeDecision,
                Grounding: grounding,
                Budget: budget,
                SystemPrompt: systemPrompt,
                ProviderRequest: providerRequest));
    }

    public AiApiResult<AiConversationSnapshot> GetConversation(OwnerScope owner, string conversationId)
    {
        AiConversationSnapshot? conversation = _conversationStore.Get(owner, conversationId);
        if (conversation is not null)
        {
            return AiApiResult<AiConversationSnapshot>.Implemented(conversation);
        }

        return NotImplemented<AiConversationSnapshot>(owner, AiApiOperations.GetConversation, conversationId);
    }

    public AiApiResult<AiConversationTurnResponse> SendChatTurn(OwnerScope owner, AiConversationTurnRequest? request)
        => ExecuteTurn(owner, AiApiOperations.SendChatTurn, AiRouteTypes.Chat, request);

    public AiApiResult<AiConversationTurnResponse> SendCoachTurn(OwnerScope owner, AiConversationTurnRequest? request)
        => ExecuteTurn(owner, AiApiOperations.SendCoachTurn, AiRouteTypes.Coach, request);

    public AiApiResult<AiConversationTurnResponse> SendBuildTurn(OwnerScope owner, AiConversationTurnRequest? request)
        => ExecuteTurn(owner, AiApiOperations.SendBuildTurn, AiRouteTypes.Build, request);

    public AiApiResult<AiConversationTurnResponse> SendDocsTurn(OwnerScope owner, AiConversationTurnRequest? request)
        => ExecuteTurn(owner, AiApiOperations.SendDocsTurn, AiRouteTypes.Docs, request);

    public AiApiResult<AiConversationTurnResponse> SendRecapTurn(OwnerScope owner, AiConversationTurnRequest? request)
        => ExecuteTurn(owner, AiApiOperations.SendRecapTurn, AiRouteTypes.Recap, request);

    private AiApiResult<AiConversationTurnResponse> ExecuteTurn(
        OwnerScope owner,
        string operation,
        string routeType,
        AiConversationTurnRequest? request)
    {
        AiConversationTurnRequest effectiveRequest = request ?? new AiConversationTurnRequest(string.Empty);
        (AiProviderRouteDecision routeDecision, AiGroundingBundle grounding, AiBudgetSnapshot budget, string systemPrompt, AiProviderTurnPlan providerRequest) =
            BuildPreviewArtifacts(owner, routeType, effectiveRequest);
        AiResponseCacheLookup? cacheLookup = CreateCacheLookup(routeType, effectiveRequest, grounding);
        if (cacheLookup is not null)
        {
            AiCachedConversationTurn? cachedTurn = _responseCacheStore.Get(owner, cacheLookup);
            if (cachedTurn is not null)
            {
                AiConversationTurnResponse cachedResponse = cachedTurn.Response with
                {
                    ConversationId = string.IsNullOrWhiteSpace(effectiveRequest.ConversationId)
                        ? cachedTurn.Response.ConversationId
                        : effectiveRequest.ConversationId,
                    Grounding = grounding,
                    Budget = budget,
                    Cache = CreateCacheMetadata(AiCacheStatuses.Hit, cachedTurn)
                };
                RecordConversationTurn(owner, routeType, effectiveRequest, cachedResponse, systemPrompt, grounding, cachedResponse.ProviderId);
                return AiApiResult<AiConversationTurnResponse>.Implemented(cachedResponse);
            }
        }

        if (ResolveExceededLimitKind(budget, requestedUnits: 1) is not null)
        {
            return QuotaExceeded<AiConversationTurnResponse>(owner, operation, budget, requestedUnits: 1, effectiveRequest.ConversationId, routeType);
        }

        IAiProvider? provider = _providerCatalog.GetProvider(routeDecision.ProviderId);
        if (provider is null)
        {
            return NotImplemented<AiConversationTurnResponse>(owner, operation, effectiveRequest.ConversationId, routeType);
        }

        AiConversationTurnResponse response;
        try
        {
            response = provider.CompleteTurn(owner, providerRequest);
            _providerHealthStore.RecordSuccess(
                routeDecision.ProviderId,
                DateTimeOffset.UtcNow,
                routeType,
                routeDecision.CredentialTier,
                routeDecision.CredentialSlotIndex);
        }
        catch (Exception ex)
        {
            _providerHealthStore.RecordFailure(
                routeDecision.ProviderId,
                ex.Message,
                DateTimeOffset.UtcNow,
                routeType,
                routeDecision.CredentialTier,
                routeDecision.CredentialSlotIndex);
            throw;
        }

        DateTimeOffset recordedAtUtc = DateTimeOffset.UtcNow;
        _usageLedgerStore.RecordUsage(owner, routeType, consumedUnits: 1, recordedAtUtc);
        AiConversationTurnResponse responseWithUpdatedBudget = response with
        {
            Budget = _budgetService.GetBudget(owner, routeType),
            Cache = cacheLookup is null
                ? null
                : CreateCacheMetadata(AiCacheStatuses.Miss, cacheLookup, recordedAtUtc)
        };
        if (cacheLookup is not null)
        {
            _responseCacheStore.Upsert(owner, new AiCachedConversationTurn(
                CacheKey: AiResponseCacheKeys.CreateCacheKey(cacheLookup),
                RouteType: cacheLookup.RouteType,
                NormalizedPrompt: cacheLookup.NormalizedPrompt,
                RuntimeFingerprint: cacheLookup.RuntimeFingerprint,
                CharacterId: cacheLookup.CharacterId,
                AttachmentKey: cacheLookup.AttachmentKey,
                CachedAtUtc: recordedAtUtc,
                Response: responseWithUpdatedBudget,
                WorkspaceId: cacheLookup.WorkspaceId));
        }

        RecordConversationTurn(owner, routeType, effectiveRequest, responseWithUpdatedBudget, systemPrompt, grounding, responseWithUpdatedBudget.ProviderId);
        return AiApiResult<AiConversationTurnResponse>.Implemented(responseWithUpdatedBudget);
    }

    private void RecordConversationTurn(
        OwnerScope owner,
        string routeType,
        AiConversationTurnRequest request,
        AiConversationTurnResponse response,
        string systemPrompt,
        AiGroundingBundle grounding,
        string providerId)
    {
        DateTimeOffset createdAtUtc = DateTimeOffset.UtcNow;
        string conversationId = !string.IsNullOrWhiteSpace(response.ConversationId)
            ? response.ConversationId
            : request.ConversationId
                ?? throw new InvalidOperationException("Conversation id must be present before recording an AI turn.");
        AiConversationSnapshot? existing = _conversationStore.Get(owner, conversationId);
        List<AiConversationMessage> messages = existing?.Messages.ToList() ?? [];
        List<AiConversationTurnRecord> turns = existing?.Turns?.ToList() ?? [];
        string? workspaceId = grounding.WorkspaceId ?? request.WorkspaceId ?? existing?.WorkspaceId;
        if (messages.Count == 0)
        {
            messages.Add(new AiConversationMessage(
                MessageId: "system-1",
                Role: AiConversationRoles.System,
                Content: systemPrompt,
                CreatedAtUtc: createdAtUtc,
                ProviderId: providerId));
        }

        messages.Add(new AiConversationMessage(
            MessageId: $"user-{messages.Count + 1}",
            Role: AiConversationRoles.User,
            Content: request.Message,
            CreatedAtUtc: createdAtUtc,
            ProviderId: providerId));
        messages.Add(new AiConversationMessage(
            MessageId: $"assistant-{messages.Count + 1}",
            Role: AiConversationRoles.Assistant,
            Content: response.Answer,
            CreatedAtUtc: createdAtUtc,
            ProviderId: providerId));
        turns.Add(new AiConversationTurnRecord(
            TurnId: $"turn-{turns.Count + 1}",
            RouteType: routeType,
            ProviderId: providerId,
            CreatedAtUtc: createdAtUtc,
            UserMessage: request.Message,
            AssistantAnswer: response.Answer,
            ToolInvocations: response.ToolInvocations,
            Citations: response.Citations,
            StructuredAnswer: response.StructuredAnswer,
            RuntimeFingerprint: grounding.RuntimeFingerprint,
            CharacterId: grounding.CharacterId,
            Cache: response.Cache,
            RouteDecision: response.RouteDecision,
            GroundingCoverage: response.Grounding.Coverage,
            WorkspaceId: workspaceId,
            SuggestedActions: response.SuggestedActions,
            FlavorLine: response.FlavorLine,
            Budget: response.Budget));

        _conversationStore.Upsert(owner, new AiConversationSnapshot(
            ConversationId: conversationId,
            RouteType: routeType,
            Messages: messages,
            RuntimeFingerprint: grounding.RuntimeFingerprint,
            CharacterId: grounding.CharacterId,
            Turns: turns,
            WorkspaceId: workspaceId));
    }

    private (AiProviderRouteDecision RouteDecision, AiGroundingBundle Grounding, AiBudgetSnapshot Budget, string SystemPrompt, AiProviderTurnPlan ProviderRequest) BuildPreviewArtifacts(
        OwnerScope owner,
        string routeType,
        AiConversationTurnRequest request)
    {
        AiProviderRouteDecision routeDecision = ResolveRoutableProviderDecision(owner, routeType, request);
        AiProviderCredentialSelection? credentialSelection = _providerCredentialSelector?.SelectCredential(routeDecision.ProviderId);
        IAiProvider? provider = _providerCatalog.GetProvider(routeDecision.ProviderId);
        AiProviderRouteDecision providerBoundDecision = routeDecision with
        {
            Reason = DescribeProviderBinding(routeDecision, provider),
            CredentialTier = credentialSelection?.CredentialTier ?? routeDecision.CredentialTier,
            CredentialSlotIndex = credentialSelection?.SlotIndex
        };
        AiGroundingBundle grounding = _retrievalService.BuildGroundingBundle(owner, routeType, request);
        AiBudgetSnapshot budget = _budgetService.GetBudget(owner, routeType);
        AiProviderTurnPlan providerRequest = _promptAssembler.AssembleTurnPlan(request, grounding, providerBoundDecision, budget);
        return (providerBoundDecision, grounding, budget, providerRequest.SystemPrompt, providerRequest);
    }

    private AiProviderRouteDecision ResolveRoutableProviderDecision(
        OwnerScope owner,
        string routeType,
        AiConversationTurnRequest request)
    {
        AiProviderRouteDecision routeDecision = _providerRouter.RouteTurn(owner, routeType, request);
        if (!string.Equals(_providerHealthStore.Get(routeDecision.ProviderId).CircuitState, AiProviderCircuitStates.Open, StringComparison.Ordinal))
        {
            return routeDecision;
        }

        AiRoutePolicyDescriptor routePolicy = AiGatewayDefaults.ResolveRoutePolicy(routeType);
        string? fallbackProviderId = routePolicy.FallbackProviderIds
            .FirstOrDefault(providerId =>
            {
                if (string.Equals(providerId, routeDecision.ProviderId, StringComparison.Ordinal))
                {
                    return false;
                }

                IAiProvider? fallbackProvider = _providerCatalog.GetProvider(providerId);
                return fallbackProvider is not null
                    && !string.Equals(_providerHealthStore.Get(providerId).CircuitState, AiProviderCircuitStates.Open, StringComparison.Ordinal);
            });
        if (string.IsNullOrWhiteSpace(fallbackProviderId))
        {
            return routeDecision;
        }

        return routeDecision with
        {
            ProviderId = fallbackProviderId,
            Reason = $"{routeDecision.Reason}; rerouted because {routeDecision.ProviderId} circuit is open"
        };
    }

    private static AiResponseCacheLookup? CreateCacheLookup(
        string routeType,
        AiConversationTurnRequest request,
        AiGroundingBundle grounding)
    {
        AiResponseCacheLookup lookup = AiResponseCacheKeys.CreateLookup(
            routeType,
            request.Message,
            grounding.RuntimeFingerprint ?? request.RuntimeFingerprint,
            grounding.CharacterId ?? request.CharacterId,
            request.AttachmentIds,
            grounding.WorkspaceId ?? request.WorkspaceId);
        return string.IsNullOrEmpty(lookup.NormalizedPrompt)
            ? null
            : lookup;
    }

    private static AiCacheMetadata CreateCacheMetadata(string status, AiResponseCacheLookup lookup, DateTimeOffset cachedAtUtc)
        => new(
            Status: status,
            CacheKey: AiResponseCacheKeys.CreateCacheKey(lookup),
            CachedAtUtc: cachedAtUtc,
            NormalizedPrompt: lookup.NormalizedPrompt,
            RuntimeFingerprint: lookup.RuntimeFingerprint,
            CharacterId: lookup.CharacterId,
            WorkspaceId: lookup.WorkspaceId);

    private static AiCacheMetadata CreateCacheMetadata(string status, AiCachedConversationTurn cachedTurn)
        => new(
            Status: status,
            CacheKey: cachedTurn.CacheKey,
            CachedAtUtc: cachedTurn.CachedAtUtc,
            NormalizedPrompt: cachedTurn.NormalizedPrompt,
            RuntimeFingerprint: cachedTurn.RuntimeFingerprint,
            CharacterId: cachedTurn.CharacterId,
            WorkspaceId: cachedTurn.WorkspaceId);

    private static string DescribeProviderBinding(AiProviderRouteDecision routeDecision, IAiProvider? provider)
    {
        if (provider is null)
        {
            return $"{routeDecision.Reason}; provider adapter missing";
        }

        if (provider.LiveExecutionEnabled)
        {
            return $"{routeDecision.Reason}; live provider adapter registered";
        }

        return provider.AdapterKind switch
        {
            AiProviderAdapterKinds.RemoteHttp => $"{routeDecision.Reason}; remote provider transport registered",
            AiProviderAdapterKinds.Stub => $"{routeDecision.Reason}; stub provider adapter registered",
            _ => $"{routeDecision.Reason}; provider adapter registered"
        };
    }

    private AiGatewayStatusProjection CreateStatusProjection(OwnerScope owner)
    {
        IReadOnlyList<AiRouteBudgetPolicyDescriptor> routeBudgets = _routeBudgetPolicyCatalog.ListPolicies();
        AiRouteBudgetStatusProjection[] routeBudgetStatuses = routeBudgets
            .Select(policy => AiGatewayDefaults.CreateRouteBudgetStatus(policy, _budgetService.GetBudget(owner, policy.RouteType)))
            .ToArray();
        int monthlyConsumed = routeBudgetStatuses.Sum(static status => status.MonthlyConsumed);
        int currentBurstConsumed = routeBudgetStatuses.Sum(static status => status.CurrentBurstConsumed);

        return AiGatewayDefaults.CreateStatus(
            _providerCredentialCatalog?.GetConfiguredCredentialCounts(),
            _providerCatalog.ListProviders(),
            routeBudgets,
            monthlyConsumed,
            currentBurstConsumed,
            routeBudgetStatuses);
    }

    private IReadOnlyList<AiProviderHealthProjection> CreateProviderHealthProjection(OwnerScope owner)
    {
        Dictionary<string, AiProviderHealthSnapshot> healthByProvider = _providerHealthStore.List()
            .ToDictionary(snapshot => snapshot.ProviderId, StringComparer.Ordinal);

        return CreateStatusProjection(owner).Providers
            .Select(provider =>
            {
                AiProviderHealthSnapshot snapshot = healthByProvider.TryGetValue(provider.ProviderId, out AiProviderHealthSnapshot? stored)
                    ? stored
                    : new AiProviderHealthSnapshot(provider.ProviderId);
                return new AiProviderHealthProjection(
                    ProviderId: provider.ProviderId,
                    DisplayName: provider.DisplayName,
                    AdapterKind: provider.AdapterKind,
                    AdapterRegistered: provider.AdapterRegistered,
                    LiveExecutionEnabled: provider.LiveExecutionEnabled,
                    AllowedRouteTypes: provider.AllowedRouteTypes,
                    CircuitState: snapshot.CircuitState,
                    ConsecutiveFailureCount: snapshot.ConsecutiveFailureCount,
                    LastSuccessAtUtc: snapshot.LastSuccessAtUtc,
                    LastFailureAtUtc: snapshot.LastFailureAtUtc,
                    LastFailureMessage: snapshot.LastFailureMessage,
                    LastRouteType: snapshot.LastRouteType,
                    LastCredentialTier: snapshot.LastCredentialTier,
                    LastCredentialSlotIndex: snapshot.LastCredentialSlotIndex,
                    IsConfigured: provider.IsConfigured,
                    PrimaryCredentialCount: provider.PrimaryCredentialCount,
                    FallbackCredentialCount: provider.FallbackCredentialCount,
                    TransportBaseUrlConfigured: provider.TransportBaseUrlConfigured,
                    TransportModelConfigured: provider.TransportModelConfigured,
                    TransportMetadataConfigured: provider.TransportMetadataConfigured,
                    IsRoutable: provider.AdapterRegistered
                        && !string.Equals(snapshot.CircuitState, AiProviderCircuitStates.Open, StringComparison.Ordinal));
            })
            .ToArray();
    }

    private static string? ResolveExceededLimitKind(AiBudgetSnapshot budget, int requestedUnits)
    {
        if (!budget.IsLimited || requestedUnits <= 0)
        {
            return null;
        }

        if (budget.MonthlyAllowance >= 0
            && budget.MonthlyConsumed + requestedUnits > budget.MonthlyAllowance)
        {
            return AiBudgetLimitKinds.MonthlyAllowance;
        }

        if (budget.BurstLimitPerMinute >= 0
            && budget.CurrentBurstConsumed + requestedUnits > budget.BurstLimitPerMinute)
        {
            return AiBudgetLimitKinds.BurstLimitPerMinute;
        }

        return null;
    }

    private static AiApiResult<T> NotImplemented<T>(OwnerScope owner, string operation, string? conversationId = null, string? routeType = null)
        => AiApiResult<T>.FromNotImplemented(
            new AiNotImplementedReceipt(
                Error: "ai_not_implemented",
                Operation: operation,
                Message: "The Chummer AI gateway is not implemented yet.",
                ConversationId: string.IsNullOrWhiteSpace(conversationId) ? null : conversationId,
                RouteType: routeType,
                OwnerId: owner.NormalizedValue));

    private static AiApiResult<T> QuotaExceeded<T>(
        OwnerScope owner,
        string operation,
        AiBudgetSnapshot budget,
        int requestedUnits,
        string? conversationId = null,
        string? routeType = null)
    {
        string limitKind = ResolveExceededLimitKind(budget, requestedUnits) ?? AiBudgetLimitKinds.MonthlyAllowance;
        string message = limitKind switch
        {
            AiBudgetLimitKinds.BurstLimitPerMinute => $"The {routeType ?? "ai"} route has exhausted its per-minute {budget.BudgetUnit} burst allowance for this owner.",
            _ => $"The {routeType ?? "ai"} route has exhausted its monthly {budget.BudgetUnit} allowance for this owner."
        };

        return AiApiResult<T>.FromQuotaExceeded(
            new AiQuotaExceededReceipt(
                Error: "ai_quota_exceeded",
                Operation: operation,
                Message: message,
                Budget: budget,
                RequestedUnits: requestedUnits,
                LimitKind: limitKind,
                ConversationId: string.IsNullOrWhiteSpace(conversationId) ? null : conversationId,
                RouteType: routeType,
                OwnerId: owner.NormalizedValue));
    }
}
