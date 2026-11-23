using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

// This is a store boundary, not a second receipt store. A non-rejection still
// requires original-command lookup in a freshly reopened, saved workspace.
public sealed record CharacterAugmentationPurchaseCommitResult(
    bool RejectedBeforeWrite, string? RejectionReason = null);

public interface ICharacterAugmentationPurchaseCommitCapability
{
    CharacterAugmentationPurchaseCommitResult CommitAugmentationPurchase(
        OwnerScope owner, CharacterWorkspaceId workspaceId,
        CharacterAugmentationPurchaseCommand command,
        ICharacterAugmentationPurchaseAuthority authority,
        CancellationToken cancellationToken = default);
}
