using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Explicit complete-state read admission. Implementations must capture the
/// document, revisions, auxiliary state and delegated ledger under one store lease.
/// A public workspace snapshot alone cannot satisfy this capability.
/// </summary>
public interface IWorkspaceContinuationReadCapability
{
    bool SupportsWorkspaceContinuationRead => false;

    CommandResult<WorkspaceContinuationSnapshot> ReadContinuation(CharacterWorkspaceId id) =>
        new(false, null, "Complete workspace continuation reads are unavailable.",
            WorkspaceOperationOutcome.Unavailable);

    CommandResult<WorkspaceContinuationSnapshot> ReadContinuation(OwnerScope owner, CharacterWorkspaceId id) =>
        new(false, null, "Owner-scoped workspace continuation reads are unavailable.",
            WorkspaceOperationOutcome.Unavailable);
}
