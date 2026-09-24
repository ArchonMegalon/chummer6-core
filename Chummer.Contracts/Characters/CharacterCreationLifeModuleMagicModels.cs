namespace Chummer.Contracts.Characters;

/// <summary>Life Modules source prices and choices, without Priority grants or a Karma foundation.</summary>
public sealed record CharacterCreationLifeModuleMagicCatalog(
    string SettingsProfileId, string RawProfileInputsDigest, string CustomDataInputsDigest,
    CharacterCreationKarmaMagicPolicy Policy, CharacterCreationLifeModuleTalentCatalog Talents,
    IReadOnlyList<CharacterCreationKarmaMagicCatalogSlice> Catalogs,
    IReadOnlyList<string> SourceAnchorIds, string AuthorityDigest);

public sealed record CharacterCreationLifeModuleMagicQuote(
    string SourceAuthorityDigest, CharacterCreationLifeModuleMagicCatalog ProjectionCatalog,
    string TalentPlanDigest, string AttributesQuoteDigest, string SkillsQuoteDigest,
    string ResourcesQuoteDigest, string ContactsQuoteDigest,
    string TalentKind, CharacterCreationKarmaMagicAccess Access,
    CharacterCreationMagicResonanceSelections Selections,
    IReadOnlyList<CharacterCreationMagicResonanceOptionFinalizationSource> Sources,
    CharacterCreationKarmaMagicPurchaseCost Cost, decimal PowerPointsTotal, decimal PowerPointsUsed,
    int MaximumSpellsPerKind, int MaximumComplexForms, decimal KarmaBeforeMagic, decimal KarmaAfterMagic,
    IReadOnlyList<string> Blockers, string QuoteDigest)
{
    public const string SelectionRequired = "creation-life-module-magic-selection-required";
}
