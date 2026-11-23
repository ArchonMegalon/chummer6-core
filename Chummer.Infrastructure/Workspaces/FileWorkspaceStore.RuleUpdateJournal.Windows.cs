using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    // Optional private sidecars. Existing workspace/proof/attempt/closure formats
    // are unchanged. A ready image is evidence only; only a matching committed
    // marker authorizes repairing an already-adopted existing target after restart.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record WindowsRuleUpdateJournal(string Schema, Guid OperationId,
        byte[] Record, DateTime TimestampUtc, DateTime? OriginalTimestampUtc, string? Slot,
        Dictionary<string, byte[]> Files);

    private byte[] ReadWindowsRuleUpdateBytes(string path, long maximum)
    {
        using var directories = PinWindowsRuleUpdateDirectory(Path.GetDirectoryName(path)!);
        using var handle = WindowsRuleUpdateNative.OpenFile(path);
        WindowsRuleUpdateNative.ValidatePath(handle, path, directory: false);
        using var stream = new FileStream(handle, FileAccess.ReadWrite);
        if (stream.Length <= 0 || stream.Length > maximum || stream.Length > int.MaxValue)
            throw RetainedRuleSourceRejected();
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private void PrepareWindowsRuleUpdatePublication(string path, string stagedRecord, RuleUpdateProof proof,
        bool legacyObservation = false)
    {
        byte[] bytes = ReadWindowsRuleUpdateBytes(stagedRecord, MaximumRetainedWorkspaceRecordBytes);
        var record = JsonSerializer.Deserialize(bytes, WorkspaceRecordJsonContext.Default.PersistedWorkspaceRecord)
            ?? throw RetainedRuleSourceRejected();
        var owner = new OwnerScope(proof.OwnerId);
        var id = proof.Target.WorkspaceId;
        ValidatePrivateClosureCustody(record, owner, id);
        var manifest = record.RetainedRuleSources ?? throw RetainedRuleSourceRejected();
        string workspaceDirectory = GetWorkspaceDirectory(owner);
        string directory = RuleUpdateDirectory(owner, id, create: false);
        using var journalDirectoryLease = PinWindowsRuleUpdateDirectory(directory);
        string ready = RuleUpdatePath(directory, proof.OperationId, ".ready");
        using var archives = new RetainedRuleSourceWorkspace(this, owner, id, manifest.IncarnationId,
            _retainedCharacterQueries!, manifest.References, observationOnly: true);
        var files = archives.SnapshotWindowsRuleUpdateSources(workspaceDirectory);
        foreach (string suffix in proof.Chosen is null ? new[] { ".json", ".closure", ".attempt" }
            : new[] { ".json", ".closure", ".attempt", ".chosen" })
        {
            string source = RuleUpdatePath(directory, proof.OperationId, suffix);
            files.Add(Path.GetRelativePath(workspaceDirectory, source).Replace('\\', '/'),
                ReadWindowsRuleUpdateBytes(source, MaximumRuleUpdateBranchBytes));
        }
        string? slot = ReadContinuationSlotUnderLease(path);
        if (slot != proof.Target.SlotGeneration) throw RetainedRuleSourceRejected();
        if (slot is not null) files.Add(id.Value + ".json.slot", ReadWindowsRuleUpdateBytes(path + ".slot", 4096));
        var journal = new WindowsRuleUpdateJournal("chummer.windows-rule-update-journal/v1", proof.OperationId,
            bytes, File.GetLastWriteTimeUtc(stagedRecord), legacyObservation ? null : File.GetLastWriteTimeUtc(path), slot, files);
        ValidateWindowsRuleUpdateJournal(path, journal, out _, out _, out _);
        byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(journal);
        var inventory = ReadRuleUpdateInventory(directory);
        if (encoded.LongLength + inventory.Bytes + 1024 > MaximumRuleUpdateBranchBytes) throw RetainedRuleSourceRejected();
        // No retry and no overwrite: a pending image is retained and charged. The
        // original author operation remains Unknown after any uncertain boundary.
        _faultInjector.OnStage(FileWorkspaceStoreFaultStage.BeforeWindowsRuleUpdateJournalPublished, path, ready);
        PublishWindowsRuleUpdateJournalOnce(ready, encoded);
        _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterWindowsRuleUpdateJournalPublished, path, ready);
    }

    private void SealWindowsRuleUpdatePublication(string path)
    {
        using var pinned = PinWindowsRuleUpdateDirectory(Path.GetDirectoryName(path)!);
        byte[] recordBytes = ReadWindowsRuleUpdateBytes(path, MaximumRetainedWorkspaceRecordBytes);
        var record = JsonSerializer.Deserialize(recordBytes, WorkspaceRecordJsonContext.Default.PersistedWorkspaceRecord)
            ?? throw RetainedRuleSourceRejected();
        var manifest = record.RetainedRuleSources;
        var last = manifest?.RestoreEvidence?.LastOrDefault();
        if (last is null || last.Kind != ReviewedUpdateEvidence)
        {
            // No adoption is being acknowledged (the existing NoMutation path).
            FlushRuleUpdateFile(path);
            return;
        }
        var owner = new OwnerScope(manifest!.OwnerId);
        var id = new CharacterWorkspaceId(manifest.WorkspaceId);
        string directory = RuleUpdateDirectory(owner, id, create: false);
        using var journalDirectoryLease = PinWindowsRuleUpdateDirectory(directory);
        var proof = ReadRuleUpdateProof(directory, owner, id, last.Receipt.OperationId);
        string ready = RuleUpdatePath(directory, proof.OperationId, ".ready");
        string commit = RuleUpdatePath(directory, proof.OperationId, ".commit");
        if (!File.Exists(ready))
        {
            // Legacy volume-flushed operations can be sealed from the verified
            // current record. They have no recorded original timestamp and cannot
            // authorize rolling an old target forward. No state is discarded.
            PrepareWindowsRuleUpdatePublication(path, path, proof, legacyObservation: true);
        }
        byte[] encoded = ReadWindowsRuleUpdateBytes(ready, MaximumRuleUpdateBranchBytes);
        var journal = DecodeWindowsRuleUpdateJournal(path, encoded);
        byte[] marker = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(encoded)));
        if (File.Exists(commit))
        {
            if (!ReadWindowsRuleUpdateBytes(commit, 64).AsSpan().SequenceEqual(marker)) throw RetainedRuleSourceRejected();
            return; // later legitimate saves may have advanced beyond this image
        }
        // Hold the adopted file without write/delete sharing while the commit
        // marker becomes durable. Existing-target File.Replace already succeeded.
        using var handle = WindowsRuleUpdateNative.OpenFile(path);
        WindowsRuleUpdateNative.ValidatePath(handle, path, directory: false);
        using var stream = new FileStream(handle, FileAccess.ReadWrite);
        if (stream.Length <= 0 || stream.Length > MaximumRetainedWorkspaceRecordBytes) throw RetainedRuleSourceRejected();
        byte[] actual = new byte[checked((int)stream.Length)];
        stream.ReadExactly(actual);
        if (!actual.AsSpan().SequenceEqual(recordBytes) || ReadContinuationSlotUnderLease(path) != journal.Slot)
            throw RetainedRuleSourceRejected();
        bool exactImage = actual.AsSpan().SequenceEqual(journal.Record) && File.GetLastWriteTimeUtc(path) == journal.TimestampUtc;
        if (!exactImage)
        {
            // Original-operation recovery after a later legitimate save: prove the
            // receipt/history without replacing or rewinding that later record.
            ValidateWindowsRuleUpdateJournal(path, journal, out var prior, out _, out _);
            ValidatePrivateClosureCustody(record, owner, id);
            if (record.ContentRevision <= prior.ContentRevision || record.LocalHistory?.IncarnationId != prior.LocalHistory?.IncarnationId
                || record.RetainedRuleSources?.RestoreReceipts?.Contains(prior.LocalHistory!.LastRestore!) != true)
                throw RetainedRuleSourceRejected();
            using var archives = new RetainedRuleSourceWorkspace(this, owner, id, manifest.IncarnationId,
                _retainedCharacterQueries!, manifest.References, observationOnly: true);
            if (!archives.ValidateRetainedRuleSources() || !TryMaterializeRecord(record, out var document, out var revision, out var saved, out _))
                throw RetainedRuleSourceRejected();
            if (record.AuthenticatedHistory is not null && !VerifyAuthenticatedHistory(owner, id, document, revision, saved,
                record.AuthenticatedHistory, digest => archives.TryRecoverRuleSourceCapture(digest, out var capture) ? capture : null))
                throw RetainedRuleSourceRejected();
            AdmitRetainedCurrentEnvironment(record, owner, id, archives, manifest.CurrentEnvironment!);
        }
        WindowsRuleUpdateNative.Flush(handle);
        _faultInjector.OnStage(FileWorkspaceStoreFaultStage.BeforeWindowsRuleUpdateCommitPublished, path, commit);
        PublishWindowsRuleUpdateJournalOnce(commit, marker);
        _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterWindowsRuleUpdateCommitPublished, path, commit);
    }

    private void PublishWindowsRuleUpdateJournalOnce(string path, byte[] bytes)
    {
        using var pinned = PinWindowsRuleUpdateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            if (!ReadWindowsRuleUpdateBytes(path, MaximumRuleUpdateBranchBytes).AsSpan().SequenceEqual(bytes))
                throw RetainedRuleSourceRejected();
            return;
        }
        string[] pending = Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.pending")
            .Take(2).ToArray();
        if (pending.Length > 1) throw RetainedRuleSourceRejected();
        if (pending.Length == 1)
        {
            if (!TryRuleUpdateEntry(pending[0], out _)
                || !ReadWindowsRuleUpdateBytes(pending[0], MaximumRuleUpdateBranchBytes).AsSpan().SequenceEqual(bytes))
                throw RetainedRuleSourceRejected();
            // Reconcile the sole exact original stage. Do not create a second
            // image or repeat author/review/confirm after an uncertain outcome.
            PublishWindowsRuleUpdateFile(pending[0], path);
            return;
        }
        WriteRuleUpdateFile(path, bytes);
    }

    private WindowsRuleUpdateJournal DecodeWindowsRuleUpdateJournal(string path, byte[] bytes)
    {
        try
        {
            var journal = JsonSerializer.Deserialize<WindowsRuleUpdateJournal>(bytes) ?? throw RetainedRuleSourceRejected();
            ValidateWindowsRuleUpdateJournal(path, journal, out _, out _, out _);
            if (!JsonSerializer.SerializeToUtf8Bytes(journal).AsSpan().SequenceEqual(bytes)) throw RetainedRuleSourceRejected();
            return journal;
        }
        catch (JsonException) { throw RetainedRuleSourceRejected(); }
    }

    private void ValidateWindowsRuleUpdateJournal(string path, WindowsRuleUpdateJournal journal,
        out PersistedWorkspaceRecord record, out RuleUpdateProof proof, out OwnerScope owner)
    {
        if (journal.Schema != "chummer.windows-rule-update-journal/v1" || journal.OperationId == Guid.Empty
            || journal.Record is not { Length: > 0 } || journal.Record.Length > MaximumRetainedWorkspaceRecordBytes
            || journal.Files is null || journal.Files.Count > MaximumRetainedRuleSourceReferences + 5
            || journal.TimestampUtc.Kind != DateTimeKind.Utc || journal.OriginalTimestampUtc is { Kind: not DateTimeKind.Utc })
            throw RetainedRuleSourceRejected();
        record = JsonSerializer.Deserialize(journal.Record, WorkspaceRecordJsonContext.Default.PersistedWorkspaceRecord)
            ?? throw RetainedRuleSourceRejected();
        var manifest = record.RetainedRuleSources ?? throw RetainedRuleSourceRejected();
        owner = new OwnerScope(manifest.OwnerId);
        var id = new CharacterWorkspaceId(manifest.WorkspaceId);
        if (!ValidRestoreOwner(owner) || TryGetPath(owner, id) is not { } expected || !PathComparer.Equals(path, expected))
            throw RetainedRuleSourceRejected();
        ValidatePrivateClosureCustody(record, owner, id);
        string stem = ".rule-update-branches/" + id.Value + "/" + journal.OperationId.ToString("N");
        byte[] Part(string suffix, long maximum)
        {
            if (!journal.Files.TryGetValue(stem + suffix, out var part) || part is null || part.LongLength <= 0 || part.LongLength > maximum)
                throw RetainedRuleSourceRejected();
            return part;
        }
        proof = ValidateRuleUpdateProof(JsonSerializer.Deserialize<RuleUpdateProof>(Part(".json", 32 * 1024))
            ?? throw RetainedRuleSourceRejected(), owner, id, journal.OperationId);
        var attempt = JsonSerializer.Deserialize<RuleUpdateAttempt>(Part(".attempt", MaximumRetainedRuleSourceReferences * 1024L))
            ?? throw RetainedRuleSourceRejected();
        var receipt = manifest.RestoreReceipts?.SingleOrDefault(value => value.OperationId == journal.OperationId);
        if (receipt is null || attempt.Schema != "chummer.reviewed-rule-update-attempt/v1" || attempt.Receipt != receipt
            || receipt.AdmissionDigest != proof.AdmissionDigest || receipt.SourceDigest != proof.Incoming.ClosureDigest
            || receipt.SnapshotDigest != proof.Incoming.ContinuationDigest || receipt.ContentRevision != proof.Incoming.ContentRevision
            || receipt.SavedRevision != proof.Incoming.SavedRevision || record.ContentRevision < receipt.ContentRevision
            || record.SavedRevision < receipt.SavedRevision || attempt.References is not { Length: > 0 }
            || attempt.References.Length > MaximumRetainedRuleSourceReferences
            || attempt.References.Any(value => value is null || !manifest.References.Contains(value))
            || attempt.References.Select(value => value.CaptureDigest).Distinct(StringComparer.Ordinal).Count() != attempt.References.Length
            || journal.OriginalTimestampUtc is not null && (record.ContentRevision != receipt.ContentRevision
                || record.SavedRevision != receipt.SavedRevision || !attempt.References.SequenceEqual(manifest.References))
            || journal.Slot != proof.Target.SlotGeneration
            || Convert.ToHexStringLower(SHA256.HashData(Part(".closure", WorkspacePrivateRuleSourceClosureBinding.MaximumBytes))) != proof.OriginalBytesDigest)
            throw RetainedRuleSourceRejected();
        var allowed = new HashSet<string>(StringComparer.Ordinal) { stem + ".json", stem + ".closure", stem + ".attempt" };
        if (proof.Chosen is not null)
        {
            if (Convert.ToHexStringLower(SHA256.HashData(Part(".chosen", WorkspacePrivateRuleSourceClosureBinding.MaximumBytes))) != proof.ChosenBytesDigest)
                throw RetainedRuleSourceRejected();
            allowed.Add(stem + ".chosen");
        }
        using var archives = new RetainedRuleSourceWorkspace(this, owner, id, manifest.IncarnationId,
            _retainedCharacterQueries!, manifest.References, observationOnly: true); // validates every reference and namespace component
        foreach (var reference in manifest.References)
        {
            string relative = ".retained-rule-sources/" + id.Value + "/" + manifest.IncarnationId + "/" + reference.CaptureDigest[7..] + ".bin";
            if (!allowed.Add(relative) || !journal.Files.TryGetValue(relative, out var bytes) || bytes is null
                || bytes.LongLength != reference.ByteLength || RetainedCharacterRuleSourceInputs.Digest(bytes) != reference.CaptureDigest)
                throw RetainedRuleSourceRejected();
        }
        if (journal.Slot is not null)
        {
            string relative = id.Value + ".json.slot";
            if (!journal.Files.TryGetValue(relative, out var bytes) || bytes is null || bytes.Length > 4096
                || Encoding.UTF8.GetString(bytes) != journal.Slot) throw RetainedRuleSourceRejected();
            allowed.Add(relative);
        }
        if (!allowed.SetEquals(journal.Files.Keys) || journal.Files.Values.Sum(value => value.LongLength) + journal.Record.LongLength > MaximumRuleUpdateBranchBytes)
            throw RetainedRuleSourceRejected();
    }

    private void RecoverWindowsRuleUpdatePublicationUnderLease(string path)
    {
        try { RecoverWindowsRuleUpdatePublicationCore(path); }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        { throw new IOException("Windows rule update journal is invalid.", error); }
    }

    private void RecoverWindowsRuleUpdatePublicationCore(string path)
    {
        // This runs inside the existing cross-process workspace lease, before any
        // read or mutation. Do not reinterpret missing/deleted workspaces as create.
        if (_retainedCharacterQueries is null || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return;
        string parent = Path.GetDirectoryName(path)!;
        string directory = Path.Combine(parent, ".rule-update-branches", Path.GetFileNameWithoutExtension(path));
        ThrowIfLinkOrReparsePoint(directory, "Windows rule journal directory");
        if (!Directory.Exists(directory)) return;
        using var pins = PinWindowsRuleUpdateDirectory(directory);
        var inventory = ReadRuleUpdateInventory(directory);
        var committed = new List<(WindowsRuleUpdateJournal Journal, RuleUpdateProof Proof, OwnerScope Owner, bool HasMarker)>();
        foreach (Guid operation in inventory.Operations)
        {
            string commit = RuleUpdatePath(directory, operation, ".commit");
            bool hasMarker = File.Exists(commit);
            byte[]? observed = null;
            if (!hasMarker)
            {
                // A complete current record can prove adoption after ReplaceFile
                // but before the commit marker. Repair its exact dependencies only;
                // an uncommitted ready image never authorizes rolling old bytes forward.
                if (!File.Exists(path) || !File.Exists(RuleUpdatePath(directory, operation, ".ready"))) continue;
                observed = ReadWindowsRuleUpdateBytes(path, MaximumRetainedWorkspaceRecordBytes);
                var visible = JsonSerializer.Deserialize(observed, WorkspaceRecordJsonContext.Default.PersistedWorkspaceRecord);
                if (visible?.RetainedRuleSources?.RestoreReceipts?.Any(value => value.OperationId == operation) != true) continue;
            }
            byte[] encoded = ReadWindowsRuleUpdateBytes(RuleUpdatePath(directory, operation, ".ready"), MaximumRuleUpdateBranchBytes);
            if (hasMarker && !ReadWindowsRuleUpdateBytes(commit, 64).AsSpan().SequenceEqual(
                Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(encoded))))) throw RetainedRuleSourceRejected();
            var journal = DecodeWindowsRuleUpdateJournal(path, encoded);
            ValidateWindowsRuleUpdateJournal(path, journal, out _, out var proof, out var owner);
            if (journal.OperationId != operation) throw RetainedRuleSourceRejected();
            if (!hasMarker && (!observed!.AsSpan().SequenceEqual(journal.Record)
                || File.GetLastWriteTimeUtc(path) != journal.TimestampUtc || ReadContinuationSlotUnderLease(path) != journal.Slot)) continue;
            committed.Add((journal, proof, owner, hasMarker));
        }
        foreach (var item in committed.OrderBy(value => value.Proof.Incoming.ContentRevision))
        {
            var journal = item.Journal;
            byte[] current = ReadWindowsRuleUpdateBytes(path, MaximumRetainedWorkspaceRecordBytes);
            var record = JsonSerializer.Deserialize(current, WorkspaceRecordJsonContext.Default.PersistedWorkspaceRecord)
                ?? throw RetainedRuleSourceRejected();
            bool isPublished = current.AsSpan().SequenceEqual(journal.Record);
            bool isOriginal = item.HasMarker && journal.OriginalTimestampUtc is { } originalTimestamp
                && CharacterCreationFinalizationDigest.Compute(record) == item.Proof.TargetRecordDigest
                && File.GetLastWriteTimeUtc(path) == originalTimestamp;
            var currentManifest = record.RetainedRuleSources;
            var publishedRecord = JsonSerializer.Deserialize(journal.Record, WorkspaceRecordJsonContext.Default.PersistedWorkspaceRecord)!;
            bool descendant = item.HasMarker && currentManifest?.OwnerId == item.Owner.NormalizedValue
                && currentManifest.WorkspaceId == item.Proof.Target.WorkspaceId.Value
                && currentManifest.IncarnationId == publishedRecord.RetainedRuleSources!.IncarnationId
                && record.ContentRevision > publishedRecord.ContentRevision
                && currentManifest.RestoreReceipts?.Any(value => value == publishedRecord.LocalHistory!.LastRestore) == true;
            // A later legitimate edit may still need the immutable dependencies.
            // It is never overwritten with the older journal record.
            if (!isPublished && !isOriginal && !descendant) continue;
            ValidatePrivateClosureCustody(record, item.Owner, item.Proof.Target.WorkspaceId);
            string? existingSlot = ReadContinuationSlotUnderLease(path);
            if (existingSlot != journal.Slot && !(isPublished && existingSlot is null))
                throw RetainedRuleSourceRejected(); // missing/changed generation cannot authorize redo of an old target
            RestoreWindowsRuleUpdateDependencies(parent, journal);
            if (ReadContinuationSlotUnderLease(path) != journal.Slot) throw RetainedRuleSourceRejected();
            ValidateWindowsRuleUpdateJournal(path, journal, out var adopted, out _, out var owner);
            var manifest = adopted.RetainedRuleSources!;
            using var archives = new RetainedRuleSourceWorkspace(this, owner, item.Proof.Target.WorkspaceId,
                manifest.IncarnationId, _retainedCharacterQueries!, manifest.References, observationOnly: true);
            if (!archives.ValidateRetainedRuleSources() || !TryMaterializeRecord(adopted, out var document, out var revision, out var saved, out _))
                throw RetainedRuleSourceRejected();
            if (adopted.AuthenticatedHistory is not null && !VerifyAuthenticatedHistory(owner, item.Proof.Target.WorkspaceId,
                document, revision, saved, adopted.AuthenticatedHistory,
                digest => archives.TryRecoverRuleSourceCapture(digest, out var capture) ? capture : null)) throw RetainedRuleSourceRejected();
            AdmitRetainedCurrentEnvironment(adopted, owner, item.Proof.Target.WorkspaceId, archives, manifest.CurrentEnvironment!);
            if (isPublished || descendant) continue;
            // Redo exact saved bytes, not the author's command. The durable marker
            // survives further rename loss; no new receipt/revision/history is made.
            string temporary = path + TempFileMarker + Guid.NewGuid().ToString("N");
            bool temporaryCreated = false;
            bool targetReplaced = false;
            try
            {
                using (var output = OpenNewSecureFile(temporary, FileAccess.Write, FileShare.None, FileOptions.WriteThrough))
                {
                    temporaryCreated = true;
                    output.Write(journal.Record);
                    output.Flush(flushToDisk: true);
                }
                SetSecureFileMode(temporary);
                File.SetLastWriteTimeUtc(temporary, journal.TimestampUtc);
                FlushRuleUpdateFile(temporary);
                _faultInjector.OnStage(FileWorkspaceStoreFaultStage.BeforeWindowsRuleUpdateRecoveryReplace, path, temporary);
                // Re-read after the callback; replacement still REQUIRES the same target.
                if (!ReadWindowsRuleUpdateBytes(path, MaximumRetainedWorkspaceRecordBytes).AsSpan().SequenceEqual(current)
                    || File.GetLastWriteTimeUtc(path) != journal.OriginalTimestampUtc
                    || ReadContinuationSlotUnderLease(path) != journal.Slot) throw RetainedRuleSourceRejected();
                File.Replace(temporary, path, destinationBackupFileName: null);
                targetReplaced = true;
                FlushRuleUpdateFile(path);
            }
            finally
            {
                if (temporaryCreated && !targetReplaced)
                    DeleteRegularFileIfPresent(temporary, "workspace temporary file");
            }
        }
    }

    private void RestoreWindowsRuleUpdateDependencies(string workspaceDirectory, WindowsRuleUpdateJournal journal)
    {
        foreach (var entry in journal.Files)
        {
            string target = Path.GetFullPath(Path.Combine(workspaceDirectory, entry.Key.Replace('/', Path.DirectorySeparatorChar)));
            EnsurePathContained(workspaceDirectory, target, "Windows rule journal dependency");
            ThrowIfLinkOrReparsePoint(target, "Windows rule journal dependency");
            if (File.Exists(target))
            {
                if (!ReadWindowsRuleUpdateBytes(target, MaximumRuleUpdateBranchBytes).AsSpan().SequenceEqual(entry.Value))
                    throw RetainedRuleSourceRejected();
                continue;
            }
            var createdDirectoryPins = new List<WindowsRuleUpdateDirectoryLease>();
            try
            {
                string current = workspaceDirectory;
                createdDirectoryPins.Add(PinWindowsRuleUpdateDirectory(current));
                foreach (string component in Path.GetRelativePath(workspaceDirectory, Path.GetDirectoryName(target)!).Split(Path.DirectorySeparatorChar))
                {
                    if (component == ".") continue;
                    current = Path.Combine(current, component);
                    EnsureSecureDirectory(current, "Windows rule journal dependency directory");
                    createdDirectoryPins.Add(PinWindowsRuleUpdateDirectory(current));
                }
                WriteRuleUpdateFile(target, entry.Value); // immutable, no overwrite
            }
            finally
            {
                for (int index = createdDirectoryPins.Count - 1; index >= 0; --index) createdDirectoryPins[index].Dispose();
            }
        }
    }
}
