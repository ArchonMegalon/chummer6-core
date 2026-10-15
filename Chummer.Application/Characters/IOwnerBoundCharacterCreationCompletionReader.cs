using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface IOwnerBoundCharacterCreationCompletionReader
{
    CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputs(
        OwnerContextStamp expectedOwner,
        CharacterCreationSkillsLoadRequest request,
        CancellationToken cancellationToken = default);

    // Explicit opt-in keeps legacy completion snapshots and replay bytes stable.
    CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputsWithLoadout(
        OwnerContextStamp expectedOwner,
        CharacterCreationSkillsLoadRequest request,
        CancellationToken cancellationToken = default)
        => new(CharacterCreationFoundationOutcomes.Blocked, null,
            [CharacterCreationLoadoutCompletionSchemas.AuthorityUnavailable]);
}
