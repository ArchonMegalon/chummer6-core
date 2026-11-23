using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCareerGearPurchaseService
{
    CharacterCareerGearPurchaseCatalogResult QueryCatalog(OwnerScope owner, CharacterCareerGearPurchaseCatalogQuery query);
    CharacterCareerGearPurchasePreviewResult Preview(OwnerScope owner, CharacterCareerGearPurchaseRequest request,
        CharacterCareerGearPurchaseCatalogSelection selection);
    CharacterCareerGearPurchasePreviewResult Preview(OwnerScope owner, CharacterCareerGearPurchaseRequest request);
    CharacterCareerGearPurchaseResult Commit(OwnerScope owner, CharacterCareerGearPurchaseCommand command,
        CancellationToken cancellationToken = default);
    CharacterCareerGearPurchaseResult Lookup(OwnerScope owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest);
}

/// <summary>Trusted executable store seam. Revalidate under the common workspace lease; atomically save payload and receipt.</summary>
public interface ICharacterCareerGearPurchaseAtomicCommitCapability
{
    bool CareerGearPurchaseGuardsInstalled => false;
    string CareerGearPurchaseRuntimeDigest { get; }
    CharacterCareerGearPurchaseResult CommitCareerGearPurchase(OwnerScope owner,
        CharacterCareerGearPurchaseCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default);
}
