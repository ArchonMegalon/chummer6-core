using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Receipts;

namespace Chummer.Application.AI;

public sealed class NotImplementedAiMediaAssetCatalogService : IAiMediaAssetCatalogService
{
    public AiApiResult<AiMediaAssetCatalog> ListMediaAssets(OwnerScope owner, AiMediaAssetQuery? query)
        => AiApiResult<AiMediaAssetCatalog>.FromNotImplemented(
            CreateReceipt(owner, AiMediaAssetApiOperations.ListMediaAssets));

    public AiApiResult<AiMediaAssetProjection> GetMediaAsset(OwnerScope owner, string assetId)
        => AiApiResult<AiMediaAssetProjection>.FromNotImplemented(
            CreateReceipt(owner, AiMediaAssetApiOperations.GetMediaAsset));

    private static AiNotImplementedReceipt CreateReceipt(OwnerScope owner, string operation)
        => new(
            Error: "ai_not_implemented",
            Operation: operation,
            Message: "The Chummer AI media-asset catalog surface is not implemented yet.",
            OwnerId: owner.NormalizedValue,
            Envelope: ReceiptEnvelopeFactory.Runtime(
                receiptKind: "ai_boundary",
                ownerScope: owner.IsLocalSingleUser ? "ai.local_single_user" : "ai.owner_scoped",
                exposureClass: ReceiptExposureClasses.SignedIn,
                evidenceRef: operation,
                reviewState: "not_implemented"));
}
