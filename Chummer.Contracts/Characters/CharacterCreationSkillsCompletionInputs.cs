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

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationKarmaSkillsPolicy? KarmaPurchasePolicy { get; init; }
}
