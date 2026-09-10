using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Runs the existing whole-build finalizer and all its nested evaluators against
/// one explicitly scoped store while the actual owner authority excludes transitions.
/// This does not owner-bind the separate domain editing services.
/// </summary>
public sealed class OwnerBoundCharacterCreationFinalizationService(
    IWorkspaceStore store,
    IOwnerContextAccessor ownerContext,
    ICharacterFileQueries characterQueries,
    ICharacterSourceDataResolver sourceResolver) : IOwnerBoundCharacterCreationFinalizationService
{
    public CharacterCreationFinalizationResult<CharacterCreationFinalizationState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.Load(request));
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationReview> Review(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Review(request));
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Confirm(request));
    }

    public CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> LookupReceipt(
        OwnerContextStamp expectedOwner, CharacterCreationFinalizationReceiptLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.LookupReceipt(request));
    }

    private CharacterCreationFinalizationResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId,
        Func<CharacterCreationFinalizationService, CharacterCreationFinalizationResult<T>> action)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationFinalizationOutcomes.Unavailable, null,
                [CharacterCreationFinalizationBlockers.WorkspaceUnavailable]);

        using (lease)
        {
            // Never cache this graph or resolve unbound singleton domain services:
            // Skills/Magic construct Attributes internally and Qualities calls both
            // Prerequisite and Attributes. Every one must share this same view.
            var view = new FinalizationWorkspaceStore(store, lease, expectedOwner, workspaceId);
            var prerequisites = new CharacterCreationPrerequisiteService(view, characterQueries, sourceResolver);
            var attributes = new CharacterCreationAttributesService(view, sourceResolver);
            var service = new CharacterCreationFinalizationService(view, characterQueries,
                prerequisites, attributes,
                new CharacterCreationSkillsService(view, sourceResolver),
                new CharacterCreationQualitiesService(view, sourceResolver, prerequisites, attributes),
                new CharacterCreationMagicResonanceService(view, sourceResolver),
                new CharacterCreationResourcesService(view, sourceResolver),
                new CharacterCreationGearService(view, sourceResolver));
            // Includes idempotency reads, the durable CAS and postcommit receipt
            // observation. No await or postcommit owner recapture may split it.
            return action(service);
        }
    }

    private sealed class FinalizationWorkspaceStore(
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

        // The private finalizer graph needs no inventory or other mutation lane.
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
}
