using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCreationKarmaMetatypeService
{
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> Load(CharacterWorkspaceId id);
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> Preview(
        CharacterCreationKarmaMetatypeBinding binding, string optionId);
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> Confirm(
        CharacterCreationKarmaMetatypeConfirmRequest request);
}
