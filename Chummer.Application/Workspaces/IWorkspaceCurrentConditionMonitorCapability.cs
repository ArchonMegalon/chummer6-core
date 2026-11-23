using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>Closed display-only condition read under the caller's existing owner lease.</summary>
public interface IWorkspaceCurrentConditionMonitorCapability
{
    CommandResult<CharacterConditionMonitorSection> GetCurrentDisplayConditionMonitor(
        IOwnerContextAccessor owners, OwnerContextStamp owner, IOwnerContextLease lease,
        CharacterWorkspaceId id, CancellationToken cancellationToken = default);
}

public interface IWorkspaceCurrentConditionMonitorStoreCapability
{
    CommandResult<CharacterConditionMonitorSection> GetCurrentDisplayConditionMonitor(
        IOwnerContextAccessor owners, OwnerContextStamp owner, IOwnerContextLease lease,
        CharacterWorkspaceId id, IRulesetWorkspaceCodecResolver unboundCodecs,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional condition projection within an existing bounded display observation.
/// Every read still admits the live owner, record and sources; no mutation authority.
/// </summary>
public interface IWorkspaceCurrentConditionMonitorObservation
{
    bool SupportsConditionMonitor { get; }
    CommandResult<CharacterConditionMonitorSection> GetConditionMonitor(
        IOwnerContextLease lease, CancellationToken cancellationToken = default);
}
