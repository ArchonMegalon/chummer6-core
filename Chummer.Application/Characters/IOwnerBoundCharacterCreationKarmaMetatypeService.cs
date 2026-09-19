using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Keep the original owner stamp across display, review and confirmation.</summary>
public interface IOwnerBoundCharacterCreationKarmaMetatypeService
{
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> Load(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId id, bool includeSkills = false);
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaMetatypeBinding binding, string optionId,
        string? talentOptionId = null,
        IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? attributeAllocations = null,
        CharacterCreationKarmaSkillsSelection? skillsSelection = null);
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaMetatypeConfirmRequest request);
}
