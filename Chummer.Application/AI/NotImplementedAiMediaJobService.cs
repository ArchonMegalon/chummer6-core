using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class NotImplementedAiMediaJobService : IAiMediaJobService
{
    public AiApiResult<AiMediaJobReceipt> QueuePortraitJob(OwnerScope owner, AiMediaJobRequest? request)
        => NotImplemented<AiMediaJobReceipt>(owner, AiMediaApiOperations.QueuePortraitJob);

    public AiApiResult<AiMediaJobReceipt> QueueDossierJob(OwnerScope owner, AiMediaJobRequest? request)
        => NotImplemented<AiMediaJobReceipt>(owner, AiMediaApiOperations.QueueDossierJob);

    public AiApiResult<AiMediaJobReceipt> QueueRouteVideoJob(OwnerScope owner, AiMediaJobRequest? request)
        => NotImplemented<AiMediaJobReceipt>(owner, AiMediaApiOperations.QueueRouteVideoJob);

    private static AiApiResult<T> NotImplemented<T>(OwnerScope owner, string operation)
        => AiApiResult<T>.FromNotImplemented(
            new AiNotImplementedReceipt(
                Error: "ai_not_implemented",
                Operation: operation,
                Message: "The Chummer AI media surface is not implemented yet.",
                OwnerId: owner.NormalizedValue));
}
