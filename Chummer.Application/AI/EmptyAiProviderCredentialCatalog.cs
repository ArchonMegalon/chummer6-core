using Chummer.Contracts.AI;

namespace Chummer.Application.AI;

/// <summary>
/// Neutral default used when the active headless-core boundary must not source-own third-party provider credentials.
/// </summary>
public sealed class EmptyAiProviderCredentialCatalog : IAiProviderCredentialCatalog
{
    private static readonly IReadOnlyDictionary<string, AiProviderCredentialCounts> Counts =
        new Dictionary<string, AiProviderCredentialCounts>(StringComparer.Ordinal)
        {
            [AiProviderIds.AiMagicx] = new(),
            [AiProviderIds.OneMinAi] = new()
        };

    private static readonly IReadOnlyDictionary<string, AiProviderCredentialSet> Sets =
        new Dictionary<string, AiProviderCredentialSet>(StringComparer.Ordinal)
        {
            [AiProviderIds.AiMagicx] = new(Array.Empty<string>(), Array.Empty<string>()),
            [AiProviderIds.OneMinAi] = new(Array.Empty<string>(), Array.Empty<string>())
        };

    public IReadOnlyDictionary<string, AiProviderCredentialCounts> GetConfiguredCredentialCounts()
        => Counts;

    public IReadOnlyDictionary<string, AiProviderCredentialSet> GetConfiguredCredentialSets()
        => Sets;
}
