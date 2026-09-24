namespace Chummer.Contracts.Characters;

/// <summary>Effective Karma-profile prices. No Priority grants or mutation authority.</summary>
public sealed record CharacterCreationKarmaMagicPolicy(
    string Schema,
    string SettingsProfileId,
    string SettingsInputsDigest,
    int KarmaPerSpell,
    int KarmaPerComplexForm,
    bool IgnoreComplexFormLimit,
    CharacterCreationMysticAdeptPowerPointPolicy PowerPointPolicy,
    string CanonicalSourceXml,
    string CanonicalSourceXmlDigest,
    IReadOnlyList<string> SourceAnchorIds,
    string PolicyDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_magic_policy.v1";
    public const string LifeModulesSchemaV1 = "chummer.character_creation_life_module_magic_policy.v1";
}

/// <summary>A source slice shared with the existing magic option projector, not a Priority talent.</summary>
public sealed record CharacterCreationKarmaMagicCatalogSlice(
    string Kind,
    string EffectiveSourceDigest,
    IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> Options);

/// <summary>
/// Karma-only effective catalogs, profile and purchased talent authority. Disabled
/// options retain their reason. Selection legality and persistence are separate.
/// </summary>
public sealed record CharacterCreationKarmaMagicCatalog(
    string Schema,
    string SettingsProfileId,
    string RawProfileInputsDigest,
    string CustomDataInputsDigest,
    CharacterCreationKarmaMagicPolicy Policy,
    CharacterCreationKarmaTalentCatalog Talents,
    IReadOnlyList<CharacterCreationKarmaMagicCatalogSlice> Catalogs,
    IReadOnlyList<string> SourceAnchorIds,
    string AuthorityDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_magic_catalog.v1";
}

/// <summary>
/// Cost arithmetic only. This does not validate selected source identities, skill
/// limits, talent permissions, available Karma or grant permission to save/finalize.
/// </summary>
public sealed record CharacterCreationKarmaMagicPurchaseCost(
    int SpellCount,
    int ComplexFormCount,
    int SpellKarma,
    int ComplexFormKarma,
    CharacterCreationMysticAdeptPowerPointAllocation? MysticPowerPoints,
    int TotalKarma,
    string PolicyDigest);

/// <summary>Core-issued chooser capabilities for this exact talent and skill unlock.</summary>
public sealed record CharacterCreationKarmaMagicAccess(
    bool RequiresTradition,
    bool RequiresStream,
    bool AllowsAdeptPowers,
    bool AllowsSpells,
    bool AllowsComplexForms);

/// <summary>
/// Read-only pending magic selection, retaining only selected catalog rows for
/// replay. SourceAuthorityDigest identifies the complete catalog that persistence
/// must independently re-admit. No character effects have been applied.
/// </summary>
public sealed record CharacterCreationKarmaMagicQuote(
    string Schema,
    string SourceAuthorityDigest,
    CharacterCreationKarmaMagicCatalog ProjectionCatalog,
    string TalentKind,
    CharacterCreationKarmaMagicAccess Access,
    string AttributesQuoteDigest,
    string SkillsQuoteDigest,
    string QualitiesQuoteDigest,
    IReadOnlyList<CharacterCreationTalentQualitySource> RacialSources,
    CharacterCreationTalentQualitySource? TalentSource,
    CharacterCreationMagicResonanceSelections Selections,
    IReadOnlyList<CharacterCreationMagicResonanceOptionFinalizationSource> Sources,
    CharacterCreationKarmaMagicPurchaseCost Cost,
    decimal PowerPointsTotal,
    decimal PowerPointsUsed,
    int SpellLimit,
    int ComplexFormLimit,
    IReadOnlyList<string> Blockers,
    string QuoteDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_magic_quote.v1";
    public bool CanSelect => Blockers.Count == 0;
}
