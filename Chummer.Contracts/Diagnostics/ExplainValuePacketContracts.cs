using Chummer.Contracts.Content;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Validation;

namespace Chummer.Contracts.Diagnostics;

public static class ExplainValuePacketCoverageKinds
{
    public const string MechanicalResult = "mechanical-result";
    public const string LegalityState = "legality-state";
    public const string Warning = "warning";
    public const string BeforeAfterDelta = "before-after-delta";
    public const string Counterfactual = "counterfactual";
    public const string SourceAnchor = "source-anchor";
}

public static class ExplainCounterfactualOutcomeKinds
{
    public const string Why = "why";
    public const string WhyNot = "why-not";
    public const string WhatIf = "what-if";
}

public sealed record ExplainValuePacketCoverageRow(
    string SurfaceKind,
    string SubjectId,
    string SummaryKey,
    IReadOnlyList<RulesetExplainParameter> SummaryParameters,
    bool Covered = true);

public sealed record ExplainCounterfactualInput(
    string CounterfactualId,
    string OutcomeKind,
    string LabelKey,
    IReadOnlyList<RulesetExplainParameter>? LabelParameters,
    RulesetCapabilityValue? Value,
    ExplainTraceDto ExplainTrace,
    ValidationSummary? Validation = null,
    RuntimeLockDiffProjection? Delta = null,
    string? LegalityState = null,
    string SummaryKey = "explain.counterfactual.summary",
    IReadOnlyList<RulesetExplainParameter>? SummaryParameters = null,
    int DisplayOrder = 0);

public sealed record ExplainCounterfactualPacket(
    string CounterfactualId,
    string OutcomeKind,
    string LabelKey,
    IReadOnlyList<RulesetExplainParameter> LabelParameters,
    RulesetCapabilityValue? Value,
    ExplainTraceDto ExplainTrace,
    IReadOnlyList<ExplainEvidencePointerDto> SourceAnchors,
    IReadOnlyList<ExplainEvidencePointerDto> Evidence,
    ValidationSummary? Validation,
    RuntimeLockDiffProjection? Delta,
    string? LegalityState,
    string SummaryKey,
    IReadOnlyList<RulesetExplainParameter> SummaryParameters,
    int DisplayOrder,
    bool Deterministic = true);

public sealed record ExplainValuePacketInput(
    string PacketId,
    string CalculationKey,
    string RulesetId,
    string RuntimeFingerprint,
    string SubjectId,
    string SubjectKind,
    RulesetCapabilityValue? Value,
    ExplainTraceDto ExplainTrace,
    ValidationSummary? Validation = null,
    RuntimeLockDiffProjection? Delta = null,
    IReadOnlyList<ExplainCounterfactualInput>? Counterfactuals = null,
    string PrivacyMode = CalculationReportPrivacyModes.SupportCase,
    string SummaryKey = "explain.packet.summary",
    IReadOnlyList<RulesetExplainParameter>? SummaryParameters = null);

public sealed record ExplainValuePacket(
    string PacketId,
    string CalculationKey,
    string RulesetId,
    string RuntimeFingerprint,
    string SubjectId,
    string SubjectKind,
    RulesetCapabilityValue? Value,
    ExplainTraceDto ExplainTrace,
    IReadOnlyList<ExplainEvidencePointerDto> SourceAnchors,
    IReadOnlyList<ExplainEvidencePointerDto> Evidence,
    ValidationSummary? Validation,
    RuntimeLockDiffProjection? Delta,
    IReadOnlyList<ExplainCounterfactualPacket> Counterfactuals,
    IReadOnlyList<ExplainValuePacketCoverageRow> CoverageRegistry,
    string PrivacyMode,
    string SummaryKey,
    IReadOnlyList<RulesetExplainParameter> SummaryParameters,
    int CounterfactualLimit,
    int CounterfactualOverflowCount);
