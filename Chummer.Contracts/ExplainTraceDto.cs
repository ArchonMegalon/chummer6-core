using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts;

public record ExplainTraceDto(
    string TargetKey,
    RulesetCapabilityValue? FinalValue,
    string SummaryKey,
    IReadOnlyList<RulesetExplainParameter> SummaryParameters,
    IReadOnlyList<TraceStepDto> Steps,
    ExplainProvenanceDto? Provenance = null,
    IReadOnlyList<ExplainEvidencePointerDto>? Evidence = null,
    ExplainProvenanceEnvelopeDto? ProvenanceEnvelope = null,
    ExplainEvidenceEnvelopeDto? EvidenceEnvelope = null
);
