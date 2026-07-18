using System.Collections.Concurrent;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed class InMemoryWorkspaceStore : IWorkspaceStore
{
    private const long InitialContentRevision = 1;
    private const long InitialSavedRevision = 0;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WorkspaceEntry>> _documentsByOwner = new(StringComparer.Ordinal);

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
        WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsSupportedWorkspaceId(id))
        {
            return new WorkspaceStoreMutationResult(
                WorkspaceOperationOutcome.Unavailable,
                Error: "Workspace id contains unsupported characters.");
        }

        while (true)
        {
            WorkspaceEntry entry = new(
                document,
                DateTimeOffset.UtcNow,
                InitialContentRevision,
                InitialSavedRevision);
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
        return ListCore(GetLocalDocuments());
    }

    public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner)
    {
        return IsInvalidScopedOwner(owner) ? [] : ListCore(GetOwnerDocuments(owner));
    }

    private static IReadOnlyList<WorkspaceStoreEntry> ListCore(
        ConcurrentDictionary<string, WorkspaceEntry> documents)
    {
        return documents
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
        return GetCore(GetLocalDocuments(), id);
    }

    public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id)
    {
        return IsInvalidScopedOwner(owner)
            ? InvalidOwnerRead()
            : GetCore(GetOwnerDocuments(owner), id);
    }

    private static WorkspaceStoreReadResult GetCore(
        ConcurrentDictionary<string, WorkspaceEntry> documents,
        CharacterWorkspaceId id)
    {
        if (documents.TryGetValue(id.Value, out WorkspaceEntry? entry))
        {
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
                id,
                expectedContentRevision,
                document);
    }

    private static WorkspaceStoreMutationResult ReplaceWorkspaceDocumentCore(
        ConcurrentDictionary<string, WorkspaceEntry> documents,
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

            if (current.ContentRevision != expectedContentRevision)
            {
                return ConflictMutation(id, current);
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
                current.SavedRevision);
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
        return SaveCheckpointCore(GetLocalDocuments(), id, expectedContentRevision);
    }

    public WorkspaceStoreMutationResult SaveCheckpoint(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        return IsInvalidScopedOwner(owner)
            ? InvalidOwnerMutation()
            : SaveCheckpointCore(GetOwnerDocuments(owner), id, expectedContentRevision);
    }

    private static WorkspaceStoreMutationResult SaveCheckpointCore(
        ConcurrentDictionary<string, WorkspaceEntry> documents,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        while (true)
        {
            if (!documents.TryGetValue(id.Value, out WorkspaceEntry? current))
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
        return DeleteCore(GetLocalDocuments(), id, expectedContentRevision);
    }

    public WorkspaceStoreMutationResult Delete(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        return IsInvalidScopedOwner(owner)
            ? InvalidOwnerMutation()
            : DeleteCore(GetOwnerDocuments(owner), id, expectedContentRevision);
    }

    private static WorkspaceStoreMutationResult DeleteCore(
        ConcurrentDictionary<string, WorkspaceEntry> documents,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        while (true)
        {
            if (!documents.TryGetValue(id.Value, out WorkspaceEntry? current))
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

    private static WorkspaceStoreMutationResult MissingMutation()
    {
        return new WorkspaceStoreMutationResult(
            WorkspaceOperationOutcome.Missing,
            Error: "Workspace not found.");
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
        long SavedRevision);

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
