using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Session;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;

namespace Chummer.Infrastructure.Workspaces;

public sealed class WorkspaceSessionNaturalRecoveryStore(FileWorkspaceStore store,
    WorkspaceSessionPlayAuthorityResolver resolver, ICharacterSourceDataResolver sources,
    IOwnerContextLeaseAccessor owners, OwnerContextStamp expected, IOwnerContextLease lease) : ISessionNaturalRecoveryStore, ISessionNaturalRecoveryAcknowledgementStore
{
    private void Check(OwnerScope owner, string characterId) => SessionPlayEncoding.Conflict(expected.IsValid
        && expected.Owner == owner && owners.Capture() == expected && lease.Stamp == expected
        && !string.IsNullOrWhiteSpace(characterId), "session-owner-lease-changed");
    private SessionPlayAuthority Project(WorkspaceStoredDocument saved, ICharacterSourceDataResolver admitted, string environment)
        => resolver.ResolveObserved5262(expected.Owner, saved.Id.Value, saved, admitted, true, environment);
    public SessionNaturalRecoveryCanonicalReview Review(OwnerScope owner, string characterId, Sr5RecoveryRequest request)
    { Check(owner, characterId); SessionPlayEncoding.Conflict(request.WorkspaceId.Value == characterId, "recovery-workspace-mismatch");
        return store.ReviewSessionNaturalRecovery5472(owners, lease, request, sources, Project); }
    public Sr5RecoveryResult Lookup(OwnerScope owner, string characterId, Guid operationId, string commandDigest)
    { Check(owner, characterId); return store.LookupSessionNaturalRecovery5472(owners, lease, new(characterId), operationId, commandDigest); }
    public SessionNaturalRecoveryReceipt ReadCommitted(OwnerScope owner, string characterId, SessionNaturalRecoveryIntent intent, Sr5RecoveryOperation operation)
    { Check(owner, characterId); return store.ReadSessionNaturalRecovery5472(owners, lease, new(characterId), intent, operation, Project); }
    public void ConfirmAcknowledgement(OwnerScope owner, string characterId, SessionNaturalRecoveryAcknowledgement acknowledgement)
    { Check(owner, characterId); store.ConfirmRecoveryAcknowledgement5485(owners, lease, new(characterId), acknowledgement); }
    public void Execute(OwnerScope owner, string characterId, SessionNaturalRecoveryCommit commit, CancellationToken token)
    { Check(owner, characterId); SessionPlayEncoding.Conflict(commit.Intent.Command.Request.WorkspaceId.Value == characterId, "recovery-workspace-mismatch");
        store.ExecuteNaturalRecovery(owners, lease, commit.Intent.Command, sources, token, commit, Project); }
}
