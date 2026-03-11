using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts;

public record TraceStepDto(
    string ProviderId,
    string CapabilityId,
    string? PackId,
    string ExplanationKey,
    IReadOnlyList<RulesetExplainParameter> ExplanationParameters,
    string Category,
    decimal? Modifier = null,
    bool? Certain = null,
    string? RuleId = null,
    IReadOnlyList<ExplainEvidencePointerDto>? Evidence = null
);
