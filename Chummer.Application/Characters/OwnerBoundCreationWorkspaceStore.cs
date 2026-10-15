using Chummer.Application.Owners;
using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

// A synchronous, lease-confined view for creation evaluators. The canonical store
// still validates the exact typed auxiliary transition; this is not a general write grant.
internal sealed class OwnerBoundCreationWorkspaceStore(
    IWorkspaceStore inner, IOwnerContextLease lease, OwnerContextStamp owner,
    CharacterWorkspaceId workspaceId, bool reuseReadObservation = false) : IWorkspaceStore, IWorkspaceAuxiliaryStateAtomicCommitCapability,
    ICharacterCreationKarmaMetatypeAtomicCommitCapability, ICharacterCreationKarmaFinalizationAtomicCommitCapability,
    ICharacterCreationCompletionAtomicCommitCapability, ICharacterCreationLoadoutAtomicCommitCapability,
    ICharacterCreationFinalizationAtomicCommitCapability,
    ICharacterCreationLifeModuleFinalizationAtomicCommitCapability
{
    // Opt-in for a single synchronous Load only. Retain the complete validated
    // store result, not a portable reconstruction; ownership is checked on every
    // access. Preview/Confirm and subsequent public calls get a fresh view.
    private bool _reuseReadObservation = reuseReadObservation;
    private WorkspaceStoreReadResult? _readObservation;

    private void BeginMutation()
    {
        // Never let a later write or raced-receipt read use an earlier snapshot,
        // even if a future caller accidentally enables this view for a mutation.
        _reuseReadObservation = false;
        _readObservation = null;
    }

    private bool IsActive
    {
        get
        {
            try { return lease.Stamp == owner; }
            catch (ObjectDisposedException) { return false; }
        }
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> CommitFinalization(
        OwnerScope requestedOwner, CharacterCreationFinalizationConfirmRequest request,
        ICharacterSourceDataResolver resolver, ICharacterFileQueries queries)
    {
        BeginMutation();
        return IsActive && requestedOwner.IsLocalSingleUser && request.Binding.WorkspaceId == workspaceId
            && inner is ICharacterCreationFinalizationAtomicCommitCapability capability
            ? capability.CommitFinalization(owner.Owner, request, resolver, queries)
            : CharacterCreationFinalizationTransaction.Rejected(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
    }

    public CharacterCreationFoundationResult<CharacterCreationLoadoutReceipt> CommitLoadout(
        OwnerScope requestedOwner, CharacterCreationLoadoutConfirmRequest request, ICharacterSourceDataResolver resolver,
        CancellationToken cancellationToken = default)
    {
        BeginMutation();
        cancellationToken.ThrowIfCancellationRequested();
        // Local-shaped evaluator methods are confined to this exact admitted partition.
        return IsActive && requestedOwner.IsLocalSingleUser && request.Request.Binding.WorkspaceId == workspaceId
            && inner is ICharacterCreationLoadoutAtomicCommitCapability capability
            ? capability.CommitLoadout(owner.Owner, request, resolver, cancellationToken)
            : CharacterCreationLoadoutService.Reject<CharacterCreationLoadoutReceipt>(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
    }

    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitCompletion(
        CharacterCreationCompletionApplyRequest request, ICharacterSourceDataResolver resolver,
        ICharacterFileQueries queries, CancellationToken cancellationToken = default)
    {
        BeginMutation();
        cancellationToken.ThrowIfCancellationRequested();
        return IsActive && request.Binding.WorkspaceId == workspaceId
            && inner is ICharacterCreationCompletionAtomicCommitCapability capability
            ? capability.CommitCompletion(owner.Owner, request, resolver, queries, cancellationToken)
            : CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
    }

    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitCompletion(
        OwnerScope requestedOwner, CharacterCreationCompletionApplyRequest request, ICharacterSourceDataResolver resolver,
        ICharacterFileQueries queries, CancellationToken cancellationToken = default)
        => requestedOwner == owner.Owner ? CommitCompletion(request, resolver, queries, cancellationToken)
            : CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);

    public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> CommitLifeModuleFinalization(
        CharacterCreationFoundationFinalizationConfirmRequest request, ICharacterSourceDataResolver resolver,
        ILifeModulesCatalogService catalog, ICharacterFileQueries characterFiles)
    {
        BeginMutation();
        return IsActive && request.Binding.WorkspaceId == workspaceId
            && inner is ICharacterCreationLifeModuleFinalizationAtomicCommitCapability capability
            ? capability.CommitLifeModuleFinalization(owner.Owner, request, resolver, catalog, characterFiles)
            : CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
    }

    public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> CommitLifeModuleFinalization(
        OwnerScope requestedOwner, CharacterCreationFoundationFinalizationConfirmRequest request, ICharacterSourceDataResolver resolver,
        ILifeModulesCatalogService catalog, ICharacterFileQueries characterFiles)
        => requestedOwner == owner.Owner ? CommitLifeModuleFinalization(request, resolver, catalog, characterFiles)
            : CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);

    public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => IsActive
        && (owner.Owner.IsLocalSingleUser
            ? inner is IWorkspaceAuxiliaryStateAtomicCommitCapability
                { SupportsWorkspaceAuxiliaryStateAtomicCommit: true }
            : inner is IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability
                { SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit: true });

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> CommitKarmaMetatype(
        CharacterCreationKarmaMetatypeConfirmRequest request, ICharacterSourceDataResolver sourceResolver)
    {
        BeginMutation();
        return IsActive && request.Binding.WorkspaceId == workspaceId
            && inner is ICharacterCreationKarmaMetatypeAtomicCommitCapability capability
            ? capability.CommitKarmaMetatype(owner.Owner, request, sourceResolver)
            : CharacterCreationKarmaMetatypeTransaction.Blocked(CharacterCreationKarmaMetatypeBlockers.PersistenceUnavailable);
    }

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> CommitKarmaMetatype(
        OwnerScope requestedOwner, CharacterCreationKarmaMetatypeConfirmRequest request,
        ICharacterSourceDataResolver sourceResolver)
        => requestedOwner == owner.Owner ? CommitKarmaMetatype(request, sourceResolver)
            : CharacterCreationKarmaMetatypeTransaction.Blocked(CharacterCreationKarmaMetatypeBlockers.WorkspaceUnavailable);

    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitKarmaFinalization(
        CharacterCreationKarmaFinalizationConfirmRequest request, ICharacterSourceDataResolver sourceResolver)
    {
        BeginMutation();
        return IsActive && request.Confirmation.Binding.WorkspaceId == workspaceId
            && inner is ICharacterCreationKarmaFinalizationAtomicCommitCapability capability
            ? capability.CommitKarmaFinalization(owner.Owner, request, sourceResolver)
            : CharacterCreationKarmaFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
    }

    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitKarmaFinalization(
        OwnerScope requestedOwner, CharacterCreationKarmaFinalizationConfirmRequest request, ICharacterSourceDataResolver sourceResolver)
        => requestedOwner == owner.Owner ? CommitKarmaFinalization(request, sourceResolver)
            : CharacterCreationKarmaFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);

    public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
    {
        if (!IsActive || id != workspaceId)
            return new(WorkspaceOperationOutcome.Unavailable,
                Error: "The finalization owner/workspace admission is unavailable.");
        if (_readObservation is { } observed)
            return observed;
        WorkspaceStoreReadResult read = owner.Owner.IsLocalSingleUser ? inner.Get(id) : inner.Get(owner.Owner, id);
        // Return the exact observation, including LocalHistory. Copying only
        // portable fields would promote imported receipts. Failures are not cached.
        if (_reuseReadObservation && read.Success && read.Value!.Id == workspaceId)
            _readObservation = read;
        return read;
    }

    public WorkspaceStoreReadResult Get(OwnerScope requestedOwner, CharacterWorkspaceId id)
        => requestedOwner == owner.Owner ? Get(id)
            : new(WorkspaceOperationOutcome.Unavailable,
                Error: "The finalization owner does not match.");

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
        CharacterWorkspaceId id, long expectedContentRevision,
        string expectedAuxiliaryStateDigest, WorkspaceDocument document)
    {
        BeginMutation();
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
