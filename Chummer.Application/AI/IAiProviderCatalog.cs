using System.Collections.Generic;
using Chummer.Contracts.AI;

namespace Chummer.Application.AI;

public interface IAiProviderCatalog
{
    IAiProvider? GetProvider(string providerId);

    IReadOnlyList<AiProviderDescriptor> ListProviders();
}
