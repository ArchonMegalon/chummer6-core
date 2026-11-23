using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerSpecializationAtomicCommitCapability
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _issuedSpecializations = new(StringComparer.Ordinal);
    // Root implements this only with the shared writer/deletion hooks in the proposal.
    static partial void VerifyCareerSpecializationGuards(ref bool installed);
    public bool CareerSpecializationGuardsInstalled
    { get { bool installed = false; VerifyCareerSpecializationGuards(ref installed); return installed; } }
    public string CareerSpecializationRuntimeDigest => CharacterCareerSpecializationTransaction.Hash(
        "chummer.specialization.runtime/v1\0" + typeof(CharacterCareerSkillSpecializationRules).Module.ModuleVersionId + "\0"
        + typeof(CharacterCareerSpecializationAuthority).Module.ModuleVersionId + "\0" + typeof(FileWorkspaceStore).Module.ModuleVersionId);

    public CharacterCareerSpecializationResult CommitCareerSpecialization(OwnerScope owner,
        CharacterCareerSpecializationCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default)
        => CommitCareerSpecialization(owner, command, sources, null, null, default, cancellationToken);

    internal CharacterCareerSpecializationResult CommitCareerSpecialization(OwnerScope owner,
        CharacterCareerSpecializationCommand command, ICharacterSourceDataResolver sources,
        IOwnerContextAccessor? ownerContext, IOwnerContextLease? ownerLease, OwnerContextStamp ownerStamp, CancellationToken cancellationToken = default)
    {
        if (CharacterCareerSpecializationTransaction.FreezeConfirmedCommand(command) is not { } frozen)
            return new(CharacterCareerSpecializationOutcome.Unavailable);
        command = frozen;
        if (ownerLease is null || ownerContext is not IOwnerContextLeaseAccessor authority || ownerLease.Stamp != ownerStamp || authority.Capture() != ownerStamp
            || command.Binding.OwnerAuthorityInstanceId != ownerStamp.AuthorityInstanceId
            || command.Binding.OwnerTransitionRevision != ownerStamp.TransitionRevision)
            return new(CharacterCareerSpecializationOutcome.Conflict, Error: "specialization_owner_changed");
        if (!CareerSpecializationGuardsInstalled || !CharacterCareerSpecializationTransaction.IsConfirmedCommand(command)
            || !CharacterCareerSpecializationTransaction.MatchesOwner(owner, command.Binding))
            return new(CharacterCareerSpecializationOutcome.Unavailable);
        string? path = TryGetPath(owner, command.Request.WorkspaceId);
        if (path is null) return new(CharacterCareerSpecializationOutcome.Unavailable);
        string digest = CharacterCareerSpecializationTransaction.CommandDigest(command);
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var prior = LookupSpecializationOriginalUnderLease(owner, command.Request.WorkspaceId, path, command.Request.OperationId,
                digest, command.Binding.IncarnationId, command.Binding.SlotGeneration);
            if (prior.Outcome != CharacterCareerSpecializationOutcome.NotFound) return prior;
            if (!_issuedSpecializations.TryRemove(digest, out _))
                return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_requires_lookup");
            var read = ReadGearWorkspaceUnderLease(owner, command.Request.WorkspaceId, path,
                ownerContext, ownerLease, ownerStamp, out var delegatedLedger, out var currentEnvironment);
            using var current = currentEnvironment;
            if (!read.Success || read.Value is not { } saved) return WorkspaceCharacterCareerSpecializationService.MapReadFailure(read);
            string? slot = ReadContinuationSlotUnderLease(path);
            if (saved.LocalHistory?.IncarnationId != command.Binding.IncarnationId || slot != command.Binding.SlotGeneration)
                return new(CharacterCareerSpecializationOutcome.Conflict, Error: "specialization_incarnation_changed");
            var history = saved.Document.AuxiliaryState.CharacterCareerSpecializationReceipts ?? [];
            if (history.Count >= CharacterCareerSpecializationTransaction.MaximumReceipts
                || history.Any(r => r.Outcome == CharacterCareerSpecializationOutcome.Pending))
                return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_history_retained");

            WorkspaceDocument? replacement = null; CharacterCareerSpecializationReceipt? receipt = null;
            bool valid = !cancellationToken.IsCancellationRequested
                && TryReadSpecializationCatalog(owner, saved, sources, current, out var catalog, slot, ownerStamp, cancellationToken)
                && CharacterCareerSpecializationTransaction.TryBuild(saved, catalog!, command, out replacement, out receipt);
            var pending = CharacterCareerSpecializationTransaction.Seal(command, CharacterCareerSpecializationOutcome.Pending,
                command.Binding.ContentRevision);
            var archive = ReadSpecializationOriginalArchive(owner, saved.Id, path);
            if (archive.Entries.Any(p => IsUnresolvedSpecializationReservation(p, history)))
                return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_reservation_unresolved");
            var reservedArchive = SealSpecializationArchive(archive.Entries.Append(CharacterCareerSpecializationTransaction.Original(pending)).ToArray(), archive.Recovery);
            if (!FitsSpecializationTerminalArchive(reservedArchive))
                return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_archive_capacity");
            var rejected = CharacterCareerSpecializationTransaction.Seal(command, CharacterCareerSpecializationOutcome.Rejected,
                saved.ContentRevision, rejection: SpecializationReviewNotCurrentRejection);
            WorkspaceDocument WithReceipt(CharacterCareerSpecializationReceipt entry) => saved.Document with
                { State = saved.Document.State with { AuxiliaryState = saved.Document.AuxiliaryState with
                    { CharacterCareerSpecializationReceipts = history.Append(entry).ToArray() } } };
            var reserved = WithReceipt(pending);
            var rejectedDocument = WithReceipt(rejected);
            if (current is not null)
            {
                // Refuse deterministic canonical/capture-capacity failures before reserving
                // original-operation custody. Publication still freshly admits each actual predecessor.
                var first = saved with { Document = valid ? reserved : rejectedDocument };
                var firstAdmission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                    _retainedCharacterQueries!, first, cancellationToken);
                RequireProspectiveAdmission(first, firstAdmission);
                if (valid)
                {
                    var final = saved with { Document = replacement!, ContentRevision = receipt!.RecordedRevision,
                        SavedRevision = receipt.RecordedRevision };
                    var finalAdmission = firstAdmission.AfterExecution!.ReadmitProposedCurrentEnvironment(first,
                        firstAdmission.AfterCapture!, _retainedCharacterQueries!, final, cancellationToken);
                    RequireProspectiveAdmission(final, finalAdmission);
                }
            }
            current?.ValidateFinalFence();
            WriteSpecializationOriginalArchive(owner, saved.Id, path, reservedArchive, valid ? null : rejected);
            if (!valid)
            {
                // A genuinely observed pre-write rejection is durable; no later inference.
                Write(rejectedDocument, saved.ContentRevision, saved.SavedRevision, rejected);
                return new(CharacterCareerSpecializationOutcome.Rejected, rejected, saved.ContentRevision);
            }
            Write(reserved, saved.ContentRevision, saved.SavedRevision, pending);
            // No other writer can pass the operation lease or the durable pending fence.
            Write(replacement!, receipt!.RecordedRevision, receipt.RecordedRevision, receipt, () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ownerLease.Stamp != ownerStamp || authority.Capture() != ownerStamp
                    || !TryReadSpecializationCatalog(owner, saved, sources, current, out var finalCatalog, slot, ownerStamp, cancellationToken)
                    || !CharacterCareerSpecializationTransaction.TryBuild(saved, finalCatalog!, command, out var finalDocument, out var finalReceipt)
                    || !CharacterCareerSpecializationTransaction.Same(finalDocument, replacement)
                    || !CharacterCareerSpecializationTransaction.Same(finalReceipt, receipt))
                    throw new IOException("specialization_quote_changed_before_replace");
                cancellationToken.ThrowIfCancellationRequested();
            });
            return new(CharacterCareerSpecializationOutcome.Applied, receipt, receipt.RecordedRevision);

            void RequireProspectiveAdmission(WorkspaceStoredDocument proposed, CharacterCurrentRuleEnvironmentReadmissionResult observed)
            {
                if (observed.AfterCapture is not { } capture || observed.AfterExecution is not { } execution || observed.Blockers.Count != 0
                    || !IsValidAuxiliaryState(saved.Id, proposed.ContentRevision, proposed.Document.AuxiliaryState, current!.Recovery))
                    throw new IOException("specialization_proposed_current_admission_unavailable");
                var references = current.Record.RetainedRuleSources!.References;
                if (!references.Any(value => value.CaptureDigest == capture.IntegrityDigest)
                    && references.Length >= MaximumRetainedRuleSourceReferences)
                    throw new IOException("specialization_retained_capture_capacity");
                var bytes = capture.ExportPrivateInputs();
                if (bytes.Length == 0 || bytes.Length > RetainedCharacterRuleSourceInputs.MaximumArchiveBytes
                    || RetainedCharacterRuleSourceInputs.Digest(bytes) != capture.IntegrityDigest)
                    throw new IOException("specialization_retained_capture_capacity");
                // Size the actual candidate shape before a durable pending reservation.
                // This is only a prospective size check; the writer independently publishes
                // and reopens the real archive and obtains a fresh canonical pair below.
                var reference = new CharacterCreationRetainedRuleSourceReference(1, references[0].OwnerBinding,
                    saved.Id.Value, saved.LocalHistory!.IncarnationId, capture.IntegrityDigest, bytes.LongLength);
                var prospectiveReferences = references.Any(value => value.CaptureDigest == capture.IntegrityDigest)
                    ? references : references.Append(reference).OrderBy(value => value.CaptureDigest, StringComparer.Ordinal).ToArray();
                var record = BuildCurrentRecord(proposed, current.Record.RetainedRuleSources!, prospectiveReferences, capture, execution);
                if (JsonSerializer.SerializeToUtf8Bytes(record).Length > MaximumRetainedWorkspaceRecordBytes)
                    throw new IOException("specialization_retained_record_capacity");
            }

            PersistedWorkspaceRecord BuildCurrentRecord(WorkspaceStoredDocument proposed, RetainedRuleSourceManifest manifest,
                CharacterCreationRetainedRuleSourceReference[] references, CharacterCreationRuleSourceCapture capture,
                CharacterCurrentRuleEnvironmentExecution execution)
            {
                var snapshot = new WorkspaceContinuationSnapshot(owner.NormalizedValue,
                    new(saved.Id, proposed.Document, proposed.LastUpdatedUtc, proposed.ContentRevision, proposed.SavedRevision), []);
                var environment = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(snapshot, capture,
                    execution.ExecutionProfile, execution.ExecutionProfileFingerprint, []);
                return BuildPersistedRecord(proposed.Document, proposed.ContentRevision, proposed.SavedRevision,
                    saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts) with
                {
                    RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion,
                    RetainedRuleSources = manifest with { References = references, CurrentEnvironment = environment }
                };
            }

            void Write(WorkspaceDocument document, long revision, long savedRevision,
                CharacterCareerSpecializationReceipt admitted, Action? fence = null)
            {
                var record = BuildPersistedRecord(document, revision, savedRevision, saved.LocalHistory!,
                    delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
                // Each persisted state (including pending/rejected auxiliary-only states)
                // receives its own canonical admission against the actual previous record.
                var beforeRead = ReadGearWorkspaceUnderLease(owner, saved.Id, path, ownerContext, ownerLease, ownerStamp,
                    out _, out var beforeCurrent);
                using var actualCurrent = beforeCurrent;
                if (!beforeRead.Success || beforeRead.Value is not { } actualBefore
                    || actualBefore.LocalHistory?.IncarnationId != command.Binding.IncarnationId
                    || ReadContinuationSlotUnderLease(path) != command.Binding.SlotGeneration)
                    throw new IOException("specialization_before_write_changed");
                if (!IsValidAuxiliaryState(saved.Id, revision, document.AuxiliaryState, actualCurrent?.Recovery))
                    throw new IOException("specialization_auxiliary_invalid");
                var proposedAfter = actualBefore with { Document = document, ContentRevision = revision, SavedRevision = savedRevision };
                CharacterCurrentRuleEnvironmentReadmissionResult? readmission = null;
                if (actualCurrent is not null)
                {
                    readmission = actualCurrent.Execution.ReadmitProposedCurrentEnvironment(actualBefore,
                        actualCurrent.Capture, _retainedCharacterQueries!, proposedAfter, cancellationToken);
                    if (readmission.AfterCapture is not { } afterCapture || readmission.AfterExecution is not { } afterExecution
                        || readmission.Blockers.Count != 0) throw new IOException("specialization_after_current_admission_unavailable");
                    actualCurrent.Recovery.RetainAdmittedCapture(new(afterCapture, _retainedCharacterQueries!), afterCapture.IntegrityDigest);
                    record = BuildCurrentRecord(proposedAfter, actualCurrent.Record.RetainedRuleSources!,
                        actualCurrent.Recovery.SnapshotReferences(), afterCapture, afterExecution);
                }
                else
                {
                    if (current is not null) throw new IOException("specialization_current_environment_lost");
                    record = PreserveRetainedRuleSources(path, record, WorkspaceWriteDisposition.ReplaceExisting);
                }
                // Private store/path/operation/full-record binding; no request can create it.
                using var admission = new SpecializationWriteAdmission(this, path, admitted, record, actualCurrent,
                    proposedAfter, readmission, command.Binding.SlotGeneration, ownerLease, ownerContext, ownerStamp, cancellationToken);
                WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: fence);
            }
        }
        catch (Exception e) when (SpecializationStorageFailure(e) || e is OperationCanceledException)
        { return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_result_unknown"); }
    }

    // Private per-instance, thread-local admission; concurrent workspaces cannot borrow it.
    [ThreadStatic] private static SpecializationWriteAdmission? _specializationAdmission;
    private sealed class SpecializationWriteAdmission : IDisposable
    {
        private readonly SpecializationWriteAdmission? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal CharacterCareerSpecializationReceipt Receipt { get; }
        internal string RecordDigest { get; }
        internal GearCurrentEnvironment? Current { get; }
        internal bool InterruptedRecovery { get; }
        private readonly WorkspaceStoredDocument _after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? _readmission;
        private readonly string _slot;
        private readonly string _beforeDigest;
        private readonly IOwnerContextLease _ownerLease;
        private readonly IOwnerContextAccessor _ownerContext;
        private readonly OwnerContextStamp _ownerStamp;
        private readonly CancellationToken _cancellationToken;
        internal SpecializationWriteAdmission(FileWorkspaceStore store, string path, CharacterCareerSpecializationReceipt receipt,
            PersistedWorkspaceRecord record, GearCurrentEnvironment? current, WorkspaceStoredDocument after,
            CharacterCurrentRuleEnvironmentReadmissionResult? readmission, string slot,
            IOwnerContextLease ownerLease, IOwnerContextAccessor ownerContext, OwnerContextStamp ownerStamp, CancellationToken cancellationToken,
            bool interruptedRecovery = false)
        {
            _previous = _specializationAdmission; Store = store; Path = System.IO.Path.GetFullPath(path); Receipt = receipt;
            RecordDigest = CharacterCreationFinalizationDigest.Compute(record); Current = current;
            _after = after; _readmission = readmission; _slot = slot;
            _ownerLease = ownerLease; _ownerContext = ownerContext; _ownerStamp = ownerStamp; _cancellationToken = cancellationToken;
            _beforeDigest = CharacterCreationFinalizationDigest.Compute(store.ReadPrivateRecord(path));
            InterruptedRecovery = interruptedRecovery;
            store.RetainSpecializationPendingPredecessor(path, receipt, record);
            _specializationAdmission = this;
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            if (FileWorkspaceStore.ReadContinuationSlotUnderLease(Path) != _slot
                || CharacterCreationFinalizationDigest.Compute(Store.ReadPrivateRecord(Path)) != _beforeDigest
                || CharacterCreationFinalizationDigest.Compute(record) != RecordDigest)
                throw new IOException("specialization_exact_record_or_slot_changed");
            if (Current is not null)
            {
                Current.ValidateFinalFence();
                if (_readmission?.AfterCapture is not { } capture || _readmission.AfterExecution is not { } execution
                    || !execution.Revalidate(_after, capture, Store._retainedCharacterQueries!, _cancellationToken))
                    throw new IOException("specialization_after_current_environment_changed");
            }
            if (_ownerLease.Stamp != _ownerStamp || _ownerContext is not IOwnerContextLeaseAccessor authority
                || authority.Capture() != _ownerStamp)
                throw new IOException("specialization_owner_changed_before_replace");
            _cancellationToken.ThrowIfCancellationRequested();
        }
        public void Dispose() => _specializationAdmission = _previous;
    }

    private SpecializationWriteAdmission? SpecializationAdmissionFor(string path)
        => _specializationAdmission is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, Path.GetFullPath(path)) ? admission : null;

    private void RevalidateSpecializationForWrite(string path, PersistedWorkspaceRecord record)
        => SpecializationAdmissionFor(path)?.ValidateFinalFence(record);

    private bool TryReadSpecializationCatalog(OwnerScope owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        GearCurrentEnvironment? current, out CharacterCareerSpecializationCatalog? catalog, string? slot,
        OwnerContextStamp ownerStamp, CancellationToken cancellationToken = default)
    {
        catalog = null;
        if (!ownerStamp.IsValid || ownerStamp.Owner != owner) return false;
        bool success = current is null
            ? CharacterCareerSpecializationTransaction.TryRead(owner, saved, sources, CareerSpecializationRuntimeDigest, out catalog, slot)
            : CharacterCareerSpecializationTransaction.TryReadCurrentEnvironment(owner, saved, current.Capture, current.Execution,
                _retainedCharacterQueries!, CareerSpecializationRuntimeDigest, out catalog, slot, cancellationToken);
        if (!success || catalog is null) return false;
        catalog = catalog with { Binding = catalog.Binding with { OwnerAuthorityInstanceId = ownerStamp.AuthorityInstanceId,
            OwnerTransitionRevision = ownerStamp.TransitionRevision } };
        return true;
    }

    private void ValidateSpecializationWrite(string path, PersistedWorkspaceRecord record, WorkspaceWriteDisposition disposition)
    {
        if (record.AuxiliaryState is { } proposedAuxiliary && !WorkspaceDocumentAuxiliaryState.HasSafeExtensionData(proposedAuxiliary))
            throw new IOException("Auxiliary extension data cannot shadow typed workspace state.");
        var previous = disposition == WorkspaceWriteDisposition.ReplaceExisting ? ReadPrivateRecord(path) : null;
        var before = previous?.AuxiliaryState?.CharacterCareerSpecializationReceipts;
        var reservations = ReadSpecializationOriginalArchiveForPath(path).Entries;
        var after = record.AuxiliaryState?.CharacterCareerSpecializationReceipts;
        var admission = SpecializationAdmissionFor(path);
        var admitted = admission?.Receipt;
        if (reservations.Any(p => IsUnresolvedSpecializationReservation(p, before)
            && (admitted is null || !CharacterCareerSpecializationTransaction.MatchesOriginal(admitted.Command, p))))
            throw new IOException("An unresolved original specialization reservation retains write custody.");
        if (admitted is null)
        {
            bool retiring = IsSpecializationIncarnationReplacement(previous, record);
            if (before?.Any(r => r.Outcome == CharacterCareerSpecializationOutcome.Pending) == true
                || previous is not null && !retiring && !CharacterCareerSpecializationTransaction.Same(before, after))
                throw new IOException("Original Specialization operation must be resolved; its history is immutable.");
            // A new file may retain imported history, but cannot manufacture locally
            // replayable receipts or import unresolved custody as a new operation.
            if ((previous is null || retiring) && after is { Count: > 0 } && (record.LocalHistory is not { } history
                || after.Any(r => r.Outcome == CharacterCareerSpecializationOutcome.Pending
                    || !history.IsImportedRevision(r.RecordedRevision))))
                throw new IOException("New Specialization history must remain imported, never original-operation authority.");
            return;
        }
        if (previous is null || after is null || !CharacterCareerSpecializationTransaction.IsCoherent(admitted))
            throw new IOException("Specialization authority requires an existing workspace.");
        var retained = (before ?? []).Where(r => r.Command.Request.OperationId != admitted.Command.Request.OperationId).ToArray();
        if (!CharacterCareerSpecializationTransaction.Same(after, retained.Append(admitted).ToArray()))
            throw new IOException("Specialization authority cannot replace other operations.");
        var original = before?.SingleOrDefault(r => r.Command.Request.OperationId == admitted.Command.Request.OperationId);
        if (admission?.InterruptedRecovery == true)
            ValidateInterruptedSpecializationRecovery(path, previous, record, admitted);
        else if (admitted.Outcome == CharacterCareerSpecializationOutcome.Applied)
        {
            if (original?.Outcome != CharacterCareerSpecializationOutcome.Pending || original.CommandDigest != admitted.CommandDigest
                || previous.ContentRevision != admitted.Command.Binding.ContentRevision
                || record.ContentRevision != previous.ContentRevision + 1 || record.SavedRevision != record.ContentRevision)
                throw new IOException("Specialization completion requires the exact original pending operation.");
        }
        else if (original is not null || record.ContentRevision != previous.ContentRevision || record.SavedRevision != previous.SavedRevision
            || record.Format != previous.Format || !CharacterCareerSpecializationTransaction.Same(record.Envelope, previous.Envelope))
            throw new IOException("Specialization reservation/rejection cannot change character content.");
        if (!CharacterCareerSpecializationTransaction.Same(record.AuxiliaryState! with { CharacterCareerSpecializationReceipts = before },
            previous.AuxiliaryState ?? WorkspaceDocumentAuxiliaryState.Empty))
            throw new IOException("Specialization must preserve every other auxiliary ledger.");
    }

    public CharacterCareerSpecializationReadResult ReadCareerSpecialization(OwnerScope owner, CharacterWorkspaceId workspace,
        ICharacterSourceDataResolver sources)
        => ReadCareerSpecialization(owner, workspace, sources, null, null, default);

    internal CharacterCareerSpecializationReadResult ReadCareerSpecialization(OwnerScope owner, CharacterWorkspaceId workspace,
        ICharacterSourceDataResolver sources, IOwnerContextAccessor? ownerContext, IOwnerContextLease? ownerLease, OwnerContextStamp ownerStamp)
    {
        try
        {
            string? path = TryGetPath(owner, workspace);
            if (!CareerSpecializationGuardsInstalled || path is null || (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)))
                return new(CharacterCareerSpecializationOutcome.Unavailable);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, workspace, path, ownerContext, ownerLease, ownerStamp, out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved || HasPendingSpecialization(saved))
                return new(CharacterCareerSpecializationOutcome.Unavailable);
            if (!TryReadSpecializationCatalog(owner, saved, sources, current, out var catalog, ReadContinuationSlotUnderLease(path), ownerStamp))
                return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_saved_source_or_incarnation_unavailable");
            current?.ValidateFinalFence();
            return new(CharacterCareerSpecializationOutcome.Available, catalog);
        }
        catch (Exception e) when (SpecializationStorageFailure(e)) { return new(CharacterCareerSpecializationOutcome.Unavailable); }
    }

    public CharacterCareerSpecializationPreviewResult PreviewCareerSpecialization(OwnerScope owner,
        CharacterCareerSpecializationBinding displayed, CharacterCareerSpecializationRequest request, ICharacterSourceDataResolver sources)
        => PreviewCareerSpecialization(owner, displayed, request, sources, null, null, default);

    internal CharacterCareerSpecializationPreviewResult PreviewCareerSpecialization(OwnerScope owner,
        CharacterCareerSpecializationBinding displayed, CharacterCareerSpecializationRequest request, ICharacterSourceDataResolver sources,
        IOwnerContextAccessor? ownerContext, IOwnerContextLease? ownerLease, OwnerContextStamp ownerStamp)
    {
        try
        {
            string? path = TryGetPath(owner, request.WorkspaceId);
            if (!CareerSpecializationGuardsInstalled || path is null || !CharacterCareerSpecializationTransaction.MatchesOwner(owner, displayed))
                return new(CharacterCareerSpecializationOutcome.Unavailable);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, request.WorkspaceId, path, ownerContext, ownerLease, ownerStamp, out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved || HasPendingSpecialization(saved)
                || !TryReadSpecializationCatalog(owner, saved, sources, current, out var catalog, ReadContinuationSlotUnderLease(path), ownerStamp)
                || !CharacterCareerSpecializationTransaction.Same(catalog!.Binding, displayed))
                return new(CharacterCareerSpecializationOutcome.Conflict);
            if (!CharacterCareerSpecializationTransaction.TryPlan(catalog, request, out var command)
                || !CharacterCareerSpecializationTransaction.TryBuild(saved, catalog, command! with { ExplicitlyConfirmed = true }, out _, out _))
                return new(CharacterCareerSpecializationOutcome.Conflict, Error: "specialization_request_or_quote_invalid");
            var archive = ReadSpecializationOriginalArchive(owner, saved.Id, path);
            if (archive.Entries.Any(p => p.OperationId == request.OperationId || p.Outcome == CharacterCareerSpecializationOutcome.Pending
                && p.IncarnationId == displayed.IncarnationId && saved.Document.AuxiliaryState.CharacterCareerSpecializationReceipts?
                    .Any(r => r.Command.Request.OperationId == p.OperationId && r.Outcome != CharacterCareerSpecializationOutcome.Pending) != true))
                return new(CharacterCareerSpecializationOutcome.Conflict);
            var pending = CharacterCareerSpecializationTransaction.Original(CharacterCareerSpecializationTransaction.Seal(
                command! with { ExplicitlyConfirmed = true }, CharacterCareerSpecializationOutcome.Pending, displayed.ContentRevision));
            current?.ValidateFinalFence();
            if (!FitsSpecializationTerminalArchive(SealSpecializationArchive(archive.Entries.Append(pending).ToArray(), archive.Recovery)))
                return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_archive_capacity");
            string issued = CharacterCareerSpecializationTransaction.CommandDigest(command! with { ExplicitlyConfirmed = true });
            if (!_issuedSpecializations.TryAdd(issued, 0)) return new(CharacterCareerSpecializationOutcome.Conflict);
            return new(CharacterCareerSpecializationOutcome.Available, command);
        }
        catch (Exception e) when (SpecializationStorageFailure(e)) { return new(CharacterCareerSpecializationOutcome.Unavailable); }
    }

    public CharacterCareerSpecializationResult CancelUnadmittedCareerSpecialization(OwnerScope owner,
        CharacterCareerSpecializationCommand originalCommand, CancellationToken cancellationToken = default)
        => CancelUnadmittedCareerSpecialization(owner, originalCommand, null, null, default, cancellationToken);

    internal CharacterCareerSpecializationResult CancelUnadmittedCareerSpecialization(OwnerScope owner,
        CharacterCareerSpecializationCommand originalCommand, IOwnerContextAccessor? ownerContext,
        IOwnerContextLease? ownerLease, OwnerContextStamp ownerStamp, CancellationToken cancellationToken = default)
    {
        if (!CareerSpecializationGuardsInstalled) return new(CharacterCareerSpecializationOutcome.Unavailable);
        if (CharacterCareerSpecializationTransaction.FreezeConfirmedCommand(originalCommand) is not { } command
            || !CharacterCareerSpecializationTransaction.MatchesOwner(owner, command.Binding))
            return new(CharacterCareerSpecializationOutcome.Conflict);
        try
        {
            string? path = TryGetPath(owner, command.Request.WorkspaceId);
            if (path is null || !TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return new(CharacterCareerSpecializationOutcome.Unavailable);
            string digest = CharacterCareerSpecializationTransaction.CommandDigest(command);
            // Same process gate and cross-process file lease as Commit/Lookup. Never re-enter it.
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            CharacterCareerSpecializationResult Observe() => LookupSpecializationOriginalUnderLease(owner,
                command.Request.WorkspaceId, path, command.Request.OperationId, digest,
                command.Binding.IncarnationId, command.Binding.SlotGeneration);
            var prior = Observe();
            if (prior.Outcome == CharacterCareerSpecializationOutcome.Pending
                && prior.OriginalLocation == CharacterCareerSpecializationOriginalLocation.LiveOriginal)
                return CancelInterruptedSpecializationUnderLease(owner, path, command, ownerContext, ownerLease, ownerStamp, cancellationToken);
            if (prior.Outcome != CharacterCareerSpecializationOutcome.NotFound) return prior;
            if (prior.OriginalLocation != CharacterCareerSpecializationOriginalLocation.LiveOriginal)
                return new(CharacterCareerSpecializationOutcome.Unavailable, OriginalLocation: prior.OriginalLocation);

            // Read private metadata only: no source recovery, evaluation, migration or workspace creation.
            var record = ReadPrivateRecord(path);
            if (!TryMaterializeRecord(record, out var document, out long revision, out long savedRevision, out _)
                || record.LocalHistory is not { } history || !history.IsValid(revision))
                return new(CharacterCareerSpecializationOutcome.Corrupt);
            if (history.IncarnationId != command.Binding.IncarnationId
                || ReadContinuationSlotUnderLease(path) != command.Binding.SlotGeneration
                || command.Binding.ContentRevision > revision || command.Binding.SavedRevision > savedRevision)
                return new(CharacterCareerSpecializationOutcome.Conflict);
            var archive = ReadSpecializationOriginalArchive(owner, command.Request.WorkspaceId, path);
            if (archive.Entries.Any(p => IsUnresolvedSpecializationReservation(p, document.AuxiliaryState.CharacterCareerSpecializationReceipts)))
                return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_pending_or_capacity");
            var binding = command.Binding;
            var excluded = new CharacterCareerSpecializationOriginalOutcome(binding.OwnerId, binding.TrustedLocalOwner,
                binding.WorkspaceId, binding.IncarnationId, binding.SlotGeneration, command.Request.OperationId,
                digest, binding.ContentRevision, CharacterCareerSpecializationOutcome.NeverApplied,
                binding.ContentRevision, null, null);
            var terminal = SealSpecializationArchive(archive.Entries.Append(excluded).ToArray(), archive.Recovery);
            if (!FitsSpecializationTerminalArchive(terminal))
                return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_archive_capacity");
            cancellationToken.ThrowIfCancellationRequested();
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return new(CharacterCareerSpecializationOutcome.Unavailable);
            try { WriteSpecializationOriginalArchive(owner, command.Request.WorkspaceId, path, terminal); }
            catch (Exception e) when (SpecializationStorageFailure(e) || e is OperationCanceledException)
            { /* Still under the same lease: only readable exact proof can resolve an uncertain write. */ }
            var recovered = Observe();
            if (recovered.Outcome == CharacterCareerSpecializationOutcome.NeverApplied
                && (recovered.Receipt is not null || !CharacterCareerSpecializationTransaction.Same(excluded, recovered.Original)))
                return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_result_unknown");
            return recovered.Outcome == CharacterCareerSpecializationOutcome.NotFound
                ? new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_result_unknown") : recovered;
        }
        catch (Exception e) when (SpecializationStorageFailure(e) || e is OperationCanceledException)
        { return new(CharacterCareerSpecializationOutcome.Unavailable); }
    }

    public CharacterCareerSpecializationResult LookupCareerSpecialization(OwnerScope owner, CharacterWorkspaceId workspace,
        Guid operationId, string commandDigest, string incarnationId, string slotGeneration)
    {
        if (!CareerSpecializationGuardsInstalled || operationId == Guid.Empty
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(workspace)
            || !CharacterCareerSpecializationTransaction.IsDigest(commandDigest)
            || !CharacterCareerSpecializationTransaction.IsGeneration(incarnationId)
            || !CharacterCareerSpecializationTransaction.IsGeneration(slotGeneration)
            || (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner))) return new(CharacterCareerSpecializationOutcome.Conflict);
        try
        {
            string? path = TryGetPath(owner, workspace);
            if (path is null || !TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return new(CharacterCareerSpecializationOutcome.Unavailable);
            // No Get/migration, temp cleanup, slot rotation, archive repair or execution in lookup.
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            return LookupSpecializationOriginalUnderLease(owner, workspace, path, operationId, commandDigest, incarnationId, slotGeneration);
        }
        catch (Exception e) when (SpecializationStorageFailure(e)) { return new(CharacterCareerSpecializationOutcome.Unavailable); }
    }

    private CharacterCareerSpecializationResult LookupSpecializationOriginalUnderLease(OwnerScope owner, CharacterWorkspaceId id,
        string path, Guid operation, string digest, string incarnation, string slot)
    {
        var archive = ReadSpecializationOriginalArchive(owner, id, path);
        var original = archive.Entries.SingleOrDefault(p => p.OperationId == operation);
        if (original is not null && (original.CommandDigest != digest || original.IncarnationId != incarnation || original.SlotGeneration != slot))
            return new(CharacterCareerSpecializationOutcome.Conflict);
        WorkspaceStoredDocument? saved = null;
        try
        {
            var record = ReadPrivateRecord(path);
            if (!TryMaterializeRecord(record, out var document, out long revision, out long savedRevision, out _)
                || record.LocalHistory is not { } history || !history.IsValid(revision))
                return new(CharacterCareerSpecializationOutcome.Corrupt);
            saved = new(id, document, revision, savedRevision, new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero))
            { LocalHistory = history };
        }
        catch (FileNotFoundException) { }
        var location = saved is null ? CharacterCareerSpecializationOriginalLocation.Missing
            : saved.LocalHistory!.IncarnationId == incarnation ? CharacterCareerSpecializationOriginalLocation.LiveOriginal
            : CharacterCareerSpecializationOriginalLocation.DifferentIncarnation;
        if (saved is not null)
        {
            if (!CharacterCareerSpecializationTransaction.IsGeneration(ReadContinuationSlotUnderLease(path)))
                return new(CharacterCareerSpecializationOutcome.Corrupt);
            var live = location == CharacterCareerSpecializationOriginalLocation.LiveOriginal
                ? CharacterCareerSpecializationTransaction.Lookup(owner, saved, operation, digest)
                : new CharacterCareerSpecializationResult(CharacterCareerSpecializationOutcome.NotFound);
            if (live.Receipt is { } receipt)
            {
                if (original is null || location != CharacterCareerSpecializationOriginalLocation.LiveOriginal
                    || !CharacterCareerSpecializationTransaction.MatchesOriginal(receipt.Command, original)
                    || receipt.Command.Binding.IncarnationId != incarnation || receipt.Command.Binding.SlotGeneration != slot)
                    return new(CharacterCareerSpecializationOutcome.Conflict);
                var proof = CharacterCareerSpecializationTransaction.Original(receipt);
                if (original.Outcome != CharacterCareerSpecializationOutcome.Pending
                    && !CharacterCareerSpecializationTransaction.Same(original, proof)) return new(CharacterCareerSpecializationOutcome.Corrupt);
                return ConfirmDurableInterruptedSpecializationUnderLease(path,
                    live with { Original = proof, OriginalLocation = location });
            }
            if (live.Outcome != CharacterCareerSpecializationOutcome.NotFound) return live;
        }
        if (original is null) return new(CharacterCareerSpecializationOutcome.NotFound,
            CurrentRevision: saved?.ContentRevision, OriginalLocation: location);
        if (original.Outcome is not (CharacterCareerSpecializationOutcome.Pending or CharacterCareerSpecializationOutcome.NeverApplied)
            && location == CharacterCareerSpecializationOriginalLocation.LiveOriginal)
            return new(CharacterCareerSpecializationOutcome.Corrupt, Error: "specialization_original_history_missing");
        if (original.Outcome == CharacterCareerSpecializationOutcome.NeverApplied)
            return ConfirmDurableSpecializationExclusionUnderLease(owner, id, path, archive, original, saved?.ContentRevision, location);
        // Archive metadata proves an original outcome, never that unlink happened.
        // Presence is observed independently under this same path lease.
        return new(original.Outcome == CharacterCareerSpecializationOutcome.Applied ? CharacterCareerSpecializationOutcome.Recorded : original.Outcome,
            CurrentRevision: saved?.ContentRevision, Original: original, OriginalLocation: location);
    }

    private const int MaximumSpecializationOriginalArchiveBytes = 4 * 1024 * 1024;
    private const string SpecializationOriginalArchiveSuffix = ".specialization-outcomes.v1.json";
    private static readonly JsonSerializerOptions SpecializationArchiveJson = new()
    { MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private sealed record SpecializationOriginalArchive(int SchemaVersion, IReadOnlyList<CharacterCareerSpecializationOriginalOutcome> Entries, string Digest)
    {
        // Optional so old v1 archive serialization/digests remain unchanged. Missing
        // provenance protects old Pending operations; it is never synthesized on read.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<SpecializationRecoveryPredecessor>? Recovery { get; init; }
    }
    // Called only after exact original identity and live receipt checks, under the SAME path lease.
    // A readable post-replace exclusion is not terminal authority until durability succeeds.
    private CharacterCareerSpecializationResult ConfirmDurableSpecializationExclusionUnderLease(OwnerScope owner,
        CharacterWorkspaceId id, string workspacePath, SpecializationOriginalArchive expectedArchive,
        CharacterCareerSpecializationOriginalOutcome expectedOriginal, long? currentRevision,
        CharacterCareerSpecializationOriginalLocation location)
    {
        try
        {
            if (expectedOriginal.Outcome != CharacterCareerSpecializationOutcome.NeverApplied
                || !CharacterCareerSpecializationTransaction.IsCoherentOriginal(expectedOriginal)
                || expectedOriginal.OwnerId != owner.NormalizedValue
                || expectedOriginal.TrustedLocalOwner != owner.IsLocalSingleUser || expectedOriginal.WorkspaceId != id
                || !CharacterCareerSpecializationTransaction.Same(expectedOriginal,
                    expectedArchive.Entries.SingleOrDefault(p => p.OperationId == expectedOriginal.OperationId))
                || !CharacterCareerSpecializationTransaction.Same(expectedArchive, ReadSpecializationOriginalArchive(owner, id, workspacePath)))
                return new(CharacterCareerSpecializationOutcome.Corrupt);
            string path = workspacePath + SpecializationOriginalArchiveSuffix;
            EnsurePathContained(_stateDirectory, path, "Specialization original outcome metadata");
            ThrowIfLinkOrReparsePoint(path, "Specialization original outcome metadata");
            // Open existing only. Writable access is needed for Flush(true); no bytes are written.
            // Do not use a creation mode, chmod, migrate, clean temporary files or repair metadata.
            using (var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open, Access = FileAccess.ReadWrite, Share = FileShare.Read,
                Options = FileOptions.WriteThrough, BufferSize = FileBufferSize
            }))
            {
                if (stream.Length is <= 0 or > MaximumSpecializationOriginalArchiveBytes)
                    return new(CharacterCareerSpecializationOutcome.Corrupt);
                stream.Flush(flushToDisk: true);
            }
            _ = FlushRetainedDirectoryChain(Path.GetDirectoryName(path)!);
            var durable = ReadSpecializationOriginalArchive(owner, id, workspacePath);
            if (!CharacterCareerSpecializationTransaction.Same(expectedArchive, durable))
                return new(CharacterCareerSpecializationOutcome.Corrupt);
            return new(CharacterCareerSpecializationOutcome.NeverApplied, CurrentRevision: currentRevision,
                Original: expectedOriginal, OriginalLocation: location);
        }
        catch (Exception e) when (SpecializationStorageFailure(e))
        { return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_durability_unavailable"); }
    }

    private static SpecializationOriginalArchive SealSpecializationArchive(IReadOnlyList<CharacterCareerSpecializationOriginalOutcome> entries,
        IReadOnlyList<SpecializationRecoveryPredecessor>? recovery = null)
    {
        var archive = new SpecializationOriginalArchive(1, entries, "") { Recovery = recovery };
        return archive with { Digest = CharacterCareerSpecializationTransaction.Hash(JsonSerializer.Serialize(archive, SpecializationArchiveJson)) };
    }
    private static bool FitsSpecializationTerminalArchive(SpecializationOriginalArchive archive)
    {
        if (archive.Entries.Count > CharacterCareerSpecializationTransaction.MaximumReceipts) return false;
        // Reserve the largest terminal scalar shape for EVERY reservation at admission.
        // Deletion cannot discover a new byte-capacity requirement after the specialization commit.
        var maximum = SealSpecializationArchive(archive.Entries.Select(p => p with
        { Outcome = CharacterCareerSpecializationOutcome.Applied, RecordedRevision = long.MaxValue,
            CommittedRevision = long.MaxValue, ReceiptDigest = new string('f', 64) }).ToArray(),
            // Reserve the canonical 71-character pending-record witness before first publication.
            archive.Recovery?.Select(p => p with { PendingRecordDigest = CharacterCreationFinalizationDigest.ReceiptLedgerRootDigest }).ToArray());
        return JsonSerializer.SerializeToUtf8Bytes(maximum, SpecializationArchiveJson).Length <= MaximumSpecializationOriginalArchiveBytes;
    }
    private SpecializationOriginalArchive ReadSpecializationOriginalArchive(OwnerScope owner, CharacterWorkspaceId id, string workspacePath)
    {
        var archive = ReadSpecializationOriginalArchiveForPath(workspacePath);
        if (archive.Entries.Any(p => p.OwnerId != owner.NormalizedValue || p.TrustedLocalOwner != owner.IsLocalSingleUser || p.WorkspaceId != id))
            throw new IOException("Specialization metadata owner or workspace differs.");
        return archive;
    }
    private SpecializationOriginalArchive ReadSpecializationOriginalArchiveForPath(string workspacePath)
    {
        string path = workspacePath + SpecializationOriginalArchiveSuffix;
        EnsurePathContained(_stateDirectory, path, "Specialization original outcome metadata");
        ThrowIfLinkOrReparsePoint(path, "Specialization original outcome metadata");
        FileStream stream;
        try { stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (FileNotFoundException) { return SealSpecializationArchive([]); }
        using (stream)
        {
            if (stream.Length is <= 0 or > MaximumSpecializationOriginalArchiveBytes) throw new IOException("Specialization original metadata size is invalid.");
            try
            {
                using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
                RequireUniqueSpecializationMetadata(json.RootElement);
                var archive = JsonSerializer.Deserialize<SpecializationOriginalArchive>(json.RootElement, SpecializationArchiveJson);
                if (archive is not { SchemaVersion: 1, Entries: not null }
                    || archive.Entries.Any(p => !CharacterCareerSpecializationTransaction.IsCoherentOriginal(p)
                        || !PathComparer.Equals(TryGetPath(p.TrustedLocalOwner ? OwnerScope.LocalSingleUser : new OwnerScope(p.OwnerId), p.WorkspaceId), workspacePath))
                    || !ValidSpecializationRecoveryArchive(archive)
                    || !FitsSpecializationTerminalArchive(archive) || archive.Digest != SealSpecializationArchive(archive.Entries, archive.Recovery).Digest
                    || archive.Entries.Select(p => p.OperationId).Distinct().Count() != archive.Entries.Count)
                    throw new IOException("Specialization original metadata is invalid.");
                return archive;
            }
            catch (JsonException error)
            {
                // Central generic-save callers handle storage failures as IO errors.
                // Preserve corrupt bytes and fail closed; never reinterpret them as empty.
                throw new IOException("Specialization original metadata could not be read.", error);
            }
        }
    }
    private void WriteSpecializationOriginalArchive(OwnerScope owner, CharacterWorkspaceId id, string workspacePath,
        SpecializationOriginalArchive archive, CharacterCareerSpecializationReceipt? observedRejection = null)
    {
        archive = BindNewSpecializationRecoveryPredecessors(owner, id, workspacePath, archive, observedRejection);
        if (!FitsSpecializationTerminalArchive(archive)) throw new IOException("Specialization original metadata capacity exhausted before admission.");
        string path = workspacePath + SpecializationOriginalArchiveSuffix;
        string temporary = path + TempFileMarker + Guid.NewGuid().ToString("N");
        EnsurePathContained(_stateDirectory, path, "Specialization original outcome metadata");
        ThrowIfLinkOrReparsePoint(path, "Specialization original outcome metadata");
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(archive, SpecializationArchiveJson);
        RemoveStaleTempFiles(path); // Only under the write lease; lookup never repairs or cleans metadata.
        try
        {
            using (var stream = OpenNewSecureFile(temporary, FileAccess.Write, FileShare.None, FileOptions.WriteThrough))
            { stream.Write(payload); stream.Flush(flushToDisk: true); }
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterTempFileFlushed, path, temporary);
            ThrowIfLinkOrReparsePoint(path, "Specialization original outcome metadata");
            File.Move(temporary, path, overwrite: true);
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterTargetReplaced, path, temporary);
            // Reuse the store's platform durability contract. Linux flushes the
            // directory chain; other platforms promise process-restart durability
            // after the flushed atomic replacement, not universal power-loss proof.
            _ = FlushRetainedDirectoryChain(Path.GetDirectoryName(path)!);
            if (!CharacterCareerSpecializationTransaction.Same(ReadSpecializationOriginalArchive(owner, id, workspacePath), archive))
                throw new IOException("Specialization metadata readback does not match the original outcomes.");
        }
        finally { DeleteRegularFileIfPresent(temporary, "Specialization metadata temporary file"); }
    }
    private void PreserveSpecializationOriginalsBeforeDelete(OwnerScope owner, CharacterWorkspaceId id, string path, WorkspaceStoredDocument saved)
    {
        if (HasPendingSpecialization(saved)) throw new IOException("Resolve the original pending Specialization operation before deletion.");
        var archive = ReadSpecializationOriginalArchive(owner, id, path);
        var local = saved.LocalHistory ?? throw new IOException("Workspace incarnation is unavailable.");
        var receipts = (saved.Document.AuxiliaryState.CharacterCareerSpecializationReceipts ?? [])
            .Where(r => (r.Outcome != CharacterCareerSpecializationOutcome.Applied || saved.CanReplayReceipt(r.RecordedRevision))
                && r.Command.Binding.IncarnationId == local.IncarnationId).ToArray();
        var originals = archive.Entries.ToArray();
        foreach (var receipt in receipts)
        {
            int index = Array.FindIndex(originals, p => p.OperationId == receipt.Command.Request.OperationId);
            if (index < 0 || !CharacterCareerSpecializationTransaction.IsCoherent(receipt)
                || !CharacterCareerSpecializationTransaction.MatchesOriginal(receipt.Command, originals[index]))
                throw new IOException("Original specialization archive reservation is unavailable.");
            var proof = CharacterCareerSpecializationTransaction.Original(receipt);
            if (originals[index].Outcome != CharacterCareerSpecializationOutcome.Pending
                && !CharacterCareerSpecializationTransaction.Same(originals[index], proof))
                throw new IOException("Original specialization outcome metadata conflicts.");
            originals[index] = proof;
        }
        if (originals.Any(p => p.IncarnationId == local.IncarnationId && p.Outcome == CharacterCareerSpecializationOutcome.Pending))
            throw new IOException("An unresolved original specialization reservation still owns this incarnation.");
        var terminal = SealSpecializationArchive(originals, archive.Recovery);
        if (!CharacterCareerSpecializationTransaction.Same(archive, terminal))
            WriteSpecializationOriginalArchive(owner, id, path, terminal);
        // Caller retires the record only after this durable readback. Publication
        // alone proves neither deletion nor replacement; lookup observes the record.
    }

    private static bool IsSpecializationIncarnationReplacement(PersistedWorkspaceRecord? previous, PersistedWorkspaceRecord next)
        => previous?.LocalHistory is { } before && next.LocalHistory is { } after
            && before.IncarnationId != after.IncarnationId && after.LastRestore is not null
            && next.ContentRevision is { } revision && after.IsValid(revision);

    private void PreserveSpecializationOriginalsBeforeReplacement(string path, PersistedWorkspaceRecord next,
        WorkspaceWriteDisposition disposition)
    {
        if (disposition != WorkspaceWriteDisposition.ReplaceExisting) return;
        var previous = ReadPrivateRecord(path);
        if (!IsSpecializationIncarnationReplacement(previous, next)) return;
        var receipts = previous.AuxiliaryState?.CharacterCareerSpecializationReceipts;
        var archive = ReadSpecializationOriginalArchiveForPath(path);
        if (archive.Entries.Any(p => IsUnresolvedSpecializationReservation(p, receipts)))
            throw new IOException("An unresolved original specialization reservation retains write custody.");
        var local = receipts?.FirstOrDefault(r => r.Command.Binding.IncarnationId == previous.LocalHistory!.IncarnationId);
        if (local is null) return;
        var owner = local.Command.Binding.TrustedLocalOwner ? OwnerScope.LocalSingleUser : new OwnerScope(local.Command.Binding.OwnerId);
        var id = local.Command.Request.WorkspaceId;
        if (!PathComparer.Equals(TryGetPath(owner, id), path)
            || !TryMaterializeRecord(previous, out var document, out var revision, out var savedRevision, out _))
            throw new IOException("Original specialization workspace identity is unavailable.");
        PreserveSpecializationOriginalsBeforeDelete(owner, id, path,
            new(id, document, revision, savedRevision, DateTimeOffset.UnixEpoch) { LocalHistory = previous.LocalHistory });
    }
    private static bool IsUnresolvedSpecializationReservation(CharacterCareerSpecializationOriginalOutcome p,
        IReadOnlyList<CharacterCareerSpecializationReceipt>? receipts)
        => p.Outcome == CharacterCareerSpecializationOutcome.Pending && receipts?.Any(r =>
            r.Outcome is CharacterCareerSpecializationOutcome.Applied or CharacterCareerSpecializationOutcome.Rejected
            && CharacterCareerSpecializationTransaction.IsCoherent(r)
            && CharacterCareerSpecializationTransaction.MatchesOriginal(r.Command, p)) != true;

    private static bool SpecializationStorageFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException
            or ArgumentException or System.Xml.XmlException or DllNotFoundException or EntryPointNotFoundException;
    private static void RequireUniqueSpecializationMetadata(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw new JsonException("Duplicate Specialization metadata key."); RequireUniqueSpecializationMetadata(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) RequireUniqueSpecializationMetadata(child);
    }

    private static bool HasPendingSpecialization(WorkspaceStoredDocument current)
        => current.Document.AuxiliaryState.CharacterCareerSpecializationReceipts?
            .Any(r => r.Outcome == CharacterCareerSpecializationOutcome.Pending) == true;
}
