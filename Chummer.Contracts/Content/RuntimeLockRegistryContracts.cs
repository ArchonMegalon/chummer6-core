using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Content;

public static class RuntimeLockCatalogKinds
{
    public const string Saved = "saved";
    public const string Published = "published";
    public const string Derived = "derived";
}

public static class RuntimeLockCompatibilityStates
{
    public const string Compatible = "compatible";
    public const string RebindRequired = "rebind-required";
    public const string MissingPack = "missing-pack";
    public const string RulesetMismatch = "ruleset-mismatch";
    public const string EngineApiMismatch = "engine-api-mismatch";
}

public sealed record RuntimeLockRegistryEntry(
    string LockId,
    OwnerScope Owner,
    string Title,
    string Visibility,
    string CatalogKind,
    ResolvedRuntimeLock RuntimeLock,
    DateTimeOffset UpdatedAtUtc,
    string? Description = null,
    ArtifactInstallState Install = null!);

public sealed record RuntimeLockSaveRequest(
    string Title,
    ResolvedRuntimeLock RuntimeLock,
    string Visibility = ArtifactVisibilityModes.LocalOnly,
    string? Description = null,
    ArtifactInstallState? Install = null);

public sealed record RuntimeLockCompatibilityDiagnostic(
    string State,
    string Message,
    string? RequiredRulesetId = null,
    string? RequiredRuntimeFingerprint = null,
    string? MessageKey = null,
    IReadOnlyList<RulesetExplainParameter>? MessageParameters = null);

public sealed record RuntimeLockInstallCandidate(
    string TargetKind,
    string TargetId,
    RuntimeLockRegistryEntry Entry,
    IReadOnlyList<RuntimeLockCompatibilityDiagnostic> Diagnostics,
    bool CanInstall = true);

public sealed record RuntimeLockRegistryPage(
    IReadOnlyList<RuntimeLockRegistryEntry> Entries,
    int TotalCount,
    string? ContinuationToken = null);

public sealed record RuntimeLockInstallHistoryRecord(
    string LockId,
    string RulesetId,
    ArtifactInstallHistoryEntry Entry);

public static class RuntimeLockInstallPreviewChangeKinds
{
    public const string RuntimeLockPinned = "runtime-lock-pinned";
    public const string SessionReplayRequired = "session-replay-required";
}

public sealed record RuntimeLockInstallPreviewItem(
    string Kind,
    string Summary,
    string SubjectId,
    bool RequiresConfirmation = false,
    string? SummaryKey = null,
    IReadOnlyList<RulesetExplainParameter>? SummaryParameters = null);

public sealed record RuntimeLockInstallPreviewReceipt(
    string LockId,
    RuleProfileApplyTarget Target,
    ResolvedRuntimeLock RuntimeLock,
    IReadOnlyList<RuntimeLockInstallPreviewItem> Changes,
    IReadOnlyList<RuntimeInspectorWarning> Warnings,
    bool RequiresConfirmation = false);

public static class RuntimeLockContractLocalization
{
    public static string ResolveCompatibilityMessageKey(RuntimeLockCompatibilityDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        return string.IsNullOrWhiteSpace(diagnostic.MessageKey)
            ? diagnostic.Message
            : diagnostic.MessageKey;
    }

    public static IReadOnlyList<RulesetExplainParameter> ResolveCompatibilityMessageParameters(RuntimeLockCompatibilityDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return diagnostic.MessageParameters ?? [];
    }

    public static string ResolveInstallPreviewSummaryKey(RuntimeLockInstallPreviewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return string.IsNullOrWhiteSpace(item.SummaryKey)
            ? item.Summary
            : item.SummaryKey;
    }

    public static IReadOnlyList<RulesetExplainParameter> ResolveInstallPreviewSummaryParameters(RuntimeLockInstallPreviewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.SummaryParameters ?? [];
    }
}
