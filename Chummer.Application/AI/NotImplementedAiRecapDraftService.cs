using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Receipts;

namespace Chummer.Application.AI;

public sealed class NotImplementedAiRecapDraftService : IAiRecapDraftService
{
    public AiApiResult<AiRecapDraftCatalog> ListRecapDrafts(OwnerScope owner, AiRecapDraftQuery? query)
        => AiApiResult<AiRecapDraftCatalog>.FromNotImplemented(
            CreateReceipt(owner, AiRecapDraftApiOperations.ListRecapDrafts));

    public AiApiResult<AiRecapDraftReceipt> CreateRecapDraft(OwnerScope owner, AiRecapDraftRequest? request)
        => AiApiResult<AiRecapDraftReceipt>.FromNotImplemented(
            CreateReceipt(owner, AiRecapDraftApiOperations.CreateRecapDraft));

    private static AiNotImplementedReceipt CreateReceipt(OwnerScope owner, string operation)
        => new(
            Error: "ai_not_implemented",
            Operation: operation,
            Message: "The Chummer AI recap-draft surface is not implemented yet.",
            OwnerId: owner.NormalizedValue,
            Envelope: BuildEnvelope(owner, operation));

    private static ReceiptEnvelope BuildEnvelope(OwnerScope owner, string operation)
        => ReceiptEnvelopeFactory.Runtime(
            receiptKind: "ai_boundary",
            ownerScope: owner.IsLocalSingleUser ? "ai.local_single_user" : "ai.owner_scoped",
            exposureClass: ReceiptExposureClasses.SignedIn,
            evidenceRef: operation,
            reviewState: "not_implemented");
}
