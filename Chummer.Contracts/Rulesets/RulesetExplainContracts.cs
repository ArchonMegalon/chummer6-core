namespace Chummer.Contracts.Rulesets;

public static class RulesetEvidencePointerKinds
{
    public const string RuntimeLock = "runtime-lock";
    public const string RuleProfile = "rule-profile";
    public const string RulePack = "rulepack";
    public const string ProviderBinding = "provider-binding";
    public const string CapabilityDescriptor = "capability-descriptor";
    public const string RuleReference = "rule-reference";
    public const string Diagnostic = "diagnostic";
}

public sealed record RulesetGasBudget(
    int ProviderInstructionLimit,
    int RequestInstructionLimit,
    long MemoryBytesLimit,
    TimeSpan? WallClockLimit = null);

public sealed record RulesetExecutionOptions(
    bool Explain = false,
    RulesetGasBudget? GasBudget = null);

public sealed record RulesetGasUsage(
    int ProviderInstructionsConsumed,
    int RequestInstructionsConsumed,
    long PeakMemoryBytes,
    bool ProviderBudgetExceeded = false,
    bool RequestBudgetExceeded = false,
    bool WallClockLimitExceeded = false);

public sealed record RulesetExplainParameter(
    string Name,
    RulesetCapabilityValue Value);

public sealed record RulesetEvidencePointer(
    string Kind,
    string Pointer,
    string? LabelKey = null,
    IReadOnlyList<RulesetExplainParameter>? LabelParameters = null,
    string? ProviderId = null,
    string? PackId = null,
    string? RuleId = null);

public sealed record RulesetTraceStep(
    string ProviderId,
    string CapabilityId,
    string? PackId,
    string ExplanationKey,
    IReadOnlyList<RulesetExplainParameter> ExplanationParameters,
    string Category,
    decimal? Modifier = null,
    bool? Certain = null,
    string? RuleId = null,
    IReadOnlyList<RulesetEvidencePointer>? Evidence = null);

public sealed record RulesetProviderTrace(
    string ProviderId,
    string CapabilityId,
    string? PackId,
    bool Success,
    IReadOnlyList<RulesetTraceStep> Steps,
    RulesetGasUsage GasUsage,
    IReadOnlyList<RulesetEvidencePointer>? Evidence = null);

public sealed record RulesetExplainTrace(
    string TargetKey,
    RulesetCapabilityValue? FinalValue,
    string SummaryKey,
    IReadOnlyList<RulesetExplainParameter> SummaryParameters,
    IReadOnlyList<RulesetProviderTrace> Providers,
    RulesetGasUsage AggregateGasUsage,
    string? RuntimeFingerprint = null,
    string? ProfileId = null,
    IReadOnlyList<RulesetEvidencePointer>? Evidence = null);
