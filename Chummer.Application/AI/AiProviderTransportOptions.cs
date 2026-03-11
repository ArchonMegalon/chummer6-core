namespace Chummer.Application.AI;

public sealed record AiProviderTransportOptions(
    string ProviderId,
    string? BaseUrl = null,
    string? DefaultModelId = null,
    bool TransportConfigured = false,
    bool RemoteExecutionEnabled = false);
