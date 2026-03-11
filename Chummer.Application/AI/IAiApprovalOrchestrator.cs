using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiApprovalOrchestrator
{
    AiApiResult<AiApprovalCatalog> ListApprovals(OwnerScope owner, AiApprovalQuery? query);

    AiApiResult<AiApprovalReceipt> SubmitApproval(OwnerScope owner, AiApprovalSubmitRequest? request);

    AiApiResult<AiApprovalReceipt> ResolveApproval(OwnerScope owner, string approvalId, AiApprovalResolveRequest? request);
}
