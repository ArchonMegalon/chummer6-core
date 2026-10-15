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
}
