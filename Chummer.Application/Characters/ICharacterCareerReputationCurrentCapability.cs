using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Owner-leased reputation authority. Every call reloads and admits the complete
/// current workspace; receipt lookup is read-only and requires the same fresh authority.</summary>
public interface ICharacterCareerReputationCurrentCapability
{
    CharacterCareerReputationReadResult ReadCareerReputation(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, ICharacterSourceDataResolver sources);
    CharacterCareerReputationPreviewResult PreviewCareerReputation(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerReputationRequest request, ICharacterSourceDataResolver sources);
    CharacterCareerReputationResult CommitCareerReputation(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerReputationCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default);
    CharacterCareerReputationResult LookupCareerReputation(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, Guid operationId, string commandDigest);
}
