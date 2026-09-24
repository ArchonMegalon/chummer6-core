namespace Chummer.Contracts.Characters;

public sealed record Sr6CreationAdeptPowerChoice(string CatalogId, int Rating);
public sealed record Sr6CreationAdeptPowerSelection(IReadOnlyList<Sr6CreationAdeptPowerChoice> Choices);
public sealed record Sr6CreationAdeptPowerOption(string Id, string SourceName, string? SubjectKind,
    string? SubjectId, string UseId, int QuarterPointsPerRating, int MaximumRating, string SourceAnchorId);
public sealed record Sr6CreationAdeptPowerValue(Sr6CreationAdeptPowerOption Option, int Rating, int QuarterPointsSpent);
public sealed record Sr6CreationAdeptPowerPreview(IReadOnlyList<Sr6CreationAdeptPowerValue> Powers,
    int QuarterPointBudget, int QuarterPointsSpent, int QuarterPointsRemaining,
    IReadOnlyList<string> WarningIds, string AuthorityDigest, IReadOnlyList<string> SourceAnchorIds);

public static class Sr6CreationAdeptPowerBlockers
{
    public const string InvalidSelection = "sr6-creation-powers-invalid-selection";
    public const string TalentRequired = "sr6-creation-powers-talent-required";
    public const string CatalogUnavailable = "sr6-creation-powers-catalog-unavailable";
    public const string RatingExceeded = "sr6-creation-powers-rating-exceeded";
    public const string BudgetExceeded = "sr6-creation-powers-budget-exceeded";
}
