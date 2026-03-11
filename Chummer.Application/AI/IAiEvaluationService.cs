using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiEvaluationService
{
    AiApiResult<AiEvaluationCatalog> ListEvaluations(OwnerScope owner, AiEvaluationQuery? query);
}
