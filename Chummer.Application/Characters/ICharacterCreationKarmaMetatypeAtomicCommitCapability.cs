using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Characters;

/// <summary>Re-evaluates Core sources under the workspace lease; never accepts caller-built state.</summary>
public interface ICharacterCreationKarmaMetatypeAtomicCommitCapability
{
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> CommitKarmaMetatype(
        CharacterCreationKarmaMetatypeConfirmRequest request, ICharacterSourceDataResolver sourceResolver);

    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> CommitKarmaMetatype(
        OwnerScope owner, CharacterCreationKarmaMetatypeConfirmRequest request,
        ICharacterSourceDataResolver sourceResolver);
}
