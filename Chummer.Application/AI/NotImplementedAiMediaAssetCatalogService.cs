using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

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
            OwnerId: owner.NormalizedValue);
}
