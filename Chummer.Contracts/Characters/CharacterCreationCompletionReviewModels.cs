using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationCompletionReviewSchemas
{
    public const string InputsV1 = "chummer.creation-completion-review-inputs.v1";
    public const string ReviewV1 = "chummer.creation-completion-review.v1";
    public const string InputsV2 = "chummer.creation-completion-review-inputs.v2";
    public const string ReviewV2 = "chummer.creation-completion-review.v2";
    public const string SnapshotConflict = "creation-completion-review-snapshot-conflict";
    public const string RequestInvalid = "creation-completion-review-request-invalid";
    public const string AuthorityUnavailable = "creation-completion-review-authority-unavailable";
    public const string SelectionPreservationConflict = "creation-completion-review-selection-preservation-conflict";
    public const string InvalidChangePermissions = "creation-completion-review-invalid-change-permissions";
    public const string KarmaBudgetUnavailable = "creation-completion-review-karma-budget-unavailable";
}

/// <summary>Original observations only. Lifestyles contains the original Qualities and Gear/Resources inputs.</summary>
public sealed record CharacterCreationCompletionReviewInputs(
    string Schema,
    CharacterCreationSkillsCompletionInputs Skills,
    CharacterCreationMagicResonanceCompletionInputs Magic,
    CharacterCreationLifestylesCompletionInputs Lifestyles,
    CharacterCreationContactsCompletionInputs Contacts,
    string SnapshotDigest)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLoadoutCompletionInputs? Loadout { get; init; }
}

/// <summary>Complete hypothetical choices. Explicit change permissions are part of the reviewed request.</summary>
public sealed record CharacterCreationCompletionSelections(
    IReadOnlyList<CharacterCreationAttributeAllocation> AttributeAllocations,
    IReadOnlyList<CharacterCreationSkillAllocation> SkillAllocations,
    IReadOnlyList<CharacterCreationSkillGroupAllocation> GroupAllocations,
    CharacterCreationMagicResonanceSelections MagicSelections,
    IReadOnlyList<string> QualityOptionIds,
    int TotalKarmaInvestment,
    IReadOnlyList<CharacterCreationGearSelection> Gear,
    IReadOnlyList<CharacterCreationKarmaContactSelection> Contacts,
    IReadOnlyList<CharacterCreationLifestyleConfiguration> Lifestyles,
    Guid? StartingLifestyleId)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLoadoutSelections? Loadout { get; init; }

    public IReadOnlyList<string> ChangeableAttributeIds { get; init; } = [];
    /// <summary>Overrides locks on existing skill rows, not an allowlist for new individual skills.
    /// New or increased groups require permission for the group and every active member.</summary>
    public IReadOnlyList<CharacterCreationSkillCompletionTarget> ChangeableSkillTargets { get; init; } = [];
}

public sealed record CharacterCreationCompletionReviewRequest(
    string InputsSnapshotDigest,
    CharacterCreationCompletionSelections Selections);

/// <summary>Full proposed spending, not saved-cost deltas. Null means unavailable, never zero.</summary>
public sealed record CharacterCreationCompletionPoolBalances(
    int CreationKarmaTotal,
    int? KarmaAfterAttributesAndQualities,
    int? SkillsKarmaCost,
    int? MysticAdeptPowerPointKarmaCost,
    int? ResourcesKarmaInvestment,
    int? ContactsKarmaCost,
    int? RemainingCreationKarma,
    int? KarmaOverspend,
    decimal? CreationNuyen,
    decimal? GearCost,
    decimal? LifestyleCost,
    decimal? RemainingNuyen,
    decimal? NuyenOverspend)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? LoadoutCost { get; init; }
}

/// <summary>A detached rules review, not an ordinary preview, pending draft, receipt or permission to save.</summary>
public sealed record CharacterCreationCompletionReview(
    string Schema,
    string InputsSnapshotDigest,
    CharacterCreationCompletionReviewRequest Request,
    CharacterCreationAttributeSkillsPreview? Skills,
    CharacterCreationCompletionBudgetPreview? UpstreamBudget,
    CharacterCreationLifestylesCompletionProjection? Lifestyles,
    CharacterCreationContactsCompletionProjection? Contacts,
    CharacterCreationCompletionPoolBalances Balances,
    IReadOnlyList<string> Blockers,
    string ReviewDigest)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLoadoutCompletionProjection? Loadout { get; init; }
}

public static class CharacterCreationCompletionReviewRules
{
    public static string ComputeInputsDigest(CharacterCreationCompletionReviewInputs inputs)
        => CharacterCreationFinalizationDigest.Compute(inputs with { SnapshotDigest = string.Empty });

    public static string ComputeReviewDigest(CharacterCreationCompletionReview review)
        => CharacterCreationFinalizationDigest.Compute(review with { ReviewDigest = string.Empty });
}
