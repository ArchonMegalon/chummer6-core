#nullable enable annotations

using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiMediaAssetCatalogServiceTests
{
    [TestMethod]
    public void Not_implemented_ai_media_asset_catalog_service_exposes_explicit_catalog_boundaries()
    {
        NotImplementedAiMediaAssetCatalogService service = new();

        AiMediaAssetQuery query = new(
            AssetKind: AiMediaAssetKinds.Portrait,
            CharacterId: "char-1",
            State: AiMediaAssetStates.PendingReview,
            MaxCount: 5);
        AiApiResult<AiMediaAssetCatalog> list = service.ListMediaAssets(
            OwnerScope.LocalSingleUser,
            query);
        AiApiResult<AiMediaAssetProjection> detail = service.GetMediaAsset(
            OwnerScope.LocalSingleUser,
            "asset-1");

        Assert.AreEqual(AiMediaAssetStates.PendingReview, query.State);
        Assert.IsFalse(list.IsImplemented);
        Assert.AreEqual(AiMediaAssetApiOperations.ListMediaAssets, list.NotImplemented?.Operation);
        Assert.IsFalse(detail.IsImplemented);
        Assert.AreEqual(AiMediaAssetApiOperations.GetMediaAsset, detail.NotImplemented?.Operation);
    }
}
