using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public sealed partial class WorkspaceService : IWorkspaceCurrentConditionMonitorCapability
{
    public CommandResult<CharacterConditionMonitorSection> GetCurrentDisplayConditionMonitor(
        IOwnerContextAccessor owners, OwnerContextStamp owner, IOwnerContextLease lease,
        CharacterWorkspaceId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _workspaceStore is IWorkspaceCurrentConditionMonitorStoreCapability current
            ? NormalizeCurrentDisplayResult(current.GetCurrentDisplayConditionMonitor(
                owners, owner, lease, id, _workspaceCodecResolver, cancellationToken))
            : CurrentDisplayUnavailable<CharacterConditionMonitorSection>();
    }
}
