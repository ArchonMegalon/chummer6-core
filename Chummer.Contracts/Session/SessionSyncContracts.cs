using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Session;

public static class SessionConflictKinds
{
    public const string RuntimeFingerprintMismatch = "runtime-fingerprint-mismatch";
    public const string BaseVersionMismatch = "base-version-mismatch";
    public const string EventReplayRejected = "event-replay-rejected";
    public const string ManualRebindRequired = "manual-rebind-required";
}

public sealed record SessionPendingEventState(
    string EventId,
    long Sequence,
    string Status,
    DateTimeOffset CreatedAtUtc,
    string? Message = null);

public sealed record SessionSyncBatch(
    string OverlayId,
    CharacterVersionReference BaseCharacterVersion,
    IReadOnlyList<SessionEventEnvelope> Events,
    string? ClientCursor = null);

public sealed record SessionReplayReceipt(
    CharacterVersionReference AppliedCharacterVersion,
    int AcceptedEventCount,
    int ReplayedEventCount,
    bool RuntimeRebindRequired = false,
    bool ManualResolutionRequired = false);

public sealed record SessionConflictDiagnostic(
    string EventId,
    string Kind,
    string Message,
    bool RequiresManualResolution,
    string? ConflictingEventId = null);

public sealed record SessionSyncReceipt(
    string OverlayId,
    SessionReplayReceipt Replay,
    IReadOnlyList<SessionPendingEventState> PendingEvents,
    IReadOnlyList<SessionConflictDiagnostic> Conflicts,
    string? ServerCursor = null);
