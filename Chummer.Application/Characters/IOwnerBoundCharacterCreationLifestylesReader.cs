using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Reads the Creation lifestyle projection under the displayed owner's live admission.</summary>
public interface IOwnerBoundCharacterCreationLifestylesReader
{
    CharacterCreationLifestyleResult<CharacterCreationLifestylesState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationLifestylesLoadRequest request);
}
