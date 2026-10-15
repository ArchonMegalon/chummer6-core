namespace Chummer.Contracts.Characters;

public static class CharacterCreationMagicCompletionBlockers
{
    public const string RequestInvalid = "creation-magic-completion-request-invalid";
    public const string SnapshotConflict = "creation-magic-completion-snapshot-conflict";
    public const string CandidateInvalid = "creation-magic-completion-candidate-invalid";
    public const string ManualSelectionConflict = "creation-magic-completion-manual-selection-conflict";
    public const string CandidateOrderExhausted = "creation-magic-completion-candidate-order-exhausted";
    public const string WorkLimitReached = "creation-magic-completion-work-limit-reached";
    public const string PowerPointSpendNotAllowed = "creation-magic-completion-power-point-spend-not-allowed";
    public const string KarmaReserveUnavailable = "creation-magic-completion-karma-reserve-unavailable";
}

/// <summary>
/// Ranked source identities are preferences, not rule authority. Manual choices
/// are retained exactly; a selected tradition/stream or purchased power level is
/// never replaced. The optional PP total is explicit, never inferred from spare
/// Karma. Its additional source-evaluated cost must fit both the cap and reserve.
/// EvaluationLimit includes initial/final budget checks and is capped by Core.
/// </summary>
public sealed record CharacterCreationMagicCompletionRequest(
    string MagicSnapshotDigest,
    string QualitiesSnapshotDigest,
    CharacterCreationMagicResonanceSelections ManualSelections,
    IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> RankedTraditions,
    IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> RankedStreams,
    IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> RankedSpells,
    IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> RankedComplexForms,
    IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> RankedAdeptPowers,
    int? DesiredTotalMysticAdeptPowerPoints = null,
    int MaximumAdditionalPowerPointKarmaSpend = 0,
    int KarmaToReserve = 0,
    int EvaluationLimit = 64);

/// <summary>
/// A detached, greedy ranked proposal, not an optimal build, saved draft or
/// finalization authority. IsMagicComplete means only that the Magic projection
/// has no blockers; other creation/quality/budget blockers may remain. No whole
/// character readiness or confirmation permission is implied.
/// </summary>
public sealed record CharacterCreationMagicCompletionPlan(
    CharacterCreationMagicCompletionRequest Request,
    IReadOnlyList<CharacterCreationAttributeAllocation> AttributeAllocations,
    IReadOnlyList<string> QualityOptionIds,
    CharacterCreationMagicResonanceSelections Selections,
    CharacterCreationMagicResonanceCompletionProjection Preview,
    CharacterCreationCompletionBudgetPreview? Budget,
    bool HasChanges,
    bool IsMagicComplete,
    int Evaluations,
    IReadOnlyList<string> Blockers,
    string PlanDigest);

public static class CharacterCreationMagicCompletionRules
{
    public static string ComputePlanDigest(CharacterCreationMagicCompletionPlan plan)
        => CharacterCreationMagicResonanceDigest.Compute(plan with { PlanDigest = string.Empty });
}
