namespace Chummer.Contracts.Characters;

public static class CharacterCreationCompletionPlanBlockers
{
    public const string RequestInvalid = "creation-completion-plan-request-invalid";
    public const string SnapshotConflict = "creation-completion-plan-snapshot-conflict";
    public const string ManualMagicMismatch = "creation-completion-plan-manual-magic-mismatch";
    public const string NegativeQualityPermissionRequired = "creation-completion-plan-negative-quality-permission-required";
    public const string KarmaReserveUnavailable = "creation-completion-plan-karma-reserve-unavailable";
    public const string KarmaReserveRequired = "creation-completion-plan-karma-reserve-required";
    public const string NuyenReserveRequired = "creation-completion-plan-nuyen-reserve-required";
}

/// <summary>
/// Explicit recommendations and permissions over one original observation.
/// Attribute/skill permitted orders are the caller's explicit unlock permissions,
/// not inferred provenance. Magic manual selections must be the original choices.
/// QualityOptionIds and Contacts are complete proposed lists, retaining originals;
/// neither is generated implicitly. Negative additions need explicit permission.
/// KarmaToReserve is the final shared reserve after contacts, Power Points and
/// the full resource investment, not a separate allowance for each domain.
/// </summary>
public sealed record CharacterCreationCompletionPlanRequest(
    string InputsSnapshotDigest,
    CharacterCreationAttributeCompletionRequest Attributes,
    CharacterCreationSkillCompletionRequest Skills,
    CharacterCreationMagicCompletionRequest Magic,
    IReadOnlyList<string> QualityOptionIds,
    IReadOnlyList<CharacterCreationKarmaContactSelection> Contacts,
    int TotalKarmaInvestment,
    IReadOnlyList<CharacterCreationPurchaseCompletionTarget> Purchases,
    Guid? StartingLifestyleId = null,
    int KarmaToReserve = 0,
    decimal NuyenToReserve = 0m,
    decimal MaximumAdditionalPurchaseSpend = 0m,
    int MaximumPurchaseEvaluations = 32,
    bool PermitNegativeQualityAdditions = false)
{
    /// <summary>Opt-in bounded attribute Karma allocation after composing all
    /// other choices. Requires Attributes.KarmaTargets and an explicit spend
    /// cap. Each level must pass the complete dependent review and final shared
    /// Karma/Nuyen reserves; magic and other choices are never re-selected.
    /// Permitted attribute IDs must be known untouched/automatic: original
    /// player choices (including zero) and unknown/imported intent stay locked.
    /// False retains legacy behavior and serialized request/digest identity.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AllocateRemainingAttributeKarma { get; init; }
}

/// <summary>
/// One detached, source-reviewed proposal composed from bounded deterministic
/// planners. Domain blockers describe unmet recommendations; Review describes the
/// resulting selections. Success is not complete-character readiness or an Apply
/// capability. Identity, new specializations, implants and dependent loadout
/// completion are not implemented here; finalization must validate the actual
/// document and every applicable requirement separately. No provider/AI claim.
/// </summary>
public sealed record CharacterCreationCompletionPlan(
    CharacterCreationCompletionPlanRequest Request,
    CharacterCreationCompletionSelections Selections,
    CharacterCreationAttributeCompletionPlan? Attributes,
    CharacterCreationSkillCompletionPlan? Skills,
    CharacterCreationMagicCompletionPlan? Magic,
    CharacterCreationPurchaseCompletionPlan? Purchases,
    CharacterCreationCompletionReview Review,
    IReadOnlyList<string> Blockers,
    string PlanDigest);

public static class CharacterCreationCompletionPlanRules
{
    public static string ComputePlanDigest(CharacterCreationCompletionPlan plan)
        => CharacterCreationFinalizationDigest.Compute(plan with { PlanDigest = string.Empty });
}
