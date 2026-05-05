using Chummer.Contracts;
using Chummer.Contracts.Content;
using Chummer.Contracts.Diagnostics;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Validation;

namespace Chummer.Application.Explain;

public sealed class DefaultExplainValuePacketService : IExplainValuePacketService
{
    public const int MaxCounterfactuals = 3;
    private static readonly HashSet<string> SupportedCounterfactualOutcomeKinds = new(StringComparer.Ordinal)
    {
        ExplainCounterfactualOutcomeKinds.Why,
        ExplainCounterfactualOutcomeKinds.WhyNot,
        ExplainCounterfactualOutcomeKinds.WhatIf
    };

    public ExplainValuePacket CreatePacket(ExplainValuePacketInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        string packetId = RequireTrimmed(input.PacketId, nameof(input.PacketId));
        string calculationKey = RequireTrimmed(input.CalculationKey, nameof(input.CalculationKey));
        string rulesetId = RequireTrimmed(input.RulesetId, nameof(input.RulesetId));
        string runtimeFingerprint = RequireTrimmed(input.RuntimeFingerprint, nameof(input.RuntimeFingerprint));
        string subjectId = RequireTrimmed(input.SubjectId, nameof(input.SubjectId));
        string subjectKind = RequireTrimmed(input.SubjectKind, nameof(input.SubjectKind));
        string privacyMode = RequireTrimmed(input.PrivacyMode, nameof(input.PrivacyMode));
        string summaryKey = RequireTrimmed(input.SummaryKey, nameof(input.SummaryKey));

        ExplainTraceDto explainTrace = NormalizeTrace(input.ExplainTrace);
        ExplainEvidencePointerDto[] evidence = CollectEvidence(explainTrace);
        ExplainEvidencePointerDto[] sourceAnchors = CollectSourceAnchors(evidence);
        ValidationSummary? validation = NormalizeValidation(input.Validation);
        RuntimeLockDiffProjection? delta = NormalizeDelta(input.Delta);
        ExplainCounterfactualPacket[] counterfactuals = NormalizeCounterfactuals(input.Counterfactuals);
        ExplainValuePacketCoverageRow[] coverageRegistry = BuildCoverageRegistry(
            packetId,
            subjectId,
            input.Value,
            explainTrace,
            sourceAnchors,
            validation,
            delta,
            counterfactuals);

        return new ExplainValuePacket(
            PacketId: packetId,
            CalculationKey: calculationKey,
            RulesetId: rulesetId,
            RuntimeFingerprint: runtimeFingerprint,
            SubjectId: subjectId,
            SubjectKind: subjectKind,
            Value: input.Value,
            ExplainTrace: explainTrace,
            SourceAnchors: sourceAnchors,
            Evidence: evidence,
            Validation: validation,
            Delta: delta,
            Counterfactuals: counterfactuals,
            CoverageRegistry: coverageRegistry,
            PrivacyMode: privacyMode,
            SummaryKey: summaryKey,
            SummaryParameters: input.SummaryParameters ?? [],
            CounterfactualLimit: MaxCounterfactuals,
            CounterfactualOverflowCount: Math.Max(0, (input.Counterfactuals?.Count ?? 0) - counterfactuals.Length));
    }

    private static ExplainCounterfactualPacket[] NormalizeCounterfactuals(IReadOnlyList<ExplainCounterfactualInput>? inputs)
    {
        return (inputs ?? [])
            .Select(NormalizeCounterfactual)
            .OrderBy(static packet => packet.DisplayOrder)
            .ThenBy(static packet => packet.CounterfactualId, StringComparer.Ordinal)
            .Take(MaxCounterfactuals)
            .ToArray();
    }

