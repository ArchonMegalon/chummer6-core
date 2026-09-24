namespace Chummer.Contracts.Characters;

/// <summary>One basic lifestyle prepaid for whole months. Not a tenancy or active character mutation.</summary>
public sealed record Sr6CreationLifestyleSelection(string LifestyleId, int Months);

public sealed record Sr6CreationLifestyleOption(string Id, decimal MonthlyNuyen, string SourceAnchorId);

/// <summary>Combined creation cash projection, not a finalization or cash-discard command.</summary>
public sealed record Sr6CreationLifestylePreview(
    Sr6CreationLifestyleSelection Selection,
    Sr6CreationLifestyleOption Option,
    decimal LifestyleSpentNuyen,
    decimal GearSpentNuyen,
    decimal ResourcesNuyen,
    decimal RemainingNuyen,
    decimal ProjectedStartingNuyen,
    decimal MaximumCarryOverNuyen,
    decimal UnspentAboveCarryOver,
    string AuthorityDigest,
    IReadOnlyList<string> SourceAnchorIds);

public static class Sr6CreationLifestyleBlockers
{
    public const string InvalidSelection = "sr6-creation-lifestyle-invalid-selection";
    public const string Unavailable = "sr6-creation-lifestyle-unavailable";
    public const string BudgetExceeded = "sr6-creation-lifestyle-budget-exceeded";
}
