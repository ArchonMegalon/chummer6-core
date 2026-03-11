using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Content;

public static class RuleProfileApplyTargetKinds
{
    public const string GlobalDefaults = "global-defaults";
    public const string Campaign = "campaign";
    public const string Workspace = "workspace";
    public const string Character = "character";
    public const string SessionLedger = "session-ledger";
}

public static class RuleProfileApplyOutcomes
{
    public const string Applied = "applied";
    public const string Deferred = "deferred";
    public const string Blocked = "blocked";
}

public static class RuleProfilePreviewChangeKinds
{
    public const string RuntimeLockPinned = "runtime-lock-pinned";
    public const string RulePackSelectionChanged = "rulepack-selection-changed";
    public const string RulesetChanged = "ruleset-changed";
    public const string SessionReplayRequired = "session-replay-required";
}

public sealed record RuleProfileApplyTarget(
    string TargetKind,
    string TargetId,
    string PinMode = RuntimeLockPinModes.Required);

public sealed record RuleProfilePreviewItem(
    string Kind,
    string Summary,
    string? SubjectId = null,
    bool RequiresConfirmation = false,
    string? SummaryKey = null,
    IReadOnlyList<RulesetExplainParameter>? SummaryParameters = null);

public sealed record RuleProfilePreviewReceipt(
    string ProfileId,
    RuleProfileApplyTarget Target,
    ResolvedRuntimeLock RuntimeLock,
    IReadOnlyList<RuleProfilePreviewItem> Changes,
    IReadOnlyList<RuntimeInspectorWarning> Warnings,
    bool RequiresConfirmation = false);

public sealed record RuleProfileApplyReceipt(
    string ProfileId,
    RuleProfileApplyTarget Target,
    string Outcome,
    RuleProfilePreviewReceipt Preview,
    RuntimeLockInstallReceipt? InstallReceipt = null,
    string? DeferredReason = null);

public static class RuleProfileContractLocalization
{
    public static string ResolvePreviewSummaryKey(RuleProfilePreviewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return string.IsNullOrWhiteSpace(item.SummaryKey)
            ? item.Summary
            : item.SummaryKey;
    }

    public static IReadOnlyList<RulesetExplainParameter> ResolvePreviewSummaryParameters(RuleProfilePreviewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.SummaryParameters ?? [];
    }
}
