using System.Collections;
using System.Globalization;

namespace Chummer.Contracts.Rulesets;

public static class RulesetCapabilityInvocationKinds
{
    public const string Rule = "rule";
    public const string Script = "script";
}

public static class RulesetCapabilityValueKinds
{
    public const string Null = "null";
    public const string String = "string";
    public const string Boolean = "boolean";
    public const string Integer = "integer";
    public const string Number = "number";
    public const string Decimal = "decimal";
    public const string List = "list";
    public const string Object = "object";
}

public static class RulesetCapabilityDiagnosticSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
}

public sealed record RulesetCapabilityArgument(
    string Name,
    RulesetCapabilityValue Value);

public sealed record RulesetCapabilityValue(
    string Kind,
    string? StringValue = null,
    bool? BooleanValue = null,
    long? IntegerValue = null,
    double? NumberValue = null,
    decimal? DecimalValue = null,
    IReadOnlyList<RulesetCapabilityValue>? Items = null,
    IReadOnlyDictionary<string, RulesetCapabilityValue>? Properties = null);

public sealed record RulesetCapabilityDiagnostic(
    string Code,
    string Message,
    string Severity = RulesetCapabilityDiagnosticSeverities.Info,
    string? MessageKey = null,
    IReadOnlyList<RulesetExplainParameter>? MessageParameters = null);

public sealed record RulesetCapabilityInvocationRequest(
    string CapabilityId,
    string InvocationKind,
    IReadOnlyList<RulesetCapabilityArgument> Arguments,
    RulesetExecutionOptions? Options = null,
    string? ProviderId = null,
    string? Source = null);

public sealed record RulesetCapabilityInvocationResult(
    bool Success,
    RulesetCapabilityValue? Output,
    IReadOnlyList<RulesetCapabilityDiagnostic> Diagnostics,
    RulesetExplainTrace? Explain = null);

public sealed record RulesetCapabilityDescriptor(
    string CapabilityId,
    string InvocationKind,
    string Title,
    bool Explainable,
    bool SessionSafe,
    RulesetGasBudget DefaultGasBudget,
    RulesetGasBudget? MaximumGasBudget = null,
    string? TitleKey = null,
    IReadOnlyList<RulesetExplainParameter>? TitleParameters = null);

public static class RulesetCapabilityDescriptorLocalization
{
    public static string ResolveTitleKey(RulesetCapabilityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return string.IsNullOrWhiteSpace(descriptor.TitleKey)
            ? $"ruleset.capability.{descriptor.CapabilityId}.title"
            : descriptor.TitleKey;
    }

    public static IReadOnlyList<RulesetExplainParameter> ResolveTitleParameters(RulesetCapabilityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.TitleParameters ?? [];
    }
}

public interface IRulesetCapabilityDescriptorProvider
{
    IReadOnlyList<RulesetCapabilityDescriptor> GetCapabilityDescriptors();
}

public interface IRulesetCapabilityHost
{
    ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(RulesetCapabilityInvocationRequest request, CancellationToken ct);
}

public static class RulesetCapabilityDiagnosticLocalization
{
    public static string ResolveMessageKey(RulesetCapabilityDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        return string.IsNullOrWhiteSpace(diagnostic.MessageKey)
            ? diagnostic.Message
            : diagnostic.MessageKey;
    }

    public static IReadOnlyList<RulesetExplainParameter> ResolveMessageParameters(RulesetCapabilityDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return diagnostic.MessageParameters ?? [];
    }
}

public sealed class RulesetRuleHostCapabilityAdapter : IRulesetRuleHost
{
    private readonly IRulesetCapabilityHost _capabilityHost;

    public RulesetRuleHostCapabilityAdapter(IRulesetCapabilityHost capabilityHost)
    {
        _capabilityHost = capabilityHost ?? throw new ArgumentNullException(nameof(capabilityHost));
    }

    public async ValueTask<RulesetRuleEvaluationResult> EvaluateAsync(RulesetRuleEvaluationRequest request, CancellationToken ct)
    {
        RulesetCapabilityInvocationResult result = await _capabilityHost
            .InvokeAsync(RulesetCapabilityBridge.FromRuleRequest(request), ct)
            .ConfigureAwait(false);

        return RulesetCapabilityBridge.ToRuleResult(result);
    }
}

public sealed class RulesetScriptHostCapabilityAdapter : IRulesetScriptHost
{
    private readonly IRulesetCapabilityHost _capabilityHost;

    public RulesetScriptHostCapabilityAdapter(IRulesetCapabilityHost capabilityHost)
    {
        _capabilityHost = capabilityHost ?? throw new ArgumentNullException(nameof(capabilityHost));
    }

    public async ValueTask<RulesetScriptExecutionResult> ExecuteAsync(RulesetScriptExecutionRequest request, CancellationToken ct)
    {
        RulesetCapabilityInvocationResult result = await _capabilityHost
            .InvokeAsync(RulesetCapabilityBridge.FromScriptRequest(request), ct)
            .ConfigureAwait(false);

        return RulesetCapabilityBridge.ToScriptResult(result);
    }
}

