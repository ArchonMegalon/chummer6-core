using Chummer.Application.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerSpellLearningAtomicCommitCapability, ICharacterCareerSpellLearningCurrentEnvironmentCapability
{
    // Root implements this partial method only with BOTH shared ledger guards.
    // Root must also add the auxiliary property and its serializer/IsEmpty coverage.
    static partial void VerifyCareerSpellLearningGuards(ref bool installed);
    public bool CareerSpellLearningGuardsInstalled
    {
        get { bool installed = false; VerifyCareerSpellLearningGuards(ref installed); return installed; }
    }
    public string CareerSpellLearningRuntimeDigest => CharacterCareerSpellLearningTransaction.Hash(
        "chummer.core.sr5-career-spell-learning-runtime/v1\0"
        + typeof(CharacterCareerSpellLearningQuote).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(CharacterCareerSpellLearningTransaction).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(FileWorkspaceStore).Module.ModuleVersionId.ToString("D"));

    public CharacterCareerSpellLearningResult CommitCareerSpellLearning(OwnerScope owner,
        CharacterCareerSpellLearningCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default)
        => CommitCareerSpellLearning(owner, command, sourceResolver, null, null, default, cancellationToken);

    public CharacterCareerSpellLearningResult CommitCareerSpellLearning(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerSpellLearningCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default)
        => CommitCareerSpellLearning(lease.Stamp.Owner, command, sourceResolver, owners, lease, lease.Stamp, cancellationToken);

    private CharacterCareerSpellLearningResult CommitCareerSpellLearning(OwnerScope owner,
        CharacterCareerSpellLearningCommand command, ICharacterSourceDataResolver sourceResolver,
        IOwnerContextLeaseAccessor? owners, IOwnerContextLease? lease, OwnerContextStamp stamp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceResolver);
        if (owners is not null && !LearningOwnerCurrent5703(owners, lease, stamp))
            return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_owner_changed");
        if (!CareerSpellLearningGuardsInstalled)
            return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_ledger_guards_not_installed");
        if (!CharacterCareerSpellLearningTransaction.IsValidOwner(owner)
            || !CharacterCareerSpellLearningTransaction.IsConfirmedCommand(command)
            || command.Binding.OwnerId != owner.NormalizedValue)
            return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_command_invalid");
        var id = command.Request.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null) return new(CharacterCareerSpellLearningOutcome.Missing);
        string digest = CharacterCareerSpellLearningTransaction.CommandDigest(command);
        bool completed = false, canceled = false;
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, id, path, owners, lease, stamp,
                out var delegatedLedger, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return CharacterCareerSpellLearningService.MapReadFailure(read);
            var prior = CharacterCareerSpellLearningTransaction.Lookup(owner, saved, command.Request.OperationId, digest);
            if (prior.Outcome != CharacterCareerSpellLearningOutcome.NotFound) return prior;
            if (cancellationToken.IsCancellationRequested)
                return new(CharacterCareerSpellLearningOutcome.Canceled, Error: "spell_learning_canceled_before_commit");
            if (saved.ContentRevision != command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision)
                return new(CharacterCareerSpellLearningOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "spell_learning_revision_conflict");
            var effective = LearningSources5703(current, sourceResolver);
            string? slot = ReadContinuationSlotUnderLease(path);
            string beforeDigest = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path));
            var imported = LearningContext5703(current, saved);
            if (!CharacterCareerSpellLearningTransaction.TryReadCurrent5703(owner, saved, command.Request, effective,
                LearningRuntime5703(CareerSpellLearningRuntimeDigest, current, slot, stamp), imported, out var snapshot, out string error))
                return new(CharacterCareerSpellLearningOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            if (!CharacterCareerSpellLearningTransaction.TryBuild(owner, saved, snapshot!, command, out var replacement, out var receipt))
                return new(CharacterCareerSpellLearningOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "spell_learning_binding_conflict");
            if (!IsValidAuxiliaryState(id, receipt!.CommittedWorkspaceRevision, replacement!.AuxiliaryState, current?.Recovery))
                return new(CharacterCareerSpellLearningOutcome.Corrupt, Error: "spell_learning_auxiliary_invalid");
            var record = BuildPersistedRecord(replacement, receipt.CommittedWorkspaceRevision, receipt.CommittedWorkspaceRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            var proposed = saved with { Document = replacement, ContentRevision = receipt.CommittedWorkspaceRevision,
                SavedRevision = receipt.CommittedWorkspaceRevision };
            CharacterCurrentRuleEnvironmentReadmissionResult? readmission = null;
            if (current is not null)
            {
                readmission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                    _retainedCharacterQueries!, proposed, cancellationToken);
                if (readmission.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution
                    || readmission.Blockers.Count != 0)
                    throw new IOException("spell_learning_proposed_environment_unavailable");
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
            cancellationToken.ThrowIfCancellationRequested();
            using var admission = new SpellLearningWriteAdmission5703(this, path, record, current, proposed, readmission);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CareerSpellLearningGuardsInstalled || owners is not null && !LearningOwnerCurrent5703(owners, lease, stamp)
                    || slot != ReadContinuationSlotUnderLease(path)
                    || beforeDigest != CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path))
                    || !CharacterCareerSpellLearningTransaction.IsSourceCurrent(saved, effective, command.Request, snapshot!))
                    throw new IOException("spell_learning_source_changed_before_replace");
                admission.ValidateFinalFence(record);
            });
            completed = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { canceled = ex is OperationCanceledException; }
        // Release the lease before a normal read reacquires it; recovery ignores cancellation.
        var observed = owners is not null && lease is not null
            ? ReadCareerSpellLearningWorkspace(owners, lease, id)
            : owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (!observed.Success || observed.Value is not { } observedWorkspace) return CharacterCareerSpellLearningService.MapReadFailure(observed);
        var recovered = CharacterCareerSpellLearningTransaction.Lookup(owner, observedWorkspace, command.Request.OperationId, digest);
        if (recovered.Outcome == CharacterCareerSpellLearningOutcome.Replayed)
            return recovered with { Outcome = completed ? CharacterCareerSpellLearningOutcome.Applied : CharacterCareerSpellLearningOutcome.Replayed };
        return recovered.Outcome == CharacterCareerSpellLearningOutcome.NotFound
            ? new(canceled ? CharacterCareerSpellLearningOutcome.Canceled : CharacterCareerSpellLearningOutcome.UnknownOutcome,
                CurrentWorkspaceRevision: observedWorkspace.ContentRevision, Error: "spell_learning_commit_not_observed") : recovered;
    }
    [ThreadStatic] private static SpellLearningWriteAdmission5703? _spellLearningAdmission5703;
    private sealed class SpellLearningWriteAdmission5703 : IDisposable
    {
        private readonly SpellLearningWriteAdmission5703? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal GearCurrentEnvironment? Current { get; }
        private readonly string _recordDigest;
        private readonly WorkspaceStoredDocument _after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? _readmission;
        internal SpellLearningWriteAdmission5703(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            GearCurrentEnvironment? current, WorkspaceStoredDocument after, CharacterCurrentRuleEnvironmentReadmissionResult? readmission)
        {
            Store = store; Path = System.IO.Path.GetFullPath(path); Current = current; _after = after; _readmission = readmission;
            _recordDigest = CharacterCreationFinalizationDigest.Compute(record);
            _previous = _spellLearningAdmission5703; _spellLearningAdmission5703 = this;
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            if (CharacterCreationFinalizationDigest.Compute(record) != _recordDigest)
                throw new IOException("spell_learning_exact_record_changed");
            if (Current is null) return;
            Current.ValidateFinalFence();
            if (_readmission?.AfterCapture is not { } capture || _readmission.AfterExecution is not { } execution
                || !execution.Revalidate(_after, capture, Store._retainedCharacterQueries!))
                throw new IOException("spell_learning_proposed_environment_changed");
        }
        public void Dispose() => _spellLearningAdmission5703 = _previous;
    }
    private SpellLearningWriteAdmission5703? SpellLearningAdmissionFor5703(string path)
        => _spellLearningAdmission5703 is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, System.IO.Path.GetFullPath(path)) ? admission : null;

}
