using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiMediaJobService
{
    AiApiResult<AiMediaJobReceipt> QueuePortraitJob(OwnerScope owner, AiMediaJobRequest? request);

    AiApiResult<AiMediaJobReceipt> QueueDossierJob(OwnerScope owner, AiMediaJobRequest? request);

    AiApiResult<AiMediaJobReceipt> QueueRouteVideoJob(OwnerScope owner, AiMediaJobRequest? request);
}
