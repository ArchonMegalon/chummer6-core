using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiProviderRouter
{
    AiProviderRouteDecision RouteTurn(OwnerScope owner, string routeType, AiConversationTurnRequest request);
}
