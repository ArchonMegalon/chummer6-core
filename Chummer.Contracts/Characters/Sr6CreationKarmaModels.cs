namespace Chummer.Contracts.Characters;

public sealed record Sr6CreationKarmaIncrease(string Id, int Increase, string? FirstExoticSpecialization = null);
public sealed record Sr6CreationKarmaSelection(IReadOnlyList<Sr6CreationKarmaIncrease> Attributes,
    IReadOnlyList<Sr6CreationKarmaIncrease> Skills, int KarmaForNuyen);
public sealed record Sr6CreationKarmaOption(string Id, int BaseRating, int MaximumIncrease,
    bool Available, string? UnavailableReason);
public sealed record Sr6CreationKarmaOptions(int KarmaBudget, int MaximumKarmaForNuyen, int NuyenPerKarma,
    int MaximumCarryOver, IReadOnlyList<Sr6CreationKarmaOption> Attributes, IReadOnlyList<Sr6CreationKarmaOption> Skills);
public sealed record Sr6CreationKarmaStep(int NewRating, int KarmaCost);
public sealed record Sr6CreationKarmaValue(string Id, int BaseRating, int Rating, int KarmaCost,
    IReadOnlyList<Sr6CreationKarmaStep> Steps, string? FirstExoticSpecialization);
public sealed record Sr6CreationKarmaPreview(IReadOnlyList<Sr6CreationKarmaValue> Attributes,
    IReadOnlyList<Sr6CreationKarmaValue> Skills, int KarmaBudget, int KarmaSpent, int KarmaRemaining,
    int KarmaForNuyen, int AdditionalNuyen, int ResourcesNuyen, int MaximumCarryOver,
    int UnspentAboveCarryOver, int AdditionalPowerPoints, string AuthorityDigest, IReadOnlyList<string> SourceAnchorIds);

public static class Sr6CreationKarmaBlockers
{
    public const string InvalidSelection = "sr6-creation-karma-invalid-selection";
    public const string AllocationsRequired = "sr6-creation-karma-allocations-required";
    public const string RatingUnavailable = "sr6-creation-karma-rating-unavailable";
    public const string BudgetExceeded = "sr6-creation-karma-budget-exceeded";
}
