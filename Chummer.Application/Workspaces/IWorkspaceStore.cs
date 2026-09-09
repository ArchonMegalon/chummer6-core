using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public readonly record struct WorkspaceStoreEntry(
    CharacterWorkspaceId Id,
    DateTimeOffset LastUpdatedUtc,
    long ContentRevision,
    long SavedRevision);

public sealed record WorkspaceStoredDocument(
    CharacterWorkspaceId Id,
    WorkspaceDocument Document,
    long ContentRevision,
    long SavedRevision,
    DateTimeOffset LastUpdatedUtc)
{
    // An observation from the host's store, not part of the portable snapshot.
    // Legacy/other store implementations have not established this capability.
    public WorkspaceLocalHistory? LocalHistory { get; init; }

    /// <summary>
    /// Classifies a receipt against this same store observation. Imported matching
    /// keys remain reserved history, never a successful local replay. A null
    /// boundary preserves pre-continuation implementations, not restore support.
    /// This pure test cannot authorize an owner, a write or an uploaded record.
    /// </summary>
    public bool CanReplayReceipt(long committedRevision) => committedRevision > 0
        && committedRevision <= ContentRevision
        && (LocalHistory is null || (LocalHistory.IsValid(ContentRevision)
            && !LocalHistory.IsImportedRevision(committedRevision)));
}

public sealed record WorkspaceStoreReadResult(
    WorkspaceOperationOutcome Outcome,
    WorkspaceStoredDocument? Value = null,
    string? Error = null)
{
    public bool Success => Outcome == WorkspaceOperationOutcome.Success && Value is not null;
}

public sealed record WorkspaceStoreMutationResult(
    WorkspaceOperationOutcome Outcome,
    WorkspaceStoreEntry? Entry = null,
    string? Error = null)
{
    public bool Success => Outcome == WorkspaceOperationOutcome.Success && Entry is not null;
}

public sealed record DelegatedGmCharacterEditLedgerEntry(
    string IdempotencyKeySha256,
    string CommandSha256,
    DelegatedGmCharacterEditAuditReceipt Receipt);

public enum DelegatedGmCharacterEditStoreOutcome
{
    NotFound = 0,
    Applied = 1,
    Replayed = 2,
    WorkspaceMissing = 3,
    RevisionConflict = 4,
    IdempotencyConflict = 5,
    Corrupt = 6,
    Unavailable = 7
}

public sealed record DelegatedGmCharacterEditStoreResult(
    DelegatedGmCharacterEditStoreOutcome Outcome,
    DelegatedGmCharacterEditAuditReceipt? Receipt = null,
    long? CurrentRevision = null,
    string? Error = null);

/// <summary>
/// Explicit capability advertised only by stores that implement the governed
/// auxiliary-state compare-and-swap and checkpoint as one durable transaction.
/// Each typed creation or career lane must still be validated independently by
/// the store; this capability is never a generic auxiliary-state write grant.
/// </summary>
public interface IWorkspaceAuxiliaryStateAtomicCommitCapability
{
    bool SupportsWorkspaceAuxiliaryStateAtomicCommit { get; }

    WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
        CharacterWorkspaceId id,
        long expectedContentRevision,
        string expectedAuxiliaryStateDigest,
        WorkspaceDocument document);
}

/// <summary>
/// Explicit atomic seam for creating the only workspace shape that may begin with
/// authority-bound auxiliary state. Generic imports must never use this capability.
/// </summary>
public interface ICharacterCreationBootstrapAtomicCreateCapability
{
    bool SupportsCharacterCreationBootstrapAtomicCreate { get; }

    WorkspaceStoreMutationResult CreateCharacterCreationBootstrapWorkspaceDocument(
        CharacterWorkspaceId id,
        WorkspaceDocument document);
}

/// <summary>
/// Explicit linked-owner atomic creation capability. A trusted-local capability
/// does not imply this capability; callers must not fall back to local creation.
/// </summary>
public interface IOwnerScopedCharacterCreationBootstrapAtomicCreateCapability
{
    bool SupportsOwnerScopedCharacterCreationBootstrapAtomicCreate => false;

    WorkspaceStoreMutationResult CreateCharacterCreationBootstrapWorkspaceDocument(
        OwnerScope owner,
        CharacterWorkspaceId id,
        WorkspaceDocument document)
        => new(WorkspaceOperationOutcome.Unavailable,
            Error: "Owner-scoped creation bootstrap is unavailable.");
}

/// <summary>
/// Explicit linked-owner auxiliary-state CAS and checkpoint capability. Both
/// comparisons and the checkpoint must occur in one durable store transaction.
/// It is not an authorization grant or a generic auxiliary-state write API.
/// </summary>
public interface IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability
{
    bool SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit => false;

    WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        string expectedAuxiliaryStateDigest,
        WorkspaceDocument document)
        => new(WorkspaceOperationOutcome.Unavailable,
            Error: "Owner-scoped auxiliary-state commit is unavailable.");
}

