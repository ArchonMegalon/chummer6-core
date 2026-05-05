using Chummer.Contracts.Diagnostics;
using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Content;

public static class RuleEnvironmentStudioDiffStatuses
{
    public const string FirstPin = "first-pin";
    public const string Clear = "clear";
    public const string RequiresReview = "requires-review";
}

public static class RuleEnvironmentStudioExplainSources
{
    public const string Engine = "engine";
}

public sealed record RuleEnvironmentStudioLifecycleProjection(
    string CurrentStage,
    string PromotionTargetStage,
    string UpdateChannel,
    string PublicationStatus,
    string Visibility,
    string PromotionSummary,
    string RollbackSummary,
    string LineageSummary,
    DateTimeOffset? PublishedAtUtc = null);

public sealed record RuleEnvironmentStudioDiffProjection(
    string Status,
    string DesiredRuntimeFingerprint,
    string? CurrentRuntimeFingerprint = null,
    RuntimeLockDiffProjection? Delta = null,
    string? SummaryKey = null,
    IReadOnlyList<RulesetExplainParameter>? SummaryParameters = null);

public sealed record RuleEnvironmentStudioExplainReceiptProjection(
    string SourceKind,
    string RuntimeFingerprint,
    string PrivacyMode,
    string DiffStatus,
    string CurrentStage,
    string PromotionTargetStage,
    string SummaryKey,
    IReadOnlyList<RulesetExplainParameter> SummaryParameters,
    IReadOnlyList<string> RequiredCoverageKinds,
    int CounterfactualLimit,
    bool DeltaIncluded,
    bool EngineTruthRequired = true,
    bool SourceAnchorsRequired = true);

public sealed record RuleEnvironmentStudioProjection(
    string ProfileId,
    string RulesetId,
    RuleProfileApplyTarget Target,
    RuntimeInspectorProjection RuntimeInspector,
    RuleProfilePreviewReceipt Preview,
    RuleEnvironmentStudioLifecycleProjection Lifecycle,
    RuleEnvironmentStudioDiffProjection Diff,
    RuleEnvironmentStudioExplainReceiptProjection ExplainReceipt,
    RuntimeLockRegistryEntry? CurrentRuntime = null);
