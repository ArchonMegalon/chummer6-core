using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    // Only the synchronous Core service calls this with its freshly acquired owner
    // lease. Historical owner metadata reconstructs a review fingerprint; it is
    // never acquired as a lease, renewed as a review, or treated as admission.
    internal WorkspaceContinuationRestoreResult ResumePrivateClosureMissingTarget(
        OwnerContextStamp owner, IOwnerContextLease lease, OwnerContextStamp originalOwner,
        CharacterWorkspaceId id, Guid operationId, string admissionDigest, string closureDigest,
        TimeProvider clock, DateTimeOffset expires, CancellationToken cancellation)
    {
        using var validationPhase = RecoveryPhaseMeasurement.Begin(RecoveryPhase.StageValidation);
        if (!SupportsWorkspacePrivateRuleSourceClosureRestore || !originalOwner.IsValid
            || originalOwner.Owner != owner.Owner || !ValidRestoreOwner(owner.Owner)
            || operationId == Guid.Empty || !IsRetainedCaptureDigest(closureDigest)
            || !DelegatedGmCharacterEditLedgerValidator.IsSha256(admissionDigest)
            || TryGetPath(owner.Owner, id) is not { } path)
            return ResumeUnresolved("restore-original-review-identity-unavailable");
        RequireRetainedRuleSourceStorage();
        if (!TrySecureExistingWorkspaceDirectory(owner.Owner, allowLegacyMigration: false, recoverStaleTempFiles: false))
            return ResumeUnresolved("restore-staged-record-unavailable");
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        void RequireOwner()
        {
            cancellation.ThrowIfCancellationRequested();
            if (lease.Stamp != owner || clock.GetUtcNow() >= expires)
                throw RetainedRuleSourceRejected();
        }
        RequireOwner();
        ThrowIfLinkOrReparsePoint(path, "workspace target");
        // A concurrent commit is reconciled by the existing receipt path on the
        // next check. No recovery ever replaces an existing target.
        if (File.Exists(path) || Directory.Exists(path))
            return ResumeUnresolved("restore-target-present-requires-reconciliation");
        if (RecoverRuleUpdateProgressUnderLease(owner.Owner, id, path, operationId, admissionDigest, null) is not null)
            return ResumeUnresolved("restore-rule-update-requires-reconciliation");

        string prefix = Path.GetFileName(path) + TempFileMarker;
        string[] entries = Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!, prefix + "*")
            .Take(129).ToArray();
        if (entries.Length > 128) return ResumeUnresolved("restore-staged-selection-ambiguous");
        (string Path, PersistedWorkspaceRecord Record)? selected = null;
        foreach (string entry in entries)
        {
            cancellation.ThrowIfCancellationRequested();
            string suffix = Path.GetFileName(entry)[prefix.Length..];
            // Select only the writer's canonical record names; slot files and
            // other operations are neither promoted nor cleaned up.
            if (!Guid.TryParseExact(suffix, "N", out var name) || name == Guid.Empty
                || name.ToString("N") != suffix) continue;
            var candidate = ReadPrivateRecord(entry); // malformed candidates fail closed
            var receipts = candidate.RetainedRuleSources?.RestoreReceipts ?? [];
            if (!receipts.Any(r => r is not null && r.OperationId == operationId)) continue;
            if (selected is not null || receipts.Length != 1 || receipts[0].AdmissionDigest != admissionDigest)
                return ResumeUnresolved("restore-staged-selection-ambiguous");
            selected = (entry, candidate);
        }
        if (selected is not { } choice) return ResumeUnresolved("restore-staged-record-unavailable");
        string? selectedSlot = ReadContinuationSlotUnderLease(choice.Path);
        string? targetSlot = ReadContinuationSlotUnderLease(path);
        if (selectedSlot is null || targetSlot is not null && targetSlot != selectedSlot)
            return ResumeUnresolved("restore-staged-slot-unproven-or-target-changed");
        var record = choice.Record;
        var manifest = record.RetainedRuleSources!;
        var receipt = manifest.RestoreReceipts![0];
        if (record.RecordSchemaVersion != RetainedWorkspaceRecordSchemaVersion
            || record.LocalHistory is not { } localHistory || localHistory.LastRestore != receipt || localHistory.LocalAdoption is not null
            || manifest.CurrentEnvironment is null || manifest.ActivatedClosureBinding is not { } binding
            || manifest.PrivateClosureBinding is not null || receipt.SourceDigest != closureDigest
            || manifest.RestoreEvidence is not { Length: 1 } evidence || evidence[0] is null
            || evidence[0].Kind != OrdinaryRestoreEvidence || evidence[0].Receipt != receipt)
            return ResumeUnresolved("restore-staged-original-identity-mismatch");

        // Reuse the normal exact, non-migrating private reader, including revision,
        // history, edition, ledger and retained-source checks. This is a detached
        // observation of a selected temp under the FINAL target's existing lease.
        // It is not a public workspace read and cannot activate the staged runner.
        using var history = WorkspacePrivateRuleSourceClosureCompletion.BeginHistoryObservation();
        // Reuse only the immutable capture after each guarded read proves identical
        // live bytes. Canonical admission and the final publication fence stay fresh;
        // no CurrentReadEvaluation (display admission cache) enters this write path.
        var read = ReadWorkspaceUnderLease(owner.Owner, id, choice.Path, out var ledger,
            continuationRead: true, privateClosureRead: true, reuseCurrentCaptureForRead: true,
            cancellation: cancellation);
        if (!read.Success || read.Value is not { } stored)
            return ResumeUnresolved("restore-staged-validation-unavailable");
        using var recovery = OpenRecordRuleSources(owner.Owner, id, record,
            reuseCurrentCaptureForRead: true, cancellation: cancellation);
        if (recovery is null) return ResumeUnresolved("restore-retained-source-unavailable");
        validationPhase?.Returned();
        using var reconstructionPhase = RecoveryPhaseMeasurement.Begin(RecoveryPhase.Reconstruction);
        var snapshot = new WorkspaceContinuationSnapshot(owner.Owner.NormalizedValue,
            new(id, stored.Document, stored.LastUpdatedUtc, stored.ContentRevision, stored.SavedRevision),
            ledger.Select(value => value.Receipt).ToArray())
            { DelegatedGmHistorySegmentStarts = stored.DelegatedGmHistorySegmentStarts.ToArray() };
        var archives = new List<byte[]>();
        byte[]? closureBytes = null;
        try
        {
            long total = 0;
            foreach (var reference in manifest.References)
            {
                RequireOwner();
                total = checked(total + reference.ByteLength);
                if (total > WorkspacePrivateRuleSourceClosureBinding.MaximumBytes
                    || !recovery.TryRecoverRuleSources(reference.CaptureDigest, out var execution) || execution is null)
                    throw RetainedRuleSourceRejected();
                byte[] archive = execution.ExportPrivateInputs();
                archives.Add(archive);
                if (archive.LongLength != reference.ByteLength) throw RetainedRuleSourceRejected();
            }
            var campaigns = (manifest.CommittedDraftCampaigns ?? []).ToList();
            if (manifest.CommittedCampaign is { } campaign) campaigns.Add(campaign);
            void RequireCustody()
            {
                ValidatePrivateClosureCustody(record, owner.Owner, id);
                if (!recovery.ValidateRetainedRuleSources()
                    || record.AuthenticatedHistory is not null && !VerifyAuthenticatedHistory(owner.Owner, id,
                        stored.Document, stored.ContentRevision, stored.SavedRevision, record.AuthenticatedHistory,
                        digest => recovery.TryRecoverRuleSourceCapture(digest, out var capture) ? capture : null))
                    throw RetainedRuleSourceRejected();
            }
            RequireCustody();
            var closure = WorkspacePrivateRuleSourceClosure.Create(snapshot, archives, campaigns,
                WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, cancellation,
                manifest.CurrentEnvironment, record.AuthenticatedHistory);
            if (closure.Binding != binding || binding.ClosureDigest != closureDigest
                || binding.ContinuationDigest != receipt.SnapshotDigest)
                return ResumeUnresolved("restore-original-closure-reconstruction-mismatch");
            closureBytes = closure.ExportPrivateBytes();

            // Reconstruct ONLY the original ordinary new-target review preimage.
            // A missing target with an older slot, replacement, local capture or
            // roaming/update grant cannot match this fingerprint. A stored digest
            // alone is never passed to the publication writer as fresh authority.
            var target = MissingRestoreTarget(owner.Owner, id).Target!;
            string originalDigest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                Contract = "chummer.current-rule-environment-restore/v1", Operation = operationId,
                Owner = originalOwner, Target = target,
                RecordDigest = CharacterCreationFinalizationDigest.Compute<object?>(null), Binding = binding,
                RoamingBaseDigest = (string?)null, CaptureOnly = false, ByteLength = closureBytes.Length
            })));
            if (originalDigest != admissionDigest)
                return ResumeUnresolved("restore-original-absent-target-review-unproven");
            reconstructionPhase?.Returned();
            using var currentPhase = RecoveryPhaseMeasurement.Begin(RecoveryPhase.CurrentAdmission);
            var decoded = WorkspacePrivateRuleSourceClosure.DecodeForRestore(closureBytes, binding,
                _retainedCharacterQueries!, cancellation);
            // Like ordinary Confirm, reuse only this call's owned deterministic
            // materialization. Every writer/source/staging/authority fence below
            // still runs; ineligible inputs retain independent canonical replay.
            using var semantics = new RestoreSemanticOperation3287(lease, owner, clock, expires, cancellation);
            if (!PrivateCurrentRuleEnvironmentAdmission.TryAdmit(decoded, _retainedCharacterQueries!, cancellation,
                    out var current, out _, semantics) || current is null)
                return ResumeUnresolved("restore-current-canonical-admission-unavailable");
            currentPhase?.Returned();
            using var writerPhase = RecoveryPhaseMeasurement.Begin(RecoveryPhase.Writer);
            // Capture the exact serialized record plus metadata/slot only AFTER
            // reconstruction, then require the bytes used by the normal writer.
            using var staging = new PrivateClosureResumeStaging(path, choice.Path, JsonSerializer.SerializeToUtf8Bytes(record), stored.LastUpdatedUtc);
            using var admission = new PrivateClosureRestoreAdmission(owner, lease, clock, expires, cancellation,
                target, binding, receipt, current, staging);
            RequireOwner();
            RequireCustody();
            _ = WriteRecordAtomically(path, record, WorkspaceWriteDisposition.CreateNew, stored.LastUpdatedUtc,
                beforeTargetReplace: () => { RequireCustody(); admission.ValidateFinalFence(); }, closureAdmission: admission,
                cancellation: cancellation);
            writerPhase?.Returned();
            // No new receipt, timestamp, incarnation, revision or operation is issued.
            // Opening today's runner remains a separate fresh read/activation.
            return new(WorkspaceContinuationRestoreOutcome.Recovered, receipt,
                Blockers: ["current-rule-environment-reopen-required"], ReopenRequired: true);
        }
        finally
        {
            if (closureBytes is not null) CryptographicOperations.ZeroMemory(closureBytes);
            foreach (byte[] archive in archives) CryptographicOperations.ZeroMemory(archive);
        }
    }

    private static WorkspaceContinuationRestoreResult ResumeUnresolved(string blocker)
        => new(WorkspaceContinuationRestoreOutcome.Unavailable,
            Blockers: ["restore-outcome-requires-recovery", blocker]);

    // Store-created under one owner + final-workspace lease. The original staged
    // pair is retained on every precommit exception. Copying its slot (rather than
    // consuming it) permits a restart between slot and record rename to validate
    // the SAME generation. A different generation is a create/delete ABA conflict.
    internal sealed class PrivateClosureResumeStaging : IDisposable
    {
        private readonly string target;
        private readonly byte[] bytes;
        private readonly DateTimeOffset timestamp;
        private readonly string slot;
        private bool disposed;
        internal string Path { get; }

        internal PrivateClosureResumeStaging(string target, string path, byte[] serializedRecord,
            DateTimeOffset timestamp)
        {
            string prefix = target + TempFileMarker;
            if (!System.IO.Path.IsPathFullyQualified(target) || !System.IO.Path.IsPathFullyQualified(path)
                || !path.StartsWith(prefix, StringComparison.Ordinal) || serializedRecord.Length > MaximumRetainedWorkspaceRecordBytes
                || !Guid.TryParseExact(path[prefix.Length..], "N", out var name) || name == Guid.Empty
                || name.ToString("N") != path[prefix.Length..]) throw RetainedRuleSourceRejected();
            this.target = target; Path = path; this.timestamp = timestamp;
            bytes = serializedRecord.ToArray();
            slot = ReadContinuationSlotUnderLease(path) ?? throw RetainedRuleSourceRejected();
            ValidateFinalFence();
        }

        internal void ValidatePublication(byte[] publication, DateTimeOffset time)
        {
            if (time != timestamp || !bytes.AsSpan().SequenceEqual(publication)) throw RetainedRuleSourceRejected();
            ValidateFinalFence();
        }

        internal void ValidateFinalFence()
        {
            if (disposed) throw RetainedRuleSourceRejected();
            ThrowIfLinkOrReparsePoint(target, "workspace target");
            ThrowIfLinkOrReparsePoint(Path, "workspace staged original");
            if (File.Exists(target) || Directory.Exists(target)
                || File.GetLastWriteTimeUtc(Path) != timestamp.UtcDateTime
                || ReadContinuationSlotUnderLease(Path) != slot
                || ReadContinuationSlotUnderLease(target) is { } published && published != slot)
                throw RetainedRuleSourceRejected();
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != bytes.Length) throw RetainedRuleSourceRejected();
            byte[] observed = new byte[bytes.Length];
            try
            {
                stream.ReadExactly(observed);
                if (stream.ReadByte() != -1 || !observed.AsSpan().SequenceEqual(bytes)) throw RetainedRuleSourceRejected();
            }
            finally { CryptographicOperations.ZeroMemory(observed); }
        }

        internal void PublishSlot(FileWorkspaceStore store)
        {
            ValidateFinalFence();
            if (ReadContinuationSlotUnderLease(target) is not null) return;
            // This helper never enumerates/removes old temp files, and never
            // overwrites even an absent-record slot. Own only this new slot copy.
            string copy = Path + ".slot.resume." + Guid.NewGuid().ToString("N");
            bool moved = false;
            try
            {
                using (var stream = OpenNewSecureFile(copy, FileAccess.Write, FileShare.None, FileOptions.WriteThrough))
                {
                    stream.Write(Encoding.ASCII.GetBytes(slot));
                    stream.Flush(flushToDisk: true);
                }
                ValidateFinalFence();
                ThrowIfLinkOrReparsePoint(copy, "workspace recovery slot copy");
                File.Move(copy, target + ".slot", overwrite: false);
                moved = true;
                _ = store.FlushRetainedDirectoryChain(System.IO.Path.GetDirectoryName(target)!);
            }
            finally
            {
                if (!moved) DeleteRegularFileIfPresent(copy, "workspace recovery slot copy");
            }
        }

        public void Dispose() { disposed = true; CryptographicOperations.ZeroMemory(bytes); }
    }
}
