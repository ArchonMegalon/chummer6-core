using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    private const string InterruptedSpecializationRejection = "specialization_interrupted_canceled";
    private const string SpecializationReviewNotCurrentRejection = "specialization_review_not_current";

    // Store-only evidence witnessed under the operation lease BEFORE either publication.
    // Hashes bind complete persisted records, including local/delegated history and retained
    // source manifests. They authorize cancellation only, never economic execution.
    private sealed record SpecializationRecoveryPredecessor(Guid OperationId, string CommandDigest,
        string BeforeRecordDigest, bool BeforeReceiptsNull, string? PendingRecordDigest = null,
        // Omission preserves the exact serialization/digest of pre-decision witnesses.
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ReviewRejected = false);

    private SpecializationOriginalArchive BindNewSpecializationRecoveryPredecessors(OwnerScope owner,
        CharacterWorkspaceId id, string path, SpecializationOriginalArchive proposed,
        CharacterCareerSpecializationReceipt? observedRejection)
    {
        var before = ReadSpecializationOriginalArchive(owner, id, path);
        var additions = proposed.Entries.Where(p => p.Outcome == CharacterCareerSpecializationOutcome.Pending
            && !before.Entries.Any(old => old.OperationId == p.OperationId)).ToArray();
        if (additions.Length == 0)
        {
            if (observedRejection is not null) throw new IOException("specialization_recovery_predecessor_unavailable");
            return proposed; // Never backfill old, unwitnessed reservations or decisions.
        }
        if (additions.Length != 1 || before.Entries.Any(p => p.Outcome == CharacterCareerSpecializationOutcome.Pending
            && IsUnresolvedSpecializationReservation(p, ReadPrivateRecord(path).AuxiliaryState?.CharacterCareerSpecializationReceipts)))
            throw new IOException("specialization_recovery_predecessor_unavailable");
        var record = ReadPrivateRecord(path);
        var original = additions[0];
        if (!TryMaterializeRecord(record, out var document, out var revision, out var savedRevision, out bool migration)
            || migration || record.LocalHistory is not { } history || !history.IsValid(revision)
            || history.IncarnationId != original.IncarnationId || ReadContinuationSlotUnderLease(path) != original.SlotGeneration
            || !CharacterCareerSpecializationTransaction.IsValidHistory(new(id, document, revision, savedRevision, DateTimeOffset.UnixEpoch)
                { LocalHistory = history })
            || document.AuxiliaryState.CharacterCareerSpecializationReceipts?.Any(r =>
                r.Command.Request.OperationId == original.OperationId || r.Outcome == CharacterCareerSpecializationOutcome.Pending) == true)
            throw new IOException("specialization_recovery_predecessor_unavailable");
        if (observedRejection is not null && (!CharacterCareerSpecializationTransaction.IsCoherent(observedRejection)
            || observedRejection.Outcome != CharacterCareerSpecializationOutcome.Rejected
            || observedRejection.Rejection != SpecializationReviewNotCurrentRejection
            || observedRejection.RecordedRevision != revision
            || !CharacterCareerSpecializationTransaction.MatchesOriginal(observedRejection.Command, original)))
            throw new IOException("specialization_recovery_predecessor_unavailable");
        var proof = new SpecializationRecoveryPredecessor(original.OperationId, original.CommandDigest,
            CharacterCreationFinalizationDigest.Compute(record), document.AuxiliaryState.CharacterCareerSpecializationReceipts is null,
            ReviewRejected: observedRejection is not null);
        return SealSpecializationArchive(proposed.Entries, (proposed.Recovery ?? []).Append(proof).ToArray());
    }

    private void RetainSpecializationPendingPredecessor(string path, CharacterCareerSpecializationReceipt receipt,
        PersistedWorkspaceRecord pendingRecord)
    {
        if (receipt.Outcome != CharacterCareerSpecializationOutcome.Pending) return;
        var command = receipt.Command;
        var owner = command.Binding.TrustedLocalOwner ? OwnerScope.LocalSingleUser : new OwnerScope(command.Binding.OwnerId);
        var archive = ReadSpecializationOriginalArchive(owner, command.Request.WorkspaceId, path);
        var proof = archive.Recovery?.SingleOrDefault(p => p.OperationId == command.Request.OperationId);
        if (proof is null || proof.ReviewRejected || proof.CommandDigest != receipt.CommandDigest || proof.PendingRecordDigest is not null
            || proof.BeforeRecordDigest != CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path))
            || !CharacterCareerSpecializationTransaction.MatchesOriginal(command,
                archive.Entries.SingleOrDefault(p => p.OperationId == command.Request.OperationId)))
            throw new IOException("specialization_recovery_predecessor_unavailable");
        // A fault before/after this archive write still leaves the unchanged predecessor
        // recoverable. No workspace or economic mutation is retried here.
        var updated = archive.Recovery!.Select(p => p.OperationId == proof.OperationId
            ? p with { PendingRecordDigest = CharacterCreationFinalizationDigest.Compute(pendingRecord) } : p).ToArray();
        WriteSpecializationOriginalArchive(owner, command.Request.WorkspaceId, path, SealSpecializationArchive(archive.Entries, updated));
    }

    private static bool ValidSpecializationRecoveryArchive(SpecializationOriginalArchive archive)
        => archive.Recovery is null || archive.Recovery.Count <= archive.Entries.Count
            && archive.Recovery.All(p => p is not null && p.OperationId != Guid.Empty
                && CharacterCareerSpecializationTransaction.IsDigest(p.CommandDigest)
                // Record witnesses use the canonical prefixed digest produced by Compute(record).
                && CharacterCreationFinalizationDigest.IsCanonical(p.BeforeRecordDigest)
                && (p.PendingRecordDigest is null || CharacterCreationFinalizationDigest.IsCanonical(p.PendingRecordDigest))
                && (!p.ReviewRejected || p.PendingRecordDigest is null)
                && archive.Entries.Any(original => original.OperationId == p.OperationId && original.CommandDigest == p.CommandDigest))
            && archive.Recovery.Select(p => p.OperationId).Distinct().Count() == archive.Recovery.Count;

    private bool MatchesSpecializationRecoveryPredecessor(string path, PersistedWorkspaceRecord record,
        SpecializationOriginalArchive archive, CharacterCareerSpecializationCommand command, out bool reviewRejected)
    {
        reviewRejected = false;
        var original = archive.Entries.SingleOrDefault(p => p.OperationId == command.Request.OperationId);
        var proof = archive.Recovery?.SingleOrDefault(p => p.OperationId == command.Request.OperationId);
        if (original?.Outcome != CharacterCareerSpecializationOutcome.Pending || proof is null
            || !CharacterCareerSpecializationTransaction.MatchesOriginal(command, original)
            || proof.CommandDigest != original.CommandDigest
            || ReadContinuationSlotUnderLease(path) != command.Binding.SlotGeneration
            || !TryMaterializeRecord(record, out var document, out var revision, out var savedRevision, out bool migration)
            || migration
            || record.LocalHistory is not { } history || history.IncarnationId != command.Binding.IncarnationId || !history.IsValid(revision)
            || !CharacterCareerSpecializationTransaction.IsValidHistory(new(command.Request.WorkspaceId, document,
                revision, savedRevision, DateTimeOffset.UnixEpoch) { LocalHistory = history })) return false;
        var entries = document.AuxiliaryState.CharacterCareerSpecializationReceipts;
        var pending = entries?.SingleOrDefault(r => r.Command.Request.OperationId == command.Request.OperationId);
        string recordDigest = CharacterCreationFinalizationDigest.Compute(record);
        if (pending is null)
        {
            if (recordDigest != proof.BeforeRecordDigest) return false;
        }
        else if (pending.Outcome != CharacterCareerSpecializationOutcome.Pending
            || !CharacterCareerSpecializationTransaction.Same(pending.Command, command)
            || recordDigest != proof.PendingRecordDigest) return false;
        var preceding = (entries ?? []).Where(r => r.Command.Request.OperationId != command.Request.OperationId).ToArray();
        if (preceding.Any(r => r.Outcome == CharacterCareerSpecializationOutcome.Pending)
            || proof.BeforeReceiptsNull && preceding.Length != 0) return false;
        if (proof.ReviewRejected)
        {
            // Commit already selected its non-economic rejection branch before witnessing
            // this exact record. The older quote need not describe that record. This is
            // never inferred from XML equality, nor usable after any witnessed-record edit.
            reviewRejected = pending is null && proof.PendingRecordDigest is null
                && revision >= command.Binding.ContentRevision && savedRevision >= command.Binding.SavedRevision;
            return reviewRejected;
        }
        if (revision != command.Binding.ContentRevision || savedRevision != command.Binding.SavedRevision) return false;
        var auxiliary = pending is null ? document.AuxiliaryState : document.AuxiliaryState with
            { CharacterCareerSpecializationReceipts = proof.BeforeReceiptsNull ? null : preceding };
        var predecessor = document with { State = document.State with { AuxiliaryState = auxiliary } };
        string sourceDigest = CharacterCareerSpecializationTransaction.Hash(JsonSerializer.Serialize(new
            { document.Format, document.RulesetId, document.SchemaVersion, document.PayloadKind,
                PayloadDigest = CharacterCareerSpecializationTransaction.Hash(document.Content) }));
        return sourceDigest == command.Binding.SourceDigest && predecessor.AuxiliaryStateDigest == command.Binding.AuxiliaryDigest;
    }

    private CharacterCareerSpecializationResult CancelInterruptedSpecializationUnderLease(OwnerScope owner, string path,
        CharacterCareerSpecializationCommand command, IOwnerContextAccessor? ownerContext, IOwnerContextLease? ownerLease,
        OwnerContextStamp ownerStamp, CancellationToken cancellationToken)
    {
        if (ownerContext is not IOwnerContextLeaseAccessor authority || ownerLease is null || !ownerStamp.IsValid
            || ownerStamp.Owner != owner || ownerLease.Stamp != ownerStamp || authority.Capture() != ownerStamp)
            return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_recovery_owner_lease_unavailable");
        var archive = ReadSpecializationOriginalArchive(owner, command.Request.WorkspaceId, path);
        var before = ReadPrivateRecord(path);
        if (!MatchesSpecializationRecoveryPredecessor(path, before, archive, command, out bool reviewRejected))
            return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_recovery_provenance_unavailable");
        var read = ReadGearWorkspaceUnderLease(owner, command.Request.WorkspaceId, path, ownerContext, ownerLease, ownerStamp,
            out var delegatedLedger, out var environment);
        using var current = environment;
        if (!read.Success || read.Value is not { } saved) return WorkspaceCharacterCareerSpecializationService.MapReadFailure(read);
        if (CharacterCreationFinalizationDigest.Compute(before) != CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)))
            return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_recovery_predecessor_changed");
        var rejected = CharacterCareerSpecializationTransaction.Seal(command, CharacterCareerSpecializationOutcome.Rejected,
            saved.ContentRevision, rejection: reviewRejected ? SpecializationReviewNotCurrentRejection : InterruptedSpecializationRejection);
        var entries = saved.Document.AuxiliaryState.CharacterCareerSpecializationReceipts ?? [];
        var document = saved.Document with { State = saved.Document.State with
        { AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerSpecializationReceipts = entries
            .Where(r => r.Command.Request.OperationId != command.Request.OperationId).Append(rejected).ToArray() } } };
        var after = saved with { Document = document };
        if (!IsValidAuxiliaryState(saved.Id, saved.ContentRevision, document.AuxiliaryState, current?.Recovery))
            return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_auxiliary_invalid");
        var record = BuildPersistedRecord(document, saved.ContentRevision, saved.SavedRevision, saved.LocalHistory!,
            delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
        CharacterCurrentRuleEnvironmentReadmissionResult? readmission = null;
        if (current is not null)
        {
            readmission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                _retainedCharacterQueries!, after, cancellationToken);
            if (readmission.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution || readmission.Blockers.Count != 0)
                return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_after_current_admission_unavailable");
            current.Recovery.RetainAdmittedCapture(new(capture, _retainedCharacterQueries!), capture.IntegrityDigest);
            var snapshot = new WorkspaceContinuationSnapshot(owner.NormalizedValue,
                new(saved.Id, document, saved.LastUpdatedUtc, saved.ContentRevision, saved.SavedRevision), []);
            record = record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion,
                RetainedRuleSources = current.Record.RetainedRuleSources! with
                { References = current.Recovery.SnapshotReferences(), CurrentEnvironment = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(
                    snapshot, capture, execution.ExecutionProfile, execution.ExecutionProfileFingerprint, []) } };
        }
        else record = PreserveRetainedRuleSources(path, record, WorkspaceWriteDisposition.ReplaceExisting);
        try
        {
            using var admission = new SpecializationWriteAdmission(this, path, rejected, record, current, after, readmission,
                command.Binding.SlotGeneration, ownerLease, ownerContext, ownerStamp, cancellationToken, interruptedRecovery: true);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                // Include the archive in the final fence as well as the existing record/slot/account/source fences.
                if (!CharacterCareerSpecializationTransaction.Same(archive, ReadSpecializationOriginalArchive(owner, saved.Id, path)))
                    throw new IOException("specialization_recovery_original_changed");
            });
        }
        catch (Exception e) when (SpecializationStorageFailure(e) || e is OperationCanceledException)
        { /* Reconcile only; never replay Commit or infer a terminal result from a failed write. */ }
        return LookupSpecializationOriginalUnderLease(owner, saved.Id, path, command.Request.OperationId,
            CharacterCareerSpecializationTransaction.CommandDigest(command), command.Binding.IncarnationId, command.Binding.SlotGeneration);
    }

    private void ValidateInterruptedSpecializationRecovery(string path, PersistedWorkspaceRecord before,
        PersistedWorkspaceRecord after, CharacterCareerSpecializationReceipt receipt)
    {
        if (receipt.Outcome != CharacterCareerSpecializationOutcome.Rejected
            || !MatchesSpecializationRecoveryPredecessor(path, before, ReadSpecializationOriginalArchiveForPath(path), receipt.Command,
                out bool reviewRejected)
            || receipt.Rejection != (reviewRejected ? SpecializationReviewNotCurrentRejection : InterruptedSpecializationRejection)
            // Only this operation's auxiliary receipt and its canonically readmitted manifest may change.
            || !CharacterCareerSpecializationTransaction.Same(before, after with
                { AuxiliaryState = before.AuxiliaryState, RetainedRuleSources = before.RetainedRuleSources }))
            throw new IOException("specialization_recovery_predecessor_changed");
    }

    private CharacterCareerSpecializationResult ConfirmDurableInterruptedSpecializationUnderLease(string path,
        CharacterCareerSpecializationResult result)
    {
        if (result.Receipt?.Rejection is not (InterruptedSpecializationRejection or SpecializationReviewNotCurrentRejection)) return result;
        try
        {
            var before = ReadPrivateRecord(path);
            var receipt = result.Receipt!;
            if (before.LocalHistory?.IncarnationId != receipt.Command.Binding.IncarnationId
                || ReadContinuationSlotUnderLease(path) != receipt.Command.Binding.SlotGeneration
                || !CharacterCareerSpecializationTransaction.Same(receipt,
                    before.AuxiliaryState?.CharacterCareerSpecializationReceipts?.SingleOrDefault(r =>
                        r.Command.Request.OperationId == receipt.Command.Request.OperationId)))
                return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_result_unknown");
            ThrowIfLinkOrReparsePoint(path, "Specialization canceled workspace");
            using (var stream = new FileStream(path, new FileStreamOptions
            { Mode = FileMode.Open, Access = FileAccess.ReadWrite, Share = FileShare.Read, Options = FileOptions.WriteThrough, BufferSize = FileBufferSize }))
                stream.Flush(flushToDisk: true);
            _ = FlushRetainedDirectoryChain(Path.GetDirectoryName(path)!);
            if (!CharacterCareerSpecializationTransaction.Same(before, ReadPrivateRecord(path)))
                return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_result_unknown");
            return result;
        }
        catch (Exception e) when (SpecializationStorageFailure(e))
        { return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_durability_unavailable"); }
    }
}
