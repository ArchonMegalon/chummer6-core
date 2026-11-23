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

public sealed partial class FileWorkspaceStore : ICharacterCareerGearSaleAtomicCommitCapability
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _issuedGearSales = new(StringComparer.Ordinal);
    // Root implements this only with the shared writer/deletion hooks in the proposal.
    static partial void VerifyCareerGearSaleGuards(ref bool installed);
    public bool CareerGearSaleGuardsInstalled
    { get { bool installed = false; VerifyCareerGearSaleGuards(ref installed); return installed; } }
    public string CareerGearSaleRuntimeDigest => CharacterCareerGearSaleTransaction.Hash(
        "chummer.gear-sale.runtime/v1\0" + typeof(CharacterGearQuantityRules).Module.ModuleVersionId + "\0"
        + typeof(CharacterCareerGearSaleAuthority).Module.ModuleVersionId + "\0" + typeof(FileWorkspaceStore).Module.ModuleVersionId);

    public CharacterCareerGearSaleResult CommitCareerGearSale(OwnerScope owner,
        CharacterCareerGearSaleCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default)
        => CommitCareerGearSale(owner, command, sources, null, null, default, cancellationToken);

    internal CharacterCareerGearSaleResult CommitCareerGearSale(OwnerScope owner,
        CharacterCareerGearSaleCommand command, ICharacterSourceDataResolver sources,
        IOwnerContextAccessor? ownerContext, IOwnerContextLease? ownerLease, OwnerContextStamp ownerStamp, CancellationToken cancellationToken = default)
    {
        if (CharacterCareerGearSaleTransaction.FreezeConfirmedCommand(command) is not { } frozen)
            return new(CharacterCareerGearSaleOutcome.Unavailable);
        command = frozen;
        if (ownerLease is null || ownerContext is not IOwnerContextLeaseAccessor authority || ownerLease.Stamp != ownerStamp || authority.Capture() != ownerStamp
            || command.Binding.OwnerAuthorityInstanceId != ownerStamp.AuthorityInstanceId
            || command.Binding.OwnerTransitionRevision != ownerStamp.TransitionRevision)
            return new(CharacterCareerGearSaleOutcome.Conflict, Error: "gear_sale_owner_changed");
        if (!CareerGearSaleGuardsInstalled || !CharacterCareerGearSaleTransaction.IsConfirmedCommand(command)
            || !CharacterCareerGearSaleTransaction.MatchesOwner(owner, command.Binding))
            return new(CharacterCareerGearSaleOutcome.Unavailable);
        string? path = TryGetPath(owner, command.Request.WorkspaceId);
        if (path is null) return new(CharacterCareerGearSaleOutcome.Unavailable);
        string digest = CharacterCareerGearSaleTransaction.CommandDigest(command);
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var prior = LookupGearSaleOriginalUnderLease(owner, command.Request.WorkspaceId, path, command.Request.OperationId,
                digest, command.Binding.IncarnationId, command.Binding.SlotGeneration);
            if (prior.Outcome != CharacterCareerGearSaleOutcome.NotFound) return prior;
            if (!_issuedGearSales.TryRemove(digest, out _))
                return new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_sale_original_requires_lookup");
            var read = ReadGearWorkspaceUnderLease(owner, command.Request.WorkspaceId, path,
                ownerContext, ownerLease, ownerStamp, out var delegatedLedger, out var currentEnvironment);
            using var current = currentEnvironment;
            if (!read.Success || read.Value is not { } saved) return WorkspaceCharacterCareerGearSaleService.MapReadFailure(read);
            string? slot = ReadContinuationSlotUnderLease(path);
            if (saved.LocalHistory?.IncarnationId != command.Binding.IncarnationId || slot != command.Binding.SlotGeneration)
                return new(CharacterCareerGearSaleOutcome.Conflict, Error: "gear_sale_incarnation_changed");
            var history = saved.Document.AuxiliaryState.CharacterCareerGearSaleReceipts ?? [];
            if (history.Count >= CharacterCareerGearSaleTransaction.MaximumReceipts
                || history.Any(r => r.Outcome == CharacterCareerGearSaleOutcome.Pending))
                return new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_sale_history_retained");

            WorkspaceDocument? replacement = null; CharacterCareerGearSaleReceipt? receipt = null;
            bool valid = !cancellationToken.IsCancellationRequested
                && TryReadGearSaleCatalog(owner, saved, sources, current, out var catalog, slot, ownerStamp, cancellationToken)
                && CharacterCareerGearSaleTransaction.TryBuild(saved, catalog!, command, out replacement, out receipt);
            var pending = CharacterCareerGearSaleTransaction.Seal(command, CharacterCareerGearSaleOutcome.Pending,
                command.Binding.ContentRevision);
            var archive = ReadGearSaleOriginalArchive(owner, saved.Id, path);
            if (archive.Entries.Any(p => IsUnresolvedGearSaleReservation(p, history)))
                return new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_sale_original_reservation_unresolved");
            var reservedArchive = SealGearSaleArchive(archive.Entries.Append(CharacterCareerGearSaleTransaction.Original(pending)).ToArray());
            if (!FitsGearSaleTerminalArchive(reservedArchive))
                return new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_sale_original_archive_capacity");
            var rejected = CharacterCareerGearSaleTransaction.Seal(command, CharacterCareerGearSaleOutcome.Rejected,
                saved.ContentRevision, rejection: "gear_sale_review_not_current");
            WorkspaceDocument WithReceipt(CharacterCareerGearSaleReceipt entry) => saved.Document with
                { State = saved.Document.State with { AuxiliaryState = saved.Document.AuxiliaryState with
                    { CharacterCareerGearSaleReceipts = history.Append(entry).ToArray() } } };
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
            WriteGearSaleOriginalArchive(owner, saved.Id, path, reservedArchive);
            if (!valid)
            {
                // A genuinely observed pre-write rejection is durable; no later inference.
                Write(rejectedDocument, saved.ContentRevision, saved.SavedRevision, rejected);
                return new(CharacterCareerGearSaleOutcome.Rejected, rejected, saved.ContentRevision);
            }
            Write(reserved, saved.ContentRevision, saved.SavedRevision, pending);
            // No other writer can pass the operation lease or the durable pending fence.
            Write(replacement!, receipt!.RecordedRevision, receipt.RecordedRevision, receipt, () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ownerLease.Stamp != ownerStamp || authority.Capture() != ownerStamp
                    || !TryReadGearSaleCatalog(owner, saved, sources, current, out var finalCatalog, slot, ownerStamp, cancellationToken)
                    || !CharacterCareerGearSaleTransaction.TryBuild(saved, finalCatalog!, command, out var finalDocument, out var finalReceipt)
                    || !CharacterCareerGearSaleTransaction.Same(finalDocument, replacement)
                    || !CharacterCareerGearSaleTransaction.Same(finalReceipt, receipt))
                    throw new IOException("gear_sale_quote_changed_before_replace");
                cancellationToken.ThrowIfCancellationRequested();
            });
            return new(CharacterCareerGearSaleOutcome.Applied, receipt, receipt.RecordedRevision);

            void RequireProspectiveAdmission(WorkspaceStoredDocument proposed, CharacterCurrentRuleEnvironmentReadmissionResult observed)
            {
                if (observed.AfterCapture is not { } capture || observed.AfterExecution is not { } execution || observed.Blockers.Count != 0
                    || !IsValidAuxiliaryState(saved.Id, proposed.ContentRevision, proposed.Document.AuxiliaryState, current!.Recovery))
                    throw new IOException("gear_sale_proposed_current_admission_unavailable");
                var references = current.Record.RetainedRuleSources!.References;
                if (!references.Any(value => value.CaptureDigest == capture.IntegrityDigest)
                    && references.Length >= MaximumRetainedRuleSourceReferences)
                    throw new IOException("gear_sale_retained_capture_capacity");
                var bytes = capture.ExportPrivateInputs();
                if (bytes.Length == 0 || bytes.Length > RetainedCharacterRuleSourceInputs.MaximumArchiveBytes
                    || RetainedCharacterRuleSourceInputs.Digest(bytes) != capture.IntegrityDigest)
                    throw new IOException("gear_sale_retained_capture_capacity");
                // Size the actual candidate shape before a durable pending reservation.
                // This is only a prospective size check; the writer independently publishes
                // and reopens the real archive and obtains a fresh canonical pair below.
                var reference = new CharacterCreationRetainedRuleSourceReference(1, references[0].OwnerBinding,
                    saved.Id.Value, saved.LocalHistory!.IncarnationId, capture.IntegrityDigest, bytes.LongLength);
                var prospectiveReferences = references.Any(value => value.CaptureDigest == capture.IntegrityDigest)
                    ? references : references.Append(reference).OrderBy(value => value.CaptureDigest, StringComparer.Ordinal).ToArray();
                var record = BuildCurrentRecord(proposed, current.Record.RetainedRuleSources!, prospectiveReferences, capture, execution);
                if (JsonSerializer.SerializeToUtf8Bytes(record).Length > MaximumRetainedWorkspaceRecordBytes)
                    throw new IOException("gear_sale_retained_record_capacity");
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
                CharacterCareerGearSaleReceipt admitted, Action? fence = null)
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
                    throw new IOException("gear_sale_before_write_changed");
                if (!IsValidAuxiliaryState(saved.Id, revision, document.AuxiliaryState, actualCurrent?.Recovery))
                    throw new IOException("gear_sale_auxiliary_invalid");
                var proposedAfter = actualBefore with { Document = document, ContentRevision = revision, SavedRevision = savedRevision };
                CharacterCurrentRuleEnvironmentReadmissionResult? readmission = null;
                if (actualCurrent is not null)
                {
                    readmission = actualCurrent.Execution.ReadmitProposedCurrentEnvironment(actualBefore,
                        actualCurrent.Capture, _retainedCharacterQueries!, proposedAfter, cancellationToken);
                    if (readmission.AfterCapture is not { } afterCapture || readmission.AfterExecution is not { } afterExecution
                        || readmission.Blockers.Count != 0) throw new IOException("gear_sale_after_current_admission_unavailable");
                    actualCurrent.Recovery.RetainAdmittedCapture(new(afterCapture, _retainedCharacterQueries!), afterCapture.IntegrityDigest);
                    record = BuildCurrentRecord(proposedAfter, actualCurrent.Record.RetainedRuleSources!,
                        actualCurrent.Recovery.SnapshotReferences(), afterCapture, afterExecution);
                }
                else
                {
                    if (current is not null) throw new IOException("gear_sale_current_environment_lost");
                    record = PreserveRetainedRuleSources(path, record, WorkspaceWriteDisposition.ReplaceExisting);
                }
                // Private store/path/operation/full-record binding; no request can create it.
                using var admission = new GearSaleWriteAdmission(this, path, admitted, record, actualCurrent,
                    proposedAfter, readmission, command.Binding.SlotGeneration, ownerLease, ownerContext, ownerStamp, cancellationToken);
                WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: fence);
            }
        }
        catch (Exception e) when (GearSaleStorageFailure(e) || e is OperationCanceledException)
        { return new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_sale_original_result_unknown"); }
    }

    // Private per-instance, thread-local admission; concurrent workspaces cannot borrow it.
    [ThreadStatic] private static GearSaleWriteAdmission? _gearSaleAdmission;
    private sealed class GearSaleWriteAdmission : IDisposable
    {
        private readonly GearSaleWriteAdmission? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal CharacterCareerGearSaleReceipt Receipt { get; }
        internal string RecordDigest { get; }
        internal GearCurrentEnvironment? Current { get; }
        private readonly WorkspaceStoredDocument _after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? _readmission;
        private readonly string _slot;
        private readonly string _beforeDigest;
        private readonly IOwnerContextLease _ownerLease;
        private readonly IOwnerContextAccessor _ownerContext;
        private readonly OwnerContextStamp _ownerStamp;
        private readonly CancellationToken _cancellationToken;
        internal GearSaleWriteAdmission(FileWorkspaceStore store, string path, CharacterCareerGearSaleReceipt receipt,
            PersistedWorkspaceRecord record, GearCurrentEnvironment? current, WorkspaceStoredDocument after,
            CharacterCurrentRuleEnvironmentReadmissionResult? readmission, string slot,
            IOwnerContextLease ownerLease, IOwnerContextAccessor ownerContext, OwnerContextStamp ownerStamp, CancellationToken cancellationToken)
        {
            _previous = _gearSaleAdmission; Store = store; Path = System.IO.Path.GetFullPath(path); Receipt = receipt;
            RecordDigest = CharacterCreationFinalizationDigest.Compute(record); Current = current;
            _after = after; _readmission = readmission; _slot = slot;
            _ownerLease = ownerLease; _ownerContext = ownerContext; _ownerStamp = ownerStamp; _cancellationToken = cancellationToken;
            _beforeDigest = CharacterCreationFinalizationDigest.Compute(store.ReadPrivateRecord(path));
            _gearSaleAdmission = this;
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            if (FileWorkspaceStore.ReadContinuationSlotUnderLease(Path) != _slot
                || CharacterCreationFinalizationDigest.Compute(Store.ReadPrivateRecord(Path)) != _beforeDigest
                || CharacterCreationFinalizationDigest.Compute(record) != RecordDigest)
                throw new IOException("gear_sale_exact_record_or_slot_changed");
            if (Current is not null)
            {
                Current.ValidateFinalFence();
                if (_readmission?.AfterCapture is not { } capture || _readmission.AfterExecution is not { } execution
                    || !execution.Revalidate(_after, capture, Store._retainedCharacterQueries!, _cancellationToken))
                    throw new IOException("gear_sale_after_current_environment_changed");
            }
            if (_ownerLease.Stamp != _ownerStamp || _ownerContext is not IOwnerContextLeaseAccessor authority
                || authority.Capture() != _ownerStamp)
                throw new IOException("gear_sale_owner_changed_before_replace");
            _cancellationToken.ThrowIfCancellationRequested();
        }
        public void Dispose() => _gearSaleAdmission = _previous;
    }

    private GearSaleWriteAdmission? GearSaleAdmissionFor(string path)
        => _gearSaleAdmission is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, Path.GetFullPath(path)) ? admission : null;

    private void RevalidateGearSaleForWrite(string path, PersistedWorkspaceRecord record)
        => GearSaleAdmissionFor(path)?.ValidateFinalFence(record);

    private bool TryReadGearSaleCatalog(OwnerScope owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        GearCurrentEnvironment? current, out CharacterCareerGearSaleCatalog? catalog, string? slot,
        OwnerContextStamp ownerStamp, CancellationToken cancellationToken = default)
    {
        catalog = null;
        if (!ownerStamp.IsValid || ownerStamp.Owner != owner) return false;
        bool success = current is null
            ? CharacterCareerGearSaleTransaction.TryRead(owner, saved, sources, CareerGearSaleRuntimeDigest, out catalog, slot)
            : CharacterCareerGearSaleTransaction.TryReadCurrentEnvironment(owner, saved, current.Capture, current.Execution,
                _retainedCharacterQueries!, CareerGearSaleRuntimeDigest, out catalog, slot, cancellationToken);
        if (!success || catalog is null) return false;
        catalog = catalog with { Binding = catalog.Binding with { OwnerAuthorityInstanceId = ownerStamp.AuthorityInstanceId,
            OwnerTransitionRevision = ownerStamp.TransitionRevision } };
        return true;
    }

    private void ValidateGearSaleWrite(string path, PersistedWorkspaceRecord record, WorkspaceWriteDisposition disposition)
    {
        if (record.AuxiliaryState is { } proposedAuxiliary && !WorkspaceDocumentAuxiliaryState.HasSafeExtensionData(proposedAuxiliary))
            throw new IOException("Auxiliary extension data cannot shadow typed workspace state.");
        var previous = disposition == WorkspaceWriteDisposition.ReplaceExisting ? ReadPrivateRecord(path) : null;
        var before = previous?.AuxiliaryState?.CharacterCareerGearSaleReceipts;
        var reservations = ReadGearSaleOriginalArchiveForPath(path).Entries;
        var after = record.AuxiliaryState?.CharacterCareerGearSaleReceipts;
        var admitted = _gearSaleAdmission is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, Path.GetFullPath(path)) ? admission.Receipt : null;
        if (reservations.Any(p => IsUnresolvedGearSaleReservation(p, before)
            && (admitted is null || !CharacterCareerGearSaleTransaction.MatchesOriginal(admitted.Command, p))))
            throw new IOException("An unresolved original Gear reservation retains write custody.");
        if (admitted is null)
        {
            bool retiring = IsGearSaleIncarnationReplacement(previous, record);
            if (before?.Any(r => r.Outcome == CharacterCareerGearSaleOutcome.Pending) == true
                || previous is not null && !retiring && !CharacterCareerGearSaleTransaction.Same(before, after))
                throw new IOException("Original Gear operation must be resolved; its history is immutable.");
            // A new file may retain imported history, but cannot manufacture locally
            // replayable receipts or import unresolved custody as a new operation.
            if ((previous is null || retiring) && after is { Count: > 0 } && (record.LocalHistory is not { } history
                || after.Any(r => r.Outcome == CharacterCareerGearSaleOutcome.Pending
                    || !history.IsImportedRevision(r.RecordedRevision))))
                throw new IOException("New Gear history must remain imported, never original-operation authority.");
            return;
        }
        if (previous is null || after is null || !CharacterCareerGearSaleTransaction.IsCoherent(admitted))
            throw new IOException("Gear authority requires an existing workspace.");
        var retained = (before ?? []).Where(r => r.Command.Request.OperationId != admitted.Command.Request.OperationId).ToArray();
        if (!CharacterCareerGearSaleTransaction.Same(after, retained.Append(admitted).ToArray()))
            throw new IOException("Gear authority cannot replace other operations.");
        var original = before?.SingleOrDefault(r => r.Command.Request.OperationId == admitted.Command.Request.OperationId);
        if (admitted.Outcome == CharacterCareerGearSaleOutcome.Applied)
        {
            if (original?.Outcome != CharacterCareerGearSaleOutcome.Pending || original.CommandDigest != admitted.CommandDigest
                || previous.ContentRevision != admitted.Command.Binding.ContentRevision
                || record.ContentRevision != previous.ContentRevision + 1 || record.SavedRevision != record.ContentRevision)
                throw new IOException("Gear completion requires the exact original pending operation.");
        }
        else if (original is not null || record.ContentRevision != previous.ContentRevision || record.SavedRevision != previous.SavedRevision
            || record.Format != previous.Format || !CharacterCareerGearSaleTransaction.Same(record.Envelope, previous.Envelope))
            throw new IOException("Gear reservation/rejection cannot change character content.");
        if (!CharacterCareerGearSaleTransaction.Same(record.AuxiliaryState! with { CharacterCareerGearSaleReceipts = before },
            previous.AuxiliaryState ?? WorkspaceDocumentAuxiliaryState.Empty))
            throw new IOException("Gear must preserve every other auxiliary ledger.");
    }

    public CharacterCareerGearSaleReadResult ReadCareerGearSale(OwnerScope owner, CharacterWorkspaceId workspace,
        ICharacterSourceDataResolver sources)
        => ReadCareerGearSale(owner, workspace, sources, null, null, default);

    internal CharacterCareerGearSaleReadResult ReadCareerGearSale(OwnerScope owner, CharacterWorkspaceId workspace,
        ICharacterSourceDataResolver sources, IOwnerContextAccessor? ownerContext, IOwnerContextLease? ownerLease, OwnerContextStamp ownerStamp)
    {
        try
        {
            string? path = TryGetPath(owner, workspace);
            if (!CareerGearSaleGuardsInstalled || path is null || (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)))
                return new(CharacterCareerGearSaleOutcome.Unavailable);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, workspace, path, ownerContext, ownerLease, ownerStamp, out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved || HasPendingGearSale(saved))
                return new(CharacterCareerGearSaleOutcome.Unavailable);
            if (!TryReadGearSaleCatalog(owner, saved, sources, current, out var catalog, ReadContinuationSlotUnderLease(path), ownerStamp))
                return new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_sale_saved_source_or_incarnation_unavailable");
            current?.ValidateFinalFence();
            return new(CharacterCareerGearSaleOutcome.Available, catalog);
        }
        catch (Exception e) when (GearSaleStorageFailure(e)) { return new(CharacterCareerGearSaleOutcome.Unavailable); }
    }

    public CharacterCareerGearSalePreviewResult PreviewCareerGearSale(OwnerScope owner,
        CharacterCareerGearSaleBinding displayed, CharacterCareerGearSaleRequest request, ICharacterSourceDataResolver sources)
        => PreviewCareerGearSale(owner, displayed, request, sources, null, null, default);

    internal CharacterCareerGearSalePreviewResult PreviewCareerGearSale(OwnerScope owner,
        CharacterCareerGearSaleBinding displayed, CharacterCareerGearSaleRequest request, ICharacterSourceDataResolver sources,
        IOwnerContextAccessor? ownerContext, IOwnerContextLease? ownerLease, OwnerContextStamp ownerStamp)
    {
        try
        {
            string? path = TryGetPath(owner, request.WorkspaceId);
            if (!CareerGearSaleGuardsInstalled || path is null || !CharacterCareerGearSaleTransaction.MatchesOwner(owner, displayed))
                return new(CharacterCareerGearSaleOutcome.Unavailable);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, request.WorkspaceId, path, ownerContext, ownerLease, ownerStamp, out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved || HasPendingGearSale(saved)
                || !TryReadGearSaleCatalog(owner, saved, sources, current, out var catalog, ReadContinuationSlotUnderLease(path), ownerStamp)
                || !CharacterCareerGearSaleTransaction.Same(catalog!.Binding, displayed))
                return new(CharacterCareerGearSaleOutcome.Conflict);
            var selected = catalog.Nodes.Where(n => n.InstanceId == request.InstanceId).ToArray();
            var blockers = selected.SelectMany(n => request.OperationKind == CharacterCareerGearOperationKind.Remove
                ? n.RemovalBlockers ?? new[] { "gear_remove_witness_unavailable" } : n.Blockers).Distinct(StringComparer.Ordinal).ToArray();
            if (blockers.Length != 0)
                return new(CharacterCareerGearSaleOutcome.Unavailable, Error: string.Join("; ", blockers));
            if (!CharacterCareerGearSaleTransaction.TryPlan(catalog, request, out var command)
                || !CharacterCareerGearSaleTransaction.TryBuild(saved, catalog, command! with { ExplicitlyConfirmed = true }, out _, out _))
                return new(CharacterCareerGearSaleOutcome.Conflict, Error: "gear_sale_request_or_quote_invalid");
            var archive = ReadGearSaleOriginalArchive(owner, saved.Id, path);
            if (archive.Entries.Any(p => p.OperationId == request.OperationId || p.Outcome == CharacterCareerGearSaleOutcome.Pending
                && p.IncarnationId == displayed.IncarnationId && saved.Document.AuxiliaryState.CharacterCareerGearSaleReceipts?
                    .Any(r => r.Command.Request.OperationId == p.OperationId && r.Outcome != CharacterCareerGearSaleOutcome.Pending) != true))
                return new(CharacterCareerGearSaleOutcome.Conflict);
            var pending = CharacterCareerGearSaleTransaction.Original(CharacterCareerGearSaleTransaction.Seal(
                command! with { ExplicitlyConfirmed = true }, CharacterCareerGearSaleOutcome.Pending, displayed.ContentRevision));
            current?.ValidateFinalFence();
            if (!FitsGearSaleTerminalArchive(SealGearSaleArchive(archive.Entries.Append(pending).ToArray())))
                return new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_sale_original_archive_capacity");
            string issued = CharacterCareerGearSaleTransaction.CommandDigest(command! with { ExplicitlyConfirmed = true });
            if (!_issuedGearSales.TryAdd(issued, 0)) return new(CharacterCareerGearSaleOutcome.Conflict);
            return new(CharacterCareerGearSaleOutcome.Available, command);
        }
        catch (Exception e) when (GearSaleStorageFailure(e)) { return new(CharacterCareerGearSaleOutcome.Unavailable); }
    }

    public CharacterCareerGearSaleResult CancelUnadmittedCareerGearSale(OwnerScope owner,
        CharacterCareerGearSaleCommand originalCommand, CancellationToken cancellationToken = default)
    {
        if (!CareerGearSaleGuardsInstalled) return new(CharacterCareerGearSaleOutcome.Unavailable);
        if (CharacterCareerGearSaleTransaction.FreezeConfirmedCommand(originalCommand) is not { } command
            || !CharacterCareerGearSaleTransaction.MatchesOwner(owner, command.Binding))
            return new(CharacterCareerGearSaleOutcome.Conflict);
        try
        {
            string? path = TryGetPath(owner, command.Request.WorkspaceId);
            if (path is null || !TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return new(CharacterCareerGearSaleOutcome.Unavailable);
            string digest = CharacterCareerGearSaleTransaction.CommandDigest(command);
            // Same process gate and cross-process file lease as Commit/Lookup. Never re-enter it.
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            CharacterCareerGearSaleResult Observe() => LookupGearSaleOriginalUnderLease(owner,
                command.Request.WorkspaceId, path, command.Request.OperationId, digest,
                command.Binding.IncarnationId, command.Binding.SlotGeneration);
            var prior = Observe();
            if (prior.Outcome != CharacterCareerGearSaleOutcome.NotFound) return prior;
            if (prior.OriginalLocation != CharacterCareerGearSaleOriginalLocation.LiveOriginal)
                return new(CharacterCareerGearSaleOutcome.Unavailable, OriginalLocation: prior.OriginalLocation);

            // Read private metadata only: no source recovery, evaluation, migration or workspace creation.
            var record = ReadPrivateRecord(path);
            if (!TryMaterializeRecord(record, out var document, out long revision, out long savedRevision, out _)
                || record.LocalHistory is not { } history || !history.IsValid(revision))
                return new(CharacterCareerGearSaleOutcome.Corrupt);
            if (history.IncarnationId != command.Binding.IncarnationId
                || ReadContinuationSlotUnderLease(path) != command.Binding.SlotGeneration
                || command.Binding.ContentRevision > revision || command.Binding.SavedRevision > savedRevision)
                return new(CharacterCareerGearSaleOutcome.Conflict);
            var archive = ReadGearSaleOriginalArchive(owner, command.Request.WorkspaceId, path);
            if (archive.Entries.Any(p => IsUnresolvedGearSaleReservation(p, document.AuxiliaryState.CharacterCareerGearSaleReceipts)))
                return new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_sale_pending_or_capacity");
            var binding = command.Binding;
            var excluded = new CharacterCareerGearSaleOriginalOutcome(binding.OwnerId, binding.TrustedLocalOwner,
                binding.WorkspaceId, binding.IncarnationId, binding.SlotGeneration, command.Request.OperationId,
                digest, binding.ContentRevision, CharacterCareerGearSaleOutcome.NeverApplied,
                binding.ContentRevision, null, null);
            var terminal = SealGearSaleArchive(archive.Entries.Append(excluded).ToArray());
            if (!FitsGearSaleTerminalArchive(terminal))
                return new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_sale_original_archive_capacity");
            cancellationToken.ThrowIfCancellationRequested();
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return new(CharacterCareerGearSaleOutcome.Unavailable);
            try { WriteGearSaleOriginalArchive(owner, command.Request.WorkspaceId, path, terminal); }
            catch (Exception e) when (GearSaleStorageFailure(e) || e is OperationCanceledException)
            { /* Still under the same lease: only readable exact proof can resolve an uncertain write. */ }
            var recovered = Observe();
            if (recovered.Outcome == CharacterCareerGearSaleOutcome.NeverApplied
                && (recovered.Receipt is not null || !CharacterCareerGearSaleTransaction.Same(excluded, recovered.Original)))
                return new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_sale_original_result_unknown");
            return recovered.Outcome == CharacterCareerGearSaleOutcome.NotFound
                ? new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_sale_original_result_unknown") : recovered;
        }
        catch (Exception e) when (GearSaleStorageFailure(e) || e is OperationCanceledException)
        { return new(CharacterCareerGearSaleOutcome.Unavailable); }
    }

    public CharacterCareerGearSaleResult LookupCareerGearSale(OwnerScope owner, CharacterWorkspaceId workspace,
        Guid operationId, string commandDigest, string incarnationId, string slotGeneration)
    {
        if (!CareerGearSaleGuardsInstalled || operationId == Guid.Empty
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(workspace)
            || !CharacterCareerGearSaleTransaction.IsDigest(commandDigest)
            || !CharacterCareerGearSaleTransaction.IsGeneration(incarnationId)
            || !CharacterCareerGearSaleTransaction.IsGeneration(slotGeneration)
            || (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner))) return new(CharacterCareerGearSaleOutcome.Conflict);
        try
        {
            string? path = TryGetPath(owner, workspace);
            if (path is null || !TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return new(CharacterCareerGearSaleOutcome.Unavailable);
            // No Get/migration, temp cleanup, slot rotation, archive repair or execution in lookup.
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            return LookupGearSaleOriginalUnderLease(owner, workspace, path, operationId, commandDigest, incarnationId, slotGeneration);
        }
        catch (Exception e) when (GearSaleStorageFailure(e)) { return new(CharacterCareerGearSaleOutcome.Unavailable); }
    }

    private CharacterCareerGearSaleResult LookupGearSaleOriginalUnderLease(OwnerScope owner, CharacterWorkspaceId id,
        string path, Guid operation, string digest, string incarnation, string slot)
    {
        var archive = ReadGearSaleOriginalArchive(owner, id, path);
        var original = archive.Entries.SingleOrDefault(p => p.OperationId == operation);
        if (original is not null && (original.CommandDigest != digest || original.IncarnationId != incarnation || original.SlotGeneration != slot))
            return new(CharacterCareerGearSaleOutcome.Conflict);
        WorkspaceStoredDocument? saved = null;
        try
        {
            var record = ReadPrivateRecord(path);
            if (!TryMaterializeRecord(record, out var document, out long revision, out long savedRevision, out _)
                || record.LocalHistory is not { } history || !history.IsValid(revision))
                return new(CharacterCareerGearSaleOutcome.Corrupt);
            saved = new(id, document, revision, savedRevision, new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero))
            { LocalHistory = history };
        }
        catch (FileNotFoundException) { }
        var location = saved is null ? CharacterCareerGearSaleOriginalLocation.Missing
            : saved.LocalHistory!.IncarnationId == incarnation ? CharacterCareerGearSaleOriginalLocation.LiveOriginal
            : CharacterCareerGearSaleOriginalLocation.DifferentIncarnation;
        if (saved is not null)
        {
            if (!CharacterCareerGearSaleTransaction.IsGeneration(ReadContinuationSlotUnderLease(path)))
                return new(CharacterCareerGearSaleOutcome.Corrupt);
            var live = location == CharacterCareerGearSaleOriginalLocation.LiveOriginal
                ? CharacterCareerGearSaleTransaction.Lookup(owner, saved, operation, digest)
                : new CharacterCareerGearSaleResult(CharacterCareerGearSaleOutcome.NotFound);
            if (live.Receipt is { } receipt)
            {
                if (original is null || location != CharacterCareerGearSaleOriginalLocation.LiveOriginal
                    || !CharacterCareerGearSaleTransaction.MatchesOriginal(receipt.Command, original)
                    || receipt.Command.Binding.IncarnationId != incarnation || receipt.Command.Binding.SlotGeneration != slot)
                    return new(CharacterCareerGearSaleOutcome.Conflict);
                var proof = CharacterCareerGearSaleTransaction.Original(receipt);
                if (original.Outcome != CharacterCareerGearSaleOutcome.Pending
                    && !CharacterCareerGearSaleTransaction.Same(original, proof)) return new(CharacterCareerGearSaleOutcome.Corrupt);
                return live with { Original = proof, OriginalLocation = location };
            }
            if (live.Outcome != CharacterCareerGearSaleOutcome.NotFound) return live;
        }
        if (original is null) return new(CharacterCareerGearSaleOutcome.NotFound,
            CurrentRevision: saved?.ContentRevision, OriginalLocation: location);
        if (original.Outcome is not (CharacterCareerGearSaleOutcome.Pending or CharacterCareerGearSaleOutcome.NeverApplied)
            && location == CharacterCareerGearSaleOriginalLocation.LiveOriginal)
            return new(CharacterCareerGearSaleOutcome.Corrupt, Error: "gear_sale_original_history_missing");
        if (original.Outcome == CharacterCareerGearSaleOutcome.NeverApplied)
            return ConfirmDurableGearSaleExclusionUnderLease(owner, id, path, archive, original, saved?.ContentRevision, location);
        // Archive metadata proves an original outcome, never that unlink happened.
        // Presence is observed independently under this same path lease.
        return new(original.Outcome == CharacterCareerGearSaleOutcome.Applied ? CharacterCareerGearSaleOutcome.Recorded : original.Outcome,
            CurrentRevision: saved?.ContentRevision, Original: original, OriginalLocation: location);
    }

    private const int MaximumGearSaleOriginalArchiveBytes = 4 * 1024 * 1024;
    private const string GearSaleOriginalArchiveSuffix = ".gear-sale-outcomes.v1.json";
    private static readonly JsonSerializerOptions GearSaleArchiveJson = new()
    { MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private sealed record GearSaleOriginalArchive(int SchemaVersion, IReadOnlyList<CharacterCareerGearSaleOriginalOutcome> Entries, string Digest);
    // Called only after exact original identity and live receipt checks, under the SAME path lease.
    // A readable post-replace exclusion is not terminal authority until durability succeeds.
    private CharacterCareerGearSaleResult ConfirmDurableGearSaleExclusionUnderLease(OwnerScope owner,
        CharacterWorkspaceId id, string workspacePath, GearSaleOriginalArchive expectedArchive,
        CharacterCareerGearSaleOriginalOutcome expectedOriginal, long? currentRevision,
        CharacterCareerGearSaleOriginalLocation location)
    {
        try
        {
            if (expectedOriginal.Outcome != CharacterCareerGearSaleOutcome.NeverApplied
                || !CharacterCareerGearSaleTransaction.IsCoherentOriginal(expectedOriginal)
                || expectedOriginal.OwnerId != owner.NormalizedValue
                || expectedOriginal.TrustedLocalOwner != owner.IsLocalSingleUser || expectedOriginal.WorkspaceId != id
                || !CharacterCareerGearSaleTransaction.Same(expectedOriginal,
                    expectedArchive.Entries.SingleOrDefault(p => p.OperationId == expectedOriginal.OperationId))
                || !CharacterCareerGearSaleTransaction.Same(expectedArchive, ReadGearSaleOriginalArchive(owner, id, workspacePath)))
                return new(CharacterCareerGearSaleOutcome.Corrupt);
            string path = workspacePath + GearSaleOriginalArchiveSuffix;
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
                if (stream.Length is <= 0 or > MaximumGearSaleOriginalArchiveBytes)
                    return new(CharacterCareerGearSaleOutcome.Corrupt);
                stream.Flush(flushToDisk: true);
            }
            _ = FlushRetainedDirectoryChain(Path.GetDirectoryName(path)!);
            var durable = ReadGearSaleOriginalArchive(owner, id, workspacePath);
            if (!CharacterCareerGearSaleTransaction.Same(expectedArchive, durable))
                return new(CharacterCareerGearSaleOutcome.Corrupt);
            return new(CharacterCareerGearSaleOutcome.NeverApplied, CurrentRevision: currentRevision,
                Original: expectedOriginal, OriginalLocation: location);
        }
        catch (Exception e) when (GearSaleStorageFailure(e))
        { return new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_sale_original_durability_unavailable"); }
    }

    private static GearSaleOriginalArchive SealGearSaleArchive(IReadOnlyList<CharacterCareerGearSaleOriginalOutcome> entries)
    {
        var archive = new GearSaleOriginalArchive(1, entries, "");
        return archive with { Digest = CharacterCareerGearSaleTransaction.Hash(JsonSerializer.Serialize(archive, GearSaleArchiveJson)) };
    }
    private static bool FitsGearSaleTerminalArchive(GearSaleOriginalArchive archive)
    {
        if (archive.Entries.Count > CharacterCareerGearSaleTransaction.MaximumReceipts) return false;
        // Reserve the largest terminal scalar shape for EVERY reservation at admission.
        // Deletion cannot discover a new byte-capacity requirement after the Gear commit.
        var maximum = SealGearSaleArchive(archive.Entries.Select(p => p with
        { Outcome = CharacterCareerGearSaleOutcome.Applied, RecordedRevision = long.MaxValue,
            CommittedRevision = long.MaxValue, ReceiptDigest = new string('f', 64) }).ToArray());
        return JsonSerializer.SerializeToUtf8Bytes(maximum, GearSaleArchiveJson).Length <= MaximumGearSaleOriginalArchiveBytes;
    }
    private GearSaleOriginalArchive ReadGearSaleOriginalArchive(OwnerScope owner, CharacterWorkspaceId id, string workspacePath)
    {
        var archive = ReadGearSaleOriginalArchiveForPath(workspacePath);
        if (archive.Entries.Any(p => p.OwnerId != owner.NormalizedValue || p.TrustedLocalOwner != owner.IsLocalSingleUser || p.WorkspaceId != id))
            throw new IOException("Gear metadata owner or workspace differs.");
        return archive;
    }
    private GearSaleOriginalArchive ReadGearSaleOriginalArchiveForPath(string workspacePath)
    {
        string path = workspacePath + GearSaleOriginalArchiveSuffix;
        EnsurePathContained(_stateDirectory, path, "Gear original outcome metadata");
        ThrowIfLinkOrReparsePoint(path, "Gear original outcome metadata");
        FileStream stream;
        try { stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (FileNotFoundException) { return SealGearSaleArchive([]); }
        using (stream)
        {
            if (stream.Length is <= 0 or > MaximumGearSaleOriginalArchiveBytes) throw new IOException("Gear original metadata size is invalid.");
            try
            {
                using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
                RequireUniqueGearSaleMetadata(json.RootElement);
                var archive = JsonSerializer.Deserialize<GearSaleOriginalArchive>(json.RootElement, GearSaleArchiveJson);
                if (archive is not { SchemaVersion: 1, Entries: not null }
                    || archive.Entries.Any(p => !CharacterCareerGearSaleTransaction.IsCoherentOriginal(p)
                        || !PathComparer.Equals(TryGetPath(p.TrustedLocalOwner ? OwnerScope.LocalSingleUser : new OwnerScope(p.OwnerId), p.WorkspaceId), workspacePath))
                    || !FitsGearSaleTerminalArchive(archive) || archive.Digest != SealGearSaleArchive(archive.Entries).Digest
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
    private void WriteGearSaleOriginalArchive(OwnerScope owner, CharacterWorkspaceId id, string workspacePath, GearSaleOriginalArchive archive)
    {
        if (!FitsGearSaleTerminalArchive(archive)) throw new IOException("Gear original metadata capacity exhausted before admission.");
        string path = workspacePath + GearSaleOriginalArchiveSuffix;
        string temporary = path + TempFileMarker + Guid.NewGuid().ToString("N");
        EnsurePathContained(_stateDirectory, path, "Gear original outcome metadata");
        ThrowIfLinkOrReparsePoint(path, "Gear original outcome metadata");
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(archive, GearSaleArchiveJson);
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
            if (!CharacterCareerGearSaleTransaction.Same(ReadGearSaleOriginalArchive(owner, id, workspacePath), archive))
                throw new IOException("Gear metadata readback does not match the original outcomes.");
        }
        finally { DeleteRegularFileIfPresent(temporary, "Gear metadata temporary file"); }
    }
    private void PreserveGearSaleOriginalsBeforeDelete(OwnerScope owner, CharacterWorkspaceId id, string path, WorkspaceStoredDocument saved)
    {
        if (HasPendingGearSale(saved)) throw new IOException("Resolve the original pending Gear operation before deletion.");
        var archive = ReadGearSaleOriginalArchive(owner, id, path);
        var local = saved.LocalHistory ?? throw new IOException("Workspace incarnation is unavailable.");
        var receipts = (saved.Document.AuxiliaryState.CharacterCareerGearSaleReceipts ?? [])
            .Where(r => (r.Outcome != CharacterCareerGearSaleOutcome.Applied || saved.CanReplayReceipt(r.RecordedRevision))
                && r.Command.Binding.IncarnationId == local.IncarnationId).ToArray();
        var originals = archive.Entries.ToArray();
        foreach (var receipt in receipts)
        {
            int index = Array.FindIndex(originals, p => p.OperationId == receipt.Command.Request.OperationId);
            if (index < 0 || !CharacterCareerGearSaleTransaction.IsCoherent(receipt)
                || !CharacterCareerGearSaleTransaction.MatchesOriginal(receipt.Command, originals[index]))
                throw new IOException("Original Gear archive reservation is unavailable.");
            var proof = CharacterCareerGearSaleTransaction.Original(receipt);
            if (originals[index].Outcome != CharacterCareerGearSaleOutcome.Pending
                && !CharacterCareerGearSaleTransaction.Same(originals[index], proof))
                throw new IOException("Original Gear outcome metadata conflicts.");
            originals[index] = proof;
        }
        if (originals.Any(p => p.IncarnationId == local.IncarnationId && p.Outcome == CharacterCareerGearSaleOutcome.Pending))
            throw new IOException("An unresolved original Gear reservation still owns this incarnation.");
        var terminal = SealGearSaleArchive(originals);
        if (!CharacterCareerGearSaleTransaction.Same(archive, terminal))
            WriteGearSaleOriginalArchive(owner, id, path, terminal);
        // Caller retires the record only after this durable readback. Publication
        // alone proves neither deletion nor replacement; lookup observes the record.
    }

    private static bool IsGearSaleIncarnationReplacement(PersistedWorkspaceRecord? previous, PersistedWorkspaceRecord next)
        => previous?.LocalHistory is { } before && next.LocalHistory is { } after
            && before.IncarnationId != after.IncarnationId && after.LastRestore is not null
            && next.ContentRevision is { } revision && after.IsValid(revision);

    private void PreserveGearSaleOriginalsBeforeReplacement(string path, PersistedWorkspaceRecord next,
        WorkspaceWriteDisposition disposition)
    {
        if (disposition != WorkspaceWriteDisposition.ReplaceExisting) return;
        var previous = ReadPrivateRecord(path);
        if (!IsGearSaleIncarnationReplacement(previous, next)) return;
        var receipts = previous.AuxiliaryState?.CharacterCareerGearSaleReceipts;
        var archive = ReadGearSaleOriginalArchiveForPath(path);
        if (archive.Entries.Any(p => IsUnresolvedGearSaleReservation(p, receipts)))
            throw new IOException("An unresolved original Gear reservation retains write custody.");
        var local = receipts?.FirstOrDefault(r => r.Command.Binding.IncarnationId == previous.LocalHistory!.IncarnationId);
        if (local is null) return;
        var owner = local.Command.Binding.TrustedLocalOwner ? OwnerScope.LocalSingleUser : new OwnerScope(local.Command.Binding.OwnerId);
        var id = local.Command.Request.WorkspaceId;
        if (!PathComparer.Equals(TryGetPath(owner, id), path)
            || !TryMaterializeRecord(previous, out var document, out var revision, out var savedRevision, out _))
            throw new IOException("Original Gear workspace identity is unavailable.");
        PreserveGearSaleOriginalsBeforeDelete(owner, id, path,
            new(id, document, revision, savedRevision, DateTimeOffset.UnixEpoch) { LocalHistory = previous.LocalHistory });
    }
    private static bool IsUnresolvedGearSaleReservation(CharacterCareerGearSaleOriginalOutcome p,
        IReadOnlyList<CharacterCareerGearSaleReceipt>? receipts)
        => p.Outcome == CharacterCareerGearSaleOutcome.Pending && receipts?.Any(r =>
            r.Outcome is CharacterCareerGearSaleOutcome.Applied or CharacterCareerGearSaleOutcome.Rejected
            && CharacterCareerGearSaleTransaction.IsCoherent(r)
            && CharacterCareerGearSaleTransaction.MatchesOriginal(r.Command, p)) != true;

    private static bool GearSaleStorageFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException
            or ArgumentException or System.Xml.XmlException or DllNotFoundException or EntryPointNotFoundException;
    private static void RequireUniqueGearSaleMetadata(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw new JsonException("Duplicate Gear metadata key."); RequireUniqueGearSaleMetadata(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) RequireUniqueGearSaleMetadata(child);
    }

    private static bool HasPendingGearSale(WorkspaceStoredDocument current)
        => current.Document.AuxiliaryState.CharacterCareerGearSaleReceipts?
            .Any(r => r.Outcome == CharacterCareerGearSaleOutcome.Pending) == true;
}
