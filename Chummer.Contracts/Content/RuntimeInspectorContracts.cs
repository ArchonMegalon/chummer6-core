using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Content;

public static class RuntimeInspectorTargetKinds
{
    public const string CharacterVersion = "character-version";
    public const string Workspace = "workspace";
    public const string SessionLedger = "session-ledger";
    public const string RuntimeLock = "runtime-lock";
}

public static class RuntimeInspectorWarningKinds
{
    public const string Compatibility = "compatibility";
    public const string Migration = "migration";
    public const string Trust = "trust";
    public const string ProviderBinding = "provider-binding";
    public const string SessionReplay = "session-replay";
}

public static class RuntimeInspectorWarningSeverityLevels
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
}

public static class RuntimeMigrationPreviewChangeKinds
{
    public const string RulePackAdded = "rulepack-added";
    public const string RulePackRemoved = "rulepack-removed";
    public const string ProviderRebound = "provider-rebound";
    public const string ContentBundleUpdated = "content-bundle-updated";
    public const string EngineApiChanged = "engine-api-changed";
    public const string RulesetChanged = "ruleset-changed";
}

public sealed record RuntimeInspectorRulePackEntry(
    ArtifactVersionReference RulePack,
    string Title,
    string Visibility,
    string TrustTier,
    IReadOnlyList<string> CapabilityIds,
    bool Enabled = true,
    string SourceKind = RegistryEntrySourceKinds.PersistedManifest);

public sealed record RuntimeInspectorProviderBinding(
    string CapabilityId,
    string ProviderId,
    string? PackId = null,
    string? SourceAssetPath = null,
    bool SessionSafe = false);

public sealed record RuntimeInspectorCapabilityDescriptorProjection(
    string CapabilityId,
    string InvocationKind,
    string Title,
    bool Explainable,
    bool SessionSafe,
    RulesetGasBudget DefaultGasBudget,
    RulesetGasBudget? MaximumGasBudget = null,
    string? ProviderId = null,
    string? PackId = null,
    string? TitleKey = null,
    IReadOnlyList<RulesetExplainParameter>? TitleParameters = null);

public sealed record RuntimeInspectorWarning(
    string Kind,
    string Severity,
    string Message,
    string? SubjectId = null,
    string? ExplainEntryId = null,
    string? MessageKey = null,
    IReadOnlyList<RulesetExplainParameter>? MessageParameters = null);

public sealed record RuntimeMigrationPreviewItem(
    string Kind,
    string Summary,
    string? SubjectId = null,
    string? BeforeValue = null,
    string? AfterValue = null,
    bool RequiresRebind = false,
    string? SummaryKey = null,
    IReadOnlyList<RulesetExplainParameter>? SummaryParameters = null);

public sealed record ActiveRuntimeStatusProjection(
    string ProfileId,
    string Title,
    string RulesetId,
    string RuntimeFingerprint,
    string InstallState,
    string? InstalledTargetKind = null,
    string? InstalledTargetId = null,
    int RulePackCount = 0,
    int ProviderBindingCount = 0,
    int WarningCount = 0);

public sealed record RuntimeInspectorProjection(
    string TargetKind,
    string TargetId,
    ResolvedRuntimeLock RuntimeLock,
    ArtifactInstallState Install,
    IReadOnlyList<RuntimeInspectorRulePackEntry> ResolvedRulePacks,
    IReadOnlyList<RuntimeInspectorProviderBinding> ProviderBindings,
    IReadOnlyList<RuntimeLockCompatibilityDiagnostic> CompatibilityDiagnostics,
    IReadOnlyList<RuntimeInspectorWarning> Warnings,
    IReadOnlyList<RuntimeMigrationPreviewItem> MigrationPreview,
    DateTimeOffset GeneratedAtUtc,
    string ProfileSourceKind = RegistryEntrySourceKinds.PersistedManifest,
    IReadOnlyList<RuntimeInspectorCapabilityDescriptorProjection>? CapabilityDescriptors = null);

public static class RuntimeInspectorContractLocalization
{
    public static string ResolveMessageKey(RuntimeInspectorWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        return string.IsNullOrWhiteSpace(warning.MessageKey)
            ? warning.Message
            : warning.MessageKey;
    }

    public static IReadOnlyList<RulesetExplainParameter> ResolveMessageParameters(RuntimeInspectorWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        return warning.MessageParameters ?? [];
    }

    public static string ResolveSummaryKey(RuntimeMigrationPreviewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return string.IsNullOrWhiteSpace(item.SummaryKey)
            ? item.Summary
            : item.SummaryKey;
    }

    public static IReadOnlyList<RulesetExplainParameter> ResolveSummaryParameters(RuntimeMigrationPreviewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.SummaryParameters ?? [];
    }
}
