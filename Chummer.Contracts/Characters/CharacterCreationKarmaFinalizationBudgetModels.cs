namespace Chummer.Contracts.Characters;

public static class CharacterCreationKarmaFinalizationBudgetBlockers
{
    public const string PolicyUnavailable = "creation-karma-completion-policy-unavailable";
    public const string StartingCashUnavailable = "creation-karma-completion-starting-cash-unavailable";
    public const string BudgetInvalid = "creation-karma-completion-budget-invalid";
}

/// <summary>Profile-owned carryover limits, separate from creation purchasing power.</summary>
public sealed record CharacterCreationKarmaCarryoverPolicy(
    string Schema,
    string SettingsProfileId,
    string RawProfileInputsDigest,
    int MaximumKarma,
    decimal MaximumNuyen,
    IReadOnlyList<string> SourceAnchorIds,
    string AuthorityDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_carryover_policy.v1";
}

/// <summary>
/// Source-bound starting-cash terms. Resolving a row does not establish ownership
/// of a purchased lifestyle, apply its effects, or authorize character completion.
/// </summary>
public sealed record CharacterCreationStartingNuyenSource(
    string Schema,
    string SettingsProfileId,
    string RawProfileInputsDigest,
    string SourceInputsDigest,
    string SourceId,
    string Name,
    int Dice,
    decimal Multiplier,
    string SourceBook,
    string Page,
    string SourceNodeXml,
    string SourceNodeDigest,
    IReadOnlyList<string> SourceAnchorIds,
    string AuthorityDigest)
{
    public const string SchemaV1 = "chummer.character_creation_starting_nuyen_source.v1";
}

/// <summary>
/// Read-only financial contribution to Karma completion. Starting cash cannot
/// fund creation purchases. The dice total is an explicit choice, never rolled
/// again during preview, confirmation, or recovery. No Created flag is implied.
/// </summary>
public sealed record CharacterCreationKarmaFinalizationBudgetQuote(
    string Schema,
    string KarmaQuoteDigest,
    CharacterCreationKarmaCarryoverPolicy Policy,
    CharacterCreationStartingNuyenSource StartingCashSource,
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
    public const string SchemaV1 = "chummer.character_creation_karma_finalization_budget_quote.v1";
}
