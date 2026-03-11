using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Session;

public static class SessionCompactionModes
{
    public const string IncrementalSnapshot = "incremental-snapshot";
    public const string FullRebuild = "full-rebuild";
}

public static class SessionRuntimeBundleRefreshOutcomes
{
    public const string Unchanged = "unchanged";
    public const string Refreshed = "refreshed";
    public const string Rebound = "rebound";
    public const string Blocked = "blocked";
}

public sealed record SessionSnapshotBaseline(
    string SnapshotId,
    string OverlayId,
    CharacterVersionReference BaseCharacterVersion,
    long ThroughSequence,
    DateTimeOffset CreatedAtUtc,
    int CompactedEventCount);

public sealed record SessionCompactionReceipt(
    string OverlayId,
    string Mode,
    SessionSnapshotBaseline Baseline,
    long NextSequence,
    int RetainedPendingEventCount,
    bool PendingEventsRetained = true);

public sealed record SessionRuntimeBundleRefreshReceipt(
    string PreviousBundleId,
    string CurrentBundleId,
    string Outcome,
    CharacterVersionReference BaseCharacterVersion,
    string RuntimeFingerprint,
    DateTimeOffset RefreshedAtUtc,
    bool SignatureChanged = false,
    string? DeferredReason = null);
