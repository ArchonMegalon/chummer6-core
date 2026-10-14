using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface IOwnerBoundCharacterCreationContactsCompletionReader
{
    CharacterCreationContactResult<CharacterCreationContactsCompletionInputs> LoadCompletionInputs(
        OwnerContextStamp expectedOwner, CharacterCreationContactsLoadRequest request);
}
