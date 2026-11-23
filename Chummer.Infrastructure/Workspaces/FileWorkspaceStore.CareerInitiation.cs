using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public string CareerInitiationRuntimeFingerprint => CharacterCareerInitiationRules.Hash(
        "chummer.core.sr5-career-initiation-runtime/v1\0"
        + typeof(CharacterCareerInitiationRules).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(CharacterCareerInitiationTransaction).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(FileWorkspaceStore).Module.ModuleVersionId.ToString("D"));

    public WorkspaceStoreReadResult ReadCareerInitiationWorkspace(OwnerContextStamp owner,
        CharacterWorkspaceId workspace)
    {
        if (!owner.IsValid || !CharacterCareerQualityTransaction.IsValidOwner(owner.Owner))
            return new(WorkspaceOperationOutcome.Unavailable, Error: "initiation_owner_admission_rejected");
        string? path = TryGetPath(owner.Owner, workspace);
        if (path is null) return new(WorkspaceOperationOutcome.Missing);
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner.Owner))
                return new(WorkspaceOperationOutcome.Missing);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner.Owner, workspace, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved) return read;
            return HasCareerInitiationCampaignAuthority(saved, delegatedLedger)
                ? new(WorkspaceOperationOutcome.Unavailable, Error: "initiation_campaign_authority_unavailable")
                : read;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new(WorkspaceOperationOutcome.Unavailable, Error: "initiation_workspace_unavailable");
        }
    }

    private static bool HasCareerInitiationCampaignAuthority(WorkspaceStoredDocument saved,
        IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> delegatedLedger)
        => delegatedLedger.Count != 0
            || saved.Document.AuxiliaryState.CharacterCreationFinalizationArchive?.CompletionAuthority?.Proposal.CampaignAuthority is not null;

    public CharacterCareerInitiationResult CommitCareerInitiation(OwnerContextStamp owner,
        CharacterCareerInitiationPlan plan, ICharacterCareerInitiationAuthorityResolver authority,
        CancellationToken cancellationToken = default)
        => CommitCareerInitiation(owner, plan, authority, null, null, cancellationToken);

    public CharacterCareerInitiationResult CommitCareerInitiation(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerInitiationPlan plan, ICharacterCareerInitiationAuthorityResolver authority,
        CancellationToken cancellationToken = default)
        => CommitCareerInitiation(lease.Stamp, plan, authority, owners, lease, cancellationToken);

    private CharacterCareerInitiationResult CommitCareerInitiation(OwnerContextStamp owner,
        CharacterCareerInitiationPlan plan, ICharacterCareerInitiationAuthorityResolver authority,
        IOwnerContextLeaseAccessor? owners, IOwnerContextLease? lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (owners is not null && !LearningOwnerCurrent5703(owners, lease, owner))
            return new(CharacterCareerInitiationOutcome.Conflict, Error: "initiation_owner_changed");
        if (!owner.IsValid || !CharacterCareerQualityTransaction.IsValidOwner(owner.Owner)
            || !CharacterCareerInitiationRules.IsValid(plan) || owner.Owner.NormalizedValue != plan.OwnerId)
            return new(CharacterCareerInitiationOutcome.Conflict, Error: "initiation_command_invalid");
        var id = new CharacterWorkspaceId(plan.WorkspaceId);
        string? path = TryGetPath(owner.Owner, id);
        if (path is null) return new(CharacterCareerInitiationOutcome.Missing);
        bool wrote = false;
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner.Owner, id, path, owners, lease, owner,
                out var delegatedLedger, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return InitiationReadFailure(read);
            var prior = CharacterCareerInitiationTransaction.Lookup(owner.Owner, saved, plan);
            if (prior.Outcome != CharacterCareerInitiationOutcome.NotFound) return prior;
            cancellationToken.ThrowIfCancellationRequested();
            // Historical receipts remain recoverable; campaign history cannot admit a fresh purchase.
            if (HasCareerInitiationCampaignAuthority(saved, delegatedLedger))
                return new(CharacterCareerInitiationOutcome.Unavailable, CurrentRevision: saved.ContentRevision,
                    Error: "initiation_campaign_authority_unavailable");
            if (saved.ContentRevision != plan.ExpectedWorkspaceRevision || saved.SavedRevision != plan.ExpectedSavedRevision
                || saved.ContentRevision != saved.SavedRevision)
                return new(CharacterCareerInitiationOutcome.Conflict, CurrentRevision: saved.ContentRevision,
                    Error: "initiation_revision_conflict");
            string? slot = ReadContinuationSlotUnderLease(path);
            string beforeDigest = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path));
            string runtime = LearningRuntime5703(CareerInitiationRuntimeFingerprint, current, slot, owner);
            using var admission = OpenInitiation5722(owner, saved, plan.Quote.Selection, runtime, authority, current);
            if (admission is null || !CharacterCareerInitiationTransaction.TryBuild(owner, saved, plan, admission,
                    runtime, out var replacement, out var entry))
                return new(CharacterCareerInitiationOutcome.Conflict, CurrentRevision: saved.ContentRevision,
                    Error: "initiation_authority_changed_or_unavailable");
            using var recovery = current is null ? OpenSavedRuleSources(owner.Owner, id, path, saved) : null;
            using var historyValidation = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
            if (!IsValidAuxiliaryState(id, entry!.Plan.TargetWorkspaceRevision, replacement!.AuxiliaryState, current?.Recovery ?? recovery))
                return new(CharacterCareerInitiationOutcome.Corrupt, Error: "initiation_auxiliary_invalid");
            var record = BuildPersistedRecord(replacement, plan.TargetWorkspaceRevision, plan.TargetSavedRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            var proposed = saved with { Document = replacement, ContentRevision = plan.TargetWorkspaceRevision,
                SavedRevision = plan.TargetSavedRevision };
            CharacterCurrentRuleEnvironmentReadmissionResult? readmission = null;
            if (current is not null)
            {
                readmission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                    _retainedCharacterQueries!, proposed, cancellationToken);
                if (readmission.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution
                    || readmission.Blockers.Count != 0)
                    throw new IOException("initiation_proposed_environment_unavailable");
                current.Recovery.RetainAdmittedCapture(new(capture, _retainedCharacterQueries!), capture.IntegrityDigest);
                var continuation = new WorkspaceContinuationSnapshot(owner.Owner.NormalizedValue,
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
            using var writeAdmission = new InitiationWriteAdmission5722(this, path, record, current, proposed, readmission);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting,
                beforeTargetReplace: () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (owners is not null && !LearningOwnerCurrent5703(owners, lease, owner)
                        || slot != ReadContinuationSlotUnderLease(path)
                        || beforeDigest != CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path))
                        || !admission.IsCurrent()) throw new IOException("initiation_authority_changed_before_replace");
                    writeAdmission.ValidateFinalFence(record);
                    cancellationToken.ThrowIfCancellationRequested();
                });
            wrote = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or OperationCanceledException)
        {
            // A throw may follow replacement. Read the durable identity once;
            // never retry a paid/mutating operation with a fresh transaction ID.
        }
        var observed = owners is not null && lease is not null
            ? ReadCareerInitiationWorkspace(owners, lease, id) : GetCore(owner.Owner, id);
        if (!observed.Success || observed.Value is not { } observedWorkspace) return InitiationReadFailure(observed);
        var recovered = CharacterCareerInitiationTransaction.Lookup(owner.Owner, observedWorkspace, plan);
        if (recovered.Outcome == CharacterCareerInitiationOutcome.Replayed && wrote)
            return recovered with { Outcome = CharacterCareerInitiationOutcome.Applied };
        return recovered.Outcome == CharacterCareerInitiationOutcome.NotFound
            ? new(CharacterCareerInitiationOutcome.Unavailable, CurrentRevision: observedWorkspace.ContentRevision,
                Error: "initiation_commit_not_observed") : recovered;
    }

    private static CharacterCareerInitiationResult InitiationReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome switch
        {
            WorkspaceOperationOutcome.Missing => CharacterCareerInitiationOutcome.Missing,
            WorkspaceOperationOutcome.Conflict => CharacterCareerInitiationOutcome.Conflict,
            WorkspaceOperationOutcome.Corrupt => CharacterCareerInitiationOutcome.Corrupt,
            _ => CharacterCareerInitiationOutcome.Unavailable
        }, Error: "initiation_workspace_unavailable");
}
