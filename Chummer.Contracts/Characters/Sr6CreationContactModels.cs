namespace Chummer.Contracts.Characters;

/// <summary>Player-authored NPC identity, not an asserted GM approval or external account.</summary>
public sealed record Sr6CreationContactChoice(Guid Id, string Name, string? Role, int Connection, int Loyalty);

public sealed record Sr6CreationContactSelection(IReadOnlyList<Sr6CreationContactChoice> Contacts);

public sealed record Sr6CreationContactOptions(int Charisma, int PointsPerCharisma, int PointBudget,
    int MinimumRating, int MaximumRating, int MaximumContacts);

public sealed record Sr6CreationContactValue(Sr6CreationContactChoice Contact, int PointCost);

public sealed record Sr6CreationContactPreview(IReadOnlyList<Sr6CreationContactValue> Contacts,
    Sr6CreationContactOptions Options, int PointsSpent, int PointsRemaining, bool AllPointsSpent,
    bool NeedsGmReview, string AuthorityDigest, IReadOnlyList<string> SourceAnchorIds);

public static class Sr6CreationContactBlockers
{
    public const string InvalidSelection = "sr6-creation-contacts-invalid";
    public const string AttributesRequired = "sr6-creation-contacts-attributes-required";
    public const string RatingExceeded = "sr6-creation-contacts-rating";
    public const string BudgetExceeded = "sr6-creation-contacts-budget";
}
