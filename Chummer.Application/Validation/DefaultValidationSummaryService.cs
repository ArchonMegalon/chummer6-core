using Chummer.Contracts;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Validation;

namespace Chummer.Application.Validation;

public sealed class DefaultValidationSummaryService : IValidationSummaryService
{
    public ValidationSummary BuildSummary(
        string scopeKind,
        string scopeId,
        IReadOnlyList<RulesetCapabilityDiagnostic> diagnostics,
        string? runtimeFingerprint = null,
        IReadOnlyDictionary<string, ExplainHookReference>? explainHooksByCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentNullException.ThrowIfNull(diagnostics);

        string normalizedScopeKind = scopeKind.Trim().ToLowerInvariant();
        string normalizedScopeId = scopeId.Trim();
        string? normalizedRuntimeFingerprint = Normalize(runtimeFingerprint);

        RulesetCapabilityDiagnostic[] orderedDiagnostics = diagnostics
            .OrderBy(GetSeverityRank)
            .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(static diagnostic => ResolveSubjectId(diagnostic), StringComparer.Ordinal)
            .ThenBy(static diagnostic => RulesetCapabilityDiagnosticLocalization.ResolveMessageKey(diagnostic), StringComparer.Ordinal)
            .ToArray();

        ValidationFailureEnvelope[] failures = orderedDiagnostics
            .Select((diagnostic, index) => CreateFailureEnvelope(
                diagnostic,
                index,
                normalizedRuntimeFingerprint,
                explainHooksByCode))
            .ToArray();

        int errorCount = failures.Count(static failure => string.Equals(failure.Severity, RulesetCapabilityDiagnosticSeverities.Error, StringComparison.Ordinal));
        int warningCount = failures.Count(static failure => string.Equals(failure.Severity, RulesetCapabilityDiagnosticSeverities.Warning, StringComparison.Ordinal));
        int infoCount = failures.Length - errorCount - warningCount;
        string state = errorCount > 0
            ? ValidationSummaryStates.Invalid
            : warningCount > 0
                ? ValidationSummaryStates.Warnings
                : ValidationSummaryStates.Valid;

        RulesetExplainParameter[] summaryParameters =
        [
            new("scopeKind", RulesetCapabilityBridge.FromObject(normalizedScopeKind)),
            new("scopeId", RulesetCapabilityBridge.FromObject(normalizedScopeId)),
            new("totalCount", RulesetCapabilityBridge.FromObject(failures.Length)),
            new("errorCount", RulesetCapabilityBridge.FromObject(errorCount)),
            new("warningCount", RulesetCapabilityBridge.FromObject(warningCount)),
            new("infoCount", RulesetCapabilityBridge.FromObject(infoCount))
        ];

        return new ValidationSummary(
            ScopeKind: normalizedScopeKind,
            ScopeId: normalizedScopeId,
            State: state,
            SummaryKey: $"validation.summary.{state}",
            SummaryParameters: summaryParameters,
            TotalCount: failures.Length,
            ErrorCount: errorCount,
            WarningCount: warningCount,
            InfoCount: infoCount,
            Failures: failures);
    }

    private static int GetSeverityRank(RulesetCapabilityDiagnostic diagnostic)
    {
        string severity = Normalize(diagnostic.Severity) ?? RulesetCapabilityDiagnosticSeverities.Info;
        return severity switch
        {
            RulesetCapabilityDiagnosticSeverities.Error => 0,
            RulesetCapabilityDiagnosticSeverities.Warning => 1,
            _ => 2
        };
    }

    private static ValidationFailureEnvelope CreateFailureEnvelope(
        RulesetCapabilityDiagnostic diagnostic,
        int index,
        string? runtimeFingerprint,
        IReadOnlyDictionary<string, ExplainHookReference>? explainHooksByCode)
    {
        string normalizedCode = diagnostic.Code.Trim();
        string messageKey = RulesetCapabilityDiagnosticLocalization.ResolveMessageKey(diagnostic);
        string? subjectId = ResolveSubjectId(diagnostic);
        string failureId = $"{index:D4}:{normalizedCode}:{subjectId ?? "global"}";

        ExplainHookReference? explain = null;
        if (explainHooksByCode is not null && explainHooksByCode.TryGetValue(normalizedCode, out ExplainHookReference? reference))
        {
            explain = reference;
        }

        return new ValidationFailureEnvelope(
            FailureId: failureId,
            Code: normalizedCode,
            Severity: Normalize(diagnostic.Severity) ?? RulesetCapabilityDiagnosticSeverities.Info,
            MessageKey: messageKey,
            MessageParameters: RulesetCapabilityDiagnosticLocalization.ResolveMessageParameters(diagnostic),
            SubjectId: subjectId,
            CapabilityId: ResolveParameterString(diagnostic, "capabilityId"),
            ProviderId: ResolveParameterString(diagnostic, "providerId"),
            PackId: ResolveParameterString(diagnostic, "packId"),
            RuntimeFingerprint: runtimeFingerprint,
            Explain: explain);
    }

    private static string? ResolveSubjectId(RulesetCapabilityDiagnostic diagnostic)
        => ResolveParameterString(diagnostic, "subjectId");

    private static string? ResolveParameterString(RulesetCapabilityDiagnostic diagnostic, string name)
    {
        RulesetExplainParameter? parameter = RulesetCapabilityDiagnosticLocalization
            .ResolveMessageParameters(diagnostic)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

        return parameter is null ? null : Normalize(RulesetCapabilityBridge.ToObject(parameter.Value)?.ToString());
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
