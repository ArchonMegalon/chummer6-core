namespace Chummer.Contracts.Characters;

public static class CharacterCreationCompletionBudgetSchemas
{
    public const string PreviewV1 = "chummer.creation-completion-budget-preview.v1";
    public const string SnapshotConflict = "creation-completion-budget-snapshot-conflict";
}

/// <summary>
/// Proposed creation Karma after Attributes, Qualities and Mystic Adept costs,
/// before any total Resources investment or Career carryover. A null balance is
/// unproven, not zero. Retained blockers may describe an incomplete magical
/// selection even when this particular balance is exact. This is neither a
/// completed character nor a draft, receipt or authorization to save one.
/// </summary>
public sealed record CharacterCreationCompletionBudgetPreview(
    string Schema,
    CharacterCreationMagicResonanceCompletionProjection Magic,
    CharacterCreationAttributeQualitiesPreview Qualities,
    int? AvailableKarmaBeforeResources,
    IReadOnlyList<string> Blockers,
    string ProposalDigest);
