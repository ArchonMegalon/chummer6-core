using Chummer.Contracts.Content;

namespace Chummer.Contracts.Diagnostics;

public static class ShadowRegressionFixtureKinds
{
    public const string CharacterFile = "character-file";
    public const string LegacySaveArchive = "legacy-save-archive";
    public const string SampleCharacter = "sample-character";
    public const string PackFixture = "pack-fixture";
}

public static class ShadowRegressionMetricKinds
{
    public const string DerivedStats = "derived-stats";
    public const string Limits = "limits";
    public const string SkillPools = "skill-pools";
    public const string ConditionTracks = "condition-tracks";
    public const string ResourceTracks = "resource-tracks";
    public const string Availability = "availability";
    public const string Pricing = "pricing";
    public const string Validation = "validation";
    public const string QuickActions = "quick-actions";
    public const string SessionProjection = "session-projection";
}

public static class ShadowRegressionDiffKinds
{
    public const string MissingValue = "missing-value";
    public const string UnexpectedValue = "unexpected-value";
    public const string ValueMismatch = "value-mismatch";
    public const string MissingWarning = "missing-warning";
    public const string UnexpectedWarning = "unexpected-warning";
}

public static class ShadowRegressionSeverityLevels
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
}

public sealed record ShadowRegressionFixtureDescriptor(
    string FixtureId,
    string RulesetId,
    string FixtureKind,
    string RelativePath,
    IReadOnlyList<ArtifactVersionReference> RulePacks,
    bool LegacyOracle = false);

public sealed record ShadowRegressionCorpusDescriptor(
    string CorpusId,
    string RulesetId,
    IReadOnlyList<ShadowRegressionFixtureDescriptor> Fixtures);

public sealed record ShadowRegressionMetricBaseline(
    string FixtureId,
    string MetricKind,
    string SubjectId,
    string ExpectedValueJson);

public sealed record ShadowRegressionExplainReference(
    string TraceId,
    string SubjectId,
    string? ProviderId = null,
    string? PackId = null);

public sealed record ShadowRegressionDiff(
    string FixtureId,
    string MetricKind,
    string DiffKind,
    string Severity,
    string SubjectId,
    string ExpectedValueJson,
    string ActualValueJson,
    string Reason,
    string? WaiverId = null,
    ShadowRegressionExplainReference? Explain = null);

public sealed record ShadowRegressionWaiver(
    string WaiverId,
    string FixtureId,
    string MetricKind,
    string SubjectId,
    string Reason,
    string DecisionId);

public sealed record ShadowRegressionRunReceipt(
    string RunId,
    string CorpusId,
    IReadOnlyList<ShadowRegressionMetricBaseline> Baselines,
    IReadOnlyList<ShadowRegressionDiff> Diffs,
    IReadOnlyList<ShadowRegressionWaiver> AppliedWaivers,
    int ComparedFixtureCount,
    int ComparedMetricCount);
