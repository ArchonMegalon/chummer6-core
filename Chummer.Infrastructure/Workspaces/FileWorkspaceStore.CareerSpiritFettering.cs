using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerSpiritFetteringAtomicCommitCapability
{
    static partial void VerifyCareerSpiritFetteringGuards(ref bool installed);
    public bool CareerSpiritFetteringGuardsInstalled { get { bool value = false; VerifyCareerSpiritFetteringGuards(ref value); return value; } }
    public string CareerSpiritFetteringRuntimeDigest => CharacterCareerSpiritFetteringTransaction.Hash(
        "chummer.core.sr5-career-spirit-fettering-runtime/v1\0" + typeof(CharacterCareerSpiritFetteringQuote).Module.ModuleVersionId
        + "\0" + typeof(CharacterCareerSpiritFetteringTransaction).Module.ModuleVersionId + "\0" + typeof(FileWorkspaceStore).Module.ModuleVersionId);
    public CharacterCareerSpiritFetteringResult CommitCareerSpiritFettering(IOwnerContextLease ownerLease,
        CharacterCareerSpiritFetteringCommand command, ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default)
        => WriteSpiritFettering(null, ownerLease, command, sources, false, cancellationToken);
    public CharacterCareerSpiritFetteringResult FinalizeCareerSpiritFetteringDispatch(IOwnerContextLease ownerLease,
        CharacterCareerSpiritFetteringCommand originalCommand, CancellationToken cancellationToken = default)
        => WriteSpiritFettering(null, ownerLease, originalCommand, null, true, cancellationToken);
    public CharacterCareerSpiritFetteringResult CommitCareerSpiritFettering(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerSpiritFetteringCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default)
        => WriteSpiritFettering(owners, lease, command, sources, false, cancellationToken);
    public CharacterCareerSpiritFetteringResult FinalizeCareerSpiritFetteringDispatch(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerSpiritFetteringCommand originalCommand, CancellationToken cancellationToken = default)
        => WriteSpiritFettering(owners, lease, originalCommand, null, true, cancellationToken);
    private CharacterCareerSpiritFetteringResult WriteSpiritFettering(IOwnerContextLeaseAccessor? owners, IOwnerContextLease lease,
        CharacterCareerSpiritFetteringCommand command, ICharacterSourceDataResolver? sources, bool retire, CancellationToken token)
    {
        var stamp = lease.Stamp; var owner = stamp.Owner;
        if (owners is not null && !LearningOwnerCurrent5703(owners, lease, stamp))
            return new(CharacterCareerSpiritFetteringOutcome.Conflict);
        if (!CareerSpiritFetteringGuardsInstalled || !(retire ? CharacterCareerSpiritFetteringTransaction.SameOwner(stamp, command) : CharacterCareerSpiritFetteringTransaction.OwnerMatches(stamp, command)))
            return new(CharacterCareerSpiritFetteringOutcome.Conflict);
        var id = command.Request.WorkspaceId; string? path = TryGetPath(owner, id);
        if (path is null) return new(CharacterCareerSpiritFetteringOutcome.Missing);
        string digest = CharacterCareerSpiritFetteringTransaction.CommandDigest(command); bool completed = false;
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, id, path, owners, lease, stamp, out var delegated, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return CharacterCareerSpiritFetteringService.MapReadFailure(read);
            var prior = CharacterCareerSpiritFetteringTransaction.Lookup(owner, saved, command.Request.OperationId, digest);
            if (prior.Outcome != CharacterCareerSpiritFetteringOutcome.NotFound) return prior;
            token.ThrowIfCancellationRequested();
            string? slot = ReadContinuationSlotUnderLease(path);
            string beforeDigest = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path));
            var effective = sources is null ? null : LearningSources5703(current, sources);
            var imported = LearningContext5703(current, saved);
            CharacterCareerSpiritFetteringSnapshot? snapshot = null;
            WorkspaceDocument? replacement; CharacterCareerSpiritFetteringReceipt? receipt;
            if (retire)
            {
                if (!CharacterCareerSpiritFetteringTransaction.TryBuildNeverApplied(stamp, saved, command, out replacement, out receipt))
                    return new(CharacterCareerSpiritFetteringOutcome.Conflict);
            }
            else if (effective is null || !CharacterCareerSpiritFetteringTransaction.TryReadCurrent5724(stamp, saved, command.Request, effective,
                LearningRuntime5703(CareerSpiritFetteringRuntimeDigest, current, slot, stamp), imported, out snapshot, out _)
                || !CharacterCareerSpiritFetteringTransaction.TryBuild(stamp, saved, snapshot!, command, out replacement, out receipt))
                return new(CharacterCareerSpiritFetteringOutcome.Conflict);
            if (!IsValidAuxiliaryState(id, receipt!.CommittedWorkspaceRevision, replacement!.AuxiliaryState, current?.Recovery))
                return new(CharacterCareerSpiritFetteringOutcome.Corrupt);
            var record = BuildPersistedRecord(replacement, receipt.CommittedWorkspaceRevision, receipt.CommittedWorkspaceRevision,
                saved.LocalHistory!, delegated, saved.DelegatedGmHistorySegmentStarts);
            var proposed = saved with { Document = replacement, ContentRevision = receipt.CommittedWorkspaceRevision,
                SavedRevision = receipt.CommittedWorkspaceRevision };
            CharacterCurrentRuleEnvironmentReadmissionResult? readmission = null;
            if (current is not null)
            {
                readmission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                    _retainedCharacterQueries!, proposed, token);
                if (readmission.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution
                    || readmission.Blockers.Count != 0) throw new IOException("spirit_fettering_proposed_environment_unavailable");
                current.Recovery.RetainAdmittedCapture(new(capture, _retainedCharacterQueries!), capture.IntegrityDigest);
                var continuation = new WorkspaceContinuationSnapshot(owner.NormalizedValue,
                    new(id, proposed.Document, proposed.LastUpdatedUtc, proposed.ContentRevision, proposed.SavedRevision), []);
                record = record with
                {
                    RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion,
                    RetainedRuleSources = current.Record.RetainedRuleSources! with
                    {
                        References = current.Recovery.SnapshotReferences(),
                        CurrentEnvironment = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(continuation,
                            capture, execution.ExecutionProfile, execution.ExecutionProfileFingerprint, [])
                    }
                };
            }
            using var admission = new SpiritFetteringWriteAdmission5724(this, path, record, current, proposed, readmission);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                token.ThrowIfCancellationRequested();
                if (lease.Stamp != stamp || !CareerSpiritFetteringGuardsInstalled
                    || owners is not null && !LearningOwnerCurrent5703(owners, lease, stamp)
                    || slot != ReadContinuationSlotUnderLease(path)
                    || beforeDigest != CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path))
                    || !retire && !CharacterCareerSpiritFetteringTransaction.IsSourceCurrent(stamp, saved, effective!, command.Request, snapshot!))
                    throw new IOException("spirit_fettering_admission_changed");
                admission.ValidateFinalFence(record);
            }, admittedSpiritFetteringReceipt: receipt);
            completed = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException) { }
        var observed = owners is not null ? ReadCareerSpiritFetteringWorkspace(owners, lease, id)
            : owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (!observed.Success || observed.Value is not { } observedWorkspace) return CharacterCareerSpiritFetteringService.MapReadFailure(observed);
        var result = CharacterCareerSpiritFetteringTransaction.Lookup(owner, observedWorkspace, command.Request.OperationId, digest);
        if (result.Outcome == CharacterCareerSpiritFetteringOutcome.Replayed && completed && !retire)
            return result with { Outcome = CharacterCareerSpiritFetteringOutcome.Applied };
        return result.Outcome == CharacterCareerSpiritFetteringOutcome.NotFound
            ? new(CharacterCareerSpiritFetteringOutcome.UnknownOutcome, CurrentWorkspaceRevision: observedWorkspace.ContentRevision) : result;
    }
    private void ValidateSpiritFetteringWrite(string path, PersistedWorkspaceRecord record,
        WorkspaceWriteDisposition disposition, CharacterCareerSpiritFetteringReceipt? admitted)
    {
        var previous = disposition == WorkspaceWriteDisposition.ReplaceExisting ? ReadPrivateRecord(path) : null;
        var before = previous?.AuxiliaryState?.CharacterCareerSpiritFetteringReceipts;
        var after = record.AuxiliaryState?.CharacterCareerSpiritFetteringReceipts;
        if (admitted is null)
        {
            // New-file continuation restoration has separately validated imported history.
            // Ordinary saves cannot append, remove, replace or forge this ledger.
            if (previous is not null && JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after))
                throw new IOException("Spirit fettering history requires its typed transaction.");
            return;
        }
        if (previous is null || after is null || after.Count != (before?.Count ?? 0) + 1
            || JsonSerializer.Serialize(after.Take(after.Count - 1)) != JsonSerializer.Serialize(before ?? [])
            || JsonSerializer.Serialize(after[^1]) != JsonSerializer.Serialize(admitted)
            || !CharacterCareerSpiritFetteringTransaction.IsCoherent(admitted)
            || record.ContentRevision != previous.ContentRevision + 1 || record.SavedRevision != record.ContentRevision
            || record.ContentRevision != admitted.CommittedWorkspaceRevision)
            throw new IOException("Spirit fettering must append exactly one admitted receipt.");
        if (admitted.NeverApplied && (record.Format != previous.Format || JsonSerializer.Serialize(record.Envelope) != JsonSerializer.Serialize(previous.Envelope)
            || JsonSerializer.Serialize(record.AuxiliaryState! with { CharacterCareerSpiritFetteringReceipts = before })
                != JsonSerializer.Serialize(previous.AuxiliaryState ?? WorkspaceDocumentAuxiliaryState.Empty)))
            throw new IOException("Dispatch retirement must preserve the complete character state.");
    }    [ThreadStatic] private static SpiritFetteringWriteAdmission5724? _spiritFetteringAdmission5724;
    private sealed class SpiritFetteringWriteAdmission5724 : IDisposable
    {
        private readonly SpiritFetteringWriteAdmission5724? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal GearCurrentEnvironment? Current { get; }
        private readonly string _recordDigest;
        private readonly WorkspaceStoredDocument _after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? _readmission;
        internal SpiritFetteringWriteAdmission5724(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            GearCurrentEnvironment? current, WorkspaceStoredDocument after, CharacterCurrentRuleEnvironmentReadmissionResult? readmission)
        {
            Store = store; Path = System.IO.Path.GetFullPath(path); Current = current; _after = after; _readmission = readmission;
            _recordDigest = CharacterCreationFinalizationDigest.Compute(record);
            _previous = _spiritFetteringAdmission5724; _spiritFetteringAdmission5724 = this;
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            if (CharacterCreationFinalizationDigest.Compute(record) != _recordDigest)
                throw new IOException("spirit_fettering_exact_record_changed");
            if (Current is null) return;
            Current.ValidateFinalFence();
            if (_readmission?.AfterCapture is not { } capture || _readmission.AfterExecution is not { } execution
                || !execution.Revalidate(_after, capture, Store._retainedCharacterQueries!))
                throw new IOException("spirit_fettering_proposed_environment_changed");
        }
        public void Dispose() => _spiritFetteringAdmission5724 = _previous;
    }
    private SpiritFetteringWriteAdmission5724? SpiritFetteringAdmissionFor5724(string path)
        => _spiritFetteringAdmission5724 is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, System.IO.Path.GetFullPath(path)) ? admission : null;

}
