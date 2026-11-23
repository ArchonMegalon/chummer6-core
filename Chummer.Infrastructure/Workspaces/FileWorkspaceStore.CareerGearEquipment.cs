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

public sealed partial class FileWorkspaceStore : ICharacterCareerGearEquipmentAtomicCommitCapability
{
    // Root implements this only with the shared writer/deletion hooks in the proposal.
    static partial void VerifyCareerGearEquipmentGuards(ref bool installed);
    public bool CareerGearEquipmentGuardsInstalled
    { get { bool installed = false; VerifyCareerGearEquipmentGuards(ref installed); return installed; } }
    public string CareerGearEquipmentRuntimeDigest => CharacterCareerGearEquipmentTransaction.Hash(
        "chummer.gear-equipped.runtime/v1\0" + typeof(CharacterGearEquipmentRules).Module.ModuleVersionId + "\0"
        + typeof(CharacterGearEquipmentCapture).Module.ModuleVersionId + "\0" + typeof(FileWorkspaceStore).Module.ModuleVersionId);

    public CharacterCareerGearEquipmentResult CommitCareerGearEquipment(OwnerScope owner,
        CharacterCareerGearEquipmentCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default)
        => CommitCareerGearEquipment(owner, command, sources, null, null, default, cancellationToken);

    internal CharacterCareerGearEquipmentResult CommitCareerGearEquipment(OwnerScope owner,
        CharacterCareerGearEquipmentCommand command, ICharacterSourceDataResolver sources,
        IOwnerContextAccessor? ownerContext, IOwnerContextLease? ownerLease, OwnerContextStamp ownerStamp, CancellationToken cancellationToken = default)
    {
        if (CharacterCareerGearEquipmentTransaction.FreezeConfirmedCommand(command) is not { } frozen)
            return new(CharacterCareerGearEquipmentOutcome.Unavailable);
        command = frozen;
        if (!CareerGearEquipmentGuardsInstalled || !CharacterCareerGearEquipmentTransaction.IsConfirmedCommand(command)
            || !CharacterCareerGearEquipmentTransaction.MatchesOwner(owner, command.Binding))
            return new(CharacterCareerGearEquipmentOutcome.Unavailable);
        string? path = TryGetPath(owner, command.Request.WorkspaceId);
        if (path is null) return new(CharacterCareerGearEquipmentOutcome.Unavailable);
        string digest = CharacterCareerGearEquipmentTransaction.CommandDigest(command);
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var prior = LookupGearOriginalUnderLease(owner, command.Request.WorkspaceId, path, command.Request.OperationId,
                digest, command.Binding.IncarnationId, command.Binding.SlotGeneration);
            if (prior.Outcome != CharacterCareerGearEquipmentOutcome.NotFound) return prior;
            var read = ReadGearWorkspaceUnderLease(owner, command.Request.WorkspaceId, path,
                ownerContext, ownerLease, ownerStamp, out var delegatedLedger, out var currentEnvironment);
            using var current = currentEnvironment;
            if (!read.Success || read.Value is not { } saved) return WorkspaceCharacterCareerGearEquipmentService.MapReadFailure(read);
            string? slot = ReadContinuationSlotUnderLease(path);
            if (saved.LocalHistory?.IncarnationId != command.Binding.IncarnationId || slot != command.Binding.SlotGeneration)
                return new(CharacterCareerGearEquipmentOutcome.Conflict, Error: "gear_incarnation_changed");
            var history = saved.Document.AuxiliaryState.CharacterCareerGearEquipmentReceipts ?? [];
            if (history.Count >= CharacterCareerGearEquipmentTransaction.MaximumReceipts
                || history.Any(r => r.Outcome == CharacterCareerGearEquipmentOutcome.Pending))
                return new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "gear_history_retained");

