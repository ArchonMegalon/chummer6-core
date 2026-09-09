using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// An explicit full-record transaction. No generic Save/Replace fallback is legal.
/// Only an in-process Core admission may reach the writer; a decoded snapshot is
/// not an admission. Target inspection and recovery are strictly read-only.
/// </summary>
public interface IWorkspaceContinuationRestoreCapability
{
    bool SupportsWorkspaceContinuationRestore => false;

    WorkspaceContinuationRestoreResult InspectRestoreTarget(OwnerScope owner, CharacterWorkspaceId id)
        => new(WorkspaceContinuationRestoreOutcome.Unavailable);

    WorkspaceContinuationRestoreResult RestoreContinuation(WorkspaceContinuationRestoreAdmission admission)
        => new(WorkspaceContinuationRestoreOutcome.Unavailable);

    WorkspaceContinuationRestoreResult RecoverContinuationRestore(OwnerScope owner, CharacterWorkspaceId id,
        Guid operationId, string admissionDigest)
        => new(WorkspaceContinuationRestoreOutcome.Unavailable);
}
