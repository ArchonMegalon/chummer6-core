using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts;

public record ExplainEvidencePointerDto(
    string Kind,
    string Pointer,
    string? LabelKey = null,
    IReadOnlyList<RulesetExplainParameter>? LabelParameters = null,
    string? ProviderId = null,
    string? PackId = null,
    string? RuleId = null);