            WorkspaceDocument? replacement = null; CharacterCareerGearEquipmentReceipt? receipt = null;
            bool valid = !cancellationToken.IsCancellationRequested
                && TryReadGearCatalog(owner, saved, sources, current, out var catalog, slot, cancellationToken)
                && CharacterCareerGearEquipmentTransaction.TryBuild(saved, catalog!, command, out replacement, out receipt);
            var pending = CharacterCareerGearEquipmentTransaction.Seal(command, CharacterCareerGearEquipmentOutcome.Pending,
                command.Binding.ContentRevision);
            var archive = ReadGearOriginalArchive(owner, saved.Id, path);
            if (archive.Entries.Any(p => IsUnresolvedGearReservation(p, history)))
                return new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "gear_original_reservation_unresolved");
            var reservedArchive = SealGearArchive(archive.Entries.Append(CharacterCareerGearEquipmentTransaction.Original(pending)).ToArray());
            if (!FitsGearTerminalArchive(reservedArchive))
                return new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "gear_original_archive_capacity");
            var rejected = CharacterCareerGearEquipmentTransaction.Seal(command, CharacterCareerGearEquipmentOutcome.Rejected,
                saved.ContentRevision, rejection: "gear_review_not_current");
            // Loadout payloads retain configuration snapshots. Reject capacity before
            // reserving original custody, including stale-command rejection on legacy stores.
            if (command.Loadout is not null && !CharacterCareerGearEquipmentTransaction.IsValidLedger(
                saved.Id, saved.ContentRevision, history.Append(rejected).ToArray()))
                return new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "loadout_history_capacity");
            WorkspaceDocument WithReceipt(CharacterCareerGearEquipmentReceipt entry) => saved.Document with
                { State = saved.Document.State with { AuxiliaryState = saved.Document.AuxiliaryState with
                    { CharacterCareerGearEquipmentReceipts = history.Append(entry).ToArray() } } };
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
            WriteGearOriginalArchive(owner, saved.Id, path, reservedArchive);
            if (!valid)
            {
                // A genuinely observed pre-write rejection is durable; no later inference.
                Write(rejectedDocument, saved.ContentRevision, saved.SavedRevision, rejected);
                return new(CharacterCareerGearEquipmentOutcome.Rejected, rejected, saved.ContentRevision);
            }
            Write(reserved, saved.ContentRevision, saved.SavedRevision, pending);
            // No other writer can pass the operation lease or the durable pending fence.
            Write(replacement!, receipt!.RecordedRevision, receipt.RecordedRevision, receipt, () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (current is null && (!CharacterCareerGearEquipmentTransaction.TryEnvironment(sources, saved.Document.Content, out string environment)
                    || environment != command.Binding.EnvironmentDigest))
                    throw new IOException("gear_environment_changed_before_replace");
            });
            return new(CharacterCareerGearEquipmentOutcome.Applied, receipt, receipt.RecordedRevision);

            void RequireProspectiveAdmission(WorkspaceStoredDocument proposed, CharacterCurrentRuleEnvironmentReadmissionResult observed)
            {
                if (observed.AfterCapture is not { } capture || observed.AfterExecution is not { } execution || observed.Blockers.Count != 0
                    || !IsValidAuxiliaryState(saved.Id, proposed.ContentRevision, proposed.Document.AuxiliaryState, current!.Recovery))
                    throw new IOException("gear_proposed_current_admission_unavailable");
                var references = current.Record.RetainedRuleSources!.References;
                if (!references.Any(value => value.CaptureDigest == capture.IntegrityDigest)
                    && references.Length >= MaximumRetainedRuleSourceReferences)
                    throw new IOException("gear_retained_capture_capacity");
                var bytes = capture.ExportPrivateInputs();
                if (bytes.Length == 0 || bytes.Length > RetainedCharacterRuleSourceInputs.MaximumArchiveBytes
                    || RetainedCharacterRuleSourceInputs.Digest(bytes) != capture.IntegrityDigest)
                    throw new IOException("gear_retained_capture_capacity");
                // Size the actual candidate shape before a durable pending reservation.
                // This is only a prospective size check; the writer independently publishes
                // and reopens the real archive and obtains a fresh canonical pair below.
                var reference = new CharacterCreationRetainedRuleSourceReference(1, references[0].OwnerBinding,
                    saved.Id.Value, saved.LocalHistory!.IncarnationId, capture.IntegrityDigest, bytes.LongLength);
                var prospectiveReferences = references.Any(value => value.CaptureDigest == capture.IntegrityDigest)
                    ? references : references.Append(reference).OrderBy(value => value.CaptureDigest, StringComparer.Ordinal).ToArray();
                var record = BuildCurrentRecord(proposed, current.Record.RetainedRuleSources!, prospectiveReferences, capture, execution);
                if (JsonSerializer.SerializeToUtf8Bytes(record).Length > MaximumRetainedWorkspaceRecordBytes)
                    throw new IOException("gear_retained_record_capacity");
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
                CharacterCareerGearEquipmentReceipt admitted, Action? fence = null)
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
                    throw new IOException("gear_before_write_changed");
                if (!IsValidAuxiliaryState(saved.Id, revision, document.AuxiliaryState, actualCurrent?.Recovery))
                    throw new IOException("gear_auxiliary_invalid");
                var proposedAfter = actualBefore with { Document = document, ContentRevision = revision, SavedRevision = savedRevision };
                CharacterCurrentRuleEnvironmentReadmissionResult? readmission = null;
                if (actualCurrent is not null)
                {
                    readmission = actualCurrent.Execution.ReadmitProposedCurrentEnvironment(actualBefore,
                        actualCurrent.Capture, _retainedCharacterQueries!, proposedAfter, cancellationToken);
                    if (readmission.AfterCapture is not { } afterCapture || readmission.AfterExecution is not { } afterExecution
                        || readmission.Blockers.Count != 0) throw new IOException("gear_after_current_admission_unavailable");
                    actualCurrent.Recovery.RetainAdmittedCapture(new(afterCapture, _retainedCharacterQueries!), afterCapture.IntegrityDigest);
                    record = BuildCurrentRecord(proposedAfter, actualCurrent.Record.RetainedRuleSources!,
                        actualCurrent.Recovery.SnapshotReferences(), afterCapture, afterExecution);
                }
                else
                {
                    if (current is not null) throw new IOException("gear_current_environment_lost");
                    record = PreserveRetainedRuleSources(path, record, WorkspaceWriteDisposition.ReplaceExisting);
                }
                // Private store/path/operation/full-record binding; no request can create it.
                using var admission = new GearEquipmentWriteAdmission(this, path, admitted, record, actualCurrent,
                    proposedAfter, readmission, command.Binding.SlotGeneration);
                WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: fence);
            }
        }
        catch (Exception e) when (GearStorageFailure(e) || e is OperationCanceledException)
        { return new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "gear_original_result_unknown"); }
    }

    // Private per-instance, thread-local admission; concurrent workspaces cannot borrow it.
    [ThreadStatic] private static GearEquipmentWriteAdmission? _gearEquipmentAdmission;
    private sealed class GearEquipmentWriteAdmission : IDisposable
    {
        private readonly GearEquipmentWriteAdmission? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal CharacterCareerGearEquipmentReceipt Receipt { get; }
        internal string RecordDigest { get; }
        internal GearCurrentEnvironment? Current { get; }
        private readonly WorkspaceStoredDocument _after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? _readmission;
        private readonly string _slot;
        private readonly string _beforeDigest;
        internal GearEquipmentWriteAdmission(FileWorkspaceStore store, string path, CharacterCareerGearEquipmentReceipt receipt,
            PersistedWorkspaceRecord record, GearCurrentEnvironment? current, WorkspaceStoredDocument after,
            CharacterCurrentRuleEnvironmentReadmissionResult? readmission, string slot)
        {
            _previous = _gearEquipmentAdmission; Store = store; Path = System.IO.Path.GetFullPath(path); Receipt = receipt;
            RecordDigest = CharacterCreationFinalizationDigest.Compute(record); Current = current;
            _after = after; _readmission = readmission; _slot = slot;
            _beforeDigest = CharacterCreationFinalizationDigest.Compute(store.ReadPrivateRecord(path));
            _gearEquipmentAdmission = this;
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            if (FileWorkspaceStore.ReadContinuationSlotUnderLease(Path) != _slot
                || CharacterCreationFinalizationDigest.Compute(Store.ReadPrivateRecord(Path)) != _beforeDigest
                || CharacterCreationFinalizationDigest.Compute(record) != RecordDigest)
                throw new IOException("gear_exact_record_or_slot_changed");
            if (Current is null) return;
            Current.ValidateFinalFence();
            if (_readmission?.AfterCapture is not { } capture || _readmission.AfterExecution is not { } execution
                || !execution.Revalidate(_after, capture, Store._retainedCharacterQueries!))
                throw new IOException("gear_after_current_environment_changed");
        }
        public void Dispose() => _gearEquipmentAdmission = _previous;
    }

    private GearEquipmentWriteAdmission? GearAdmissionFor(string path)
        => _gearEquipmentAdmission is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, Path.GetFullPath(path)) ? admission : null;

    private void RevalidateGearEquipmentForWrite(string path, PersistedWorkspaceRecord record)
        => GearAdmissionFor(path)?.ValidateFinalFence(record);

    // Protected Career and Life Module routes hold the exact owner/workspace leases.
    // No resolver or canonical execution escapes this private synchronous scope.
    private sealed class GearCurrentEnvironment : IDisposable
    {
        private readonly FileWorkspaceStore _store;
        private readonly string _path;
        private readonly IOwnerContextLease _ownerLease;
        private readonly IOwnerContextLeaseAccessor _ownerContext;
        private readonly OwnerContextStamp _ownerStamp;
        private readonly string _recordDigest;
        private readonly string? _slot;
        internal PersistedWorkspaceRecord Record { get; }
        internal WorkspaceStoredDocument Stored { get; }
        internal RetainedRuleSourceWorkspace Recovery { get; }
        internal CharacterCreationRuleSourceCapture Capture { get; }
        internal CharacterCurrentRuleEnvironmentExecution Execution { get; }
        internal GearCurrentEnvironment(FileWorkspaceStore store, string path, IOwnerContextLeaseAccessor ownerContext, IOwnerContextLease ownerLease,
            OwnerContextStamp ownerStamp, PersistedWorkspaceRecord record, WorkspaceStoredDocument stored,
            RetainedRuleSourceWorkspace recovery, CharacterCreationRuleSourceCapture capture,
            CharacterCurrentRuleEnvironmentExecution execution)
        {
            _store = store; _path = path; _ownerContext = ownerContext; _ownerLease = ownerLease; _ownerStamp = ownerStamp;
            Record = record; Stored = stored; Recovery = recovery; Capture = capture; Execution = execution;
            _recordDigest = CharacterCreationFinalizationDigest.Compute(record); _slot = FileWorkspaceStore.ReadContinuationSlotUnderLease(path);
        }
        private void ValidateStorageFence()
        {
            if (!_ownerStamp.IsValid || _ownerLease.Stamp != _ownerStamp || _ownerContext.Capture() != _ownerStamp
                || !PathComparer.Equals(_store.TryGetPath(_ownerStamp.Owner, Stored.Id), _path)
                || FileWorkspaceStore.ReadContinuationSlotUnderLease(_path) != _slot
                || CharacterCreationFinalizationDigest.Compute(_store.ReadPrivateRecord(_path)) != _recordDigest
                || !Recovery.ValidateRetainedRuleSources())
                throw new IOException("gear_current_environment_changed");
        }
        internal void ValidateReadObservation(CancellationToken cancellationToken = default)
        {
            // For an admitted read/review or a post-publication result observation.
            // Pre-publication write fences always use ValidateFinalFence instead.
            cancellationToken.ThrowIfCancellationRequested();
            ValidateStorageFence();
            bool unchanged = Capture.SourcebookSelection is null
                ? Execution.RevalidateDisplayObservation(Stored, Capture, _store._retainedCharacterQueries!, cancellationToken)
                : Execution.Revalidate(Stored, Capture, _store._retainedCharacterQueries!, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!unchanged) throw new IOException("gear_current_environment_changed");
        }
        internal void ValidateFreeSpriteReadObservation()
            => ValidateCompleteReadObservation();

        internal void ValidateCompleteReadObservation(CancellationToken cancellationToken = default)
        {
            // The Free Sprite history has been checked and CurrentReadEvaluation
            // has been disposed. Keep the full sourcebook/display distinction.
            ValidateReadObservation(cancellationToken);
            // Queries can change live state after the observation's opening fence.
            // Reopen the record and retained sources; do not rebase this snapshot.
            ValidateStorageFence();
            cancellationToken.ThrowIfCancellationRequested();
            if (new DateTimeOffset(File.GetLastWriteTimeUtc(_path), TimeSpan.Zero) != Stored.LastUpdatedUtc)
                throw new IOException("gear_current_environment_changed");
        }
        internal void ValidateFinalFence()
        {
            ValidateStorageFence();
            // Publication and later operation fences still execute fresh admission.
            if (!Execution.Revalidate(Stored, Capture, _store._retainedCharacterQueries!))
                throw new IOException("gear_current_environment_changed");
        }
        public void Dispose() => Recovery.Dispose();
    }

    private WorkspaceStoreReadResult ReadGearWorkspaceUnderLease(OwnerScope owner, CharacterWorkspaceId id, string path,
        IOwnerContextAccessor? ownerContext, IOwnerContextLease? ownerLease, OwnerContextStamp ownerStamp,
        out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> ledger, out GearCurrentEnvironment? current)
        => ReadGearWorkspaceCoreUnderLease(owner, id, path, ownerContext, ownerLease, ownerStamp,
            deferFreeSpriteReadObservation: false, out ledger, out current);

    // Only ReadFreeSpriteConversionUnderLease may defer the observation. It owns
    // the pending recovery and must validate history, observe and fence before
    // returning anything. The original eight-argument reflective entry stays unique.
    private WorkspaceStoreReadResult ReadGearWorkspaceCoreUnderLease(OwnerScope owner, CharacterWorkspaceId id, string path,
        IOwnerContextAccessor? ownerContext, IOwnerContextLease? ownerLease, OwnerContextStamp ownerStamp,
        bool deferFreeSpriteReadObservation,
        out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> ledger, out GearCurrentEnvironment? current,
        bool completeReadObservation = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        current = null; ledger = [];
        if (!File.Exists(path)) return ReadWorkspaceUnderLease(owner, id, path, out ledger);
        var record = ReadPrivateRecord(path);
        if (record.RetainedRuleSources?.CurrentEnvironment is not { } environment)
            return ReadWorkspaceUnderLease(owner, id, path, out ledger);
        if (ownerContext is not IOwnerContextLeaseAccessor authority || ownerLease is null || !ownerStamp.IsValid
            || authority.Capture() != ownerStamp || ownerLease.Stamp != ownerStamp
            || ownerStamp.Owner != owner || _retainedCharacterQueries is null
            || record.RetainedRuleSources.PrivateClosureBinding is not null) return UnavailableRead();
        if (!TryMaterializeRecord(record, out var capturedDocument, out var capturedRevision,
            out var capturedSavedRevision, out bool migration) || migration) return UnavailableRead();
        DateTimeOffset readTimestamp = new(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        // Reward read/preview can provide the complete private history up front.
        // Only the sealed local query implementation may reuse that observation.
        // The ordinary store read still validates the complete record and ledger;
        // its exact result must match before any evaluated result can be returned.
        WorkspaceStoredDocument? completeRead = null;
        if (completeReadObservation
            && _retainedCharacterQueries is Chummer.Infrastructure.Xml.XmlCharacterFileQueries
                { HasImmutableEvaluationQueries2764: true }
            && record.LocalHistory is { } localHistory && localHistory.IsValid(capturedRevision))
        {
            // Segment inspection dereferences ledger receipts. Validate the raw
            // persisted ledger first, using the ordinary store's same owner,
            // workspace, revision and segment checks before binding this read.
            if (!TryMaterializeDelegatedEditLedger(owner, id, capturedRevision,
                    record.DelegatedGmCharacterEdits,
                    record.RecordSchemaVersion >= 4 ? record.DelegatedGmHistorySegmentStarts : [],
                    out var completeReadLedger)
                || !HasValidLocalDelegatedEditSegments(completeReadLedger,
                    record.DelegatedGmHistorySegmentStarts ?? [], localHistory))
                return CorruptRead();
            completeRead = new(id, capturedDocument, capturedRevision, capturedSavedRevision, readTimestamp)
            {
                LocalHistory = localHistory,
                DelegatedGmHistorySegmentStarts = record.DelegatedGmHistorySegmentStarts ?? [],
                HasDelegatedGmHistory = completeReadLedger.Count > 0
            };
        }
        // This token is disposed before control can return to a write caller.
        using var currentRead = new PrivateCurrentRuleEnvironmentAdmission.CurrentReadEvaluation(owner.NormalizedValue,
            new(id, capturedDocument, readTimestamp, capturedRevision, capturedSavedRevision),
            environment, _retainedCharacterQueries, CharacterCreationFinalizationDigest.Compute(record), completeRead, cancellationToken);
        // Share only this synchronous read's exact admission observation.
        // The recovery escapes inside GearCurrentEnvironment, so its decoded
        // capture cache must remain independent of the shorter read scope.
        // Full stored-document admission below and all later read/write fences
        // remain fresh; every recovery still reopens and validates archive bytes.
        var recovery = OpenRecordRuleSources(owner, id, record, reuseCurrentCaptureForRead: true,
            currentRead: currentRead, borrowDecodedCaptureForRead: false);
        try
        {
            var read = ReadWorkspaceUnderLease(owner, id, path, out ledger, privateClosureRead: true,
                reuseCurrentCaptureForRead: true, currentRead: currentRead);
            if (!read.Success || read.Value is not { } stored || recovery is null
                || stored.LastUpdatedUtc != readTimestamp || stored.ContentRevision != capturedRevision
                || stored.SavedRevision != capturedSavedRevision
                || CharacterCreationFinalizationDigest.Compute(stored.Document)
                    != CharacterCreationFinalizationDigest.Compute(capturedDocument)
                || !recovery.TryRecoverRuleSourceCapture(environment.CaptureDigest, out var capture) || capture is null)
                return UnavailableRead();
            CharacterCurrentRuleEnvironmentExecution? execution;
            IReadOnlyList<string> blockers;
            if (completeRead is not null)
                execution = currentRead.Evaluate(owner.NormalizedValue, stored, capture, environment,
                    _retainedCharacterQueries, cancellationToken, out blockers);
            else
            {
                var admitted = CharacterCurrentRuleEnvironmentAdmission.AdmitCurrentEnvironment(stored, capture, _retainedCharacterQueries, cancellationToken);
                execution = admitted.Execution; blockers = admitted.Blockers;
            }
            if (execution is null || blockers.Count != 0) return UnavailableRead();
            current = new(this, path, authority, ownerLease, ownerStamp, record, stored, recovery, capture, execution);
            if (!deferFreeSpriteReadObservation) current.ValidateReadObservation(cancellationToken);
            // The Free Sprite caller temporarily owns this admitted recovery until
            // its mandatory final observation succeeds; every refusal disposes it.
            recovery = null;
            return read;
        }
        finally { recovery?.Dispose(); }
    }

    private bool TryReadGearCatalog(OwnerScope owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        GearCurrentEnvironment? current, out CharacterCareerGearEquipmentCatalog? catalog, string? slot,
        CancellationToken cancellationToken = default, bool displayObservation = false)
        => current is null
            ? CharacterCareerGearEquipmentTransaction.TryRead(owner, saved, sources, CareerGearEquipmentRuntimeDigest, out catalog, slot)
            : displayObservation
                ? CharacterCareerGearEquipmentTransaction.TryReadCurrentEnvironmentForDisplay(owner, saved, current.Capture, current.Execution,
                    _retainedCharacterQueries!, CareerGearEquipmentRuntimeDigest, out catalog, slot, cancellationToken)
                : CharacterCareerGearEquipmentTransaction.TryReadCurrentEnvironment(owner, saved, current.Capture, current.Execution,
                    _retainedCharacterQueries!, CareerGearEquipmentRuntimeDigest, out catalog, slot, cancellationToken);

    private void ValidateGearEquipmentWrite(string path, PersistedWorkspaceRecord record, WorkspaceWriteDisposition disposition)
    {
        if (record.AuxiliaryState is { } proposedAuxiliary && !WorkspaceDocumentAuxiliaryState.HasSafeExtensionData(proposedAuxiliary))
            throw new IOException("Auxiliary extension data cannot shadow typed workspace state.");
        var previous = disposition == WorkspaceWriteDisposition.ReplaceExisting ? ReadPrivateRecord(path) : null;
        var before = previous?.AuxiliaryState?.CharacterCareerGearEquipmentReceipts;
        var reservations = ReadGearOriginalArchiveForPath(path).Entries;
        var after = record.AuxiliaryState?.CharacterCareerGearEquipmentReceipts;
        var admitted = _gearEquipmentAdmission is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, Path.GetFullPath(path)) ? admission.Receipt : null;
        if (reservations.Any(p => IsUnresolvedGearReservation(p, before)
            && (admitted is null || !CharacterCareerGearEquipmentTransaction.MatchesOriginal(admitted.Command, p))))
            throw new IOException("An unresolved original Gear reservation retains write custody.");
        if (admitted is null)
        {
            bool retiring = IsGearIncarnationReplacement(previous, record);
            if (previous is not null && !retiring && !CharacterCareerGearEquipmentTransaction.Same(
                previous.AuxiliaryState?.CharacterCareerLoadoutPresets, record.AuxiliaryState?.CharacterCareerLoadoutPresets))
                throw new IOException("Loadout configuration requires its typed original operation.");
            if (before?.Any(r => r.Outcome == CharacterCareerGearEquipmentOutcome.Pending) == true
                || previous is not null && !retiring && !CharacterCareerGearEquipmentTransaction.Same(before, after))
                throw new IOException("Original Gear operation must be resolved; its history is immutable.");
            // A new file may retain imported history, but cannot manufacture locally
            // replayable receipts or import unresolved custody as a new operation.
            if ((previous is null || retiring) && after is { Count: > 0 } && (record.LocalHistory is not { } history
                || after.Any(r => r.Outcome == CharacterCareerGearEquipmentOutcome.Pending
                    || !history.IsImportedRevision(r.RecordedRevision))))
                throw new IOException("New Gear history must remain imported, never original-operation authority.");
            return;
        }
        if (previous is null || after is null || !CharacterCareerGearEquipmentTransaction.IsCoherent(admitted))
            throw new IOException("Gear authority requires an existing workspace.");
        var retained = (before ?? []).Where(r => r.Command.Request.OperationId != admitted.Command.Request.OperationId).ToArray();
        if (!CharacterCareerGearEquipmentTransaction.Same(after, retained.Append(admitted).ToArray()))
            throw new IOException("Gear authority cannot replace other operations.");
        var original = before?.SingleOrDefault(r => r.Command.Request.OperationId == admitted.Command.Request.OperationId);
        if (admitted.Outcome == CharacterCareerGearEquipmentOutcome.Applied)
        {
            if (original?.Outcome != CharacterCareerGearEquipmentOutcome.Pending || original.CommandDigest != admitted.CommandDigest
                || previous.ContentRevision != admitted.Command.Binding.ContentRevision
                || record.ContentRevision != previous.ContentRevision + 1 || record.SavedRevision != record.ContentRevision)
                throw new IOException("Gear completion requires the exact original pending operation.");
        }
        else if (original is not null || record.ContentRevision != previous.ContentRevision || record.SavedRevision != previous.SavedRevision
            || record.Format != previous.Format || !CharacterCareerGearEquipmentTransaction.Same(record.Envelope, previous.Envelope))
            throw new IOException("Gear reservation/rejection cannot change character content.");
        var previousPresets = previous.AuxiliaryState?.CharacterCareerLoadoutPresets;
        var expectedPresets = admitted.Outcome == CharacterCareerGearEquipmentOutcome.Applied && admitted.Command.Loadout is { } loadout
            ? (loadout.AfterPresets.Count == 0 ? null : loadout.AfterPresets) : previousPresets;
        if (!CharacterCareerGearEquipmentTransaction.Same(record.AuxiliaryState?.CharacterCareerLoadoutPresets, expectedPresets)
            || admitted.Outcome == CharacterCareerGearEquipmentOutcome.Applied
                && admitted.Command.Loadout is { } presetPlan && !CharacterCareerGearEquipmentTransaction.Same(
                presetPlan.BeforePresets, previousPresets ?? []))
            throw new IOException("Loadout configuration does not match the original typed plan.");
        if (!CharacterCareerGearEquipmentTransaction.Same(record.AuxiliaryState! with
            { CharacterCareerGearEquipmentReceipts = before, CharacterCareerLoadoutPresets = previousPresets },
            previous.AuxiliaryState ?? WorkspaceDocumentAuxiliaryState.Empty))
            throw new IOException("Gear must preserve every other auxiliary ledger.");
    }

    public CharacterCareerGearEquipmentReadResult ReadCareerGearEquipment(OwnerScope owner, CharacterWorkspaceId workspace,
        ICharacterSourceDataResolver sources)
        => ReadCareerGearEquipment(owner, workspace, sources, null, null, default);

    internal CharacterCareerGearEquipmentReadResult ReadCareerGearEquipment(OwnerScope owner, CharacterWorkspaceId workspace,
        ICharacterSourceDataResolver sources, IOwnerContextAccessor? ownerContext, IOwnerContextLease? ownerLease, OwnerContextStamp ownerStamp,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string? path = TryGetPath(owner, workspace);
            if (!CareerGearEquipmentGuardsInstalled || path is null || (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)))
                return new(CharacterCareerGearEquipmentOutcome.Unavailable);
            using var operation = AcquireWorkspaceOperation(path, cancellationToken: cancellationToken);
            // This catalog is a display read. Reuse only the complete admitted
            // observation under this lease; previews and writes stay fresh.
            var read = ReadGearWorkspaceCoreUnderLease(owner, workspace, path, ownerContext, ownerLease, ownerStamp,
                deferFreeSpriteReadObservation: false, out _, out var environment,
                completeReadObservation: true, cancellationToken: cancellationToken);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved || HasPendingGearEquipment(saved))
                return new(CharacterCareerGearEquipmentOutcome.Unavailable);
            if (!TryReadGearCatalog(owner, saved, sources, current, out var catalog, ReadContinuationSlotUnderLease(path),
                cancellationToken, displayObservation: true))
                return new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "gear_saved_source_or_incarnation_unavailable");
            current?.ValidateCompleteReadObservation(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new(CharacterCareerGearEquipmentOutcome.Available, catalog);
        }
        catch (Exception e) when (GearStorageFailure(e)) { return new(CharacterCareerGearEquipmentOutcome.Unavailable); }
    }

    public CharacterCareerGearEquipmentPreviewResult PreviewCareerGearEquipment(OwnerScope owner,
        CharacterCareerGearEquipmentBinding displayed, CharacterCareerGearEquipmentRequest request, ICharacterSourceDataResolver sources)
        => PreviewCareerGearEquipment(owner, displayed, request, sources, null, null, default);

    internal CharacterCareerGearEquipmentPreviewResult PreviewCareerGearEquipment(OwnerScope owner,
        CharacterCareerGearEquipmentBinding displayed, CharacterCareerGearEquipmentRequest request, ICharacterSourceDataResolver sources,
        IOwnerContextAccessor? ownerContext, IOwnerContextLease? ownerLease, OwnerContextStamp ownerStamp)
    {
        try
        {
            string? path = TryGetPath(owner, request.WorkspaceId);
            if (!CareerGearEquipmentGuardsInstalled || path is null || !CharacterCareerGearEquipmentTransaction.MatchesOwner(owner, displayed))
                return new(CharacterCareerGearEquipmentOutcome.Unavailable);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, request.WorkspaceId, path, ownerContext, ownerLease, ownerStamp, out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved || HasPendingGearEquipment(saved)
                || !TryReadGearCatalog(owner, saved, sources, current, out var catalog, ReadContinuationSlotUnderLease(path))
                || !CharacterCareerGearEquipmentTransaction.Same(catalog!.Binding, displayed)
                || !CharacterCareerGearEquipmentTransaction.TryPlan(catalog, request, out var command)
                || !CharacterCareerGearEquipmentTransaction.TryBuild(saved, catalog, command! with { ExplicitlyConfirmed = true }, out _, out _))
                return new(CharacterCareerGearEquipmentOutcome.Conflict);
            var archive = ReadGearOriginalArchive(owner, saved.Id, path);
            if (archive.Entries.Any(p => p.OperationId == request.OperationId || p.Outcome == CharacterCareerGearEquipmentOutcome.Pending
                && p.IncarnationId == displayed.IncarnationId && saved.Document.AuxiliaryState.CharacterCareerGearEquipmentReceipts?
                    .Any(r => r.Command.Request.OperationId == p.OperationId && r.Outcome != CharacterCareerGearEquipmentOutcome.Pending) != true))
                return new(CharacterCareerGearEquipmentOutcome.Conflict);
            var pending = CharacterCareerGearEquipmentTransaction.Original(CharacterCareerGearEquipmentTransaction.Seal(
                command! with { ExplicitlyConfirmed = true }, CharacterCareerGearEquipmentOutcome.Pending, displayed.ContentRevision));
            current?.ValidateFinalFence();
            return FitsGearTerminalArchive(SealGearArchive(archive.Entries.Append(pending).ToArray()))
                ? new(CharacterCareerGearEquipmentOutcome.Available, command)
                : new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "gear_original_archive_capacity");
        }
        catch (Exception e) when (GearStorageFailure(e)) { return new(CharacterCareerGearEquipmentOutcome.Unavailable); }
    }

    public CharacterCareerGearEquipmentResult CancelUnadmittedCareerGearEquipment(OwnerScope owner,
        CharacterCareerGearEquipmentCommand originalCommand, CancellationToken cancellationToken = default)
    {
        if (!CareerGearEquipmentGuardsInstalled) return new(CharacterCareerGearEquipmentOutcome.Unavailable);
        if (CharacterCareerGearEquipmentTransaction.FreezeConfirmedCommand(originalCommand) is not { } command
            || !CharacterCareerGearEquipmentTransaction.MatchesOwner(owner, command.Binding))
            return new(CharacterCareerGearEquipmentOutcome.Conflict);
        try
        {
            string? path = TryGetPath(owner, command.Request.WorkspaceId);
            if (path is null || !TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return new(CharacterCareerGearEquipmentOutcome.Unavailable);
            string digest = CharacterCareerGearEquipmentTransaction.CommandDigest(command);
            // Same process gate and cross-process file lease as Commit/Lookup. Never re-enter it.
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            CharacterCareerGearEquipmentResult Observe() => LookupGearOriginalUnderLease(owner,
                command.Request.WorkspaceId, path, command.Request.OperationId, digest,
                command.Binding.IncarnationId, command.Binding.SlotGeneration);
            var prior = Observe();
            if (prior.Outcome != CharacterCareerGearEquipmentOutcome.NotFound) return prior;
            if (prior.OriginalLocation != CharacterCareerGearEquipmentOriginalLocation.LiveOriginal)
                return new(CharacterCareerGearEquipmentOutcome.Unavailable, OriginalLocation: prior.OriginalLocation);

            // Read private metadata only: no source recovery, evaluation, migration or workspace creation.
            var record = ReadPrivateRecord(path);
            if (!TryMaterializeRecord(record, out var document, out long revision, out long savedRevision, out _)
                || record.LocalHistory is not { } history || !history.IsValid(revision))
                return new(CharacterCareerGearEquipmentOutcome.Corrupt);
            if (history.IncarnationId != command.Binding.IncarnationId
                || ReadContinuationSlotUnderLease(path) != command.Binding.SlotGeneration
                || command.Binding.ContentRevision > revision || command.Binding.SavedRevision > savedRevision)
                return new(CharacterCareerGearEquipmentOutcome.Conflict);
            var archive = ReadGearOriginalArchive(owner, command.Request.WorkspaceId, path);
            if (archive.Entries.Any(p => IsUnresolvedGearReservation(p, document.AuxiliaryState.CharacterCareerGearEquipmentReceipts)))
                return new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "gear_pending_or_capacity");
            var binding = command.Binding;
            var excluded = new CharacterCareerGearEquipmentOriginalOutcome(binding.OwnerId, binding.TrustedLocalOwner,
                binding.WorkspaceId, binding.IncarnationId, binding.SlotGeneration, command.Request.OperationId,
                digest, binding.ContentRevision, CharacterCareerGearEquipmentOutcome.NeverApplied,
                binding.ContentRevision, null, null);
            var terminal = SealGearArchive(archive.Entries.Append(excluded).ToArray());
            if (!FitsGearTerminalArchive(terminal))
                return new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "gear_original_archive_capacity");
            cancellationToken.ThrowIfCancellationRequested();
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return new(CharacterCareerGearEquipmentOutcome.Unavailable);
            try { WriteGearOriginalArchive(owner, command.Request.WorkspaceId, path, terminal); }
            catch (Exception e) when (GearStorageFailure(e) || e is OperationCanceledException)
            { /* Still under the same lease: only readable exact proof can resolve an uncertain write. */ }
            var recovered = Observe();
            if (recovered.Outcome == CharacterCareerGearEquipmentOutcome.NeverApplied
                && (recovered.Receipt is not null || !CharacterCareerGearEquipmentTransaction.Same(excluded, recovered.Original)))
                return new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "gear_original_result_unknown");
            return recovered.Outcome == CharacterCareerGearEquipmentOutcome.NotFound
                ? new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "gear_original_result_unknown") : recovered;
        }
        catch (Exception e) when (GearStorageFailure(e) || e is OperationCanceledException)
        { return new(CharacterCareerGearEquipmentOutcome.Unavailable); }
    }

    public CharacterCareerGearEquipmentResult LookupCareerGearEquipment(OwnerScope owner, CharacterWorkspaceId workspace,
        Guid operationId, string commandDigest, string incarnationId, string slotGeneration)
    {
        if (!CareerGearEquipmentGuardsInstalled || operationId == Guid.Empty
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(workspace)
            || !CharacterCareerGearEquipmentTransaction.IsDigest(commandDigest)
            || !CharacterCareerGearEquipmentTransaction.IsGeneration(incarnationId)
            || !CharacterCareerGearEquipmentTransaction.IsGeneration(slotGeneration)
            || (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner))) return new(CharacterCareerGearEquipmentOutcome.Conflict);
        try
        {
            string? path = TryGetPath(owner, workspace);
            if (path is null || !TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return new(CharacterCareerGearEquipmentOutcome.Unavailable);
            // No Get/migration, temp cleanup, slot rotation, archive repair or execution in lookup.
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            return LookupGearOriginalUnderLease(owner, workspace, path, operationId, commandDigest, incarnationId, slotGeneration);
        }
        catch (Exception e) when (GearStorageFailure(e)) { return new(CharacterCareerGearEquipmentOutcome.Unavailable); }
    }

    private CharacterCareerGearEquipmentResult LookupGearOriginalUnderLease(OwnerScope owner, CharacterWorkspaceId id,
        string path, Guid operation, string digest, string incarnation, string slot)
    {
        var archive = ReadGearOriginalArchive(owner, id, path);
        var original = archive.Entries.SingleOrDefault(p => p.OperationId == operation);
        if (original is not null && (original.CommandDigest != digest || original.IncarnationId != incarnation || original.SlotGeneration != slot))
            return new(CharacterCareerGearEquipmentOutcome.Conflict);
        WorkspaceStoredDocument? saved = null;
        try
        {
            var record = ReadPrivateRecord(path);
            if (!TryMaterializeRecord(record, out var document, out long revision, out long savedRevision, out _)
                || record.LocalHistory is not { } history || !history.IsValid(revision))
                return new(CharacterCareerGearEquipmentOutcome.Corrupt);
            saved = new(id, document, revision, savedRevision, new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero))
            { LocalHistory = history };
        }
        catch (FileNotFoundException) { }
        var location = saved is null ? CharacterCareerGearEquipmentOriginalLocation.Missing
            : saved.LocalHistory!.IncarnationId == incarnation ? CharacterCareerGearEquipmentOriginalLocation.LiveOriginal
            : CharacterCareerGearEquipmentOriginalLocation.DifferentIncarnation;
        if (saved is not null)
        {
            if (!CharacterCareerGearEquipmentTransaction.IsGeneration(ReadContinuationSlotUnderLease(path)))
                return new(CharacterCareerGearEquipmentOutcome.Corrupt);
            var live = location == CharacterCareerGearEquipmentOriginalLocation.LiveOriginal
                ? CharacterCareerGearEquipmentTransaction.Lookup(owner, saved, operation, digest)
                : new CharacterCareerGearEquipmentResult(CharacterCareerGearEquipmentOutcome.NotFound);
            if (live.Receipt is { } receipt)
            {
                if (original is null || location != CharacterCareerGearEquipmentOriginalLocation.LiveOriginal
                    || !CharacterCareerGearEquipmentTransaction.MatchesOriginal(receipt.Command, original)
                    || receipt.Command.Binding.IncarnationId != incarnation || receipt.Command.Binding.SlotGeneration != slot)
                    return new(CharacterCareerGearEquipmentOutcome.Conflict);
                var proof = CharacterCareerGearEquipmentTransaction.Original(receipt);
                if (original.Outcome != CharacterCareerGearEquipmentOutcome.Pending
                    && !CharacterCareerGearEquipmentTransaction.Same(original, proof)) return new(CharacterCareerGearEquipmentOutcome.Corrupt);
                return live with { Original = proof, OriginalLocation = location };
            }
            if (live.Outcome != CharacterCareerGearEquipmentOutcome.NotFound) return live;
        }
        if (original is null) return new(CharacterCareerGearEquipmentOutcome.NotFound,
            CurrentRevision: saved?.ContentRevision, OriginalLocation: location);
        if (original.Outcome is not (CharacterCareerGearEquipmentOutcome.Pending or CharacterCareerGearEquipmentOutcome.NeverApplied)
            && location == CharacterCareerGearEquipmentOriginalLocation.LiveOriginal)
            return new(CharacterCareerGearEquipmentOutcome.Corrupt, Error: "gear_original_history_missing");
        if (original.Outcome == CharacterCareerGearEquipmentOutcome.NeverApplied)
            return ConfirmDurableGearExclusionUnderLease(owner, id, path, archive, original, saved?.ContentRevision, location);
        // Archive metadata proves an original outcome, never that unlink happened.
        // Presence is observed independently under this same path lease.
        return new(original.Outcome == CharacterCareerGearEquipmentOutcome.Applied ? CharacterCareerGearEquipmentOutcome.Recorded : original.Outcome,
            CurrentRevision: saved?.ContentRevision, Original: original, OriginalLocation: location);
    }

    private const int MaximumGearOriginalArchiveBytes = 4 * 1024 * 1024;
    private const string GearOriginalArchiveSuffix = ".gear-equipped-outcomes.v1.json";
    private static readonly JsonSerializerOptions GearArchiveJson = new()
    { MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private sealed record GearOriginalArchive(int SchemaVersion, IReadOnlyList<CharacterCareerGearEquipmentOriginalOutcome> Entries, string Digest);
    // Called only after exact original identity and live receipt checks, under the SAME path lease.
    // A readable post-replace exclusion is not terminal authority until durability succeeds.
    private CharacterCareerGearEquipmentResult ConfirmDurableGearExclusionUnderLease(OwnerScope owner,
        CharacterWorkspaceId id, string workspacePath, GearOriginalArchive expectedArchive,
        CharacterCareerGearEquipmentOriginalOutcome expectedOriginal, long? currentRevision,
        CharacterCareerGearEquipmentOriginalLocation location)
    {
        try
        {
            if (expectedOriginal.Outcome != CharacterCareerGearEquipmentOutcome.NeverApplied
                || !CharacterCareerGearEquipmentTransaction.IsCoherentOriginal(expectedOriginal)
                || expectedOriginal.OwnerId != owner.NormalizedValue
                || expectedOriginal.TrustedLocalOwner != owner.IsLocalSingleUser || expectedOriginal.WorkspaceId != id
                || !CharacterCareerGearEquipmentTransaction.Same(expectedOriginal,
                    expectedArchive.Entries.SingleOrDefault(p => p.OperationId == expectedOriginal.OperationId))
                || !CharacterCareerGearEquipmentTransaction.Same(expectedArchive, ReadGearOriginalArchive(owner, id, workspacePath)))
                return new(CharacterCareerGearEquipmentOutcome.Corrupt);
            string path = workspacePath + GearOriginalArchiveSuffix;
            EnsurePathContained(_stateDirectory, path, "Gear original outcome metadata");
            ThrowIfLinkOrReparsePoint(path, "Gear original outcome metadata");
            // Open existing only. Writable access is needed for Flush(true); no bytes are written.
            // Do not use a creation mode, chmod, migrate, clean temporary files or repair metadata.
            using (var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open, Access = FileAccess.ReadWrite, Share = FileShare.Read,
                Options = FileOptions.WriteThrough, BufferSize = FileBufferSize
            }))
            {
                if (stream.Length is <= 0 or > MaximumGearOriginalArchiveBytes)
                    return new(CharacterCareerGearEquipmentOutcome.Corrupt);
                stream.Flush(flushToDisk: true);
            }
            _ = FlushRetainedDirectoryChain(Path.GetDirectoryName(path)!);
            var durable = ReadGearOriginalArchive(owner, id, workspacePath);
            if (!CharacterCareerGearEquipmentTransaction.Same(expectedArchive, durable))
                return new(CharacterCareerGearEquipmentOutcome.Corrupt);
            return new(CharacterCareerGearEquipmentOutcome.NeverApplied, CurrentRevision: currentRevision,
                Original: expectedOriginal, OriginalLocation: location);
        }
        catch (Exception e) when (GearStorageFailure(e))
        { return new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "gear_original_durability_unavailable"); }
    }

    private static GearOriginalArchive SealGearArchive(IReadOnlyList<CharacterCareerGearEquipmentOriginalOutcome> entries)
    {
        var archive = new GearOriginalArchive(1, entries, "");
        return archive with { Digest = CharacterCareerGearEquipmentTransaction.Hash(JsonSerializer.Serialize(archive, GearArchiveJson)) };
    }
    private static bool FitsGearTerminalArchive(GearOriginalArchive archive)
    {
        if (archive.Entries.Count > CharacterCareerGearEquipmentTransaction.MaximumReceipts) return false;
        // Reserve the largest terminal scalar shape for EVERY reservation at admission.
        // Deletion cannot discover a new byte-capacity requirement after the Gear commit.
        var maximum = SealGearArchive(archive.Entries.Select(p => p with
        { Outcome = CharacterCareerGearEquipmentOutcome.Applied, RecordedRevision = long.MaxValue,
            CommittedRevision = long.MaxValue, ReceiptDigest = new string('f', 64) }).ToArray());
        return JsonSerializer.SerializeToUtf8Bytes(maximum, GearArchiveJson).Length <= MaximumGearOriginalArchiveBytes;
    }
    private GearOriginalArchive ReadGearOriginalArchive(OwnerScope owner, CharacterWorkspaceId id, string workspacePath)
    {
        var archive = ReadGearOriginalArchiveForPath(workspacePath);
        if (archive.Entries.Any(p => p.OwnerId != owner.NormalizedValue || p.TrustedLocalOwner != owner.IsLocalSingleUser || p.WorkspaceId != id))
            throw new IOException("Gear metadata owner or workspace differs.");
        return archive;
    }
    private GearOriginalArchive ReadGearOriginalArchiveForPath(string workspacePath)
    {
        string path = workspacePath + GearOriginalArchiveSuffix;
        EnsurePathContained(_stateDirectory, path, "Gear original outcome metadata");
        ThrowIfLinkOrReparsePoint(path, "Gear original outcome metadata");
        FileStream stream;
        try { stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (FileNotFoundException) { return SealGearArchive([]); }
        using (stream)
        {
            if (stream.Length is <= 0 or > MaximumGearOriginalArchiveBytes) throw new IOException("Gear original metadata size is invalid.");
            try
            {
                using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
                RequireUniqueGearMetadata(json.RootElement);
                var archive = JsonSerializer.Deserialize<GearOriginalArchive>(json.RootElement, GearArchiveJson);
                if (archive is not { SchemaVersion: 1, Entries: not null }
                    || archive.Entries.Any(p => !CharacterCareerGearEquipmentTransaction.IsCoherentOriginal(p)
                        || !PathComparer.Equals(TryGetPath(p.TrustedLocalOwner ? OwnerScope.LocalSingleUser : new OwnerScope(p.OwnerId), p.WorkspaceId), workspacePath))
                    || !FitsGearTerminalArchive(archive) || archive.Digest != SealGearArchive(archive.Entries).Digest
                    || archive.Entries.Select(p => p.OperationId).Distinct().Count() != archive.Entries.Count)
                    throw new IOException("Gear original metadata is invalid.");
                return archive;
            }
            catch (JsonException error)
            {
                // Central generic-save callers handle storage failures as IO errors.
                // Preserve corrupt bytes and fail closed; never reinterpret them as empty.
                throw new IOException("Gear original metadata could not be read.", error);
            }
        }
    }
    private void WriteGearOriginalArchive(OwnerScope owner, CharacterWorkspaceId id, string workspacePath, GearOriginalArchive archive)
    {
        if (!FitsGearTerminalArchive(archive)) throw new IOException("Gear original metadata capacity exhausted before admission.");
        string path = workspacePath + GearOriginalArchiveSuffix;
        string temporary = path + TempFileMarker + Guid.NewGuid().ToString("N");
        EnsurePathContained(_stateDirectory, path, "Gear original outcome metadata");
        ThrowIfLinkOrReparsePoint(path, "Gear original outcome metadata");
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(archive, GearArchiveJson);
        RemoveStaleTempFiles(path); // Only under the write lease; lookup never repairs or cleans metadata.
        try
        {
            using (var stream = OpenNewSecureFile(temporary, FileAccess.Write, FileShare.None, FileOptions.WriteThrough))
            { stream.Write(payload); stream.Flush(flushToDisk: true); }
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterTempFileFlushed, path, temporary);
            ThrowIfLinkOrReparsePoint(path, "Gear original outcome metadata");
            File.Move(temporary, path, overwrite: true);
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterTargetReplaced, path, temporary);
            // Reuse the store's platform durability contract. Linux flushes the
            // directory chain; other platforms promise process-restart durability
            // after the flushed atomic replacement, not universal power-loss proof.
            _ = FlushRetainedDirectoryChain(Path.GetDirectoryName(path)!);
            if (!CharacterCareerGearEquipmentTransaction.Same(ReadGearOriginalArchive(owner, id, workspacePath), archive))
                throw new IOException("Gear metadata readback does not match the original outcomes.");
        }
        finally { DeleteRegularFileIfPresent(temporary, "Gear metadata temporary file"); }
    }
    private void PreserveGearOriginalsBeforeDelete(OwnerScope owner, CharacterWorkspaceId id, string path, WorkspaceStoredDocument saved)
    {
        if (HasPendingGearEquipment(saved)) throw new IOException("Resolve the original pending Gear operation before deletion.");
        var archive = ReadGearOriginalArchive(owner, id, path);
        var local = saved.LocalHistory ?? throw new IOException("Workspace incarnation is unavailable.");
        var receipts = (saved.Document.AuxiliaryState.CharacterCareerGearEquipmentReceipts ?? [])
            .Where(r => (r.Outcome != CharacterCareerGearEquipmentOutcome.Applied || saved.CanReplayReceipt(r.RecordedRevision))
                && r.Command.Binding.IncarnationId == local.IncarnationId).ToArray();
        var originals = archive.Entries.ToArray();
        foreach (var receipt in receipts)
        {
            int index = Array.FindIndex(originals, p => p.OperationId == receipt.Command.Request.OperationId);
            if (index < 0 || !CharacterCareerGearEquipmentTransaction.IsCoherent(receipt)
                || !CharacterCareerGearEquipmentTransaction.MatchesOriginal(receipt.Command, originals[index]))
                throw new IOException("Original Gear archive reservation is unavailable.");
            var proof = CharacterCareerGearEquipmentTransaction.Original(receipt);
            if (originals[index].Outcome != CharacterCareerGearEquipmentOutcome.Pending
                && !CharacterCareerGearEquipmentTransaction.Same(originals[index], proof))
                throw new IOException("Original Gear outcome metadata conflicts.");
            originals[index] = proof;
        }
        if (originals.Any(p => p.IncarnationId == local.IncarnationId && p.Outcome == CharacterCareerGearEquipmentOutcome.Pending))
            throw new IOException("An unresolved original Gear reservation still owns this incarnation.");
        var terminal = SealGearArchive(originals);
        if (!CharacterCareerGearEquipmentTransaction.Same(archive, terminal))
            WriteGearOriginalArchive(owner, id, path, terminal);
        // Caller retires the record only after this durable readback. Publication
        // alone proves neither deletion nor replacement; lookup observes the record.
    }

    private static bool IsGearIncarnationReplacement(PersistedWorkspaceRecord? previous, PersistedWorkspaceRecord next)
        => previous?.LocalHistory is { } before && next.LocalHistory is { } after
            && before.IncarnationId != after.IncarnationId && after.LastRestore is not null
            && next.ContentRevision is { } revision && after.IsValid(revision);

    private void PreserveGearOriginalsBeforeReplacement(string path, PersistedWorkspaceRecord next,
        WorkspaceWriteDisposition disposition)
    {
        if (disposition != WorkspaceWriteDisposition.ReplaceExisting) return;
        var previous = ReadPrivateRecord(path);
        if (!IsGearIncarnationReplacement(previous, next)) return;
        var receipts = previous.AuxiliaryState?.CharacterCareerGearEquipmentReceipts;
        var archive = ReadGearOriginalArchiveForPath(path);
        if (archive.Entries.Any(p => IsUnresolvedGearReservation(p, receipts)))
            throw new IOException("An unresolved original Gear reservation retains write custody.");
        var local = receipts?.FirstOrDefault(r => r.Command.Binding.IncarnationId == previous.LocalHistory!.IncarnationId);
        if (local is null) return;
        var owner = local.Command.Binding.TrustedLocalOwner ? OwnerScope.LocalSingleUser : new OwnerScope(local.Command.Binding.OwnerId);
        var id = local.Command.Request.WorkspaceId;
        if (!PathComparer.Equals(TryGetPath(owner, id), path)
            || !TryMaterializeRecord(previous, out var document, out var revision, out var savedRevision, out _))
            throw new IOException("Original Gear workspace identity is unavailable.");
        PreserveGearOriginalsBeforeDelete(owner, id, path,
            new(id, document, revision, savedRevision, DateTimeOffset.UnixEpoch) { LocalHistory = previous.LocalHistory });
    }
    private static bool IsUnresolvedGearReservation(CharacterCareerGearEquipmentOriginalOutcome p,
        IReadOnlyList<CharacterCareerGearEquipmentReceipt>? receipts)
        => p.Outcome == CharacterCareerGearEquipmentOutcome.Pending && receipts?.Any(r =>
            r.Outcome is CharacterCareerGearEquipmentOutcome.Applied or CharacterCareerGearEquipmentOutcome.Rejected
            && CharacterCareerGearEquipmentTransaction.IsCoherent(r)
            && CharacterCareerGearEquipmentTransaction.MatchesOriginal(r.Command, p)) != true;

    private static bool GearStorageFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException
            or ArgumentException or System.Xml.XmlException or DllNotFoundException or EntryPointNotFoundException;
    private static void RequireUniqueGearMetadata(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw new JsonException("Duplicate Gear metadata key."); RequireUniqueGearMetadata(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) RequireUniqueGearMetadata(child);
    }

    private static bool HasPendingGearEquipment(WorkspaceStoredDocument current)
        => current.Document.AuxiliaryState.CharacterCareerGearEquipmentReceipts?
            .Any(r => r.Outcome == CharacterCareerGearEquipmentOutcome.Pending) == true;
}
