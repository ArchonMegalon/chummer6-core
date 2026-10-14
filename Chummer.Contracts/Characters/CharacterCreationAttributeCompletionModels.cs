namespace Chummer.Contracts.Characters;

public static class CharacterCreationAttributeCompletionBlockers
{
    public const string RequestInvalid = "creation-attribute-completion-request-invalid";
    public const string SnapshotConflict = "creation-attribute-completion-snapshot-conflict";
    public const string NormalPointsUnallocated = "creation-attribute-completion-normal-points-unallocated";
    public const string SpecialPointsUnallocated = "creation-attribute-completion-special-points-unallocated";
    public const string WorkLimitReached = "creation-attribute-completion-work-limit-reached";
}

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
    int SpecialPointsToReserve = 0);

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
