using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCareerSpiritFetteringService
{
    CharacterCareerSpiritFetteringListResult List(OwnerContextStamp owner, CharacterWorkspaceId workspaceId, long expectedRevision);
    CharacterCareerSpiritFetteringPreviewResult Preview(OwnerContextStamp owner, CharacterCareerSpiritFetteringRequest request);
    CharacterCareerSpiritFetteringResult Commit(OwnerContextStamp owner, CharacterCareerSpiritFetteringCommand command,
        CancellationToken cancellationToken = default);
    CharacterCareerSpiritFetteringResult Lookup(OwnerContextStamp owner, CharacterWorkspaceId workspaceId,
        Guid originalOperationId, string originalCommandDigest);
    CharacterCareerSpiritFetteringResult FinalizeDispatch(OwnerContextStamp owner,
        CharacterCareerSpiritFetteringCommand originalCommand, CancellationToken cancellationToken = default);
}

public interface ICharacterCareerSpiritFetteringAtomicCommitCapability
{
    bool CareerSpiritFetteringGuardsInstalled { get; }
    string CareerSpiritFetteringRuntimeDigest { get; }
    CharacterCareerSpiritFetteringResult CommitCareerSpiritFettering(IOwnerContextLease ownerLease,
        CharacterCareerSpiritFetteringCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default);
    CharacterCareerSpiritFetteringResult FinalizeCareerSpiritFetteringDispatch(IOwnerContextLease ownerLease,
        CharacterCareerSpiritFetteringCommand originalCommand, CancellationToken cancellationToken = default);
}

// Issued and consumed under the caller's existing owner lease. Ordinary Get/save stay closed.
public interface ICharacterCareerSpiritFetteringCurrentEnvironmentCapability
{
    WorkspaceStoreReadResult ReadCareerSpiritFetteringWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id);
    CharacterCareerSpiritFetteringListResult ListCareerSpiritFettering(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, long expectedRevision);
    CharacterCareerSpiritFetteringPreviewResult PreviewCareerSpiritFettering(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerSpiritFetteringRequest request, ICharacterSourceDataResolver sources);
    CharacterCareerSpiritFetteringResult CommitCareerSpiritFettering(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerSpiritFetteringCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default);
    CharacterCareerSpiritFetteringResult FinalizeCareerSpiritFetteringDispatch(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerSpiritFetteringCommand originalCommand, CancellationToken cancellationToken = default);
}
