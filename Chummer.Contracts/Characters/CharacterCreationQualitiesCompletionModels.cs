namespace Chummer.Contracts.Characters;

public static class CharacterCreationQualitiesCompletionSchemas
{
    public const string InputsV1 = "chummer.creation-qualities-completion-inputs.v1";
    public const string PreviewV1 = "chummer.creation-attribute-qualities-preview.v1";
    public const string SnapshotConflict = "creation-qualities-completion-snapshot-conflict";
}

/// <summary>
/// Source-admitted original inputs, not a saved or confirmable Qualities draft.
/// Binding retains actual Attributes history (zero/empty before its first save).
/// Detached inputs never prove current ownership or permit a workspace mutation.
/// </summary>
public sealed record CharacterCreationQualitiesCompletionInputs(
    string Schema,
    CharacterCreationQualitiesBinding Binding,
    CharacterCreationAttributesState Attributes,
    CharacterCreationQualitiesAuthority Authority,
    CharacterCreationPrerequisiteDraft PrerequisiteDraft,
    IReadOnlyList<string> SelectedOptionIds,
    string SnapshotDigest)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationQualitiesState? SavedQualitiesState { get; init; }
}

/// <summary>Pure selection/cost projection. No confirmation or receipt authority.</summary>
public sealed record CharacterCreationQualitiesCompletionEvaluation(
    string AuthorityDigest,
    int ProposedAttributeKarmaUsed,
    IReadOnlyList<CharacterCreationQualitySelection> Selections,
    IReadOnlyList<CharacterCreationGrantedQuality> GrantedQualities,
    CharacterCreationQualitiesBudget PositiveQualityBudget,
    CharacterCreationQualitiesBudget NegativeQualityBudget,
    int MetagenicPositiveKarma,
    int MetagenicNegativeKarma,
    int KarmaRemaining,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers);

/// <summary>
/// Hypothetical Attributes/Qualities result tied to both original snapshots.
/// A proposal digest is a comparison aid, not a token accepted by Confirm.
/// </summary>
public sealed record CharacterCreationAttributeQualitiesPreview(
    string Schema,
    CharacterCreationQualitiesBinding Binding,
    string AttributesSnapshotDigest,
    string QualitiesSnapshotDigest,
    CharacterCreationAttributesPreview Attributes,
    CharacterCreationQualitiesCompletionEvaluation Qualities,
    IReadOnlyList<string> Blockers,
    string ProposalDigest);
