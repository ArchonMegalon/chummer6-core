using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Captures complete continuation data while the host's actual owner authority
/// remains leased. It neither uploads state nor admits imported state for writing.
/// </summary>
public sealed class WorkspaceContinuationExportService(
    IWorkspaceStore store, IOwnerContextAccessor ownerContext)
{
    /// <summary>Observe local storage provenance for this exact admitted export.
    /// Fresh owner authority and the store's complete read admission are required;
    /// no storage identity is accepted from portable continuation bytes.</summary>
    public CommandResult<WorkspaceContinuationRestoreTarget> ObserveStorage(
        OwnerContextStamp expectedOwner, WorkspaceContinuationExport admitted)
    {
        CommandResult<WorkspaceContinuationRestoreTarget> UnavailableTarget() =>
            new(false, null, "The admitted continuation's storage identity is unavailable.",
                WorkspaceOperationOutcome.Unavailable);
        if (!OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return UnavailableTarget();
        using (lease)
        {
            if (store is not IWorkspaceContinuationReadCapability { SupportsWorkspaceContinuationRead: true } capability
                || admitted.Snapshot.OwnerId != expectedOwner.Owner.NormalizedValue)
                return UnavailableTarget();
            var workspace = admitted.Snapshot.Workspace;
            var read = capability.ReadContinuationStorageTarget(expectedOwner.Owner, workspace.Id);
            if (!read.Success || read.Outcome != WorkspaceOperationOutcome.Success
                || read.Value is not { Exists: true } target || target.OwnerId != admitted.Snapshot.OwnerId
                || target.WorkspaceId != workspace.Id || string.IsNullOrWhiteSpace(target.IncarnationId)
                || !Guid.TryParseExact(target.SlotGeneration, "N", out var slot) || slot == Guid.Empty
                || target.ContentRevision != workspace.ContentRevision || target.SavedRevision != workspace.SavedRevision
                || target.SnapshotDigest != admitted.SnapshotDigest)
                return UnavailableTarget();
            try
            {
                return WorkspaceContinuationSnapshotDigest.Compute(admitted.Snapshot) == target.SnapshotDigest
                    ? read : UnavailableTarget();
            }
            catch (JsonException) { return UnavailableTarget(); }
        }
    }

    public CommandResult<WorkspaceContinuationExport> Export(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId id)
    {
        if (!OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return Unavailable();
        using (lease)
        {
            if (store is not IWorkspaceContinuationReadCapability
                { SupportsWorkspaceContinuationRead: true } capability)
                return Unavailable();

            CommandResult<WorkspaceContinuationSnapshot> read = expectedOwner.Owner.IsLocalSingleUser
                ? capability.ReadContinuation(id)
                : capability.ReadContinuation(expectedOwner.Owner, id);
            if (!read.Success || read.Outcome != WorkspaceOperationOutcome.Success)
                return new(false, null, "Complete workspace continuation read failed.",
                    read.Outcome == WorkspaceOperationOutcome.Success
                        ? WorkspaceOperationOutcome.Unavailable : read.Outcome);

            WorkspaceContinuationSnapshot? snapshot = read.Value;
            if (snapshot?.Workspace?.Document is null
                || snapshot.DelegatedGmCharacterEdits is null
                || !string.Equals(snapshot.OwnerId, expectedOwner.Owner.NormalizedValue, StringComparison.Ordinal)
                || snapshot.Workspace.Id != id
                || snapshot.Workspace.ContentRevision < 1
                || snapshot.Workspace.SavedRevision < 0
                || snapshot.Workspace.SavedRevision > snapshot.Workspace.ContentRevision)
                return new(false, null, "Complete workspace continuation identity is invalid.",
                    WorkspaceOperationOutcome.Corrupt);

            try
            {
                return new(true, new(snapshot, WorkspaceContinuationSnapshotDigest.Compute(snapshot)), null,
                    WorkspaceOperationOutcome.Success);
            }
            catch (JsonException)
            {
                return new(false, null, "Complete workspace continuation state is invalid.",
                    WorkspaceOperationOutcome.Corrupt);
            }
        }
    }

    private static CommandResult<WorkspaceContinuationExport> Unavailable() =>
        new(false, null, "Complete owner-bound workspace continuation export is unavailable.",
            WorkspaceOperationOutcome.Unavailable);
}
