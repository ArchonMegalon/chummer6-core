namespace Chummer.Contracts.Characters;

public sealed record Sr6CreationQualitySelection(IReadOnlyList<string> OptionIds);

/// <summary>Core-owned choice. Positive KarmaCost spends Karma; negative grants it.</summary>
public sealed record Sr6CreationQualityOption(string Id, string Name, string FamilyId,
    int KarmaCost, string SourceAnchorId, string? AttributeId = null, string? SkillId = null,
    bool Available = true, string? UnavailableReason = null);

public sealed record Sr6CreationQualityPreview(IReadOnlyList<Sr6CreationQualityOption> Values,
    int PositiveKarmaCost, int NegativeKarmaBonus, int NetKarmaBonus, int CustomizationKarma,
    int MaximumChoices, int MaximumNetBonus, string AuthorityDigest, IReadOnlyList<string> SourceAnchorIds);

public static class Sr6CreationQualityBlockers
{
    public const string InvalidSelection = "sr6-creation-qualities-invalid";
    public const string LimitExceeded = "sr6-creation-qualities-limit";
    public const string Unavailable = "sr6-creation-qualities-unavailable";
    public const string Conflict = "sr6-creation-qualities-conflict";
    public const string BudgetExceeded = "sr6-creation-qualities-budget";
}
