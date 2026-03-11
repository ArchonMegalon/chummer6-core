using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Content;

public static class RulePackResolutionDiagnosticKinds
{
    public const string MissingDependency = "missing-dependency";
    public const string VersionConflict = "version-conflict";
    public const string PackConflict = "pack-conflict";
    public const string TrustTierViolation = "trust-tier-violation";
    public const string CapabilityBlocked = "capability-blocked";
    public const string SignatureRequired = "signature-required";
    public const string ReviewRequired = "review-required";
}

public static class RulePackResolutionSeverityLevels
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
}

public static class RulePackCompileStatuses
{
    public const string Compiled = "compiled";
    public const string CompiledWithReview = "compiled-with-review";
    public const string Blocked = "blocked";
    public const string Failed = "failed";
}

public sealed record RulePackCompilerRequest(
    string RulesetId,
    IReadOnlyList<ContentBundleDescriptor> ContentBundles,
    IReadOnlyList<ArtifactVersionReference> SelectedRulePacks,
    string EngineApiVersion,
    string Environment,
    string MinimumTrustTier);

public sealed record RulePackResolutionDiagnostic(
    string Kind,
    string Severity,
    string SubjectId,
    string Message,
    string? RelatedPackId = null,
    string? RelatedDependencyId = null,
    string? MessageKey = null,
    IReadOnlyList<RulesetExplainParameter>? MessageParameters = null);

public sealed record RulePackResolutionResult(
    string RulesetId,
    IReadOnlyList<ArtifactVersionReference> RequestedRulePacks,
    IReadOnlyList<ArtifactVersionReference> ResolvedRulePacks,
    IReadOnlyList<RulePackResolutionDiagnostic> Diagnostics,
    bool RequiresReview);

public sealed record RulePackCompileReceipt(
    string Status,
    RulePackCompilerRequest Request,
    RulePackResolutionResult Resolution,
    ResolvedRuntimeLock? RuntimeLock,
    DateTimeOffset CompiledAtUtc);

public static class RulePackResolutionDiagnosticLocalization
{
    public static string ResolveMessageKey(RulePackResolutionDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        return string.IsNullOrWhiteSpace(diagnostic.MessageKey)
            ? diagnostic.Message
            : diagnostic.MessageKey;
    }

    public static IReadOnlyList<RulesetExplainParameter> ResolveMessageParameters(RulePackResolutionDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return diagnostic.MessageParameters ?? [];
    }
}
