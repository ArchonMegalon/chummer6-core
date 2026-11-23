using System.Security.Cryptography;
using Chummer.Application.Characters;
using Chummer.Application.Session;
using Chummer.Contracts.Session;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Tx = Chummer.Application.Characters.CharacterSr5NaturalRecoveryTransaction;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterSr5NaturalRecoveryCurrentEnvironmentStore
{
    // Root supplies this only alongside the shared PreserveRetainedRuleSources hook.
    // The existing generic recovery ledger guards remain required independently.
    static partial void VerifyNaturalRecoveryRetainedGuards(ref bool installed);
    public bool SupportsRetainedNaturalRecovery
    {
        get { bool installed = false; VerifyNaturalRecoveryRetainedGuards(ref installed); return SupportsNaturalRecovery && installed; }
    }

    // The existing deterministic entropy seam, with real retained storage enabled.
    internal static FileWorkspaceStore CreateWithRetainedNaturalRecoverySources(string directory,
        ICharacterFileQueries queries, IFileWorkspaceStoreFaultInjector faults, Func<int> die)
        => new(RequireRetainedStateRoot(directory), queries, faults, die);
    private FileWorkspaceStore(string directory, ICharacterFileQueries queries,
        IFileWorkspaceStoreFaultInjector faults, Func<int> die) : this(directory, faults, queries)
        => _naturalRecoveryTestDie = die ?? throw new ArgumentNullException(nameof(die));

    private static void RecoveryOwner(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease, OwnerContextStamp stamp)
    {
        if (!stamp.IsValid || lease.Stamp != stamp || owners.Capture() != stamp)
            throw new IOException("recovery_owner_changed");
    }

    private WorkspaceStoredDocument ReadRecoveryUnderLease(string path, CharacterWorkspaceId id,
        IOwnerContextLeaseAccessor owners, IOwnerContextLease lease, OwnerContextStamp stamp,
        out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> ledger, out GearCurrentEnvironment? current)
    {
        RecoveryOwner(owners, lease, stamp);
        var record = ReadPrivateRecord(path);
        // No migration/repair or missing-current-closure fallback at this boundary.
        if (!TryMaterializeRecord(record, out _, out _, out _, out bool migration) || migration
            || record.RetainedRuleSources is { CurrentEnvironment: null })
            throw new IOException("recovery_current_source_admission_unavailable");
        var read = ReadGearWorkspaceUnderLease(stamp.Owner, id, path, owners, lease, stamp, out ledger, out current);
        if (read.Success && read.Value is { } saved && Tx.IsValidHistory(saved)) return saved;
        current?.Dispose(); current = null;
        throw new IOException("recovery_current_source_admission_unavailable");
    }

    private static ICharacterSourceDataResolver RecoverySources(GearCurrentEnvironment? current,
        ICharacterSourceDataResolver sources)
    {
        if (current is null) return sources;
        current.ValidateReadObservation();
        if (!current.Capture.MatchesOriginalCharacter(current.Stored.Document.Content))
            throw new IOException("recovery_current_capture_mismatch");
        return current.Capture.CreateCurrentEnvironmentResolution(current.Execution.ExecutionProfile,
            current.Execution.ExecutionProfileFingerprint)?.Resolver
            ?? throw new IOException("recovery_current_resolution_unavailable");
    }

    private string RecoveryRuntime(GearCurrentEnvironment? current) => current is null ? NaturalRecoveryRuntimeDigest : Tx.Digest(new
    {
        Runtime = NaturalRecoveryRuntimeDigest, current.Capture.IntegrityDigest, current.Capture.EffectivePolicyDigest,
        current.Execution.ExecutionProfile, current.Execution.ExecutionProfileFingerprint
    });
    private string RecoveryBinding(string path, OwnerContextStamp stamp, WorkspaceStoredDocument saved, GearCurrentEnvironment? current)
        => Tx.Digest(new { stamp, Slot = ReadContinuationSlotUnderLease(path), saved.LocalHistory!.IncarnationId,
            Record = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)), Runtime = RecoveryRuntime(current) });

    public TResult ReadNaturalRecovery<TResult>(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterWorkspaceId workspace, ICharacterSourceDataResolver sources,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string, string?, TResult> project)
    {
        if (!SupportsRetainedNaturalRecovery) throw new IOException("recovery_retained_guards_unavailable");
        var stamp = lease.Stamp; RecoveryOwner(owners, lease, stamp);
        string path = TryGetPath(stamp.Owner, workspace) ?? throw new IOException("recovery_workspace_unavailable");
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        var saved = ReadRecoveryUnderLease(path, workspace, owners, lease, stamp, out _, out var environment);
        using var current = environment;
        if (HasPendingGearEquipment(saved)) throw new IOException("recovery_pending_equipment");
        using var scope = current is null ? (sources as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope() : null;
        string binding = RecoveryBinding(path, stamp, saved, current);
        var result = project(saved, RecoverySources(current, scope ?? sources), RecoveryRuntime(current), binding);
        current?.ValidateReadObservation(); RecoveryOwner(owners, lease, stamp);
        if (RecoveryBinding(path, stamp, saved, current) != binding) throw new IOException("recovery_observation_changed");
        return result;
    }

    public Sr5RecoveryResult LookupNaturalRecovery(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterWorkspaceId workspace, Guid operationId, string digest)
    {
        try
        {
            var stamp = lease.Stamp; RecoveryOwner(owners, lease, stamp);
            string path = TryGetPath(stamp.Owner, workspace) ?? throw new IOException("recovery_workspace_unavailable");
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var result = LookupRecoveryOriginal5472(path, workspace, stamp.Owner.NormalizedValue, operationId, digest);
            RecoveryOwner(owners, lease, stamp);
            return result;
        }
        catch (Exception ex) when (RecoveryExpected(ex)) { return new(Sr5RecoveryOutcome.Unknown); }
    }

    public Sr5RecoveryResult ExecuteNaturalRecovery(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        Sr5RecoveryCommand command, ICharacterSourceDataResolver sources, CancellationToken token)
        => ExecuteNaturalRecovery(owners, lease, command, sources, token, null, null);

    internal Sr5RecoveryResult ExecuteNaturalRecovery(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        Sr5RecoveryCommand command, ICharacterSourceDataResolver sources, CancellationToken token,
        SessionNaturalRecoveryCommit? continuation,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string, SessionPlayAuthority>? project)
    {
        var stamp = lease.Stamp;
        if (!SupportsRetainedNaturalRecovery || !Tx.ValidCommand(command) || command.OwnerId != stamp.Owner.NormalizedValue)
            return new(Sr5RecoveryOutcome.Conflict);
        try
        {
            RecoveryOwner(owners, lease, stamp);
            var id = command.Request.WorkspaceId;
            string path = TryGetPath(stamp.Owner, id) ?? throw new IOException("recovery_workspace_unavailable");
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            (WorkspaceDocument Document, Sr5RecoveryOperation Operation) next;
            {
                var saved = ReadRecoveryUnderLease(path, id, owners, lease, stamp, out var ledger, out var environment);
                using var current = environment;
                var prior = Tx.Lookup(stamp.Owner, saved, command.Request.OperationId, Tx.Digest(command));
                if (prior.Outcome != Sr5RecoveryOutcome.NotFound) return prior;
                if (HasPendingGearEquipment(saved)) return new(Sr5RecoveryOutcome.Unavailable);
                token.ThrowIfCancellationRequested();
                Sr5RecoveryReview Review()
                {
                    using var scope = current is null ? (sources as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope() : null;
                    return Tx.Review(stamp.Owner, saved, command.Request, RecoverySources(current, scope ?? sources),
                        RecoveryRuntime(current), RecoveryBinding(path, stamp, saved, current));
                }
                var review = Review();
                if (continuation is not null && (project is null
                    || SessionPlayEncoding.Digest(RecoverySessionBinding(path, saved, current, stamp, project))
                        != SessionPlayEncoding.Digest(continuation.Intent.Review.Canonical)))
                    throw new IOException("recovery_session_review_changed");
                if (command != review.Command with { ExplicitlyConfirmed = true }) return new(Sr5RecoveryOutcome.Conflict);
                next = Tx.Apply(saved, review, command);
                var published = PublishRecovery(path, owners, lease, stamp, saved, ledger, current, next.Document, next.Operation, token, () =>
                {
                    if (Review().Command != review.Command) throw new IOException("recovery_review_changed");
                }, continuation, project);
                next = (published, next.Operation);
                if (continuation is not null)
                    continuation.Acknowledge(ReadRecoveryContinuationUnderLease5472(path, owners, lease, continuation.Intent, next.Operation, project!));
            }
            if (command.Request.Action == Sr5RecoveryAction.Roll)
            {
                // The first replace above durably published RollUnresolved. Re-admit that
                // exact record: the pre-fence environment is no longer current after writing.
                var fenced = ReadRecoveryUnderLease(path, id, owners, lease, stamp, out var ledger, out var environment);
                using var current = environment;
                var original = Tx.Lookup(stamp.Owner, fenced, command.Request.OperationId, Tx.Digest(command));
                if (original.Outcome != Sr5RecoveryOutcome.Unknown || original.Operation?.Digest != next.Operation.Digest
                    || fenced.ContentRevision != next.Operation.RecordedRevision
                    || CharacterCreationFinalizationDigest.Compute(fenced.Document) != CharacterCreationFinalizationDigest.Compute(next.Document))
                    throw new IOException("recovery_roll_fence_changed");
                void CheckRollSource()
                {
                    token.ThrowIfCancellationRequested(); RecoveryOwner(owners, lease, stamp);
                    current?.ValidateFinalFence();
                    using var scope = current is null ? (sources as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope() : null;
                    var quote = next.Operation.Activity.Quote;
                    var actual = Tx.Quote(fenced, RecoverySources(current, scope ?? sources), RecoveryRuntime(current),
                        quote.ActivityId, quote.Track, quote.EarliestStartUtc);
                    if (actual.QuoteDigest != quote.QuoteDigest) throw new IOException("recovery_roll_source_changed");
                }
                CheckRollSource();
                int[] dice = Enumerable.Range(0, next.Operation.Activity.Quote.DicePool)
                    .Select(_ => _naturalRecoveryTestDie?.Invoke() ?? RandomNumberGenerator.GetInt32(1, 7)).ToArray();
                var rolled = Tx.FinishRoll(next.Document, next.Operation, dice);
                var published = PublishRecovery(path, owners, lease, stamp, fenced, ledger, current, rolled.Document, rolled.Operation,
                    token, CheckRollSource, continuation, project);
                next = (published, rolled.Operation);
                if (continuation is not null)
                    continuation.Acknowledge(ReadRecoveryContinuationUnderLease5472(path, owners, lease, continuation.Intent, next.Operation, project!));
            }
            return new(Sr5RecoveryOutcome.Applied, next.Operation);
        }
        catch (Exception ex) when (RecoveryExpected(ex))
        {
            // The service observes the same operation after lease release. Never dispatch again.
            return new(Sr5RecoveryOutcome.Unknown);
        }
    }

    private WorkspaceDocument PublishRecovery(string path, IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        OwnerContextStamp stamp, WorkspaceStoredDocument saved, IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> ledger,
        GearCurrentEnvironment? current, WorkspaceDocument document, Sr5RecoveryOperation operation,
        CancellationToken token, Action validateSource, SessionNaturalRecoveryCommit? continuation = null,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string, SessionPlayAuthority>? project = null)
    {
        document = SessionConditionTransferRules.WithNaturalRecoveryCondition(saved.Document, document, operation);
        using var conditionAdmission = operation.Command.Request.Action == Sr5RecoveryAction.Complete
            ? AdmitNaturalRecoveryCondition5460(path, saved, document, operation) : null;
        if (!IsValidAuxiliaryState(saved.Id, operation.RecordedRevision, document.AuxiliaryState, current?.Recovery))
            throw new IOException("recovery_auxiliary_invalid");
        string before = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path));
        string? slot = ReadContinuationSlotUnderLease(path);
        var record = BuildPersistedRecord(document, operation.RecordedRevision, operation.RecordedRevision,
            saved.LocalHistory!, ledger, saved.DelegatedGmHistorySegmentStarts);
        var proposed = saved with { Document = document, ContentRevision = operation.RecordedRevision, SavedRevision = operation.RecordedRevision };
        CharacterCurrentRuleEnvironmentReadmissionResult? readmission = null;
        if (current is not null)
        {
            readmission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                _retainedCharacterQueries!, proposed, token);
            if (readmission.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution || readmission.Blockers.Count != 0)
                throw new IOException("recovery_proposed_environment_unavailable");
            current.Recovery.RetainAdmittedCapture(new(capture, _retainedCharacterQueries!), capture.IntegrityDigest);
            var continuationSnapshot = new WorkspaceContinuationSnapshot(stamp.Owner.NormalizedValue,
                new(saved.Id, proposed.Document, proposed.LastUpdatedUtc, proposed.ContentRevision, proposed.SavedRevision), []);
            record = record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion,
                RetainedRuleSources = current.Record.RetainedRuleSources! with {
                    References = current.Recovery.SnapshotReferences(),
                    CurrentEnvironment = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(continuationSnapshot,
                        capture, execution.ExecutionProfile, execution.ExecutionProfileFingerprint, []) } };
        }
        using var admission = new NaturalRecoveryWriteAdmission(this, path, record, current, proposed, readmission);
        // All readmission/capacity checks precede intent. Both journal files are reserved before entropy.
        validateSource(); admission.ValidateFinalFence(record);
        using var publication = continuation is null ? null : new RecoveryPublication5472(this, path, record, publishedRecord =>
        {
            PreserveRecoveryContinuationPlan5472(path, continuation, operation, publishedRecord, proposed, readmission, project!);
            continuation.PublishIntent();
        });
        WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
        {
            token.ThrowIfCancellationRequested(); RecoveryOwner(owners, lease, stamp);
            if (!SupportsRetainedNaturalRecovery || slot != ReadContinuationSlotUnderLease(path)
                || before != CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)))
                throw new IOException("recovery_record_changed");
            validateSource(); admission.ValidateFinalFence(record);
        });
        return document;
    }

    [ThreadStatic] private static NaturalRecoveryWriteAdmission? _naturalRecoveryAdmission;
    private sealed class NaturalRecoveryWriteAdmission : IDisposable
    {
        private readonly NaturalRecoveryWriteAdmission? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal GearCurrentEnvironment? Current { get; }
        private readonly string _recordDigest;
        private readonly WorkspaceStoredDocument _after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? _readmission;
        internal NaturalRecoveryWriteAdmission(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            GearCurrentEnvironment? current, WorkspaceStoredDocument after, CharacterCurrentRuleEnvironmentReadmissionResult? readmission)
        {
            Store = store; Path = System.IO.Path.GetFullPath(path); Current = current; _after = after; _readmission = readmission;
            _recordDigest = CharacterCreationFinalizationDigest.Compute(record);
            _previous = _naturalRecoveryAdmission; _naturalRecoveryAdmission = this;
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            if (CharacterCreationFinalizationDigest.Compute(record) != _recordDigest) throw new IOException("recovery_exact_record_changed");
            if (Current is null) return;
            Current.ValidateFinalFence();
            if (_readmission?.AfterCapture is not { } capture || _readmission.AfterExecution is not { } execution
                || !execution.Revalidate(_after, capture, Store._retainedCharacterQueries!))
                throw new IOException("recovery_proposed_environment_changed");
        }
        public void Dispose() => _naturalRecoveryAdmission = _previous;
    }
    private NaturalRecoveryWriteAdmission? NaturalRecoveryAdmissionFor(string path)
        => _naturalRecoveryAdmission is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, System.IO.Path.GetFullPath(path)) ? admission : null;
    private static bool RecoveryExpected(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidOperationException
        or ArgumentException or FormatException or OverflowException or System.Xml.XmlException or OperationCanceledException;
}
