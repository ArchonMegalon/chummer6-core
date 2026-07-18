using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;

namespace Chummer.Infrastructure.Workspaces;

public sealed class FileWorkspaceStore : IWorkspaceStore, IWorkspaceStoreReadinessProbe
{
    private const int CurrentWorkspaceSchemaVersion = 1;
    private const string WorkspacePayloadKind = "workspace";
    private const long InitialContentRevision = 1;
    private const long InitialSavedRevision = 0;
    private const long LegacyMigratedRevision = 1;
    private const string LockFileSuffix = ".lock";
    private const string TempFileMarker = ".tmp.";
    private const int FileBufferSize = 16 * 1024;
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

    private WorkspaceStoreMutationResult CreateWorkspaceDocumentCore(
        OwnerScope owner,
        CharacterWorkspaceId workspaceId,
        WorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
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
            return ReadWorkspaceUnderLease(id, path);
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
            WorkspaceStoreReadResult read = ReadWorkspaceUnderLease(id, path);
            if (!read.Success || read.Value is not WorkspaceStoredDocument current)
            {
                return MutationFromRead(read);
            }

            if (current.ContentRevision != expectedContentRevision)
            {
                return ConflictMutation(current);
            }

            if (current.ContentRevision == long.MaxValue)
            {
                return UnavailableMutation("Workspace content revision is exhausted.");
            }

            long nextContentRevision = current.ContentRevision + 1;
            PersistedWorkspaceRecord record = BuildPersistedRecord(
                document,
                nextContentRevision,
                current.SavedRevision);
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
            WorkspaceStoreReadResult read = ReadWorkspaceUnderLease(id, path);
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
                    current.ContentRevision);
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
            WorkspaceStoreReadResult read = ReadWorkspaceUnderLease(id, path);
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
        CharacterWorkspaceId id,
        string path)
    {
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
                out bool requiresLegacyMigration))
        {
            return CorruptRead();
        }

        DateTimeOffset? migratedAtUtc = null;
        if (requiresLegacyMigration)
        {
            // Legacy records predate dirty-state tracking. They were already durable workspace
            // files, so deterministic migration treats revision 1 as an existing checkpoint.
            PersistedWorkspaceRecord migrated = BuildPersistedRecord(
                document,
                LegacyMigratedRevision,
                LegacyMigratedRevision);
            migratedAtUtc = WriteRecordAtomically(
                path,
                migrated,
                WorkspaceWriteDisposition.ReplaceExisting,
                persistedLastUpdatedUtc);
            contentRevision = LegacyMigratedRevision;
            savedRevision = LegacyMigratedRevision;
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
        requiresLegacyMigration = record.ContentRevision is null && record.SavedRevision is null;
        if (requiresLegacyMigration)
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
        long savedRevision)
    {
        return new PersistedWorkspaceRecord(document.Format.ToString())
        {
            Envelope = NormalizeEnvelope(document.State),
            ContentRevision = contentRevision,
            SavedRevision = savedRevision
        };
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
            payload: content);
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
        public WorkspacePayloadEnvelope? Envelope { get; init; }

        public long? ContentRevision { get; init; }

        public long? SavedRevision { get; init; }

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
