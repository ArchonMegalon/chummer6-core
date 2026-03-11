using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class NotImplementedAiEvaluationService : IAiEvaluationService
{
    public AiApiResult<AiEvaluationCatalog> ListEvaluations(OwnerScope owner, AiEvaluationQuery? query)
        => AiApiResult<AiEvaluationCatalog>.FromNotImplemented(
            new AiNotImplementedReceipt(
                Error: "ai_not_implemented",
                Operation: AiEvaluationApiOperations.ListEvaluations,
                Message: "The Chummer AI evaluation surface is not implemented yet.",
                RouteType: query?.RouteType,
                OwnerId: owner.NormalizedValue));
}
