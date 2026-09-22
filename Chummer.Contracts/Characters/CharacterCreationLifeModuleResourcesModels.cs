namespace Chummer.Contracts.Characters;

/// <summary>Creation shopping funds only. Does not include rolled starting cash,
/// purchases, lifestyle costs, final carryover, or permission to save a runner.</summary>
public sealed record CharacterCreationLifeModuleResourcesQuote(
    CharacterCreationKarmaResourcesPolicy Policy,
    string EffectPlanDigest, string MetatypePlanDigest, string TalentPlanDigest,
    string AttributeQuoteDigest, string SkillsQuoteDigest,
    decimal TotalKarma, decimal KarmaBeforeResources, decimal KarmaInvestment,
    decimal NuyenFromKarma, decimal KarmaAfterResources,
    IReadOnlyList<string> Blockers, string QuoteDigest)
{
    public const string SelectionRequired = "creation-life-module-resources-selection-required";
    public const string InvestmentInvalid = "creation-life-module-resources-investment-invalid";
}
