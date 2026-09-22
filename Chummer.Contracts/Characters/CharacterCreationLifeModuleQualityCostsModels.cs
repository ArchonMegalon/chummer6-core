namespace Chummer.Contracts.Characters;

/// <summary>Cost projection of the already source-admitted cumulative quality
/// graph. Does not purchase qualities or authorize character mutation.</summary>
public sealed record CharacterCreationLifeModuleQualityCostLine(
    string InstanceId, string SourceId, string Name, string Origin, int SourceKarma,
    bool CountsAgainstQualityLimit, bool CountsAgainstKarma, bool CountsAgainstMetagenicLimit);

public sealed record CharacterCreationLifeModuleQualityCostsQuote(
    CharacterCreationKarmaQualitiesPolicy Policy,
    string EffectPlanDigest, string MetatypePlanDigest, string TalentPlanDigest,
    IReadOnlyList<CharacterCreationLifeModuleQualityCostLine> Lines,
    decimal FreePositiveQualities, decimal FreeNegativeQualities,
    CharacterCreationQualityCostTotals Costs, int MetagenicBalanceKarma,
    decimal KarmaAdjustmentAfterTalent, IReadOnlyList<string> Blockers, string QuoteDigest);
