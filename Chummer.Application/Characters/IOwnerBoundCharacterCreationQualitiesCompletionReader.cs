using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Reads original completion inputs in one admitted owner partition.</summary>
public interface IOwnerBoundCharacterCreationQualitiesCompletionReader
{
    CharacterCreationFoundationResult<CharacterCreationQualitiesCompletionInputs> LoadCompletionInputs(
        OwnerContextStamp expectedOwner, CharacterCreationQualitiesLoadRequest request);
}
