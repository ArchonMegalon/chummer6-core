using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCareerGearEquipmentService
{
    CharacterCareerGearEquipmentReadResult Read(OwnerContextStamp owner, CharacterWorkspaceId workspace);
    // Compatibility for existing providers; the local store additionally cancels
    // its admitted read and releases the workspace lease when the page leaves.
    CharacterCareerGearEquipmentReadResult Read(OwnerContextStamp owner, CharacterWorkspaceId workspace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = Read(owner, workspace);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
    CharacterCareerGearEquipmentPreviewResult Preview(OwnerContextStamp owner,
        CharacterCareerGearEquipmentBinding displayed, CharacterCareerGearEquipmentRequest request);
    CharacterCareerGearEquipmentResult Commit(OwnerContextStamp owner, CharacterCareerGearEquipmentCommand command,
        CancellationToken cancellationToken = default);
    CharacterCareerGearEquipmentResult CancelUnadmittedOriginal(OwnerContextStamp currentOwner,
        CharacterCareerGearEquipmentCommand originalCommand, CancellationToken cancellationToken = default)
        => new(CharacterCareerGearEquipmentOutcome.Unavailable);
    // Read only. A fresh current stamp authorizes inspection, never replay of a historical stamp.
    CharacterCareerGearEquipmentResult Lookup(OwnerContextStamp owner, string originalOwnerId, bool trustedLocalOwner,
        CharacterWorkspaceId workspace, Guid operationId, string commandDigest, string incarnationId, string slotGeneration);
}

public interface ICharacterCareerGearEquipmentAtomicCommitCapability
{
    bool CareerGearEquipmentGuardsInstalled { get; }
    string CareerGearEquipmentRuntimeDigest { get; }
    CharacterCareerGearEquipmentReadResult ReadCareerGearEquipment(OwnerScope owner, CharacterWorkspaceId workspace,
        ICharacterSourceDataResolver sources);
    CharacterCareerGearEquipmentPreviewResult PreviewCareerGearEquipment(OwnerScope owner,
        CharacterCareerGearEquipmentBinding displayed, CharacterCareerGearEquipmentRequest request, ICharacterSourceDataResolver sources);
    CharacterCareerGearEquipmentResult LookupCareerGearEquipment(OwnerScope owner, CharacterWorkspaceId workspace,
        Guid operationId, string commandDigest, string incarnationId, string slotGeneration);
    CharacterCareerGearEquipmentResult CommitCareerGearEquipment(OwnerScope owner,
        CharacterCareerGearEquipmentCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default);
    CharacterCareerGearEquipmentResult CancelUnadmittedCareerGearEquipment(OwnerScope owner,
        CharacterCareerGearEquipmentCommand originalCommand, CancellationToken cancellationToken = default)
        => new(CharacterCareerGearEquipmentOutcome.Unavailable);
}
