using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

// Callers must durably journal the exact confirmed command/digest before Commit; unknown
// results require original-operation Lookup, never a newly issued replacement operation.
public interface ICharacterCareerSpecializationService
{
    CharacterCareerSpecializationReadResult Read(OwnerContextStamp owner, CharacterWorkspaceId workspace);
    CharacterCareerSpecializationPreviewResult Preview(OwnerContextStamp owner,
        CharacterCareerSpecializationBinding displayed, CharacterCareerSpecializationRequest request);
    CharacterCareerSpecializationCommand Confirm(CharacterCareerSpecializationCommand preview);
    CharacterCareerSpecializationResult Commit(OwnerContextStamp owner, CharacterCareerSpecializationCommand command,
        CancellationToken cancellationToken = default);
    CharacterCareerSpecializationResult CancelUnadmittedOriginal(OwnerContextStamp currentOwner,
        CharacterCareerSpecializationCommand originalCommand, CancellationToken cancellationToken = default)
        => new(CharacterCareerSpecializationOutcome.Unavailable);
    // Read only. A fresh current stamp authorizes inspection, never replay of a historical stamp.
    CharacterCareerSpecializationResult Lookup(OwnerContextStamp owner, string originalOwnerId, bool trustedLocalOwner,
        CharacterWorkspaceId workspace, Guid operationId, string commandDigest, string incarnationId, string slotGeneration);
}

public interface ICharacterCareerSpecializationAtomicCommitCapability
{
    bool CareerSpecializationGuardsInstalled { get; }
    string CareerSpecializationRuntimeDigest { get; }
    CharacterCareerSpecializationReadResult ReadCareerSpecialization(OwnerScope owner, CharacterWorkspaceId workspace,
        ICharacterSourceDataResolver sources);
    CharacterCareerSpecializationPreviewResult PreviewCareerSpecialization(OwnerScope owner,
        CharacterCareerSpecializationBinding displayed, CharacterCareerSpecializationRequest request, ICharacterSourceDataResolver sources);
    CharacterCareerSpecializationResult LookupCareerSpecialization(OwnerScope owner, CharacterWorkspaceId workspace,
        Guid operationId, string commandDigest, string incarnationId, string slotGeneration);
    CharacterCareerSpecializationResult CommitCareerSpecialization(OwnerScope owner,
        CharacterCareerSpecializationCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default);
    CharacterCareerSpecializationResult CancelUnadmittedCareerSpecialization(OwnerScope owner,
        CharacterCareerSpecializationCommand originalCommand, CancellationToken cancellationToken = default)
        => new(CharacterCareerSpecializationOutcome.Unavailable);
}
