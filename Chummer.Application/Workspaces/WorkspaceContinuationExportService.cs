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
