namespace Chummer.Contracts;

public static class ExplainEnvelopeSchemas
{
    public const string ProvenanceV1 = "explain.provenance.v1";
    public const string EvidenceV1 = "explain.evidence.v1";
}

public record ExplainProvenanceEnvelopeDto(
    string Schema,
    ExplainProvenanceDto Provenance,
    string? CapabilityId = null,
    string? ProviderId = null,
    string? PackId = null);

public record ExplainEvidenceEnvelopeDto(
    string Schema,
    IReadOnlyList<ExplainEvidencePointerDto> Pointers,
    string? CapabilityId = null,
    string? ProviderId = null,
    string? PackId = null,
    bool Deterministic = true);