    private static ExplainCounterfactualPacket NormalizeCounterfactual(ExplainCounterfactualInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        ExplainTraceDto explainTrace = NormalizeTrace(input.ExplainTrace);
        ExplainEvidencePointerDto[] evidence = CollectEvidence(explainTrace);

        return new ExplainCounterfactualPacket(
            CounterfactualId: RequireTrimmed(input.CounterfactualId, nameof(input.CounterfactualId)),
            OutcomeKind: NormalizeCounterfactualOutcomeKind(input.OutcomeKind),
            LabelKey: RequireTrimmed(input.LabelKey, nameof(input.LabelKey)),
            LabelParameters: input.LabelParameters ?? [],
            Value: input.Value,
            ExplainTrace: explainTrace,
            SourceAnchors: CollectSourceAnchors(evidence),
            Evidence: evidence,
            Validation: NormalizeValidation(input.Validation),
            Delta: NormalizeDelta(input.Delta),
            LegalityState: Normalize(input.LegalityState),
            SummaryKey: RequireTrimmed(input.SummaryKey, nameof(input.SummaryKey)),
            SummaryParameters: input.SummaryParameters ?? [],
            DisplayOrder: input.DisplayOrder);
    }

    private static ExplainValuePacketCoverageRow[] BuildCoverageRegistry(
        string packetId,
        string subjectId,
        RulesetCapabilityValue? value,
        ExplainTraceDto explainTrace,
        IReadOnlyList<ExplainEvidencePointerDto> sourceAnchors,
        ValidationSummary? validation,
        RuntimeLockDiffProjection? delta,
        IReadOnlyList<ExplainCounterfactualPacket> counterfactuals)
    {
        List<ExplainValuePacketCoverageRow> rows = [];

        AppendVisibleResultCoverageRows(
            rows,
            subjectId,
            value,
            explainTrace,
            sourceAnchors,
            validation,
            delta,
            packetId,
            counterfactualId: null,
            outcomeKind: null,
            legalityState: null);

        foreach (ExplainCounterfactualPacket counterfactual in counterfactuals)
        {
            rows.Add(Row(
                ExplainValuePacketCoverageKinds.Counterfactual,
                counterfactual.CounterfactualId,
                "explain.packet.coverage.counterfactual",
                [
                    Param("counterfactualId", counterfactual.CounterfactualId),
                    Param("outcomeKind", counterfactual.OutcomeKind)
                ]));

            AppendVisibleResultCoverageRows(
                rows,
                counterfactual.CounterfactualId,
                counterfactual.Value,
                counterfactual.ExplainTrace,
                counterfactual.SourceAnchors,
                counterfactual.Validation,
                counterfactual.Delta,
                packetId,
                counterfactual.CounterfactualId,
                counterfactual.OutcomeKind,
                counterfactual.LegalityState);
        }

        return rows
            .OrderBy(static row => row.SurfaceKind, StringComparer.Ordinal)
            .ThenBy(static row => row.SubjectId, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AppendVisibleResultCoverageRows(
        ICollection<ExplainValuePacketCoverageRow> rows,
        string subjectId,
        RulesetCapabilityValue? value,
        ExplainTraceDto explainTrace,
        IReadOnlyList<ExplainEvidencePointerDto> sourceAnchors,
        ValidationSummary? validation,
        RuntimeLockDiffProjection? delta,
        string packetId,
        string? counterfactualId,
        string? outcomeKind,
        string? legalityState)
    {
        if (value is not null || explainTrace.FinalValue is not null)
        {
            rows.Add(Row(
                ExplainValuePacketCoverageKinds.MechanicalResult,
                subjectId,
                "explain.packet.coverage.mechanical-result",
                BuildCoverageParameters(packetId, subjectId, counterfactualId, outcomeKind)));
        }

        string? normalizedLegalityState = Normalize(legalityState) ?? validation?.State;
        string legalitySubjectId = validation?.ScopeId ?? subjectId;
        if (!string.IsNullOrWhiteSpace(normalizedLegalityState))
        {
            rows.Add(Row(
                ExplainValuePacketCoverageKinds.LegalityState,
                legalitySubjectId,
                "explain.packet.coverage.legality-state",
                BuildCoverageParameters(
                    packetId,
                    legalitySubjectId,
                    counterfactualId,
                    outcomeKind,
                    Param("state", normalizedLegalityState))));
        }

        if ((validation?.WarningCount ?? 0) > 0)
        {
            rows.Add(Row(
                ExplainValuePacketCoverageKinds.Warning,
                legalitySubjectId,
                "explain.packet.coverage.warning",
                BuildCoverageParameters(
                    packetId,
                    legalitySubjectId,
                    counterfactualId,
                    outcomeKind,
                    Param("warningCount", validation!.WarningCount))));
        }

        if (delta is not null && delta.Changes.Count > 0)
        {
            rows.Add(Row(
                ExplainValuePacketCoverageKinds.BeforeAfterDelta,
                subjectId,
                "explain.packet.coverage.before-after-delta",
                BuildCoverageParameters(
                    packetId,
                    subjectId,
                    counterfactualId,
                    outcomeKind,
                    Param("changeCount", delta.Changes.Count))));
        }

        if (sourceAnchors.Count > 0)
        {
            rows.Add(Row(
                ExplainValuePacketCoverageKinds.SourceAnchor,
                $"{subjectId}:anchors",
                "explain.packet.coverage.source-anchors",
                BuildCoverageParameters(
                    packetId,
                    subjectId,
                    counterfactualId,
                    outcomeKind,
                    Param("sourceAnchorCount", sourceAnchors.Count))));
        }
    }

    private static ValidationSummary? NormalizeValidation(ValidationSummary? summary)
    {
        if (summary is null)
        {
            return null;
        }

        ValidationFailureEnvelope[] failures = (summary.Failures ?? [])
            .Select(static failure => failure with
            {
                FailureId = RequireTrimmed(failure.FailureId, nameof(failure.FailureId)),
                Code = RequireTrimmed(failure.Code, nameof(failure.Code)),
                Severity = RequireTrimmed(failure.Severity, nameof(failure.Severity)),
                MessageKey = RequireTrimmed(failure.MessageKey, nameof(failure.MessageKey)),
                MessageParameters = failure.MessageParameters ?? [],
                SubjectId = Normalize(failure.SubjectId),
                CapabilityId = Normalize(failure.CapabilityId),
                ProviderId = Normalize(failure.ProviderId),
                PackId = Normalize(failure.PackId),
                RuntimeFingerprint = Normalize(failure.RuntimeFingerprint)
            })
            .OrderBy(static failure => failure.FailureId, StringComparer.Ordinal)
            .ToArray();

        return summary with
        {
            ScopeKind = RequireTrimmed(summary.ScopeKind, nameof(summary.ScopeKind)),
            ScopeId = RequireTrimmed(summary.ScopeId, nameof(summary.ScopeId)),
            State = RequireTrimmed(summary.State, nameof(summary.State)),
            SummaryKey = RequireTrimmed(summary.SummaryKey, nameof(summary.SummaryKey)),
            SummaryParameters = summary.SummaryParameters ?? [],
            Failures = failures
        };
    }

    private static RuntimeLockDiffProjection? NormalizeDelta(RuntimeLockDiffProjection? delta)
    {
        if (delta is null)
        {
            return null;
        }

        RuntimeLockDiffChange[] changes = (delta.Changes ?? [])
            .Select(static change => change with
            {
                Kind = RequireTrimmed(change.Kind, nameof(change.Kind)),
                SubjectId = RequireTrimmed(change.SubjectId, nameof(change.SubjectId)),
                BeforeValue = Normalize(change.BeforeValue),
                AfterValue = Normalize(change.AfterValue),
                ReasonKey = RequireTrimmed(change.ReasonKey, nameof(change.ReasonKey)),
                ReasonParameters = change.ReasonParameters ?? []
            })
            .OrderBy(static change => change.Kind, StringComparer.Ordinal)
            .ThenBy(static change => change.SubjectId, StringComparer.Ordinal)
            .ThenBy(static change => change.BeforeValue, StringComparer.Ordinal)
            .ThenBy(static change => change.AfterValue, StringComparer.Ordinal)
            .ToArray();

        return delta with
        {
            BeforeFingerprint = RequireTrimmed(delta.BeforeFingerprint, nameof(delta.BeforeFingerprint)),
            AfterFingerprint = RequireTrimmed(delta.AfterFingerprint, nameof(delta.AfterFingerprint)),
            Changes = changes
        };
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
                    PackId = Normalize(step.PackId),
                    ExplanationKey = RequireTrimmed(step.ExplanationKey, nameof(step.ExplanationKey)),
                    ExplanationParameters = step.ExplanationParameters ?? [],
                    Category = RequireTrimmed(step.Category, nameof(step.Category)),
                    RuleId = Normalize(step.RuleId),
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
            .Select(static pointer => pointer with
            {
                Kind = RequireTrimmed(pointer.Kind, nameof(pointer.Kind)),
                Pointer = RequireTrimmed(pointer.Pointer, nameof(pointer.Pointer)),
                LabelKey = Normalize(pointer.LabelKey),
                LabelParameters = pointer.LabelParameters ?? [],
                ProviderId = Normalize(pointer.ProviderId),
                PackId = Normalize(pointer.PackId),
                RuleId = Normalize(pointer.RuleId)
            })
            .GroupBy(static pointer => $"{pointer.Kind}|{pointer.Pointer}|{pointer.RuleId}", StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static pointer => pointer.Kind, StringComparer.Ordinal)
            .ThenBy(static pointer => pointer.Pointer, StringComparer.Ordinal)
            .ThenBy(static pointer => pointer.RuleId, StringComparer.Ordinal)
            .ToArray();
    }

    private static ExplainEvidencePointerDto[] CollectSourceAnchors(IEnumerable<ExplainEvidencePointerDto> evidence)
    {
        return evidence
            .Where(static pointer =>
                string.Equals(pointer.Kind, RulesetEvidencePointerKinds.RuleReference, StringComparison.Ordinal)
                || string.Equals(pointer.Kind, RulesetEvidencePointerKinds.RuleProfile, StringComparison.Ordinal)
                || string.Equals(pointer.Kind, RulesetEvidencePointerKinds.RuntimeLock, StringComparison.Ordinal))
            .ToArray();
    }

    private static ExplainEvidencePointerDto[] NormalizeEvidence(IReadOnlyList<ExplainEvidencePointerDto>? evidence)
        => evidence?.ToArray() ?? [];

    private static ExplainValuePacketCoverageRow Row(
        string surfaceKind,
        string subjectId,
        string summaryKey,
        IReadOnlyList<RulesetExplainParameter> summaryParameters)
        => new(
            SurfaceKind: surfaceKind,
            SubjectId: subjectId,
            SummaryKey: summaryKey,
            SummaryParameters: summaryParameters);

    private static string RequireTrimmed(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value.Trim();
    }

    private static string NormalizeCounterfactualOutcomeKind(string value)
    {
        string normalized = RequireTrimmed(value, nameof(ExplainCounterfactualInput.OutcomeKind));
        if (SupportedCounterfactualOutcomeKinds.Contains(normalized))
        {
            return normalized;
        }

        throw new ArgumentOutOfRangeException(
            nameof(ExplainCounterfactualInput.OutcomeKind),
            normalized,
            "Counterfactual outcome kinds must stay within the promoted core packet surface.");
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static RulesetExplainParameter[] BuildCoverageParameters(
        string packetId,
        string subjectId,
        string? counterfactualId,
        string? outcomeKind,
        params RulesetExplainParameter[] extras)
    {
        List<RulesetExplainParameter> parameters =
        [
            Param("packetId", packetId),
            Param("subjectId", subjectId)
        ];

        if (!string.IsNullOrWhiteSpace(counterfactualId))
        {
            parameters.Add(Param("counterfactualId", counterfactualId));
        }

        if (!string.IsNullOrWhiteSpace(outcomeKind))
        {
            parameters.Add(Param("outcomeKind", outcomeKind));
        }

        parameters.AddRange(extras);
        return [.. parameters];
    }

    private static RulesetExplainParameter Param(string name, object? value)
        => new(name, RulesetCapabilityBridge.FromObject(value));
}
