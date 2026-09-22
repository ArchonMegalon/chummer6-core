namespace Chummer.Contracts.Characters;

/// <summary>Read-only completion finances for Life Modules. Explicit dice are
/// retained across preview/confirmation; starting cash never funds purchases.</summary>
public sealed record CharacterCreationLifeModuleFinalizationBudgetQuote(
    CharacterCreationKarmaCarryoverPolicy Policy,
    CharacterCreationStartingNuyenSource StartingCashSource,
    string ResourcesQuoteDigest,
    string LifestylesQuoteDigest,
    string ContactsQuoteDigest,
    int DiceTotal,
    decimal ResourceKarmaRoundingAdjustment,
    int KarmaBeforeCarryover,
    int KarmaCarried,
    int KarmaDiscarded,
    decimal NuyenBeforeCarryover,
    decimal NuyenCarried,
    decimal NuyenDiscarded,
    decimal LifestyleStartingNuyen,
    decimal CareerNuyen,
    IReadOnlyList<string> SourceAnchorIds,
    string QuoteDigest)
{
    public const string PolicyUnavailable = "creation-life-module-completion-policy-unavailable";
    public const string StartingCashUnavailable = "creation-life-module-completion-starting-cash-unavailable";
    public const string DiceRequired = "creation-life-module-completion-dice-required";
    public const string BudgetInvalid = "creation-life-module-completion-budget-invalid";
}
