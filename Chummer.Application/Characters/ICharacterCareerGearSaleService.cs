using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

// Historical API name retained for Sale source compatibility. OperationKind also admits
// explicit Remove requests through the same issuance, custody and recovery authority.
// Callers must durably journal the exact confirmed command/digest before Commit; unknown
// results require original-operation Lookup, never a newly issued replacement operation.
public interface ICharacterCareerGearSaleService
{
    CharacterCareerGearSaleReadResult Read(OwnerContextStamp owner, CharacterWorkspaceId workspace);
    CharacterCareerGearSalePreviewResult Preview(OwnerContextStamp owner,
        CharacterCareerGearSaleBinding displayed, CharacterCareerGearSaleRequest request);
    CharacterCareerGearSaleCommand Confirm(CharacterCareerGearSaleCommand preview);
    CharacterCareerGearSaleResult Commit(OwnerContextStamp owner, CharacterCareerGearSaleCommand command,
        CancellationToken cancellationToken = default);
    CharacterCareerGearSaleResult CancelUnadmittedOriginal(OwnerContextStamp currentOwner,
        CharacterCareerGearSaleCommand originalCommand, CancellationToken cancellationToken = default)
        => new(CharacterCareerGearSaleOutcome.Unavailable);
    // Read only. A fresh current stamp authorizes inspection, never replay of a historical stamp.
    CharacterCareerGearSaleResult Lookup(OwnerContextStamp owner, string originalOwnerId, bool trustedLocalOwner,
        CharacterWorkspaceId workspace, Guid operationId, string commandDigest, string incarnationId, string slotGeneration);
}

public interface ICharacterCareerGearSaleAtomicCommitCapability
{
    bool CareerGearSaleGuardsInstalled { get; }
    string CareerGearSaleRuntimeDigest { get; }
    CharacterCareerGearSaleReadResult ReadCareerGearSale(OwnerScope owner, CharacterWorkspaceId workspace,
        ICharacterSourceDataResolver sources);
    CharacterCareerGearSalePreviewResult PreviewCareerGearSale(OwnerScope owner,
        CharacterCareerGearSaleBinding displayed, CharacterCareerGearSaleRequest request, ICharacterSourceDataResolver sources);
    CharacterCareerGearSaleResult LookupCareerGearSale(OwnerScope owner, CharacterWorkspaceId workspace,
        Guid operationId, string commandDigest, string incarnationId, string slotGeneration);
    CharacterCareerGearSaleResult CommitCareerGearSale(OwnerScope owner,
        CharacterCareerGearSaleCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default);
    CharacterCareerGearSaleResult CancelUnadmittedCareerGearSale(OwnerScope owner,
        CharacterCareerGearSaleCommand originalCommand, CancellationToken cancellationToken = default)
        => new(CharacterCareerGearSaleOutcome.Unavailable);
}
