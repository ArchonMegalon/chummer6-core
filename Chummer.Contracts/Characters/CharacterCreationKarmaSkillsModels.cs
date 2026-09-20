namespace Chummer.Contracts.Characters;

public static class CharacterCreationKarmaSpecializationPayments
{
    public const string Karma = "karma";
    public const string KnowledgePoint = "knowledge-point";
    public const string ExoticIdentity = "exotic-identity";
}

public sealed record CharacterCreationKarmaSkillAllocation(
    string SourceSkillId,
    string Kind,
    int KarmaLevels,
    int KnowledgePointLevels = 0,
    bool IsNativeLanguage = false,
    string? SpecializationOptionId = null,
    string? SpecializationPayment = null);

public sealed record CharacterCreationKarmaSkillGroupAllocation(string GroupId, int KarmaLevels);

public sealed record CharacterCreationKarmaSkillsSelection(
    IReadOnlyList<CharacterCreationKarmaSkillAllocation> Skills,
    IReadOnlyList<CharacterCreationKarmaSkillGroupAllocation> Groups,
    string? TalentUnlock = null);

/// <summary>
/// KarmaCost includes SpecializationKarmaCost; the latter is a breakdown, not an
/// additional charge. Group purchases are charged separately in group projections.
/// </summary>
public sealed record CharacterCreationKarmaSkillProjection(
    CharacterCreationKarmaSkillAllocation Allocation,
    string SourceNodeDigest,
    string Name,
    int? Rating,
    int GroupLevels,
    int KnowledgePointCost,
    int KarmaCost,
    int SpecializationKarmaCost,
    bool IsEnabled,
    IReadOnlyList<string> Blockers);

public sealed record CharacterCreationKarmaSkillGroupProjection(
    CharacterCreationKarmaSkillGroupAllocation Allocation,
    string Name,
    int KarmaCost,
    bool IsBroken,
    IReadOnlyList<string> Blockers);

/// <summary>
/// Only the selected skills, complete affected groups and selected talent. This
/// permits offline arithmetic validation, not admission against current sources.
/// CatalogDigest on the quote identifies the full source catalog separately.
/// </summary>
public sealed record CharacterCreationKarmaSkillsBasis(
    CharacterCreationSkillsCatalog Catalog,
    CharacterCreationKarmaTalentCatalog Talents);

/// <summary>
/// Read-only calculation, not a mutation authorization. CatalogDigest binds the
/// full source catalog; Access covers only Basis.Catalog, not all picker choices.
/// </summary>
public sealed record CharacterCreationKarmaSkillsQuote(
    string Schema,
    CharacterCreationKarmaSkillsPolicy Policy,
    string CatalogDigest,
    string AttributesDigest,
    CharacterCreationKarmaSkillAccess Access,
    CharacterCreationKarmaSkillsSelection Selection,
    IReadOnlyList<CharacterCreationKarmaSkillProjection> Skills,
    IReadOnlyList<CharacterCreationKarmaSkillGroupProjection> Groups,
    int KnowledgePointsTotal,
    decimal KnowledgePointsUsed,
    int NativeLanguagesUsed,
    int KarmaAvailable,
    decimal KarmaUsed,
    IReadOnlyList<string> Blockers,
    string QuoteDigest,
    CharacterCreationKarmaSkillsBasis Basis)
{
    public const string SchemaV1 = "chummer.character_creation_karma_skills_quote.v1";
    public bool CanSelect => Blockers.Count == 0;
}
