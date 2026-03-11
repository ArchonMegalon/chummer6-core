using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.BuildLab;

public sealed record BuildVariantConstraint(
    string ConstraintId,
    string ConstraintKey,
    IReadOnlyList<RulesetExplainParameter> Parameters,
    string Severity = RulesetCapabilityDiagnosticSeverities.Warning);

public sealed record BuildVariantScore(
    string MetricId,
    decimal Value,
    decimal Weight = 1m,
    string? ExplainEntryId = null);

public sealed record BuildVariantProjection(
    string VariantId,
    string LabelKey,
    IReadOnlyList<RulesetExplainParameter> LabelParameters,
    IReadOnlyList<string> RoleTags,
    decimal Rank,
    string SummaryKey,
    IReadOnlyList<RulesetExplainParameter> SummaryParameters,
    IReadOnlyList<BuildVariantScore> Scores,
    IReadOnlyList<BuildVariantConstraint> Constraints,
    IReadOnlyList<RulesetCapabilityDiagnostic>? Diagnostics = null,
    string? ExplainEntryId = null);

public sealed record KarmaSpendStep(
    string StepId,
    int KarmaTotal,
    decimal Rank,
    string SummaryKey,
    IReadOnlyList<RulesetExplainParameter> SummaryParameters,
    IReadOnlyList<BuildVariantScore> Scores,
    IReadOnlyList<string> AppliedChoiceIds,
    IReadOnlyList<RulesetCapabilityDiagnostic>? Diagnostics = null,
    string? ExplainEntryId = null);

public sealed record KarmaSpendProjection(
    string VariantId,
    string SummaryKey,
    IReadOnlyList<RulesetExplainParameter> SummaryParameters,
    IReadOnlyList<KarmaSpendStep> Steps,
    IReadOnlyList<RulesetCapabilityDiagnostic>? Diagnostics = null,
    string? ExplainEntryId = null);

public sealed record BuildTrapChoice(
    string ChoiceId,
    string ReasonKey,
    IReadOnlyList<RulesetExplainParameter> Parameters,
    string Severity = RulesetCapabilityDiagnosticSeverities.Warning,
    string? ExplainEntryId = null);

public sealed record BuildRoleOverlap(
    string LeftVariantId,
    string RightVariantId,
    decimal OverlapScore,
    string ReasonKey,
    IReadOnlyList<RulesetExplainParameter> ReasonParameters,
    string? ExplainEntryId = null);

public sealed record BuildCorePackageSuggestion(
    string PackageId,
    string LabelKey,
    IReadOnlyList<RulesetExplainParameter> LabelParameters,
    decimal Rank,
    string SummaryKey,
    IReadOnlyList<RulesetExplainParameter> SummaryParameters,
    IReadOnlyList<RulesetCapabilityDiagnostic>? Diagnostics = null,
    string? ExplainEntryId = null);
