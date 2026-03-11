using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IRetrievalService
{
    AiGroundingBundle BuildGroundingBundle(OwnerScope owner, string routeType, AiConversationTurnRequest request);
}
