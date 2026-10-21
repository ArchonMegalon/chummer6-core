using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Characters;

public interface ICharacterCreationCompletionDraftAtomicCommitCapability
{
    CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> CommitCompletionDraft(
        OwnerScope owner, CharacterCreationCompletionDraftApplyRequest request,
        ICharacterSourceDataResolver resolver, ICharacterFileQueries queries,
        CancellationToken cancellationToken = default);

    CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> CommitCompletionDraft(
        OwnerScope owner, CharacterCreationCompletionDraftApplyRequest request,
        ICharacterSourceDataResolver resolver, ICharacterFileQueries queries,
        CharacterCreationCampaignExecutionContext campaign, CancellationToken cancellationToken = default)
        => CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationCampaignExecutionContext.Blocker);
}
