namespace Chummer.Contracts.LifeModules;

public sealed record LifeModuleStageDto(
    int Order,
    string Name)
{
    public bool IsRequired => LifeModuleJourneyStageOrders.Required.Contains(Order);

    public bool CanRepeat => Order == LifeModuleJourneyStageOrders.RealLife;
}

public sealed record LifeModuleSummaryDto(
    string Id,
    string Stage,
    string Name,
    string Karma,
    string Source,
    string Page,
    string Story);

public static class LifeModuleJourneySchemas
{
    public const string V1 = "chummer.life_module_journey.v1";
    public const string CatalogAuthorityV1 = "chummer.life_module_catalog_authority.v1";
}

/// <summary>
/// Identifies the exact raw lifemodules.xml bytes from which catalog projections
/// were loaded. The digest is intentionally not a digest of projected DTOs.
/// </summary>
public sealed record LifeModuleCatalogAuthorityDto(
    string Schema,
    string RawXmlDigest,
    IReadOnlyList<string> SourceAnchorIds);

public static class LifeModuleJourneyStageOrders
{
    public const int Nationality = 1;
    public const int FormativeYears = 2;
    public const int TeenYears = 3;
    public const int FurtherEducation = 4;
    public const int RealLife = 5;

    public static IReadOnlyList<int> Required { get; } =
    [Nationality, FormativeYears, TeenYears, FurtherEducation];
}

public sealed record LifeModuleRequirementProjectionDto(
    string RequirementId,
    string Label,
    bool IsMet,
    string? DisableReasonKey,
    IReadOnlyDictionary<string, string> DisableReasonArguments,
    IReadOnlyList<string> SourceAnchorIds,
    string Operator,
    string SubjectKind,
    IReadOnlyList<string> AcceptedValues,
    string RawXml,
    bool RequiresCharacterAuthority);

public sealed record LifeModuleEffectProjectionDto(
    string EffectId,
    string Domain,
    string TargetId,
    string? BeforeValue,
    string? AfterValue,
    string? BudgetId,
    decimal BudgetDelta,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyDictionary<string, string> Parameters,
    string RawXml,
    bool IsFullyTyped,
    string? AuthorityBlocker);

public sealed record LifeModuleFollowUpOptionDto(
    string OptionId,
    string Label,
    bool IsEnabled,
    string? DisableReasonKey,
    IReadOnlyDictionary<string, string> DisableReasonArguments,
    string SourceValue);

public sealed record LifeModuleFollowUpPromptDto(
    string PromptId,
    string Label,
    string InputKind,
    bool IsRequired,
    IReadOnlyList<LifeModuleFollowUpOptionDto> Options,
    IReadOnlyList<string> SourceAnchorIds,
    string EffectId,
    string ValuePath);

public sealed record LifeModuleVersionProjectionDto(
    string VersionId,
    string Label,
    bool IsEnabled,
    IReadOnlyList<LifeModuleRequirementProjectionDto> Requirements,
    IReadOnlyList<LifeModuleEffectProjectionDto> Effects,
    IReadOnlyList<LifeModuleFollowUpPromptDto> FollowUps,
    IReadOnlyList<string> SourceAnchorIds,
    string StoryTemplate,
    decimal KarmaCost,
    string KarmaRaw,
    bool KarmaIsExact,
    string Source,
    int? Page,
    string PageReference,
    IReadOnlyList<string> AuthorityBlockers);

public sealed record LifeModuleLegalOptionDto(
    string ModuleId,
    int StageOrder,
    string Name,
    decimal KarmaCost,
    string Source,
    int? Page,
    string StoryTemplate,
    bool IsEnabled,
    IReadOnlyList<LifeModuleRequirementProjectionDto> Requirements,
    IReadOnlyList<LifeModuleVersionProjectionDto> Versions,
    IReadOnlyList<LifeModuleEffectProjectionDto> Effects,
    IReadOnlyList<LifeModuleFollowUpPromptDto> FollowUps,
    IReadOnlyList<string> SourceAnchorIds,
    string StageId,
    bool CanRepeat,
    string KarmaRaw,
    bool KarmaIsExact,
    string PageReference,
    IReadOnlyList<string> AuthorityBlockers);

public sealed record LifeModuleStageProgressDto(
    int StageOrder,
    string StageId,
    string Label,
    bool IsRequired,
    bool IsComplete,
    bool IsCurrent,
    bool CanRepeat,
    IReadOnlyList<string> SelectedModuleIds,
    IReadOnlyList<string> Blockers);

public sealed record LifeModuleJourneyStateDto(
    string Schema,
    string WorkspaceId,
    long WorkspaceRevision,
    string ContentDigest,
    string SourceDigest,
    IReadOnlyList<LifeModuleStageProgressDto> Stages,
    int CurrentStageOrder,
    IReadOnlyList<LifeModuleLegalOptionDto> LegalOptions,
    decimal KarmaTotal,
    decimal KarmaUsed,
    decimal KarmaRemaining,
    bool BudgetIsExact,
    IReadOnlyList<string> PendingFollowUpIds,
    IReadOnlyList<string> CompletionBlockers,
    string SnapshotDigest);
