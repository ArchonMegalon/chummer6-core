using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class DefaultAiProviderRouter(
    IAiProviderCredentialCatalog? credentialCatalog = null,
    IAiProviderCatalog? providerCatalog = null) : IAiProviderRouter
{
    private readonly IAiProviderCredentialCatalog? _credentialCatalog = credentialCatalog;
    private readonly IAiProviderCatalog _providerCatalog = providerCatalog ?? new DefaultAiProviderCatalog();

    public AiProviderRouteDecision RouteTurn(OwnerScope owner, string routeType, AiConversationTurnRequest request)
    {
        ArgumentNullException.ThrowIfNull(routeType);
        ArgumentNullException.ThrowIfNull(request);

        AiRoutePolicyDescriptor policy = ResolvePolicy(routeType);
        IReadOnlyDictionary<string, AiProviderCredentialCounts>? configuredCredentials = _credentialCatalog?.GetConfiguredCredentialCounts();
        IReadOnlyDictionary<string, AiProviderDescriptor> registeredProviders = _providerCatalog.ListProviders()
            .ToDictionary(static provider => provider.ProviderId, StringComparer.Ordinal);

        if (TryResolveConfiguredProvider(
            policy,
            policy.PrimaryProviderId,
            configuredCredentials,
            registeredProviders,
            requireLiveExecution: true,
            reason: "primary provider live execution enabled",
            out AiProviderRouteDecision primaryLiveDecision))
        {
            return primaryLiveDecision;
        }

        foreach (string fallbackProviderId in policy.FallbackProviderIds)
        {
            if (TryResolveConfiguredProvider(
                policy,
                fallbackProviderId,
                configuredCredentials,
                registeredProviders,
                requireLiveExecution: true,
                reason: "fallback provider live execution enabled",
                out AiProviderRouteDecision fallbackLiveDecision))
            {
                return fallbackLiveDecision;
            }
        }

        if (TryResolveConfiguredProvider(
            policy,
            policy.PrimaryProviderId,
            configuredCredentials,
            registeredProviders,
            requireLiveExecution: false,
            reason: "primary provider configured; adapter registered without live execution",
            out AiProviderRouteDecision primaryConfiguredDecision))
        {
            return primaryConfiguredDecision;
        }

        foreach (string fallbackProviderId in policy.FallbackProviderIds)
        {
            if (TryResolveConfiguredProvider(
                policy,
                fallbackProviderId,
                configuredCredentials,
                registeredProviders,
                requireLiveExecution: false,
                reason: "fallback provider configured; adapter registered without live execution",
                out AiProviderRouteDecision fallbackConfiguredDecision))
            {
                return fallbackConfiguredDecision;
            }
        }

        return CreateDecision(
            policy,
            policy.PrimaryProviderId,
            "no compatible configured provider available; using default route policy",
            new AiProviderCredentialCounts());
    }

    private static AiRoutePolicyDescriptor ResolvePolicy(string routeType)
        => AiGatewayDefaults.ResolveRoutePolicy(routeType);

    private static bool TryResolveConfiguredProvider(
        AiRoutePolicyDescriptor policy,
        string providerId,
        IReadOnlyDictionary<string, AiProviderCredentialCounts>? configuredCredentials,
        IReadOnlyDictionary<string, AiProviderDescriptor> registeredProviders,
        bool requireLiveExecution,
        string reason,
        out AiProviderRouteDecision decision)
    {
        if (configuredCredentials is not null
            && configuredCredentials.TryGetValue(providerId, out AiProviderCredentialCounts? configured))
        {
            AiProviderCredentialCounts credentials = configured ?? new AiProviderCredentialCounts();
            if (credentials.IsConfigured
                && SupportsRoute(registeredProviders, providerId, policy.RouteType)
                && (!requireLiveExecution || IsLiveExecutionEnabled(registeredProviders, providerId)))
            {
                decision = CreateDecision(policy, providerId, reason, credentials);
                return true;
            }
        }

        decision = default!;
        return false;
    }

    private static bool SupportsRoute(
        IReadOnlyDictionary<string, AiProviderDescriptor> registeredProviders,
        string providerId,
        string routeType)
        => registeredProviders.TryGetValue(providerId, out AiProviderDescriptor? provider)
            && provider.AllowedRouteTypes.Contains(routeType, StringComparer.Ordinal);

    private static bool IsLiveExecutionEnabled(
        IReadOnlyDictionary<string, AiProviderDescriptor> registeredProviders,
        string providerId)
        => registeredProviders.TryGetValue(providerId, out AiProviderDescriptor? provider)
            && provider.LiveExecutionEnabled;

    private static AiProviderRouteDecision CreateDecision(
        AiRoutePolicyDescriptor policy,
        string providerId,
        string reason,
        AiProviderCredentialCounts credentials)
        => new(
            RouteType: policy.RouteType,
            ProviderId: providerId,
            Reason: reason,
            BudgetUnit: AiBudgetUnits.ChummerAiUnits,
            ToolingEnabled: policy.ToolingEnabled,
            RetrievalEnabled: policy.RetrievalCorpusIds.Count > 0,
            CredentialTier: ResolveCredentialTier(credentials));

    private static string ResolveCredentialTier(AiProviderCredentialCounts credentials)
        => credentials.PrimaryCredentialCount > 0
            ? AiProviderCredentialTiers.Primary
            : credentials.FallbackCredentialCount > 0
                ? AiProviderCredentialTiers.Fallback
                : AiProviderCredentialTiers.None;
}
