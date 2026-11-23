using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerReputationCurrentCapability
{
    private static bool ReputationOwnerCurrent(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        OwnerContextStamp stamp) => stamp.IsValid && lease.Stamp == stamp && owners.Capture() == stamp;

    // The private current reader owns admission, source custody and authenticated history.
    // No ordinary Get or caller-supplied replacement can authorize a current document.
    private T ReadReputation<T>(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterWorkspaceId id, Func<WorkspaceStoredDocument, GearCurrentEnvironment?, string, T> action,
        Func<WorkspaceStoreReadResult, T> failure)
    {
        var stamp = lease.Stamp;
        if (!CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id)
            || !ReputationOwnerCurrent(owners, lease, stamp)) return failure(UnavailableRead());
        try
        {
            string? path = TryGetPath(stamp.Owner, id);
            if (path is null) return failure(new(WorkspaceOperationOutcome.Missing));
            // Read, preview and receipt recovery must not delete interrupted-write evidence.
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var read = ReadGearWorkspaceUnderLease(stamp.Owner, id, path, owners, lease, stamp, out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return failure(read);
            if (!CharacterCareerReputationTransaction.IsValidHistory(saved))
                return failure(new(WorkspaceOperationOutcome.Corrupt, Error: "reputation_history_invalid"));
            var result = action(saved, current, path);
            current?.ValidateReadObservation();
            return ReputationOwnerCurrent(owners, lease, stamp) ? result : failure(UnavailableRead());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return failure(new(WorkspaceOperationOutcome.Unavailable, Error: "reputation_current_unavailable")); }
    }

    private static ICharacterSourceDataResolver ReputationSources(GearCurrentEnvironment? current,
        ICharacterSourceDataResolver fallback)
    {
        if (current is null) return fallback;
        current.ValidateReadObservation();
        if (!current.Capture.MatchesOriginalCharacter(current.Stored.Document.Content))
            throw new IOException("reputation_current_capture_mismatch");
        return current.Capture.CreateCurrentEnvironmentResolution(current.Execution.ExecutionProfile,
            current.Execution.ExecutionProfileFingerprint)?.Resolver
            ?? throw new IOException("reputation_current_resolution_unavailable");
    }

    private string ReputationRuntime(GearCurrentEnvironment? current, WorkspaceStoredDocument saved, string path, OwnerContextStamp stamp)
        => CharacterCareerReputationTransaction.Hash(System.Text.Json.JsonSerializer.Serialize(new
        {
            Runtime = CareerReputationRuntimeDigest, stamp.Owner, stamp.AuthorityInstanceId, stamp.TransitionRevision,
            Slot = ReadContinuationSlotUnderLease(path), Incarnation = saved.LocalHistory?.IncarnationId,
            Capture = current?.Capture.IntegrityDigest, Profile = current?.Execution.ExecutionProfile,
            ProfileFingerprint = current?.Execution.ExecutionProfileFingerprint
        }));

    public CharacterCareerReputationReadResult ReadCareerReputation(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, ICharacterSourceDataResolver sources)
        => ReadReputation(owners, lease, id, (saved, current, _) =>
            CharacterCareerReputationProjector.TryRead(saved, ReputationSources(current, sources), out var snapshot, out string error)
                ? new CharacterCareerReputationReadResult(CharacterCareerReputationOutcome.Available, snapshot)
                : new(CharacterCareerReputationOutcome.Unavailable, Error: error),
            read => { var failure = WorkspaceCharacterCareerReputationService.MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); });

    public CharacterCareerReputationPreviewResult PreviewCareerReputation(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerReputationRequest request, ICharacterSourceDataResolver sources)
    {
        if (!CharacterCareerReputationTransaction.IsValidRequest(request))
            return new(CharacterCareerReputationOutcome.Conflict, Error: "reputation_request_invalid");
        return ReadReputation(owners, lease, request.WorkspaceId, (saved, current, path) =>
        {
            if (saved.Document.AuxiliaryState.CharacterCareerReputationReceipts?
                .Any(row => row.Command.Request.OperationId == request.OperationId) == true)
                return new CharacterCareerReputationPreviewResult(CharacterCareerReputationOutcome.IdempotencyConflict,
                    Error: "reputation_operation_already_used");
            var captured = new ReputationSourceCapture(ReputationSources(current, sources));
            string runtime = ReputationRuntime(current, saved, path, lease.Stamp);
            if (!CharacterCareerReputationProjector.TryRead(saved, captured, out var snapshot, out string error))
                return new(CharacterCareerReputationOutcome.Unavailable, Error: error);
            if (!CharacterCareerReputationTransaction.TryPreview(snapshot!, request, runtime, out var preview)
                || !CharacterCareerReputationTransaction.TryBuild(saved, snapshot!, preview!.Command with { ExplicitlyConfirmed = true },
                    runtime, out var replacement, out var receipt))
                return new(CharacterCareerReputationOutcome.Unavailable, Error: "reputation_quote_unavailable");
            if (current is not null)
            {
                var proposed = saved with { Document = replacement!, ContentRevision = receipt!.CommittedWorkspaceRevision,
                    SavedRevision = receipt.CommittedWorkspaceRevision };
                var admission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                    _retainedCharacterQueries!, proposed);
                if (admission.AfterCapture is null || admission.AfterExecution is null || admission.Blockers.Count != 0)
                    return new(CharacterCareerReputationOutcome.Unavailable, Error: "reputation_proposed_environment_unavailable");
            }
            if (!captured.IsCurrent(snapshot!.RuleStateDigest))
                return new(CharacterCareerReputationOutcome.Conflict, Error: "reputation_source_changed");
            return new(CharacterCareerReputationOutcome.Available, preview, saved.ContentRevision);
        }, read => { var failure = WorkspaceCharacterCareerReputationService.MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); });
    }

    public CharacterCareerReputationResult LookupCareerReputation(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, Guid operationId, string commandDigest)
    {
        if (operationId == Guid.Empty || !CharacterCareerReputationTransaction.IsDigest(commandDigest))
            return new(CharacterCareerReputationOutcome.Corrupt, Error: "reputation_lookup_invalid");
        return ReadReputation(owners, lease, id,
            (saved, _, _) => CharacterCareerReputationTransaction.Lookup(saved, operationId, commandDigest),
            WorkspaceCharacterCareerReputationService.MapReadFailure);
    }

    public CharacterCareerReputationResult CommitCareerReputation(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerReputationCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CharacterCareerReputationTransaction.IsConfirmedCommand(command))
            return new(CharacterCareerReputationOutcome.Conflict, Error: "reputation_command_invalid");
        var stamp = lease.Stamp;
        if (!ReputationOwnerCurrent(owners, lease, stamp))
            return new(CharacterCareerReputationOutcome.Conflict, Error: "reputation_owner_changed");
        var id = command.Request.WorkspaceId;
        string digest = CharacterCareerReputationTransaction.CommandDigest(command);
        bool completed = false;
        try
        {
            string? path = TryGetPath(stamp.Owner, id);
            if (path is null) return new(CharacterCareerReputationOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var read = ReadGearWorkspaceUnderLease(stamp.Owner, id, path, owners, lease, stamp, out var ledger, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved)
                return WorkspaceCharacterCareerReputationService.MapReadFailure(read);
            var prior = CharacterCareerReputationTransaction.Lookup(saved, command.Request.OperationId, digest);
            current?.ValidateReadObservation();
            if (!ReputationOwnerCurrent(owners, lease, stamp)) throw new IOException("reputation_owner_changed");
            if (prior.Outcome != CharacterCareerReputationOutcome.NotFound) return prior;
            if (saved.ContentRevision != command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision)
                return new(CharacterCareerReputationOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "reputation_revision_conflict");
            var captured = new ReputationSourceCapture(ReputationSources(current, sources));
            string beforeDigest = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path));
            string? slot = ReadContinuationSlotUnderLease(path);
            if (!CharacterCareerReputationProjector.TryRead(saved, captured, out var snapshot, out string error))
                return new(CharacterCareerReputationOutcome.Conflict, Error: error);
            if (!CharacterCareerReputationTransaction.TryBuild(saved, snapshot!, command,
                ReputationRuntime(current, saved, path, stamp), out var replacement, out var receipt))
                return new(CharacterCareerReputationOutcome.Conflict, Error: "reputation_preview_conflict");
            if (!IsValidAuxiliaryState(id, receipt!.CommittedWorkspaceRevision, replacement!.AuxiliaryState, current?.Recovery))
                return new(CharacterCareerReputationOutcome.Corrupt, Error: "reputation_auxiliary_invalid");
            var record = BuildPersistedRecord(replacement, receipt.CommittedWorkspaceRevision, receipt.CommittedWorkspaceRevision,
                saved.LocalHistory!, ledger, saved.DelegatedGmHistorySegmentStarts);
            var proposed = saved with { Document = replacement, ContentRevision = receipt.CommittedWorkspaceRevision,
                SavedRevision = receipt.CommittedWorkspaceRevision };
            CharacterCurrentRuleEnvironmentReadmissionResult? readmission = null;
            if (current is not null)
            {
                readmission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                    _retainedCharacterQueries!, proposed, cancellationToken);
                if (readmission.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution
                    || readmission.Blockers.Count != 0) throw new IOException("reputation_proposed_environment_unavailable");
                current.Recovery.RetainAdmittedCapture(new(capture, _retainedCharacterQueries!), capture.IntegrityDigest);
                var continuation = new WorkspaceContinuationSnapshot(stamp.Owner.NormalizedValue,
                    new(id, proposed.Document, proposed.LastUpdatedUtc, proposed.ContentRevision, proposed.SavedRevision), []);
                record = record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion,
                    RetainedRuleSources = current.Record.RetainedRuleSources! with
                    {
                        References = current.Recovery.SnapshotReferences(),
                        CurrentEnvironment = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(continuation, capture,
                            execution.ExecutionProfile, execution.ExecutionProfileFingerprint, [])
                    } };
            }
            void Fence()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ReputationOwnerCurrent(owners, lease, stamp) || slot != ReadContinuationSlotUnderLease(path)
                    || beforeDigest != CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path))
                    || !captured.IsCurrent(snapshot!.RuleStateDigest)) throw new IOException("reputation_source_changed_before_replace");
            }
            using var admission = new ReputationWriteAdmission(this, path, record, current, proposed, readmission, Fence);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting,
                beforeTargetReplace: () => admission.ValidateFinalFence(record));
            completed = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { /* A durable result is recovered without the canceled token and without another mutation. */ }
        var recovered = LookupCareerReputation(owners, lease, id, command.Request.OperationId, digest);
        if (recovered.Outcome == CharacterCareerReputationOutcome.Replayed)
            return recovered with { Outcome = completed ? CharacterCareerReputationOutcome.Applied : CharacterCareerReputationOutcome.Replayed };
        return recovered.Outcome == CharacterCareerReputationOutcome.NotFound
            ? new(CharacterCareerReputationOutcome.Unavailable, CurrentWorkspaceRevision: recovered.CurrentWorkspaceRevision,
                Error: "reputation_commit_not_observed") : recovered;
    }

    [ThreadStatic] private static ReputationWriteAdmission? _reputationWriteAdmission;
    private sealed class ReputationWriteAdmission : IDisposable
    {
        private readonly ReputationWriteAdmission? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal GearCurrentEnvironment? Current { get; }
        private readonly string _recordDigest;
        private readonly WorkspaceStoredDocument _after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? _readmission;
        private readonly Action _fence;
        internal ReputationWriteAdmission(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            GearCurrentEnvironment? current, WorkspaceStoredDocument after,
            CharacterCurrentRuleEnvironmentReadmissionResult? readmission, Action fence)
        {
            Store = store; Path = System.IO.Path.GetFullPath(path); Current = current; _after = after;
            _readmission = readmission; _fence = fence;
            _recordDigest = CharacterCreationFinalizationDigest.Compute(record);
            _previous = _reputationWriteAdmission; _reputationWriteAdmission = this;
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            _fence();
            if (CharacterCreationFinalizationDigest.Compute(record) != _recordDigest)
                throw new IOException("reputation_exact_record_changed");
            if (Current is null) return;
            Current.ValidateFinalFence();
            if (_readmission?.AfterCapture is not { } capture || _readmission.AfterExecution is not { } execution
                || !execution.Revalidate(_after, capture, Store._retainedCharacterQueries!))
                throw new IOException("reputation_proposed_environment_changed");
        }
        public void Dispose() => _reputationWriteAdmission = _previous;
    }

    private ReputationWriteAdmission? ReputationAdmissionFor(string path)
        => _reputationWriteAdmission is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, System.IO.Path.GetFullPath(path)) ? admission : null;
}
