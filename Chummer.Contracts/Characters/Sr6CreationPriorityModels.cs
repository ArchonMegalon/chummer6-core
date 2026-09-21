namespace Chummer.Contracts.Characters;

/// <summary>
/// A pure SR6 allocation request. It is not a workspace mutation or permission
/// to enable a sourcebook. The creation service must resolve the active profile
/// before using the calculation in a wizard.
/// </summary>
public sealed record Sr6CreationPriorityRequest(
    string RulesetId,
    string BuildMethod,
    IReadOnlyList<Sr6CreationPriorityChoice> Assignments);

public sealed record Sr6CreationPriorityChoice(string CategoryId, string Rank);

/// <summary>
/// SR6 adjustment points are deliberately distinct from SR5 special-attribute
/// points. Magic/Resonance rank is retained for the subsequent talent choice;
/// selecting a rank alone does not grant Magic, spells or power points.
/// </summary>
public sealed record Sr6CreationPriorityBudget(
    int AttributePoints,
    int SkillPoints,
    int ResourcesNuyen,
    int MetatypeAdjustmentPoints,
    string MagicResonanceRank);

public sealed record Sr6CreationPriorityResult(
    bool IsValid,
    int? SumToTenTotal,
    Sr6CreationPriorityBudget? Budget,
    IReadOnlyList<string> Blockers);

public static class Sr6CreationPriorityBlockers
{
    public const string RulesetMismatch = "sr6-creation-priority-ruleset-mismatch";
    public const string MethodUnsupported = "sr6-creation-priority-method-unsupported";
    public const string CompanionRequired = "sr6-creation-priority-companion-required";
    public const string CategoriesInvalid = "sr6-creation-priority-categories-invalid";
    public const string RankInvalid = "sr6-creation-priority-rank-invalid";
    public const string UniqueRanksRequired = "sr6-creation-priority-unique-ranks-required";
    public const string SumToTenMismatch = "sr6-creation-priority-sum-to-ten-mismatch";
}
