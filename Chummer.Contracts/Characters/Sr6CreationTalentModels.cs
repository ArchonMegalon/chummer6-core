namespace Chummer.Contracts.Characters;

/// <summary>Whole power-point purchase (Point Buy) or original-Magic split (priority mystic adept).
/// This reserves a budget; it does not choose powers or apply their effects.</summary>
public sealed record Sr6CreationTalentSelection(int SelectedPowerPoints);

public sealed record Sr6CreationTalentOptions(int MaximumSelectedPowerPoints, int AutomaticPowerPoints,
    bool UsesCharacterPoints, int CharacterPointsPerPowerPoint, int CharacterPointsPerSpellOrForm,
    bool RequiresAspect);

/// <summary>Creation entitlements/purchase limits only, not learned spells, forms or powers.</summary>
public sealed record Sr6CreationTalentPreview(int Magic, int Resonance, int PowerPointBudget,
    int PowerPointCharacterPointCost, int SpellOrRitualLimit, int AlchemicalSpellLimit, int ComplexFormLimit,
    int FreeSpellOrRitualSlots, int FreeAlchemicalSpellSlots, int FreeComplexFormSlots,
    int CharacterPointsPerSpellOrForm, string AuthorityDigest, IReadOnlyList<string> SourceAnchorIds);

public static class Sr6CreationTalentBlockers
{
    public const string InvalidSelection = "sr6-creation-talent-invalid-selection";
    public const string AttributesRequired = "sr6-creation-talent-attributes-required";
    public const string AspectRequired = "sr6-creation-talent-aspect-required";
    public const string PowerPointLimit = "sr6-creation-talent-power-point-limit";
}
