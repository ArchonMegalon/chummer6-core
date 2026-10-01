namespace Chummer.Contracts.Characters;

/// <summary>
/// Read-only alternatives for one Karma draft. Each quality selection is evaluated
/// against the complete draft, not a UI-computed remaining budget.
/// </summary>
public sealed record CharacterCreationKarmaQualityPreviewRequest(
    CharacterCreationKarmaMetatypeBinding Binding,
    string MetatypeOptionId,
    IReadOnlyList<IReadOnlyList<string>> QualitySelections,
    string? TalentOptionId = null,
    IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? AttributeAllocations = null,
    CharacterCreationKarmaSkillsSelection? SkillsSelection = null,
    decimal? ResourceKarmaInvestment = null,
    IReadOnlyList<CharacterCreationGearSelection>? GearSelections = null,
    IReadOnlyList<CharacterCreationKarmaContactSelection>? ContactSelections = null,
    IReadOnlyList<CharacterCreationLifestyleConfiguration>? LifestyleSelections = null,
    Guid? StartingLifestyleId = null,
    CharacterCreationMagicResonanceSelections? MagicSelections = null)
{
    public const int MaximumCandidates = 20;
}

/// <summary>Results retain request order. No confirmation or persistence authority is issued.</summary>
public sealed record CharacterCreationKarmaQualityPreviews(
    IReadOnlyList<CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote>> Results);
