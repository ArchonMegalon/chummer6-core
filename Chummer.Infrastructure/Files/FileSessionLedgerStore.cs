using System.Text.Json;
using Chummer.Application.Session;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Infrastructure.Files;

/// <summary>
/// Immutable intent/result files with a process-shared exclusive writer lock. A result is acknowledged
/// only after file flush and atomic rename. Orphan temporary bytes are retained and counted, never purged.
/// The journal is local; it cannot issue a server cursor, rebase a character or evict pending operations.
/// </summary>
public sealed partial class FileSessionLedgerStore : ISessionPlayStore
{
    private const string Schema = "chummer.session.local-journal/v1";
    private readonly string _root;
    public FileSessionLedgerStore(string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        _root = Path.GetFullPath(Path.Combine(stateDirectory, "session-play-v1"));
    }
    public T Execute<T>(OwnerScope owner, string characterId, Func<ISessionPlayTransaction, T> action)
    {
        Identity(owner.NormalizedValue, "owner-required"); Identity(characterId, "character-required");
        Require(!owner.UsesLocalSingleUserValue || owner.IsLocalSingleUser, "trusted-local-owner-required");
        EnsureNoLinks(_root);
        CreatePrivateDirectory(_root);
        EnsureNoLinks(_root);
        string lockPath = Path.Combine(_root, "writer.lock");
        EnsureNoLinks(lockPath);
        using var lease = OpenPrivateFile(lockPath, FileMode.OpenOrCreate);
        string scope = Path.Combine(_root, Hash(JsonSerializer.Serialize(new[] { owner.NormalizedValue, characterId }, Json)));
        EnsureNoLinks(scope);
        if (!Directory.Exists(scope))
        {
            Require(Directory.EnumerateDirectories(_root).Count() < SessionPlayLimits.MaxCharacters, "character-storage-capacity-reached");
            CreatePrivateDirectory(scope);
        }
        return action(new Transaction(_root, scope, owner.NormalizedValue, characterId));
    }

