using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

// Typed composite successors require the store's final live-source fence.
// No caller-supplied callback or serialized authority marker crosses this boundary.
public interface ICharacterCreationCompositeStageAtomicCommitCapability
{
    bool SupportsCompositeStageAtomicCommit { get; }

    WorkspaceStoreMutationResult CommitCompositeStage(OwnerScope owner, WorkspaceDocument replacement,
        CharacterCreationMagicResonancePreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries);

    WorkspaceStoreMutationResult CommitCompositeStage(OwnerScope owner, WorkspaceDocument replacement,
        CharacterCreationQualitiesPreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries);
    WorkspaceStoreMutationResult CommitCompositeStage(OwnerScope owner, WorkspaceDocument replacement,
        CharacterCreationResourcesPreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries);

    WorkspaceStoreMutationResult CommitCompositeStage(OwnerScope owner, WorkspaceDocument replacement,
        CharacterCreationGearPreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries);

    WorkspaceStoreMutationResult CommitCompositeStage(OwnerScope owner, WorkspaceDocument replacement,
        CharacterCreationContactPreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries);

    WorkspaceStoreMutationResult CommitCompositeStage(OwnerScope owner, WorkspaceDocument replacement,
        CharacterCreationLifestylePreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries);
}
