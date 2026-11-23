using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerLifestyleCurrentCapability
{
    // Enabled only by the partial shipped with both shared retained-write hooks.
    static partial void VerifyCareerLifestyleCurrentWriteGuards(ref bool installed);
    private bool CareerLifestyleCurrentWriteGuardsInstalled
    { get { bool installed = false; VerifyCareerLifestyleCurrentWriteGuards(ref installed); return installed; } }

    private T WithLifestyleEnvironment<T>(IOwnerContextAccessor owners, OwnerContextStamp stamp, IOwnerContextLease lease,
        CharacterWorkspaceId id, ICharacterSourceDataResolver unboundSources,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, GearCurrentEnvironment?, string,
            IReadOnlyList<DelegatedGmCharacterEditLedgerEntry>, T> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!stamp.IsValid || owners is not IOwnerContextLeaseAccessor authority || authority.Capture() != stamp
            || lease.Stamp != stamp || !CharacterCareerLifestyleMaintenanceTransaction.IsValidOwner(stamp.Owner)
            || TryGetPath(stamp.Owner, id) is not { } path) throw new InvalidOperationException("lifestyle_owner_changed");
        using var operation = AcquireWorkspaceOperation(path);
        // Existing store-local owner-lease/current-capture admission, also used by other Career lanes.
        // It observes the actual record before selecting the resolver. No ambient retry on refusal.
        var read = ReadGearWorkspaceUnderLease(stamp.Owner, id, path, owners, lease, stamp, out var ledger, out var environment);
        using var current = environment;
        if (!read.Success || read.Value is not { } saved) throw new InvalidOperationException("lifestyle_current_workspace_unavailable");
        var exact = current is null ? unboundSources : current.Capture.CreateReplayResolver();
        var result = action(saved, exact, current, path, ledger);
        ValidateLifestyleOwner(owners, stamp, lease);
        return result;
    }
    private static void ValidateLifestyleOwner(IOwnerContextAccessor owners, OwnerContextStamp stamp, IOwnerContextLease lease)
    {
        if (!stamp.IsValid || owners is not IOwnerContextLeaseAccessor authority
            || authority.Capture() != stamp || lease.Stamp != stamp)
            throw new InvalidOperationException("lifestyle_owner_changed");
    }
    private static string LifestyleOwnerRuntime(string runtime, OwnerContextStamp stamp)
        => CharacterCareerLifestyleMaintenanceTransaction.Hash(runtime + "\0" + JsonSerializer.Serialize(stamp));
    private static bool LifestyleCurrentFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or ArgumentException;

    public CharacterCareerLifestyleMaintenanceLoadResult LoadCurrentCareerLifestyleMaintenance(IOwnerContextAccessor owners,
        OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease, CharacterWorkspaceId workspaceId,
        ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default)
    {
        try
        {
            return WithLifestyleEnvironment(owners, expectedOwner, ownerLease, workspaceId, sources, (saved, exact, current, _, _) =>
            {
                if (!CharacterCareerLifestyleMaintenanceTransaction.IsValidHistory(saved))
                    return new CharacterCareerLifestyleMaintenanceLoadResult(CharacterCareerLifestyleMaintenanceOutcome.Corrupt);
                var result = CharacterCareerLifestyleMaintenanceProjection.Load(saved, exact);
                current?.ValidateReadObservation();
                return result;
            }, cancellationToken);
        }
        catch (Exception ex) when (LifestyleCurrentFailure(ex))
        { return new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_current_workspace_unavailable"); }
    }

    public CharacterCareerLifestyleMaintenancePreviewResult PreviewCurrentCareerLifestyleMaintenance(IOwnerContextAccessor owners,
        OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease, CharacterCareerLifestyleMaintenanceRequest request,
        ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default)
    {
        request = CharacterCareerLifestyleMaintenanceTransaction.Freeze(request)!;
        if (!CharacterCareerLifestyleMaintenanceTransaction.IsValidRequest(request)) return new(CharacterCareerLifestyleMaintenanceOutcome.Conflict);
        try
        {
            return WithLifestyleEnvironment(owners, expectedOwner, ownerLease, request.WorkspaceId, sources, (saved, exact, current, _, _) =>
            {
                if (current is not null && !CareerLifestyleCurrentWriteGuardsInstalled)
                    return new CharacterCareerLifestyleMaintenancePreviewResult(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_current_write_admission_unavailable");
                string runtime = LifestyleOwnerRuntime(CareerLifestyleMaintenanceRuntimeDigest, expectedOwner);
                var result = CharacterCareerLifestyleMaintenanceService.PreviewSaved(expectedOwner.Owner, saved, request, exact, runtime);
                if (current is not null && result.Preview is { } preview)
                {
                    if (!CharacterCareerLifestyleMaintenanceTransaction.TryRead(expectedOwner.Owner, saved, request, exact, runtime, out var snapshot, out _)
                        || !CharacterCareerLifestyleMaintenanceTransaction.TryBuild(expectedOwner.Owner, saved, snapshot!, preview.Command with { ExplicitlyConfirmed = true },
                            out var replacement, out var receipt)) return new CharacterCareerLifestyleMaintenancePreviewResult(CharacterCareerLifestyleMaintenanceOutcome.Unavailable);
                    PrepareLifestyleReadmission(current, saved with { Document = replacement!, ContentRevision = receipt!.CommittedWorkspaceRevision,
                        SavedRevision = receipt.CommittedWorkspaceRevision }, cancellationToken);
                }
                current?.ValidateFinalFence();
                return result;
            }, cancellationToken);
        }
        catch (Exception ex) when (LifestyleCurrentFailure(ex))
        { return new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_current_mechanics_not_admitted"); }
    }

    public CharacterCareerLifestyleMaintenanceResult LookupCurrentCareerLifestyleMaintenance(IOwnerContextAccessor owners,
        OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease, CharacterCareerLifestyleMaintenanceCommand original,
        ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default)
    {
        if (!(CharacterCareerLifestyleMaintenanceTransaction.IsConfirmedCommand(original))) return new(CharacterCareerLifestyleMaintenanceOutcome.Conflict);
        try
        {
            return WithLifestyleEnvironment(owners, expectedOwner, ownerLease, original.Request.WorkspaceId, sources, (saved, _, current, _, _) =>
            {
                var result = CharacterCareerLifestyleMaintenanceTransaction.Lookup(expectedOwner.Owner, saved, original);
                current?.ValidateReadObservation();
                return result;
            }, cancellationToken);
        }
        catch (Exception ex) when (LifestyleCurrentFailure(ex))
        { return new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_current_result_unavailable"); }
    }

    public CharacterCareerLifestyleMaintenanceResult CommitCurrentCareerLifestyleMaintenance(IOwnerContextAccessor owners,
        OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease, CharacterCareerLifestyleMaintenanceCommand command,
        ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default)
    {
        command = CharacterCareerLifestyleMaintenanceTransaction.Freeze(command)!;
        if (!CharacterCareerLifestyleMaintenanceTransaction.IsConfirmedCommand(command) || command.Binding.OwnerId != expectedOwner.Owner.NormalizedValue)
            return new(CharacterCareerLifestyleMaintenanceOutcome.Conflict);
        bool completed = false;
        try
        {
            var result = WithLifestyleEnvironment<CharacterCareerLifestyleMaintenanceResult>(owners, expectedOwner, ownerLease, command.Request.WorkspaceId,
                sources, (saved, exact, current, path, ledger) =>
            {
                var prior = CharacterCareerLifestyleMaintenanceTransaction.Lookup(expectedOwner.Owner, saved, command);
                if (prior.Outcome != CharacterCareerLifestyleMaintenanceOutcome.NotFound) return prior;
                if (cancellationToken.IsCancellationRequested) return new(CharacterCareerLifestyleMaintenanceOutcome.Canceled);
                string runtime = LifestyleOwnerRuntime(CareerLifestyleMaintenanceRuntimeDigest, expectedOwner);
                if (current is not null && !CareerLifestyleCurrentWriteGuardsInstalled
                    || !CharacterCareerLifestyleMaintenanceTransaction.TryRead(expectedOwner.Owner, saved, command.Request, exact, runtime, out var snapshot, out _)
                    || !CharacterCareerLifestyleMaintenanceTransaction.TryBuild(expectedOwner.Owner, saved, snapshot!, command, out var replacement, out var receipt))
                    return new(CharacterCareerLifestyleMaintenanceOutcome.Conflict, Error: "lifestyle_binding_conflict");
                WriteCurrentLifestyle(saved, replacement!, receipt!.CommittedWorkspaceRevision, current, path, ledger,
                    () => CharacterCareerLifestyleMaintenanceTransaction.IsSourceCurrent(saved, exact, command.Binding),
                    () => ValidateLifestyleOwner(owners, expectedOwner, ownerLease), cancellationToken);
                completed = true;
                return new(CharacterCareerLifestyleMaintenanceOutcome.Applied, receipt, receipt.CommittedWorkspaceRevision);
            }, CancellationToken.None);
            if (!completed) return result;
        }
        catch (Exception ex) when (LifestyleCurrentFailure(ex)) { }
        // Release the workspace lease before lookup. Never repeat a dispatch after an uncertain result.
        var recovered = LookupCurrentCareerLifestyleMaintenance(owners, expectedOwner, ownerLease, command, sources);
        return recovered.Outcome == CharacterCareerLifestyleMaintenanceOutcome.Replayed
            ? recovered with { Outcome = completed ? CharacterCareerLifestyleMaintenanceOutcome.Applied : CharacterCareerLifestyleMaintenanceOutcome.Replayed }
            : new(CharacterCareerLifestyleMaintenanceOutcome.UnknownOutcome, Error: "lifestyle_commit_not_observed");
    }

    public CharacterCareerLifestylePaymentPreviewResult PreviewCurrentCareerLifestylePayment(IOwnerContextAccessor owners,
        OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease, CharacterCareerLifestylePaymentRequest request,
        ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default)
    {

        if (!CharacterCareerLifestylePaymentTransaction.IsValidRequest(request)) return new(CharacterCareerLifestylePaymentOutcome.Conflict);
        try
        {
            return WithLifestyleEnvironment(owners, expectedOwner, ownerLease, request.WorkspaceId, sources, (saved, exact, current, _, _) =>
            {
                if (current is not null && !CareerLifestyleCurrentWriteGuardsInstalled)
                    return new CharacterCareerLifestylePaymentPreviewResult(CharacterCareerLifestylePaymentOutcome.Unavailable, Error: "lifestyle_current_write_admission_unavailable");
                string runtime = LifestyleOwnerRuntime(CareerLifestylePaymentRuntimeDigest, expectedOwner);
                var result = CharacterCareerLifestylePaymentService.PreviewSaved(expectedOwner.Owner, saved, request, exact, runtime);
                if (current is not null && result.Preview is { } preview)
                {
                    if (!CharacterCareerLifestylePaymentTransaction.TryRead(expectedOwner.Owner, saved, request.LifestyleId, exact, runtime, out var snapshot, out _)
                        || !CharacterCareerLifestylePaymentTransaction.TryBuild(expectedOwner.Owner, saved, snapshot!, preview.Command with { ExplicitlyConfirmed = true },
                            out var replacement, out var receipt)) return new CharacterCareerLifestylePaymentPreviewResult(CharacterCareerLifestylePaymentOutcome.Unavailable);
                    PrepareLifestyleReadmission(current, saved with { Document = replacement!, ContentRevision = receipt!.CommittedWorkspaceRevision,
                        SavedRevision = receipt.CommittedWorkspaceRevision }, cancellationToken);
                }
                current?.ValidateFinalFence();
                return result;
            }, cancellationToken);
        }
        catch (Exception ex) when (LifestyleCurrentFailure(ex))
        { return new(CharacterCareerLifestylePaymentOutcome.Unavailable, Error: "lifestyle_current_mechanics_not_admitted"); }
    }

    public CharacterCareerLifestylePaymentResult LookupCurrentCareerLifestylePayment(IOwnerContextAccessor owners,
        OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease, CharacterWorkspaceId workspaceId, Guid operationId, string commandDigest,
        ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default)
    {
        if (!(operationId != Guid.Empty && CharacterCareerLifestylePaymentTransaction.IsDigest(commandDigest))) return new(CharacterCareerLifestylePaymentOutcome.Conflict);
        try
        {
            return WithLifestyleEnvironment(owners, expectedOwner, ownerLease, workspaceId, sources, (saved, _, current, _, _) =>
            {
                var result = CharacterCareerLifestylePaymentTransaction.Lookup(expectedOwner.Owner, saved, operationId, commandDigest);
                current?.ValidateReadObservation();
                return result;
            }, cancellationToken);
        }
        catch (Exception ex) when (LifestyleCurrentFailure(ex))
        { return new(CharacterCareerLifestylePaymentOutcome.Unavailable, Error: "lifestyle_current_result_unavailable"); }
    }

    public CharacterCareerLifestylePaymentResult CommitCurrentCareerLifestylePayment(IOwnerContextAccessor owners,
        OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease, CharacterCareerLifestylePaymentCommand command,
        ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default)
    {

        if (!CharacterCareerLifestylePaymentTransaction.IsConfirmedCommand(command) || command.Binding.OwnerId != expectedOwner.Owner.NormalizedValue)
            return new(CharacterCareerLifestylePaymentOutcome.Conflict);
        bool completed = false;
        try
        {
            var result = WithLifestyleEnvironment<CharacterCareerLifestylePaymentResult>(owners, expectedOwner, ownerLease, command.Request.WorkspaceId,
                sources, (saved, exact, current, path, ledger) =>
            {
                var prior = CharacterCareerLifestylePaymentTransaction.Lookup(expectedOwner.Owner, saved, command.Request.OperationId, CharacterCareerLifestylePaymentTransaction.CommandDigest(command));
                if (prior.Outcome != CharacterCareerLifestylePaymentOutcome.NotFound) return prior;
                if (cancellationToken.IsCancellationRequested) return new(CharacterCareerLifestylePaymentOutcome.Canceled);
                string runtime = LifestyleOwnerRuntime(CareerLifestylePaymentRuntimeDigest, expectedOwner);
                if (current is not null && !CareerLifestyleCurrentWriteGuardsInstalled
                    || !CharacterCareerLifestylePaymentTransaction.TryRead(expectedOwner.Owner, saved, command.Request.LifestyleId, exact, runtime, out var snapshot, out _)
                    || !CharacterCareerLifestylePaymentTransaction.TryBuild(expectedOwner.Owner, saved, snapshot!, command, out var replacement, out var receipt))
                    return new(CharacterCareerLifestylePaymentOutcome.Conflict, Error: "lifestyle_binding_conflict");
                WriteCurrentLifestyle(saved, replacement!, receipt!.CommittedWorkspaceRevision, current, path, ledger,
                    () => CharacterCareerLifestylePaymentTransaction.IsSourceCurrent(saved, exact, command.Binding),
                    () => ValidateLifestyleOwner(owners, expectedOwner, ownerLease), cancellationToken);
                completed = true;
                return new(CharacterCareerLifestylePaymentOutcome.Applied, receipt, receipt.CommittedWorkspaceRevision);
            }, CancellationToken.None);
            if (!completed) return result;
        }
        catch (Exception ex) when (LifestyleCurrentFailure(ex)) { }
        // Release the workspace lease before lookup. Never repeat a dispatch after an uncertain result.
        var recovered = LookupCurrentCareerLifestylePayment(owners, expectedOwner, ownerLease, command.Request.WorkspaceId, command.Request.OperationId, CharacterCareerLifestylePaymentTransaction.CommandDigest(command), sources);
        return recovered.Outcome == CharacterCareerLifestylePaymentOutcome.Replayed
            ? recovered with { Outcome = completed ? CharacterCareerLifestylePaymentOutcome.Applied : CharacterCareerLifestylePaymentOutcome.Replayed }
            : new(CharacterCareerLifestylePaymentOutcome.UnknownOutcome, Error: "lifestyle_commit_not_observed");
    }
    private CharacterCurrentRuleEnvironmentReadmissionResult PrepareLifestyleReadmission(GearCurrentEnvironment current,
        WorkspaceStoredDocument proposed, CancellationToken token)
    {
        var result = current.Execution.ReadmitProposedCurrentEnvironment(current.Stored, current.Capture,
            _retainedCharacterQueries!, proposed, token);
        if (result.AfterCapture is not { } capture || result.AfterExecution is null || result.Blockers.Count != 0
            || !IsValidAuxiliaryState(proposed.Id, proposed.ContentRevision, proposed.Document.AuxiliaryState, current.Recovery))
            throw new InvalidOperationException("lifestyle_current_mechanics_not_admitted");
        var references = current.Record.RetainedRuleSources!.References;
        if (!references.Any(value => value.CaptureDigest == capture.IntegrityDigest) && references.Length >= MaximumRetainedRuleSourceReferences)
            throw new InvalidOperationException("lifestyle_retained_capture_capacity");
        return result;
    }

    private void WriteCurrentLifestyle(WorkspaceStoredDocument saved, WorkspaceDocument replacement, long revision,
        GearCurrentEnvironment? current, string path, IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> ledger,
        Func<bool> sourceCurrent, Action ownerCurrent, CancellationToken token)
    {
        if (!IsValidAuxiliaryState(saved.Id, revision, replacement.AuxiliaryState, current?.Recovery))
            throw new InvalidOperationException("lifestyle_auxiliary_invalid");
        var proposed = saved with { Document = replacement, ContentRevision = revision, SavedRevision = revision };
        var record = BuildPersistedRecord(replacement, revision, revision, saved.LocalHistory!, ledger, saved.DelegatedGmHistorySegmentStarts);
        CharacterCurrentRuleEnvironmentReadmissionResult? readmission = null;
        if (current is not null)
        {
            if (!CareerLifestyleCurrentWriteGuardsInstalled) throw new InvalidOperationException("lifestyle_current_write_admission_unavailable");
            readmission = PrepareLifestyleReadmission(current, proposed, token);
            var capture = readmission.AfterCapture!;
            current.Recovery.RetainAdmittedCapture(new(capture, _retainedCharacterQueries!), capture.IntegrityDigest);
            var snapshot = new WorkspaceContinuationSnapshot(current.Record.RetainedRuleSources!.OwnerId,
                new(saved.Id, replacement, saved.LastUpdatedUtc, revision, revision), ledger.Select(entry => entry.Receipt).ToArray())
                { DelegatedGmHistorySegmentStarts = saved.DelegatedGmHistorySegmentStarts.ToArray() };
            var environment = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(snapshot, capture,
                readmission.AfterExecution!.ExecutionProfile, readmission.AfterExecution.ExecutionProfileFingerprint, []);
            record = record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion,
                RetainedRuleSources = current.Record.RetainedRuleSources with
                { References = current.Recovery.SnapshotReferences(), CurrentEnvironment = environment } };
        }
        else record = PreserveRetainedRuleSources(path, record, WorkspaceWriteDisposition.ReplaceExisting);
        using var admission = new LifestyleCurrentWriteAdmission(this, path, record, current, proposed, readmission, sourceCurrent, ownerCurrent);
        token.ThrowIfCancellationRequested();
        WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
        {
            token.ThrowIfCancellationRequested();
            admission.ValidateFinalFence(record);
        });
    }

    [ThreadStatic] private static LifestyleCurrentWriteAdmission? _lifestyleCurrentAdmission;
    private sealed class LifestyleCurrentWriteAdmission : IDisposable
    {
        private readonly LifestyleCurrentWriteAdmission? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal GearCurrentEnvironment? Current { get; }
        private readonly string _before, _after;
        private readonly string? _slot;
        private readonly WorkspaceStoredDocument _proposed;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? _readmission;
        private readonly Func<bool> _sourceCurrent;
        private readonly Action _ownerCurrent;
        internal LifestyleCurrentWriteAdmission(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            GearCurrentEnvironment? current, WorkspaceStoredDocument proposed, CharacterCurrentRuleEnvironmentReadmissionResult? readmission,
            Func<bool> sourceCurrent, Action ownerCurrent)
        {
            Store = store; Path = System.IO.Path.GetFullPath(path); Current = current; _proposed = proposed; _readmission = readmission;
            _sourceCurrent = sourceCurrent; _ownerCurrent = ownerCurrent;
            _before = CharacterCreationFinalizationDigest.Compute(store.ReadPrivateRecord(path));
            _after = CharacterCreationFinalizationDigest.Compute(record); _slot = ReadContinuationSlotUnderLease(path);
            _previous = _lifestyleCurrentAdmission; _lifestyleCurrentAdmission = this;
        }
        private void ValidateRecordFence(PersistedWorkspaceRecord record)
        {
            _ownerCurrent();
            if (ReadContinuationSlotUnderLease(Path) != _slot
                || CharacterCreationFinalizationDigest.Compute(Store.ReadPrivateRecord(Path)) != _before
                || CharacterCreationFinalizationDigest.Compute(record) != _after)
                throw new IOException("lifestyle_record_changed");
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            ValidateRecordFence(record);
            if (Current is not null)
            {
                Current.ValidateFinalFence();
                if (_readmission?.AfterCapture is not { } capture || _readmission.AfterExecution is not { } execution
                    || !execution.Revalidate(_proposed, capture, Store._retainedCharacterQueries!))
                    throw new IOException("lifestyle_current_environment_changed");
            }
            if (!_sourceCurrent()) throw new IOException("lifestyle_source_changed_before_replace");
            // Revalidation calls source/query collaborators. Recheck custody and the
            // exact original record/owner after they return, never just before them.
            if (Current is not null && !Current.Recovery.ValidateRetainedRuleSources())
                throw new IOException("lifestyle_current_environment_changed");
            ValidateRecordFence(record);
        }
        public void Dispose() => _lifestyleCurrentAdmission = _previous;
    }
    private LifestyleCurrentWriteAdmission? LifestyleAdmissionFor(string path)
        => _lifestyleCurrentAdmission is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, Path.GetFullPath(path)) ? admission : null;
    private void RevalidateLifestyleCurrentForWrite(string path, PersistedWorkspaceRecord record)
        => LifestyleAdmissionFor(path)?.ValidateFinalFence(record);
}
