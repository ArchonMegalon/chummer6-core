namespace Chummer.Contracts.Characters;

/// <summary>Additional paid levels after the selected modules; never Priority points.</summary>
public sealed record CharacterCreationLifeModuleAttributePurchase(string AttributeId, int KarmaLevels);

public sealed record CharacterCreationLifeModuleAttributeValue(string AttributeId, int Minimum, int Maximum,
    long ModuleLevels, int AppliedModuleLevels, int KarmaLevels, int Current, int KarmaCost,
    IReadOnlyList<string> SourceAnchorIds);

/// <summary>
/// Source-bound attributes after module grants and proposed purchases. Magic or
/// Resonance requires an exact explicit talent plan; no whole-character budget or write permission.
/// </summary>
public sealed record CharacterCreationLifeModuleAttributeQuote(CharacterCreationAttributePolicy Policy,
    string EffectPlanDigest, string MetatypePlanDigest,
    IReadOnlyList<CharacterCreationLifeModuleAttributeValue> Attributes, decimal KarmaUsed,
    IReadOnlyList<string> Blockers, string QuoteDigest)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TalentPlanDigest { get; init; }
}
