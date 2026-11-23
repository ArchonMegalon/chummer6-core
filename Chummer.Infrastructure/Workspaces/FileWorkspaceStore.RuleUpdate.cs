using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    private const int MaximumRuleUpdateBranches = 32;
    private const long MaximumRuleUpdateBranchBytes = 512L * 1024 * 1024;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record RuleUpdateProof(string Schema, string OwnerId, string OwnerAuthorityInstanceId,
        long OwnerTransitionRevision, Guid OperationId,
        string AdmissionDigest, string Decision, WorkspaceContinuationRestoreTarget Target,
        string TargetRecordDigest, WorkspacePrivateRuleSourceClosureBinding Original,
        WorkspacePrivateRuleSourceClosureBinding Incoming, string OriginalBytesDigest, DateTimeOffset PreservedAtUtc)
    {
        // Present only for an internally prepared local resolution. Incoming is
        // the genuinely issued local closure; Chosen remains the exact carrier.
        public WorkspacePrivateRuleSourceClosureBinding? Chosen { get; init; }
        public string? ChosenBytesDigest { get; init; }
        public Guid? AlternativeOperation { get; init; }
    }

    private string RuleUpdateDirectory(OwnerScope owner, CharacterWorkspaceId id, bool create)
    {
        RequireRetainedRuleSourceStorage();
        if (!ValidRestoreOwner(owner) || TryGetPath(owner, id) is null
            || !TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false)) throw RetainedRuleSourceRejected();
        string root = Path.Combine(GetWorkspaceDirectory(owner), ".rule-update-branches");
        string directory = Path.Combine(root, id.Value);
        EnsurePathContained(GetWorkspaceDirectory(owner), directory, "rule update branches");
        ThrowIfLinkOrReparsePoint(root, "rule update branches");
        ThrowIfLinkOrReparsePoint(directory, "rule update branches");
        if (create)
        {
            EnsureSecureDirectory(root, "rule update branches");
            EnsureSecureDirectory(directory, "rule update branches");
        }
        return directory;
    }

    private static string RuleUpdatePath(string directory, Guid operation, string suffix)
    {
        if (operation == Guid.Empty) throw RetainedRuleSourceRejected();
        return Path.Combine(directory, operation.ToString("N") + suffix);
    }

    private static RuleUpdateProof ReadRuleUpdateProof(string directory, OwnerScope owner,
        CharacterWorkspaceId id, Guid operation)
    {
        string path = RuleUpdateProofPath(directory, operation);
        ThrowIfLinkOrReparsePoint(path, "rule update proof");
        if (new FileInfo(path).Length > 32 * 1024) throw RetainedRuleSourceRejected();
        var proof = JsonSerializer.Deserialize<RuleUpdateProof>(File.ReadAllBytes(path)) ?? throw RetainedRuleSourceRejected();
        return ValidateRuleUpdateProof(proof, owner, id, operation);
    }

    private static RuleUpdateProof ValidateRuleUpdateProof(RuleUpdateProof proof, OwnerScope owner, CharacterWorkspaceId id, Guid operation)
    {
        if (proof.Target is null || proof.Original is null || proof.Incoming is null
            || proof.Schema != "chummer.reviewed-rule-update-proof/v1" || proof.OperationId != operation
            || proof.OwnerId != owner.NormalizedValue || string.IsNullOrWhiteSpace(proof.OwnerAuthorityInstanceId)
            || proof.OwnerTransitionRevision < 0
            || proof.Decision != PrivateRuleUpdateOriginal.Decision
            || !DelegatedGmCharacterEditLedgerValidator.IsSha256(proof.AdmissionDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(proof.TargetRecordDigest)
            || !DelegatedGmCharacterEditLedgerValidator.IsSha256(proof.OriginalBytesDigest)
            || proof.Target.OwnerId != owner.NormalizedValue || proof.Target.WorkspaceId != id || !proof.Target.Exists
            || proof.Original.OwnerId != owner.NormalizedValue || proof.Original.WorkspaceId != id
            || proof.Incoming.OwnerId != owner.NormalizedValue || proof.Incoming.WorkspaceId != id
            || proof.Original.ContinuationDigest != proof.Target.SnapshotDigest
            || proof.Incoming.ContentRevision <= proof.Target.ContentRevision) throw RetainedRuleSourceRejected();
        if (proof.AlternativeOperation == Guid.Empty || proof.AlternativeOperation is not null && proof.Chosen is null
            || (proof.Chosen is null) != (proof.ChosenBytesDigest is null)
            || proof.Chosen is { } chosen && (chosen.OwnerId != proof.OwnerId || chosen.WorkspaceId != id
                || chosen.Schema != WorkspacePrivateRuleSourceClosureBinding.SchemaV2
                || !DelegatedGmCharacterEditLedgerValidator.IsSha256(proof.ChosenBytesDigest)
                || Math.Max(chosen.ContentRevision, proof.Target.ContentRevision) == long.MaxValue
                || proof.Incoming.ContentRevision != Math.Max(chosen.ContentRevision, proof.Target.ContentRevision) + 1
                || proof.Incoming.SavedRevision != proof.Incoming.ContentRevision)) throw RetainedRuleSourceRejected();
        return proof;
    }

    private static byte[] ReadRuleUpdateBytes(string directory, RuleUpdateProof proof)
    {
        string path = RuleUpdatePath(directory, proof.OperationId, ".closure");
        ThrowIfLinkOrReparsePoint(path, "rule update original");
        long size = new FileInfo(path).Length;
        if (size <= 0 || size > WorkspacePrivateRuleSourceClosureBinding.MaximumBytes) throw RetainedRuleSourceRejected();
        byte[] bytes = File.ReadAllBytes(path);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != proof.OriginalBytesDigest)
        { CryptographicOperations.ZeroMemory(bytes); throw RetainedRuleSourceRejected(); }
        return bytes;
    }

    private void ValidateRuleUpdateChosen(string directory, RuleUpdateProof proof)
    {
        if (proof.Chosen is not { } chosen) return;
        string path = RuleUpdatePath(directory, proof.OperationId, ".chosen");
        ThrowIfLinkOrReparsePoint(path, "chosen rule branch");
        long size = new FileInfo(path).Length;
        if (size <= 0 || size > WorkspacePrivateRuleSourceClosureBinding.MaximumBytes) throw RetainedRuleSourceRejected();
        byte[] bytes = File.ReadAllBytes(path);
        try
        {
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != proof.ChosenBytesDigest) throw RetainedRuleSourceRejected();
            _ = WorkspacePrivateRuleSourceClosure.DecodeForRestore(bytes, chosen, _retainedCharacterQueries!, CancellationToken.None);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private void ValidateChosenAlternativeUnderLease(OwnerScope owner, CharacterWorkspaceId id,
        PrivateClosureRestoreAdmission admission)
    {
        if (admission.AlternativeOperation is not { } operation) return;
        if (admission.ChosenBinding is null || admission.ChosenBytes is null) throw RetainedRuleSourceRejected();
        string directory = RuleUpdateDirectory(owner, id, create: false);
        var proof = ReadRuleUpdateProof(directory, owner, id, operation);
        ValidateRuleUpdateChosen(directory, proof);
        byte[] bytes = ReadRuleUpdateBytes(directory, proof);
        try
        {
            if (proof.Original != admission.ChosenBinding || !bytes.AsSpan().SequenceEqual(admission.ChosenBytes))
                throw RetainedRuleSourceRejected();
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static string RuleUpdateProofPath(string directory, Guid operation)
    {
        string published = RuleUpdatePath(directory, operation, ".json");
        if (File.Exists(published)) return published;
        // The proof is flushed before publishing the closure. A crash between
        // those publications can leave the complete proof at its staging name.
        // Read only one exact-operation candidate; ambiguity remains unknown.
        string[] pending = Directory.EnumerateFiles(directory, operation.ToString("N") + ".json.*.pending")
            .Take(2).ToArray();
        if (pending.Length != 1 || !TryRuleUpdateEntry(pending[0], out var id) || id != operation)
            throw RetainedRuleSourceRejected();
        return pending[0];
    }

    private void WriteRuleUpdateFile(string path, ReadOnlySpan<byte> bytes, Action? beforePublish = null)
    {
        ThrowIfLinkOrReparsePoint(path, "rule update file");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        using (var stream = OpenNewSecureFile(temporary, FileAccess.Write, FileShare.None, FileOptions.WriteThrough))
        { stream.Write(bytes); stream.Flush(flushToDisk: true); }
        SetSecureFileMode(temporary);
        FlushRuleUpdateFile(temporary);
        _faultInjector.OnStage(FileWorkspaceStoreFaultStage.BeforeRuleUpdateFilePublished, path, temporary);
        beforePublish?.Invoke();
        if (OperatingSystem.IsWindows()) PublishWindowsRuleUpdateFile(temporary, path);
        else File.Move(temporary, path, overwrite: false);
        FlushRuleUpdateDirectory(Path.GetDirectoryName(path)!);
    }

    private static bool TryRuleUpdateEntry(string path, out Guid operation)
    {
        string[] parts = Path.GetFileName(path).Split('.');
        operation = Guid.Empty;
        return parts.Length is 2 or 4 && Guid.TryParseExact(parts[0], "N", out operation) && operation != Guid.Empty
            && operation.ToString("N") == parts[0] && parts[1] is ("json" or "closure" or "attempt" or "chosen" or "ready" or "commit")
            && (parts.Length == 2 || parts[3] == "pending" && Guid.TryParseExact(parts[2], "N", out var nonce)
                && nonce != Guid.Empty && nonce.ToString("N") == parts[2]);
    }

    private sealed record RuleUpdateInventory(Guid[] Operations, long Bytes);

    private static RuleUpdateInventory ReadRuleUpdateInventory(string directory)
    {
        if (!Directory.Exists(directory)) return new([], 0);
        // Four legacy publications plus two Windows journal publications, each
        // with at most one stage. Pending originals/journals remain charged.
        // Count staged bytes too; no cleanup ever deletes a possibly sole original.
        string[] entries = Directory.EnumerateFileSystemEntries(directory).Take(MaximumRuleUpdateBranches * 12 + 1).ToArray();
        if (entries.Length > MaximumRuleUpdateBranches * 12) throw RetainedRuleSourceRejected();
        var operations = new HashSet<Guid>();
        long bytes = 0;
        foreach (string path in entries)
        {
            ThrowIfLinkOrReparsePoint(path, "rule update entry");
            if (!TryRuleUpdateEntry(path, out var operation) || Directory.Exists(path)) throw RetainedRuleSourceRejected();
            operations.Add(operation);
            bytes = checked(bytes + new FileInfo(path).Length);
            if (operations.Count > MaximumRuleUpdateBranches || bytes > MaximumRuleUpdateBranchBytes)
                throw RetainedRuleSourceRejected();
        }
        return new(operations.ToArray(), bytes);
    }

    private IReadOnlyList<RuleUpdateProof> ReadRuleUpdateProofs(string directory, OwnerScope owner, CharacterWorkspaceId id)
    {
        var result = new List<RuleUpdateProof>();
        foreach (var operation in ReadRuleUpdateInventory(directory).Operations)
        {
            try
            {
                var proof = ReadRuleUpdateProof(directory, owner, id, operation);
                byte[] bytes = ReadRuleUpdateBytes(directory, proof);
                CryptographicOperations.ZeroMemory(bytes);
                ValidateRuleUpdateChosen(directory, proof);
                result.Add(proof);
            }
            catch (Exception error) when (WorkspacePrivateRuleSourceClosure.IsFailure(error))
            {
                // One interrupted entry is not authority over other alternatives.
                // It stays retained and charged to the capacity bound.
            }
        }
        return result;
    }

    private void PreserveRuleUpdateOriginal(OwnerScope owner, CharacterWorkspaceId id,
        PrivateClosureRestoreAdmission admission, PersistedWorkspaceRecord previous)
    {
        if (admission.UpdateOriginal is not { } original) throw RetainedRuleSourceRejected();
        admission.ValidateFinalFence();
        if (original.Binding.OwnerId != owner.NormalizedValue || original.Binding.WorkspaceId != id
            || original.Binding.ContinuationDigest != admission.Target.SnapshotDigest
            || CharacterCreationFinalizationDigest.Compute(previous) != admission.TargetRecordDigest)
            throw RetainedRuleSourceRejected();
        ValidatePrivateClosureCustody(previous, owner, id);
        ValidateChosenAlternativeUnderLease(owner, id, admission);
        string directory = RuleUpdateDirectory(owner, id, create: false);
        var inventory = ReadRuleUpdateInventory(directory);
        if (inventory.Operations.Length >= MaximumRuleUpdateBranches
            || inventory.Operations.Contains(admission.Receipt.OperationId)
            || checked(inventory.Bytes + original.Bytes.LongLength + (admission.ChosenBytes?.LongLength ?? 0) + 32 * 1024
                + MaximumRetainedRuleSourceReferences * 1024L) > MaximumRuleUpdateBranchBytes)
            throw RetainedRuleSourceRejected();
        var reviewedOperations = (previous.RetainedRuleSources?.RestoreEvidence ?? [])
            .Where(value => value.Kind == ReviewedUpdateEvidence).Select(value => value.Receipt.OperationId).ToHashSet();
        foreach (var operation in inventory.Operations.Concat(reviewedOperations).Distinct())
        {
            // A visible receipt does not clear an earlier failed durability ack.
            // Reconcile it under THIS workspace lease before permitting a successor.
            if (!reviewedOperations.Contains(operation) && !File.Exists(RuleUpdatePath(directory, operation, ".attempt"))) continue;
            var proof = ReadRuleUpdateProof(directory, owner, id, operation);
            ReconcileRuleUpdateCommitUnderLease(owner, id, TryGetPath(owner, id)!, directory, proof, previous);
        }
        // Reconcile persisted reviewed operations before creating any successor
        // evidence, including when the entire prior sidecar directory is gone.
        directory = RuleUpdateDirectory(owner, id, create: true);
        var identity = new RuleUpdateProof("chummer.reviewed-rule-update-proof/v1", admission.Owner.Owner.NormalizedValue,
            admission.Owner.AuthorityInstanceId, admission.Owner.TransitionRevision,
            admission.Receipt.OperationId, admission.Receipt.AdmissionDigest, PrivateRuleUpdateOriginal.Decision,
            admission.Target, admission.TargetRecordDigest, original.Binding, admission.Binding,
            Convert.ToHexStringLower(SHA256.HashData(original.Bytes)), admission.Receipt.RestoredAtUtc)
        {
            Chosen = admission.ChosenBinding,
            AlternativeOperation = admission.AlternativeOperation,
            ChosenBytesDigest = admission.ChosenBytes is { } sourceBytes ? Convert.ToHexStringLower(SHA256.HashData(sourceBytes)) : null
        };
        WriteRuleUpdateFile(RuleUpdatePath(directory, identity.OperationId, ".json"), JsonSerializer.SerializeToUtf8Bytes(identity), () =>
        {
            if (admission.ChosenBytes is { } chosenBytes)
                WriteRuleUpdateFile(RuleUpdatePath(directory, identity.OperationId, ".chosen"), chosenBytes);
            WriteRuleUpdateFile(RuleUpdatePath(directory, identity.OperationId, ".closure"), original.Bytes);
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterRuleUpdateClosurePublished,
                TryGetPath(owner, id)!, RuleUpdatePath(directory, identity.OperationId, ".closure"));
        });
        byte[] verified = ReadRuleUpdateBytes(directory, ReadRuleUpdateProof(directory, owner, id, identity.OperationId));
        CryptographicOperations.ZeroMemory(verified);
    }

    // Called under the existing workspace write lease immediately before the real File.Replace.
    private void FenceRuleUpdatePublication(string path, string stagedRecord, PrivateClosureRestoreAdmission admission, PersistedWorkspaceRecord record)
    {
        if (admission.UpdateOriginal is null) return;
        var owner = admission.Owner.Owner.IsLocalSingleUser ? admission.Owner.Owner : new OwnerScope(admission.Owner.Owner.NormalizedValue);
        var id = admission.Target.WorkspaceId;
        var target = ReadPrivateClosureTargetUnderLease(owner, id, path, out _);
        if (target.Result.Target != admission.Target || target.RecordDigest != admission.TargetRecordDigest)
            throw RetainedRuleSourceRejected();
        string directory = RuleUpdateDirectory(owner, id, create: false);
        var proof = ReadRuleUpdateProof(directory, owner, id, admission.Receipt.OperationId);
        if (proof.OwnerId != admission.Owner.Owner.NormalizedValue
            || proof.OwnerAuthorityInstanceId != admission.Owner.AuthorityInstanceId
            || proof.OwnerTransitionRevision != admission.Owner.TransitionRevision
            || proof.AdmissionDigest != admission.Receipt.AdmissionDigest
            || proof.TargetRecordDigest != admission.TargetRecordDigest || proof.Incoming != admission.Binding
            || proof.Original != admission.UpdateOriginal.Binding || proof.Chosen != admission.ChosenBinding
            || proof.AlternativeOperation != admission.AlternativeOperation
            || proof.ChosenBytesDigest != (admission.ChosenBytes is { } chosen ? Convert.ToHexStringLower(SHA256.HashData(chosen)) : null))
            throw RetainedRuleSourceRejected();
        ValidateRuleUpdateChosen(directory, proof);
        byte[] verified = ReadRuleUpdateBytes(directory, proof);
        CryptographicOperations.ZeroMemory(verified);
        admission.ValidateFinalFence();
        ValidateChosenAlternativeUnderLease(owner, id, admission);
        var references = record.RetainedRuleSources?.References ?? throw RetainedRuleSourceRejected();
        WriteRuleUpdateFile(RuleUpdatePath(directory, proof.OperationId, ".attempt"), JsonSerializer.SerializeToUtf8Bytes(
            new RuleUpdateAttempt("chummer.reviewed-rule-update-attempt/v1", admission.Receipt, references)));
        _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterRuleUpdateAttemptFlushed, path,
            RuleUpdatePath(directory, proof.OperationId, ".attempt"));
        ValidateChosenAlternativeUnderLease(owner, id, admission);
        // After the attempt fence, only exact receipt and durability reconciliation resolve uncertainty.
        if (OperatingSystem.IsWindows())
            PrepareWindowsRuleUpdatePublication(path, stagedRecord, proof);
        admission.ValidateFinalFence();
        if (CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)) != admission.TargetRecordDigest
            || ReadContinuationSlotUnderLease(path) != admission.Target.SlotGeneration
            || File.GetLastWriteTimeUtc(path) != admission.UpdateOriginal.LastUpdatedUtc.UtcDateTime)
            throw RetainedRuleSourceRejected();
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record RuleUpdateAttempt(string Schema, WorkspaceContinuationRestoreReceipt Receipt,
        CharacterCreationRetainedRuleSourceReference[] References);

    private void ReconcileRuleUpdateCommitUnderLease(OwnerScope owner, CharacterWorkspaceId id,
        string path, string directory, RuleUpdateProof proof, PersistedWorkspaceRecord record)
    {
        byte[] original = ReadRuleUpdateBytes(directory, proof);
        CryptographicOperations.ZeroMemory(original);
        ValidateRuleUpdateChosen(directory, proof);
        string attemptPath = RuleUpdatePath(directory, proof.OperationId, ".attempt");
        ThrowIfLinkOrReparsePoint(attemptPath, "rule update attempt");
        if (new FileInfo(attemptPath).Length > MaximumRetainedRuleSourceReferences * 1024L) throw RetainedRuleSourceRejected();
        var attempt = JsonSerializer.Deserialize<RuleUpdateAttempt>(File.ReadAllBytes(attemptPath)) ?? throw RetainedRuleSourceRejected();
        var receipt = record.RetainedRuleSources?.RestoreReceipts?.SingleOrDefault(value => value.OperationId == proof.OperationId);
        var evidence = record.RetainedRuleSources?.RestoreEvidence?.SingleOrDefault(value => value.Receipt.OperationId == proof.OperationId);
        if (attempt.Schema != "chummer.reviewed-rule-update-attempt/v1" || receipt is null || attempt.Receipt != receipt
            || evidence is null || evidence.Kind != ReviewedUpdateEvidence || evidence.Receipt != receipt
            || receipt.AdmissionDigest != proof.AdmissionDigest || receipt.SourceDigest != proof.Incoming.ClosureDigest
            || receipt.SnapshotDigest != proof.Incoming.ContinuationDigest || receipt.ContentRevision != proof.Incoming.ContentRevision
            || receipt.SavedRevision != proof.Incoming.SavedRevision || attempt.References is not { Length: > 0 }
            || attempt.References.Length > MaximumRetainedRuleSourceReferences
            || attempt.References.Any(reference => reference is null)
            || attempt.References.Select(reference => reference.CaptureDigest).Distinct(StringComparer.Ordinal).Count() != attempt.References.Length)
            throw RetainedRuleSourceRejected();
        ValidatePrivateClosureCustody(record, owner, id);
        string recordDigest = CharacterCreationFinalizationDigest.Compute(record);
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        string? slot = ReadContinuationSlotUnderLease(path);
        if (slot != proof.Target.SlotGeneration) throw RetainedRuleSourceRejected();
        using var archives = new RetainedRuleSourceWorkspace(this, owner, id, receipt.IncarnationId,
            _retainedCharacterQueries!, attempt.References, observationOnly: true, ruleUpdateDurability: true);
        foreach (var reference in attempt.References)
            if (archives.ReconcilePublished(reference).Durability != (OperatingSystem.IsWindows()
                ? RetainedRuleSourceDurability.ProcessRestartOnly : RetainedRuleSourceDurability.FileAndDirectoriesFlushed))
                throw RetainedRuleSourceRejected();
        if (proof.Chosen is not null) FlushRuleUpdateFile(RuleUpdatePath(directory, proof.OperationId, ".chosen"));
        FlushRuleUpdateFile(RuleUpdatePath(directory, proof.OperationId, ".closure"));
        FlushRuleUpdateFile(RuleUpdateProofPath(directory, proof.OperationId));
        FlushRuleUpdateFile(attemptPath);
        FlushRuleUpdateDirectory(directory);
        FlushRuleUpdateFile(path);
        if (slot is not null) FlushRuleUpdateFile(path + ".slot");
        FlushRuleUpdateRecordDirectory(path);
        // No replacement or logical timestamp mutation occurs during recovery.
        // Later saves are permitted, but this exact observation must stay stable.
        if (CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)) != recordDigest
            || File.GetLastWriteTimeUtc(path) != timestamp || ReadContinuationSlotUnderLease(path) != slot)
            throw RetainedRuleSourceRejected();
    }

    private WorkspaceRuleUpdateProgress? RecoverRuleUpdateProgressUnderLease(OwnerScope owner, CharacterWorkspaceId id,
        string path, Guid operation, string admissionDigest, PersistedWorkspaceRecord? record)
    {
        try
        {
            if (record is not null) ValidatePrivateClosureCustody(record, owner, id);
            var evidence = record?.RetainedRuleSources?.RestoreEvidence?.SingleOrDefault(value => value.Receipt.OperationId == operation);
            string directory = RuleUpdateDirectory(owner, id, create: false);
            if (!ReadRuleUpdateInventory(directory).Operations.Contains(operation))
                return evidence?.Kind == ReviewedUpdateEvidence ? Unknown() : null;
            if (evidence is not null && (evidence.Kind != ReviewedUpdateEvidence || evidence.Receipt.AdmissionDigest != admissionDigest))
                return Unknown();
            var proof = ReadRuleUpdateProof(directory, owner, id, operation);
            if (proof.AdmissionDigest != admissionDigest) return Unknown();
            if (record?.RetainedRuleSources?.RestoreReceipts?.Any(receipt => receipt.OperationId == operation) == true)
            {
                ReconcileRuleUpdateCommitUnderLease(owner, id, path, directory, proof, record);
                return new(operation, admissionDigest, proof.Decision, WorkspaceRuleUpdateState.CommittedUpdate);
            }
            if (File.Exists(RuleUpdatePath(directory, operation, ".attempt"))) return Unknown();
            if (!File.Exists(RuleUpdatePath(directory, operation, ".closure")))
            {
                // An interrupted stage has not preserved a usable alternative.
                // Only an exact, still-live original whose durability we can now
                // acknowledge permits NoMutation; never infer it from a filename.
                var current = ReadPrivateClosureTargetUnderLease(owner, id, path, out _);
                if (current.Result.Target != proof.Target || current.RecordDigest != proof.TargetRecordDigest) return Unknown();
                FlushRuleUpdateFile(path);
                if (proof.Target.SlotGeneration is not null) FlushRuleUpdateFile(path + ".slot");
                FlushRuleUpdateRecordDirectory(path);
                var verified = ReadPrivateClosureTargetUnderLease(owner, id, path, out _);
                if (verified.Result.Target != proof.Target || verified.RecordDigest != proof.TargetRecordDigest) return Unknown();
                return new(operation, admissionDigest, proof.Decision, WorkspaceRuleUpdateState.NoMutation);
            }
            byte[] bytes = ReadRuleUpdateBytes(directory, proof);
            CryptographicOperations.ZeroMemory(bytes);
            ValidateRuleUpdateChosen(directory, proof);
            if (proof.Chosen is not null) FlushRuleUpdateFile(RuleUpdatePath(directory, operation, ".chosen"));
            // A complete original with no published attempt cannot have replaced
            // the target. Acknowledge its own durability before retiring a journal.
            FlushRuleUpdateFile(RuleUpdatePath(directory, operation, ".closure"));
            FlushRuleUpdateFile(RuleUpdateProofPath(directory, operation));
            FlushRuleUpdateDirectory(directory);
            return new(operation, admissionDigest, proof.Decision, WorkspaceRuleUpdateState.PreservedBranchOnly);
        }
        catch (Exception error) when (WorkspacePrivateRuleSourceClosure.IsFailure(error)) { return Unknown(); }
        WorkspaceRuleUpdateProgress Unknown() => new(operation, admissionDigest,
            PrivateRuleUpdateOriginal.Decision, WorkspaceRuleUpdateState.Unknown);
    }

    internal WorkspaceRuleUpdateProgress? RecoverRuleUpdateProgress(OwnerScope owner, CharacterWorkspaceId id, Guid operation, string admissionDigest)
    {
        if (!ValidRestoreOwner(owner) || TryGetPath(owner, id) is not { } path || operation == Guid.Empty
            || !DelegatedGmCharacterEditLedgerValidator.IsSha256(admissionDigest)) return null;
        try
        {
            using var lease = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            return RecoverRuleUpdateProgressUnderLease(owner, id, path, operation, admissionDigest,
                File.Exists(path) ? ReadPrivateRecord(path) : null);
        }
        catch (Exception error) when (WorkspacePrivateRuleSourceClosure.IsFailure(error))
        {
            return new(operation, admissionDigest, PrivateRuleUpdateOriginal.Decision, WorkspaceRuleUpdateState.Unknown);
        }
    }

    internal IReadOnlyList<WorkspaceRuleUpdateAlternative> ListRuleUpdateAlternatives(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (TryGetPath(owner, id) is not { } path) throw RetainedRuleSourceRejected();
        using var lease = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        string directory = RuleUpdateDirectory(owner, id, create: false);
        return ReadRuleUpdateProofs(directory, owner, id).OrderByDescending(proof => proof.PreservedAtUtc)
            .Select(proof => new WorkspaceRuleUpdateAlternative(proof.OperationId, proof.Original, proof.PreservedAtUtc)).ToArray();
    }

    internal (byte[] Bytes, WorkspacePrivateRuleSourceClosureBinding Binding) ReadRuleUpdateAlternative(
        OwnerScope owner, CharacterWorkspaceId id, Guid operation)
    {
        if (TryGetPath(owner, id) is not { } path) throw RetainedRuleSourceRejected();
        using var lease = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        string directory = RuleUpdateDirectory(owner, id, create: false);
        var proof = ReadRuleUpdateProof(directory, owner, id, operation);
        ValidateRuleUpdateChosen(directory, proof);
        return (ReadRuleUpdateBytes(directory, proof), proof.Original);
    }
}
