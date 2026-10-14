using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Admits original Gear completion inputs in one exact owner partition.</summary>
public interface IOwnerBoundCharacterCreationGearCompletionReader
{
    CharacterCreationGearResult<CharacterCreationGearCompletionInputs> LoadCompletionInputs(
        OwnerContextStamp expectedOwner, CharacterCreationGearLoadRequest request);
}
