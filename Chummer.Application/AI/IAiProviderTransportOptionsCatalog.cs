namespace Chummer.Application.AI;

public interface IAiProviderTransportOptionsCatalog
{
    IReadOnlyDictionary<string, AiProviderTransportOptions> GetConfiguredTransportOptions();
}
