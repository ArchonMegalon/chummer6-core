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
    {
        ArgumentNullException.ThrowIfNull(authority);
        cancellationToken.ThrowIfCancellationRequested();
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
            var read = ReadWorkspaceUnderLease(owner.Owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved) return InitiationReadFailure(read);
            var prior = CharacterCareerInitiationTransaction.Lookup(owner.Owner, saved, plan);
            if (prior.Outcome != CharacterCareerInitiationOutcome.NotFound) return prior;
            // Historical receipts remain recoverable; campaign history cannot admit a fresh purchase.
            if (HasCareerInitiationCampaignAuthority(saved, delegatedLedger))
                return new(CharacterCareerInitiationOutcome.Unavailable, CurrentRevision: saved.ContentRevision,
                    Error: "initiation_campaign_authority_unavailable");
            if (saved.ContentRevision != plan.ExpectedWorkspaceRevision || saved.SavedRevision != plan.ExpectedSavedRevision
                || saved.ContentRevision != saved.SavedRevision)
                return new(CharacterCareerInitiationOutcome.Conflict, CurrentRevision: saved.ContentRevision,
                    Error: "initiation_revision_conflict");
            using var admission = authority.Open(owner, saved, plan.Quote.Selection, CareerInitiationRuntimeFingerprint);
            if (admission is null || !CharacterCareerInitiationTransaction.TryBuild(owner, saved, plan, admission,
                    CareerInitiationRuntimeFingerprint, out var replacement, out var entry))
                return new(CharacterCareerInitiationOutcome.Conflict, CurrentRevision: saved.ContentRevision,
                    Error: "initiation_authority_changed_or_unavailable");
            using var recovery = OpenSavedRuleSources(owner.Owner, id, path, saved);
            using var historyValidation = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
            if (!IsValidAuxiliaryState(id, entry!.Plan.TargetWorkspaceRevision, replacement!.AuxiliaryState, recovery))
                return new(CharacterCareerInitiationOutcome.Corrupt, Error: "initiation_auxiliary_invalid");
            var record = BuildPersistedRecord(replacement, plan.TargetWorkspaceRevision, plan.TargetSavedRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            cancellationToken.ThrowIfCancellationRequested();
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting,
                beforeTargetReplace: () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!admission.IsCurrent()) throw new IOException("initiation_authority_changed_before_replace");
                });
            wrote = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or OperationCanceledException)
        {
            // A throw may follow replacement. Read the durable identity once;
            // never retry a paid/mutating operation with a fresh transaction ID.
        }
        var observed = GetCore(owner.Owner, id);
        if (!observed.Success || observed.Value is not { } current) return InitiationReadFailure(observed);
        var recovered = CharacterCareerInitiationTransaction.Lookup(owner.Owner, current, plan);
        if (recovered.Outcome == CharacterCareerInitiationOutcome.Replayed && wrote)
            return recovered with { Outcome = CharacterCareerInitiationOutcome.Applied };
        return recovered.Outcome == CharacterCareerInitiationOutcome.NotFound
            ? new(CharacterCareerInitiationOutcome.Unavailable, CurrentRevision: current.ContentRevision,
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
