namespace Chummer.Contracts.Workspaces;

/// <summary>
/// Pointer projection used by account roaming. The complete character remains in
/// the existing private closure; Payload is deliberately empty on this lane.
/// This is a custody identity, never a source-admission or ownership capability.
/// </summary>
public sealed record WorkspaceRoamingRuleEnvironmentSnapshot(
    string WorkspaceId,
    string RulesetId,
    string Format,
    int SchemaVersion,
    string PayloadKind,
    string Payload,
    DateTimeOffset UpdatedAtUtc,
    string WorkspaceContinuationDigest,
    WorkspaceRuleEnvironmentTransfer RuleEnvironment)
{
    public static WorkspaceRoamingRuleEnvironmentSnapshot From(
        WorkspaceContinuationExport continuation, WorkspaceRuleEnvironmentTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        ArgumentNullException.ThrowIfNull(transfer);
        var workspace = continuation.Snapshot.Workspace;
        var result = new WorkspaceRoamingRuleEnvironmentSnapshot(workspace.Id.Value,
            workspace.Document.RulesetId, workspace.Document.Format.ToString(),
            workspace.Document.SchemaVersion, workspace.Document.PayloadKind, "",
            workspace.LastUpdatedUtc, continuation.SnapshotDigest, transfer);
        if (!result.Matches(continuation.Snapshot.OwnerId, continuation))
            throw new InvalidDataException("The roaming source and character are not the same snapshot.");
        return result;
    }

    public bool IsValid(string authenticatedOwner)
        => authenticatedOwner is { Length: > 0 and <= 256 }
           && WorkspaceId is { Length: > 0 and <= 128 }
           && RuleEnvironment?.Binding is not null
           && RuleEnvironment.IsValid(authenticatedOwner, WorkspaceId, WorkspaceContinuationDigest)
           && !string.IsNullOrWhiteSpace(RulesetId) && RulesetId.Length <= 64
           && Enum.TryParse<WorkspaceDocumentFormat>(Format, true, out var format) && Enum.IsDefined(format)
           && Format == format.ToString()
           && SchemaVersion > 0 && !string.IsNullOrWhiteSpace(PayloadKind) && PayloadKind.Length <= 128
           && Payload == "" && UpdatedAtUtc != default;

    public bool Matches(string authenticatedOwner, WorkspaceContinuationExport continuation)
    {
        if (!IsValid(authenticatedOwner) || continuation?.Snapshot is null
            || continuation.Snapshot.OwnerId != authenticatedOwner
            || continuation.SnapshotDigest != WorkspaceContinuationDigest
            || WorkspaceContinuationSnapshotDigest.Compute(continuation.Snapshot) != WorkspaceContinuationDigest)
            return false;
        var workspace = continuation.Snapshot.Workspace;
        return workspace.Id.Value == WorkspaceId
               && workspace.ContentRevision == RuleEnvironment.Binding.ContentRevision
               && workspace.SavedRevision == RuleEnvironment.Binding.SavedRevision
               && workspace.Document.RulesetId == RulesetId
               && workspace.Document.Format.ToString() == Format
               && workspace.Document.SchemaVersion == SchemaVersion
               && workspace.Document.PayloadKind == PayloadKind
               && workspace.LastUpdatedUtc.EqualsExact(UpdatedAtUtc);
    }

    public override string ToString() => nameof(WorkspaceRoamingRuleEnvironmentSnapshot);
}

/// <summary>Exact remote CAS observation; neither clock order nor a workspace ID is a common base.</summary>
public sealed record WorkspaceRoamingRuleEnvironmentSelection(
    WorkspaceRoamingRuleEnvironmentSnapshot Snapshot, long RemoteRevision, string ServerToken)
{
    public bool IsValid(string authenticatedOwner)
        => Snapshot is not null && Snapshot.IsValid(authenticatedOwner) && RemoteRevision > 0
           && ServerToken is { Length: 64 }
           && ServerToken.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public override string ToString() => nameof(WorkspaceRoamingRuleEnvironmentSelection);
}
