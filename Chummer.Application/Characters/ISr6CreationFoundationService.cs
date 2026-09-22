using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ISr6CreationFoundationService
{
    CharacterCreationFoundationResult<Sr6CreationFoundationState> Load(OwnerContextStamp owner, CharacterWorkspaceId workspaceId);
    CharacterCreationFoundationResult<Sr6CreationFoundationPreview> Preview(OwnerContextStamp owner,
        Sr6CreationFoundationBinding binding, Sr6CreationFoundationSelection selection);
    /// <summary>Materializes the exact saved choices without saving or finalizing the runner.</summary>
    CharacterCreationFoundationResult<Sr6CreationCharacterProjection> ProjectCharacter(OwnerContextStamp owner,
        Sr6CreationFoundationBinding binding);
    CharacterCreationFoundationResult<Sr6CreationFinalizationReview> ReviewFinalization(OwnerContextStamp owner,
        Sr6CreationFoundationBinding binding);
    CharacterCreationFoundationResult<Sr6CreationFinalizationCommit> ConfirmFinalization(OwnerContextStamp owner,
        Sr6CreationFinalizationRequest request);
    CharacterCreationFoundationResult<Sr6CreationFinalizationReceipt> LoadFinalization(OwnerContextStamp owner,
        CharacterWorkspaceId workspaceId);
    CharacterCreationFoundationResult<Sr6CreationFoundationCommit> Confirm(OwnerContextStamp owner,
        Sr6CreationFoundationConfirmRequest request);
}

/// <summary>Source revalidation and compare-and-swap under the existing workspace lease.</summary>
public interface ISr6CreationFoundationAtomicCommitCapability
{
    CharacterCreationFoundationResult<Sr6CreationFoundationCommit> CommitSr6Foundation(
        OwnerScope owner, Sr6CreationFoundationConfirmRequest request);
}

public interface ISr6CreationFinalizationAtomicCommitCapability
{
    CharacterCreationFoundationResult<Sr6CreationFinalizationCommit> CommitSr6Finalization(
        OwnerScope owner, Sr6CreationFinalizationRequest request);
}
