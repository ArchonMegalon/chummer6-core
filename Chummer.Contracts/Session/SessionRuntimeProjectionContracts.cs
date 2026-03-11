namespace Chummer.Contracts.Session;

public static class SessionRuntimeSelectionStates
{
    public const string Unselected = "unselected";
    public const string Selected = "selected";
    public const string Blocked = "blocked";
}

public static class SessionRuntimeBundleFreshnessStates
{
    public const string Missing = "missing";
    public const string Current = "current";
    public const string ExpiringSoon = "expiring-soon";
    public const string RefreshRequired = "refresh-required";
}

public sealed record SessionRuntimeStatusProjection(
    string CharacterId,
    string SelectionState,
    string? ProfileId = null,
    string? ProfileTitle = null,
    string? RulesetId = null,
    string? RuntimeFingerprint = null,
    bool SessionReady = false,
    string BundleFreshness = SessionRuntimeBundleFreshnessStates.Missing,
    string? BundleId = null,
    string? BundleDeliveryMode = null,
    string? BundleTrustState = null,
    DateTimeOffset? BundleSignedAtUtc = null,
    DateTimeOffset? BundleExpiresAtUtc = null,
    bool RequiresBundleRefresh = false,
    string? DeferredReason = null);
