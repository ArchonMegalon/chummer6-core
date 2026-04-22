using Chummer.Contracts;
using Chummer.Contracts.Diagnostics;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Explain;

public sealed class DefaultCalculationReportService : ICalculationReportService
{
    public CalculationReportPacket CreatePacket(CalculationReportInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        string reportId = RequireTrimmed(input.ReportId, nameof(input.ReportId));
        string calculationKey = RequireTrimmed(input.CalculationKey, nameof(input.CalculationKey));
        string appVersion = RequireTrimmed(input.AppVersion, nameof(input.AppVersion));
        string platform = RequireTrimmed(input.Platform, nameof(input.Platform));
        string rulesetId = RequireTrimmed(input.RulesetId, nameof(input.RulesetId));
        string runtimeFingerprint = RequireTrimmed(input.RuntimeFingerprint, nameof(input.RuntimeFingerprint));
        string subjectId = RequireTrimmed(input.SubjectId, nameof(input.SubjectId));
        string subjectKind = RequireTrimmed(input.SubjectKind, nameof(input.SubjectKind));
        string privacyMode = RequireTrimmed(input.PrivacyMode, nameof(input.PrivacyMode));

        ExplainTraceDto explainTrace = NormalizeTrace(input.ExplainTrace);
        ExplainEvidencePointerDto[] evidence = CollectEvidence(explainTrace);
        ExplainEvidencePointerDto[] sourceAnchors = evidence
            .Where(static pointer => string.Equals(pointer.Kind, RulesetEvidencePointerKinds.RuleReference, StringComparison.Ordinal))
            .ToArray();
        CalculationReportRecentChange[] recentChanges = (input.RecentChanges ?? [])
            .Select(static change => new CalculationReportRecentChange(
                ChangeKey: change.ChangeKey.Trim(),
                Parameters: change.Parameters ?? []))
            .Where(static change => !string.IsNullOrWhiteSpace(change.ChangeKey))
            .ToArray();

        return new CalculationReportPacket(
            ReportId: reportId,
            CalculationKey: calculationKey,
            AppVersion: appVersion,
            Platform: platform,
            RulesetId: rulesetId,
            RuntimeFingerprint: runtimeFingerprint,
            RuleEnvironmentId: string.IsNullOrWhiteSpace(input.RuleEnvironmentId) ? null : input.RuleEnvironmentId.Trim(),
            SubjectId: subjectId,
            SubjectKind: subjectKind,
            ActualValue: input.ActualValue,
            ExpectedValue: string.IsNullOrWhiteSpace(input.ExpectedValue) ? null : input.ExpectedValue.Trim(),
            ExplainTrace: explainTrace,
            SourceAnchors: sourceAnchors,
            Evidence: evidence,
            RecentChanges: recentChanges,
            PrivacyMode: privacyMode,
            IncludesCharacterSnapshot: input.IncludesCharacterSnapshot,
            SummaryKey: "support.calculation-report.summary",
            SummaryParameters:
            [
                Param("calculationKey", calculationKey),
                Param("subjectId", subjectId),
                Param("subjectKind", subjectKind),
                Param("rulesetId", rulesetId),
                Param("runtimeFingerprint", runtimeFingerprint),
                Param("sourceAnchorCount", sourceAnchors.Length),
                Param("recentChangeCount", recentChanges.Length)
            ]);
    }

    private static ExplainTraceDto NormalizeTrace(ExplainTraceDto trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        return trace with
        {
            TargetKey = RequireTrimmed(trace.TargetKey, nameof(trace.TargetKey)),
            SummaryKey = RequireTrimmed(trace.SummaryKey, nameof(trace.SummaryKey)),
            SummaryParameters = trace.SummaryParameters ?? [],
            Steps = (trace.Steps ?? [])
                .Select(static step => step with
                {
                    ProviderId = RequireTrimmed(step.ProviderId, nameof(step.ProviderId)),
                    CapabilityId = RequireTrimmed(step.CapabilityId, nameof(step.CapabilityId)),
                    ExplanationKey = RequireTrimmed(step.ExplanationKey, nameof(step.ExplanationKey)),
                    ExplanationParameters = step.ExplanationParameters ?? [],
                    Category = RequireTrimmed(step.Category, nameof(step.Category)),
                    Evidence = NormalizeEvidence(step.Evidence)
                })
                .ToArray(),
            Evidence = NormalizeEvidence(trace.Evidence),
            ProvenanceEnvelope = trace.ProvenanceEnvelope,
            EvidenceEnvelope = trace.EvidenceEnvelope
        };
    }

    private static ExplainEvidencePointerDto[] CollectEvidence(ExplainTraceDto trace)
    {
        IEnumerable<ExplainEvidencePointerDto> all = (trace.Evidence ?? [])
            .Concat(trace.Steps.SelectMany(static step => step.Evidence ?? []))
            .Concat(trace.EvidenceEnvelope?.Pointers ?? []);

        return all
            .Where(static pointer => !string.IsNullOrWhiteSpace(pointer.Kind) && !string.IsNullOrWhiteSpace(pointer.Pointer))
            .Select(pointer => pointer with
            {
                Kind = pointer.Kind.Trim(),
                Pointer = pointer.Pointer.Trim(),
                LabelKey = string.IsNullOrWhiteSpace(pointer.LabelKey) ? null : pointer.LabelKey.Trim(),
                LabelParameters = pointer.LabelParameters ?? [],
                ProviderId = string.IsNullOrWhiteSpace(pointer.ProviderId) ? null : pointer.ProviderId.Trim(),
                PackId = string.IsNullOrWhiteSpace(pointer.PackId) ? null : pointer.PackId.Trim(),
                RuleId = string.IsNullOrWhiteSpace(pointer.RuleId) ? null : pointer.RuleId.Trim()
            })
            .GroupBy(static pointer => $"{pointer.Kind}|{pointer.Pointer}|{pointer.RuleId}", StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static pointer => pointer.Kind, StringComparer.Ordinal)
            .ThenBy(static pointer => pointer.Pointer, StringComparer.Ordinal)
            .ThenBy(static pointer => pointer.RuleId, StringComparer.Ordinal)
            .ToArray();
    }

    private static ExplainEvidencePointerDto[] NormalizeEvidence(IReadOnlyList<ExplainEvidencePointerDto>? evidence)
        => evidence?.ToArray() ?? [];

    private static string RequireTrimmed(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value.Trim();
    }

    private static RulesetExplainParameter Param(string name, object? value)
        => new(name, RulesetCapabilityBridge.FromObject(value));
}
