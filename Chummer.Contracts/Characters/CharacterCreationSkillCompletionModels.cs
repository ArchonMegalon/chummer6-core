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
}

/// <summary>Kind is active, knowledge or group; SourceId is the exact admitted catalog identity.</summary>
public sealed record CharacterCreationSkillCompletionTarget(string Kind, string SourceId);

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
    int KnowledgePointsToReserve = 0);

/// <summary>
/// Read-only skill/group additions in the context of explicit proposed attributes.
/// Existing specializations and native language are preserved, never invented.
/// Missing prerequisites or unspent points remain explicit blockers. HasChanges
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
