using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Contracts.AI;

namespace Chummer.Application.AI;

public sealed class DefaultAiProviderCatalog(IEnumerable<IAiProvider>? providers = null) : IAiProviderCatalog
{
    private readonly IReadOnlyDictionary<string, IAiProvider> _providers = ResolveProviders(providers)
        .GroupBy(static provider => provider.ProviderId, StringComparer.Ordinal)
        .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);

    public IAiProvider? GetProvider(string providerId)
        => _providers.TryGetValue(providerId, out IAiProvider? provider) ? provider : null;

    public IReadOnlyList<AiProviderDescriptor> ListProviders()
        => _providers.Values
            .OrderBy(static provider => provider.ProviderId, StringComparer.Ordinal)
            .Select(CreateDescriptor)
            .ToArray();

    private static IReadOnlyList<IAiProvider> CreateDefaultProviders()
        =>
        [
            new NotImplementedAiProvider(AiProviderIds.AiMagicx),
            new NotImplementedAiProvider(AiProviderIds.OneMinAi)
        ];

    private static IReadOnlyList<IAiProvider> ResolveProviders(IEnumerable<IAiProvider>? providers)
        => CreateDefaultProviders()
            .Concat(providers ?? Array.Empty<IAiProvider>())
            .ToArray();

    private static AiProviderDescriptor CreateDescriptor(IAiProvider provider)
    {
        bool transportBaseUrlConfigured = provider is RemoteHttpAiProvider remoteProvider
            && !string.IsNullOrWhiteSpace(remoteProvider.BaseUrl);
        bool transportModelConfigured = provider is RemoteHttpAiProvider remoteProviderWithModel
            && !string.IsNullOrWhiteSpace(remoteProviderWithModel.DefaultModelId);
        bool transportMetadataConfigured = transportBaseUrlConfigured && transportModelConfigured;

        return AiGatewayDefaults.CreateDescriptor(
            provider.ExecutionPolicy,
            adapterKind: provider.AdapterKind,
            liveExecutionEnabled: provider.LiveExecutionEnabled,
            adapterRegistered: true,
            transportBaseUrlConfigured: transportBaseUrlConfigured,
            transportModelConfigured: transportModelConfigured,
            transportMetadataConfigured: transportMetadataConfigured);
    }
}
