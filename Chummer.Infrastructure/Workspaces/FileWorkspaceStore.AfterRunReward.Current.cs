using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Rewards = Chummer.Infrastructure.Workspaces.WorkspaceCharacterAfterRunRewardService;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterAfterRunRewardCurrentEnvironmentStore
{
    private static void RewardOwner(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease, OwnerContextStamp stamp)
    {
        if (!stamp.IsValid || lease.Stamp != stamp || owners.Capture() != stamp)
            throw new IOException("reward_owner_changed");
    }

    private string RewardBinding(string path, OwnerContextStamp stamp)
        => CharacterAfterRunRewardProjector.PayloadDigest(CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.core.after-run-current-binding/v1", stamp,
            Record = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)),
            Slot = ReadContinuationSlotUnderLease(path),
            UpdatedUtc = File.GetLastWriteTimeUtc(path)
        }));

    private WorkspaceStoreReadResult ReadRewardUnderLease(string path, CharacterWorkspaceId id,
        IOwnerContextLeaseAccessor owners, IOwnerContextLease lease, OwnerContextStamp stamp,
        out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> ledger, out GearCurrentEnvironment? current,
        bool completeReadObservation = false)
    {
        ledger = []; current = null;
        RewardOwner(owners, lease, stamp);
        if (!File.Exists(path)) return new(WorkspaceOperationOutcome.Missing);
        var record = ReadPrivateRecord(path);
        // Never migrate, activate a private closure, or fall back to stock rules.
        if (!TryMaterializeRecord(record, out _, out _, out _, out bool migration) || migration
            || record.RetainedRuleSources is { CurrentEnvironment: null }) return UnavailableRead();
        // Read/preview may bind the complete private record during their one
        // synchronous read. Commit retains its existing fresh admission path.
        var read = completeReadObservation
            ? ReadGearWorkspaceCoreUnderLease(stamp.Owner, id, path, owners, lease, stamp,
                deferFreeSpriteReadObservation: false, out ledger, out current, completeReadObservation: true)
            : ReadGearWorkspaceUnderLease(stamp.Owner, id, path, owners, lease, stamp, out ledger, out current);
        return read;
    }

    private void ObserveReward(string path, string binding, IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, OwnerContextStamp stamp, GearCurrentEnvironment? current)
    {
        // This established observation also closes the custody/storage fence
        // after query evaluation; no source mutation can escape the opening fence.
        current?.ValidateFreeSpriteReadObservation();
        RewardOwner(owners, lease, stamp);
        if (RewardBinding(path, stamp) != binding) throw new IOException("reward_observation_changed");
    }

    public CharacterAfterRunRewardReadResult ReadAfterRunReward(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId workspaceId)
    {
        if (!CharacterAfterRunRewardProjector.IsValidWorkspaceId(workspaceId))
            return new(CharacterAfterRunRewardOutcome.Corrupt, Error: "reward_workspace_identity_invalid");
        try
        {
            var stamp = lease.Stamp; RewardOwner(owners, lease, stamp);
            string? path = TryGetPath(stamp.Owner, workspaceId);
            if (path is null || !File.Exists(path)) return new(CharacterAfterRunRewardOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            string binding = RewardBinding(path, stamp);
            var read = ReadRewardUnderLease(path, workspaceId, owners, lease, stamp, out _, out var environment, completeReadObservation: true);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return new(Rewards.Map(read.Outcome), Error: read.Error);
            var result = Rewards.ReadSaved(saved);
            ObserveReward(path, binding, owners, lease, stamp, current);
            return result;
        }
        catch (Exception ex) when (RewardExpected(ex))
        { return new(CharacterAfterRunRewardOutcome.Unavailable, Error: "reward_current_read_unavailable"); }
    }

    public CharacterAfterRunRewardPreviewResult PreviewAfterRunReward(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterAfterRunRewardPreviewRequest request)
    {
        if (!CharacterAfterRunRewardProjector.IsValidPreviewRequest(request))
            return new(CharacterAfterRunRewardOutcome.Conflict, Error: "reward_command_invalid");
        try
        {
            var stamp = lease.Stamp; RewardOwner(owners, lease, stamp);
            string? path = TryGetPath(stamp.Owner, request.WorkspaceId);
            if (path is null || !File.Exists(path)) return new(CharacterAfterRunRewardOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            string binding = RewardBinding(path, stamp);
            var read = ReadRewardUnderLease(path, request.WorkspaceId, owners, lease, stamp, out _, out var environment, completeReadObservation: true);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return new(Rewards.Map(read.Outcome), Error: read.Error);
            if (HasPendingGearEquipment(saved)) return new(CharacterAfterRunRewardOutcome.Unavailable, Error: "reward_pending_equipment");
            var result = Rewards.PreviewSaved(saved, request, SupportsWorkspaceAuxiliaryStateAtomicCommit, binding);
            if (result.Preview is { } preview)
            {
                if (!CharacterAfterRunRewardProjector.TryBuild(saved, preview.Command with { ExplicitlyConfirmed = true },
                        out var document, out var receipt, out string error))
                    return new(Rewards.ProjectionOutcome(error), Error: error);
                if (current is not null)
                {
                    var proposed = saved with { Document = document, ContentRevision = receipt.CommittedWorkspaceRevision,
                        SavedRevision = receipt.CommittedWorkspaceRevision };
                    var admission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                        _retainedCharacterQueries!, proposed);
                    if (admission.AfterCapture is null || admission.AfterExecution is null || admission.Blockers.Count != 0)
                        return new(CharacterAfterRunRewardOutcome.Unavailable, Error: "reward_proposed_environment_unavailable");
                }
            }
            ObserveReward(path, binding, owners, lease, stamp, current);
            return result;
        }
        catch (Exception ex) when (RewardExpected(ex))
        { return new(CharacterAfterRunRewardOutcome.Unavailable, Error: "reward_current_preview_unavailable"); }
    }

    public CharacterAfterRunRewardResult LookupAfterRunReward(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId workspaceId, Guid operationId, string commandDigest)
    {
        if (!CharacterAfterRunRewardProjector.IsValidWorkspaceId(workspaceId) || operationId == Guid.Empty
            || !CharacterAfterRunRewardProjector.IsDigest(commandDigest))
            return new(CharacterAfterRunRewardOutcome.Corrupt, Error: "reward_lookup_invalid");
        try
        {
            var stamp = lease.Stamp; RewardOwner(owners, lease, stamp);
            string? path = TryGetPath(stamp.Owner, workspaceId);
            if (path is null || !File.Exists(path)) return new(CharacterAfterRunRewardOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var result = LookupRewardUnderLease(path, workspaceId, operationId, commandDigest);
            RewardOwner(owners, lease, stamp);
            return result;
        }
        catch (Exception ex) when (RewardExpected(ex))
        { return new(CharacterAfterRunRewardOutcome.Unavailable, Error: "reward_receipt_observation_unavailable"); }
    }

    // Original-operation observation needs no current source evaluation and performs
    // no migration, repair, capture activation or write. Lineage is still mandatory.
    private CharacterAfterRunRewardResult LookupRewardUnderLease(string path, CharacterWorkspaceId id,
        Guid operationId, string digest)
    {
        var record = ReadPrivateRecord(path);
        if (!TryMaterializeRecord(record, out var document, out long revision, out long savedRevision, out bool migration)
            || migration || record.LocalHistory is not { } local || !local.IsValid(revision))
            return new(CharacterAfterRunRewardOutcome.Corrupt, Error: "reward_receipt_ledger_corrupt");
        var saved = new WorkspaceStoredDocument(id, document, revision, savedRevision,
            new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)) { LocalHistory = local };
        return Rewards.LookupSaved(saved, operationId, digest);
    }

    public CharacterAfterRunRewardResult CommitAfterRunReward(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterAfterRunRewardCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CharacterAfterRunRewardProjector.IsValidCommand(command))
            return new(CharacterAfterRunRewardOutcome.Conflict, Error: "reward_command_invalid");
        bool published = false;
        CharacterAfterRunRewardResult result;
        try { result = PublishAfterRunReward(owners, lease, command, cancellationToken, out published); }
        catch (Exception ex) when (RewardExpected(ex))
        { result = new(CharacterAfterRunRewardOutcome.Unavailable, Error: "reward_commit_result_unknown"); }
        if (result.Outcome != CharacterAfterRunRewardOutcome.Applied
            && result.Error != "reward_commit_result_unknown") return result;
        // The old observation and workspace lease are disposed. Observe only; never
        // dispatch another mutation after an exception, cancellation or lost response.
        var recovered = LookupAfterRunReward(owners, lease, command.WorkspaceId, command.OperationId, command.CommandDigest());
        return recovered.Outcome == CharacterAfterRunRewardOutcome.Replayed
            ? recovered with { Outcome = published ? CharacterAfterRunRewardOutcome.Applied : CharacterAfterRunRewardOutcome.Replayed }
            : recovered.Outcome == CharacterAfterRunRewardOutcome.NotFound ? result with
                { Outcome = CharacterAfterRunRewardOutcome.Unavailable, Error = "reward_commit_not_observed" } : recovered;
    }

    private CharacterAfterRunRewardResult PublishAfterRunReward(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterAfterRunRewardCommand command, CancellationToken token, out bool published)
    {
        published = false;
        var stamp = lease.Stamp; RewardOwner(owners, lease, stamp);
        string? path = TryGetPath(stamp.Owner, command.WorkspaceId);
        if (path is null || !File.Exists(path)) return new(CharacterAfterRunRewardOutcome.Missing);
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        var prior = LookupRewardUnderLease(path, command.WorkspaceId, command.OperationId, command.CommandDigest());
        if (prior.Outcome != CharacterAfterRunRewardOutcome.NotFound) return prior;
        string binding = RewardBinding(path, stamp);
        if (command.ExpectedCurrentBindingDigest != binding)
            return new(CharacterAfterRunRewardOutcome.Conflict, Error: "reward_current_binding_conflict");
        var read = ReadRewardUnderLease(path, command.WorkspaceId, owners, lease, stamp, out var ledger, out var environment);
        using var current = environment;
        if (!read.Success || read.Value is not { } saved) return new(Rewards.Map(read.Outcome), Error: read.Error);
        if (!SupportsWorkspaceAuxiliaryStateAtomicCommit || HasPendingGearEquipment(saved))
            return new(CharacterAfterRunRewardOutcome.Unavailable, Error: "reward_atomic_commit_unavailable");
        if (saved.ContentRevision != command.ExpectedWorkspaceRevision || saved.SavedRevision != saved.ContentRevision)
            return new(CharacterAfterRunRewardOutcome.Conflict, Error: "reward_workspace_revision_conflict");
        token.ThrowIfCancellationRequested();
        if (!CharacterAfterRunRewardProjector.TryBuild(saved, command, out var document, out var receipt, out string error))
            return new(Rewards.ProjectionOutcome(error), Error: error);
        if (!IsValidAuxiliaryStateTransition(saved.Id, saved.ContentRevision, saved.SavedRevision,
                receipt.CommittedWorkspaceRevision, saved.Document.AuxiliaryState, document.AuxiliaryState,
                saved.Document, document, current?.Recovery))
            return new(CharacterAfterRunRewardOutcome.Corrupt, Error: "reward_auxiliary_transition_invalid");
        var proposed = saved with { Document = document, ContentRevision = receipt.CommittedWorkspaceRevision,
            SavedRevision = receipt.CommittedWorkspaceRevision };
        var record = BuildPersistedRecord(document, proposed.ContentRevision, proposed.SavedRevision,
            saved.LocalHistory!, ledger, saved.DelegatedGmHistorySegmentStarts);
        CharacterCurrentRuleEnvironmentReadmissionResult? readmission = null;
        if (current is not null)
        {
            readmission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                _retainedCharacterQueries!, proposed, token);
            if (readmission.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution
                || readmission.Blockers.Count != 0)
                return new(CharacterAfterRunRewardOutcome.Unavailable, Error: "reward_proposed_environment_unavailable");
            current.Recovery.RetainAdmittedCapture(new(capture, _retainedCharacterQueries!), capture.IntegrityDigest);
            var snapshot = new WorkspaceContinuationSnapshot(stamp.Owner.NormalizedValue,
                new(saved.Id, document, proposed.LastUpdatedUtc, proposed.ContentRevision, proposed.SavedRevision), []);
            record = record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion,
                RetainedRuleSources = current.Record.RetainedRuleSources! with {
                    References = current.Recovery.SnapshotReferences(),
                    CurrentEnvironment = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(snapshot,
                        capture, execution.ExecutionProfile, execution.ExecutionProfileFingerprint, []) } };
        }
        using var admission = new RewardWriteAdmission(this, path, record, current, proposed, readmission);
        WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
        {
            token.ThrowIfCancellationRequested(); RewardOwner(owners, lease, stamp);
            if (RewardBinding(path, stamp) != binding) throw new IOException("reward_current_binding_changed");
        }, rewardAdmission: admission);
        published = true;
        return new(CharacterAfterRunRewardOutcome.Applied);
    }

    [ThreadStatic] private static RewardWriteAdmission? _rewardWriteAdmission;
    private sealed class RewardWriteAdmission : IDisposable
    {
        private readonly RewardWriteAdmission? _previous;
        private bool _claimed;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal GearCurrentEnvironment? Current { get; }
        private readonly string _recordDigest;
        private readonly WorkspaceStoredDocument _after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? _readmission;
        internal RewardWriteAdmission(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            GearCurrentEnvironment? current, WorkspaceStoredDocument after, CharacterCurrentRuleEnvironmentReadmissionResult? readmission)
        {
            Store = store; Path = System.IO.Path.GetFullPath(path); Current = current; _after = after; _readmission = readmission;
            _recordDigest = CharacterCreationFinalizationDigest.Compute(record);
            _previous = _rewardWriteAdmission; _rewardWriteAdmission = this;
        }
        internal void Claim(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            WorkspaceWriteDisposition disposition, bool hasOwnerFence)
        {
            if (_claimed || !hasOwnerFence || disposition != WorkspaceWriteDisposition.ReplaceExisting
                || !ReferenceEquals(Store, store) || !ReferenceEquals(_rewardWriteAdmission, this)
                || !PathComparer.Equals(Path, System.IO.Path.GetFullPath(path)))
                throw new IOException("reward_write_admission_not_owned");
            ValidatePreparation(record);
            _claimed = true;
        }
        internal void ValidateClaimedPreparation(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record)
        {
            if (!_claimed || !ReferenceEquals(Store, store) || !ReferenceEquals(_rewardWriteAdmission, this)
                || !PathComparer.Equals(Path, System.IO.Path.GetFullPath(path))
                || Current is not null && (_readmission?.AfterCapture is null || _readmission.AfterExecution is null))
                throw new IOException("reward_write_admission_not_owned");
            ValidatePreparation(record);
        }
        internal void ValidatePreparation(PersistedWorkspaceRecord record)
        {
            // This private store/path-bound admission was created only after
            // successful before/after admission. Preparation binds that exact
            // proposed record; it is not permission to publish it.
            if (CharacterCreationFinalizationDigest.Compute(record) != _recordDigest)
                throw new IOException("reward_exact_record_changed");
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            ValidateClaimedPreparation(Store, Path, record);
            if (Current is null) return;
            Current.ValidateFinalFence();
            if (_readmission?.AfterCapture is not { } capture || _readmission.AfterExecution is not { } execution
                || !execution.Revalidate(_after, capture, Store._retainedCharacterQueries!))
                throw new IOException("reward_proposed_environment_changed");
        }
        public void Dispose() => _rewardWriteAdmission = _previous;
    }
    private RewardWriteAdmission? RewardAdmissionFor(string path)
        => _rewardWriteAdmission is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, System.IO.Path.GetFullPath(path)) ? admission : null;
    private static bool RewardExpected(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidOperationException
        or ArgumentException or FormatException or OverflowException or System.Xml.XmlException or OperationCanceledException;
}
