using System.Linq;
using Chummer.Application.AI;
using Chummer.Contracts.AI;

namespace Chummer.Infrastructure.AI;

public sealed class EnvironmentAiProviderCredentialCatalog : IAiProviderCredentialCatalog
{
    public const string AiMagicxPrimaryApiKeyEnvironmentVariable = "CHUMMER_AI_AIMAGICX_PRIMARY_API_KEY";
    public const string AiMagicxFallbackApiKeyEnvironmentVariable = "CHUMMER_AI_AIMAGICX_FALLBACK_API_KEY";
    public const string OneMinAiPrimaryApiKeyEnvironmentVariable = "CHUMMER_AI_1MINAI_PRIMARY_API_KEY";
    public const string OneMinAiFallbackApiKeyEnvironmentVariable = "CHUMMER_AI_1MINAI_FALLBACK_API_KEY";

    public IReadOnlyDictionary<string, AiProviderCredentialCounts> GetConfiguredCredentialCounts()
        => new Dictionary<string, AiProviderCredentialCounts>(StringComparer.Ordinal)
        {
            [AiProviderIds.AiMagicx] = new(
                PrimaryCredentialCount: CountConfiguredKeys(AiMagicxPrimaryApiKeyEnvironmentVariable),
                FallbackCredentialCount: CountConfiguredKeys(AiMagicxFallbackApiKeyEnvironmentVariable)),
            [AiProviderIds.OneMinAi] = new(
                PrimaryCredentialCount: CountConfiguredKeys(OneMinAiPrimaryApiKeyEnvironmentVariable),
                FallbackCredentialCount: CountConfiguredKeys(OneMinAiFallbackApiKeyEnvironmentVariable))
        };

    public IReadOnlyDictionary<string, AiProviderCredentialSet> GetConfiguredCredentialSets()
        => new Dictionary<string, AiProviderCredentialSet>(StringComparer.Ordinal)
        {
            [AiProviderIds.AiMagicx] = new(
                PrimaryCredentials: ParseConfiguredKeys(AiMagicxPrimaryApiKeyEnvironmentVariable),
                FallbackCredentials: ParseConfiguredKeys(AiMagicxFallbackApiKeyEnvironmentVariable)),
            [AiProviderIds.OneMinAi] = new(
                PrimaryCredentials: ParseConfiguredKeys(OneMinAiPrimaryApiKeyEnvironmentVariable),
                FallbackCredentials: ParseConfiguredKeys(OneMinAiFallbackApiKeyEnvironmentVariable))
        };

    private static int CountConfiguredKeys(string environmentVariable)
        => ParseConfiguredKeys(environmentVariable).Count;

    private static IReadOnlyList<string> ParseConfiguredKeys(string environmentVariable)
    {
        string? raw = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeKey)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .ToArray();
    }

    private static string NormalizeKey(string key)
        => key
            .Trim()
            .Trim('"', '\'')
            .TrimEnd('*')
            .Trim();
}
