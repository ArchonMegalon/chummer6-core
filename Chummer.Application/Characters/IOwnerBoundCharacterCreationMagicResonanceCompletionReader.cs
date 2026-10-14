using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface IOwnerBoundCharacterCreationMagicResonanceCompletionReader
{
    CharacterCreationFoundationResult<CharacterCreationMagicResonanceCompletionInputs> LoadCompletionInputs(
        OwnerContextStamp expectedOwner, CharacterCreationMagicResonanceLoadRequest request);
}
