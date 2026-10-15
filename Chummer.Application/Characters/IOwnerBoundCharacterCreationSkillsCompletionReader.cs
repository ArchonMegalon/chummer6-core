using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Reads completion inputs in the admitted owner's partition without saving
/// drafts or granting permission to confirm an ordinary Skills mutation.
/// </summary>
public interface IOwnerBoundCharacterCreationSkillsCompletionReader
{
    CharacterCreationFoundationResult<CharacterCreationSkillsCompletionInputs> LoadCompletionInputs(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request);
}
