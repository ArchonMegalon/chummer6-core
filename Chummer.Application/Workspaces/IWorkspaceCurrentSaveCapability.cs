using Chummer.Application.Owners;
using Chummer.Contracts.Api;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Explicit checkpoint of the exact current revision under the caller's actual owner
/// lease. Display/roster admission does not grant this capability or authorize a retry.
/// </summary>
public interface IWorkspaceCurrentSaveCapability
{
    CommandResult<WorkspaceSaveReceipt> SaveCurrent(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId id, long expectedContentRevision, CancellationToken cancellationToken = default);
}

/// <summary>
/// The store owns fresh retained-source admission, checkpoint construction and the final
/// write fence. The synchronous receipt projection receives no source resolver or write
/// authority; it runs before publication and cannot supply a replacement document.
/// </summary>
public interface IWorkspaceCurrentSaveStoreCapability
{
    CommandResult<WorkspaceSaveReceipt> SaveCurrent(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId id, long expectedContentRevision, IRulesetWorkspaceCodecResolver unboundCodecs,
        Func<WorkspaceStoredDocument, DataExportBundle, WorkspaceSaveReceipt> buildReceipt,
        CancellationToken cancellationToken = default);
}
