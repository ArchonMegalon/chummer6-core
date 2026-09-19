using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCreationKarmaMetatypeService
{
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> Load(CharacterWorkspaceId id);
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> Preview(
        CharacterCreationKarmaMetatypeBinding binding, string optionId, string? talentOptionId = null,
        IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? attributeAllocations = null);
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> Confirm(
        CharacterCreationKarmaMetatypeConfirmRequest request);
}
