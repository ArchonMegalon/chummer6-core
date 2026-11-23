using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Closed, synchronous settlement operations under the actual owner's live lease.
/// The store validates retained custody and typed proposals and owns the entire checkpoint.
/// No replacement document, source resolver or execution authority is accepted from callers.</summary>
public sealed record CharacterAfterRunSettlementSnapshotReadResult(WorkspaceStoreReadResult Read, string CurrentContextDigest = "");

public interface ICharacterAfterRunSettlementCurrentStore
{
    CharacterAfterRunSettlementSnapshotReadResult ReadAfterRunSettlementSnapshot(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id);
    CharacterAfterRunSettlementWorkspaceReadResult ReadAfterRunSettlement(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, CharacterAfterRunSettlementIdentity identity,
        ICharacterAfterRunSettlementProposalProjectionSource proposals);
    CharacterAfterRunSettlementWorkspaceLookupResult LookupAfterRunSettlement(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, Guid transactionId, string commandDigest);
    CharacterAfterRunSettlementWorkspaceCommitResult CommitAfterRunSettlement(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterAfterRunSettlementWorkspaceCommitRequest request,
        ICharacterAfterRunSettlementProposalProjectionSource proposals, CancellationToken cancellationToken = default);
}
