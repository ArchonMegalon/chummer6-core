using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Reuses ordinary finalization admission without accepting a replacement or a write predicate.</summary>
public static class CharacterCreationFinalizationTransaction
{
    public static bool IsConfirmed(CharacterCreationFinalizationConfirmRequest? request)
        => request is { ExplicitlyConfirmed: true, Binding: { } binding, StartingCash: not null }
            && binding.BuildMethod is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen
            && binding.ContentRevision is > 0 and < long.MaxValue && binding.SavedRevision == binding.ContentRevision
            && !string.IsNullOrWhiteSpace(binding.WorkspaceId.Value)
            && CharacterCreationFinalizationDigest.IsCanonical(binding.RawCharacterXmlDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(binding.AuthorityDigest)
            && CharacterCareerReputationTransaction.IsDigest(binding.AuxiliaryStateDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(request.PreviewDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(request.PlanDigest)
            && request.IdempotencyKey is { Length: >= 8 and <= 200 }
            && request.IdempotencyKey == request.IdempotencyKey.Trim()
            && !request.IdempotencyKey.Any(char.IsControl);

    public static CharacterCreationFinalizationConfirmRequest Freeze(CharacterCreationFinalizationConfirmRequest request)
        => JsonSerializer.Deserialize<CharacterCreationFinalizationConfirmRequest>(JsonSerializer.Serialize(request))
            ?? throw new InvalidDataException("Finalization command could not be detached.");

    public static CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt>? Lookup(
        WorkspaceStoredDocument saved, CharacterCreationFinalizationConfirmRequest request)
    {
        if (IsConfirmed(request) && CharacterCreationCompletionDraftTransaction.HasKey(saved.Document.AuxiliaryState,
                CharacterCreationFinalizationDigest.ComputeIdempotencyKeyDigest(request.IdempotencyKey)))
            return Rejected(CharacterCreationFinalizationBlockers.IdempotencyConflict);
        var ledger = saved.Document.AuxiliaryState.CharacterCreationFinalizationReceipts;
        if (ledger is null) return null;
        if (!IsConfirmed(request) || !WorkspaceAuxiliaryStateIntegrity.IsValidShape(saved.Id,
                saved.ContentRevision, saved.Document.AuxiliaryState))
            return Rejected(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
        string key = CharacterCreationFinalizationDigest.ComputeIdempotencyKeyDigest(request.IdempotencyKey);
        var entry = ledger.SingleOrDefault(row => row.IdempotencyKeyDigest == key);
        if (entry is null) return Rejected(CharacterCreationFinalizationBlockers.CharacterAlreadyCreated);
        return entry.CommandDigest == CharacterCreationFinalizationService.ComputeCommandDigest(request)
                && entry.Receipt.CompletionAuthorityDigest is null && saved.CanReplayReceipt(entry.Receipt.ContentRevision)
            ? new(CharacterCreationFinalizationOutcomes.Replayed, entry.Receipt, [])
            : Rejected(CharacterCreationFinalizationBlockers.IdempotencyConflict);
    }

    public static bool TryBuild(IWorkspaceStore admittedStore, OwnerScope owner, WorkspaceStoredDocument saved,
        ICharacterSourceDataResolver resolver, ICharacterFileQueries queries,
        CharacterCreationFinalizationConfirmRequest request,
        out WorkspaceDocument? replacement, out CharacterCreationFinalizationReceipt? receipt)
    {
        replacement = null; receipt = null;
        if (!IsConfirmed(request) || request.Binding.WorkspaceId != saved.Id
            || saved.Document.AuxiliaryState.CharacterCreationLoadoutDraft is null
            || Lookup(saved, request) is not null) return false;
        try
        {
            // Re-admit live source on every call, including the post-flush fence.
            // An existing operation scope revalidates reuse and stays poisoned
            // after drift; open a scope here only when given a scope factory.
            // No archived/source-supplied replacement is admitted.
            using ICharacterSourceDataResolverOperationScope? scope =
                (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var live = scope ?? resolver;
            var view = new ObservationStore(admittedStore, owner, saved);
            var prerequisites = new CharacterCreationPrerequisiteService(view, queries, live);
            var attributes = new CharacterCreationAttributesService(view, live);
            var service = new CharacterCreationFinalizationService(view, queries, prerequisites, attributes,
                new CharacterCreationSkillsService(view, live),
                new CharacterCreationQualitiesService(view, live, prerequisites, attributes),
                new CharacterCreationMagicResonanceService(view, live),
                new CharacterCreationResourcesService(view, live), new CharacterCreationGearService(view, live), live);
            var prepared = service.PrepareCommit(request, out var candidate);
            if (prepared.Outcome != CharacterCreationFinalizationOutcomes.Available || candidate is null
                || prepared.Value is not { } result
                || !WorkspaceAuxiliaryStateIntegrity.IsValidShape(saved.Id, result.ContentRevision, candidate.AuxiliaryState)
                || !CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidTransition(saved.Id,
                    saved.ContentRevision, saved.SavedRevision, result.ContentRevision, saved.Document, candidate)) return false;
            replacement = candidate; receipt = result;
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException
            or UnauthorizedAccessException or System.Xml.XmlException or OverflowException or FormatException)
        { return false; }
    }

    public static CharacterCreationFinalizationResult<CharacterCreationFinalizationReceipt> Rejected(string blocker)
        => new(CharacterCreationFinalizationOutcomes.Unavailable, null, [blocker]);

    // Retains the complete actual observation, including local/imported history.
    // This view cannot mutate or recursively reacquire the storage lease.
    private sealed class ObservationStore(IWorkspaceStore admittedStore, OwnerScope owner, WorkspaceStoredDocument saved)
        : IWorkspaceStore, IWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => owner.IsLocalSingleUser
            ? admittedStore is IWorkspaceAuxiliaryStateAtomicCommitCapability { SupportsWorkspaceAuxiliaryStateAtomicCommit: true }
            : admittedStore is IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability { SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit: true };
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
            => id == saved.Id ? new(WorkspaceOperationOutcome.Success, saved) : new(WorkspaceOperationOutcome.Missing);
        public WorkspaceStoreReadResult Get(OwnerScope requestedOwner, CharacterWorkspaceId id)
            => requestedOwner == owner ? Get(id) : new(WorkspaceOperationOutcome.Missing);
        public IReadOnlyList<WorkspaceStoreEntry> List() => [];
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope requestedOwner) => [];
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) => Denied();
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope requestedOwner, WorkspaceDocument document) => Denied();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long revision, WorkspaceDocument document) => Denied();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope requestedOwner, CharacterWorkspaceId id,
            long revision, WorkspaceDocument document) => Denied();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id, long revision, string digest, WorkspaceDocument document) => Denied();
        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long revision) => Denied();
        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope requestedOwner, CharacterWorkspaceId id, long revision) => Denied();
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long revision) => Denied();
        public WorkspaceStoreMutationResult Delete(OwnerScope requestedOwner, CharacterWorkspaceId id, long revision) => Denied();
        private static WorkspaceStoreMutationResult Denied() => new(WorkspaceOperationOutcome.Unavailable);
    }
}
