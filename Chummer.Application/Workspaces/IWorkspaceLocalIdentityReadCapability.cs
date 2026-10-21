using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Trusted local-store port. The caller must hold its actual current owner lease.
/// The implementation admits the exact retained history and delegated ledger and
/// computes identity under one workspace lease, returning no source/document data.
/// Use WorkspaceLocalIdentityObservationService outside an existing owner lease.
/// Never call this port inside a workspace commit fence (it acquires that lock).
/// </summary>
public interface IWorkspaceLocalIdentityReadCapability
{
    CommandResult<WorkspaceLocalIdentityObservation> ReadLocalIdentity(
        OwnerScope owner, CharacterWorkspaceId id, CancellationToken cancellationToken = default);
}
