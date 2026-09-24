namespace Chummer.Contracts.Characters;

/// <summary>A reviewed basket against Life Modules creation funds. Granted racial
/// or talent gear is separate; no inventory mutation or Career cash is implied.</summary>
public sealed record CharacterCreationLifeModuleGearQuote(
    CharacterCreationKarmaGearBasis Basis,
    string ResourcesQuoteDigest,
    IReadOnlyList<CharacterCreationGearSelection> Selection,
    IReadOnlyList<CharacterCreationGearLine> Lines,
    CharacterCreationGearBudget Budget,
    IReadOnlyList<string> Blockers,
    string QuoteDigest)
{
    public const string SelectionRequired = "creation-life-module-gear-selection-required";
}
