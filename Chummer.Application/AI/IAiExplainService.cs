using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiExplainService
{
    AiExplainValueProjection? GetExplainValue(OwnerScope owner, AiExplainValueQuery query);
}
