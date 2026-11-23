using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerMartialAcquisitionAtomicCommitCapability
{
    static partial void VerifyCareerMartialAcquisitionGuards(ref bool installed);
    public bool CareerMartialAcquisitionGuardsInstalled { get { bool value = false; VerifyCareerMartialAcquisitionGuards(ref value); return value; } }
    public string CareerMartialAcquisitionRuntimeDigest => CharacterCareerMartialAcquisitionTransaction.Hash(
        "chummer.core.sr5-career-martial-acquisition-runtime/v1\0" + typeof(CharacterCareerMartialAcquisitionQuote).Module.ModuleVersionId
        + "\0" + typeof(CharacterCareerMartialAcquisitionTransaction).Module.ModuleVersionId + "\0" + typeof(FileWorkspaceStore).Module.ModuleVersionId);
    // Reuse the store's existing admitted current-environment lane. A retained character
    // never falls through to installed/default sources. The resolver stays inside its lease.
    private ICharacterSourceDataResolver MartialSources(GearCurrentEnvironment? current, ICharacterSourceDataResolver sources)
    {
        if (current is null) return sources;
        current.ValidateReadObservation();
        if (!current.Capture.MatchesOriginalCharacter(current.Stored.Document.Content))
            throw new IOException("martial_acquisition_current_capture_mismatch");
        return current.Capture.CreateCurrentEnvironmentResolution(current.Execution.ExecutionProfile,
            current.Execution.ExecutionProfileFingerprint)?.Resolver
            ?? throw new IOException("martial_acquisition_current_resolution_unavailable");
    }
    private string MartialRuntime(GearCurrentEnvironment? current, string? slot)
        => CharacterCareerMartialAcquisitionTransaction.Hash(string.Join('\0', CareerMartialAcquisitionRuntimeDigest,
            slot ?? "", current?.Capture.IntegrityDigest ?? "", current?.Execution.ExecutionProfile ?? "",
            current?.Execution.ExecutionProfileFingerprint ?? ""));

    public WorkspaceStoreReadResult ReadCareerMartialAcquisitionWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id)
    {
        var stamp = lease.Stamp;
        if (!CareerMartialAcquisitionGuardsInstalled || !stamp.IsValid || owners.Capture() != stamp)
            return UnavailableRead();
        try
        {
            string? path = TryGetPath(stamp.Owner, id);
            if (path is null) return MissingRead();
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(stamp.Owner, id, path, owners, lease, stamp, out _, out var environment);
            using var current = environment;
            current?.ValidateReadObservation();
            return owners.Capture() == stamp && lease.Stamp == stamp ? read : UnavailableRead();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return UnavailableRead(); }
    }

    public CharacterCareerMartialAcquisitionResult RecoverRestoredCareerMartialAcquisitionReceipt(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, WorkspaceStoredDocument expected, Guid operationId, string commandDigest)
    {
        var conflict = new CharacterCareerMartialAcquisitionResult(CharacterCareerMartialAcquisitionOutcome.IdempotencyConflict,
            CurrentWorkspaceRevision: expected.ContentRevision);
        var stamp = lease.Stamp;
        if (!CareerMartialAcquisitionGuardsInstalled || !stamp.IsValid || owners.Capture() != stamp
            || operationId == Guid.Empty || !CharacterCareerMartialAcquisitionTransaction.IsDigest(commandDigest)) return conflict;
        try
        {
            string? path = TryGetPath(stamp.Owner, expected.Id);
            if (path is null) return conflict;
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(stamp.Owner, expected.Id, path, owners, lease, stamp, out _, out var environment);
            using var current = environment;
            // Only the store's validated private restore/current-source custody can recover
            // imported history. A portable receipt, caller observation or local-history DTO
            // is insufficient. Re-read under the owner/slot/source/record fences.
            if (!read.Success || read.Value is not { } saved || current is null
                || CharacterCreationFinalizationDigest.Compute(saved) != CharacterCreationFinalizationDigest.Compute(expected)
                || !CharacterCareerMartialAcquisitionTransaction.IsValidHistory(saved)
                || saved.LocalHistory is not { LastRestore: { } restore } history
                || !history.IsValid(saved.ContentRevision)
                || current.Record.RetainedRuleSources?.RestoreEvidence is not { Length: > 0 } evidence
                || evidence[^1].Kind != OrdinaryRestoreEvidence || evidence[^1].Receipt != restore
                || current.Record.RetainedRuleSources.ActivatedClosureBinding is not { } activation
                || activation.ContinuationDigest != history.ImportedSnapshotDigest
                || activation.ClosureDigest != restore.SourceDigest) return conflict;
            var receipt = saved.Document.AuxiliaryState.CharacterCareerMartialAcquisitionReceipts?
                .SingleOrDefault(row => row.Command.Request.OperationId == operationId);
            if (receipt is null || receipt.NeverApplied || !history.IsImportedRevision(receipt.CommittedWorkspaceRevision)
                || receipt.Command.Binding.LocalHistoryIncarnationId == history.IncarnationId
                || receipt.Command.Binding.OwnerId != stamp.Owner.NormalizedValue
                || receipt.Command.Binding.LocalSingleUser != stamp.Owner.IsLocalSingleUser
                || receipt.CommandDigest != commandDigest
                || !VerifyAuthenticatedHistory(stamp.Owner, saved.Id, saved.Document, saved.ContentRevision, saved.SavedRevision,
                    current.Record.AuthenticatedHistory, digest => current.Recovery.TryRecoverRuleSourceCapture(digest, out var capture)
                        ? capture : null, receipt)) return conflict;
            current.ValidateReadObservation();
            // Report the exact admitted historical result; do not replay its mutation,
            // rebind its command, rewrite its receipt or grant the old incarnation a write.
            return new(CharacterCareerMartialAcquisitionOutcome.Replayed, receipt, saved.ContentRevision);
        }
        catch (Exception e) when (IsRetainedRuleSourceFailure(e))
        { return conflict; }
    }

    public CharacterCareerMartialAcquisitionListResult ListCareerMartialAcquisition(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, long expectedRevision, ICharacterSourceDataResolver sources)
    {
        var stamp = lease.Stamp;
        if (!CareerMartialAcquisitionGuardsInstalled || !stamp.IsValid || owners.Capture() != stamp)
            return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
        try
        {
            string? path = TryGetPath(stamp.Owner, id);
            if (path is null) return new(CharacterCareerMartialAcquisitionOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(stamp.Owner, id, path, owners, lease, stamp, out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return new(CharacterCareerMartialAcquisitionService.MapReadFailure(read).Outcome);
            if (saved.ContentRevision != expectedRevision || saved.SavedRevision != saved.ContentRevision)
                return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
            string? slot = ReadContinuationSlotUnderLease(path);
            bool available = CharacterCareerMartialAcquisitionTransaction.TryCatalog(stamp, saved, MartialSources(current, sources),
                MartialRuntime(current, slot), out var entries, out var error);
            current?.ValidateReadObservation();
            if (owners.Capture() != stamp || lease.Stamp != stamp || slot != ReadContinuationSlotUnderLease(path))
                return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
            return available ? new(CharacterCareerMartialAcquisitionOutcome.Available, entries, saved.ContentRevision)
                : new(CharacterCareerMartialAcquisitionOutcome.Unavailable, Error: error);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(CharacterCareerMartialAcquisitionOutcome.Unavailable, Error: "martial_acquisition_current_sources_unavailable"); }
    }

    public CharacterCareerMartialAcquisitionPreviewResult PreviewCareerMartialAcquisition(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerMartialAcquisitionRequest request, ICharacterSourceDataResolver sources)
    {
        var stamp = lease.Stamp;
        if (!CareerMartialAcquisitionGuardsInstalled || !stamp.IsValid || owners.Capture() != stamp
            || !CharacterCareerMartialAcquisitionTransaction.IsValidRequest(request)) return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
        try
        {
            string? path = TryGetPath(stamp.Owner, request.WorkspaceId);
            if (path is null) return new(CharacterCareerMartialAcquisitionOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(stamp.Owner, request.WorkspaceId, path, owners, lease, stamp, out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return new(CharacterCareerMartialAcquisitionService.MapReadFailure(read).Outcome);
            var prior = CharacterCareerMartialAcquisitionTransaction.Lookup(stamp.Owner, saved, request.OperationId, new string('0', 64));
            if (prior.Outcome != CharacterCareerMartialAcquisitionOutcome.NotFound) return new(prior.Outcome);
            var effective = MartialSources(current, sources);
            string? slot = ReadContinuationSlotUnderLease(path);
            if (!CharacterCareerMartialAcquisitionTransaction.TryRead(stamp, saved, request, effective, MartialRuntime(current, slot),
                    out var snapshot, out var error)
                || !CharacterCareerMartialAcquisitionTransaction.TryPreview(snapshot!, request, out var preview))
                return new(CharacterCareerMartialAcquisitionOutcome.Unavailable, Error: error);
            if (!CharacterCareerMartialAcquisitionTransaction.TryBuild(stamp, saved, snapshot!, preview!.Command with { ExplicitlyConfirmed = true }, out _, out _)
                || !CharacterCareerMartialAcquisitionTransaction.IsSourceCurrent(stamp, saved, effective, request, snapshot!))
                return new(CharacterCareerMartialAcquisitionOutcome.Conflict, Error: "martial_acquisition_preview_changed");
            current?.ValidateReadObservation();
            if (owners.Capture() != stamp || lease.Stamp != stamp || slot != ReadContinuationSlotUnderLease(path))
                return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
            return new(CharacterCareerMartialAcquisitionOutcome.Available, preview, saved.ContentRevision);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(CharacterCareerMartialAcquisitionOutcome.Unavailable, Error: "martial_acquisition_current_sources_unavailable"); }
    }

    public CharacterCareerMartialAcquisitionResult CommitCareerMartialAcquisition(IOwnerContextLease ownerLease,
        CharacterCareerMartialAcquisitionCommand command, ICharacterSourceDataResolver sources,
        IOwnerContextLeaseAccessor owners, CancellationToken cancellationToken = default)
        => WriteMartialAcquisition(owners, ownerLease, command, sources, false, cancellationToken);
    public CharacterCareerMartialAcquisitionResult FinalizeCareerMartialAcquisitionDispatch(IOwnerContextLease ownerLease,
        CharacterCareerMartialAcquisitionCommand originalCommand, IOwnerContextLeaseAccessor owners, CancellationToken cancellationToken = default)
        => WriteMartialAcquisition(owners, ownerLease, originalCommand, null, true, cancellationToken);
    private CharacterCareerMartialAcquisitionResult WriteMartialAcquisition(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterCareerMartialAcquisitionCommand command, ICharacterSourceDataResolver? sources, bool retire, CancellationToken token)
    {
        var stamp = lease.Stamp; var owner = stamp.Owner;
        if (!CareerMartialAcquisitionGuardsInstalled || owners.Capture() != stamp
            || !(retire ? CharacterCareerMartialAcquisitionTransaction.SameOwner(stamp, command) : CharacterCareerMartialAcquisitionTransaction.OwnerMatches(stamp, command)))
            return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
        var id = command.Request.WorkspaceId; string? path = TryGetPath(owner, id);
        if (path is null) return new(CharacterCareerMartialAcquisitionOutcome.Missing);
        string digest = CharacterCareerMartialAcquisitionTransaction.CommandDigest(command); bool completed = false;
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, id, path, owners, lease, stamp, out var delegated, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return CharacterCareerMartialAcquisitionService.MapReadFailure(read);
            var prior = CharacterCareerMartialAcquisitionTransaction.Lookup(owner, saved, command.Request.OperationId, digest);
            if (prior.Outcome != CharacterCareerMartialAcquisitionOutcome.NotFound) return prior;
            token.ThrowIfCancellationRequested();
            string? slot = ReadContinuationSlotUnderLease(path);
            string beforeDigest = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path));
            var effective = retire ? null : MartialSources(current, sources!);
            CharacterCareerMartialAcquisitionSnapshot? snapshot = null;
            WorkspaceDocument? replacement; CharacterCareerMartialAcquisitionReceipt? receipt;
            if (retire)
            {
                if (!CharacterCareerMartialAcquisitionTransaction.TryBuildNeverApplied(stamp, saved, command, out replacement, out receipt))
                    return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
            }
            else if (sources is null || !CharacterCareerMartialAcquisitionTransaction.TryRead(stamp, saved, command.Request, effective!,
                MartialRuntime(current, slot), out snapshot, out _)
                || !CharacterCareerMartialAcquisitionTransaction.TryBuild(stamp, saved, snapshot!, command, out replacement, out receipt))
                return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
            if (!IsValidAuxiliaryState(id, receipt!.CommittedWorkspaceRevision, replacement!.AuxiliaryState, current?.Recovery))
                return new(CharacterCareerMartialAcquisitionOutcome.Corrupt);
            var record = BuildPersistedRecord(replacement, receipt.CommittedWorkspaceRevision, receipt.CommittedWorkspaceRevision,
                saved.LocalHistory!, delegated, saved.DelegatedGmHistorySegmentStarts);
            var proposed = saved with { Document = replacement, ContentRevision = receipt.CommittedWorkspaceRevision, SavedRevision = receipt.CommittedWorkspaceRevision };
            CharacterCurrentRuleEnvironmentReadmissionResult? readmission = null;
            if (current is not null)
            {
                readmission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture, _retainedCharacterQueries!, proposed, token);
                if (readmission.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution || readmission.Blockers.Count != 0)
                    throw new IOException("martial_acquisition_proposed_environment_unavailable");
                current.Recovery.RetainAdmittedCapture(new(capture, _retainedCharacterQueries!), capture.IntegrityDigest);
                var continuation = new WorkspaceContinuationSnapshot(owner.NormalizedValue,
                    new(id, proposed.Document, proposed.LastUpdatedUtc, proposed.ContentRevision, proposed.SavedRevision), []);
                record = record with
                {
                    RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion,
                    RetainedRuleSources = current.Record.RetainedRuleSources! with
                    {
                        References = current.Recovery.SnapshotReferences(),
                        CurrentEnvironment = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(continuation, capture,
                            execution.ExecutionProfile, execution.ExecutionProfileFingerprint, [])
                    }
                };
            }
            record = BindMartialTerminal(owner, saved, ReadPrivateRecord(path), record, receipt);
            using var admission = new MartialAcquisitionWriteAdmission(this, path, record, current, proposed, readmission);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                token.ThrowIfCancellationRequested();
                if (lease.Stamp != stamp || owners.Capture() != stamp || !CareerMartialAcquisitionGuardsInstalled
                    || slot != ReadContinuationSlotUnderLease(path) || beforeDigest != CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path))
                    || !retire && !CharacterCareerMartialAcquisitionTransaction.IsSourceCurrent(stamp, saved, effective!, command.Request, snapshot!))
                    throw new IOException("martial_acquisition_admission_changed");
                admission.ValidateFinalFence(record);
            }, admittedMartialAcquisitionReceipt: receipt);
            completed = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException) { }
        var observed = ReadCareerMartialAcquisitionWorkspace(owners, lease, id);
        if (!observed.Success || observed.Value is not { } savedAfter) return CharacterCareerMartialAcquisitionService.MapReadFailure(observed);
        var result = CharacterCareerMartialAcquisitionTransaction.Lookup(owner, savedAfter, command.Request.OperationId, digest);
        if (result.Outcome == CharacterCareerMartialAcquisitionOutcome.Replayed && completed && !retire)
            return result with { Outcome = CharacterCareerMartialAcquisitionOutcome.Applied };
        return result.Outcome == CharacterCareerMartialAcquisitionOutcome.NotFound
            ? new(CharacterCareerMartialAcquisitionOutcome.UnknownOutcome, CurrentWorkspaceRevision: savedAfter.ContentRevision) : result;
    }
    [ThreadStatic] private static MartialAcquisitionWriteAdmission? _martialAcquisitionAdmission;
    private sealed class MartialAcquisitionWriteAdmission : IDisposable
    {
        private readonly MartialAcquisitionWriteAdmission? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal GearCurrentEnvironment? Current { get; }
        private readonly string _recordDigest;
        private readonly WorkspaceStoredDocument _after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? _readmission;
        internal MartialAcquisitionWriteAdmission(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            GearCurrentEnvironment? current, WorkspaceStoredDocument after, CharacterCurrentRuleEnvironmentReadmissionResult? readmission)
        {
            Store = store; Path = System.IO.Path.GetFullPath(path); Current = current; _after = after; _readmission = readmission;
            _recordDigest = CharacterCreationFinalizationDigest.Compute(record);
            _previous = _martialAcquisitionAdmission; _martialAcquisitionAdmission = this;
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            if (CharacterCreationFinalizationDigest.Compute(record) != _recordDigest)
                throw new IOException("martial_acquisition_exact_record_changed");
            if (Current is null) return;
            Current.ValidateFinalFence();
            if (_readmission?.AfterCapture is not { } capture || _readmission.AfterExecution is not { } execution
                || !execution.Revalidate(_after, capture, Store._retainedCharacterQueries!))
                throw new IOException("martial_acquisition_proposed_environment_changed");
        }
        public void Dispose() => _martialAcquisitionAdmission = _previous;
    }
    private MartialAcquisitionWriteAdmission? MartialAcquisitionAdmissionFor(string path)
        => _martialAcquisitionAdmission is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, System.IO.Path.GetFullPath(path)) ? admission : null;
    private void ValidateMartialAcquisitionWrite(string path, PersistedWorkspaceRecord record,
        WorkspaceWriteDisposition disposition, CharacterCareerMartialAcquisitionReceipt? admitted)
    {
        var previous = disposition == WorkspaceWriteDisposition.ReplaceExisting ? ReadPrivateRecord(path) : null;
        var before = previous?.AuxiliaryState?.CharacterCareerMartialAcquisitionReceipts;
        var after = record.AuxiliaryState?.CharacterCareerMartialAcquisitionReceipts;
        if (admitted is null)
        {
            // New-file continuation restoration has separately validated imported history.
            // Ordinary saves cannot append, remove, replace or forge this ledger.
            if (previous is not null && JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after))
                throw new IOException("Martial acquisition history requires its typed transaction.");
            if (previous is null && after is { Count: > 0 }
                && (record.LocalHistory is not { } history || after.Any(r => !history.IsImportedRevision(r.CommittedWorkspaceRevision))))
                throw new IOException("New workspaces cannot manufacture locally replayable martial history.");
            return;
        }
        if (previous is null || after is null || after.Count != (before?.Count ?? 0) + 1
            || JsonSerializer.Serialize(after.Take(after.Count - 1)) != JsonSerializer.Serialize(before ?? [])
            || JsonSerializer.Serialize(after[^1]) != JsonSerializer.Serialize(admitted)
            || !CharacterCareerMartialAcquisitionTransaction.IsCoherent(admitted)
            || record.ContentRevision != previous.ContentRevision + 1 || record.SavedRevision != record.ContentRevision
            || record.ContentRevision != admitted.CommittedWorkspaceRevision)
            throw new IOException("Martial acquisition must append exactly one admitted receipt.");
        if (admitted.NeverApplied && (record.Format != previous.Format || JsonSerializer.Serialize(record.Envelope) != JsonSerializer.Serialize(previous.Envelope)
            || JsonSerializer.Serialize(record.AuxiliaryState! with { CharacterCareerMartialAcquisitionReceipts = before })
                != JsonSerializer.Serialize(previous.AuxiliaryState ?? WorkspaceDocumentAuxiliaryState.Empty)))
            throw new IOException("Dispatch retirement must preserve the complete character state.");
    }
}
