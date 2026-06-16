using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Receipts;

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
                OwnerId: owner.NormalizedValue,
                Envelope: ReceiptEnvelopeFactory.Runtime(
                    receiptKind: "ai_boundary",
                    ownerScope: owner.IsLocalSingleUser ? "ai.local_single_user" : "ai.owner_scoped",
                    exposureClass: ReceiptExposureClasses.SignedIn,
                    evidenceRef: AiEvaluationApiOperations.ListEvaluations,
                    reviewState: "not_implemented")));
}
