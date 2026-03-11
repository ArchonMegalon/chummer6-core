using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiMediaQueueService
{
    AiMediaQueueReceipt? QueueMediaJob(OwnerScope owner, AiMediaQueueRequest request);
}
