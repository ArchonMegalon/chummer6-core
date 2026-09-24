namespace Chummer.Contracts.Characters;

public static class Sr6CreationSkillIds
{
    public static IReadOnlyList<string> Ordered { get; } = Array.AsReadOnly(new[]
    {
        "Astral", "Athletics", "Biotech", "CloseCombat", "Con", "Conjuring", "Cracking",
        "Electronics", "Enchanting", "Engineering", "ExoticWeapons", "Firearms", "Influence",
        "Outdoors", "Perception", "Piloting", "Sorcery", "Stealth", "Tasking"
    });
}

public sealed record Sr6CreationSkillSpend(string SkillId, int Rating, IReadOnlyList<string> Specializations);
public sealed record Sr6CreationSkillSelection(IReadOnlyList<Sr6CreationSkillSpend> Allocations, string? AspectedSkillId = null);
public sealed record Sr6CreationSkillOption(string SkillId, int Maximum, bool Available, string? UnavailableReason);
public sealed record Sr6CreationSkillValue(string SkillId, int Rating, IReadOnlyList<string> Specializations, int SpecializationCost, int TotalCost);
public sealed record Sr6CreationSkillPreview(IReadOnlyList<Sr6CreationSkillValue> Values,
    int PointsSpent, int PointsRemaining, bool AllPointsSpent, bool SpecializationsNeedGmReview,
    string AuthorityDigest, IReadOnlyList<string> SourceAnchorIds);

public static class Sr6CreationSkillBlockers
{
    public const string InvalidAllocation = "sr6-creation-skills-invalid-allocation";
    public const string SkillUnavailable = "sr6-creation-skills-unavailable";
    public const string AspectRequired = "sr6-creation-skills-aspect-required";
    public const string AstralPowerRequired = "sr6-creation-skills-astral-power-required";
    public const string MaximumCountExceeded = "sr6-creation-skills-maximum-count-exceeded";
    public const string BudgetExceeded = "sr6-creation-skills-budget-exceeded";
    public const string SpecializationInvalid = "sr6-creation-skills-specialization-invalid";
}
