using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public CommandResult<CharacterConditionMonitorSection> GetCurrentDisplayConditionMonitor(
        IOwnerContextAccessor owners, OwnerContextStamp owner, IOwnerContextLease lease,
        CharacterWorkspaceId id, IRulesetWorkspaceCodecResolver unboundCodecs,
        CancellationToken cancellationToken = default)
    {
        var read = ReadCurrentDisplay(owners, owner, lease, id, unboundCodecs, false,
            out _, out _, out var monitor, cancellationToken, includeConditionMonitor: true);
        return read.Success && monitor is not null
            ? new(true, monitor, null, WorkspaceOperationOutcome.Success)
            : new(false, null, read.Error, read.Outcome);
    }
}