    public T Inspect<T>(OwnerScope owner, string characterId, Func<ISessionPlayReader?, T> action)
    {
        Identity(owner.NormalizedValue, "owner-required"); Identity(characterId, "character-required");
        Require(!owner.UsesLocalSingleUserValue || owner.IsLocalSingleUser, "trusted-local-owner-required");
        EnsureNoLinks(_root);
        if (!Directory.Exists(_root)) return action(null);
        string lockPath = Path.Combine(_root, "writer.lock");
        EnsureNoLinks(lockPath);
        // Open existing lock read-only, with the same exclusive sharing as writers.
        // No mkdir, OpenOrCreate, chmod, archive repair or source capture.
        using var lease = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None);
        string scope = Path.Combine(_root, Hash(JsonSerializer.Serialize(new[] { owner.NormalizedValue, characterId }, Json)));
        EnsureNoLinks(scope);
        return action(Directory.Exists(scope) ? new Transaction(_root, scope, owner.NormalizedValue, characterId) : null);
    }

    public ISessionLedgerStore ForOwner(OwnerScope owner, string characterId) => new LedgerView(this, owner, characterId);
    public ISessionReplicaStore ReplicaForOwner(OwnerScope owner, string characterId) => new FileSessionReplicaStore(this, owner, characterId);

    internal static SessionEventEnvelope[] Events(IReadOnlyList<SessionPlayStoredOperation> operations)
        => operations.Where(o => o.Result is { Failure: null, Snapshot: not null })
            .SelectMany(o => o.Intent.Events).GroupBy(e => e.EventId, StringComparer.Ordinal)
            .Select(g => g.First()).OrderBy(e => e.Sequence).ToArray();

    private sealed class LedgerView(FileSessionLedgerStore store, OwnerScope owner, string characterId) : ISessionLedgerStore
    {
        public Task<CachedClientPayload<SessionLedger>?> GetAsync(string overlayId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var value = store.Execute(owner, characterId, tx => {
                var origin = tx.ReadOrigin();
                if (origin is null || origin.OverlayId != overlayId) return null;
                var events = Events(tx.ReadOperations());
                return new CachedClientPayload<SessionLedger>(SessionClientCacheAreas.Ledger, overlayId,
                    new(overlayId, origin.Authority.Binding.Version, events, NextSequence: events.Length + 1L),
                    DateTimeOffset.UtcNow, "local-file-journal");
            });
            return Task.FromResult(value);
        }
        public Task<CachedClientPayload<SessionLedger>> UpsertAsync(SessionLedger ledger, CancellationToken ct = default)
            => throw new NotSupportedException("Append through ISessionService with a persisted operation identity; raw replacement bypasses authority.");
        public Task RemoveAsync(string overlayId, CancellationToken ct = default)
            => throw new NotSupportedException("An only-copy journal cannot be evicted through the cache contract.");
    }

    private sealed record Record<T>(string Schema, string Digest, T Payload);
    private sealed partial class Transaction(string root, string scope, string owner, string character) : ISessionPlayTransaction
    {
        public SessionPlayOrigin? ReadOrigin()
        {
            var origin = Read<SessionPlayOrigin>("origin.json");
            if (origin is not null)
                Conflict(origin.Authority.Binding.OwnerId == owner && origin.Authority.Binding.Version.CharacterId == character
                    && origin.OverlayId == OverlayId(origin.Authority.Binding), "journal-scope-mismatch");
            return origin;
        }
        public void CreateOrigin(SessionPlayOrigin origin)
        {
            Conflict(origin.Authority.Binding.OwnerId == owner && origin.Authority.Binding.Version.CharacterId == character
                && origin.OverlayId == OverlayId(origin.Authority.Binding), "journal-scope-mismatch");
            Conflict(ReadOrigin() is null, "journal-already-created");
            Write("origin.json", origin, 0, false);
        }
        public IReadOnlyList<SessionPlayStoredOperation> ReadOperations()
        {
            var origin = ReadOrigin();
            var intents = Directory.EnumerateFiles(scope, "*.intent.json").Order(StringComparer.Ordinal).ToArray();
            Require(intents.Length <= SessionPlayLimits.MaxOperations, "journal-operation-capacity-invalid");
            var results = Directory.EnumerateFiles(scope, "*.result.json").ToArray();
            Require(results.Length <= intents.Length && results.All(r => File.Exists(r.Replace(".result.json", ".intent.json", StringComparison.Ordinal))),
                "journal-orphan-result");
            var list = new List<SessionPlayStoredOperation>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < intents.Length; i++)
            {
                long ordinal = i + 1L;
                string prefix = ordinal.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
                Conflict(Path.GetFileName(intents[i]) == prefix + ".intent.json", "journal-sequence-gap");
                var intent = Read<SessionPlayOperation>(prefix + ".intent.json")!;
                Conflict(origin is not null && intent.OverlayId == origin.OverlayId && ids.Add(intent.OperationId), "journal-intent-divergence");
                var result = Read<SessionPlayOperationResult>(prefix + ".result.json");
                Conflict(result is null || result.OperationId == intent.OperationId && result.PayloadDigest == intent.PayloadDigest,
                    "journal-result-divergence");
                Conflict(result is not null || i == intents.Length - 1, "journal-pending-predecessor");
                if (result?.Snapshot is { } snapshot)
                    Conflict(snapshot.OverlayId == origin!.OverlayId && snapshot.BaseCharacterVersion == intent.BaseCharacterVersion,
                        "journal-snapshot-divergence");
                list.Add(new(ordinal, intent, result));
            }
            return list;
        }
        public SessionPlayStoredOperation Prepare(SessionPlayOperation intent)
        {
            Conflict(ReadFocusContinuations7490().All(x => x.Receipt is not null
                && x.Intent.ExpectedReceipt.Command.Request.OperationId.ToString("D") != intent.OperationId),
                "focus-continuation-requires-original-recovery");
            Conflict(ReadConditionTransfers().All(t => t.Receipt is not null && t.Intent.Review.OperationId != intent.OperationId),
                "condition-transfer-requires-original-recovery");
            Conflict(ReadNaturalRecoveries().All(x => x.Receipt is not null && x.Intent.Command.Request.OperationId.ToString("D") != intent.OperationId),
                "natural-recovery-requires-original-recovery");
            var operations = ReadOperations();
            Require(operations.Count < SessionPlayLimits.MaxOperations, "operation-capacity-reached");
            Conflict(operations.All(o => o.Result is not null && o.Intent.OperationId != intent.OperationId), "operation-pending-or-duplicate");
            var prepared = new SessionPlayStoredOperation(operations.Count + 1L, intent, null);
            // Reserve one maximum result before the identity can become pending.
            Write(prepared.Ordinal.ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + ".intent.json",
                intent, SessionPlayLimits.MaxRecordBytes, false);
            return prepared;
        }
        public void Complete(SessionPlayStoredOperation operation, SessionPlayOperationResult result)
        {
            var current = ReadOperations().SingleOrDefault(o => o.Ordinal == operation.Ordinal);
            Conflict(current is not null && Digest(current.Intent) == Digest(operation.Intent)
                && current.Result is null && result.OperationId == current.Intent.OperationId
                && result.PayloadDigest == current.Intent.PayloadDigest, "operation-completion-divergence");
            Write(operation.Ordinal.ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + ".result.json", result, 0, true);
        }
        private T? Read<T>(string file) where T : class
        {
            string path = Path.Combine(scope, file);
            EnsureNoLinks(path);
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Require(stream.Length <= SessionPlayLimits.MaxRecordBytes, "journal-record-too-large");
            Record<T>? record;
            try { record = JsonSerializer.Deserialize<Record<T>>(stream, Json); }
            catch (JsonException) { throw new SessionPlayException(SessionPlayFailures.Conflict("journal-record-corrupt")); }
            Conflict(record is not null && record.Schema == Schema && record.Payload is not null
                && record.Digest == Digest(record.Payload), "journal-record-corrupt");
            return record!.Payload;
        }
        private void Write<T>(string file, T payload, long reserve, bool completesPending)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Record<T>(Schema, Digest(payload), payload), Json);
            Require(bytes.Length <= SessionPlayLimits.MaxRecordBytes, "journal-record-capacity-reached");
            var all = Files(root).Take(16385).ToArray();
            long pending = all.Count(f => f.EndsWith(".intent.json", StringComparison.Ordinal)
                && !File.Exists(f.Replace(".intent.json", ".result.json", StringComparison.Ordinal)));
            if (all.Length + 1 + pending + (reserve > 0 ? (int)(reserve / SessionPlayLimits.MaxRecordBytes) : 0) - (completesPending ? 1 : 0) > 16384)
                throw new SessionPlayException(SessionPlayFailures.Unavailable("journal-file-capacity-reached"));
            long ownPending = Directory.EnumerateFiles(scope, "*.intent.json").Count(f =>
                !File.Exists(f.Replace(".intent.json", ".result.json", StringComparison.Ordinal)));
            long transferPending = all.Count(f => f.EndsWith(".transfer-intent.json", StringComparison.Ordinal)
                && !File.Exists(f.Replace(".transfer-intent.json", ".transfer-ack.json", StringComparison.Ordinal)));
            long ownTransferPending = Directory.EnumerateFiles(scope, "*.transfer-intent.json").Count(f =>
                !File.Exists(f.Replace(".transfer-intent.json", ".transfer-ack.json", StringComparison.Ordinal)));
            int recoveryPending = RecoveryReservations(all);
            int ownRecoveryPending = RecoveryReservations(Directory.EnumerateFiles(scope));
            int FocusReservations(IEnumerable<string> names) => names.Where(f => f.EndsWith(".focus-intent.json", StringComparison.Ordinal)
                && !File.Exists(f.Replace(".focus-intent.json", ".focus-ack.json", StringComparison.Ordinal)))
                .Sum(f => File.Exists(f.Replace(".focus-intent.json", ".focus-retire.json", StringComparison.Ordinal)) ? 1 : 2);
            int focusPending = FocusReservations(all), ownFocusPending = FocusReservations(Directory.EnumerateFiles(scope));
            bool focusRetire = file.EndsWith(".focus-retire.json", StringComparison.Ordinal);
            bool focusAck = file.EndsWith(".focus-ack.json", StringComparison.Ordinal);
            int focusReleased = focusRetire ? 1 : focusAck
                ? (File.Exists(Path.Combine(scope, file.Replace(".focus-ack.json", ".focus-retire.json", StringComparison.Ordinal))) ? 1 : 2) : 0;
            pending += recoveryPending + focusPending; ownPending += ownRecoveryPending + ownFocusPending;
            bool recoveryAck = file.EndsWith(".recovery-fence.json", StringComparison.Ordinal) || file.EndsWith(".recovery-ack.json", StringComparison.Ordinal);
            bool transferAck = file.EndsWith(".transfer-ack.json", StringComparison.Ordinal);
            if (all.Length + pending + transferPending + 1 + (reserve > 0 ? (int)(reserve / SessionPlayLimits.MaxRecordBytes) : 0) - (completesPending || transferAck || recoveryAck ? 1 : 0) - focusReleased > 16384)
                throw new SessionPlayException(SessionPlayFailures.Unavailable("journal-file-capacity-reached"));
            pending += transferPending; ownPending += ownTransferPending;
            long released = (completesPending || transferAck || recoveryAck ? SessionPlayLimits.MaxRecordBytes : 0L)
                + (long)focusReleased * SessionPlayLimits.MaxRecordBytes;
            long bytesUsed = all.Sum(f => new FileInfo(f).Length);
            long scopeBytes = Directory.EnumerateFiles(scope).Sum(f => new FileInfo(f).Length);
            if (bytesUsed + bytes.Length + pending * SessionPlayLimits.MaxRecordBytes + reserve - released > SessionPlayLimits.MaxStoreBytes
                || scopeBytes + bytes.Length + ownPending * SessionPlayLimits.MaxRecordBytes + reserve - released > SessionPlayLimits.MaxCharacterBytes)
                throw new SessionPlayException(SessionPlayFailures.Unavailable("journal-storage-capacity-reached"));
            string target = Path.Combine(scope, file);
            EnsureNoLinks(target);
            Conflict(!File.Exists(target), "journal-record-already-exists");
            string temp = Path.Combine(scope, ".pending-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var stream = OpenPrivateFile(temp, FileMode.CreateNew))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            File.Move(temp, target, overwrite: false);
        }
    }
    private static IEnumerable<string> Files(string root)
    {
        foreach (var f in Directory.EnumerateFiles(root)) { EnsureNoLinks(f); yield return f; }
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            EnsureNoLinks(directory);
            Require(!Directory.EnumerateDirectories(directory).Any(), "unexpected-journal-subdirectory");
            foreach (var f in Directory.EnumerateFiles(directory)) { EnsureNoLinks(f); yield return f; }
        }
    }
    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    private static FileStream OpenPrivateFile(string path, FileMode mode)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.ReadWrite, Share = FileShare.None,
            BufferSize = 4096, Options = FileOptions.WriteThrough };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }
    private static void EnsureNoLinks(string path)
    {
        for (string? part = Path.GetFullPath(path); part is not null; part = Path.GetDirectoryName(part))
        {
            var info = new FileInfo(part);
            // LinkTarget also identifies dangling links; Exists alone does not.
            if (info.LinkTarget is not null || ((File.Exists(part) || Directory.Exists(part))
                && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0))
                throw new SessionPlayException(SessionPlayFailures.Unavailable("journal-path-link-refused"));
        }
    }
}
