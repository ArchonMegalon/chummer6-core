using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Validation;

public static class ValidationSummaryStates
{
    public const string Valid = "valid";
    public const string Warnings = "warnings";
    public const string Invalid = "invalid";
}

public sealed record ValidationFailureEnvelope(
    string FailureId,
    string Code,
    string Severity,
    string MessageKey,
    IReadOnlyList<RulesetExplainParameter> MessageParameters,
    string? SubjectId = null,
    string? CapabilityId = null,
    string? ProviderId = null,
    string? PackId = null,
    string? RuntimeFingerprint = null,
    ExplainHookReference? Explain = null);

public sealed record ValidationSummary(
    string ScopeKind,
    string ScopeId,
    string State,
    string SummaryKey,
    IReadOnlyList<RulesetExplainParameter> SummaryParameters,
    int TotalCount,
    int ErrorCount,
    int WarningCount,
    int InfoCount,
    IReadOnlyList<ValidationFailureEnvelope> Failures);

public static class ValidationSummaryLocalization
{
    public static string ResolveSummaryKey(ValidationSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return string.IsNullOrWhiteSpace(summary.SummaryKey)
            ? $"validation.summary.{summary.State}"
            : summary.SummaryKey;
    }

    public static IReadOnlyList<RulesetExplainParameter> ResolveSummaryParameters(ValidationSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return summary.SummaryParameters ?? [];
    }
}
