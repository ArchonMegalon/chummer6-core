using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiMediaAssetCatalogService
{
    AiApiResult<AiMediaAssetCatalog> ListMediaAssets(OwnerScope owner, AiMediaAssetQuery? query);

    AiApiResult<AiMediaAssetProjection> GetMediaAsset(OwnerScope owner, string assetId);
}
