namespace Chummer.Contracts.Characters;

public static class CharacterCreationCompletionBaselineBlockers
{
    public const string RequestInvalid = "creation-completion-baseline-request-invalid";
    public const string CompleteReviewRequired = "creation-completion-baseline-complete-review-required";
    public const string NoCompleteCandidate = "creation-completion-baseline-no-complete-candidate";
    public const string SearchLimitReached = "creation-completion-baseline-search-limit-reached";
    public const string CatalogLimitReached = "creation-completion-baseline-catalog-limit-reached";
}

/// <summary>
/// A bounded feasibility request, not an effectiveness or optimality claim.
/// Plan retains all explicit locks, goals, spending limits and source pins.
/// Catalog permission allows missing magic choices from the current enabled
/// catalog; it neither replaces manual choices nor authorizes buying Power Points.
/// Native language, contacts, qualities, investment and equipment remain explicit
/// Plan choices. No positive or negative quality is invented to exhaust Karma.
/// </summary>
public sealed record CharacterCreationCompletionBaselineRequest(
    CharacterCreationCompletionPlanRequest Plan,
    int MaximumCandidateEvaluations = 8,
    bool PermitCatalogMagicCompletion = false);

/// <summary>
/// A fully reviewed feasible baseline for the supplied goals. Only the strict
/// baseline planner returns this value, after canonical finalization projection.
/// The projection is detached comparison evidence, not owner/save/Career authority.
/// Role effectiveness remains a separate assessment. An incomplete search branch
/// is never returned as a baseline; the original character is never mutated.
/// </summary>
public sealed record CharacterCreationCompletionBaseline(
    CharacterCreationCompletionPlan Plan,
    CharacterCreationCompletionFinalizationProjection Finalization,
    int CandidateEvaluations);
