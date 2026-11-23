using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCareerFocusBondingService
{
    CharacterCareerFocusBondingListResult List(OwnerContextStamp owner, CharacterWorkspaceId workspaceId, long expectedRevision);
    CharacterCareerFocusBondingPreviewResult Preview(OwnerContextStamp owner, CharacterCareerFocusBondingRequest request);
    CharacterCareerFocusBondingResult Commit(OwnerContextStamp owner, CharacterCareerFocusBondingCommand command,
        CancellationToken cancellationToken = default);
    CharacterCareerFocusBondingResult Lookup(OwnerContextStamp owner, CharacterWorkspaceId workspaceId,
        Guid originalOperationId, string originalCommandDigest);
    CharacterCareerFocusBondingResult FinalizeDispatch(OwnerContextStamp owner,
        CharacterCareerFocusBondingCommand originalCommand, CancellationToken cancellationToken = default);
}

public interface ICharacterCareerFocusBondingAtomicCommitCapability
{
    bool CareerFocusBondingGuardsInstalled { get; }
    string CareerFocusBondingRuntimeDigest { get; }
    CharacterCareerFocusBondingResult CommitCareerFocusBonding(IOwnerContextLease ownerLease,
        CharacterCareerFocusBondingCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default);
    CharacterCareerFocusBondingResult FinalizeCareerFocusBondingDispatch(IOwnerContextLease ownerLease,
        CharacterCareerFocusBondingCommand originalCommand, CancellationToken cancellationToken = default);
}

// Issued and consumed under the caller's existing owner lease. Ordinary Get/save stay closed.
public interface ICharacterCareerFocusBondingCurrentEnvironmentCapability
{
    CharacterCareerFocusBondingResult LookupCareerFocusBonding(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, Guid operationId, string commandDigest);
    WorkspaceStoreReadResult ReadCareerFocusBondingWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id);
    CharacterCareerFocusBondingListResult ListCareerFocusBonding(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, long expectedRevision);
    CharacterCareerFocusBondingPreviewResult PreviewCareerFocusBonding(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerFocusBondingRequest request, ICharacterSourceDataResolver sources);
    CharacterCareerFocusBondingResult CommitCareerFocusBonding(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerFocusBondingCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default);
    CharacterCareerFocusBondingResult FinalizeCareerFocusBondingDispatch(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerFocusBondingCommand originalCommand, CancellationToken cancellationToken = default);
}
