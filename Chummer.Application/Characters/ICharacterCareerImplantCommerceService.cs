using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCareerImplantCommerceService
{
    CharacterCareerImplantCommerceCatalogResult QueryCatalog(OwnerContextStamp owner, CharacterCareerImplantCommerceQuery query);
    CharacterCareerImplantCommercePreviewResult Preview(OwnerContextStamp owner,
        CharacterCareerImplantCommerceRequest request, CharacterCareerImplantCommerceBinding catalogBinding);
    CharacterCareerImplantCommerceCommand Confirm(CharacterCareerImplantCommercePreview preview);
    CharacterCareerImplantCommerceResult Commit(OwnerContextStamp owner, CharacterCareerImplantCommerceCommand command,
        CancellationToken cancellationToken = default);
    CharacterCareerImplantCommerceResult Lookup(OwnerContextStamp currentOwner, CharacterWorkspaceId workspace,
        Guid originalOperationId, string originalCommandDigest);
    // Observes the original commit or durably prevents it. Never dispatches/reconstructs an economic operation.
    CharacterCareerImplantCommerceResult FinalizeDispatch(OwnerContextStamp currentOwner,
        CharacterCareerImplantCommerceCommand originalCommand, CancellationToken cancellationToken = default)
        => new(CharacterCareerImplantCommerceOutcome.ReconciliationRequired, Error: "implant_finalization_unavailable");
}

public interface ICharacterCareerImplantCommerceAuthority
{
    CharacterCareerImplantCommercePreparation ReadInstalled(string characterXml, long contentRevision);
    CharacterCareerImplantCommerceQuote? Quote(CharacterCareerImplantCommercePreparation preparation,
        CharacterCareerImplantCommerceRequest request, out string error);
    // Produces a candidate only; never durable success.
    string Materialize(string characterXml, CharacterCareerImplantCommerceQuote quote);
}

public interface ICharacterCareerImplantCommerceAtomicCommitCapability
{
    bool CareerImplantCommerceGuardsInstalled => false;
    string CareerImplantCommerceRuntimeDigest { get; }
    // The actual shared owner authority must hold this live lease for the complete call.
    CharacterCareerImplantCommerceResult CommitCareerImplantCommerce(IOwnerContextLease ownerLease,
        CharacterCareerImplantCommerceCommand command, ICharacterCareerImplantCommerceAuthority authority,
        CancellationToken cancellationToken = default);
    CharacterCareerImplantCommerceResult FinalizeCareerImplantCommerceDispatch(IOwnerContextLease ownerLease,
        CharacterCareerImplantCommerceCommand originalCommand, CancellationToken cancellationToken = default)
        => new(CharacterCareerImplantCommerceOutcome.ReconciliationRequired, Error: "implant_finalization_unavailable");
}
