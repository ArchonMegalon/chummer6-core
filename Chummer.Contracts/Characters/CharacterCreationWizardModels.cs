namespace Chummer.Contracts.Characters;

/// <summary>
/// Versioned, renderer-neutral character creation projection.  Presentation owns navigation;
/// rules/runtime authorities own every budget, option, and blocker placed in this snapshot.
/// </summary>
public static class CharacterCreationWizardSchemas
{
    public const string SnapshotV1 = "chummer.character_creation_wizard.v1";
}

public static class CharacterCreationBuildMethods
{
    public const string Karma = "Karma";
    public const string Priority = "Priority";
    public const string SumToTen = "SumtoTen";
    public const string LifeModules = "LifeModule";

    public static bool IsSupported(string? value)
        => value is Karma or Priority or SumToTen or LifeModules;
}

public static class CharacterCreationWizardStepIds
{
    public const string Basics = "basics";
    public const string Method = "method";
    public const string Foundation = "foundation";
    public const string LifeModules = "life-modules";
    public const string Attributes = "attributes";
    public const string Qualities = "qualities";
    public const string Skills = "skills";
    public const string MagicResonance = "magic-resonance";
    public const string Resources = "resources";
    public const string ContactsLifestyles = "contacts-lifestyles";
    public const string IdentityStory = "identity-story";
    public const string Review = "review";
}

public static class CharacterCreationWizardStepStatuses
{
    public const string NotStarted = "not-started";
    public const string Available = "available";
    public const string InProgress = "in-progress";
    public const string Blocked = "blocked";
    public const string Complete = "complete";
    public const string NeedsReview = "needs-review";
}

public static class CharacterCreationBudgetIds
{
    public const string Karma = "karma";
    public const string PositiveQualities = "positive-qualities";
    public const string NegativeQualities = "negative-qualities";
    public const string NormalAttributes = "normal-attributes";
    public const string SpecialAttributes = "special-attributes";
    public const string ActiveSkills = "active-skills";
    public const string SkillGroups = "skill-groups";
    public const string KnowledgeSkills = "knowledge-skills";
    public const string Contacts = "contacts";
    public const string Resources = "resources";
    public const string SpellsFormsPrograms = "spells-forms-programs";
    public const string LifeModules = "life-modules";
}

public static class CharacterCreationLifeModuleStageIds
{
    public const string Nationality = "nationality";
    public const string FormativeYears = "formative-years";
    public const string TeenYears = "teen-years";
    public const string FurtherEducation = "further-education";
    public const string RealLife = "real-life";

    public static IReadOnlyList<string> RequiredStages { get; } =
    [Nationality, FormativeYears, TeenYears, FurtherEducation];
}

public sealed record CharacterCreationBudgetState(
    string BudgetId,
    string Label,
    decimal Total,
    decimal Used,
    decimal Remaining,
    bool IsExact,
    IReadOnlyList<string> Blockers,
    string Unit);

public sealed record CharacterCreationChoiceCost(
    string BudgetId,
    decimal Delta,
    string Unit);

public sealed record CharacterCreationChoiceConsequence(
    string ConsequenceId,
    string Domain,
    string TargetId,
    string? BeforeValue,
    string? AfterValue,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationLegalOption(
    string OptionId,
    string Label,
    bool IsEnabled,
    string? DisableReasonKey,
    IReadOnlyDictionary<string, string> DisableReasonArguments,
    IReadOnlyList<CharacterCreationChoiceCost> Costs,
    IReadOnlyList<CharacterCreationChoiceConsequence> Consequences,
    IReadOnlyList<string> SourceAnchorIds,
    string? SourceId = null,
    int? SourcePage = null,
    string? VersionId = null);

public sealed record CharacterCreationWizardStageState(
    string StepId,
    string Label,
    string Status,
    bool IsRequired,
    bool IsAvailable,
    bool IsComplete,
    IReadOnlyList<string> BudgetIds,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> LegalNextStepIds);

public sealed record CharacterCreationWizardSnapshot(
    string Schema,
    string WorkspaceId,
    long WorkspaceRevision,
    string ContentDigest,
    string SourceDigest,
    string RulesetId,
    string RuntimeFingerprint,
    string BuildMethod,
    bool CharacterCreated,
    string ActiveStepId,
    IReadOnlyList<CharacterCreationWizardStageState> Steps,
    IReadOnlyList<CharacterCreationBudgetState> Budgets,
    IReadOnlyDictionary<string, IReadOnlyList<CharacterCreationLegalOption>> LegalOptionsByStep,
    IReadOnlyList<string> CompletionBlockers,
    IReadOnlyList<string> Warnings,
    bool CanFinalize,
    string SnapshotDigest);
