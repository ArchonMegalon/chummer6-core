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

/// <summary>Read-only pending allocation; no persistence or finalization authority.</summary>
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
    string QuoteDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_skills_quote.v1";
    public bool CanSelect => Blockers.Count == 0;
}
