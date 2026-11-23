using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface IOwnerBoundCharacterCreationCompletionReader
{
    /// <summary>Synchronous read-only planning under one fresh owner/source admission.
    /// The callback must return detached data, never an execution context or a Task.</summary>
    CharacterCreationFoundationResult<T> EvaluateCompletePlanning<T>(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request,
        Func<CharacterCreationAdmittedCompletionContext, CharacterCreationFoundationResult<T>> evaluate,
        CancellationToken cancellationToken = default)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(CharacterCreationFoundationOutcomes.Blocked, default,
            [CharacterCreationCompletionReviewSchemas.AuthorityUnavailable]);
    }

    CharacterCreationFoundationResult<CharacterCreationRuleSourcePreparation> LoadCompletionInputsWithInitiativeSourceExecution(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request,
        bool includeLoadout = false, bool includeSourcebooks = false, CancellationToken cancellationToken = default)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationPhysicalInitiativeAuthority.Unavailable]);

    CharacterCreationFoundationResult<CharacterCreationRuleSourcePreparation> LoadCompletionInputsWithMovementSourceExecution(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request,
        bool includeLoadout = false, bool includeSourcebooks = false, CancellationToken cancellationToken = default)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationCompletionReviewSchemas.AuthorityUnavailable]);

    CharacterCreationFoundationResult<CharacterCreationRuleSourcePreparation> LoadCompletionInputsWithSourcebookSelectionExecution(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request,
        bool includeLoadout = false, CancellationToken cancellationToken = default)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationSourcebookSelectionAuthority.Unavailable]);

    CharacterCreationFoundationResult<CharacterCreationRuleSourcePreparation> LoadCompletionInputsWithRuleSourceExecution(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request, bool includeLoadout = false,
        CancellationToken cancellationToken = default)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationCompletionReviewSchemas.AuthorityUnavailable]);

    CharacterCreationFoundationResult<CharacterCreationRuleSourcePreparation> LoadCompletionInputsWithPurchaseSourceExecution(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request, bool includeLoadout = false,
        CancellationToken cancellationToken = default)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationCompletionReviewSchemas.AuthorityUnavailable]);

    // Explicit opt-in. An older reader must not synthesize an original snapshot.
    CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputsWithOriginalCharacter(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request, bool includeLoadout = false,
        CancellationToken cancellationToken = default)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationCompletionReviewSchemas.AuthorityUnavailable]);

    // Source-accounted opt-in. Older readers cannot manufacture evaluated settings.
    CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputsWithPolicyConsumption(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request, bool includeLoadout = false,
        CancellationToken cancellationToken = default)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationEffectivePolicyContext.Unavailable]);

    // Explicit v2 read/review opt-in. Existing implementations cannot synthesize this evidence.
    CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputsWithConsumerPolicy(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request, bool includeLoadout = false,
        CancellationToken cancellationToken = default)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationEffectivePolicyContext.Unavailable]);

    // Explicit opt-in; old implementations cannot assert effective-policy coverage.
    CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputsWithEffectivePolicy(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request, bool includeLoadout = false,
        CancellationToken cancellationToken = default)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationEffectivePolicyContext.Unavailable]);

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
