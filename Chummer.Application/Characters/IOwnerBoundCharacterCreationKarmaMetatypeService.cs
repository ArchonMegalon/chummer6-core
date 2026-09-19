using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Keep the original owner stamp across display, review and confirmation.</summary>
public interface IOwnerBoundCharacterCreationKarmaMetatypeService
{
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> Load(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId id);
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaMetatypeBinding binding, string optionId,
        string? talentOptionId = null);
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaMetatypeConfirmRequest request);
}
