using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCareerCustomDrugPurchaseService
{
    CharacterCareerCustomDrugPurchaseCatalogResult QueryCatalog(OwnerScope owner, CharacterCareerCustomDrugPurchaseCatalogQuery query);
    CharacterCareerCustomDrugPurchasePreviewResult Preview(OwnerScope owner, CharacterCareerCustomDrugPurchaseRequest request,
        CharacterCareerCustomDrugPurchaseCatalogSelection selection);
    CharacterCareerCustomDrugPurchaseResult Commit(OwnerScope owner, CharacterCareerCustomDrugPurchaseCommand command,
        CancellationToken cancellationToken = default);
    CharacterCareerCustomDrugPurchaseResult Lookup(OwnerScope owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest);
}

/// <summary>Minimal reuse of the canonical saved-component writer; no parallel source resolver or pricing implementation.</summary>
public interface ICharacterCareerCustomDrugPurchaseComponentValidator
{
    bool MatchesSavedComponent(string componentXml, CharacterCustomDrugComponentSource source, int level, Guid instanceId);
}

/// <summary>Trusted store seam: common owner/workspace lease, fresh canonical authority, one atomic replacement.</summary>
public interface ICharacterCareerCustomDrugPurchaseAtomicCommitCapability
{
    bool CareerCustomDrugPurchaseGuardsInstalled => false;
    string CareerCustomDrugPurchaseRuntimeDigest { get; }
    CharacterCareerCustomDrugPurchaseResult CommitCareerCustomDrugPurchase(OwnerScope owner,
        CharacterCareerCustomDrugPurchaseCommand command, ICharacterSourceDataResolver sourceResolver,
        ICharacterCareerCustomDrugPurchaseComponentValidator componentValidator, CancellationToken cancellationToken = default);
}
