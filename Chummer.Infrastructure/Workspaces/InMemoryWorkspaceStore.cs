using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed class InMemoryWorkspaceStore :
    IWorkspaceStore,
    ICharacterCreationBootstrapAtomicCreateCapability,
    IRunnerLibraryStore
{
    private const long InitialContentRevision = 1;
    private const long InitialSavedRevision = 0;
    private const int MaximumDelegatedEditAuditEntries = 4096;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WorkspaceEntry>> _documentsByOwner = new(StringComparer.Ordinal);
    private readonly object _runnerLibraryMutationGate = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryWorkspaceStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool SupportsCharacterCreationBootstrapAtomicCreate => true;

    [Obsolete("Use CreateWorkspaceDocument to receive revision metadata and typed storage outcomes.")]
    public CharacterWorkspaceId Create(WorkspaceDocument document)
    {
        WorkspaceStoreMutationResult result = CreateWorkspaceDocument(document);
        return result.Entry?.Id
            ?? throw new InvalidOperationException(result.Error ?? "Workspace could not be created.");
    }

    [Obsolete("Use CreateWorkspaceDocument to receive revision metadata and typed storage outcomes.")]
    public CharacterWorkspaceId Create(OwnerScope owner, WorkspaceDocument document)
    {
        WorkspaceStoreMutationResult result = CreateWorkspaceDocument(owner, document);
        return result.Entry?.Id
            ?? throw new InvalidOperationException(result.Error ?? "Workspace could not be created.");
    }

    public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
    {
        return CreateGeneratedWorkspaceDocument(GetLocalDocuments(), document);
    }

    public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document)
    {
        return IsInvalidScopedOwner(owner)
            ? InvalidOwnerMutation()
            : CreateGeneratedWorkspaceDocument(GetOwnerDocuments(owner), document);
    }

    public WorkspaceStoreMutationResult CreateWorkspaceDocument(
        CharacterWorkspaceId id,
        WorkspaceDocument document)
    {
        return CreateWorkspaceDocumentCore(GetLocalDocuments(), id, document);
    }

    public WorkspaceStoreMutationResult CreateWorkspaceDocument(
        OwnerScope owner,
        CharacterWorkspaceId id,
        WorkspaceDocument document)
    {
        return IsInvalidScopedOwner(owner)
            ? InvalidOwnerMutation()
            : CreateWorkspaceDocumentCore(GetOwnerDocuments(owner), id, document);
    }

    public WorkspaceStoreMutationResult CreateCharacterCreationBootstrapWorkspaceDocument(
        CharacterWorkspaceId id,
        WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return CharacterCreationBootstrapStoreIntegrity.IsValidInitialState(id, document)
            ? CreateWorkspaceDocumentCore(
                GetLocalDocuments(),
                id,
                document,
                allowBootstrapAuxiliaryState: true)
            : new WorkspaceStoreMutationResult(
                WorkspaceOperationOutcome.Unavailable,
                Error: "Character creation bootstrap state is invalid.");
    }

    private static WorkspaceStoreMutationResult CreateGeneratedWorkspaceDocument(
        ConcurrentDictionary<string, WorkspaceEntry> documents,
        WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        while (true)
        {
            CharacterWorkspaceId id = new(Guid.NewGuid().ToString("N"));
            WorkspaceStoreMutationResult created = CreateWorkspaceDocumentCore(documents, id, document);
            if (created.Success)
            {
                return created;
            }
        }
    }

    private static WorkspaceStoreMutationResult CreateWorkspaceDocumentCore(
        ConcurrentDictionary<string, WorkspaceEntry> documents,
        CharacterWorkspaceId id,
        WorkspaceDocument document,
        bool allowBootstrapAuxiliaryState = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.AuxiliaryState.IsEmpty && !allowBootstrapAuxiliaryState)
        {
            return new WorkspaceStoreMutationResult(
                WorkspaceOperationOutcome.Unavailable,
                Error: "Workspace auxiliary state can only be created by an explicit creation-authority commit.");
        }

        if (!IsSupportedWorkspaceId(id))
        {
            return new WorkspaceStoreMutationResult(
                WorkspaceOperationOutcome.Unavailable,
                Error: "Workspace id contains unsupported characters.");
        }

        while (true)
        {
            DateTimeOffset createdAtUtc = DateTimeOffset.UtcNow;
            WorkspaceEntry entry = new(
                document,
                createdAtUtc,
                InitialContentRevision,
                InitialSavedRevision,
                EmptyDelegatedEditLedger(),
                RunnerLibraryStoreStateMachine.CreateLegacy(id, createdAtUtc));
            if (documents.TryAdd(id.Value, entry))
            {
                return SuccessfulMutation(id, entry);
            }

            if (documents.TryGetValue(id.Value, out WorkspaceEntry? current))
            {
                return new WorkspaceStoreMutationResult(
                    WorkspaceOperationOutcome.Conflict,
                    ToStoreEntry(id, current),
                    "Workspace already exists.");
            }
        }
    }

    public IReadOnlyList<WorkspaceStoreEntry> List()
    {
        return ListCore(GetLocalDocuments(), OwnerScope.LocalSingleUser);
    }

    public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner)
    {
        return IsInvalidScopedOwner(owner) ? [] : ListCore(GetOwnerDocuments(owner), owner);
    }

    private static IReadOnlyList<WorkspaceStoreEntry> ListCore(
        ConcurrentDictionary<string, WorkspaceEntry> documents,
        OwnerScope owner)
    {
        return documents
            .Where(pair => IsValidWorkspaceEntry(
                owner,
                new CharacterWorkspaceId(pair.Key),
                pair.Value)
                && !IsRunnerDeleted(pair.Value))
            .OrderByDescending(pair => pair.Value.LastUpdatedUtc)
            .Select(pair => ToStoreEntry(new CharacterWorkspaceId(pair.Key), pair.Value))
            .ToArray();
    }

    [Obsolete("Use Get to distinguish typed storage outcomes.")]
    public bool TryGet(CharacterWorkspaceId id, out WorkspaceDocument document)
    {
        WorkspaceStoreReadResult result = Get(id);
        if (result.Success && result.Value is WorkspaceStoredDocument value)
        {
            document = value.Document;
            return true;
        }

        document = null!;
        return false;
    }

    [Obsolete("Use Get to distinguish typed storage outcomes.")]
    public bool TryGet(OwnerScope owner, CharacterWorkspaceId id, out WorkspaceDocument document)
    {
        WorkspaceStoreReadResult result = Get(owner, id);
        if (result.Success && result.Value is WorkspaceStoredDocument value)
        {
            document = value.Document;
            return true;
        }

        document = null!;
        return false;
    }

    public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
    {
        return GetCore(GetLocalDocuments(), OwnerScope.LocalSingleUser, id);
    }

    public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id)
    {
        return IsInvalidScopedOwner(owner)
            ? InvalidOwnerRead()
            : GetCore(GetOwnerDocuments(owner), owner, id);
    }

    private static WorkspaceStoreReadResult GetCore(
        ConcurrentDictionary<string, WorkspaceEntry> documents,
        OwnerScope owner,
        CharacterWorkspaceId id)
    {
        if (documents.TryGetValue(id.Value, out WorkspaceEntry? entry))
        {
            if (!IsValidWorkspaceEntry(owner, id, entry))
            {
                return CorruptRead();
            }

            if (IsRunnerDeleted(entry))
            {
                return new WorkspaceStoreReadResult(
                    WorkspaceOperationOutcome.Missing,
                    Error: "Workspace is in the recoverable-delete lifecycle.");
            }

            return new WorkspaceStoreReadResult(
                WorkspaceOperationOutcome.Success,
                new WorkspaceStoredDocument(
                    id,
                    entry.Document,
                    entry.ContentRevision,
                    entry.SavedRevision,
                    entry.LastUpdatedUtc));
        }

        return new WorkspaceStoreReadResult(
            WorkspaceOperationOutcome.Missing,
            Error: "Workspace not found.");
    }

    [Obsolete("Use ReplaceWorkspaceDocument with an expected content revision.")]
    public void Save(CharacterWorkspaceId id, WorkspaceDocument document)
    {
        SaveCompatibility(OwnerScope.LocalSingleUser, id, document, trustedLocalScope: true);
    }

    [Obsolete("Use ReplaceWorkspaceDocument with an expected content revision.")]
    public void Save(OwnerScope owner, CharacterWorkspaceId id, WorkspaceDocument document)
    {
        SaveCompatibility(owner, id, document, trustedLocalScope: false);
    }

    private void SaveCompatibility(
        OwnerScope owner,
        CharacterWorkspaceId id,
        WorkspaceDocument document,
        bool trustedLocalScope)
    {
        if (!trustedLocalScope && IsInvalidScopedOwner(owner))
        {
            throw new InvalidOperationException("Owner scope is invalid.");
        }

        WorkspaceStoreReadResult read = trustedLocalScope ? Get(id) : Get(owner, id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument current)
        {
            throw new InvalidOperationException(read.Error ?? "Workspace could not be read before replacement.");
        }

        WorkspaceStoreMutationResult result = trustedLocalScope
            ? ReplaceWorkspaceDocument(id, current.ContentRevision, document)
            : ReplaceWorkspaceDocument(owner, id, current.ContentRevision, document);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                result.Outcome == WorkspaceOperationOutcome.Conflict
                    ? "Workspace changed before compatibility replacement completed."
                    : result.Error ?? "Workspace could not be replaced.");
        }
    }

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document)
    {
        return ReplaceWorkspaceDocumentCore(
            GetLocalDocuments(),
            OwnerScope.LocalSingleUser,
            id,
            expectedContentRevision,
            document);
    }

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document)
    {
        return IsInvalidScopedOwner(owner)
            ? InvalidOwnerMutation()
            : ReplaceWorkspaceDocumentCore(
                GetOwnerDocuments(owner),
                owner,
                id,
                expectedContentRevision,
                document);
    }

    private static WorkspaceStoreMutationResult ReplaceWorkspaceDocumentCore(
        ConcurrentDictionary<string, WorkspaceEntry> documents,
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        while (true)
        {
            if (!documents.TryGetValue(id.Value, out WorkspaceEntry? current))
            {
                return MissingMutation();
            }

            if (!IsValidWorkspaceEntry(owner, id, current))
            {
                return CorruptMutation();
            }

            if (IsRunnerDeleted(current))
            {
                return MissingMutation();
            }

            if (current.ContentRevision != expectedContentRevision)
            {
                return ConflictMutation(id, current);
            }

            if (!HasSameAuxiliaryState(current.Document, document))
            {
                return AuxiliaryStateConflictMutation(id, current);
            }

            if (current.ContentRevision == long.MaxValue)
            {
                return new WorkspaceStoreMutationResult(
                    WorkspaceOperationOutcome.Unavailable,
                    Error: "Workspace content revision is exhausted.");
            }

            WorkspaceEntry replacement = new(
                document,
                DateTimeOffset.UtcNow,
                current.ContentRevision + 1,
                current.SavedRevision,
                current.DelegatedGmCharacterEdits,
                current.RunnerLibraryState);
            if (documents.TryUpdate(id.Value, replacement, current))
            {
                return SuccessfulMutation(id, replacement);
            }
        }
    }

    public WorkspaceStoreMutationResult SaveCheckpoint(
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        return SaveCheckpointCore(
            GetLocalDocuments(),
            OwnerScope.LocalSingleUser,
            id,
            expectedContentRevision);
    }

    public WorkspaceStoreMutationResult SaveCheckpoint(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        return IsInvalidScopedOwner(owner)
            ? InvalidOwnerMutation()
            : SaveCheckpointCore(GetOwnerDocuments(owner), owner, id, expectedContentRevision);
    }

    private static WorkspaceStoreMutationResult SaveCheckpointCore(
        ConcurrentDictionary<string, WorkspaceEntry> documents,
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        while (true)
        {
            if (!documents.TryGetValue(id.Value, out WorkspaceEntry? current))
            {
                return MissingMutation();
            }

            if (!IsValidWorkspaceEntry(owner, id, current))
            {
                return CorruptMutation();
            }

            if (IsRunnerDeleted(current))
            {
                return MissingMutation();
            }

            if (current.ContentRevision != expectedContentRevision)
            {
                return ConflictMutation(id, current);
            }

            WorkspaceEntry checkpoint = current.SavedRevision == current.ContentRevision
                ? current
                : current with
                {
                    LastUpdatedUtc = DateTimeOffset.UtcNow,
                    SavedRevision = current.ContentRevision
                };
            if (documents.TryUpdate(id.Value, checkpoint, current))
            {
                return SuccessfulMutation(id, checkpoint);
            }
        }
    }

    [Obsolete("Use Delete with an expected content revision.")]
    public bool Delete(CharacterWorkspaceId id)
    {
        WorkspaceStoreReadResult read = Get(id);
        return read.Success
               && read.Value is WorkspaceStoredDocument current
               && Delete(id, current.ContentRevision).Success;
    }

    [Obsolete("Use Delete with an expected content revision.")]
    public bool Delete(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (IsInvalidScopedOwner(owner))
        {
            return false;
        }

        WorkspaceStoreReadResult read = Get(owner, id);
        return read.Success
               && read.Value is WorkspaceStoredDocument current
               && Delete(owner, id, current.ContentRevision).Success;
    }

    public WorkspaceStoreMutationResult Delete(
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        lock (_runnerLibraryMutationGate)
        {
            return DeleteCore(
                GetLocalDocuments(),
                OwnerScope.LocalSingleUser,
                id,
                expectedContentRevision);
        }
    }

    public WorkspaceStoreMutationResult Delete(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        if (IsInvalidScopedOwner(owner))
        {
            return InvalidOwnerMutation();
        }

        lock (_runnerLibraryMutationGate)
        {
            return DeleteCore(GetOwnerDocuments(owner), owner, id, expectedContentRevision);
        }
    }

    public DelegatedGmCharacterEditStoreResult LookupDelegatedGmCharacterEdit(
        OwnerScope owner,
        CharacterWorkspaceId id,
        string idempotencyKeySha256,
        string commandSha256)
    {
        if (IsInvalidScopedOwner(owner))
        {
            return DelegatedEditUnavailable("Owner scope is invalid.");
        }

        if (!IsSha256(idempotencyKeySha256) || !IsSha256(commandSha256))
        {
            return DelegatedEditUnavailable("Delegated GM character-edit hashes are invalid.");
        }

        if (!GetOwnerDocuments(owner).TryGetValue(id.Value, out WorkspaceEntry? current))
        {
            return new DelegatedGmCharacterEditStoreResult(
                DelegatedGmCharacterEditStoreOutcome.WorkspaceMissing);
        }

        if (!IsValidWorkspaceEntry(owner, id, current))
        {
            return DelegatedEditCorrupt(current.ContentRevision);
        }

        if (IsRunnerDeleted(current))
        {
            return new DelegatedGmCharacterEditStoreResult(
                DelegatedGmCharacterEditStoreOutcome.WorkspaceMissing);
        }

        return ResolveDelegatedEditReplay(
            current,
            owner,
            id,
            idempotencyKeySha256,
            commandSha256);
    }

    public DelegatedGmCharacterEditStoreResult ApplyDelegatedGmCharacterEdit(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document,
        DelegatedGmCharacterEditLedgerEntry ledgerEntry)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(ledgerEntry);
        if (IsInvalidScopedOwner(owner))
        {
            return DelegatedEditUnavailable("Owner scope is invalid.");
        }

        if (!IsSha256(ledgerEntry.IdempotencyKeySha256)
            || !IsSha256(ledgerEntry.CommandSha256))
        {
            return DelegatedEditUnavailable("Delegated GM character-edit hashes are invalid.");
        }

        ConcurrentDictionary<string, WorkspaceEntry> documents = GetOwnerDocuments(owner);
        while (true)
        {
            if (!documents.TryGetValue(id.Value, out WorkspaceEntry? current))
            {
                return new DelegatedGmCharacterEditStoreResult(
                    DelegatedGmCharacterEditStoreOutcome.WorkspaceMissing);
            }

            if (!IsValidWorkspaceEntry(owner, id, current))
            {
                return DelegatedEditCorrupt(current.ContentRevision);
            }

            if (IsRunnerDeleted(current))
            {
                return new DelegatedGmCharacterEditStoreResult(
                    DelegatedGmCharacterEditStoreOutcome.WorkspaceMissing);
            }

            DelegatedGmCharacterEditStoreResult replay = ResolveDelegatedEditReplay(
                current,
                owner,
                id,
                ledgerEntry.IdempotencyKeySha256,
                ledgerEntry.CommandSha256);
            if (replay.Outcome != DelegatedGmCharacterEditStoreOutcome.NotFound)
            {
                return replay;
            }

            if (current.ContentRevision != expectedContentRevision)
            {
                return new DelegatedGmCharacterEditStoreResult(
                    DelegatedGmCharacterEditStoreOutcome.RevisionConflict,
                    CurrentRevision: current.ContentRevision,
                    Error: "Workspace content revision does not match the expected revision.");
            }

            if (!HasSameAuxiliaryState(current.Document, document))
            {
                return DelegatedEditUnavailable(
                    "Generic delegated editing cannot change workspace auxiliary state.");
            }

            if (current.ContentRevision == long.MaxValue
                || current.DelegatedGmCharacterEdits.Length >= MaximumDelegatedEditAuditEntries
                || !IsValidDelegatedEditLedgerEntry(owner, id, expectedContentRevision, ledgerEntry))
            {
                return DelegatedEditUnavailable("Delegated GM character-edit commit is invalid.");
            }

            long nextContentRevision = current.ContentRevision + 1;
            ImmutableArray<DelegatedGmCharacterEditLedgerEntry> updatedLedger =
                current.DelegatedGmCharacterEdits.Add(ledgerEntry);
            if (!DelegatedGmCharacterEditLedgerValidator.IsValidLedger(
                    owner,
                    id,
                    nextContentRevision,
                    updatedLedger))
            {
                return DelegatedEditUnavailable(
                    "Delegated GM character-edit commit would corrupt the immutable audit ledger.");
            }

            WorkspaceEntry replacement = new(
                document,
                DateTimeOffset.UtcNow,
                nextContentRevision,
                current.SavedRevision,
                updatedLedger,
                current.RunnerLibraryState);
            if (documents.TryUpdate(id.Value, replacement, current))
            {
                return new DelegatedGmCharacterEditStoreResult(
                    DelegatedGmCharacterEditStoreOutcome.Applied,
                    ledgerEntry.Receipt,
                    replacement.ContentRevision);
            }
        }
    }

    private static WorkspaceStoreMutationResult DeleteCore(
        ConcurrentDictionary<string, WorkspaceEntry> documents,
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        while (true)
        {
            if (!documents.TryGetValue(id.Value, out WorkspaceEntry? current))
            {
                return MissingMutation();
            }

            if (!IsValidWorkspaceEntry(owner, id, current))
            {
                return CorruptMutation();
            }

            if (IsRunnerDeleted(current))
            {
                return MissingMutation();
            }

            if (current.ContentRevision != expectedContentRevision)
            {
                return ConflictMutation(id, current);
            }

            bool removed = ((ICollection<KeyValuePair<string, WorkspaceEntry>>)documents)
                .Remove(new KeyValuePair<string, WorkspaceEntry>(id.Value, current));
            if (removed)
            {
                return SuccessfulMutation(id, current);
            }
        }
    }

    public RunnerLibraryListResult ListRunners(
        OwnerScope owner,
        RunnerLibraryListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!TryGetRunnerLibraryDocuments(owner, out ConcurrentDictionary<string, WorkspaceEntry> documents))
        {
            return RunnerLibraryInvalidList("Owner scope is invalid.");
        }

        List<RunnerLibraryItem> items = [];
        foreach ((string idValue, WorkspaceEntry entry) in documents)
        {
            CharacterWorkspaceId id = new(idValue);
            if (!IsValidWorkspaceEntry(owner, id, entry))
            {
                return new RunnerLibraryListResult(
                    RunnerLibraryOperationOutcome.Corrupt,
                    [],
                    "Runner Library state is corrupt.");
            }

            RunnerLibraryStoreState state = entry.RunnerLibraryState
                ?? RunnerLibraryStoreStateMachine.CreateLegacy(id, entry.LastUpdatedUtc);
            if (!Includes(query.Lifecycles, state.Lifecycle)
                || (query.NameContains is not null
                    && !state.DisplayName.Contains(
                        query.NameContains,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string contentDigest = RunnerLibraryCanonical.ComputeContentDigest(entry.Document);
            items.Add(RunnerLibraryStoreStateMachine.ToItem(
                id,
                state,
                entry.ContentRevision,
                entry.SavedRevision,
                contentDigest,
                entry.LastUpdatedUtc));
        }

        return new RunnerLibraryListResult(
            RunnerLibraryOperationOutcome.Success,
            items
                .OrderBy(item => item.DisplayName, StringComparer.Ordinal)
                .ThenBy(item => item.Id.Value, StringComparer.Ordinal)
                .ToArray());
    }

    public RunnerLibraryMutationResult ApplyRunnerLibraryMutation(
        OwnerScope owner,
        RunnerLibraryStoreMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (!RunnerLibraryCanonical.IsValidStoreMutation(mutation))
        {
            return RunnerLibraryInvalidMutation("Runner Library store mutation is invalid.");
        }

        if (!TryGetRunnerLibraryDocuments(owner, out ConcurrentDictionary<string, WorkspaceEntry> documents))
        {
            return RunnerLibraryInvalidMutation("Owner scope is invalid.");
        }

        lock (_runnerLibraryMutationGate)
        {
            return mutation.Kind == RunnerLibraryMutationKind.Duplicate
                ? DuplicateRunner(documents, owner, mutation)
                : MutateRunner(documents, owner, mutation);
        }
    }

    private RunnerLibraryMutationResult MutateRunner(
        ConcurrentDictionary<string, WorkspaceEntry> documents,
        OwnerScope owner,
        RunnerLibraryStoreMutation mutation)
    {
        while (true)
        {
            if (!documents.TryGetValue(mutation.RunnerId.Value, out WorkspaceEntry? current))
            {
                return RunnerLibraryMissingMutation();
            }

            if (!IsValidWorkspaceEntry(owner, mutation.RunnerId, current))
            {
                return RunnerLibraryCorruptMutation();
            }

            RunnerLibraryStoreState state = current.RunnerLibraryState
                ?? RunnerLibraryStoreStateMachine.CreateLegacy(
                    mutation.RunnerId,
                    current.LastUpdatedUtc);
            string contentDigest = RunnerLibraryCanonical.ComputeContentDigest(current.Document);
            RunnerLibraryMutationResult? replay =
                RunnerLibraryStoreStateMachine.ResolveReplayOrConflict(
                    mutation.RunnerId,
                    state,
                    mutation,
                    () => RunnerLibraryStoreStateMachine.ToItem(
                        mutation.RunnerId,
                        state,
                        current.ContentRevision,
                        current.SavedRevision,
                        contentDigest,
                        current.LastUpdatedUtc));
            if (replay is not null)
            {
                return replay;
            }

            if (current.ContentRevision != mutation.ExpectedContentRevision
                || !string.Equals(
                    contentDigest,
                    mutation.ExpectedContentDigestSha256,
                    StringComparison.Ordinal))
            {
                return new RunnerLibraryMutationResult(
                    RunnerLibraryOperationOutcome.Conflict,
                    RunnerLibraryStoreStateMachine.ToItem(
                        mutation.RunnerId,
                        state,
                        current.ContentRevision,
                        current.SavedRevision,
                        contentDigest,
                        current.LastUpdatedUtc),
                    CurrentLifecycleRevision: state.LifecycleRevision,
                    Error: "Runner content revision or digest does not match the expected snapshot.");
            }

            if (!RunnerLibraryStoreStateMachine.TryApply(
                    mutation.RunnerId,
                    state,
                    mutation,
                    current.ContentRevision,
                    contentDigest,
                    _timeProvider.GetUtcNow(),
                    out RunnerLibraryStoreState replacementState,
                    out RunnerLibraryMutationReceipt receipt,
                    out string? error))
            {
                return new RunnerLibraryMutationResult(
                    RunnerLibraryOperationOutcome.Conflict,
                    RunnerLibraryStoreStateMachine.ToItem(
                        mutation.RunnerId,
                        state,
                        current.ContentRevision,
                        current.SavedRevision,
                        contentDigest,
                        current.LastUpdatedUtc),
                    CurrentLifecycleRevision: state.LifecycleRevision,
                    Error: error);
            }

            WorkspaceEntry replacement = current with { RunnerLibraryState = replacementState };
            if (documents.TryUpdate(mutation.RunnerId.Value, replacement, current))
            {
                return new RunnerLibraryMutationResult(
                    RunnerLibraryOperationOutcome.Applied,
                    RunnerLibraryStoreStateMachine.ToItem(
                        mutation.RunnerId,
                        replacementState,
                        replacement.ContentRevision,
                        replacement.SavedRevision,
                        contentDigest,
                        replacement.LastUpdatedUtc),
                    receipt,
                    replacementState.LifecycleRevision);
            }
        }
    }

    private RunnerLibraryMutationResult DuplicateRunner(
        ConcurrentDictionary<string, WorkspaceEntry> documents,
        OwnerScope owner,
        RunnerLibraryStoreMutation mutation)
    {
        if (mutation.NewRunnerId is not CharacterWorkspaceId newRunnerId
            || mutation.DisplayName is null)
        {
            return RunnerLibraryInvalidMutation("Duplicate runner command is incomplete.");
        }

        if (!documents.TryGetValue(mutation.RunnerId.Value, out WorkspaceEntry? source))
        {
            return RunnerLibraryMissingMutation();
        }

        if (!IsValidWorkspaceEntry(owner, mutation.RunnerId, source))
        {
            return RunnerLibraryCorruptMutation();
        }

        RunnerLibraryStoreState sourceState = source.RunnerLibraryState
            ?? RunnerLibraryStoreStateMachine.CreateLegacy(
                mutation.RunnerId,
                source.LastUpdatedUtc);
        RunnerLibraryMutationLedgerEntry? sourceReplay = sourceState.MutationLedger.FirstOrDefault(
            entry => string.Equals(
                entry.IdempotencyKeyDigestSha256,
                mutation.IdempotencyKeyDigestSha256,
                StringComparison.Ordinal));
        if (sourceReplay is not null)
        {
            if (!string.Equals(
                    sourceReplay.CommandDigestSha256,
                    mutation.CommandDigestSha256,
                    StringComparison.Ordinal))
            {
                return new RunnerLibraryMutationResult(
                    RunnerLibraryOperationOutcome.Conflict,
                    CurrentLifecycleRevision: sourceState.LifecycleRevision,
                    Error: "Idempotency key was already used for a different Runner Library mutation.");
            }

            return documents.TryGetValue(newRunnerId.Value, out WorkspaceEntry? replayTarget)
                ? ResolveExistingDuplicate(owner, newRunnerId, replayTarget, mutation)
                : RunnerLibraryCorruptMutation();
        }

        string sourceContentDigest = RunnerLibraryCanonical.ComputeContentDigest(source.Document);
        if (sourceState.Lifecycle == RunnerLibraryLifecycle.Deleted
            || sourceState.LifecycleRevision != mutation.ExpectedLifecycleRevision
            || source.ContentRevision != mutation.ExpectedContentRevision
            || !string.Equals(
                sourceContentDigest,
                mutation.ExpectedContentDigestSha256,
                StringComparison.Ordinal))
        {
            return new RunnerLibraryMutationResult(
                RunnerLibraryOperationOutcome.Conflict,
                CurrentLifecycleRevision: sourceState.LifecycleRevision,
                Error: "Source runner lifecycle, content revision, or digest does not allow duplication.");
        }

        if (documents.TryGetValue(newRunnerId.Value, out WorkspaceEntry? existing))
        {
            RunnerLibraryMutationResult existingResult = ResolveExistingDuplicate(
                owner,
                newRunnerId,
                existing,
                mutation);
            if (existingResult.Outcome == RunnerLibraryOperationOutcome.Replayed
                && existingResult.Receipt is RunnerLibraryMutationReceipt existingReceipt)
            {
                return AttachDuplicateReceiptToSource(
                    documents,
                    owner,
                    mutation.RunnerId,
                    mutation,
                    existingReceipt,
                    existingResult);
            }

            return existingResult;
        }

        DateTimeOffset committedAtUtc = _timeProvider.GetUtcNow();
        if (!RunnerLibraryStoreStateMachine.TryCreateDuplicate(
                mutation.RunnerId,
                newRunnerId,
                sourceState.DisplayName,
                sourceState.Lifecycle,
                sourceState.LifecycleBeforeDelete,
                mutation.DisplayName,
                sourceState.LifecycleRevision,
                sourceState.Provenance,
                source.ContentRevision,
                sourceContentDigest,
                mutation,
                committedAtUtc,
                out RunnerLibraryStoreState duplicateState,
                out RunnerLibraryMutationReceipt receipt)
            || !RunnerLibraryStoreStateMachine.TryAddDuplicateReceipt(
                mutation.RunnerId,
                sourceState,
                receipt,
                out _))
        {
            return RunnerLibraryUnavailableMutation();
        }
        WorkspaceDocument duplicateDocument = DeepClone(source.Document);
        WorkspaceEntry duplicate = new(
            duplicateDocument,
            committedAtUtc,
            InitialContentRevision,
            InitialContentRevision,
            EmptyDelegatedEditLedger(),
            duplicateState);
        if (!documents.TryAdd(newRunnerId.Value, duplicate))
        {
            if (!documents.TryGetValue(newRunnerId.Value, out existing))
            {
                return RunnerLibraryUnavailableMutation();
            }

            RunnerLibraryMutationResult raced = ResolveExistingDuplicate(
                owner,
                newRunnerId,
                existing,
                mutation);
            return raced.Outcome == RunnerLibraryOperationOutcome.Replayed
                   && raced.Receipt is RunnerLibraryMutationReceipt racedReceipt
                ? AttachDuplicateReceiptToSource(
                    documents,
                    owner,
                    mutation.RunnerId,
                    mutation,
                    racedReceipt,
                    raced)
                : raced;
        }

        RunnerLibraryMutationResult applied = new(
            RunnerLibraryOperationOutcome.Applied,
            RunnerLibraryStoreStateMachine.ToItem(
                newRunnerId,
                duplicateState,
                duplicate.ContentRevision,
                duplicate.SavedRevision,
                sourceContentDigest,
                duplicate.LastUpdatedUtc),
            receipt,
            duplicateState.LifecycleRevision);
        return AttachDuplicateReceiptToSource(
            documents,
            owner,
            mutation.RunnerId,
            mutation,
            receipt,
            applied);
    }

    private static RunnerLibraryMutationResult AttachDuplicateReceiptToSource(
        ConcurrentDictionary<string, WorkspaceEntry> documents,
        OwnerScope owner,
        CharacterWorkspaceId sourceRunnerId,
        RunnerLibraryStoreMutation mutation,
        RunnerLibraryMutationReceipt receipt,
        RunnerLibraryMutationResult completedResult)
    {
        while (true)
        {
            if (!documents.TryGetValue(sourceRunnerId.Value, out WorkspaceEntry? source)
                || !IsValidWorkspaceEntry(owner, sourceRunnerId, source))
            {
                return RunnerLibraryCorruptMutation();
            }

            RunnerLibraryStoreState state = source.RunnerLibraryState
                ?? RunnerLibraryStoreStateMachine.CreateLegacy(
                    sourceRunnerId,
                    source.LastUpdatedUtc);
            RunnerLibraryMutationLedgerEntry? existing = state.MutationLedger.FirstOrDefault(
                entry => string.Equals(
                    entry.IdempotencyKeyDigestSha256,
                    mutation.IdempotencyKeyDigestSha256,
                    StringComparison.Ordinal));
            if (existing is not null)
            {
                return string.Equals(
                    existing.CommandDigestSha256,
                    mutation.CommandDigestSha256,
                    StringComparison.Ordinal)
                    ? completedResult
                    : new RunnerLibraryMutationResult(
                        RunnerLibraryOperationOutcome.Conflict,
                        CurrentLifecycleRevision: state.LifecycleRevision,
                        Error: "Idempotency key was already used for a different Runner Library mutation.");
            }

            if (!RunnerLibraryStoreStateMachine.TryAddDuplicateReceipt(
                    sourceRunnerId,
                    state,
                    receipt,
                    out RunnerLibraryStoreState replacementState))
            {
                return RunnerLibraryUnavailableMutation();
            }

            if (documents.TryUpdate(
                    sourceRunnerId.Value,
                    source with { RunnerLibraryState = replacementState },
                    source))
            {
                return completedResult;
            }
        }
    }

    private static RunnerLibraryMutationResult ResolveExistingDuplicate(
        OwnerScope owner,
        CharacterWorkspaceId newRunnerId,
        WorkspaceEntry existing,
        RunnerLibraryStoreMutation mutation)
    {
        if (!IsValidWorkspaceEntry(owner, newRunnerId, existing))
        {
            return RunnerLibraryCorruptMutation();
        }

        RunnerLibraryStoreState state = existing.RunnerLibraryState
            ?? RunnerLibraryStoreStateMachine.CreateLegacy(newRunnerId, existing.LastUpdatedUtc);
        string contentDigest = RunnerLibraryCanonical.ComputeContentDigest(existing.Document);
        RunnerLibraryMutationResult? replay =
            RunnerLibraryStoreStateMachine.ResolveReplayOrConflict(
                newRunnerId,
                state,
                mutation,
                () => RunnerLibraryStoreStateMachine.ToItem(
                    newRunnerId,
                    state,
                    existing.ContentRevision,
                    existing.SavedRevision,
                    contentDigest,
                    existing.LastUpdatedUtc));
        return replay ?? new RunnerLibraryMutationResult(
            RunnerLibraryOperationOutcome.Conflict,
            CurrentLifecycleRevision: state.LifecycleRevision,
            Error: "Duplicate target runner already exists.");
    }

    private static WorkspaceDocument DeepClone(WorkspaceDocument document)
    {
        byte[] auxiliaryBytes = JsonSerializer.SerializeToUtf8Bytes(document.AuxiliaryState);
        WorkspaceDocumentAuxiliaryState auxiliaryState =
            JsonSerializer.Deserialize<WorkspaceDocumentAuxiliaryState>(auxiliaryBytes)
            ?? throw new InvalidOperationException(
                "Runner auxiliary-state clone could not be materialized.");
        WorkspaceDocumentState state = new(
            document.RulesetId,
            document.SchemaVersion,
            document.PayloadKind,
            string.Concat(document.Content))
        {
            AuxiliaryState = auxiliaryState
        };
        return new WorkspaceDocument(state, document.Format);
    }

    private bool TryGetRunnerLibraryDocuments(
        OwnerScope owner,
        out ConcurrentDictionary<string, WorkspaceEntry> documents)
    {
        if (owner.IsLocalSingleUser)
        {
            documents = GetLocalDocuments();
            return true;
        }

        if (IsInvalidScopedOwner(owner))
        {
            documents = null!;
            return false;
        }

        documents = GetOwnerDocuments(owner);
        return true;
    }

    private static bool Includes(
        RunnerLibraryLifecycleFilter filter,
        RunnerLibraryLifecycle lifecycle)
    {
        RunnerLibraryLifecycleFilter flag = lifecycle switch
        {
            RunnerLibraryLifecycle.Active => RunnerLibraryLifecycleFilter.Active,
            RunnerLibraryLifecycle.Archived => RunnerLibraryLifecycleFilter.Archived,
            RunnerLibraryLifecycle.Deleted => RunnerLibraryLifecycleFilter.Deleted,
            _ => RunnerLibraryLifecycleFilter.None
        };
        return (filter & flag) != 0;
    }

    private static RunnerLibraryListResult RunnerLibraryInvalidList(string error)
    {
        return new RunnerLibraryListResult(RunnerLibraryOperationOutcome.Invalid, [], error);
    }

    private static RunnerLibraryMutationResult RunnerLibraryMissingMutation()
    {
        return new RunnerLibraryMutationResult(
            RunnerLibraryOperationOutcome.Missing,
            Error: "Runner not found.");
    }

    private static RunnerLibraryMutationResult RunnerLibraryInvalidMutation(string error)
    {
        return new RunnerLibraryMutationResult(RunnerLibraryOperationOutcome.Invalid, Error: error);
    }

    private static RunnerLibraryMutationResult RunnerLibraryCorruptMutation()
    {
        return new RunnerLibraryMutationResult(
            RunnerLibraryOperationOutcome.Corrupt,
            Error: "Runner Library state is corrupt.");
    }

    private static RunnerLibraryMutationResult RunnerLibraryUnavailableMutation()
    {
        return new RunnerLibraryMutationResult(
            RunnerLibraryOperationOutcome.Unavailable,
            Error: "Runner Library store is unavailable.");
    }

    private static WorkspaceStoreEntry ToStoreEntry(
        CharacterWorkspaceId id,
        WorkspaceEntry entry)
    {
        return new WorkspaceStoreEntry(
            id,
            entry.LastUpdatedUtc,
            entry.ContentRevision,
            entry.SavedRevision);
    }

    private static WorkspaceStoreMutationResult SuccessfulMutation(
        CharacterWorkspaceId id,
        WorkspaceEntry entry)
    {
        return new WorkspaceStoreMutationResult(
            WorkspaceOperationOutcome.Success,
            ToStoreEntry(id, entry));
    }

    private static WorkspaceStoreMutationResult ConflictMutation(
        CharacterWorkspaceId id,
        WorkspaceEntry current)
    {
        return new WorkspaceStoreMutationResult(
            WorkspaceOperationOutcome.Conflict,
            ToStoreEntry(id, current),
            "Workspace content revision does not match the expected revision.");
    }

    private static WorkspaceStoreMutationResult AuxiliaryStateConflictMutation(
        CharacterWorkspaceId id,
        WorkspaceEntry current)
    {
        return new WorkspaceStoreMutationResult(
            WorkspaceOperationOutcome.Conflict,
            ToStoreEntry(id, current),
            "Workspace auxiliary state does not match the expected authoritative state.");
    }

    private static bool HasSameAuxiliaryState(
        WorkspaceDocument current,
        WorkspaceDocument replacement)
    {
        return string.Equals(
            current.AuxiliaryStateDigest,
            replacement.AuxiliaryStateDigest,
            StringComparison.Ordinal);
    }

    private static WorkspaceStoreMutationResult MissingMutation()
    {
        return new WorkspaceStoreMutationResult(
            WorkspaceOperationOutcome.Missing,
            Error: "Workspace not found.");
    }

    private static WorkspaceStoreReadResult CorruptRead()
    {
        return new WorkspaceStoreReadResult(
            WorkspaceOperationOutcome.Corrupt,
            Error: "Workspace audit ledger is corrupt.");
    }

    private static WorkspaceStoreMutationResult CorruptMutation()
    {
        return new WorkspaceStoreMutationResult(
            WorkspaceOperationOutcome.Corrupt,
            Error: "Workspace audit ledger is corrupt.");
    }

    private static WorkspaceStoreReadResult InvalidOwnerRead()
    {
        return new WorkspaceStoreReadResult(
            WorkspaceOperationOutcome.Unavailable,
            Error: "Owner scope is invalid.");
    }

    private static WorkspaceStoreMutationResult InvalidOwnerMutation()
    {
        return new WorkspaceStoreMutationResult(
            WorkspaceOperationOutcome.Unavailable,
            Error: "Owner scope is invalid.");
    }

    private static bool IsInvalidScopedOwner(OwnerScope owner)
    {
        return string.IsNullOrWhiteSpace(owner.NormalizedValue) || owner.UsesLocalSingleUserValue;
    }

    private static DelegatedGmCharacterEditStoreResult ResolveDelegatedEditReplay(
        WorkspaceEntry current,
        OwnerScope owner,
        CharacterWorkspaceId id,
        string idempotencyKeySha256,
        string commandSha256)
    {
        DelegatedGmCharacterEditLedgerEntry? existing = current.DelegatedGmCharacterEdits
            .FirstOrDefault(entry => string.Equals(
                entry.IdempotencyKeySha256,
                idempotencyKeySha256,
                StringComparison.Ordinal));
        if (existing is null)
        {
            return new DelegatedGmCharacterEditStoreResult(
                DelegatedGmCharacterEditStoreOutcome.NotFound,
                CurrentRevision: current.ContentRevision);
        }

        if (!DelegatedGmCharacterEditLedgerValidator.IsValidPersistedEntry(
                owner,
                id,
                current.ContentRevision,
                existing))
        {
            return new DelegatedGmCharacterEditStoreResult(
                DelegatedGmCharacterEditStoreOutcome.Corrupt,
                CurrentRevision: current.ContentRevision,
                Error: "Delegated GM character-edit audit ledger is corrupt.");
        }

        return string.Equals(existing.CommandSha256, commandSha256, StringComparison.Ordinal)
            ? new DelegatedGmCharacterEditStoreResult(
                DelegatedGmCharacterEditStoreOutcome.Replayed,
                existing.Receipt,
                current.ContentRevision)
            : new DelegatedGmCharacterEditStoreResult(
                DelegatedGmCharacterEditStoreOutcome.IdempotencyConflict,
                CurrentRevision: current.ContentRevision,
                Error: "Idempotency key was already used for a different command.");
    }

    private static bool IsValidDelegatedEditLedgerEntry(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        DelegatedGmCharacterEditLedgerEntry entry)
    {
        return DelegatedGmCharacterEditLedgerValidator.IsValidForCommit(
            owner,
            id,
            expectedContentRevision,
            entry);
    }

    private static bool IsSha256(string? value)
    {
        return DelegatedGmCharacterEditLedgerValidator.IsSha256(value);
    }

    private static DelegatedGmCharacterEditStoreResult DelegatedEditUnavailable(string error)
    {
        return new DelegatedGmCharacterEditStoreResult(
            DelegatedGmCharacterEditStoreOutcome.Unavailable,
            Error: error);
    }

    private static DelegatedGmCharacterEditStoreResult DelegatedEditCorrupt(long currentRevision)
    {
        return new DelegatedGmCharacterEditStoreResult(
            DelegatedGmCharacterEditStoreOutcome.Corrupt,
            CurrentRevision: currentRevision,
            Error: "Delegated GM character-edit audit ledger is corrupt.");
    }

    private static ImmutableArray<DelegatedGmCharacterEditLedgerEntry>
        EmptyDelegatedEditLedger()
    {
        return ImmutableArray<DelegatedGmCharacterEditLedgerEntry>.Empty;
    }

    private static bool IsValidWorkspaceEntry(
        OwnerScope owner,
        CharacterWorkspaceId id,
        WorkspaceEntry entry)
    {
        return DelegatedGmCharacterEditLedgerValidator.IsValidLedger(
                   owner,
                   id,
                   entry.ContentRevision,
                   entry.DelegatedGmCharacterEdits)
               && (entry.RunnerLibraryState is null
                   || RunnerLibraryStoreStateMachine.IsValid(id, entry.RunnerLibraryState));
    }

    private static bool IsRunnerDeleted(WorkspaceEntry entry)
    {
        return entry.RunnerLibraryState?.Lifecycle == RunnerLibraryLifecycle.Deleted;
    }

    private static bool IsSupportedWorkspaceId(CharacterWorkspaceId id)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            return false;
        }

        foreach (char character in id.Value)
        {
            if (!(char.IsLetterOrDigit(character) || character is '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record WorkspaceEntry(
        WorkspaceDocument Document,
        DateTimeOffset LastUpdatedUtc,
        long ContentRevision,
        long SavedRevision,
        ImmutableArray<DelegatedGmCharacterEditLedgerEntry> DelegatedGmCharacterEdits,
        RunnerLibraryStoreState? RunnerLibraryState = null);

    private ConcurrentDictionary<string, WorkspaceEntry> GetLocalDocuments()
    {
        return _documentsByOwner.GetOrAdd(
            "\0local-single-user",
            static _ => new ConcurrentDictionary<string, WorkspaceEntry>(StringComparer.Ordinal));
    }

    private ConcurrentDictionary<string, WorkspaceEntry> GetOwnerDocuments(OwnerScope owner)
    {
        if (IsInvalidScopedOwner(owner))
        {
            throw new InvalidOperationException("Owner scope is invalid.");
        }

        return _documentsByOwner.GetOrAdd(
            "owner:" + owner.NormalizedValue,
            static _ => new ConcurrentDictionary<string, WorkspaceEntry>(StringComparer.Ordinal));
    }
}
