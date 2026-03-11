#nullable enable annotations

using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiApprovalOrchestratorTests
{
    [TestMethod]
    public void Not_implemented_ai_approval_orchestrator_exposes_explicit_approval_boundaries()
    {
        NotImplementedAiApprovalOrchestrator service = new();

        AiApiResult<AiApprovalCatalog> list = service.ListApprovals(
            OwnerScope.LocalSingleUser,
            new AiApprovalQuery(State: AiApprovalStates.PendingReview, TargetKind: AiApprovalTargetKinds.MediaJob, MaxCount: 5));
        AiApiResult<AiApprovalReceipt> submit = service.SubmitApproval(
            OwnerScope.LocalSingleUser,
            new AiApprovalSubmitRequest(
                TargetKind: AiApprovalTargetKinds.RecapDraft,
                TargetId: "recap-1",
                Title: "Review recap draft",
                Summary: "Approve this recap before it becomes canonical."));
        AiApiResult<AiApprovalReceipt> resolve = service.ResolveApproval(
            OwnerScope.LocalSingleUser,
            "approval-1",
            new AiApprovalResolveRequest(
                Decision: AiApprovalDecisionKinds.Approve,
                FinalState: AiApprovalStates.ApprovedCanonical));

        Assert.IsFalse(list.IsImplemented);
        Assert.AreEqual(AiApprovalApiOperations.ListApprovals, list.NotImplemented?.Operation);
        Assert.IsFalse(submit.IsImplemented);
        Assert.AreEqual(AiApprovalApiOperations.SubmitApproval, submit.NotImplemented?.Operation);
        Assert.IsFalse(resolve.IsImplemented);
        Assert.AreEqual(AiApprovalApiOperations.ResolveApproval, resolve.NotImplemented?.Operation);
    }
}
