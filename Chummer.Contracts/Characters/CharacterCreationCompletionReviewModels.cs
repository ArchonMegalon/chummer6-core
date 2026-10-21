using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationCompletionReviewSchemas
{
    public const string InputsV1 = "chummer.creation-completion-review-inputs.v1";
    public const string ReviewV1 = "chummer.creation-completion-review.v1";
    public const string InputsV2 = "chummer.creation-completion-review-inputs.v2";
    public const string ReviewV2 = "chummer.creation-completion-review.v2";
    public const string InputsV3 = "chummer.creation-completion-review-inputs.v3";
    public const string ReviewV3 = "chummer.creation-completion-review.v3";
    public const string ProfileSettingsExecutionV1 = "chummer.creation-profile-settings-execution.v1";
    public const string MaximumAttributesDefaultExecutionV1 = "chummer.creation-maximum-attributes-default-execution.v1";
    public const string RuleSourceExecutionV1 = "chummer.creation-rule-source-execution.v1";
    public const string RuleSourceExecutionV2 = "chummer.creation-rule-source-execution.v2";
    // Opt-in movement and supporting source consumers; V1/V2 replay is unchanged.
    public const string RuleSourceExecutionV3 = "chummer.creation-rule-source-execution.v3";
    public const string RuleSourceExecutionV4 = "chummer.creation-rule-source-execution.v4";
    public const string OriginalCharacterExecutionV1 = "chummer.creation-original-character-execution.v1";
    public const string AdditionalConsumerExecutionV1 = "chummer.creation-additional-consumer-execution.v1";
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
    public CharacterCreationPhysicalInitiativeAuthority? PhysicalInitiative886 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuleSourceExecutionSchema { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuleSourceCaptureDigest { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationSourcebookSelectionAuthority? SourcebookSelection { get; init; }

    /// <summary>The effective editable selection origin. Ordinary stage snapshots
    /// retain their source-admitted receipt history; proposals start from this
    /// selection set when present, including explicit paid skill purchases.</summary>
    [JsonIgnore]
    public CharacterCreationCompositeBaseline? CompositeBaseline => Skills.CompositeBaseline;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLoadoutCompletionInputs? Loadout { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationEffectivePolicyContext? EffectivePolicy { get; init; }

    /// <summary>Only the consumer-policy opt-in captures this canonical source input.
    /// Null means not captured; it never supplies default house-rule flags.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationAttributePolicy? AttributePolicy { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLoadoutLegalityPolicy? LoadoutLegalityPolicy { get; init; }

    // Captured only by the explicit original-character reader. Portable input,
    // never owner/source admission or permission to save.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationCompletionOriginalCharacter? OriginalCharacter { get; init; }
}

/// <summary>The exact observed bytes and bootstrap inputs, without local owner or
/// history capabilities. Selected replay uses its archive instead of copying this.</summary>
public sealed record CharacterCreationCompletionOriginalCharacter(
    string Schema,
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string RawCharacterXml,
    WorkspaceDocumentAuxiliaryState AuxiliaryState,
    JsonElement BootstrapSourceSnapshot);

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
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterCreationSkillKarmaPurchase>? SkillKarmaPurchases { get; init; }

    /// <summary>Unspent Creation Karma before the ordinary carryover cap.
    /// Purchase plans bind their shared reserve into review and Apply.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int KarmaToReserve { get; init; }


    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLoadoutSelections? Loadout { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterCreationAmmunitionAssignment>? AmmunitionAssignments { get; init; }

    // Cumulative manual choices, including explicitly retained zero allocations.
    // Null retains the unknown legacy meaning and the exact old wire/digest.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? PlayerChosenAttributeIds { get; init; }

    public IReadOnlyList<string> ChangeableAttributeIds { get; init; } = [];
    /// <summary>Overrides locks on existing skill rows, not an allowlist for new individual skills.
    /// New or increased groups require permission for the group and every active member.</summary>
    public IReadOnlyList<CharacterCreationSkillCompletionTarget> ChangeableSkillTargets { get; init; } = [];
}

public sealed record CharacterCreationCompletionReviewRequest(
    string InputsSnapshotDigest,
    CharacterCreationCompletionSelections Selections)
{
    // Recorded intent only. It cannot authorize live replacement or campaign admission.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationAttributesReplacementIntent? AttributesReplacement { get; init; }

    // Comparison only; each live phase needs a separately reconstructed adapter.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationCampaignBinding? CampaignAuthority { get; init; }

    // Versioned accounting of additional canonical executions. This is not an
    // execution receipt or permission; omission preserves historical assessments.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AdditionalConsumerExecutionSchema { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuleSourceExecutionSchema { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuleSourceCaptureDigest { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationSourcebookSelectionAuthority? SourcebookSelection { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CharacterCreationPolicyRequirement PolicyRequirement { get; init; }

    // Explicit opt-in, including when coverage is optional. Binds selected
    // history and tells live admission to recapture the exact policy inventory.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EffectivePolicyDigest { get; init; }

    // Explicitly pins accounting in the archived request as well as in its evidence.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PolicyConsumptionSchema { get; init; }

    // Explicit request opt-in; old requests retain their exact serialized bytes.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CompleteAssessmentSchema { get; init; }

    // Requested execution variant, never source authority. The existing request
    // digest binds full review, selected replay and live proposal recapture.
    // Null retains the historical V4 absent-setting assessment and request bytes.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MaximumAttributesDefaultExecutionSchema { get; init; }

    // Opt-in execution only; null preserves historical request and capture bytes.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProfileSettingsExecutionSchema { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginalCharacterExecutionSchema { get; init; }
}

/// <summary>Portable manual intent, never an execution capability. Targets are the
/// complete ordinal-normalized allocation set. The request also binds all retained choices.</summary>
public sealed record CharacterCreationAttributesReplacementIntent(
    string Schema,
    CharacterCreationAttributesBinding Original,
    string OwnerScopeDigest,
    string OwnerStampDigest,
    string InputsSnapshotDigest,
    string OriginalSelectionsDigest,
    string TargetSelectionsDigest,
    IReadOnlyList<CharacterCreationAttributeAllocation> Targets)
{
    public const string SchemaV1 = "chummer.creation-attributes-replacement-intent.v1";
    public const string Invalid = "creation-attributes-replacement-intent-invalid";
    public override string ToString() => nameof(CharacterCreationAttributesReplacementIntent);
}

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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationPolicyConsumption? PolicyConsumption { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationCompleteAssessment? CompleteAssessment { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationSelectedPrerequisiteEvaluation? SelectedPrerequisites { get; init; }
}

public static class CharacterCreationCompletionReviewRules
{
    public static bool HasValidProfileSettingsExecution(CharacterCreationCompletionReviewRequest request)
        => request.ProfileSettingsExecutionSchema is null
            || request.ProfileSettingsExecutionSchema == CharacterCreationCompletionReviewSchemas.ProfileSettingsExecutionV1
                && request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
                && request.PolicyConsumptionSchema == CharacterCreationPolicyConsumption.SchemaV1
                && CharacterCreationFinalizationDigest.IsCanonical(request.EffectivePolicyDigest);

    public static bool HasValidAdditionalConsumerExecution(CharacterCreationCompletionReviewRequest request)
        => request.AdditionalConsumerExecutionSchema is null
            || request.AdditionalConsumerExecutionSchema == CharacterCreationCompletionReviewSchemas.AdditionalConsumerExecutionV1
                && request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
                && request.PolicyConsumptionSchema == CharacterCreationPolicyConsumption.SchemaV1
                && CharacterCreationFinalizationDigest.IsCanonical(request.EffectivePolicyDigest);

    public static bool HasValidRuleSourceExecution(CharacterCreationCompletionReviewRequest request)
        => HasValidSourcebookSelection(request)
        && (request.RuleSourceExecutionSchema is null ? request.RuleSourceCaptureDigest is null
            : request.RuleSourceExecutionSchema is (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1
                or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
                && CharacterCreationFinalizationDigest.IsCanonical(request.RuleSourceCaptureDigest)
                && request.OriginalCharacterExecutionSchema == CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1
                && HasValidOriginalCharacterExecution(request));

    public static bool HasValidSourcebookSelection(CharacterCreationCompletionReviewRequest request)
        => request.SourcebookSelection is null || request.SourcebookSelection.IsValid()
            && request.RuleSourceExecutionSchema is (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
            && request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4;

    public static bool HasValidOriginalCharacterExecution(CharacterCreationCompletionReviewRequest request)
        => request.OriginalCharacterExecutionSchema is null
            || request.OriginalCharacterExecutionSchema == CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1
                && request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
                && request.PolicyConsumptionSchema == CharacterCreationPolicyConsumption.SchemaV1
                && CharacterCreationFinalizationDigest.IsCanonical(request.EffectivePolicyDigest);

    // Version/shape admission only. Canonical source resolution and private
    // attribute execution are still required before any coverage is issued.
    public static bool HasValidMaximumAttributesDefaultExecution(CharacterCreationCompletionReviewRequest request)
        => request.MaximumAttributesDefaultExecutionSchema is null
            || request.MaximumAttributesDefaultExecutionSchema == CharacterCreationCompletionReviewSchemas.MaximumAttributesDefaultExecutionV1
                && request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
                && request.PolicyConsumptionSchema == CharacterCreationPolicyConsumption.SchemaV1
                && CharacterCreationFinalizationDigest.IsCanonical(request.EffectivePolicyDigest);

    public static string ComputeInputsDigest(CharacterCreationCompletionReviewInputs inputs)
        => CharacterCreationFinalizationDigest.Compute(inputs with { SnapshotDigest = string.Empty });

    public static string ComputeReviewDigest(CharacterCreationCompletionReview review)
        => CharacterCreationFinalizationDigest.Compute(review with { ReviewDigest = string.Empty });
}
