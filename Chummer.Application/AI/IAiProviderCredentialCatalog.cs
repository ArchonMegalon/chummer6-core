using Chummer.Contracts.AI;

namespace Chummer.Application.AI;

public interface IAiProviderCredentialCatalog
{
    IReadOnlyDictionary<string, AiProviderCredentialCounts> GetConfiguredCredentialCounts();

    IReadOnlyDictionary<string, AiProviderCredentialSet> GetConfiguredCredentialSets();
}
