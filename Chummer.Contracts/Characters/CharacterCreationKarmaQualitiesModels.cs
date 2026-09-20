namespace Chummer.Contracts.Characters;

public sealed record CharacterCreationKarmaQualitiesPolicy(
    string Schema,
    string SettingsProfileId,
    string RawProfileInputsDigest,
    string SourceInputsDigest,
    int QualityKarmaLimit,
    bool MayExceedPositiveLimit,
    bool MayExceedNegativeLimit,
    int MetagenicLimit,
    CharacterCreationQualityCostPolicy Costs,
    IReadOnlyList<string> SourceAnchorIds,
    string AuthorityDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_qualities_policy.v1";
}

public sealed record CharacterCreationKarmaQualitiesCatalog(
    string Schema,
    CharacterCreationKarmaQualitiesPolicy Policy,
    IReadOnlyList<CharacterCreationQualityCatalogOption> Options,
    string CatalogDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_qualities_catalog.v1";
}

/// <summary>
/// Source-bound pending purchases, not applied character effects. Only selected
/// complete source rows are retained in history; an entire mutable catalog is not.
/// </summary>
public sealed record CharacterCreationKarmaQualitiesQuote(
    string Schema,
    CharacterCreationKarmaQualitiesPolicy Policy,
    string CatalogDigest,
    string MetatypeDigest,
    string TalentDigest,
    IReadOnlyList<CharacterCreationQualityCatalogOption> Selections,
    CharacterCreationQualityCostTotals Costs,
    IReadOnlyList<string> Blockers,
    string QuoteDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_qualities_quote.v1";
    public bool CanSelect => Blockers.Count == 0;
}
