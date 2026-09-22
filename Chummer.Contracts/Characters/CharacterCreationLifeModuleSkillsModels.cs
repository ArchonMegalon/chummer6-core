namespace Chummer.Contracts.Characters;

/// <summary>KarmaCost includes specialization cost. For Knowledge, the modified
/// rank and specialization sum is rounded once; a rounded specialization display
/// is not an additional charge. Module levels are not paid allocations.</summary>
public sealed record CharacterCreationLifeModuleSkillValue(
    CharacterCreationKarmaSkillAllocation Allocation, string Name, string SourceNodeDigest,
    int ModuleLevels, int ModuleGroupLevels, int GroupLevels, int? Rating,
    int KarmaCost, int SpecializationKarmaCost, int KnowledgePointCost,
    bool IsEnabled, IReadOnlyList<string> Blockers, IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationLifeModuleSkillGroupValue(
    CharacterCreationKarmaSkillGroupAllocation Allocation, string Name, int ModuleLevels,
    int EffectiveLevels, int KarmaCost, bool IsEnabled, bool IsBroken, IReadOnlyList<string> Blockers);

/// <summary>Life Modules grant-aware spending, not a Karma foundation or a save permission.
/// Module levels are retained separately and must remain Improvements when saved.</summary>
public sealed record CharacterCreationLifeModuleSkillsQuote(
    CharacterCreationKarmaSkillsPolicy Policy, string CatalogDigest,
    string EffectPlanDigest, string MetatypePlanDigest, string TalentPlanDigest, string AttributeQuoteDigest,
    CharacterCreationKarmaSkillsSelection Selection,
    IReadOnlyList<string> AllowedActiveSkillSourceIds, IReadOnlyList<string> AllowedSkillGroupIds,
    IReadOnlyList<CharacterCreationLifeModuleSkillValue> Skills,
    IReadOnlyList<CharacterCreationLifeModuleSkillGroupValue> Groups,
    int KnowledgePointsTotal, decimal ModuleKnowledgePoints, int KnowledgePointsUsed,
    int NativeLanguagesUsed, decimal KarmaUsed, IReadOnlyList<string> Blockers, string QuoteDigest);
