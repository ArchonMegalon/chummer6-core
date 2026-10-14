using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Admits original Lifestyle inputs in one exact owner partition.</summary>
public interface IOwnerBoundCharacterCreationLifestylesCompletionReader
{
    CharacterCreationLifestyleResult<CharacterCreationLifestylesCompletionInputs> LoadCompletionInputs(
        OwnerContextStamp expectedOwner, CharacterCreationLifestylesLoadRequest request);
}
