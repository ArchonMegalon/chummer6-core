using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Characters;

public interface ICharacterCreationKarmaFinalizationAtomicCommitCapability
{
    CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitKarmaFinalization(
        CharacterCreationKarmaFinalizationConfirmRequest request, ICharacterSourceDataResolver sourceResolver);
    CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitKarmaFinalization(
        OwnerScope owner, CharacterCreationKarmaFinalizationConfirmRequest request, ICharacterSourceDataResolver sourceResolver);
}
