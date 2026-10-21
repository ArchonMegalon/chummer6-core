using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public string CareerPowerRuntimeFingerprint => CharacterCareerReputationTransaction.Hash(
        "chummer.core.sr5-career-power-runtime/v1\0"
        + typeof(CharacterAdeptPowerPointRules).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(CharacterCareerPowerTransaction).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(FileWorkspaceStore).Module.ModuleVersionId.ToString("D"));

    public CharacterCareerPowerResult CommitCareerPower(OwnerScope owner,
        CharacterCareerPowerPlan plan, ICharacterCareerPowerAuthorityResolver authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CharacterCareerQualityTransaction.IsValidOwner(owner)
            || !CharacterCareerPowerTransaction.IsValidPlan(plan) || owner.NormalizedValue != plan.OwnerId)
            return new(CharacterCareerPowerOutcome.Conflict, Error: "power_command_invalid");
        var id = new CharacterWorkspaceId(plan.WorkspaceId);
        string? path = TryGetPath(owner, id);
        if (path is null) return new(CharacterCareerPowerOutcome.Missing);
        bool wrote = false;
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved) return PowerReadFailure(read);
            var prior = CharacterCareerPowerTransaction.Lookup(owner, saved, plan);
            if (prior.Outcome != CharacterCareerPowerOutcome.NotFound) return prior;
            // Recover committed receipts before denying new local campaign mutations.
            if (delegatedLedger.Count > 0)
                return new(CharacterCareerPowerOutcome.Conflict, CurrentRevision: saved.ContentRevision,
                    Error: "power_campaign_authority_unavailable");
            if (saved.ContentRevision != plan.ExpectedWorkspaceRevision || saved.SavedRevision != plan.ExpectedSavedRevision
                || saved.ContentRevision != saved.SavedRevision)
                return new(CharacterCareerPowerOutcome.Conflict, CurrentRevision: saved.ContentRevision,
                    Error: "power_revision_conflict");
            using var admission = authority.Open(owner, saved, plan.Command, CareerPowerRuntimeFingerprint);
            if (admission is null || !CharacterCareerPowerTransaction.TryBuild(owner, saved, plan, admission,
                    CareerPowerRuntimeFingerprint, out var replacement, out var entry))
                return new(CharacterCareerPowerOutcome.Conflict, CurrentRevision: saved.ContentRevision,
                    Error: "power_authority_changed_or_unavailable");
            using var recovery = OpenSavedRuleSources(owner, id, path, saved);
            using var historyValidation = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
            if (!IsValidAuxiliaryState(id, entry!.Plan.TargetWorkspaceRevision, replacement!.AuxiliaryState, recovery))
                return new(CharacterCareerPowerOutcome.Corrupt, Error: "power_auxiliary_invalid");
            var record = BuildPersistedRecord(replacement, plan.TargetWorkspaceRevision, plan.TargetSavedRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            cancellationToken.ThrowIfCancellationRequested();
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting,
                beforeTargetReplace: () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!admission.IsCurrent()) throw new IOException("power_authority_changed_before_replace");
                });
            wrote = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or OperationCanceledException)
        {
            // A throw may follow replacement. Read the durable identity once;
            // never retry a paid/mutating operation with a fresh transaction ID.
        }
        var observed = GetCore(owner, id);
        if (!observed.Success || observed.Value is not { } current) return PowerReadFailure(observed);
        var recovered = CharacterCareerPowerTransaction.Lookup(owner, current, plan);
        if (recovered.Outcome == CharacterCareerPowerOutcome.Replayed && wrote)
            return recovered with { Outcome = CharacterCareerPowerOutcome.Applied };
        return recovered.Outcome == CharacterCareerPowerOutcome.NotFound
            ? new(CharacterCareerPowerOutcome.Unavailable, CurrentRevision: current.ContentRevision,
                Error: "power_commit_not_observed") : recovered;
    }

    private static CharacterCareerPowerResult PowerReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome switch
        {
            WorkspaceOperationOutcome.Missing => CharacterCareerPowerOutcome.Missing,
            WorkspaceOperationOutcome.Conflict => CharacterCareerPowerOutcome.Conflict,
            WorkspaceOperationOutcome.Corrupt => CharacterCareerPowerOutcome.Corrupt,
            _ => CharacterCareerPowerOutcome.Unavailable
        }, Error: "power_workspace_unavailable");
}
