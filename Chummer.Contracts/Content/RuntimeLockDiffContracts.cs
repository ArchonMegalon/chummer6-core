using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Content;

public static class RuntimeLockDiffChangeKinds
{
    public const string RulesetChanged = "ruleset-changed";
    public const string EngineApiChanged = "engine-api-changed";
    public const string ContentBundleAdded = "content-bundle-added";
    public const string ContentBundleRemoved = "content-bundle-removed";
    public const string RulePackAdded = "rulepack-added";
    public const string RulePackRemoved = "rulepack-removed";
    public const string ProviderBindingChanged = "provider-binding-changed";
}

public sealed record RuntimeLockDiffChange(
    string Kind,
    string SubjectId,
    string? BeforeValue,
    string? AfterValue,
    string ReasonKey,
    IReadOnlyList<RulesetExplainParameter> ReasonParameters);

public sealed record RuntimeLockDiffProjection(
    string BeforeFingerprint,
    string AfterFingerprint,
    IReadOnlyList<RuntimeLockDiffChange> Changes);
