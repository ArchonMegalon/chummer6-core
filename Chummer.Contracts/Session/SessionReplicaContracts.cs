using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Session;

public static class SessionReplicaValueKinds
{
    public const string PnCounter = "pn-counter";
    public const string ObservedRemoveSet = "observed-remove-set";
    public const string Sequence = "sequence";
    public const string LastWriterRegister = "last-writer-register";
    public const string ObservedRemoveMap = "observed-remove-map";
}

public sealed record SessionReplicaClock(
    string ReplicaId,
    long LogicalClock,
    DateTimeOffset UpdatedAtUtc);

public sealed record SessionReplicaValue(
    string SemanticKey,
    string ValueKind,
    string PayloadJson,
    string? ParentSemanticKey = null);

public sealed record SessionReplicaState(
    string OverlayId,
    CharacterVersionReference BaseCharacterVersion,
    string RuntimeFingerprint,
    string ReplicaId,
    IReadOnlyList<SessionReplicaClock> ClockSummary,
    IReadOnlyList<SessionReplicaValue> Values,
    DateTimeOffset UpdatedAtUtc,
    int PendingOperationCount = 0);
