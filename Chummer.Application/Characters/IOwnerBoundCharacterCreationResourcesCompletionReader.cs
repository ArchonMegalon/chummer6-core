using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Reads original Resources completion inputs in one admitted owner partition.</summary>
public interface IOwnerBoundCharacterCreationResourcesCompletionReader
{
    CharacterCreationResourcesResult<CharacterCreationResourcesCompletionInputs> LoadCompletionInputs(
        OwnerContextStamp expectedOwner, CharacterCreationResourcesLoadRequest request);
}
