using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Characters;

/// <summary>Storage-owned ordinary finalization with live source admission under the commit lease.</summary>
public interface ICharacterCreationFinalizationAtomicCommitCapability
{
    CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> CommitFinalization(
        OwnerScope owner, CharacterCreationFinalizationConfirmRequest request,
        ICharacterSourceDataResolver resolver, ICharacterFileQueries queries);
}
