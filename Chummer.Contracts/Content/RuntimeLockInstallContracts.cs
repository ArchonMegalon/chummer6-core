namespace Chummer.Contracts.Content;

public static class RuntimeLockTargetKinds
{
    public const string CharacterVersion = "character-version";
    public const string Workspace = "workspace";
    public const string SessionLedger = "session-ledger";
    public const string RulePackSelection = "rulepack-selection";
    public const string BuildKitSelection = "buildkit-selection";
}

public static class RuntimeLockPinModes
{
    public const string Required = "required";
    public const string Preferred = "preferred";
    public const string Inherited = "inherited";
}

public static class RuntimeLockInstallOutcomes
{
    public const string Installed = "installed";
    public const string Updated = "updated";
    public const string Rebound = "rebound";
    public const string Unchanged = "unchanged";
    public const string Blocked = "blocked";
}

public static class RuntimeLockRebindReasons
{
    public const string ContentBundleChanged = "content-bundle-changed";
    public const string RulePackSelectionChanged = "rulepack-selection-changed";
    public const string EngineApiChanged = "engine-api-changed";
    public const string RulesetChanged = "ruleset-changed";
}

public sealed record RuntimeLockReference(
    string RuntimeFingerprint,
    string RulesetId,
    string EngineApiVersion);

public sealed record RuntimeLockPin(
    string TargetKind,
    string TargetId,
    string PinMode,
    RuntimeLockReference RuntimeLock,
    IReadOnlyList<ArtifactVersionReference> RulePacks);

public sealed record RuntimeLockRebindNotice(
    string Reason,
    string PriorRuntimeFingerprint,
    string CurrentRuntimeFingerprint,
    bool SessionSafe = false);

public sealed record RuntimeLockInstallReceipt(
    string TargetKind,
    string TargetId,
    string Outcome,
    ResolvedRuntimeLock RuntimeLock,
    DateTimeOffset InstalledAtUtc,
    IReadOnlyList<RuntimeLockRebindNotice> RebindNotices,
    bool RequiresSessionReplay = false);
