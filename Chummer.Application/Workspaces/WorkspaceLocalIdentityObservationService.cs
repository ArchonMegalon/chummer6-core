using Chummer.Application.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>Owner-bound, synchronous local observation; never portable export.</summary>
public sealed class WorkspaceLocalIdentityObservationService(
    IWorkspaceStore store, IOwnerContextAccessor ownerContext)
{
    public CommandResult<WorkspaceLocalIdentityObservation> Observe(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (store is not IWorkspaceLocalIdentityReadCapability capability
            || ownerContext is not IOwnerContextLeaseAccessor authority
            || expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return Unavailable();
        using (lease)
        {
            var result = capability.ReadLocalIdentity(expectedOwner.Owner, id, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (lease.Stamp != expectedOwner || authority.Capture() != expectedOwner)
                return Unavailable();
            if (!result.Success || result.Outcome != WorkspaceOperationOutcome.Success)
                return new(false, null, "Local workspace identity is unavailable.",
                    result.Outcome == WorkspaceOperationOutcome.Success ? WorkspaceOperationOutcome.Unavailable : result.Outcome);
            if (result.Value is not { } value || value.OwnerId != expectedOwner.Owner.NormalizedValue
                || value.WorkspaceId != id || value.ContentRevision < 1
                || value.SavedRevision < 0 || value.SavedRevision > value.ContentRevision)
                return Unavailable();
            return result;
        }
    }

    private static CommandResult<WorkspaceLocalIdentityObservation> Unavailable() =>
        new(false, null, "Local workspace identity is unavailable.", WorkspaceOperationOutcome.Unavailable);
}
