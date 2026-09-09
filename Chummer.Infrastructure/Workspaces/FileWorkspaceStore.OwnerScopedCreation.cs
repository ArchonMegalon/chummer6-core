using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore :
    IOwnerScopedCharacterCreationBootstrapAtomicCreateCapability,
    IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability
{
    public bool SupportsOwnerScopedCharacterCreationBootstrapAtomicCreate => true;

    // The existing owner-scoped implementation compares revision and auxiliary
    // digest, validates the typed transition, and checkpoints in one commit.
    public bool SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit => true;

    public WorkspaceStoreMutationResult CreateCharacterCreationBootstrapWorkspaceDocument(
        OwnerScope owner,
        CharacterWorkspaceId id,
        WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (IsInvalidScopedOwner(owner))
            return InvalidOwnerMutation();

        return CharacterCreationBootstrapStoreIntegrity.IsValidInitialState(id, document)
            ? CreateWorkspaceDocumentCore(owner, id, document, allowBootstrapAuxiliaryState: true)
            : UnavailableMutation("Character creation bootstrap state is invalid.");
    }
}
