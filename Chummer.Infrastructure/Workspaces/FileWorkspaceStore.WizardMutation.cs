using Chummer.Contracts.Characters;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : IWorkspaceWizardMutationStoreCapability
{
    public WorkspaceWizardAdvancementCatalog ReadWizardAdvancement(IOwnerContextAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, CharacterWorkspaceId id, long revision, bool attributes, CancellationToken cancellationToken = default)
    {
        WorkspaceWizardAdvancementCatalog Failed(WorkspaceWizardOutcome outcome = WorkspaceWizardOutcome.Rejected)
            => new(outcome,id,revision,[],[],0,[],0,"The exact current advancement source is unavailable.");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!IsCurrentSaveOwner(owners,expected,lease) || TryGetPath(expected.Owner,id) is not { } path
                || !File.Exists(path) || !TrySecureExistingWorkspaceDirectory(expected.Owner, allowLegacyMigration:false)) return Failed();
            using var operation = AcquireWorkspaceOperation(path,recoverStaleTempFiles:false);
            var read = ReadGearWorkspaceUnderLease(expected.Owner,id,path,owners,lease,expected,out _,out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved || saved.ContentRevision != revision) return Failed();
            if (current is null) return Failed(IsCurrentSaveOwner(owners,expected,lease) ? WorkspaceWizardOutcome.Legacy : WorkspaceWizardOutcome.Rejected);
            var result = WorkspaceWizardTransaction.ReadAdvancement(saved,WizardSources(current,cancellationToken),attributes);
            current.ValidateReadObservation(); cancellationToken.ThrowIfCancellationRequested();
            return IsCurrentSaveOwner(owners,expected,lease) ? result : Failed();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException
            or ArgumentException or System.Xml.XmlException or OverflowException) { return Failed(); }
    }
    public WorkspaceWizardResult PreviewWizardMutation(IOwnerContextAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, WorkspaceWizardRequest request, CancellationToken cancellationToken = default)
        => ObserveWizard(owners, expected, lease, request, null, preview: true, cancellationToken);
    public WorkspaceWizardResult CommitWizardMutation(IOwnerContextAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, WorkspaceWizardCommand command, CancellationToken cancellationToken = default)
        => command is null ? WizardRefused() : ObserveWizard(owners, expected, lease, command.Request, command, preview: false, cancellationToken);
    public WorkspaceWizardResult LookupWizardMutation(IOwnerContextAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, WorkspaceWizardRequest request, CancellationToken cancellationToken = default)
        => ObserveWizard(owners, expected, lease, request, null, preview: false, cancellationToken);

    private WorkspaceWizardResult ObserveWizard(IOwnerContextAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, WorkspaceWizardRequest request, WorkspaceWizardCommand? command,
        bool preview, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!WorkspaceWizardTransaction.Valid(request) || !IsCurrentSaveOwner(owners, expected, lease)) return WizardRefused();
        bool dispatched = false;
        try
        {
            request = WorkspaceWizardTransaction.Freeze(request);
            string? path = TryGetPath(expected.Owner, request.WorkspaceId);
            // Observation cannot create a foreign-owner migration lock, migrate a
            // legacy directory or discard interrupted owner-migration evidence.
            if (path is null || !File.Exists(path)
                || !TrySecureExistingWorkspaceDirectory(expected.Owner, allowLegacyMigration:false)) return WizardRefused();
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var read = ReadGearWorkspaceUnderLease(expected.Owner, request.WorkspaceId, path, owners, lease, expected,
                out var ledger, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved || HasPendingGearEquipment(saved)) return WizardRefused();
            // Only a successfully admitted unbound observation can select the old
            // compatibility route. A retained refusal never authorizes that route.
            if (current is null) return IsCurrentSaveOwner(owners,expected,lease) ? new(WorkspaceWizardOutcome.Legacy) : WizardRefused();
            void CheckRead()
            {
                cancellationToken.ThrowIfCancellationRequested(); current.ValidateReadObservation();
                if (!IsCurrentSaveOwner(owners, expected, lease)) throw new IOException("Wizard owner changed.");
            }
            var prior = WorkspaceWizardTransaction.Lookup(saved, expected.Owner.NormalizedValue, request);
            if (prior.Outcome != WorkspaceWizardOutcome.NotFound) { CheckRead(); return prior; }
            if (!preview && command is null) { CheckRead(); return prior; }
            if (saved.ContentRevision != request.ExpectedContentRevision || saved.SavedRevision != saved.ContentRevision)
                return WizardRefused("Save or reopen the exact reviewed runner revision before this action.");
            string before = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path));
            string? slot = ReadContinuationSlotUnderLease(path);
            string binding = WorkspaceWizardTransaction.Hash(new { before, slot, expected, request,
                current.Capture.IntegrityDigest, current.Execution.ExecutionProfile, current.Execution.ExecutionProfileFingerprint,
                Runtime = typeof(WorkspaceWizardTransaction).Module.ModuleVersionId });
            if (command is not null && command.BindingDigest != binding) return WizardRefused("The reviewed owner, source or character changed.");
            // Exact retained source bytes, never an ambient resolver or supplied settings.
            var replacement = WorkspaceWizardTransaction.Build(saved, request, expected.Owner.NormalizedValue,
                WizardSources(current,cancellationToken), new XmlCharacterMetadataCommands(new CharacterFileService()),
                _retainedCharacterQueries!, out var receipt);
            var proposed = saved with { Document = replacement, ContentRevision = receipt.CommittedRevision,
                SavedRevision = receipt.CommittedRevision };
            var readmission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                _retainedCharacterQueries!, proposed, cancellationToken);
            if (readmission.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution
                || readmission.Blockers.Count != 0) return WizardRefused("The resulting runner cannot be admitted by its retained rules.");
            CheckRead();
            if (preview) return new(WorkspaceWizardOutcome.Available, new(request, binding));
            if (!IsValidAuxiliaryState(saved.Id, proposed.ContentRevision, replacement.AuxiliaryState, current.Recovery)) return WizardRefused();
            var record = BuildPersistedRecord(replacement, proposed.ContentRevision, proposed.SavedRevision,
                saved.LocalHistory!, ledger, saved.DelegatedGmHistorySegmentStarts);
            current.Recovery.RetainAdmittedCapture(new(capture, _retainedCharacterQueries!), capture.IntegrityDigest);
            var continuation = new WorkspaceContinuationSnapshot(expected.Owner.NormalizedValue,
                new(saved.Id, proposed.Document, proposed.LastUpdatedUtc, proposed.ContentRevision, proposed.SavedRevision), []);
            record = record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion,
                RetainedRuleSources = current.Record.RetainedRuleSources! with {
                    References = current.Recovery.SnapshotReferences(),
                    CurrentEnvironment = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(continuation,
                        capture, execution.ExecutionProfile, execution.ExecutionProfileFingerprint, []) } };
            void Fence()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentSaveOwner(owners, expected, lease) || ReadContinuationSlotUnderLease(path) != slot
                    || CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)) != before)
                    throw new IOException("Wizard owner, continuation or original record changed.");
                current.ValidateFinalFence();
                if (!execution.Revalidate(proposed, capture, _retainedCharacterQueries!))
                    throw new IOException("Wizard proposed current environment changed.");
            }
            using var admission = new WizardWriteAdmission(this, path, record, Fence);
            Fence(); dispatched = true;
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting,
                beforeTargetReplace: () => admission.Validate(record));
            // Publication completed. Do not turn cancellation into permission to retry.
            return new(WorkspaceWizardOutcome.Applied, Receipt: receipt);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or System.Xml.XmlException or System.Text.Json.JsonException or OverflowException or OperationCanceledException)
        {
            if (!dispatched) return WizardRefused("The action was rejected before publication. Reopen the current review.");
        }
        // A writer exception may follow atomic publication. Read only, under the
        // same original owner authority; never call Commit again to find out.
        try
        {
            var recovered = LookupWizardMutation(owners, expected, lease, request, CancellationToken.None);
            if (recovered.Applied) return recovered;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException) { }
        return new(WorkspaceWizardOutcome.Unknown, Error: "The save result is unknown. Keep this operation identity and recover its receipt before another action.");
    }
    private static WorkspaceWizardResult WizardRefused(string? error = null) => new(WorkspaceWizardOutcome.Rejected,
        Error: error ?? "The typed action is unavailable for this owner, source or runner.");

    private static ICharacterSourceDataResolver WizardSources(GearCurrentEnvironment current, CancellationToken token)
        => new WizardRetainedSources(current.Capture,current.Execution.ExecutionProfile,
            current.Execution.ExecutionProfileFingerprint,token);

    // The existing editors reproject both reviewed and staged XML. An ordinary
    // replay resolver deliberately accepts only its original character bytes.
    // Rebind through Core's existing closed-capture observation, never ambient
    // files or a new rule engine. This grants no write/admission authority: the
    // complete proposed record still passes ReadmitProposedCurrentEnvironment.
    private sealed class WizardRetainedSources(CharacterCreationRuleSourceCapture original,
        string profile, string fingerprint, CancellationToken token) : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            token.ThrowIfCancellationRequested();
            var capture = original.MatchesOriginalCharacter(characterXml) ? original
                : original.RecaptureCurrentEnvironment(characterXml,profile,fingerprint,token);
            return capture?.CreateCurrentEnvironmentResolution(profile,fingerprint)?.Resolver.TryCreateContext(characterXml);
        }
    }

    [ThreadStatic] private static WizardWriteAdmission? _wizardWriteAdmission;
    private sealed class WizardWriteAdmission : IDisposable
    {
        private readonly WizardWriteAdmission? previous;
        private readonly string digest;
        private readonly Action fence;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal WizardWriteAdmission(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record, Action fence)
        {
            Store = store; Path = System.IO.Path.GetFullPath(path); this.fence = fence;
            digest = CharacterCreationFinalizationDigest.Compute(record);
            previous = _wizardWriteAdmission; _wizardWriteAdmission = this;
        }
        internal void Validate(PersistedWorkspaceRecord record)
        {
            if (CharacterCreationFinalizationDigest.Compute(record) != digest) throw new IOException("Wizard record changed.");
            fence();
        }
        public void Dispose() => _wizardWriteAdmission = previous;
    }
    private WizardWriteAdmission? WizardAdmissionFor(string path) => _wizardWriteAdmission is { } admission
        && ReferenceEquals(admission.Store,this) && PathComparer.Equals(admission.Path,System.IO.Path.GetFullPath(path)) ? admission : null;
}
