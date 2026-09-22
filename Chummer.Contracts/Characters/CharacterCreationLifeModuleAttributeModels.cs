namespace Chummer.Contracts.Characters;

/// <summary>Additional paid levels after the selected modules; never Priority points.</summary>
public sealed record CharacterCreationLifeModuleAttributePurchase(string AttributeId, int KarmaLevels);

public sealed record CharacterCreationLifeModuleAttributeValue(string AttributeId, int Minimum, int Maximum,
    long ModuleLevels, int AppliedModuleLevels, int KarmaLevels, int Current, int KarmaCost,
    IReadOnlyList<string> SourceAnchorIds);

/// <summary>
/// Source-bound normal attributes and Edge after module grants and proposed purchases.
/// Not a talent choice, awakened-attribute quote, whole-character budget or write permission.
/// </summary>
public sealed record CharacterCreationLifeModuleAttributeQuote(CharacterCreationAttributePolicy Policy,
    string EffectPlanDigest, string MetatypePlanDigest,
    IReadOnlyList<CharacterCreationLifeModuleAttributeValue> Attributes, decimal KarmaUsed,
    IReadOnlyList<string> Blockers, string QuoteDigest);
