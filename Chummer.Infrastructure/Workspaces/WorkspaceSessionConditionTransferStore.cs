using Chummer.Application.Owners;
using Chummer.Application.Session;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;

namespace Chummer.Infrastructure.Workspaces;

/// <summary>One synchronous owner lease; the caller holds the journal before entering this adapter.</summary>
public sealed class WorkspaceSessionConditionTransferStore(FileWorkspaceStore store,
    WorkspaceSessionPlayAuthorityResolver resolver, IOwnerContextLeaseAccessor owners, OwnerContextStamp expected,
    IOwnerContextLease lease) : ISessionConditionTransferStore
{
    private void Check(OwnerScope owner) => SessionPlayEncoding.Conflict(expected.IsValid && owner == expected.Owner
        && owners.Capture() == expected && lease.Stamp == expected, "session-owner-lease-changed");
    private SessionPlayAuthority Project(WorkspaceStoredDocument saved,
        Chummer.Application.Characters.ICharacterSourceDataResolver sources, string environment)
        => resolver.ResolveObserved5262(expected.Owner, saved.Id.Value, saved, sources, true, environment);

    public SessionConditionTransferBinding Read(OwnerScope owner, string characterId)
    { Check(owner); return store.ReadConditionTransfer5460(owners, expected, lease, new(characterId), Project); }
    public SessionConditionTransferCanonicalReceipt? Lookup(OwnerScope owner, string characterId, string operationId)
    { Check(owner); return store.LookupConditionTransfer5460(owners, expected, lease, new(characterId), operationId); }
    public SessionConditionTransferCanonicalReceipt Commit(OwnerScope owner, string characterId, SessionConditionTransferCommit admission)
    { Check(owner); return store.CommitConditionTransfer5460(owners, expected, lease, new(characterId), admission, Project); }
    public SessionPlayAuthority ReadCommittedAuthority(OwnerScope owner, string characterId, SessionConditionTransferCanonicalReceipt receipt)
    { Check(owner); return store.ReadConditionTransfer5460(owners, expected, lease, new(characterId), Project, receipt).Authority; }
}
