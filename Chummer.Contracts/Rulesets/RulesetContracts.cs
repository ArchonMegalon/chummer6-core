namespace Chummer.Contracts.Rulesets;

public static class RulesetDefaults
{
    public const string Sr4 = "sr4";
    public const string Sr5 = "sr5";
    public const string Sr6 = "sr6";

    public static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim().ToLowerInvariant();
        if (HasCanonicalVariantPrefix(normalized, Sr4))
        {
            return Sr4;
        }

        if (HasCanonicalVariantPrefix(normalized, Sr5))
        {
            return Sr5;
        }

        if (HasCanonicalVariantPrefix(normalized, Sr6))
        {
            return Sr6;
        }

        return normalized switch
        {
            "sr4" or "sr 4" or "shadowrun 4" or "shadowrun4" or "shadowrun fourth edition" => Sr4,
            "sr5" or "sr 5" or "shadowrun 5" or "shadowrun5" or "shadowrun fifth edition" => Sr5,
            "sr6" or "sr 6" or "shadowrun 6" or "shadowrun6" or "shadowrun sixth edition" => Sr6,
            _ => normalized
        };
    }

    public static string NormalizeRequired(string value)
    {
        string? normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            throw new ArgumentException("Ruleset id is required.", nameof(value));
        }

        return normalized;
    }

    private static bool HasCanonicalVariantPrefix(string value, string canonical)
        => value.StartsWith(canonical + ".", StringComparison.Ordinal)
            || value.StartsWith(canonical + "-", StringComparison.Ordinal)
            || value.StartsWith(canonical + "_", StringComparison.Ordinal)
            || value.StartsWith(canonical + ":", StringComparison.Ordinal)
            || value.StartsWith(canonical + "/", StringComparison.Ordinal);
}

public readonly record struct RulesetId(string Value)
{
    public static RulesetId Default => new(string.Empty);

    public string NormalizedValue => RulesetDefaults.NormalizeOptional(Value) ?? string.Empty;

    public override string ToString() => NormalizedValue;
}

public sealed record WorkspacePayloadEnvelope(
    string RulesetId,
    int SchemaVersion,
    string PayloadKind,
    string Payload);

public interface IRulesetSerializer
{
    RulesetId RulesetId { get; }

    int SchemaVersion { get; }

    WorkspacePayloadEnvelope Wrap(string payloadKind, string payload);
}

public sealed record RulesetRuleEvaluationRequest(
    string RuleId,
    IReadOnlyDictionary<string, object?> Inputs,
    RulesetExecutionOptions? Options = null);

public sealed record RulesetRuleEvaluationResult(
    bool Success,
    IReadOnlyDictionary<string, object?> Outputs,
    IReadOnlyList<string> Messages,
    RulesetExplainTrace? Explain = null);

public interface IRulesetRuleHost
{
    ValueTask<RulesetRuleEvaluationResult> EvaluateAsync(RulesetRuleEvaluationRequest request, CancellationToken ct);
}

public sealed record RulesetScriptExecutionRequest(
    string ScriptId,
    string ScriptSource,
    IReadOnlyDictionary<string, object?> Inputs,
    RulesetExecutionOptions? Options = null);

public sealed record RulesetScriptExecutionResult(
    bool Success,
    string? Error,
    IReadOnlyDictionary<string, object?> Outputs,
    RulesetExplainTrace? Explain = null);

public interface IRulesetScriptHost
{
    ValueTask<RulesetScriptExecutionResult> ExecuteAsync(RulesetScriptExecutionRequest request, CancellationToken ct);
}