/// <summary>
/// Explicit complete inventory for cleanup/recovery decisions. Unlike the
/// display roster, an unreadable member must produce a failed result, not be
/// silently omitted. The returned entries are observations, not deletion grants.
/// </summary>
public interface IWorkspaceStoreInventory
{
    CommandResult<IReadOnlyList<WorkspaceStoreEntry>> Inspect();

    CommandResult<IReadOnlyList<WorkspaceStoreEntry>> Inspect(OwnerScope owner);
}

public interface IWorkspaceStore
{
    WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document);

    WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document);

    WorkspaceStoreMutationResult CreateWorkspaceDocument(
        CharacterWorkspaceId id,
        WorkspaceDocument document)
        => new(
            WorkspaceOperationOutcome.Unavailable,
            Error: "Conditional workspace creation is unavailable.");

    WorkspaceStoreMutationResult CreateWorkspaceDocument(
        OwnerScope owner,
        CharacterWorkspaceId id,
        WorkspaceDocument document)
        => new(
            WorkspaceOperationOutcome.Unavailable,
            Error: "Conditional workspace creation is unavailable.");

    IReadOnlyList<WorkspaceStoreEntry> List();

    IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner);

    WorkspaceStoreReadResult Get(CharacterWorkspaceId id);

    WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id);

    WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document);

    WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document);

    /// <summary>
    /// Atomically replaces a workspace document when its content revision matches and
    /// checkpoints the newly-created content revision in the same durable commit.
    /// Implementations must not compose this operation from a replacement followed by
    /// <see cref="SaveCheckpoint(CharacterWorkspaceId, long)"/>.
    /// </summary>
    WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndCheckpoint(
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document)
        => new(
            WorkspaceOperationOutcome.Unavailable,
            Error: "Atomic workspace replacement and checkpoint is unavailable.");

    /// <summary>
    /// Atomically replaces an owner-scoped workspace document when its content revision
    /// matches and checkpoints the newly-created content revision in the same durable commit.
    /// Implementations must not route this method to local state or compose it from separate
    /// replacement and checkpoint writes.
    /// </summary>
    WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndCheckpoint(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document)
        => new(
            WorkspaceOperationOutcome.Unavailable,
            Error: "Atomic workspace replacement and checkpoint is unavailable.");

    /// <summary>
    /// Governed-authority-only atomic replacement. The implementation must compare both
    /// the content revision and the digest of the currently persisted auxiliary state,
    /// then replace the document and checkpoint the new revision in one durable commit.
    /// Generic replacement APIs must not change auxiliary state.
    /// </summary>
    WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
        CharacterWorkspaceId id,
        long expectedContentRevision,
        string expectedAuxiliaryStateDigest,
        WorkspaceDocument document)
        => new(
            WorkspaceOperationOutcome.Unavailable,
            Error: "Governed-authority workspace replacement is unavailable.");

    /// <summary>
    /// Owner-scoped governed-authority-only atomic replacement. Implementations must not
    /// route this method to local state or compose it from separate writes.
    /// </summary>
    WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        string expectedAuxiliaryStateDigest,
        WorkspaceDocument document)
        => new(
            WorkspaceOperationOutcome.Unavailable,
            Error: "Governed-authority workspace replacement is unavailable.");

    WorkspaceStoreMutationResult SaveCheckpoint(
        CharacterWorkspaceId id,
        long expectedContentRevision);

    WorkspaceStoreMutationResult SaveCheckpoint(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision);

    WorkspaceStoreMutationResult Delete(
        CharacterWorkspaceId id,
        long expectedContentRevision);

    WorkspaceStoreMutationResult Delete(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision);

    DelegatedGmCharacterEditStoreResult LookupDelegatedGmCharacterEdit(
        OwnerScope owner,
        CharacterWorkspaceId id,
        string idempotencyKeySha256,
        string commandSha256)
        => new(
            DelegatedGmCharacterEditStoreOutcome.Unavailable,
            Error: "Delegated GM character-edit idempotency is unavailable.");

    /// <summary>
    /// Atomically applies one owner-scoped CAS replacement and appends its
    /// immutable idempotency/audit entry. Implementations must check replay
    /// before ExpectedRevision and must never route this method to local state.
    /// </summary>
    DelegatedGmCharacterEditStoreResult ApplyDelegatedGmCharacterEdit(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document,
        DelegatedGmCharacterEditLedgerEntry ledgerEntry)
        => new(
            DelegatedGmCharacterEditStoreOutcome.Unavailable,
            Error: "Atomic delegated GM character editing is unavailable.");
}

/// <summary>
/// Verifies that the configured workspace store can complete an owner-scoped,
/// durable write/read/delete cycle. Implementations must not create a user
/// workspace or expose the probe owner through public state.
/// </summary>
public interface IWorkspaceStoreReadinessProbe
{
    void Probe(OwnerScope owner);
}
