using Chummer.Contracts.AI;

namespace Chummer.Application.AI;

/// <summary>
/// Neutral default used when the active headless-core boundary must not source-own third-party transport routing.
/// </summary>
public sealed class EmptyAiProviderTransportOptionsCatalog : IAiProviderTransportOptionsCatalog
{
    private static readonly IReadOnlyDictionary<string, AiProviderTransportOptions> Options =
        new Dictionary<string, AiProviderTransportOptions>(StringComparer.Ordinal)
        {
            [AiProviderIds.AiMagicx] = new(
                ProviderId: AiProviderIds.AiMagicx,
                BaseUrl: null,
                DefaultModelId: null,
                TransportConfigured: false,
                RemoteExecutionEnabled: false),
            [AiProviderIds.OneMinAi] = new(
                ProviderId: AiProviderIds.OneMinAi,
                BaseUrl: null,
                DefaultModelId: null,
                TransportConfigured: false,
                RemoteExecutionEnabled: false)
        };

    public IReadOnlyDictionary<string, AiProviderTransportOptions> GetConfiguredTransportOptions()
        => Options;
}
