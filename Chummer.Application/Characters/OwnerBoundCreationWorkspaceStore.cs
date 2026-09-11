using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

// A synchronous, lease-confined view for creation evaluators. The canonical store
// still validates the exact typed auxiliary transition; this is not a general write grant.
internal sealed class OwnerBoundCreationWorkspaceStore(
    IWorkspaceStore inner, IOwnerContextLease lease, OwnerContextStamp owner,
    CharacterWorkspaceId workspaceId) : IWorkspaceStore, IWorkspaceAuxiliaryStateAtomicCommitCapability
{
    private bool IsActive
    {
        get
        {
            try { return lease.Stamp == owner; }
            catch (ObjectDisposedException) { return false; }
        }
    }

    public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => IsActive
        && (owner.Owner.IsLocalSingleUser
            ? inner is IWorkspaceAuxiliaryStateAtomicCommitCapability
                { SupportsWorkspaceAuxiliaryStateAtomicCommit: true }
            : inner is IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability
                { SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit: true });

    public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
        => IsActive && id == workspaceId
            // Return the exact store observation, including LocalHistory.
            // Copying only portable fields would promote imported receipts.
            ? owner.Owner.IsLocalSingleUser ? inner.Get(id) : inner.Get(owner.Owner, id)
            : new(WorkspaceOperationOutcome.Unavailable,
                Error: "The finalization owner/workspace admission is unavailable.");

    public WorkspaceStoreReadResult Get(OwnerScope requestedOwner, CharacterWorkspaceId id)
        => requestedOwner == owner.Owner ? Get(id)
            : new(WorkspaceOperationOutcome.Unavailable,
                Error: "The finalization owner does not match.");

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
        CharacterWorkspaceId id, long expectedContentRevision,
        string expectedAuxiliaryStateDigest, WorkspaceDocument document)
    {
        if (id != workspaceId || !SupportsWorkspaceAuxiliaryStateAtomicCommit)
            return UnavailableMutation();
        return owner.Owner.IsLocalSingleUser
            ? ((IWorkspaceAuxiliaryStateAtomicCommitCapability)inner)
                .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id, expectedContentRevision, expectedAuxiliaryStateDigest, document)
            : ((IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability)inner)
                .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(owner.Owner,
                    id, expectedContentRevision, expectedAuxiliaryStateDigest, document);
    }

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
        OwnerScope requestedOwner, CharacterWorkspaceId id, long expectedContentRevision,
        string expectedAuxiliaryStateDigest, WorkspaceDocument document)
        => requestedOwner == owner.Owner
            ? ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                id, expectedContentRevision, expectedAuxiliaryStateDigest, document)
            : UnavailableMutation();

    // These private creation evaluator graphs need no inventory or other mutation lane.
    // Explicitly deny them instead of inheriting a future ambient fallback.
    public IReadOnlyList<WorkspaceStoreEntry> List() => [];
    public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope requestedOwner) => [];
    public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
        => UnavailableMutation();
    public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope requestedOwner, WorkspaceDocument document)
        => UnavailableMutation();
    public WorkspaceStoreMutationResult CreateWorkspaceDocument(CharacterWorkspaceId id, WorkspaceDocument document)
        => UnavailableMutation();
    public WorkspaceStoreMutationResult CreateWorkspaceDocument(
        OwnerScope requestedOwner, CharacterWorkspaceId id, WorkspaceDocument document) => UnavailableMutation();
    public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
        CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document) => UnavailableMutation();
    public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
        OwnerScope requestedOwner, CharacterWorkspaceId id, long expectedContentRevision,
        WorkspaceDocument document) => UnavailableMutation();
    public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndCheckpoint(
        CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document) => UnavailableMutation();
    public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndCheckpoint(
        OwnerScope requestedOwner, CharacterWorkspaceId id, long expectedContentRevision,
        WorkspaceDocument document) => UnavailableMutation();
    public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long expectedContentRevision)
        => UnavailableMutation();
    public WorkspaceStoreMutationResult SaveCheckpoint(
        OwnerScope requestedOwner, CharacterWorkspaceId id, long expectedContentRevision) => UnavailableMutation();
    public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long expectedContentRevision)
        => UnavailableMutation();
    public WorkspaceStoreMutationResult Delete(
        OwnerScope requestedOwner, CharacterWorkspaceId id, long expectedContentRevision) => UnavailableMutation();
    public DelegatedGmCharacterEditStoreResult LookupDelegatedGmCharacterEdit(
        OwnerScope requestedOwner, CharacterWorkspaceId id, string idempotencyKeySha256, string commandSha256)
        => new(DelegatedGmCharacterEditStoreOutcome.Unavailable);
    public DelegatedGmCharacterEditStoreResult ApplyDelegatedGmCharacterEdit(
        OwnerScope requestedOwner, CharacterWorkspaceId id, long expectedContentRevision,
        WorkspaceDocument document, DelegatedGmCharacterEditLedgerEntry ledgerEntry)
        => new(DelegatedGmCharacterEditStoreOutcome.Unavailable);

    private static WorkspaceStoreMutationResult UnavailableMutation() => new(
        WorkspaceOperationOutcome.Unavailable,
        Error: "The owner-bound finalization operation is unavailable.");
}
