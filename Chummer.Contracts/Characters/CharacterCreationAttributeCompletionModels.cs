namespace Chummer.Contracts.Characters;

public static class CharacterCreationAttributeCompletionBlockers
{
    public const string RequestInvalid = "creation-attribute-completion-request-invalid";
    public const string SnapshotConflict = "creation-attribute-completion-snapshot-conflict";
    public const string NormalPointsUnallocated = "creation-attribute-completion-normal-points-unallocated";
    public const string SpecialPointsUnallocated = "creation-attribute-completion-special-points-unallocated";
    public const string WorkLimitReached = "creation-attribute-completion-work-limit-reached";
    public const string TargetsUnfulfilled = "creation-attribute-completion-targets-unfulfilled";
    public const string KarmaTargetsRequired = "creation-attribute-completion-karma-targets-required";
    public const string KarmaTargetsUnfulfilled = "creation-attribute-completion-karma-targets-unfulfilled";
    public const string KarmaSpendLimit = "creation-attribute-completion-karma-spend-limit";
    public const string AutomaticPermissionUnavailable = "creation-attribute-completion-automatic-permission-unavailable";
}

/// <summary>Desired unaugmented total, including source grants and retained Karma
/// levels. This is an additive goal: an existing higher value is never lowered.</summary>
public sealed record CharacterCreationAttributeCompletionTarget(string AttributeId, int TargetUnaugmentedRating);

/// <summary>
/// Explicit permission to add priority points, in preference order. Omitted IDs
/// stay locked, including attributes at their default value. Callers must not
/// infer permission from a zero allocation or from unknown/imported provenance.
/// Reserves are points to leave unallocated in each independent point pool.
/// </summary>
public sealed record CharacterCreationAttributeCompletionRequest(
    CharacterCreationAttributesBinding Binding,
    string SnapshotDigest,
    IReadOnlyList<string> PermittedAttributeOrder,
    int NormalPointsToReserve = 0,
    int SpecialPointsToReserve = 0)
{
    /// <summary>Optional bounded goals in allocation order, separate from the
    /// permission list. Null retains the legacy fill-to-cap behavior; an empty
    /// list makes no additions. Every goal must be explicitly permitted. Legal
    /// unused points are retained when all goals have been met.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterCreationAttributeCompletionTarget>? Targets { get; init; }

    /// <summary>Opt-in completion of the remaining permitted point pools. Targets
    /// are minimum goals, attempted first; remaining points are then added one
    /// per permitted attribute per round, retaining independent reserves. With
    /// no targets (null or empty), rounds start from the unchanged baseline.
    /// This never expands permissions or spends Karma. False preserves the
    /// existing target-only/legacy behavior and serialized request identity.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool FillRemainingPoints { get; init; }

    /// <summary>Explicit useful unaugmented totals for the whole-character Karma
    /// phase, in preference order. Every identity must also be permitted. Null
    /// or empty supplies no spending goals; neither implies fill-to-cap. The
    /// attribute-only planner never spends Karma. Only the whole request's
    /// AllocateRemainingAttributeKarma flag activates these goals.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterCreationAttributeCompletionTarget>? KarmaTargets { get; init; }

    /// <summary>Maximum additional source-priced attribute Karma across all
    /// KarmaTargets, after Priority allocation. Zero allows no spending. The
    /// whole-character shared wallet and reserve can further reduce this cap.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int MaximumAdditionalKarmaSpend { get; init; }
}

/// <summary>
/// A detached, deterministic attribute-only proposal, not AI output, a save
/// authorization, or a completed character. A blocked result can retain a legal
/// partial proposal and unspent budgets; HasChanges=false never requires a save.
/// Even a successful plan needs fresh owner-bound Preview/Confirm before use.
/// The outer completion flow must separately bind locks, owner and preferences
/// and validate every other category before offering atomic application.
/// </summary>
public sealed record CharacterCreationAttributeCompletionPlan(
    CharacterCreationAttributeCompletionRequest Request,
    IReadOnlyList<CharacterCreationAttributeAllocation> Allocations,
    CharacterCreationAttributesPreview Preview,
    bool HasChanges,
    int Evaluations,
    IReadOnlyList<string> Blockers);
