namespace Chummer.Contracts;

public record ExplainProvenanceDto(
    string RuntimeFingerprint,
    string RulesetId,
    string EngineApiVersion,
    string CatalogKind,
    string RuntimeTitle,
    string? ProfileId = null,
    string? ProfileTitle = null,
    string? ProviderId = null,
    string? PackId = null,
    IReadOnlyList<string>? RulePacks = null,
    IReadOnlyDictionary<string, string>? ProviderBindings = null);
