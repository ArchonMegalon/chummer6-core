using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Characters;

public interface ICharacterCreationCompletionAtomicCommitCapability
{
    CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitCompletion(
        CharacterCreationCompletionApplyRequest request, ICharacterSourceDataResolver sourceResolver,
        ICharacterFileQueries characterQueries, CancellationToken cancellationToken = default);

    CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitCompletion(
        OwnerScope owner, CharacterCreationCompletionApplyRequest request,
        ICharacterSourceDataResolver sourceResolver, ICharacterFileQueries characterQueries,
        CancellationToken cancellationToken = default);
    // Existing stores must explicitly implement the campaign path. The default
    // never silently calls an implementation that drops the live authority.
    CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitCompletion(
        CharacterCreationCompletionApplyRequest request, ICharacterSourceDataResolver sourceResolver,
        ICharacterFileQueries characterQueries, CharacterCreationCampaignExecutionContext campaign,
        CancellationToken cancellationToken = default)
        => CharacterCreationCompletionTransaction.Blocked(CharacterCreationCampaignExecutionContext.Blocker);

    CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitCompletion(
        OwnerScope owner, CharacterCreationCompletionApplyRequest request,
        ICharacterSourceDataResolver sourceResolver, ICharacterFileQueries characterQueries,
        CharacterCreationCampaignExecutionContext campaign, CancellationToken cancellationToken = default)
        => CharacterCreationCompletionTransaction.Blocked(CharacterCreationCampaignExecutionContext.Blocker);
}
