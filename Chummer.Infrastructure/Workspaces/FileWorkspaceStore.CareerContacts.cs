using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerContactsAtomicCommitCapability, ICharacterCareerContactsCurrentEnvironmentCapability
{
    // Root implements this partial method only with BOTH shared ledger guards.
    // An unintegrated worker patch compiles but cannot offer or commit edits.
    static partial void VerifyCareerContactsGuards(ref bool installed);
    public bool CareerContactsGuardsInstalled
    {
        get { bool installed = false; VerifyCareerContactsGuards(ref installed); return installed; }
    }
    public string CareerContactsRuntimeDigest => CharacterCareerContactsTransaction.Hash(
        "chummer.core.sr5-career-contacts-runtime/v1\0"
        + typeof(CharacterCareerContactsRules).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(CharacterCareerContactsTransaction).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(FileWorkspaceStore).Module.ModuleVersionId.ToString("D"));

    public CharacterCareerContactsResult CommitCareerContacts(OwnerScope owner,
        CharacterCareerContactsCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default)
        => CommitCareerContacts(owner, command, sourceResolver, null, null, default, cancellationToken);

    public CharacterCareerContactsResult CommitCareerContacts(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerContactsCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default)
        => CommitCareerContacts(lease.Stamp.Owner, command, sourceResolver, owners, lease, lease.Stamp, cancellationToken);

    private CharacterCareerContactsResult CommitCareerContacts(OwnerScope owner,
        CharacterCareerContactsCommand command, ICharacterSourceDataResolver sourceResolver,
        IOwnerContextLeaseAccessor? owners, IOwnerContextLease? lease, OwnerContextStamp stamp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceResolver);
        if (owners is not null && !ContactOwnerCurrent(owners, lease, stamp))
            return new(CharacterCareerContactsOutcome.Conflict, Error: "contact_owner_changed");
        if (!CareerContactsGuardsInstalled)
            return new(CharacterCareerContactsOutcome.Unavailable, Error: "contact_ledger_guards_not_installed");
        if (!CharacterCareerContactsTransaction.IsValidOwner(owner)
            || !CharacterCareerContactsTransaction.IsConfirmedCommand(command)
            || command.Binding.OwnerId != owner.NormalizedValue)
            return new(CharacterCareerContactsOutcome.Conflict, Error: "contact_command_invalid");
        var id = command.Request.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null) return new(CharacterCareerContactsOutcome.Missing);
        string digest = CharacterCareerContactsTransaction.CommandDigest(command);
        bool completed = false, canceled = false;
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, id, path, owners, lease, stamp,
                out var delegatedLedger, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return CharacterCareerContactsService.MapReadFailure(read);
            var prior = CharacterCareerContactsTransaction.Lookup(owner, saved, command.Request.OperationId, digest);
            if (prior.Outcome != CharacterCareerContactsOutcome.NotFound) return prior;
            if (cancellationToken.IsCancellationRequested)
                return new(CharacterCareerContactsOutcome.Canceled, Error: "contact_canceled_before_commit");
            if (saved.ContentRevision != command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision)
                return new(CharacterCareerContactsOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "contact_revision_conflict");
            var effective = ContactSources(current, sourceResolver);
            string? slot = ReadContinuationSlotUnderLease(path);
            string beforeDigest = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path));
            if (!CharacterCareerContactsTransaction.TryRead(owner, saved, command.Request.Change, effective,
                ContactRuntime(current, slot, stamp), out var snapshot, out string error))
                return new(CharacterCareerContactsOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            if (!CharacterCareerContactsTransaction.TryBuild(owner, saved, snapshot!, command, out var replacement, out var receipt))
                return new(CharacterCareerContactsOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "contact_binding_conflict");
            if (!IsValidAuxiliaryState(id, receipt!.CommittedWorkspaceRevision, replacement!.AuxiliaryState, current?.Recovery))
                return new(CharacterCareerContactsOutcome.Corrupt, Error: "contact_auxiliary_invalid");
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
                    throw new IOException("contact_proposed_environment_unavailable");
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
            using var admission = new ContactsWriteAdmission(this, path, record, current, proposed, readmission);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CareerContactsGuardsInstalled || owners is not null && !ContactOwnerCurrent(owners, lease, stamp)
                    || slot != ReadContinuationSlotUnderLease(path)
                    || beforeDigest != CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path))
                    || !CharacterCareerContactsTransaction.IsSourceCurrent(saved, effective, command.Binding))
                    throw new IOException("contact_source_changed_before_replace");
                admission.ValidateFinalFence(record);
            });
            completed = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { canceled = ex is OperationCanceledException; }
        // Release the lease before a normal read reacquires it; recovery ignores cancellation.
        var observed = owners is not null && lease is not null
            ? ReadCareerContactsWorkspace(owners, lease, id)
            : owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (!observed.Success || observed.Value is not { } observedWorkspace) return CharacterCareerContactsService.MapReadFailure(observed);
        var recovered = CharacterCareerContactsTransaction.Lookup(owner, observedWorkspace, command.Request.OperationId, digest);
        if (recovered.Outcome == CharacterCareerContactsOutcome.Replayed)
            return recovered with { Outcome = completed ? CharacterCareerContactsOutcome.Applied : CharacterCareerContactsOutcome.Replayed };
        return recovered.Outcome == CharacterCareerContactsOutcome.NotFound
            ? new(canceled ? CharacterCareerContactsOutcome.Canceled : CharacterCareerContactsOutcome.UnknownOutcome,
                CurrentWorkspaceRevision: observedWorkspace.ContentRevision, Error: "contact_commit_not_observed") : recovered;
    }

    // Only this synchronous store/path scope may publish the exact prepared Contacts
    // record. A public command or replacement manifest cannot manufacture admission.
    [ThreadStatic] private static ContactsWriteAdmission? _contactsAdmission;
    private sealed class ContactsWriteAdmission : IDisposable
    {
        private readonly ContactsWriteAdmission? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal GearCurrentEnvironment? Current { get; }
        private readonly string _recordDigest;
        private readonly WorkspaceStoredDocument _after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? _readmission;
        internal ContactsWriteAdmission(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            GearCurrentEnvironment? current, WorkspaceStoredDocument after, CharacterCurrentRuleEnvironmentReadmissionResult? readmission)
        {
            Store = store; Path = System.IO.Path.GetFullPath(path); Current = current; _after = after; _readmission = readmission;
            _recordDigest = CharacterCreationFinalizationDigest.Compute(record);
            _previous = _contactsAdmission; _contactsAdmission = this;
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            if (CharacterCreationFinalizationDigest.Compute(record) != _recordDigest)
                throw new IOException("contact_exact_record_changed");
            if (Current is null) return;
            Current.ValidateFinalFence();
            if (_readmission?.AfterCapture is not { } capture || _readmission.AfterExecution is not { } execution
                || !execution.Revalidate(_after, capture, Store._retainedCharacterQueries!))
                throw new IOException("contact_proposed_environment_changed");
        }
        public void Dispose() => _contactsAdmission = _previous;
    }
    private ContactsWriteAdmission? ContactsAdmissionFor(string path)
        => _contactsAdmission is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, System.IO.Path.GetFullPath(path)) ? admission : null;

    private static bool ContactOwnerCurrent(IOwnerContextLeaseAccessor owners, IOwnerContextLease? lease, OwnerContextStamp stamp)
        => stamp.IsValid && lease is not null && lease.Stamp == stamp && owners.Capture() == stamp;

    // Resolution is local to the already-admitted capture; a refusal never falls back to ambient sources.
    private static ICharacterSourceDataResolver ContactSources(GearCurrentEnvironment? current, ICharacterSourceDataResolver sources)
    {
        if (current is null) return sources;
        current.ValidateReadObservation();
        if (!current.Capture.MatchesOriginalCharacter(current.Stored.Document.Content))
            throw new IOException("contact_current_capture_mismatch");
        return current.Capture.CreateCurrentEnvironmentResolution(current.Execution.ExecutionProfile,
            current.Execution.ExecutionProfileFingerprint)?.Resolver
            ?? throw new IOException("contact_current_resolution_unavailable");
    }

    private string ContactRuntime(GearCurrentEnvironment? current, string? slot, OwnerContextStamp stamp)
        => current is null ? CareerContactsRuntimeDigest : CharacterCareerContactsTransaction.Hash(string.Join('\0',
            CareerContactsRuntimeDigest, slot ?? "", current.Stored.LocalHistory!.IncarnationId,
            current.Capture.IntegrityDigest, current.Execution.ExecutionProfile, current.Execution.ExecutionProfileFingerprint,
            stamp.AuthorityInstanceId, stamp.TransitionRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public WorkspaceStoreReadResult ReadCareerContactsWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id)
    {
        var stamp = lease.Stamp;
        if (!CareerContactsGuardsInstalled || !ContactOwnerCurrent(owners, lease, stamp)) return UnavailableRead();
        try
        {
            string? path = TryGetPath(stamp.Owner, id);
            if (path is null) return new(WorkspaceOperationOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(stamp.Owner, id, path, owners, lease, stamp, out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return read;
            if (!CharacterCareerContactsTransaction.IsValidHistory(saved))
                return new(WorkspaceOperationOutcome.Corrupt, Error: "contact_history_invalid");
            current?.ValidateReadObservation();
            return ContactOwnerCurrent(owners, lease, stamp) ? read : UnavailableRead();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return UnavailableRead(); }
    }

    public CharacterCareerContactsPreviewResult PreviewCareerContacts(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerContactsRequest request, ICharacterSourceDataResolver sources)
    {
        var stamp = lease.Stamp;
        if (!CareerContactsGuardsInstalled || !ContactOwnerCurrent(owners, lease, stamp)
            || !CharacterCareerContactsTransaction.IsValidRequest(request))
            return new(CharacterCareerContactsOutcome.Conflict, Error: "contact_request_invalid");
        try
        {
            string? path = TryGetPath(stamp.Owner, request.WorkspaceId);
            if (path is null) return new(CharacterCareerContactsOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(stamp.Owner, request.WorkspaceId, path, owners, lease, stamp, out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved)
            { var failure = CharacterCareerContactsService.MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
            if (!CharacterCareerContactsTransaction.IsValidHistory(saved))
                return new(CharacterCareerContactsOutcome.Corrupt, Error: "contact_history_invalid");
            if (saved.Document.AuxiliaryState.CharacterCareerContactsReceipts?.Any(r => r.Command.Request.OperationId == request.OperationId) == true)
                return new(CharacterCareerContactsOutcome.IdempotencyConflict, Error: "contact_operation_already_used");
            var effective = ContactSources(current, sources);
            string? slot = ReadContinuationSlotUnderLease(path);
            if (!CharacterCareerContactsTransaction.TryRead(stamp.Owner, saved, request.Change, effective,
                ContactRuntime(current, slot, stamp), out var snapshot, out string error)
                || !CharacterCareerContactsTransaction.TryPreview(snapshot!, request, out var preview, out error))
                return new(CharacterCareerContactsOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            if (!CharacterCareerContactsTransaction.TryBuild(stamp.Owner, saved, snapshot!,
                preview!.Command with { ExplicitlyConfirmed = true }, out _, out _)
                || !CharacterCareerContactsTransaction.IsSourceCurrent(saved, effective, preview.Command.Binding))
                return new(CharacterCareerContactsOutcome.Unavailable, Error: "contact_edit_unavailable");
            current?.ValidateReadObservation();
            if (!ContactOwnerCurrent(owners, lease, stamp) || slot != ReadContinuationSlotUnderLease(path))
                return new(CharacterCareerContactsOutcome.Conflict, Error: "contact_owner_changed");
            return new(CharacterCareerContactsOutcome.Available, preview, saved.ContentRevision);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(CharacterCareerContactsOutcome.Unavailable, Error: "contact_current_sources_unavailable"); }
    }
}
