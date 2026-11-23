using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Owner-leased reward operations over the store's exact admitted current environment.</summary>
public interface ICharacterAfterRunRewardCurrentEnvironmentStore
{
    CharacterAfterRunRewardReadResult ReadAfterRunReward(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId workspaceId);
    CharacterAfterRunRewardPreviewResult PreviewAfterRunReward(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterAfterRunRewardPreviewRequest request);
    CharacterAfterRunRewardResult CommitAfterRunReward(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterAfterRunRewardCommand command, CancellationToken cancellationToken);
    CharacterAfterRunRewardResult LookupAfterRunReward(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId workspaceId, Guid operationId, string commandDigest);
}