public static class RulesetCapabilityBridge
{
    public static RulesetCapabilityInvocationRequest FromRuleRequest(RulesetRuleEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new RulesetCapabilityInvocationRequest(
            CapabilityId: request.RuleId,
            InvocationKind: RulesetCapabilityInvocationKinds.Rule,
            Arguments: request.Inputs
                .Select(static pair => new RulesetCapabilityArgument(pair.Key, FromObject(pair.Value)))
                .ToArray(),
            Options: request.Options);
    }

    public static RulesetCapabilityInvocationRequest FromScriptRequest(RulesetScriptExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new RulesetCapabilityInvocationRequest(
            CapabilityId: request.ScriptId,
            InvocationKind: RulesetCapabilityInvocationKinds.Script,
            Arguments: request.Inputs
                .Select(static pair => new RulesetCapabilityArgument(pair.Key, FromObject(pair.Value)))
                .ToArray(),
            Options: request.Options,
            Source: request.ScriptSource);
    }

    public static RulesetRuleEvaluationResult ToRuleResult(RulesetCapabilityInvocationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new RulesetRuleEvaluationResult(
            Success: result.Success,
            Outputs: ToOutputDictionary(result.Output),
            Messages: result.Diagnostics.Select(static diagnostic => diagnostic.Message).ToArray(),
            Explain: result.Explain);
    }

    public static RulesetScriptExecutionResult ToScriptResult(RulesetCapabilityInvocationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string? error = result.Success
            ? null
            : result.Diagnostics.FirstOrDefault(static diagnostic => string.Equals(diagnostic.Severity, RulesetCapabilityDiagnosticSeverities.Error, StringComparison.Ordinal))?.Message
              ?? result.Diagnostics.FirstOrDefault()?.Message
              ?? "Capability invocation failed.";

        return new RulesetScriptExecutionResult(
            Success: result.Success,
            Error: error,
            Outputs: ToOutputDictionary(result.Output),
            Explain: result.Explain);
    }

    public static RulesetCapabilityValue FromObject(object? value)
    {
        if (value is null)
        {
            return new RulesetCapabilityValue(RulesetCapabilityValueKinds.Null);
        }

        return value switch
        {
            RulesetCapabilityValue capabilityValue => capabilityValue,
            string stringValue => new RulesetCapabilityValue(RulesetCapabilityValueKinds.String, StringValue: stringValue),
            bool booleanValue => new RulesetCapabilityValue(RulesetCapabilityValueKinds.Boolean, BooleanValue: booleanValue),
            byte or sbyte or short or ushort or int or uint or long =>
                new RulesetCapabilityValue(RulesetCapabilityValueKinds.Integer, IntegerValue: Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            float or double =>
                new RulesetCapabilityValue(RulesetCapabilityValueKinds.Number, NumberValue: Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            decimal decimalValue => new RulesetCapabilityValue(RulesetCapabilityValueKinds.Decimal, DecimalValue: decimalValue),
            IEnumerable<KeyValuePair<string, object?>> pairs => new RulesetCapabilityValue(
                RulesetCapabilityValueKinds.Object,
                Properties: pairs.ToDictionary(
                    static pair => pair.Key,
                    static pair => FromObject(pair.Value),
                    StringComparer.Ordinal)),
            IDictionary dictionary => new RulesetCapabilityValue(
                RulesetCapabilityValueKinds.Object,
                Properties: dictionary.Keys
                    .Cast<object>()
                    .ToDictionary(
                        static key => Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty,
                        key => FromObject(dictionary[key]!),
                        StringComparer.Ordinal)),
            IEnumerable enumerable when value is not string => new RulesetCapabilityValue(
                RulesetCapabilityValueKinds.List,
                Items: enumerable.Cast<object?>().Select(FromObject).ToArray()),
            _ => new RulesetCapabilityValue(RulesetCapabilityValueKinds.String, StringValue: Convert.ToString(value, CultureInfo.InvariantCulture))
        };
    }

    public static object? ToObject(RulesetCapabilityValue? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Kind switch
        {
            RulesetCapabilityValueKinds.Null => null,
            RulesetCapabilityValueKinds.String => value.StringValue,
            RulesetCapabilityValueKinds.Boolean => value.BooleanValue,
            RulesetCapabilityValueKinds.Integer => value.IntegerValue,
            RulesetCapabilityValueKinds.Number => value.NumberValue,
            RulesetCapabilityValueKinds.Decimal => value.DecimalValue,
            RulesetCapabilityValueKinds.List => value.Items?.Select(ToObject).ToArray() ?? Array.Empty<object?>(),
            RulesetCapabilityValueKinds.Object => value.Properties?.ToDictionary(
                static pair => pair.Key,
                static pair => ToObject(pair.Value),
                StringComparer.Ordinal),
            _ => value.StringValue
        };
    }

    public static IReadOnlyDictionary<string, object?> ToOutputDictionary(RulesetCapabilityValue? output)
    {
        if (output is null)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        if (string.Equals(output.Kind, RulesetCapabilityValueKinds.Object, StringComparison.Ordinal) && output.Properties is not null)
        {
            return output.Properties.ToDictionary(
                static pair => pair.Key,
                static pair => ToObject(pair.Value),
                StringComparer.Ordinal);
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["value"] = ToObject(output)
        };
    }
}
