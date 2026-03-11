using Chummer.Contracts.Presentation;

namespace Chummer.Contracts.Rulesets;

public static class RulesetDefaults
{
    public const string Sr4 = "sr4";
    public const string Sr5 = "sr5";
    public const string Sr6 = "sr6";

    public static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();
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

public interface IRulesetPlugin
{
    RulesetId Id { get; }

    string DisplayName { get; }

    IRulesetSerializer Serializer { get; }

    IRulesetShellDefinitionProvider ShellDefinitions { get; }

    IRulesetCatalogProvider Catalogs { get; }

    IRulesetCapabilityDescriptorProvider CapabilityDescriptors { get; }

    IRulesetCapabilityHost Capabilities { get; }

    IRulesetRuleHost Rules { get; }

    IRulesetScriptHost Scripts { get; }
}

public interface IRulesetSerializer
{
    RulesetId RulesetId { get; }

    int SchemaVersion { get; }

    WorkspacePayloadEnvelope Wrap(string payloadKind, string payload);
}

public interface IRulesetShellDefinitionProvider
{
    IReadOnlyList<AppCommandDefinition> GetCommands();

    IReadOnlyList<NavigationTabDefinition> GetNavigationTabs();
}

public interface IRulesetCatalogProvider
{
    IReadOnlyList<WorkflowDefinition> GetWorkflowDefinitions() => System.Array.Empty<WorkflowDefinition>();

    IReadOnlyList<WorkflowSurfaceDefinition> GetWorkflowSurfaces() => System.Array.Empty<WorkflowSurfaceDefinition>();

    IReadOnlyList<WorkspaceSurfaceActionDefinition> GetWorkspaceActions();
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
