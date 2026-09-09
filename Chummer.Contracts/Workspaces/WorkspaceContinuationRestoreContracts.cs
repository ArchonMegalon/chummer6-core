using System.Text.Json.Serialization;

namespace Chummer.Contracts.Workspaces;

/// <summary>One local target observation, not portable state or a write grant.</summary>
public sealed record WorkspaceContinuationRestoreTarget(
    string OwnerId, CharacterWorkspaceId WorkspaceId, bool Exists,
    string? IncarnationId, long ContentRevision, long SavedRevision, string? SnapshotDigest,
    string? SlotGeneration = null);

/// <summary>Proof of this store's restore transaction, never accepted from a snapshot.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkspaceContinuationRestoreReceipt(
    [property: JsonRequired] Guid OperationId,
    [property: JsonRequired] string AdmissionDigest,
    [property: JsonRequired] string SnapshotDigest,
    [property: JsonRequired] string SourceDigest,
    [property: JsonRequired] string IncarnationId,
    [property: JsonRequired] long ContentRevision,
    [property: JsonRequired] long SavedRevision,
    [property: JsonRequired] DateTimeOffset RestoredAtUtc);

public enum WorkspaceContinuationRestoreOutcome
{
    Available, Applied, Recovered, AlreadyCurrent, Conflict, Rejected, Unavailable,
    ReviewConsumed, ReviewExpired, RecoveryProofMissing, Canceled
}

public sealed record WorkspaceContinuationRestoreResult(
    WorkspaceContinuationRestoreOutcome Outcome,
    WorkspaceContinuationRestoreReceipt? Receipt = null,
    WorkspaceContinuationRestoreTarget? Target = null,
    IReadOnlyList<string>? Blockers = null,
    bool ReopenRequired = false);
