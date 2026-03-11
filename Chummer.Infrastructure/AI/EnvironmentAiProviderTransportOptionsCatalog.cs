using Chummer.Application.AI;
using Chummer.Contracts.AI;

namespace Chummer.Infrastructure.AI;

public sealed class EnvironmentAiProviderTransportOptionsCatalog : IAiProviderTransportOptionsCatalog
{
    public const string EnableRemoteExecutionEnvironmentVariable = "CHUMMER_AI_ENABLE_REMOTE_EXECUTION";
    public const string AiMagicxBaseUrlEnvironmentVariable = "CHUMMER_AI_AIMAGICX_BASE_URL";
    public const string AiMagicxModelEnvironmentVariable = "CHUMMER_AI_AIMAGICX_MODEL";
    public const string OneMinAiBaseUrlEnvironmentVariable = "CHUMMER_AI_1MINAI_BASE_URL";
    public const string OneMinAiModelEnvironmentVariable = "CHUMMER_AI_1MINAI_MODEL";

    public IReadOnlyDictionary<string, AiProviderTransportOptions> GetConfiguredTransportOptions()
    {
        bool remoteExecutionEnabled = ResolveBooleanEnvironmentVariable(EnableRemoteExecutionEnvironmentVariable);

        return new Dictionary<string, AiProviderTransportOptions>(StringComparer.Ordinal)
        {
            [AiProviderIds.AiMagicx] = CreateOptions(
                AiProviderIds.AiMagicx,
                AiMagicxBaseUrlEnvironmentVariable,
                AiMagicxModelEnvironmentVariable,
                remoteExecutionEnabled),
            [AiProviderIds.OneMinAi] = CreateOptions(
                AiProviderIds.OneMinAi,
                OneMinAiBaseUrlEnvironmentVariable,
                OneMinAiModelEnvironmentVariable,
                remoteExecutionEnabled)
        };
    }

    private static AiProviderTransportOptions CreateOptions(
        string providerId,
        string baseUrlEnvironmentVariable,
        string modelEnvironmentVariable,
        bool remoteExecutionEnabled)
    {
        string? baseUrl = Normalize(Environment.GetEnvironmentVariable(baseUrlEnvironmentVariable));
        string? modelId = Normalize(Environment.GetEnvironmentVariable(modelEnvironmentVariable));
        bool transportConfigured = !string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(modelId);

        return new AiProviderTransportOptions(
            ProviderId: providerId,
            BaseUrl: baseUrl,
            DefaultModelId: modelId,
            TransportConfigured: transportConfigured,
            RemoteExecutionEnabled: transportConfigured && remoteExecutionEnabled);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ResolveBooleanEnvironmentVariable(string variableName)
    {
        string? raw = Environment.GetEnvironmentVariable(variableName);
        return bool.TryParse(raw, out bool parsed) && parsed;
    }
}
