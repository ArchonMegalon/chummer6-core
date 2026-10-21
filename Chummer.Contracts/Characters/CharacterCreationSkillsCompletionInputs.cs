namespace Chummer.Contracts.Characters;

public static class CharacterCreationSkillsCompletionSchemas
{
    public const string InputsV1 = "chummer.creation-skill-completion-inputs.v1";
}

/// <summary>
/// Source-admitted, read-only completion inputs. Unlike an ordinary SkillsState,
/// these do not require a saved Attributes draft. Binding describes actual
/// original history: its attribute revision/digest are zero/empty when absent,
/// never the revision of a proposed or pretend saved draft. Attributes carries
/// the real original snapshot; proposed values are evaluated separately.
/// No confirmation, persistence or current owner authority is granted here.
/// </summary>
public sealed record CharacterCreationSkillsCompletionInputs(
    string Schema,
    CharacterCreationSkillsBinding Binding,
    CharacterCreationAttributesState Attributes,
    CharacterCreationSkillsAuthority Authority,
    CharacterCreationPrerequisiteDraft PrerequisiteDraft,
    IReadOnlyList<CharacterCreationSkillAllocation> Allocations,
    IReadOnlyList<CharacterCreationSkillGroupAllocation> GroupAllocations,
    IReadOnlyList<CharacterCreationKnowledgePointContribution> KnowledgePointContributions,
    int SelectedActiveSkillPoints,
    int SelectedSkillGroupPoints,
    CharacterCreationMovementCapability MovementCapability,
    string SnapshotDigest)
{
    // If skill history exists, keep its actually validated snapshot rather than
    // silently treating an existing character as an empty initial allocation.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationSkillsState? SavedSkillsState { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public CharacterCreationCompositeBaseline? CompositeBaseline => Attributes.CompositeBaseline;

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationKarmaSkillsPolicy? KarmaPurchasePolicy { get; init; }

    /// <summary>Effective source order determines the single enabled member that
    /// receives the legacy group compensation. Captured from the validated catalog,
    /// never inferred from localized names, purchase order, or sorted source IDs.
    /// Null retains historical inputs that did not capture this authority.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? KarmaCompensationSourceOrder { get; init; }

    /// <summary>Freshly admitted composite quality sources. Null preserves historical
    /// bytes; a composite with selected qualities may never substitute an empty list.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterCreationQualitySelection>? SkillKarmaQualitySelections { get; init; }
}
