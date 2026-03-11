using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiProviderTransportClient
{
    AiProviderTransportResponse Execute(OwnerScope owner, AiProviderTransportRequest request);
}
