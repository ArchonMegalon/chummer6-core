using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerFocusBondingAtomicCommitCapability
{
    static partial void VerifyCareerFocusBondingGuards(ref bool installed);
    public bool CareerFocusBondingGuardsInstalled { get { bool value = false; VerifyCareerFocusBondingGuards(ref value); return value; } }
    public string CareerFocusBondingRuntimeDigest => CharacterCareerFocusBondingTransaction.Hash(
        "chummer.core.sr5-career-spirit-fettering-runtime/v1\0" + typeof(CharacterCareerFocusBondingQuote).Module.ModuleVersionId
        + "\0" + typeof(CharacterCareerFocusBondingTransaction).Module.ModuleVersionId + "\0" + typeof(FileWorkspaceStore).Module.ModuleVersionId);
    public CharacterCareerFocusBondingResult CommitCareerFocusBonding(IOwnerContextLease ownerLease,
        CharacterCareerFocusBondingCommand command, ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default)
        => WriteFocusBonding(null, ownerLease, command, sources, false, cancellationToken);
    public CharacterCareerFocusBondingResult FinalizeCareerFocusBondingDispatch(IOwnerContextLease ownerLease,
        CharacterCareerFocusBondingCommand originalCommand, CancellationToken cancellationToken = default)
        => WriteFocusBonding(null, ownerLease, originalCommand, null, true, cancellationToken);
    public CharacterCareerFocusBondingResult CommitCareerFocusBonding(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerFocusBondingCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default)
        => WriteFocusBonding(owners, lease, command, sources, false, cancellationToken);
    public CharacterCareerFocusBondingResult FinalizeCareerFocusBondingDispatch(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerFocusBondingCommand originalCommand, CancellationToken cancellationToken = default)
        => WriteFocusBonding(owners, lease, originalCommand, null, true, cancellationToken);
    private CharacterCareerFocusBondingResult WriteFocusBonding(IOwnerContextLeaseAccessor? owners, IOwnerContextLease lease,
        CharacterCareerFocusBondingCommand command, ICharacterSourceDataResolver? sources, bool retire, CancellationToken token,
        Chummer.Application.Session.SessionFocusCanonicalAdmission7490? playAdmission = null)
    {
        var stamp = lease.Stamp; var owner = stamp.Owner;
        if (owners is not null && !LearningOwnerCurrent5703(owners, lease, stamp))
            return new(CharacterCareerFocusBondingOutcome.Conflict);
        if (!CareerFocusBondingGuardsInstalled || !CharacterCareerFocusBondingTransaction.SameOwner(stamp, command))
            return new(CharacterCareerFocusBondingOutcome.Conflict);
        var id = command.Request.WorkspaceId; string? path = TryGetPath(owner, id);
        if (path is null) return new(CharacterCareerFocusBondingOutcome.Missing);
        string digest = CharacterCareerFocusBondingTransaction.CommandDigest(command); bool completed = false;
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            if (owners is not null)
            {
                var historical = LookupFocusBondingUnderLease6495(path, owner, id, command.Request.OperationId, digest);
                if (!LearningOwnerCurrent5703(owners, lease, stamp)) return new(CharacterCareerFocusBondingOutcome.Conflict);
                if (historical.Outcome != CharacterCareerFocusBondingOutcome.NotFound) return historical;
            }
            if (!retire && !CharacterCareerFocusBondingTransaction.OwnerMatches(stamp, command))
                return new(CharacterCareerFocusBondingOutcome.Conflict);
            var read = ReadGearWorkspaceUnderLease(owner, id, path, owners, lease, stamp, out var delegated, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return CharacterCareerFocusBondingService.MapReadFailure(read);
            var prior = CharacterCareerFocusBondingTransaction.Lookup(owner, saved, command.Request.OperationId, digest);
            if (prior.Outcome != CharacterCareerFocusBondingOutcome.NotFound) return prior;
            token.ThrowIfCancellationRequested();
            string? slot = ReadContinuationSlotUnderLease(path);
            string beforeDigest = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path));
            var effective = sources is null ? null : LearningSources5703(current, sources);
            CharacterCareerFocusBondingSnapshot? snapshot = null;
            WorkspaceDocument? replacement; CharacterCareerFocusBondingReceipt? receipt;
            if (retire)
            {
                if (!CharacterCareerFocusBondingTransaction.TryBuildNeverApplied(stamp, saved, command, out replacement, out receipt))
                    return new(CharacterCareerFocusBondingOutcome.Conflict);
            }
            else if (effective is null || !CharacterCareerFocusBondingTransaction.TryRead(stamp, saved, command.Request, effective,
                LearningRuntime5703(CareerFocusBondingRuntimeDigest, current, slot, stamp), out snapshot, out _)
                || !CharacterCareerFocusBondingTransaction.TryBuild(stamp, saved, snapshot!, command, out replacement, out receipt))
                return new(CharacterCareerFocusBondingOutcome.Conflict);
            if (!IsValidAuxiliaryState(id, receipt!.CommittedWorkspaceRevision, replacement!.AuxiliaryState, current?.Recovery))
                return new(CharacterCareerFocusBondingOutcome.Corrupt);
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
                    || readmission.Blockers.Count != 0) throw new IOException("focus_bonding_proposed_environment_unavailable");
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
            using var admission = new FocusBondingWriteAdmission6495(this, path, record, current, proposed, readmission);
            if (playAdmission is not null)
            {
                admission.ValidateFinalFence(record);
                PublishFocusContinuation7490(playAdmission, stamp, saved, proposed, slot, receipt,
                    effective!, current, readmission);
            }
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                token.ThrowIfCancellationRequested();
                if (lease.Stamp != stamp || !CareerFocusBondingGuardsInstalled
                    || owners is not null && !LearningOwnerCurrent5703(owners, lease, stamp)
                    || slot != ReadContinuationSlotUnderLease(path)
                    || beforeDigest != CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path))
                    || !retire && !CharacterCareerFocusBondingTransaction.IsSourceCurrent(stamp, saved, effective!, command.Request, snapshot!))
                    throw new IOException("focus_bonding_admission_changed");
                admission.ValidateFinalFence(record);
                // Canonical source queries run inside the final admission. Fence the
                // exact owner, record, continuation and custody again after those calls.
                if (lease.Stamp != stamp || owners is not null && !LearningOwnerCurrent5703(owners, lease, stamp)
                    || slot != ReadContinuationSlotUnderLease(path)
                    || beforeDigest != CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path))
                    || current is not null && !current.Recovery.ValidateRetainedRuleSources())
                    throw new IOException("focus_bonding_final_observation_changed");
                playAdmission?.ValidateFinalObservation();
            }, admittedFocusBondingReceipt: receipt);
            completed = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException) { }
        CharacterCareerFocusBondingResult result;
        if (owners is not null)
            result = LookupCareerFocusBonding(owners, lease, id, command.Request.OperationId, digest);
        else
        {
            var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
            if (!observed.Success || observed.Value is not { } observedWorkspace) return CharacterCareerFocusBondingService.MapReadFailure(observed);
            result = CharacterCareerFocusBondingTransaction.Lookup(owner, observedWorkspace, command.Request.OperationId, digest);
        }
        if (result.Outcome == CharacterCareerFocusBondingOutcome.Replayed && completed && !retire)
            return result with { Outcome = CharacterCareerFocusBondingOutcome.Applied };
        return result.Outcome == CharacterCareerFocusBondingOutcome.NotFound
            ? new(CharacterCareerFocusBondingOutcome.UnknownOutcome, CurrentWorkspaceRevision: result.CurrentWorkspaceRevision) : result;
    }
    private void ValidateFocusBondingWrite(string path, PersistedWorkspaceRecord record,
        WorkspaceWriteDisposition disposition, CharacterCareerFocusBondingReceipt? admitted)
    {
        var previous = disposition == WorkspaceWriteDisposition.ReplaceExisting ? ReadPrivateRecord(path) : null;
        var before = previous?.AuxiliaryState?.CharacterCareerFocusBondingReceipts;
        var after = record.AuxiliaryState?.CharacterCareerFocusBondingReceipts;
        if (admitted is null)
        {
            // New-file continuation restoration has separately validated imported history.
            // Ordinary saves cannot append, remove, replace or forge this ledger.
            if (previous is not null && JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after))
                throw new IOException("Focus bonding history requires its typed transaction.");
            return;
        }
        if (previous is null || after is null || after.Count != (before?.Count ?? 0) + 1
            || JsonSerializer.Serialize(after.Take(after.Count - 1)) != JsonSerializer.Serialize(before ?? [])
            || JsonSerializer.Serialize(after[^1]) != JsonSerializer.Serialize(admitted)
            || !CharacterCareerFocusBondingTransaction.IsCoherent(admitted)
            || record.ContentRevision != previous.ContentRevision + 1 || record.SavedRevision != record.ContentRevision
            || record.ContentRevision != admitted.CommittedWorkspaceRevision)
            throw new IOException("Focus bonding must append exactly one admitted receipt.");
        if (admitted.NeverApplied && (record.Format != previous.Format || JsonSerializer.Serialize(record.Envelope) != JsonSerializer.Serialize(previous.Envelope)
            || JsonSerializer.Serialize(record.AuxiliaryState! with { CharacterCareerFocusBondingReceipts = before })
                != JsonSerializer.Serialize(previous.AuxiliaryState ?? WorkspaceDocumentAuxiliaryState.Empty)))
            throw new IOException("Dispatch retirement must preserve the complete character state.");
    }

    [ThreadStatic] private static FocusBondingWriteAdmission6495? _spiritFetteringAdmission6495;
    private sealed class FocusBondingWriteAdmission6495 : IDisposable
    {
        private readonly FocusBondingWriteAdmission6495? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal GearCurrentEnvironment? Current { get; }
        private readonly string _recordDigest;
        private readonly WorkspaceStoredDocument _after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? _readmission;
        internal FocusBondingWriteAdmission6495(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            GearCurrentEnvironment? current, WorkspaceStoredDocument after, CharacterCurrentRuleEnvironmentReadmissionResult? readmission)
        {
            Store = store; Path = System.IO.Path.GetFullPath(path); Current = current; _after = after; _readmission = readmission;
            _recordDigest = CharacterCreationFinalizationDigest.Compute(record);
            _previous = _spiritFetteringAdmission6495; _spiritFetteringAdmission6495 = this;
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            if (CharacterCreationFinalizationDigest.Compute(record) != _recordDigest)
                throw new IOException("focus_bonding_exact_record_changed");
            if (Current is null) return;
            Current.ValidateFinalFence();
            if (_readmission?.AfterCapture is not { } capture || _readmission.AfterExecution is not { } execution
                || !execution.Revalidate(_after, capture, Store._retainedCharacterQueries!))
                throw new IOException("focus_bonding_proposed_environment_changed");
        }
        public void Dispose() => _spiritFetteringAdmission6495 = _previous;
    }
    private FocusBondingWriteAdmission6495? FocusBondingAdmissionFor6495(string path)
        => _spiritFetteringAdmission6495 is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, System.IO.Path.GetFullPath(path)) ? admission : null;

}
