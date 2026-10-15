namespace Chummer.Contracts.Characters;

public static class CharacterCreationMagicResonanceCompletionSchemas
{
    public const string InputsV1 = "chummer.character_creation_magic_resonance_completion_inputs.v1";
    public const string ProjectionV1 = "chummer.character_creation_magic_resonance_completion_projection.v1";
    public const string SnapshotConflict = "creation.magic_resonance.completion_snapshot_conflict";
}

/// <summary>
/// Detached inputs admitted from one real saved workspace and source observation.
/// A missing Attributes draft stays revision zero with an empty digest in Binding.
/// This is not a replacement saved state or permission to mutate the workspace.
/// </summary>
public sealed record CharacterCreationMagicResonanceCompletionInputs(
    string Schema,
    CharacterCreationMagicResonanceBinding Binding,
    CharacterCreationAttributesState Attributes,
    CharacterCreationMagicResonanceAuthority Authority,
    CharacterCreationPrerequisiteDraft PrerequisiteDraft,
    CharacterCreationMagicResonanceTalentOption SelectedTalent,
    CharacterCreationMagicResonanceSelections Selections,
    string SnapshotDigest)
{
    public CharacterCreationMagicResonanceState? SavedMagicState { get; init; }
}

/// <summary>
/// A hypothetical attribute/Magic evaluation. No draft, receipt, finalization
/// contribution or confirm permission is created by this projection.
/// </summary>
public sealed record CharacterCreationMagicResonanceCompletionProjection(
    string Schema,
    string AttributesSnapshotDigest,
    string MagicSnapshotDigest,
    CharacterCreationMagicResonanceBinding Binding,
    CharacterCreationAttributesPreview Attributes,
    CharacterCreationMagicResonanceEffectiveAttributes EffectiveAttributes,
    CharacterCreationMagicResonanceTalentOption Talent,
    CharacterCreationMagicResonanceSelections Selections,
    CharacterCreationMagicResonanceBudgetState TraditionBudget,
    CharacterCreationMagicResonanceBudgetState StreamBudget,
    CharacterCreationMagicResonanceBudgetState AdeptPowerPointBudget,
    CharacterCreationMagicResonanceBudgetState SpellBudget,
    CharacterCreationMagicResonanceBudgetState ComplexFormBudget,
    CharacterCreationMysticAdeptPowerPointAllocation? MysticAdeptPowerPoints,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    string ProposalDigest);
