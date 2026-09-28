using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Skill allocation admitted by the exact owner stamp of the original
/// display. An old preview must never be rebound to the current account.
/// </summary>
public interface IOwnerBoundCharacterCreationSkillsService
{
    CharacterCreationFoundationResult<CharacterCreationSkillsState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationSkillsPreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsPreviewRequest request);

    CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsConfirmRequest request);
}
