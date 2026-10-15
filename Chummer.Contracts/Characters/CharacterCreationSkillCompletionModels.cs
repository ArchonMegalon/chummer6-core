namespace Chummer.Contracts.Characters;

public static class CharacterCreationSkillCompletionKinds
{
    public const string Group = "group";
}

public static class CharacterCreationSkillCompletionBlockers
{
    public const string RequestInvalid = "creation-skill-completion-request-invalid";
    public const string SnapshotConflict = "creation-skill-completion-snapshot-conflict";
    public const string ActivePointsUnallocated = "creation-skill-completion-active-points-unallocated";
    public const string GroupPointsUnallocated = "creation-skill-completion-group-points-unallocated";
    public const string KnowledgePointsUnallocated = "creation-skill-completion-knowledge-points-unallocated";
    public const string GroupMemberLocked = "creation-skill-completion-group-member-locked";
    public const string WorkLimitReached = "creation-skill-completion-work-limit-reached";
    public const string NativeLanguageChoiceConflict = "creation-skill-completion-native-language-choice-conflict";
    public const string TargetsUnfulfilled = "creation-skill-completion-targets-unfulfilled";
}

/// <summary>Kind is active, knowledge or group; SourceId is the exact admitted catalog identity.</summary>
public sealed record CharacterCreationSkillCompletionTarget(string Kind, string SourceId);

/// <summary>Desired source-grant-inclusive effective rating. A higher existing
/// rating is retained; native languages remain non-numeric identity choices.</summary>
public sealed record CharacterCreationSkillCompletionRatingTarget(string Kind, string SourceId, int TargetRating);

/// <summary>Read-only observation of one goal against an already evaluated Core
/// preview. A native language remains nonnumeric; unknown source identities have
/// no effective rating. This is not source admission or permission to save.</summary>
public sealed record CharacterCreationSkillCompletionTargetEvaluation(
    CharacterCreationSkillCompletionRatingTarget Target,
    bool IsKnown,
    int? EffectiveRating,
    bool IsSatisfied);

/// <summary>
/// Explicit permission and deterministic preference order, not an archetype or
/// optimization claim. Omitted choices, including zero/default choices, stay
/// locked. A group requires permission for every canonical active member too;
/// known unavailable members may be listed as permissions but Core still rejects
/// unavailable purchases. Reserves apply independently to all three point pools.
/// </summary>
public sealed record CharacterCreationSkillCompletionRequest(
    CharacterCreationSkillsBinding Binding,
    string AttributesSnapshotDigest,
    string SkillsSnapshotDigest,
    IReadOnlyList<CharacterCreationSkillCompletionTarget> PermittedTargetOrder,
    int ActivePointsToReserve = 0,
    int GroupPointsToReserve = 0,
    int KnowledgePointsToReserve = 0)
{
    /// <summary>
    /// Optional explicit candidate for an unfilled native-language choice.
    /// Null leaves it unchanged. This never replaces an existing language or
    /// converts a purchased language rating; Core checks source eligibility.
    /// </summary>
    public string? NativeLanguageSourceId { get; init; }

    /// <summary>Optional numeric goals in allocation order. Null retains legacy
    /// fill-to-cap behavior; an empty list makes no numeric additions. Permissions
    /// remain explicit and separate, so a group member can be permitted without
    /// requesting an individual allocation. Every goal must be permitted. Legal
    /// unused points are retained when the requested goals have been met.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterCreationSkillCompletionRatingTarget>? Targets { get; init; }

    /// <summary>Opt-in completion after attempting the numeric target floors.
    /// Adds at most one rating per permitted choice per round, groups first,
    /// while retaining each point reserve. Members of a positive group are not
    /// independently raised by the fill pass. Null/empty targets start rounds
    /// from the baseline; permissions, native identity and source rules are
    /// unchanged. False preserves existing behavior and serialized identity.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool FillRemainingPoints { get; init; }
}

/// <summary>
/// Read-only skill/group additions in the context of explicit proposed attributes.
/// Existing specializations and native language are preserved. A missing native
/// language is filled only from the request's explicit admitted source identity.
/// Missing prerequisites, unmet targets or legacy unspent points remain explicit blockers. HasChanges
/// describes skill/group changes only; the outer assistant owns attribute locks.
/// This is neither a receipt nor authority to save/finalize a character.
/// </summary>
public sealed record CharacterCreationSkillCompletionPlan(
    CharacterCreationSkillCompletionRequest Request,
    IReadOnlyList<CharacterCreationAttributeAllocation> AttributeAllocations,
    IReadOnlyList<CharacterCreationSkillAllocation> Allocations,
    IReadOnlyList<CharacterCreationSkillGroupAllocation> GroupAllocations,
    CharacterCreationAttributeSkillsPreview Preview,
    bool HasChanges,
    int Evaluations,
    IReadOnlyList<string> Blockers,
    string PlanDigest);
