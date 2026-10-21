using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

// The existing atomic auxiliary receipt path plus a final live-source fence,
// confined to the pre1008 -> locations1008 Lifestyle confirmation.
public interface ICharacterCreationLifestyleAuthorityReviewCommitCapability
{
    bool SupportsLifestyleAuthorityReviewCommit { get; }
    WorkspaceStoreMutationResult CommitLifestyleAuthorityReview(OwnerScope owner, WorkspaceDocument replacement,
        CharacterCreationLifestylePreview preview, ICharacterSourceDataResolver resolver);
}
