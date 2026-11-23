using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
namespace Chummer.Application.Characters;

public interface ICharacterCareerSkillGroupOperationScope
{
    // Synchronous only: one actual owner lease covers lookup, projection and commit.
    IDisposable BeginSkillGroupOperation(CancellationToken cancellationToken);
}

public interface ICharacterCareerSkillGroupCurrentCapability
{
    CharacterCareerSkillGroupWorkspaceReadResult ReadCurrentSkillGroup(IOwnerContextAccessor owners,
        OwnerContextStamp stamp, IOwnerContextLease lease, CharacterWorkspaceId id,
        CharacterCareerSkillGroupIdentity identity, CancellationToken cancellationToken = default);
    CharacterCareerSkillGroupWorkspaceLookupResult LookupCurrentSkillGroup(IOwnerContextAccessor owners,
        OwnerContextStamp stamp, IOwnerContextLease lease, CharacterWorkspaceId id, Guid operationId,
        string commandDigest, CancellationToken cancellationToken = default);
    CharacterCareerSkillGroupWorkspaceCommitResult CommitCurrentSkillGroup(IOwnerContextAccessor owners,
        OwnerContextStamp stamp, IOwnerContextLease lease, CharacterCareerSkillGroupWorkspaceCommitRequest request,
        CancellationToken cancellationToken = default);
}
