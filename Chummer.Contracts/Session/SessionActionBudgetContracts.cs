using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Session;

public static class SessionActionBudgetTimingModes
{
    public const string OnTurn = "on-turn";
    public const string BetweenTurns = "between-turns";
    public const string Anytime = "anytime";
}

public static class SessionActionAffordanceStates
{
    public const string Available = "available";
    public const string Unavailable = "unavailable";
}

public sealed record SessionActionBudgetBucket(
    int Base,
    int Available,
    int Spent,
    int? Computed = null,
    int? TurnStartCap = null);

public sealed record SessionActionBudgetCost(
    int Major = 0,
    int Minor = 0);

public sealed record SessionActionBudgetConversionState(
    bool CanSpendFourMinorForAnytimeMajor,
    bool CanHoldConvertedMajorBeforeTurn,
    int ConvertibleAnytimeMajorCount,
    int HeldConvertedMajorCount = 0);

public sealed record SessionActionAffordanceTemplate(
    string ActionKey,
    string Timing,
    SessionActionBudgetCost Cost,
    string? SummaryKey = null,
    IReadOnlyList<RulesetExplainParameter>? SummaryParameters = null,
    string? ExplainEntryId = null);

public sealed record SessionActionAffordance(
    string ActionKey,
    string Timing,
    SessionActionBudgetCost Cost,
    string State,
    string? SummaryKey = null,
    IReadOnlyList<RulesetExplainParameter>? SummaryParameters = null,
    string? UnavailableReasonKey = null,
    IReadOnlyList<RulesetExplainParameter>? UnavailableReasonParameters = null,
    string? ExplainEntryId = null);

public sealed record SessionActionBudgetReceipt(
    string SourceAnchorRef,
    string SummaryKey,
    IReadOnlyList<RulesetExplainParameter>? SummaryParameters = null);

public sealed record SessionActionBudgetInput(
    string ActorRef,
    string RoundRef,
    string RulesetId,
    int InitiativeDice,
    bool IsOwnTurnActive = true,
    int MajorSpent = 0,
    int MinorSpent = 0,
    int HeldConvertedMajorCount = 0,
    int MinorTurnStartCap = 5,
    bool CanSpendFourMinorForAnytimeMajor = true,
    bool CanHoldConvertedMajorBeforeTurn = false,
    IReadOnlyList<SessionActionAffordanceTemplate>? Affordances = null,
    IReadOnlyList<SessionActionBudgetReceipt>? Receipts = null,
    string? ExplainEntryId = null);

public sealed record SessionActionBudgetResult(
    string ActorRef,
    string RoundRef,
    string RulesetId,
    int InitiativeDice,
    SessionActionBudgetBucket Major,
    SessionActionBudgetBucket Minor,
    SessionActionBudgetConversionState Conversions,
    IReadOnlyList<SessionActionAffordance> Affordances,
    IReadOnlyList<SessionActionBudgetReceipt> Receipts,
    IReadOnlyList<RulesetCapabilityDiagnostic>? Diagnostics = null,
    string? ExplainEntryId = null);
