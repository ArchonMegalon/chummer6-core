using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;

namespace Chummer.Infrastructure.Workspaces;

public sealed class FileWorkspaceStore :
    IWorkspaceStore,
    IWorkspaceStoreReadinessProbe,
    IWorkspaceAuxiliaryStateAtomicCommitCapability,
    ICharacterCreationBootstrapAtomicCreateCapability
{
    private const int CurrentWorkspaceSchemaVersion = 1;
    private const int CurrentWorkspaceRecordSchemaVersion = 2;
    private const string WorkspacePayloadKind = "workspace";
    private const long InitialContentRevision = 1;
    private const long InitialSavedRevision = 0;
    private const long LegacyMigratedRevision = 1;
    private const string LockFileSuffix = ".lock";
    private const string TempFileMarker = ".tmp.";
    private const int FileBufferSize = 16 * 1024;
    private const int MaximumDelegatedEditAuditEntries = 4096;
    private static readonly TimeSpan DefaultWorkspaceOperationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CrossProcessLeaseRetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly object GateRegistrySync = new();
    private static readonly Dictionary<string, WorkspaceGate> GateRegistry = new(PathComparer);
    private static readonly UnixFileMode SecureDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private static readonly UnixFileMode SecureFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly byte[] ReadinessProbePayload = "chummer-workspace-readiness-v1"u8.ToArray();

    internal static int ActiveGateCount
    {
        get
        {
            lock (GateRegistrySync)
            {
                return GateRegistry.Count;
            }
        }
    }

    public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => true;

    public bool SupportsCharacterCreationBootstrapAtomicCreate => true;

    private readonly string _stateDirectory;
    private readonly IFileWorkspaceStoreFaultInjector _faultInjector;
    private readonly TimeSpan _workspaceOperationTimeout;

    public FileWorkspaceStore(string? stateDirectory = null)
        : this(stateDirectory, FileWorkspaceStoreFaultInjector.None, DefaultWorkspaceOperationTimeout)
    {
    }

    internal FileWorkspaceStore(
        string? stateDirectory,
        IFileWorkspaceStoreFaultInjector faultInjector,
        TimeSpan? workspaceOperationTimeout = null)
    {
        string configuredDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        _stateDirectory = Path.GetFullPath(configuredDirectory);
        _faultInjector = faultInjector ?? throw new ArgumentNullException(nameof(faultInjector));
        _workspaceOperationTimeout = workspaceOperationTimeout ?? DefaultWorkspaceOperationTimeout;
        if (_workspaceOperationTimeout <= TimeSpan.Zero
            || _workspaceOperationTimeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workspaceOperationTimeout),
                "The workspace operation timeout must be positive and fit in a platform wait interval.");
        }

        ValidateStaticAncestorChain(_stateDirectory);
        RejectDetectedNfsStateRoot(_stateDirectory);
        EnsureWorkspaceDirectory(OwnerScope.LocalSingleUser);
    }

    public void Probe(OwnerScope owner)
    {
        if (IsInvalidScopedOwner(owner))
        {
            throw new ArgumentException(
                "A non-local owner scope is required for workspace readiness.",
                nameof(owner));
        }

        EnsureWorkspaceDirectory(owner);
        string workspaceDirectory = Path.GetFullPath(GetWorkspaceDirectory(owner));
        string probePath = Path.GetFullPath(Path.Combine(
            workspaceDirectory,
            $".readiness-{Environment.ProcessId}-{Guid.NewGuid():N}.probe"));
        EnsurePathContained(workspaceDirectory, probePath, "workspace readiness probe");

        try
        {
            ThrowIfLinkOrReparsePoint(probePath, "workspace readiness probe");
            using (var stream = new FileStream(
                       probePath,
                       FileMode.CreateNew,
                       FileAccess.ReadWrite,
                       FileShare.None,
                       FileBufferSize,
                       FileOptions.None))
            {
                stream.Write(ReadinessProbePayload);
                stream.Flush(flushToDisk: true);
                SetSecureFileMode(probePath);
                stream.Position = 0;
                byte[] observed = new byte[ReadinessProbePayload.Length];
                stream.ReadExactly(observed);
                if (!ReadinessProbePayload.AsSpan().SequenceEqual(observed))
                {
                    throw new IOException("Workspace readiness probe verification failed.");
                }
            }

            ThrowIfLinkOrReparsePoint(probePath, "workspace readiness probe");
            File.Delete(probePath);
            if (File.Exists(probePath))
            {
                throw new IOException("Workspace readiness probe cleanup failed.");
            }
        }
        finally
        {
            if (File.Exists(probePath))
            {
                ThrowIfLinkOrReparsePoint(probePath, "workspace readiness probe");
                File.Delete(probePath);
            }
        }
    }

    [Obsolete("Use CreateWorkspaceDocument to receive revision metadata and typed storage outcomes.")]
    public CharacterWorkspaceId Create(WorkspaceDocument document)
    {
        WorkspaceStoreMutationResult result = CreateWorkspaceDocument(document);
        return result.Entry?.Id
            ?? throw new IOException(result.Error ?? "Workspace could not be created.");
    }

    [Obsolete("Use CreateWorkspaceDocument to receive revision metadata and typed storage outcomes.")]
    public CharacterWorkspaceId Create(OwnerScope owner, WorkspaceDocument document)
    {
        WorkspaceStoreMutationResult result = CreateWorkspaceDocument(owner, document);
        if (!result.Success || result.Entry is not WorkspaceStoreEntry entry)
        {
            throw new IOException(result.Error ?? "Workspace could not be created.");
        }

        return entry.Id;
    }

    public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
    {
        return CreateWorkspaceDocumentCore(
            OwnerScope.LocalSingleUser,
            new CharacterWorkspaceId(Guid.NewGuid().ToString("N")),
            document);
    }

    public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document)
    {
        return IsInvalidScopedOwner(owner)
            ? InvalidOwnerMutation()
            : CreateWorkspaceDocumentCore(
                owner,
                new CharacterWorkspaceId(Guid.NewGuid().ToString("N")),
                document);
    }

    public WorkspaceStoreMutationResult CreateWorkspaceDocument(
        CharacterWorkspaceId id,
        WorkspaceDocument document)
    {
        return CreateWorkspaceDocumentCore(OwnerScope.LocalSingleUser, id, document);
    }

    public WorkspaceStoreMutationResult CreateWorkspaceDocument(
        OwnerScope owner,
        CharacterWorkspaceId id,
        WorkspaceDocument document)
    {
        return IsInvalidScopedOwner(owner)
            ? InvalidOwnerMutation()
            : CreateWorkspaceDocumentCore(owner, id, document);
    }

    public WorkspaceStoreMutationResult CreateCharacterCreationBootstrapWorkspaceDocument(
        CharacterWorkspaceId id,
        WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return CharacterCreationBootstrapStoreIntegrity.IsValidInitialState(id, document)
            ? CreateWorkspaceDocumentCore(
                OwnerScope.LocalSingleUser,
                id,
                document,
                allowBootstrapAuxiliaryState: true)
            : UnavailableMutation("Character creation bootstrap state is invalid.");
    }

    private WorkspaceStoreMutationResult CreateWorkspaceDocumentCore(
        OwnerScope owner,
        CharacterWorkspaceId workspaceId,
        WorkspaceDocument document,
        bool allowBootstrapAuxiliaryState = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.AuxiliaryState.IsEmpty && !allowBootstrapAuxiliaryState)
        {
            return UnavailableMutation(
                "Workspace auxiliary state can only be created by an explicit creation-authority commit.");
        }

        string? path = TryGetPath(owner, workspaceId);
        if (path is null)
        {
            return UnavailableMutation("Workspace id contains unsupported characters.");
        }

        try
        {
            EnsureWorkspaceDirectory(owner);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            ThrowIfLinkOrReparsePoint(path, "workspace target");
            if (File.Exists(path))
            {
                return new WorkspaceStoreMutationResult(
                    WorkspaceOperationOutcome.Conflict,
                    Error: "Workspace already exists.");
            }

            PersistedWorkspaceRecord record = BuildPersistedRecord(
                document,
                InitialContentRevision,
                InitialSavedRevision);
            DateTimeOffset committedAtUtc = WriteRecordAtomically(
                path,
                record,
                WorkspaceWriteDisposition.CreateNew);
            return SuccessfulMutation(
                workspaceId,
                committedAtUtc,
                InitialContentRevision,
                InitialSavedRevision);
        }
        catch (IOException)
        {
            return UnavailableMutation("Workspace storage is unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            return UnavailableMutation("Workspace storage is unavailable.");
        }
    }

    public IReadOnlyList<WorkspaceStoreEntry> List()
    {
        return ListCore(OwnerScope.LocalSingleUser);
    }

    public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner)
    {
        return IsInvalidScopedOwner(owner) ? [] : ListCore(owner);
    }

    private IReadOnlyList<WorkspaceStoreEntry> ListCore(OwnerScope owner)
    {
        string workspaceDirectory = GetWorkspaceDirectory(owner);
        if (!TrySecureExistingWorkspaceDirectory(owner))
        {
            return [];
        }

        List<WorkspaceStoreEntry> entries = [];
        foreach (string path in Directory.EnumerateFiles(workspaceDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            CharacterWorkspaceId id = new(fileName);
            if (TryGetPath(owner, id) is null)
            {
                continue;
            }

            WorkspaceStoreReadResult read = GetCore(owner, id);
            if (read.Success && read.Value is WorkspaceStoredDocument value)
            {
                entries.Add(ToEntry(value));
            }
        }

        return entries
            .OrderByDescending(entry => entry.LastUpdatedUtc)
            .ToArray();
    }

    [Obsolete("Use Get to distinguish missing, corrupt, and unavailable workspace state.")]
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

    [Obsolete("Use Get to distinguish missing, corrupt, and unavailable workspace state.")]
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
        return GetCore(OwnerScope.LocalSingleUser, id);
    }

    public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id)
    {
        return IsInvalidScopedOwner(owner)
            ? InvalidOwnerRead()
            : GetCore(owner, id);
    }

    private WorkspaceStoreReadResult GetCore(OwnerScope owner, CharacterWorkspaceId id)
    {
        string? path = TryGetPath(owner, id);
        if (path is null)
        {
            return MissingRead();
        }

        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner))
            {
                return MissingRead();
            }

            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            return ReadWorkspaceUnderLease(owner, id, path);
        }
        catch (IOException)
        {
            return UnavailableRead();
        }
        catch (UnauthorizedAccessException)
        {
            return UnavailableRead();
        }
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
        ArgumentNullException.ThrowIfNull(document);
        if (!trustedLocalScope && IsInvalidScopedOwner(owner))
        {
            throw new IOException("Owner scope is invalid.");
        }

        WorkspaceStoreReadResult read = trustedLocalScope ? Get(id) : Get(owner, id);
        if (read.Outcome == WorkspaceOperationOutcome.Missing)
        {
            throw new FileNotFoundException("Workspace does not exist and cannot be replaced.");
        }

        if (!read.Success || read.Value is not WorkspaceStoredDocument current)
        {
            throw new IOException(read.Error ?? "Workspace could not be read before replacement.");
        }

        WorkspaceStoreMutationResult result = trustedLocalScope
            ? ReplaceWorkspaceDocument(id, current.ContentRevision, document)
            : ReplaceWorkspaceDocument(owner, id, current.ContentRevision, document);
        if (!result.Success)
        {
            throw result.Outcome == WorkspaceOperationOutcome.Conflict
                ? new InvalidOperationException("Workspace changed before compatibility replacement completed.")
                : new IOException(result.Error ?? "Workspace could not be replaced.");
        }
    }

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document)
    {
        return ReplaceWorkspaceDocumentCore(
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
            : ReplaceWorkspaceDocumentCore(owner, id, expectedContentRevision, document);
    }

    private WorkspaceStoreMutationResult ReplaceWorkspaceDocumentCore(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        string? path = TryGetPath(owner, id);
        if (path is null)
        {
            return new WorkspaceStoreMutationResult(WorkspaceOperationOutcome.Missing);
        }

        try
        {
            EnsureWorkspaceDirectory(owner);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            WorkspaceStoreReadResult read = ReadWorkspaceUnderLease(
                owner,
                id,
                path,
                out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> delegatedEditLedger);
            if (!read.Success || read.Value is not WorkspaceStoredDocument current)
            {
                return MutationFromRead(read);
            }

            if (current.ContentRevision != expectedContentRevision)
            {
                return ConflictMutation(current);
            }

            if (!HasSameAuxiliaryState(current.Document, document))
            {
                return AuxiliaryStateConflictMutation(current);
            }

            if (current.ContentRevision == long.MaxValue)
            {
                return UnavailableMutation("Workspace content revision is exhausted.");
            }

            long nextContentRevision = current.ContentRevision + 1;
            PersistedWorkspaceRecord record = BuildPersistedRecord(
                document,
                nextContentRevision,
                current.SavedRevision,
                delegatedEditLedger);
            DateTimeOffset committedAtUtc = WriteRecordAtomically(
                path,
                record,
                WorkspaceWriteDisposition.ReplaceExisting);
            return SuccessfulMutation(
                id,
                committedAtUtc,
                nextContentRevision,
                current.SavedRevision);
        }
        catch (IOException)
        {
            return UnavailableMutation("Workspace storage is unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            return UnavailableMutation("Workspace storage is unavailable.");
        }
    }

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndCheckpoint(
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document)
    {
        return ReplaceWorkspaceDocumentAndCheckpointCore(
            OwnerScope.LocalSingleUser,
            id,
            expectedContentRevision,
            document);
    }

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndCheckpoint(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document)
    {
        return IsInvalidScopedOwner(owner)
            ? InvalidOwnerMutation()
            : ReplaceWorkspaceDocumentAndCheckpointCore(
                owner,
                id,
                expectedContentRevision,
                document);
    }

    private WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndCheckpointCore(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        string? path = TryGetPath(owner, id);
        if (path is null)
        {
            return new WorkspaceStoreMutationResult(WorkspaceOperationOutcome.Missing);
        }

        try
        {
            EnsureWorkspaceDirectory(owner);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            WorkspaceStoreReadResult read = ReadWorkspaceUnderLease(
                owner,
                id,
                path,
                out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> delegatedEditLedger);
            if (!read.Success || read.Value is not WorkspaceStoredDocument current)
            {
                return MutationFromRead(read);
            }

            if (current.ContentRevision != expectedContentRevision)
            {
                return ConflictMutation(current);
            }

            if (!HasSameAuxiliaryState(current.Document, document))
            {
                return AuxiliaryStateConflictMutation(current);
            }

            if (current.ContentRevision == long.MaxValue)
            {
                return UnavailableMutation("Workspace content revision is exhausted.");
            }

            long nextContentRevision = current.ContentRevision + 1;
            PersistedWorkspaceRecord record = BuildPersistedRecord(
                document,
                nextContentRevision,
                nextContentRevision,
                delegatedEditLedger);
            DateTimeOffset committedAtUtc = WriteRecordAtomically(
                path,
                record,
                WorkspaceWriteDisposition.ReplaceExisting);
            return SuccessfulMutation(
                id,
                committedAtUtc,
                nextContentRevision,
                nextContentRevision);
        }
        catch (IOException)
        {
            return UnavailableMutation("Workspace storage is unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            return UnavailableMutation("Workspace storage is unavailable.");
        }
    }

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
        CharacterWorkspaceId id,
        long expectedContentRevision,
        string expectedAuxiliaryStateDigest,
        WorkspaceDocument document)
    {
        return ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpointCore(
            OwnerScope.LocalSingleUser,
            id,
            expectedContentRevision,
            expectedAuxiliaryStateDigest,
            document);
    }

    public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        string expectedAuxiliaryStateDigest,
        WorkspaceDocument document)
    {
        return IsInvalidScopedOwner(owner)
            ? InvalidOwnerMutation()
            : ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpointCore(
                owner,
                id,
                expectedContentRevision,
                expectedAuxiliaryStateDigest,
                document);
    }

    private WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpointCore(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        string expectedAuxiliaryStateDigest,
        WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsSha256(expectedAuxiliaryStateDigest))
        {
            return UnavailableMutation("Expected workspace auxiliary-state digest is invalid.");
        }

        string? path = TryGetPath(owner, id);
        if (path is null)
        {
            return new WorkspaceStoreMutationResult(WorkspaceOperationOutcome.Missing);
        }

        try
        {
            EnsureWorkspaceDirectory(owner);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            WorkspaceStoreReadResult read = ReadWorkspaceUnderLease(
                owner,
                id,
                path,
                out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> delegatedEditLedger);
            if (!read.Success || read.Value is not WorkspaceStoredDocument current)
            {
                return MutationFromRead(read);
            }

            if (current.ContentRevision != expectedContentRevision)
            {
                return ConflictMutation(current);
            }

            if (!string.Equals(
                    current.Document.AuxiliaryStateDigest,
                    expectedAuxiliaryStateDigest,
                    StringComparison.Ordinal))
            {
                return AuxiliaryStateConflictMutation(current);
            }

            if (current.ContentRevision == long.MaxValue)
            {
                return UnavailableMutation("Workspace content revision is exhausted.");
            }

            long nextContentRevision = current.ContentRevision + 1;
            if (!IsValidAuxiliaryStateTransition(
                    id,
                    current.ContentRevision,
                    current.SavedRevision,
                    nextContentRevision,
                    current.Document.AuxiliaryState,
                    document.AuxiliaryState,
                    current.Document,
                    document))
            {
                return UnavailableMutation("Workspace auxiliary state is invalid.");
            }

            PersistedWorkspaceRecord record = BuildPersistedRecord(
                document,
                nextContentRevision,
                nextContentRevision,
                delegatedEditLedger);
            DateTimeOffset committedAtUtc = WriteRecordAtomically(
                path,
                record,
                WorkspaceWriteDisposition.ReplaceExisting);
            return SuccessfulMutation(
                id,
                committedAtUtc,
                nextContentRevision,
                nextContentRevision);
        }
        catch (IOException)
        {
            return UnavailableMutation("Workspace storage is unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            return UnavailableMutation("Workspace storage is unavailable.");
        }
    }

    public WorkspaceStoreMutationResult SaveCheckpoint(
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        return SaveCheckpointCore(OwnerScope.LocalSingleUser, id, expectedContentRevision);
    }

    public WorkspaceStoreMutationResult SaveCheckpoint(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        return IsInvalidScopedOwner(owner)
            ? InvalidOwnerMutation()
            : SaveCheckpointCore(owner, id, expectedContentRevision);
    }

    private WorkspaceStoreMutationResult SaveCheckpointCore(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        string? path = TryGetPath(owner, id);
        if (path is null)
        {
            return new WorkspaceStoreMutationResult(WorkspaceOperationOutcome.Missing);
        }

        try
        {
            EnsureWorkspaceDirectory(owner);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            WorkspaceStoreReadResult read = ReadWorkspaceUnderLease(
                owner,
                id,
                path,
                out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> delegatedEditLedger);
            if (!read.Success || read.Value is not WorkspaceStoredDocument current)
            {
                return MutationFromRead(read);
            }

            if (current.ContentRevision != expectedContentRevision)
            {
                return ConflictMutation(current);
            }

            DateTimeOffset lastUpdatedUtc = current.LastUpdatedUtc;
            if (current.SavedRevision != current.ContentRevision)
            {
                PersistedWorkspaceRecord record = BuildPersistedRecord(
                    current.Document,
                    current.ContentRevision,
                    current.ContentRevision,
                    delegatedEditLedger);
                lastUpdatedUtc = WriteRecordAtomically(
                    path,
                    record,
                    WorkspaceWriteDisposition.ReplaceExisting);
            }

            return SuccessfulMutation(
                id,
                lastUpdatedUtc,
                current.ContentRevision,
                current.ContentRevision);
        }
        catch (IOException)
        {
            return UnavailableMutation("Workspace storage is unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            return UnavailableMutation("Workspace storage is unavailable.");
        }
    }

    private DateTimeOffset WriteRecordAtomically(
        string path,
        PersistedWorkspaceRecord record,
        WorkspaceWriteDisposition disposition,
        DateTimeOffset? logicalLastUpdatedUtc = null)
    {
        string normalizedPath = Path.GetFullPath(path);
        EnsurePathContained(_stateDirectory, normalizedPath, "workspace target");
        byte[] serialized = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record));
        string tempPath = Path.GetFullPath($"{normalizedPath}{TempFileMarker}{Guid.NewGuid():N}");
        EnsurePathContained(_stateDirectory, tempPath, "workspace temporary file");
        if (!PathComparer.Equals(Path.GetDirectoryName(normalizedPath), Path.GetDirectoryName(tempPath)))
        {
            throw new IOException("The workspace temporary file must share the target directory.");
        }

        DateTimeOffset committedAtUtc = logicalLastUpdatedUtc?.ToUniversalTime()
            ?? DateTimeOffset.UtcNow;
        bool targetReplaced = false;

        try
        {
            ThrowIfLinkOrReparsePoint(tempPath, "workspace temporary file");
            using (FileStream stream = OpenNewSecureFile(
                       tempPath,
                       FileAccess.Write,
                       FileShare.None,
                       FileOptions.WriteThrough))
            {
                stream.Write(serialized);
                stream.Flush(flushToDisk: true);
            }

            ThrowIfLinkOrReparsePoint(tempPath, "workspace temporary file");
            SetSecureFileMode(tempPath);
            File.SetLastWriteTimeUtc(tempPath, committedAtUtc.UtcDateTime);
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterTempFileFlushed, normalizedPath, tempPath);

            ThrowIfLinkOrReparsePoint(normalizedPath, "workspace target");
            if (disposition == WorkspaceWriteDisposition.ReplaceExisting)
            {
                // File.Replace requires the destination to still exist, so a delete that wins the
                // workspace lease cannot be undone by a later replacement save.
                File.Replace(tempPath, normalizedPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, normalizedPath, overwrite: false);
            }

            targetReplaced = true;
            try
            {
                _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterTargetReplaced, normalizedPath, tempPath);
            }
            catch (Exception)
            {
                // The atomic rename already committed the exact, flushed record and inherited the
                // temp file's restrictive mode. A diagnostic hook cannot turn that known commit
                // into a reported failure and induce an unsafe caller retry.
            }

            // Flush(true) above persists the file contents before the atomic same-directory rename.
            // System.IO does not expose a safely portable directory fsync primitive; persistence of
            // the renamed directory entry across sudden power loss remains platform-dependent.
            return committedAtUtc;
        }
        finally
        {
            if (!targetReplaced)
            {
                DeleteRegularFileIfPresent(tempPath, "workspace temporary file");
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
        if (!read.Success || read.Value is not WorkspaceStoredDocument current)
        {
            return false;
        }

        return Delete(owner, id, current.ContentRevision).Success;
    }

    public WorkspaceStoreMutationResult Delete(
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        return DeleteCore(OwnerScope.LocalSingleUser, id, expectedContentRevision);
    }

    public WorkspaceStoreMutationResult Delete(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        return IsInvalidScopedOwner(owner)
            ? InvalidOwnerMutation()
            : DeleteCore(owner, id, expectedContentRevision);
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

        string? path = TryGetPath(owner, id);
        if (path is null)
        {
            return DelegatedEditWorkspaceMissing();
        }

        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner))
            {
                return DelegatedEditWorkspaceMissing();
            }

            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            WorkspaceStoreReadResult read = ReadWorkspaceUnderLease(
                owner,
                id,
                path,
                out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> ledger);
            if (!read.Success || read.Value is not WorkspaceStoredDocument current)
            {
                return DelegatedEditFromRead(read);
            }

            return ResolveDelegatedEditReplay(
                ledger,
                owner,
                id,
                idempotencyKeySha256,
                commandSha256,
                current.ContentRevision);
        }
        catch (IOException)
        {
            return DelegatedEditUnavailable("Workspace storage is unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            return DelegatedEditUnavailable("Workspace storage is unavailable.");
        }
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

        string? path = TryGetPath(owner, id);
        if (path is null)
        {
            return DelegatedEditWorkspaceMissing();
        }

        try
        {
            EnsureWorkspaceDirectory(owner);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            WorkspaceStoreReadResult read = ReadWorkspaceUnderLease(
                owner,
                id,
                path,
                out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> ledger);
            if (!read.Success || read.Value is not WorkspaceStoredDocument current)
            {
                return DelegatedEditFromRead(read);
            }

            DelegatedGmCharacterEditStoreResult replay = ResolveDelegatedEditReplay(
                ledger,
                owner,
                id,
                ledgerEntry.IdempotencyKeySha256,
                ledgerEntry.CommandSha256,
                current.ContentRevision);
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
                || ledger.Count >= MaximumDelegatedEditAuditEntries
                || !IsValidDelegatedEditLedgerEntry(owner, id, expectedContentRevision, ledgerEntry))
            {
                return DelegatedEditUnavailable(
                    "Delegated GM character-edit commit is invalid or its immutable audit ledger is full.");
            }

            long nextContentRevision = current.ContentRevision + 1;
            DelegatedGmCharacterEditLedgerEntry[] updatedLedger =
            [
                .. ledger,
                ledgerEntry
            ];
            if (!DelegatedGmCharacterEditLedgerValidator.IsValidLedger(
                    owner,
                    id,
                    nextContentRevision,
                    updatedLedger))
            {
                return DelegatedEditUnavailable(
                    "Delegated GM character-edit commit would corrupt the immutable audit ledger.");
            }

            PersistedWorkspaceRecord record = BuildPersistedRecord(
                document,
                nextContentRevision,
                current.SavedRevision,
                updatedLedger);
            _ = WriteRecordAtomically(
                path,
                record,
                WorkspaceWriteDisposition.ReplaceExisting);
            return new DelegatedGmCharacterEditStoreResult(
                DelegatedGmCharacterEditStoreOutcome.Applied,
                ledgerEntry.Receipt,
                nextContentRevision);
        }
        catch (IOException)
        {
            return DelegatedEditUnavailable("Workspace storage is unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            return DelegatedEditUnavailable("Workspace storage is unavailable.");
        }
    }

    private WorkspaceStoreMutationResult DeleteCore(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        string? path = TryGetPath(owner, id);
        if (path is null)
        {
            return new WorkspaceStoreMutationResult(WorkspaceOperationOutcome.Missing);
        }

        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner))
            {
                return new WorkspaceStoreMutationResult(WorkspaceOperationOutcome.Missing);
            }

            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            WorkspaceStoreReadResult read = ReadWorkspaceUnderLease(owner, id, path);
            if (!read.Success || read.Value is not WorkspaceStoredDocument current)
            {
                return MutationFromRead(read);
            }

            if (current.ContentRevision != expectedContentRevision)
            {
                return ConflictMutation(current);
            }

            File.Delete(path);
            return new WorkspaceStoreMutationResult(
                WorkspaceOperationOutcome.Success,
                ToEntry(current));
        }
        catch (IOException)
        {
            return UnavailableMutation("Workspace storage is unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            return UnavailableMutation("Workspace storage is unavailable.");
        }
    }

    private WorkspaceStoreReadResult ReadWorkspaceUnderLease(
        OwnerScope owner,
        CharacterWorkspaceId id,
        string path)
    {
        return ReadWorkspaceUnderLease(owner, id, path, out _);
    }

    private WorkspaceStoreReadResult ReadWorkspaceUnderLease(
        OwnerScope owner,
        CharacterWorkspaceId id,
        string path,
        out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> delegatedEditLedger)
    {
        delegatedEditLedger = [];
        ThrowIfLinkOrReparsePoint(path, "workspace target");
        if (!File.Exists(path))
        {
            return MissingRead();
        }

        SetSecureFileMode(path);
        DateTimeOffset persistedLastUpdatedUtc = new(
            File.GetLastWriteTimeUtc(path),
            TimeSpan.Zero);
        PersistedWorkspaceRecord? record;
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize,
                FileOptions.SequentialScan);
            record = JsonSerializer.Deserialize<PersistedWorkspaceRecord>(stream);
        }
        catch (JsonException)
        {
            return CorruptRead();
        }

        if (record is null
            || !TryMaterializeRecord(
                record,
                out WorkspaceDocument document,
                out long contentRevision,
                out long savedRevision,
                out bool requiresLegacyMigration)
            || !TryMaterializeDelegatedEditLedger(
                owner,
                id,
                contentRevision,
                record.DelegatedGmCharacterEdits,
                out delegatedEditLedger)
            || !IsValidAuxiliaryState(id, contentRevision, document.AuxiliaryState))
        {
            return CorruptRead();
        }

        DateTimeOffset? migratedAtUtc = null;
        if (requiresLegacyMigration)
        {
            // Records without revisions predate dirty-state tracking and are treated as an
            // existing checkpoint at revision 1. Version-1 records retain their existing
            // revisions while being rewritten into the typed auxiliary-state envelope.
            PersistedWorkspaceRecord migrated = BuildPersistedRecord(
                document,
                contentRevision,
                savedRevision,
                delegatedEditLedger);
            migratedAtUtc = WriteRecordAtomically(
                path,
                migrated,
                WorkspaceWriteDisposition.ReplaceExisting,
                persistedLastUpdatedUtc);
        }

        DateTimeOffset lastUpdatedUtc = migratedAtUtc ?? persistedLastUpdatedUtc;
        return new WorkspaceStoreReadResult(
            WorkspaceOperationOutcome.Success,
            new WorkspaceStoredDocument(
                id,
                document,
                contentRevision,
                savedRevision,
                lastUpdatedUtc));
    }

    private static bool TryMaterializeRecord(
        PersistedWorkspaceRecord record,
        out WorkspaceDocument document,
        out long contentRevision,
        out long savedRevision,
        out bool requiresLegacyMigration)
    {
        if (record.RecordSchemaVersion is int recordSchemaVersion
            && (recordSchemaVersion < 1 || recordSchemaVersion > CurrentWorkspaceRecordSchemaVersion))
        {
            document = null!;
            contentRevision = 0;
            savedRevision = 0;
            requiresLegacyMigration = false;
            return false;
        }

        bool revisionsRequireMigration = record.ContentRevision is null && record.SavedRevision is null;
        bool recordEnvelopeRequiresMigration = record.RecordSchemaVersion is null
                                               || record.RecordSchemaVersion < CurrentWorkspaceRecordSchemaVersion;
        requiresLegacyMigration = revisionsRequireMigration || recordEnvelopeRequiresMigration;
        if (recordEnvelopeRequiresMigration && record.AuxiliaryState is not null)
        {
            document = null!;
            contentRevision = 0;
            savedRevision = 0;
            return false;
        }

        if (revisionsRequireMigration)
        {
            contentRevision = LegacyMigratedRevision;
            savedRevision = LegacyMigratedRevision;
        }
        else if (record.ContentRevision is not long persistedContentRevision
                 || record.SavedRevision is not long persistedSavedRevision
                 || persistedContentRevision < InitialContentRevision
                 || persistedSavedRevision < InitialSavedRevision
                 || persistedSavedRevision > persistedContentRevision)
        {
            document = null!;
            contentRevision = 0;
            savedRevision = 0;
            return false;
        }
        else
        {
            contentRevision = persistedContentRevision;
            savedRevision = persistedSavedRevision;
        }

        string? content = ResolveContent(record);
        if (string.IsNullOrWhiteSpace(content))
        {
            document = null!;
            return false;
        }

        if (!TryParseFormat(record.Format, out WorkspaceDocumentFormat format))
        {
            document = null!;
            return false;
        }

        string rulesetId = ResolveRulesetId(record, content);
        if (string.IsNullOrWhiteSpace(rulesetId))
        {
            document = null!;
            return false;
        }

        WorkspaceDocumentState state = ResolveState(record, content, rulesetId);
        document = new WorkspaceDocument(state, format);
        return true;
    }

    private static PersistedWorkspaceRecord BuildPersistedRecord(
        WorkspaceDocument document,
        long contentRevision,
        long savedRevision,
        IReadOnlyList<DelegatedGmCharacterEditLedgerEntry>? delegatedEditLedger = null)
    {
        return new PersistedWorkspaceRecord(document.Format.ToString())
        {
            RecordSchemaVersion = CurrentWorkspaceRecordSchemaVersion,
            Envelope = NormalizeEnvelope(document.State),
            ContentRevision = contentRevision,
            SavedRevision = savedRevision,
            AuxiliaryState = document.AuxiliaryState.IsEmpty
                ? null
                : document.AuxiliaryState,
            DelegatedGmCharacterEdits = delegatedEditLedger is { Count: > 0 }
                ? delegatedEditLedger.ToArray()
                : null
        };
    }

    private static bool TryMaterializeDelegatedEditLedger(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long currentContentRevision,
        DelegatedGmCharacterEditLedgerEntry[]? persisted,
        out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> ledger)
    {
        ledger = [];
        if (persisted is null)
        {
            return true;
        }

        if (persisted.Length > MaximumDelegatedEditAuditEntries)
        {
            return false;
        }

        if (!DelegatedGmCharacterEditLedgerValidator.IsValidLedger(
                owner,
                id,
                currentContentRevision,
                persisted))
        {
            return false;
        }

        ledger = persisted;
        return true;
    }

    private static WorkspaceStoreEntry ToEntry(WorkspaceStoredDocument document)
    {
        return new WorkspaceStoreEntry(
            document.Id,
            document.LastUpdatedUtc,
            document.ContentRevision,
            document.SavedRevision);
    }

    private static WorkspaceStoreMutationResult SuccessfulMutation(
        CharacterWorkspaceId id,
        DateTimeOffset lastUpdatedUtc,
        long contentRevision,
        long savedRevision)
    {
        return new WorkspaceStoreMutationResult(
            WorkspaceOperationOutcome.Success,
            new WorkspaceStoreEntry(
                id,
                lastUpdatedUtc,
                contentRevision,
                savedRevision));
    }

    private static WorkspaceStoreMutationResult ConflictMutation(WorkspaceStoredDocument current)
    {
        return new WorkspaceStoreMutationResult(
            WorkspaceOperationOutcome.Conflict,
            ToEntry(current),
            "Workspace content revision does not match the expected revision.");
    }

    private static WorkspaceStoreMutationResult AuxiliaryStateConflictMutation(
        WorkspaceStoredDocument current)
    {
        return new WorkspaceStoreMutationResult(
            WorkspaceOperationOutcome.Conflict,
            ToEntry(current),
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

    private static bool IsValidAuxiliaryState(
        CharacterWorkspaceId workspaceId,
        long currentContentRevision,
        WorkspaceDocumentAuxiliaryState state)
    {
        CharacterCreationFoundationDraftLedger? draft = state.CharacterCreationFoundationDraft;
        bool foundationValid = draft is null || string.Equals(
                   draft.Schema,
                   CharacterCreationFoundationSchemas.DraftLedgerV1,
                   StringComparison.Ordinal)
               && draft.WorkspaceId == workspaceId
               && draft.DraftRevision > 0
               && draft.BaseContentRevision > 0
               && draft.BaseContentRevision < currentContentRevision
               && IsFoundationSha256(draft.BaseRawCharacterXmlDigest)
               && IsFoundationSha256(draft.SourceDigest)
               && !string.IsNullOrWhiteSpace(draft.RequestedMetatype)
               && draft.Selection is not null
               && !string.IsNullOrWhiteSpace(draft.Selection.ModuleId)
               && draft.RequirementEvaluations is not null
               && draft.ProjectedEffects is not null
               && draft.FollowUpValues is not null
               && draft.SourceAnchorIds is not null
               && string.Equals(
                   draft.CompilationStatus,
                   CharacterCreationFoundationDraftStatuses.PendingFinalization,
                   StringComparison.Ordinal)
               && !draft.CharacterEffectsApplied
               && IsFoundationSha256(draft.DraftDigest);
        CharacterCreationPrerequisiteDraft? prerequisite =
            state.CharacterCreationPrerequisiteDraft;
        bool prerequisiteValid = prerequisite is null
            || string.Equals(
                prerequisite.Schema,
                CharacterCreationPrerequisiteSchemas.DraftV1,
                StringComparison.Ordinal)
            && prerequisite.WorkspaceId == workspaceId
            && prerequisite.DraftRevision > 0
            && prerequisite.BaseContentRevision > 0
            && prerequisite.BaseContentRevision < currentContentRevision
            && IsFoundationSha256(prerequisite.BaseRawCharacterXmlDigest)
            && IsFoundationSha256(prerequisite.AuthorityDigest)
            && (prerequisite.BuildMethod is CharacterCreationBuildMethods.Priority
                or CharacterCreationBuildMethods.SumToTen)
            && !string.IsNullOrWhiteSpace(prerequisite.SettingsProfileId)
            && !string.IsNullOrWhiteSpace(prerequisite.PriorityTable)
            && prerequisite.PriorityArray is { Count: 5 }
            && prerequisite.Assignments is { Count: 5 }
            && prerequisite.HeritageSelection is not null
            && prerequisite.TalentSelection is not null
            && prerequisite.EffectiveNormalAttributePoints >= 0
            && prerequisite.TotalSpecialAttributePoints >= 0
            && prerequisite.CreationKarmaTotal >= 0
            && prerequisite.CreationKarmaUsed >= 0
            && prerequisite.CreationKarmaUsed <= prerequisite.CreationKarmaTotal
            && prerequisite.SourceAnchorIds is not null
            && IsFoundationSha256(prerequisite.DraftDigest);
        CharacterCreationAttributesDraft? attributes = state.CharacterCreationAttributesDraft;
        bool attributesValid = attributes is null
            || string.Equals(attributes.Schema, CharacterCreationAttributesSchemas.DraftV1, StringComparison.Ordinal)
            && attributes.WorkspaceId == workspaceId
            && attributes.DraftRevision > 0
            && attributes.BaseContentRevision > 0
            && attributes.BaseContentRevision < currentContentRevision
            && IsFoundationSha256(attributes.BaseRawCharacterXmlDigest)
            && attributes.PrerequisiteDraftRevision > 0
            && IsFoundationSha256(attributes.PrerequisiteDraftDigest)
            && IsFoundationSha256(attributes.PrerequisiteAuthorityDigest)
            && Guid.TryParseExact(attributes.MetatypeSourceId, "D", out Guid metatypeSourceId)
            && metatypeSourceId != Guid.Empty
            && IsFoundationSha256(attributes.MetatypeSourceNodeDigest)
            && attributes.NormalPointTotal >= 0
            && attributes.NormalPointUsed >= 0
            && attributes.NormalPointUsed <= attributes.NormalPointTotal
            && attributes.SpecialPointTotal >= 0
            && attributes.SpecialPointUsed >= 0
            && attributes.SpecialPointUsed <= attributes.SpecialPointTotal
            && attributes.CreationKarmaTotal >= 0
            && attributes.CreationKarmaUsed >= 0
            && attributes.CreationKarmaUsed <= attributes.CreationKarmaTotal
            && attributes.Allocations is not null
            && attributes.Attributes is not null
            && attributes.SourceAnchorIds is { Count: > 0 }
            && !attributes.CharacterEffectsApplied
            && IsFoundationSha256(attributes.DraftDigest);
        CharacterCreationSkillsDraft? skills = state.CharacterCreationSkillsDraft;
        IReadOnlyList<CharacterCreationSkillsReceipt>? skillReceipts = state.CharacterCreationSkillsReceipts;
        bool skillsValid = skills is null
            ? skillReceipts is null
            : skillReceipts is { Count: > 0 }
              && CharacterCreationSkillsDigest.IsCanonical(skills.DraftDigest)
              && CharacterCreationSkillsDraftIntegrity.IsValidReceiptLedger(
                  skillReceipts,
                  workspaceId,
                  currentContentRevision)
              && skillReceipts[^1].DraftRevision == skills.DraftRevision
              && CharacterCreationSkillsDigest.EqualsFixedTime(skillReceipts[^1].DraftDigest, skills.DraftDigest)
              && CharacterCreationSkillsDigest.EqualsFixedTime(
                  skillReceipts[^1].IdempotencyKeyDigest,
                  skills.LastIdempotencyKeyDigest)
              && CharacterCreationSkillsDigest.EqualsFixedTime(
                  skillReceipts[^1].PreviewDigest,
                  skills.LastPreviewDigest)
              && CharacterCreationSkillsDigest.EqualsFixedTime(
                  skillReceipts[^1].CommandDigest,
                  skills.LastCommandDigest)
              && CharacterCreationSkillsDigest.EqualsFixedTime(
                  skillReceipts[^1].SkillsAuthorityDigest,
                  skills.SkillsAuthorityDigest)
              && CharacterCreationSkillsDigest.EqualsFixedTime(
                  skillReceipts[^1].RuntimeDigest,
                  skills.RuntimeDigest);
        CharacterCreationMagicResonanceDraft? magicResonance =
            state.CharacterCreationMagicResonanceDraft;
        IReadOnlyList<CharacterCreationMagicResonanceReceipt>? magicResonanceReceipts =
            state.CharacterCreationMagicResonanceReceipts;
        bool magicResonanceValid = magicResonance is null
            ? magicResonanceReceipts is null
            : magicResonanceReceipts is { Count: > 0 }
              && CharacterCreationMagicResonanceDigest.IsCanonical(magicResonance.DraftDigest)
              && CharacterCreationMagicResonanceDraftIntegrity.IsValidReceiptLedger(
                  magicResonanceReceipts,
                  workspaceId,
                  currentContentRevision)
              && magicResonanceReceipts[^1].DraftRevision == magicResonance.DraftRevision
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].DraftDigest, magicResonance.DraftDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].IdempotencyKeyDigest,
                  magicResonance.LastIdempotencyKeyDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].PreviewDigest,
                  magicResonance.LastPreviewDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].CommandDigest,
                  magicResonance.LastCommandDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].AuthorityDigest,
                  magicResonance.AuthorityDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].SourceInputsDigest,
                  magicResonance.SourceInputsDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].CustomDataInputsDigest,
                  magicResonance.CustomDataInputsDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].GmPolicyDigest,
                  magicResonance.GmPolicyDigest)
              && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                  magicResonanceReceipts[^1].RuntimeDigest,
                  magicResonance.RuntimeDigest)
              && magicResonanceReceipts[^1].AdeptPowerPointsRemaining
                  == magicResonance.AdeptPowerPointBudget.Remaining
              && magicResonanceReceipts[^1].SpellsRemaining
                  == magicResonance.SpellBudget.Remaining
              && magicResonanceReceipts[^1].ComplexFormsRemaining
                  == magicResonance.ComplexFormBudget.Remaining;
        IReadOnlyList<CharacterCreationContactReceiptLedgerEntry>? contactReceipts =
            state.CharacterCreationContactReceipts;
        bool contactReceiptsValid = contactReceipts is null
            || CharacterCreationContactReceiptLedgerIntegrity.IsValidLedger(
                workspaceId,
                currentContentRevision,
                contactReceipts);
        IReadOnlyList<CharacterCreationLifestyleReceiptLedgerEntry>? lifestyleReceipts =
            state.CharacterCreationLifestyleReceipts;
        bool lifestyleReceiptsValid = lifestyleReceipts is null
            || CharacterCreationLifestyleReceiptLedgerIntegrity.IsValidLedger(
                workspaceId,
                currentContentRevision,
                lifestyleReceipts);
        CharacterCreationResourcesDraft? resourcesDraft =
            state.CharacterCreationResourcesDraft;
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry>? resourcesReceipts =
            state.CharacterCreationResourcesReceipts;
        bool resourcesValid = CharacterCreationResourcesReceiptLedgerIntegrity.IsValidLedger(
            workspaceId,
            currentContentRevision,
            resourcesDraft,
            resourcesReceipts);
        CharacterCreationGearDraft? gearDraft = state.CharacterCreationGearDraft;
        IReadOnlyList<CharacterCreationGearReceiptLedgerEntry>? gearReceipts =
            state.CharacterCreationGearReceipts;
        bool gearValid = CharacterCreationGearReceiptLedgerIntegrity.IsValidLedger(
            workspaceId,
            currentContentRevision,
            gearDraft,
            gearReceipts);
        IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry>? afterRunReceipts =
            state.CharacterAfterRunSettlementReceipts;
        bool afterRunReceiptsValid = afterRunReceipts is null
            || CharacterAfterRunSettlementReceiptLedgerIntegrity.IsValidLedger(
                workspaceId,
                currentContentRevision,
                afterRunReceipts);
        CharacterCreationBootstrapBinding? bootstrap =
            state.CharacterCreationBootstrapBinding;
        bool bootstrapValid = bootstrap is null
            || CharacterCreationBootstrapStoreIntegrity.IsValidBinding(workspaceId, bootstrap);
        return foundationValid
               && prerequisiteValid
               && attributesValid
               && skillsValid
               && magicResonanceValid
               && contactReceiptsValid
               && lifestyleReceiptsValid
               && resourcesValid
               && gearValid
               && afterRunReceiptsValid
               && bootstrapValid;
    }

    private static bool IsValidAuxiliaryStateTransition(
        CharacterWorkspaceId workspaceId,
        long previousContentRevision,
        long previousSavedRevision,
        long nextContentRevision,
        WorkspaceDocumentAuxiliaryState currentState,
        WorkspaceDocumentAuxiliaryState replacementState,
        WorkspaceDocument currentDocument,
        WorkspaceDocument replacementDocument)
    {
        if (!IsValidAuxiliaryState(workspaceId, nextContentRevision, replacementState))
        {
            return false;
        }

        CharacterCreationFoundationDraftLedger? replacementFoundation =
            replacementState.CharacterCreationFoundationDraft;
        CharacterCreationFoundationDraftLedger? currentFoundation =
            currentState.CharacterCreationFoundationDraft;
        CharacterCreationPrerequisiteDraft? replacementPrerequisite =
            replacementState.CharacterCreationPrerequisiteDraft;
        CharacterCreationPrerequisiteDraft? currentPrerequisite =
            currentState.CharacterCreationPrerequisiteDraft;
        CharacterCreationAttributesDraft? replacementAttributes =
            replacementState.CharacterCreationAttributesDraft;
        CharacterCreationAttributesDraft? currentAttributes =
            currentState.CharacterCreationAttributesDraft;
        CharacterCreationSkillsDraft? replacementSkills =
            replacementState.CharacterCreationSkillsDraft;
        CharacterCreationSkillsDraft? currentSkills =
            currentState.CharacterCreationSkillsDraft;
        IReadOnlyList<CharacterCreationSkillsReceipt>? replacementSkillReceipts =
            replacementState.CharacterCreationSkillsReceipts;
        IReadOnlyList<CharacterCreationSkillsReceipt>? currentSkillReceipts =
            currentState.CharacterCreationSkillsReceipts;
        CharacterCreationMagicResonanceDraft? replacementMagicResonance =
            replacementState.CharacterCreationMagicResonanceDraft;
        CharacterCreationMagicResonanceDraft? currentMagicResonance =
            currentState.CharacterCreationMagicResonanceDraft;
        IReadOnlyList<CharacterCreationMagicResonanceReceipt>? replacementMagicResonanceReceipts =
            replacementState.CharacterCreationMagicResonanceReceipts;
        IReadOnlyList<CharacterCreationMagicResonanceReceipt>? currentMagicResonanceReceipts =
            currentState.CharacterCreationMagicResonanceReceipts;
        IReadOnlyList<CharacterCreationContactReceiptLedgerEntry>? replacementContactReceipts =
            replacementState.CharacterCreationContactReceipts;
        IReadOnlyList<CharacterCreationContactReceiptLedgerEntry>? currentContactReceipts =
            currentState.CharacterCreationContactReceipts;
        IReadOnlyList<CharacterCreationLifestyleReceiptLedgerEntry>? replacementLifestyleReceipts =
            replacementState.CharacterCreationLifestyleReceipts;
        IReadOnlyList<CharacterCreationLifestyleReceiptLedgerEntry>? currentLifestyleReceipts =
            currentState.CharacterCreationLifestyleReceipts;
        CharacterCreationResourcesDraft? replacementResourcesDraft =
            replacementState.CharacterCreationResourcesDraft;
        CharacterCreationResourcesDraft? currentResourcesDraft =
            currentState.CharacterCreationResourcesDraft;
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry>? replacementResourcesReceipts =
            replacementState.CharacterCreationResourcesReceipts;
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry>? currentResourcesReceipts =
            currentState.CharacterCreationResourcesReceipts;
        CharacterCreationGearDraft? replacementGearDraft =
            replacementState.CharacterCreationGearDraft;
        CharacterCreationGearDraft? currentGearDraft =
            currentState.CharacterCreationGearDraft;
        IReadOnlyList<CharacterCreationGearReceiptLedgerEntry>? replacementGearReceipts =
            replacementState.CharacterCreationGearReceipts;
        IReadOnlyList<CharacterCreationGearReceiptLedgerEntry>? currentGearReceipts =
            currentState.CharacterCreationGearReceipts;
        CharacterCreationQualitiesDraft? replacementQualities =
            replacementState.CharacterCreationQualitiesDraft;
        CharacterCreationQualitiesDraft? currentQualities =
            currentState.CharacterCreationQualitiesDraft;
        IReadOnlyList<CharacterCreationQualitiesDraftReceipt>? replacementQualityReceipts =
            replacementState.CharacterCreationQualitiesReceipts;
        IReadOnlyList<CharacterCreationQualitiesDraftReceipt>? currentQualityReceipts =
            currentState.CharacterCreationQualitiesReceipts;
        IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry>? replacementAfterRunReceipts =
            replacementState.CharacterAfterRunSettlementReceipts;
        IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry>? currentAfterRunReceipts =
            currentState.CharacterAfterRunSettlementReceipts;
        CharacterCreationBootstrapBinding? replacementBootstrap =
            replacementState.CharacterCreationBootstrapBinding;
        CharacterCreationBootstrapBinding? currentBootstrap =
            currentState.CharacterCreationBootstrapBinding;

        bool foundationUnchanged = HasSameFoundationDraft(
            currentFoundation,
            replacementFoundation);
        bool prerequisiteUnchanged = HasSamePrerequisiteDraft(
            currentPrerequisite,
            replacementPrerequisite);
        bool attributesUnchanged = HasSameAttributesDraft(
            currentAttributes,
            replacementAttributes);
        bool skillsUnchanged = HasSameSkillsLane(
            currentSkills,
            currentSkillReceipts,
            replacementSkills,
            replacementSkillReceipts);
        bool magicResonanceUnchanged = HasSameMagicResonanceLane(
            currentMagicResonance,
            currentMagicResonanceReceipts,
            replacementMagicResonance,
            replacementMagicResonanceReceipts);
        bool contactReceiptsUnchanged = HasSameContactReceiptLedger(
            currentContactReceipts,
            replacementContactReceipts);
        bool lifestyleReceiptsUnchanged = HasSameLifestyleReceiptLedger(
            currentLifestyleReceipts,
            replacementLifestyleReceipts);
        bool resourcesUnchanged = HasSameResourcesLane(
            currentResourcesDraft,
            currentResourcesReceipts,
            replacementResourcesDraft,
            replacementResourcesReceipts);
        bool gearUnchanged = HasSameGearLane(
            currentGearDraft,
            currentGearReceipts,
            replacementGearDraft,
            replacementGearReceipts);
        bool qualitiesUnchanged = HasSameQualitiesLane(
            currentQualities,
            currentQualityReceipts,
            replacementQualities,
            replacementQualityReceipts);
        bool afterRunReceiptsUnchanged = HasSameAfterRunReceiptLedger(
            currentAfterRunReceipts,
            replacementAfterRunReceipts);
        bool bootstrapUnchanged = HasSameBootstrapBinding(
            currentBootstrap,
            replacementBootstrap);
        int changedLaneCount = (foundationUnchanged ? 0 : 1)
                               + (prerequisiteUnchanged ? 0 : 1)
                               + (attributesUnchanged ? 0 : 1)
                               + (skillsUnchanged ? 0 : 1)
                               + (magicResonanceUnchanged ? 0 : 1)
                               + (contactReceiptsUnchanged ? 0 : 1)
                               + (lifestyleReceiptsUnchanged ? 0 : 1)
                               + (resourcesUnchanged ? 0 : 1)
                               + (gearUnchanged ? 0 : 1)
                               + (qualitiesUnchanged ? 0 : 1)
                               + (afterRunReceiptsUnchanged ? 0 : 1)
                               + (bootstrapUnchanged ? 0 : 1);
        if (changedLaneCount != 1)
        {
            // The authority must advance exactly one typed lane. This prevents
            // a caller from smuggling a sibling draft change into the same CAS.
            return false;
        }

        if (!foundationUnchanged)
        {
            return IsValidFoundationTransition(
                currentFoundation,
                replacementFoundation,
                previousContentRevision);
        }
        if (!prerequisiteUnchanged)
        {
            return IsValidPrerequisiteTransition(
                currentPrerequisite,
                replacementPrerequisite,
                previousContentRevision);
        }
        if (!attributesUnchanged)
        {
            return IsValidAttributesTransition(
                currentAttributes,
                replacementAttributes,
                previousContentRevision);
        }
        if (!skillsUnchanged)
        {
            return IsValidSkillsTransition(
                workspaceId,
                currentSkills,
                currentSkillReceipts,
                replacementSkills,
                replacementSkillReceipts,
                previousContentRevision,
                nextContentRevision);
        }
        if (!magicResonanceUnchanged)
        {
            return IsValidMagicResonanceTransition(
                workspaceId,
                currentMagicResonance,
                currentMagicResonanceReceipts,
                replacementMagicResonance,
                replacementMagicResonanceReceipts,
                previousContentRevision,
                nextContentRevision);
        }
        if (!bootstrapUnchanged)
        {
            return IsValidBootstrapTransition(
                currentBootstrap,
                replacementBootstrap,
                currentDocument,
                replacementDocument);
        }
        if (!qualitiesUnchanged)
        {
            // Qualities owns its own typed draft transition. This store must still
            // count and preserve that sibling lane so another authority cannot
            // smuggle a quality mutation into an After Run CAS.
            return false;
        }
        if (!afterRunReceiptsUnchanged)
        {
            return CharacterAfterRunSettlementReceiptLedgerIntegrity
                .IsValidAppendTransition(
                    workspaceId,
                    previousContentRevision,
                    nextContentRevision,
                    currentAfterRunReceipts,
                    replacementAfterRunReceipts,
                    currentDocument.Content,
                    replacementDocument.Content);
        }
        if (!lifestyleReceiptsUnchanged)
        {
            return CharacterCreationLifestyleReceiptLedgerIntegrity.IsValidAppendTransition(
                       workspaceId,
                       previousContentRevision,
                       previousSavedRevision,
                       nextContentRevision,
                       currentLifestyleReceipts,
                       replacementLifestyleReceipts)
                   && replacementLifestyleReceipts is { Count: > 0 }
                   && CharacterCreationLifestyleReceiptLedgerIntegrity.HasValidContentTransition(
                       replacementLifestyleReceipts[^1],
                       currentDocument,
                       replacementDocument);
        }
        if (!resourcesUnchanged)
        {
            return CharacterCreationResourcesReceiptLedgerIntegrity.IsValidAppendTransition(
                workspaceId,
                previousContentRevision,
                previousSavedRevision,
                nextContentRevision,
                currentResourcesDraft,
                currentResourcesReceipts,
                replacementResourcesDraft,
                replacementResourcesReceipts,
                currentDocument,
                replacementDocument);
        }
        if (!gearUnchanged)
        {
            return CharacterCreationGearReceiptLedgerIntegrity.IsValidAppendTransition(
                workspaceId,
                previousContentRevision,
                previousSavedRevision,
                nextContentRevision,
                currentGearDraft,
                currentGearReceipts,
                replacementGearDraft,
                replacementGearReceipts,
                currentDocument,
                replacementDocument);
        }
        return CharacterCreationContactReceiptLedgerIntegrity.IsValidAppendTransition(
                   workspaceId,
                   previousContentRevision,
                   previousSavedRevision,
                   nextContentRevision,
                   currentContactReceipts,
                   replacementContactReceipts)
               && replacementContactReceipts is { Count: > 0 }
               && CharacterCreationContactReceiptLedgerIntegrity.HasValidContentTransition(
                   replacementContactReceipts[^1],
                   currentDocument,
                   replacementDocument);
    }

    private static bool IsValidBootstrapTransition(
        CharacterCreationBootstrapBinding? current,
        CharacterCreationBootstrapBinding? replacement,
        WorkspaceDocument currentDocument,
        WorkspaceDocument replacementDocument)
    {
        _ = current;
        _ = replacement;
        _ = currentDocument;
        _ = replacementDocument;
        // Clearing the bootstrap binding must be part of the future resolver-bound
        // metatype/finalization authority. A generic auxiliary-state CAS cannot prove
        // the selected source row or all required creation effects, so it fails closed.
        return false;
    }

    private static bool IsValidFoundationTransition(
        CharacterCreationFoundationDraftLedger? current,
        CharacterCreationFoundationDraftLedger? replacement,
        long previousContentRevision)
    {
        if (replacement is null)
            return current is not null;
        return current is null
            ? replacement.DraftRevision == 1
              && replacement.BaseContentRevision == previousContentRevision
            : current.DraftRevision < long.MaxValue
              && replacement.DraftRevision == current.DraftRevision + 1
              && replacement.BaseContentRevision == previousContentRevision;
    }

    private static bool IsValidPrerequisiteTransition(
        CharacterCreationPrerequisiteDraft? current,
        CharacterCreationPrerequisiteDraft? replacement,
        long previousContentRevision)
    {
        if (replacement is null)
            return current is not null;
        return current is null
            ? replacement.DraftRevision == 1
              && replacement.BaseContentRevision == previousContentRevision
            : current.DraftRevision < long.MaxValue
              && replacement.DraftRevision == current.DraftRevision + 1
              && replacement.BaseContentRevision == previousContentRevision;
    }

    private static bool IsValidAttributesTransition(
        CharacterCreationAttributesDraft? current,
        CharacterCreationAttributesDraft? replacement,
        long previousContentRevision)
    {
        if (replacement is null)
            return current is not null;
        return current is null
            ? replacement.DraftRevision == 1
              && replacement.BaseContentRevision == previousContentRevision
            : current.DraftRevision < long.MaxValue
              && replacement.DraftRevision == current.DraftRevision + 1
              && replacement.BaseContentRevision == previousContentRevision;
    }

    private static bool IsValidSkillsTransition(
        CharacterWorkspaceId workspaceId,
        CharacterCreationSkillsDraft? current,
        IReadOnlyList<CharacterCreationSkillsReceipt>? currentReceipts,
        CharacterCreationSkillsDraft? replacement,
        IReadOnlyList<CharacterCreationSkillsReceipt>? replacementReceipts,
        long previousContentRevision,
        long nextContentRevision)
    {
        if (replacement is null
            || replacementReceipts is null
            || nextContentRevision != previousContentRevision + 1
            || replacement.BaseContentRevision != previousContentRevision
            || replacement.DraftRevision != (current?.DraftRevision + 1 ?? 1)
            || replacementReceipts.Count != (currentReceipts?.Count ?? 0) + 1)
            return false;
        if (currentReceipts is not null
            && !currentReceipts.Zip(replacementReceipts.Take(currentReceipts.Count))
                .All(pair => CharacterCreationSkillsDigest.EqualsFixedTime(
                    pair.First.ReceiptDigest,
                    pair.Second.ReceiptDigest)))
            return false;
        CharacterCreationSkillsReceipt receipt = replacementReceipts[^1];
        return receipt.WorkspaceId == workspaceId
               && receipt.PreviousContentRevision == previousContentRevision
               && receipt.ContentRevision == nextContentRevision
               && receipt.DraftRevision == replacement.DraftRevision
               && CharacterCreationSkillsDigest.EqualsFixedTime(receipt.DraftDigest, replacement.DraftDigest)
               && CharacterCreationSkillsDigest.EqualsFixedTime(
                   receipt.IdempotencyKeyDigest,
                   replacement.LastIdempotencyKeyDigest)
               && CharacterCreationSkillsDigest.EqualsFixedTime(
                   receipt.PreviewDigest,
                   replacement.LastPreviewDigest)
               && CharacterCreationSkillsDigest.EqualsFixedTime(
                   receipt.CommandDigest,
                   replacement.LastCommandDigest)
               && CharacterCreationSkillsDigest.EqualsFixedTime(
                   receipt.PreviousReceiptDigest,
                   currentReceipts is { Count: > 0 }
                       ? currentReceipts[^1].ReceiptDigest
                       : CharacterCreationSkillsDigest.ReceiptLedgerRootDigest)
               && receipt.ActivePointsRemaining == replacement.ActivePointTotal - replacement.ActivePointUsed
               && receipt.SkillGroupPointsRemaining == replacement.SkillGroupPointTotal - replacement.SkillGroupPointUsed
               && receipt.KnowledgePointsRemaining == replacement.KnowledgePointTotal - replacement.KnowledgePointUsed
               && receipt.KnowledgePointOverflowToActive == replacement.KnowledgePointOverflowToActive
               && (currentReceipts is null
                   || currentReceipts.All(existing => !CharacterCreationSkillsDigest.EqualsFixedTime(
                       existing.IdempotencyKeyDigest,
                       receipt.IdempotencyKeyDigest)));
    }

    private static bool IsValidMagicResonanceTransition(
        CharacterWorkspaceId workspaceId,
        CharacterCreationMagicResonanceDraft? current,
        IReadOnlyList<CharacterCreationMagicResonanceReceipt>? currentReceipts,
        CharacterCreationMagicResonanceDraft? replacement,
        IReadOnlyList<CharacterCreationMagicResonanceReceipt>? replacementReceipts,
        long previousContentRevision,
        long nextContentRevision)
    {
        if (replacement is null
            || replacementReceipts is null
            || nextContentRevision != previousContentRevision + 1
            || replacement.BaseContentRevision != previousContentRevision
            || replacement.DraftRevision != (current?.DraftRevision + 1 ?? 1)
            || replacementReceipts.Count != (currentReceipts?.Count ?? 0) + 1)
            return false;
        if (currentReceipts is not null
            && !currentReceipts.Zip(replacementReceipts.Take(currentReceipts.Count))
                .All(pair => CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                    pair.First.ReceiptDigest,
                    pair.Second.ReceiptDigest)))
            return false;
        CharacterCreationMagicResonanceReceipt receipt = replacementReceipts[^1];
        return receipt.WorkspaceId == workspaceId
               && receipt.PreviousContentRevision == previousContentRevision
               && receipt.ContentRevision == nextContentRevision
               && receipt.DraftRevision == replacement.DraftRevision
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.DraftDigest, replacement.DraftDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.IdempotencyKeyDigest, replacement.LastIdempotencyKeyDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.PreviewDigest, replacement.LastPreviewDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.CommandDigest, replacement.LastCommandDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.PreviousReceiptDigest,
                   currentReceipts is { Count: > 0 }
                       ? currentReceipts[^1].ReceiptDigest
                       : CharacterCreationMagicResonanceDigest.ReceiptLedgerRootDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.AuthorityDigest, replacement.AuthorityDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.SourceInputsDigest, replacement.SourceInputsDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.CustomDataInputsDigest, replacement.CustomDataInputsDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.GmPolicyDigest, replacement.GmPolicyDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.RuntimeDigest, replacement.RuntimeDigest)
               && receipt.TalentKind == replacement.TalentKind
               && receipt.AdeptPowerPointsRemaining == replacement.AdeptPowerPointBudget.Remaining
               && receipt.SpellsRemaining == replacement.SpellBudget.Remaining
               && receipt.ComplexFormsRemaining == replacement.ComplexFormBudget.Remaining
               && !receipt.CharacterDocumentChanged
               && (currentReceipts is null
                   || currentReceipts.All(existing =>
                       !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                           existing.IdempotencyKeyDigest,
                           receipt.IdempotencyKeyDigest)));
    }

    private static bool HasSameFoundationDraft(
        CharacterCreationFoundationDraftLedger? left,
        CharacterCreationFoundationDraftLedger? right) =>
        string.Equals(
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(left)),
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(right)),
            StringComparison.Ordinal);

    private static bool HasSamePrerequisiteDraft(
        CharacterCreationPrerequisiteDraft? left,
        CharacterCreationPrerequisiteDraft? right) =>
        string.Equals(
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationFoundationDraft: null,
                    CharacterCreationPrerequisiteDraft: left)),
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationFoundationDraft: null,
                    CharacterCreationPrerequisiteDraft: right)),
            StringComparison.Ordinal);

    private static bool HasSameAttributesDraft(
        CharacterCreationAttributesDraft? left,
        CharacterCreationAttributesDraft? right) =>
        string.Equals(
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationFoundationDraft: null,
                    CharacterCreationPrerequisiteDraft: null,
                    CharacterCreationAttributesDraft: left)),
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationFoundationDraft: null,
                    CharacterCreationPrerequisiteDraft: null,
                    CharacterCreationAttributesDraft: right)),
            StringComparison.Ordinal);

    private static bool HasSameSkillsLane(
        CharacterCreationSkillsDraft? leftDraft,
        IReadOnlyList<CharacterCreationSkillsReceipt>? leftReceipts,
        CharacterCreationSkillsDraft? rightDraft,
        IReadOnlyList<CharacterCreationSkillsReceipt>? rightReceipts) =>
        string.Equals(
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationSkillsDraft: leftDraft,
                    CharacterCreationSkillsReceipts: leftReceipts)),
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationSkillsDraft: rightDraft,
                    CharacterCreationSkillsReceipts: rightReceipts)),
            StringComparison.Ordinal);

    private static bool HasSameMagicResonanceLane(
        CharacterCreationMagicResonanceDraft? leftDraft,
        IReadOnlyList<CharacterCreationMagicResonanceReceipt>? leftReceipts,
        CharacterCreationMagicResonanceDraft? rightDraft,
        IReadOnlyList<CharacterCreationMagicResonanceReceipt>? rightReceipts) =>
        string.Equals(
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationMagicResonanceDraft: leftDraft,
                    CharacterCreationMagicResonanceReceipts: leftReceipts)),
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationMagicResonanceDraft: rightDraft,
                    CharacterCreationMagicResonanceReceipts: rightReceipts)),
            StringComparison.Ordinal);

    private static bool HasSameContactReceiptLedger(
        IReadOnlyList<CharacterCreationContactReceiptLedgerEntry>? left,
        IReadOnlyList<CharacterCreationContactReceiptLedgerEntry>? right) =>
        string.Equals(
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationContactReceipts: left)),
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationContactReceipts: right)),
            StringComparison.Ordinal);

    private static bool HasSameLifestyleReceiptLedger(
        IReadOnlyList<CharacterCreationLifestyleReceiptLedgerEntry>? left,
        IReadOnlyList<CharacterCreationLifestyleReceiptLedgerEntry>? right) =>
        string.Equals(
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationLifestyleReceipts: left)),
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationLifestyleReceipts: right)),
            StringComparison.Ordinal);

    private static bool HasSameResourcesLane(
        CharacterCreationResourcesDraft? leftDraft,
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry>? leftReceipts,
        CharacterCreationResourcesDraft? rightDraft,
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry>? rightReceipts) =>
        string.Equals(
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationResourcesDraft: leftDraft,
                    CharacterCreationResourcesReceipts: leftReceipts)),
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationResourcesDraft: rightDraft,
                    CharacterCreationResourcesReceipts: rightReceipts)),
            StringComparison.Ordinal);

    private static bool HasSameGearLane(
        CharacterCreationGearDraft? leftDraft,
        IReadOnlyList<CharacterCreationGearReceiptLedgerEntry>? leftReceipts,
        CharacterCreationGearDraft? rightDraft,
        IReadOnlyList<CharacterCreationGearReceiptLedgerEntry>? rightReceipts) =>
        string.Equals(
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationGearDraft: leftDraft,
                    CharacterCreationGearReceipts: leftReceipts)),
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationGearDraft: rightDraft,
                    CharacterCreationGearReceipts: rightReceipts)),
            StringComparison.Ordinal);

    private static bool HasSameQualitiesLane(
        CharacterCreationQualitiesDraft? leftDraft,
        IReadOnlyList<CharacterCreationQualitiesDraftReceipt>? leftReceipts,
        CharacterCreationQualitiesDraft? rightDraft,
        IReadOnlyList<CharacterCreationQualitiesDraftReceipt>? rightReceipts) =>
        string.Equals(
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationQualitiesDraft: leftDraft,
                    CharacterCreationQualitiesReceipts: leftReceipts)),
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationQualitiesDraft: rightDraft,
                    CharacterCreationQualitiesReceipts: rightReceipts)),
            StringComparison.Ordinal);

    private static bool HasSameAfterRunReceiptLedger(
        IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry>? left,
        IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry>? right) =>
        string.Equals(
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterAfterRunSettlementReceipts: left)),
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterAfterRunSettlementReceipts: right)),
            StringComparison.Ordinal);

    private static bool HasSameBootstrapBinding(
        CharacterCreationBootstrapBinding? left,
        CharacterCreationBootstrapBinding? right) =>
        string.Equals(
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationBootstrapBinding: left)),
            WorkspaceDocumentAuxiliaryStateDigest.Compute(
                new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationBootstrapBinding: right)),
            StringComparison.Ordinal);

    private static bool IsFoundationSha256(string? value)
    {
        const string prefix = "sha256:";
        return value is { Length: 71 }
               && value.StartsWith(prefix, StringComparison.Ordinal)
               && IsSha256(value[prefix.Length..]);
    }

    private static DelegatedGmCharacterEditStoreResult ResolveDelegatedEditReplay(
        IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> ledger,
        OwnerScope owner,
        CharacterWorkspaceId id,
        string idempotencyKeySha256,
        string commandSha256,
        long currentRevision)
    {
        DelegatedGmCharacterEditLedgerEntry? existing = ledger.FirstOrDefault(entry =>
            string.Equals(
                entry.IdempotencyKeySha256,
                idempotencyKeySha256,
                StringComparison.Ordinal));
        if (existing is null)
        {
            return new DelegatedGmCharacterEditStoreResult(
                DelegatedGmCharacterEditStoreOutcome.NotFound,
                CurrentRevision: currentRevision);
        }

        if (!DelegatedGmCharacterEditLedgerValidator.IsValidPersistedEntry(
                owner,
                id,
                currentRevision,
                existing))
        {
            return new DelegatedGmCharacterEditStoreResult(
                DelegatedGmCharacterEditStoreOutcome.Corrupt,
                CurrentRevision: currentRevision,
                Error: "Delegated GM character-edit audit ledger is corrupt.");
        }

        return string.Equals(existing.CommandSha256, commandSha256, StringComparison.Ordinal)
            ? new DelegatedGmCharacterEditStoreResult(
                DelegatedGmCharacterEditStoreOutcome.Replayed,
                existing.Receipt,
                currentRevision)
            : new DelegatedGmCharacterEditStoreResult(
                DelegatedGmCharacterEditStoreOutcome.IdempotencyConflict,
                CurrentRevision: currentRevision,
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

    private static DelegatedGmCharacterEditStoreResult DelegatedEditFromRead(
        WorkspaceStoreReadResult read)
    {
        return read.Outcome switch
        {
            WorkspaceOperationOutcome.Missing => DelegatedEditWorkspaceMissing(),
            WorkspaceOperationOutcome.Corrupt => new DelegatedGmCharacterEditStoreResult(
                DelegatedGmCharacterEditStoreOutcome.Corrupt,
                Error: read.Error),
            _ => DelegatedEditUnavailable(read.Error ?? "Workspace storage is unavailable.")
        };
    }

    private static DelegatedGmCharacterEditStoreResult DelegatedEditWorkspaceMissing()
    {
        return new DelegatedGmCharacterEditStoreResult(
            DelegatedGmCharacterEditStoreOutcome.WorkspaceMissing,
            Error: "Workspace not found.");
    }

    private static DelegatedGmCharacterEditStoreResult DelegatedEditUnavailable(string error)
    {
        return new DelegatedGmCharacterEditStoreResult(
            DelegatedGmCharacterEditStoreOutcome.Unavailable,
            Error: error);
    }

    private static WorkspaceStoreMutationResult MutationFromRead(WorkspaceStoreReadResult read)
    {
        return new WorkspaceStoreMutationResult(read.Outcome, Error: read.Error);
    }

    private static WorkspaceStoreReadResult MissingRead()
    {
        return new WorkspaceStoreReadResult(
            WorkspaceOperationOutcome.Missing,
            Error: "Workspace not found.");
    }

    private static WorkspaceStoreReadResult CorruptRead()
    {
        return new WorkspaceStoreReadResult(
            WorkspaceOperationOutcome.Corrupt,
            Error: "Workspace data is corrupt.");
    }

    private static WorkspaceStoreReadResult UnavailableRead()
    {
        return new WorkspaceStoreReadResult(
            WorkspaceOperationOutcome.Unavailable,
            Error: "Workspace storage is unavailable.");
    }

    private static WorkspaceStoreMutationResult UnavailableMutation(string error)
    {
        return new WorkspaceStoreMutationResult(
            WorkspaceOperationOutcome.Unavailable,
            Error: error);
    }

    private static WorkspaceStoreReadResult InvalidOwnerRead()
    {
        return new WorkspaceStoreReadResult(
            WorkspaceOperationOutcome.Unavailable,
            Error: "Owner scope is invalid.");
    }

    private static WorkspaceStoreMutationResult InvalidOwnerMutation()
    {
        return UnavailableMutation("Owner scope is invalid.");
    }

    private static bool IsInvalidScopedOwner(OwnerScope owner)
    {
        return string.IsNullOrWhiteSpace(owner.NormalizedValue) || owner.UsesLocalSingleUserValue;
    }

    private string? TryGetPath(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
            return null;

        foreach (char character in id.Value)
        {
            if (!(char.IsLetterOrDigit(character) || character is '-' or '_'))
                return null;
        }

        string workspaceDirectory = Path.GetFullPath(GetWorkspaceDirectory(owner));
        string candidate = Path.GetFullPath(Path.Combine(workspaceDirectory, $"{id.Value}.json"));
        return IsPathContained(workspaceDirectory, candidate) ? candidate : null;
    }

    private string GetWorkspaceDirectory(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveWorkspaceOwnerDirectory(_stateDirectory, owner);
        return Path.Combine(ownerDirectory, "workspaces");
    }

    private void EnsureWorkspaceDirectory(OwnerScope owner)
    {
        ValidateStaticAncestorChain(_stateDirectory);
        EnsureSecureDirectory(_stateDirectory, "workspace state root");
        VerifyPrivateStateRoot(_stateDirectory);
        string ownerDirectory = EnsureOwnerDirectory(owner);

        EnsureSecureDirectory(Path.Combine(ownerDirectory, "workspaces"), "workspace directory");
    }

    private bool TrySecureExistingWorkspaceDirectory(OwnerScope owner)
    {
        ThrowIfLinkOrReparsePoint(_stateDirectory, "workspace state root");
        if (!Directory.Exists(_stateDirectory))
            return false;
        SetSecureDirectoryMode(_stateDirectory);

        string? ownerDirectory = TrySecureExistingOwnerDirectory(owner);
        if (ownerDirectory is null)
            return false;

        return TrySecureExistingDirectory(
            Path.Combine(ownerDirectory, "workspaces"),
            "workspace directory");
    }

    private string EnsureOwnerDirectory(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveWorkspaceOwnerDirectory(_stateDirectory, owner);
        if (PathComparer.Equals(ownerDirectory, _stateDirectory))
        {
            return ownerDirectory;
        }

        string ownersDirectory = Path.GetFullPath(Path.Combine(_stateDirectory, "owners"));
        EnsurePathContained(_stateDirectory, ownersDirectory, "workspace owners directory");
        EnsureSecureDirectory(ownersDirectory, "workspace owners directory");
        using WorkspaceOperationLease migration = AcquireOwnerMigrationOperation(ownerDirectory);
        MigrateLegacyWorkspaceDirectoryUnderLease(owner, ownerDirectory);
        EnsureSecureDirectory(ownerDirectory, "workspace owner directory");
        return ownerDirectory;
    }

    private string? TrySecureExistingOwnerDirectory(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveWorkspaceOwnerDirectory(_stateDirectory, owner);
        if (PathComparer.Equals(ownerDirectory, _stateDirectory))
        {
            return ownerDirectory;
        }

        string ownersDirectory = Path.GetFullPath(Path.Combine(_stateDirectory, "owners"));
        EnsurePathContained(_stateDirectory, ownersDirectory, "workspace owners directory");
        if (!TrySecureExistingDirectory(ownersDirectory, "workspace owners directory"))
        {
            return null;
        }

        using WorkspaceOperationLease migration = AcquireOwnerMigrationOperation(ownerDirectory);
        MigrateLegacyWorkspaceDirectoryUnderLease(owner, ownerDirectory);
        return TrySecureExistingDirectory(ownerDirectory, "workspace owner directory")
            ? ownerDirectory
            : null;
    }

    private WorkspaceOperationLease AcquireOwnerMigrationOperation(string ownerDirectory)
    {
        string migrationKey = Path.GetFullPath(ownerDirectory + ".owner-migration");
        EnsurePathContained(
            Path.Combine(_stateDirectory, "owners"),
            migrationKey,
            "workspace owner migration key");
        return AcquireWorkspaceOperation(migrationKey);
    }

    private void MigrateLegacyWorkspaceDirectoryUnderLease(OwnerScope owner, string ownerDirectory)
    {
        ThrowIfLinkOrReparsePoint(ownerDirectory, "workspace owner directory");
        bool ownerDirectoryExists = Directory.Exists(ownerDirectory);
        if (File.Exists(ownerDirectory) && !ownerDirectoryExists)
        {
            throw new IOException("The workspace owner path must be a directory.");
        }

        if (!OwnerScopedStatePath.TryResolveContainedLegacyOwnerDirectory(
                _stateDirectory,
                owner,
                out string legacyOwnerDirectory)
            || PathComparer.Equals(legacyOwnerDirectory, ownerDirectory))
        {
            return;
        }

        ThrowIfLinkOrReparsePoint(legacyOwnerDirectory, "legacy workspace owner directory");
        bool legacyDirectoryExists = Directory.Exists(legacyOwnerDirectory);
        if (File.Exists(legacyOwnerDirectory) && !legacyDirectoryExists)
        {
            throw new IOException("The legacy workspace owner path must be a directory.");
        }

        if (!legacyDirectoryExists)
        {
            return;
        }

        string legacyWorkspaceDirectory = Path.GetFullPath(
            Path.Combine(legacyOwnerDirectory, "workspaces"));
        EnsurePathContained(
            legacyOwnerDirectory,
            legacyWorkspaceDirectory,
            "legacy workspace directory");
        ThrowIfLinkOrReparsePoint(legacyWorkspaceDirectory, "legacy workspace directory");
        bool legacyWorkspaceDirectoryExists = Directory.Exists(legacyWorkspaceDirectory);
        if (File.Exists(legacyWorkspaceDirectory) && !legacyWorkspaceDirectoryExists)
        {
            throw new IOException("The legacy workspace path must be a directory.");
        }

        if (!legacyWorkspaceDirectoryExists)
        {
            return;
        }

        string workspaceDirectory = Path.GetFullPath(Path.Combine(ownerDirectory, "workspaces"));
        EnsurePathContained(ownerDirectory, workspaceDirectory, "workspace directory");
        ThrowIfLinkOrReparsePoint(workspaceDirectory, "workspace directory");
        bool workspaceDirectoryExists = Directory.Exists(workspaceDirectory);
        if (File.Exists(workspaceDirectory) && !workspaceDirectoryExists)
        {
            throw new IOException("The workspace path must be a directory.");
        }

        if (workspaceDirectoryExists)
        {
            throw new IOException(
                "Both canonical and legacy workspace directories exist; automatic migration is unsafe.");
        }

        if (!ownerDirectoryExists)
        {
            EnsureSecureDirectory(ownerDirectory, "workspace owner directory");
        }

        SetSecureDirectoryMode(legacyOwnerDirectory);
        SetSecureDirectoryMode(legacyWorkspaceDirectory);
        Directory.Move(legacyWorkspaceDirectory, workspaceDirectory);
        ThrowIfLinkOrReparsePoint(ownerDirectory, "workspace owner directory");
        SetSecureDirectoryMode(ownerDirectory);
        SetSecureDirectoryMode(workspaceDirectory);
    }

    private WorkspaceOperationLease AcquireWorkspaceOperation(string path)
    {
        string normalizedPath = Path.GetFullPath(path);
        EnsurePathContained(_stateDirectory, normalizedPath, "workspace operation target");
        Stopwatch deadline = Stopwatch.StartNew();
        InProcessGateLease processGate = AcquireInProcessGate(
            normalizedPath,
            deadline,
            _workspaceOperationTimeout);
        try
        {
            string lockPath = Path.GetFullPath(normalizedPath + LockFileSuffix);
            EnsurePathContained(_stateDirectory, lockPath, "workspace lock file");
            FileStream fileLease = AcquireCrossProcessLease(
                lockPath,
                deadline,
                _workspaceOperationTimeout);
            try
            {
                RemoveStaleTempFiles(normalizedPath);
                return new WorkspaceOperationLease(processGate, fileLease);
            }
            catch
            {
                fileLease.Dispose();
                throw;
            }
        }
        catch
        {
            processGate.Dispose();
            throw;
        }
    }

    private static InProcessGateLease AcquireInProcessGate(
        string key,
        Stopwatch deadline,
        TimeSpan timeout)
    {
        WorkspaceGate gate;
        lock (GateRegistrySync)
        {
            if (!GateRegistry.TryGetValue(key, out gate!))
            {
                gate = new WorkspaceGate();
                GateRegistry.Add(key, gate);
            }

            gate.ReferenceCount++;
        }

        try
        {
            TimeSpan remaining = GetRemaining(deadline, timeout);
            if (remaining <= TimeSpan.Zero || !gate.Semaphore.Wait(remaining))
            {
                throw new IOException("Timed out waiting for the workspace operation lease.");
            }

            return new InProcessGateLease(key, gate);
        }
        catch
        {
            ReleaseGateReference(key, gate, releaseSemaphore: false);
            throw;
        }
    }

    private static void ReleaseGateReference(string key, WorkspaceGate gate, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            gate.Semaphore.Release();
        }

        lock (GateRegistrySync)
        {
            gate.ReferenceCount--;
            if (gate.ReferenceCount == 0)
            {
                GateRegistry.Remove(key);
                gate.Semaphore.Dispose();
            }
        }
    }

    private static FileStream AcquireCrossProcessLease(
        string lockPath,
        Stopwatch deadline,
        TimeSpan timeout)
    {
        while (true)
        {
            ThrowIfLinkOrReparsePoint(lockPath, "workspace lock file");
            try
            {
                FileStream stream = OpenSecureFile(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    FileOptions.WriteThrough);
                try
                {
                    ThrowIfLinkOrReparsePoint(lockPath, "workspace lock file");
                    SetSecureFileMode(lockPath);
                    return stream;
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
            catch (IOException exception)
            {
                TimeSpan remaining = GetRemaining(deadline, timeout);
                if (remaining <= TimeSpan.Zero)
                {
                    throw new IOException(
                        "Timed out waiting for the workspace cross-process lease.",
                        exception);
                }

                Thread.Sleep(remaining < CrossProcessLeaseRetryDelay
                    ? remaining
                    : CrossProcessLeaseRetryDelay);
            }
        }
    }

    private static TimeSpan GetRemaining(Stopwatch deadline, TimeSpan timeout)
    {
        TimeSpan remaining = timeout - deadline.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static void RemoveStaleTempFiles(string path)
    {
        string directory = Path.GetDirectoryName(path)!;
        string pattern = Path.GetFileName(path) + TempFileMarker + "*";
        foreach (string tempPath in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                     .OrderBy(candidate => candidate, PathComparer))
        {
            ThrowIfLinkOrReparsePoint(tempPath, "workspace temporary file");
            File.Delete(tempPath);
        }
    }

    private static FileStream OpenNewSecureFile(
        string path,
        FileAccess access,
        FileShare share,
        FileOptions options)
    {
        return OpenSecureFile(path, FileMode.CreateNew, access, share, options);
    }

    private static FileStream OpenSecureFile(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share,
        FileOptions options)
    {
        FileStreamOptions streamOptions = new()
        {
            Mode = mode,
            Access = access,
            Share = share,
            Options = options,
            BufferSize = FileBufferSize
        };
        if (!OperatingSystem.IsWindows())
        {
            streamOptions.UnixCreateMode = SecureFileMode;
        }

        return new FileStream(path, streamOptions);
    }

    private static void EnsureSecureDirectory(string path, string description)
    {
        ThrowIfLinkOrReparsePoint(path, description);
        if (File.Exists(path) && !Directory.Exists(path))
        {
            throw new IOException($"The {description} must be a directory.");
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, SecureDirectoryMode);
        }

        ThrowIfLinkOrReparsePoint(path, description);
        SetSecureDirectoryMode(path);
    }

    private static bool TrySecureExistingDirectory(string path, string description)
    {
        ThrowIfLinkOrReparsePoint(path, description);
        if (!Directory.Exists(path))
            return false;

        SetSecureDirectoryMode(path);
        return true;
    }

    private static void EnsurePathContained(string root, string candidate, string description)
    {
        if (!IsPathContained(root, candidate))
        {
            throw new IOException($"The {description} escapes its storage root.");
        }
    }

    private static bool IsPathContained(string root, string candidate)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string normalizedCandidate = Path.GetFullPath(candidate);
        if (PathComparer.Equals(normalizedRoot, normalizedCandidate))
        {
            return true;
        }

        string prefix = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(
            prefix,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static void ValidateStaticAncestorChain(string path)
    {
        string fullPath = Path.GetFullPath(path);

        if (OperatingSystem.IsAndroid())
        {
            // Android owns the app-data ancestors and may expose them through storage aliases that
            // System.IO reports as reparse points. The application cannot replace those ancestors;
            // its trust boundary starts at the private state root. Keep rejecting a linked state
            // root here, and keep the existing pre/post link checks for every owned descendant.
            ThrowIfLinkOrReparsePoint(fullPath, "workspace state root");
            return;
        }

        string pathRoot = Path.GetPathRoot(fullPath)
            ?? throw new IOException("The workspace state root has no filesystem root.");
        string current = pathRoot;
        ThrowIfLinkOrReparsePoint(current, "workspace filesystem root");

        string relative = Path.GetRelativePath(pathRoot, fullPath);
        foreach (string component in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            ThrowIfLinkOrReparsePoint(current, "workspace state ancestor");
        }
    }

    private static void VerifyPrivateStateRoot(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // The portable lane rejects reparse points but cannot prove an owner-only Windows ACL.
            // Deployments must provision and audit the configured root ACL outside this component.
            return;
        }

        UnixFileMode mode = File.GetUnixFileMode(path);
        UnixFileMode nonOwnerPermissions =
            UnixFileMode.GroupRead
            | UnixFileMode.GroupWrite
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherWrite
            | UnixFileMode.OtherExecute;
        if ((mode & nonOwnerPermissions) != 0)
        {
            throw new IOException("The workspace state root must be private to its owning user.");
        }
    }

    private static void RejectDetectedNfsStateRoot(string path)
    {
        // File.Replace/rename, advisory sharing, and durability semantics are not strong enough for
        // this protocol on NFS. DriveInfo is a best-effort portable detector; deployments must also
        // keep the configured root off NFS bind mounts that the runtime cannot identify.
        DriveInfo? containingDrive;
        try
        {
            containingDrive = DriveInfo.GetDrives()
                .Where(drive => drive.IsReady && IsPathContained(drive.RootDirectory.FullName, path))
                .OrderByDescending(drive => drive.RootDirectory.FullName.Length)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        string format;
        try
        {
            format = containingDrive?.DriveFormat ?? string.Empty;
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        if (format.StartsWith("nfs", StringComparison.OrdinalIgnoreCase))
        {
            throw new PlatformNotSupportedException(
                "FileWorkspaceStore does not support NFS-backed state roots.");
        }
    }

    private static void ThrowIfLinkOrReparsePoint(string path, string description)
    {
        // These portable pre/post checks reject links at each trust boundary, but System.IO does
        // not bind the path lookup and later open/rename to the same directory handle. A same-UID
        // attacker could still race a path swap between checks. Unix mode 0700 limits cross-user
        // exposure; fully closing that residual race requires a native handle-relative no-follow
        // lane (openat/O_NOFOLLOW on Unix and equivalent reparse-safe handles on Windows).
        FileSystemInfo info = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        info.Refresh();

        string? linkTarget = null;
        try
        {
            linkTarget = info.LinkTarget;
        }
        catch (FileNotFoundException)
        {
            // A genuinely absent path is safe to create. Broken links report a LinkTarget.
        }

        if (linkTarget is not null
            || (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0))
        {
            throw new IOException($"The {description} cannot be a symbolic link or reparse point.");
        }
    }

    private static void DeleteRegularFileIfPresent(string path, string description)
    {
        ThrowIfLinkOrReparsePoint(path, description);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void SetSecureDirectoryMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, SecureDirectoryMode);
        }
    }

    private static void SetSecureFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, SecureFileMode);
        }
    }

    private static bool TryParseFormat(
        string? format,
        out WorkspaceDocumentFormat parsed)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            parsed = WorkspaceDocumentFormat.NativeXml;
            return true;
        }

        return Enum.TryParse(format, ignoreCase: true, out parsed)
            && Enum.IsDefined(typeof(WorkspaceDocumentFormat), parsed);
    }

    private static string? ResolveContent(PersistedWorkspaceRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.Envelope?.Payload))
        {
            return record.Envelope.Payload;
        }

        if (!string.IsNullOrWhiteSpace(record.Content))
        {
            return record.Content;
        }

        return record.Xml;
    }

    private static string ResolveRulesetId(
        PersistedWorkspaceRecord record,
        string content)
    {
        return RulesetDefaults.NormalizeOptional(record.Envelope?.RulesetId)
            ?? RulesetDefaults.NormalizeOptional(record.RulesetId)
            ?? WorkspaceRulesetDetection.Detect(record.Envelope?.PayloadKind, content)
            ?? string.Empty;
    }

    private static WorkspaceDocumentState ResolveState(
        PersistedWorkspaceRecord record,
        string content,
        string fallbackRulesetId)
    {
        WorkspacePayloadEnvelope? envelope = record.Envelope;
        string normalizedRulesetId = RulesetDefaults.NormalizeOptional(envelope?.RulesetId)
            ?? RulesetDefaults.NormalizeOptional(fallbackRulesetId)
            ?? WorkspaceRulesetDetection.Detect(envelope?.PayloadKind, content)
            ?? string.Empty;
        int schemaVersion = envelope?.SchemaVersion is > 0
            ? envelope.SchemaVersion
            : CurrentWorkspaceSchemaVersion;
        string payloadKind = string.IsNullOrWhiteSpace(envelope?.PayloadKind)
            ? WorkspacePayloadKind
            : envelope.PayloadKind;
        return new WorkspaceDocumentState(
            rulesetId: normalizedRulesetId,
            schemaVersion: schemaVersion,
            payloadKind: payloadKind,
            payload: content)
        {
            AuxiliaryState = record.AuxiliaryState ?? WorkspaceDocumentAuxiliaryState.Empty
        };
    }

    private static WorkspacePayloadEnvelope NormalizeEnvelope(WorkspaceDocumentState state)
    {
        int schemaVersion = state.SchemaVersion > 0
            ? state.SchemaVersion
            : CurrentWorkspaceSchemaVersion;
        string payloadKind = string.IsNullOrWhiteSpace(state.PayloadKind)
            ? WorkspacePayloadKind
            : state.PayloadKind;
        return new WorkspacePayloadEnvelope(
            RulesetId: state.RulesetId,
            SchemaVersion: schemaVersion,
            PayloadKind: payloadKind,
            Payload: state.Payload);
    }

    private sealed record PersistedWorkspaceRecord(string Format)
    {
        public int? RecordSchemaVersion { get; init; }

        public WorkspacePayloadEnvelope? Envelope { get; init; }

        public long? ContentRevision { get; init; }

        public long? SavedRevision { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public WorkspaceDocumentAuxiliaryState? AuxiliaryState { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DelegatedGmCharacterEditLedgerEntry[]? DelegatedGmCharacterEdits { get; init; }

        // Backward compatibility for older persisted payloads.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Content { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RulesetId { get; init; }

        // Backward compatibility for legacy persisted payloads.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Xml { get; init; }
    }

    private enum WorkspaceWriteDisposition
    {
        CreateNew,
        ReplaceExisting
    }

    private sealed class WorkspaceGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }
    }

    private sealed class InProcessGateLease : IDisposable
    {
        private readonly string _key;
        private WorkspaceGate? _gate;

        public InProcessGateLease(string key, WorkspaceGate gate)
        {
            _key = key;
            _gate = gate;
        }

        public void Dispose()
        {
            WorkspaceGate? gate = Interlocked.Exchange(ref _gate, null);
            if (gate is not null)
            {
                ReleaseGateReference(_key, gate, releaseSemaphore: true);
            }
        }
    }

    private sealed class WorkspaceOperationLease : IDisposable
    {
        private InProcessGateLease? _processGate;
        private FileStream? _fileLease;

        public WorkspaceOperationLease(InProcessGateLease processGate, FileStream fileLease)
        {
            _processGate = processGate;
            _fileLease = fileLease;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _fileLease, null)?.Dispose();
            Interlocked.Exchange(ref _processGate, null)?.Dispose();
        }
    }
}

internal enum FileWorkspaceStoreFaultStage
{
    AfterTempFileFlushed,
    AfterTargetReplaced
}

internal interface IFileWorkspaceStoreFaultInjector
{
    void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath);
}

internal sealed class FileWorkspaceStoreFaultInjector : IFileWorkspaceStoreFaultInjector
{
    public static IFileWorkspaceStoreFaultInjector None { get; } = new FileWorkspaceStoreFaultInjector();

    private FileWorkspaceStoreFaultInjector()
    {
    }

    public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
    {
    }
}
