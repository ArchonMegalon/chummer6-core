using Chummer.Application.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerComplexFormLearningAtomicCommitCapability, ICharacterCareerComplexFormLearningCurrentEnvironmentCapability
{
    // Root implements this partial method only with BOTH shared ledger guards.
    // Root must also add the auxiliary property and its serializer/IsEmpty coverage.
    static partial void VerifyCareerComplexFormLearningGuards(ref bool installed);
    public bool CareerComplexFormLearningGuardsInstalled
    {
        get { bool installed = false; VerifyCareerComplexFormLearningGuards(ref installed); return installed; }
    }
    public string CareerComplexFormLearningRuntimeDigest => CharacterCareerComplexFormLearningTransaction.Hash(
        "chummer.core.sr5-career-complex-form-learning-runtime/v1\0"
        + typeof(CharacterCareerComplexFormLearningQuote).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(CharacterCareerComplexFormLearningTransaction).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(FileWorkspaceStore).Module.ModuleVersionId.ToString("D"));

    public CharacterCareerComplexFormLearningResult CommitCareerComplexFormLearning(OwnerScope owner,
        CharacterCareerComplexFormLearningCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default)
        => CommitCareerComplexFormLearning(owner, command, sourceResolver, null, null, default, cancellationToken);

    public CharacterCareerComplexFormLearningResult CommitCareerComplexFormLearning(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerComplexFormLearningCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default)
        => CommitCareerComplexFormLearning(lease.Stamp.Owner, command, sourceResolver, owners, lease, lease.Stamp, cancellationToken);

    private CharacterCareerComplexFormLearningResult CommitCareerComplexFormLearning(OwnerScope owner,
        CharacterCareerComplexFormLearningCommand command, ICharacterSourceDataResolver sourceResolver,
        IOwnerContextLeaseAccessor? owners, IOwnerContextLease? lease, OwnerContextStamp stamp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceResolver);
        if (owners is not null && !LearningOwnerCurrent5703(owners, lease, stamp))
            return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_owner_changed");
        if (!CareerComplexFormLearningGuardsInstalled)
            return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_ledger_guards_not_installed");
        if (!CharacterCareerComplexFormLearningTransaction.IsValidOwner(owner)
            || !CharacterCareerComplexFormLearningTransaction.IsConfirmedCommand(command)
            || command.Binding.OwnerId != owner.NormalizedValue)
            return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_command_invalid");
        var id = command.Request.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null) return new(CharacterCareerComplexFormLearningOutcome.Missing);
        string digest = CharacterCareerComplexFormLearningTransaction.CommandDigest(command);
        bool completed = false, canceled = false;
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, id, path, owners, lease, stamp,
                out var delegatedLedger, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return CharacterCareerComplexFormLearningService.MapReadFailure(read);
            var prior = CharacterCareerComplexFormLearningTransaction.Lookup(owner, saved, command.Request.OperationId, digest);
            if (prior.Outcome != CharacterCareerComplexFormLearningOutcome.NotFound) return prior;
            if (cancellationToken.IsCancellationRequested)
                return new(CharacterCareerComplexFormLearningOutcome.Canceled, Error: "complex_form_learning_canceled_before_commit");
            if (saved.ContentRevision != command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision)
                return new(CharacterCareerComplexFormLearningOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "complex_form_learning_revision_conflict");
            var effective = LearningSources5703(current, sourceResolver);
            string? slot = ReadContinuationSlotUnderLease(path);
            string beforeDigest = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path));
            var imported = LearningContext5703(current, saved);
            if (!CharacterCareerComplexFormLearningTransaction.TryReadCurrent5703(owner, saved, command.Request, effective,
                LearningRuntime5703(CareerComplexFormLearningRuntimeDigest, current, slot, stamp), imported, out var snapshot, out string error))
                return new(CharacterCareerComplexFormLearningOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            if (!CharacterCareerComplexFormLearningTransaction.TryBuild(owner, saved, snapshot!, command, out var replacement, out var receipt))
                return new(CharacterCareerComplexFormLearningOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "complex_form_learning_binding_conflict");
            if (!IsValidAuxiliaryState(id, receipt!.CommittedWorkspaceRevision, replacement!.AuxiliaryState, current?.Recovery))
                return new(CharacterCareerComplexFormLearningOutcome.Corrupt, Error: "complex_form_learning_auxiliary_invalid");
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
                    throw new IOException("complex_form_learning_proposed_environment_unavailable");
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
            using var admission = new ComplexFormLearningWriteAdmission5703(this, path, record, current, proposed, readmission);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CareerComplexFormLearningGuardsInstalled || owners is not null && !LearningOwnerCurrent5703(owners, lease, stamp)
                    || slot != ReadContinuationSlotUnderLease(path)
                    || beforeDigest != CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path))
                    || !CharacterCareerComplexFormLearningTransaction.IsSourceCurrent(owner, saved, effective, command.Request, snapshot!))
                    throw new IOException("complex_form_learning_source_changed_before_replace");
                admission.ValidateFinalFence(record);
                cancellationToken.ThrowIfCancellationRequested();
            });
            completed = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { canceled = ex is OperationCanceledException; }
        // Release the lease before a normal read reacquires it; recovery ignores cancellation.
        var observed = owners is not null && lease is not null
            ? ReadCareerComplexFormLearningWorkspace(owners, lease, id)
            : owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (!observed.Success || observed.Value is not { } observedWorkspace) return CharacterCareerComplexFormLearningService.MapReadFailure(observed);
        var recovered = CharacterCareerComplexFormLearningTransaction.Lookup(owner, observedWorkspace, command.Request.OperationId, digest);
        if (recovered.Outcome == CharacterCareerComplexFormLearningOutcome.Replayed)
            return recovered with { Outcome = completed ? CharacterCareerComplexFormLearningOutcome.Applied : CharacterCareerComplexFormLearningOutcome.Replayed };
        return recovered.Outcome == CharacterCareerComplexFormLearningOutcome.NotFound
            ? new(canceled ? CharacterCareerComplexFormLearningOutcome.Canceled : CharacterCareerComplexFormLearningOutcome.UnknownOutcome,
                CurrentWorkspaceRevision: observedWorkspace.ContentRevision, Error: "complex_form_learning_commit_not_observed") : recovered;
    }
    [ThreadStatic] private static ComplexFormLearningWriteAdmission5703? _complex_formLearningAdmission5703;
    private sealed class ComplexFormLearningWriteAdmission5703 : IDisposable
    {
        private readonly ComplexFormLearningWriteAdmission5703? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal GearCurrentEnvironment? Current { get; }
        private readonly string _recordDigest;
        private readonly WorkspaceStoredDocument _after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? _readmission;
        internal ComplexFormLearningWriteAdmission5703(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            GearCurrentEnvironment? current, WorkspaceStoredDocument after, CharacterCurrentRuleEnvironmentReadmissionResult? readmission)
        {
            Store = store; Path = System.IO.Path.GetFullPath(path); Current = current; _after = after; _readmission = readmission;
            _recordDigest = CharacterCreationFinalizationDigest.Compute(record);
            _previous = _complex_formLearningAdmission5703; _complex_formLearningAdmission5703 = this;
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            if (CharacterCreationFinalizationDigest.Compute(record) != _recordDigest)
                throw new IOException("complex_form_learning_exact_record_changed");
            if (Current is null) return;
            Current.ValidateFinalFence();
            if (_readmission?.AfterCapture is not { } capture || _readmission.AfterExecution is not { } execution
                || !execution.Revalidate(_after, capture, Store._retainedCharacterQueries!))
                throw new IOException("complex_form_learning_proposed_environment_changed");
        }
        public void Dispose() => _complex_formLearningAdmission5703 = _previous;
    }
    private ComplexFormLearningWriteAdmission5703? ComplexFormLearningAdmissionFor5703(string path)
        => _complex_formLearningAdmission5703 is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, System.IO.Path.GetFullPath(path)) ? admission : null;

}
