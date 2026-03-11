using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class NotImplementedAiApprovalOrchestrator : IAiApprovalOrchestrator
{
    public AiApiResult<AiApprovalCatalog> ListApprovals(OwnerScope owner, AiApprovalQuery? query)
        => AiApiResult<AiApprovalCatalog>.FromNotImplemented(
            CreateReceipt(owner, AiApprovalApiOperations.ListApprovals));

    public AiApiResult<AiApprovalReceipt> SubmitApproval(OwnerScope owner, AiApprovalSubmitRequest? request)
        => AiApiResult<AiApprovalReceipt>.FromNotImplemented(
            CreateReceipt(owner, AiApprovalApiOperations.SubmitApproval));

    public AiApiResult<AiApprovalReceipt> ResolveApproval(OwnerScope owner, string approvalId, AiApprovalResolveRequest? request)
        => AiApiResult<AiApprovalReceipt>.FromNotImplemented(
            CreateReceipt(owner, AiApprovalApiOperations.ResolveApproval));

    private static AiNotImplementedReceipt CreateReceipt(OwnerScope owner, string operation)
        => new(
            Error: "ai_not_implemented",
            Operation: operation,
            Message: "The Chummer AI approval surface is not implemented yet.",
            OwnerId: owner.NormalizedValue);
}
