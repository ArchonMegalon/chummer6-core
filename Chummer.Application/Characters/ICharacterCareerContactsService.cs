using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCareerContactsService
{
    // The service acquires the actual injected authority against this exact original
    // stamp. Unsupported implementations must not downgrade to owner-string calls.
    WorkspaceStoreReadResult Read(OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId)
        => new(WorkspaceOperationOutcome.Unavailable, Error: "contact_owner_bound_service_unavailable");
    CharacterCareerContactsPreviewResult Preview(OwnerContextStamp expectedOwner, CharacterCareerContactsRequest request)
        => new(CharacterCareerContactsOutcome.Unavailable, Error: "contact_owner_bound_service_unavailable");
    CharacterCareerContactsResult Commit(OwnerContextStamp expectedOwner, CharacterCareerContactsCommand command,
        CancellationToken cancellationToken = default)
        => new(CharacterCareerContactsOutcome.Unavailable, Error: "contact_owner_bound_service_unavailable");
    CharacterCareerContactsResult Lookup(OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest)
        => new(CharacterCareerContactsOutcome.Unavailable, Error: "contact_owner_bound_service_unavailable");

    WorkspaceStoreReadResult Read(OwnerScope owner, CharacterWorkspaceId workspaceId)
        => new(WorkspaceOperationOutcome.Unavailable, Error: "contact_current_read_unavailable");
    CharacterCareerContactsPreviewResult Preview(OwnerScope owner, CharacterCareerContactsRequest request);
    CharacterCareerContactsResult Commit(OwnerScope owner, CharacterCareerContactsCommand command,
        CancellationToken cancellationToken = default);
    CharacterCareerContactsResult Lookup(OwnerScope owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest);
}

/// <summary>Trusted executable store seam. Revalidate under the common workspace lease; atomically save payload and receipt.</summary>
public interface ICharacterCareerContactsAtomicCommitCapability
{
    bool CareerContactsGuardsInstalled { get; }
    string CareerContactsRuntimeDigest { get; }
    CharacterCareerContactsResult CommitCareerContacts(OwnerScope owner,
        CharacterCareerContactsCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default);
}

/// <summary>Closed contact operations under the actual caller's owner lease and store-owned current admission.</summary>
public interface ICharacterCareerContactsCurrentEnvironmentCapability
{
    WorkspaceStoreReadResult ReadCareerContactsWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id);
    CharacterCareerContactsPreviewResult PreviewCareerContacts(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerContactsRequest request, ICharacterSourceDataResolver sources);
    CharacterCareerContactsResult CommitCareerContacts(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerContactsCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default);
}
