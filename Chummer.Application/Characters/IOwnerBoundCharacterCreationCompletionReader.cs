using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface IOwnerBoundCharacterCreationCompletionReader
{
    CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputs(
        OwnerContextStamp expectedOwner,
        CharacterCreationSkillsLoadRequest request,
        CancellationToken cancellationToken = default);
}
