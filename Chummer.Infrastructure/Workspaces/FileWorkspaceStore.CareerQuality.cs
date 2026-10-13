using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public string CareerQualityRuntimeFingerprint => CharacterCareerReputationTransaction.Hash(
        "chummer.core.sr5-career-quality-runtime/v1\0"
        + typeof(CharacterCareerQualityRules).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(CharacterCareerQualityTransaction).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(FileWorkspaceStore).Module.ModuleVersionId.ToString("D"));

    public CharacterCareerQualityCommitResult CommitCareerQuality(OwnerScope owner,
        CharacterCareerQualityPlan plan, ICharacterCareerQualityAuthorityResolver authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CharacterCareerQualityTransaction.IsValidOwner(owner)
            || !CharacterCareerQualityTransaction.IsValidPlan(plan) || owner.NormalizedValue != plan.OwnerId)
            return new(CharacterCareerQualityCommitOutcome.Conflict, Error: "quality_command_invalid");
        var id = new CharacterWorkspaceId(plan.WorkspaceId);
        string? path = TryGetPath(owner, id);
        if (path is null) return new(CharacterCareerQualityCommitOutcome.Missing);
        bool wrote = false;
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved) return QualityReadFailure(read);
            var prior = CharacterCareerQualityTransaction.Lookup(owner, saved, plan);
            if (prior.Outcome != CharacterCareerQualityCommitOutcome.NotFound) return prior;
            if (saved.ContentRevision != plan.ExpectedWorkspaceRevision || saved.SavedRevision != plan.ExpectedSavedRevision
                || saved.ContentRevision != saved.SavedRevision)
                return new(CharacterCareerQualityCommitOutcome.Conflict, CurrentRevision: saved.ContentRevision,
                    Error: "quality_revision_conflict");
            using var admission = authority.Open(owner, saved, plan.Identity, plan.Operation, CareerQualityRuntimeFingerprint);
            if (admission is null || !CharacterCareerQualityTransaction.TryBuild(owner, saved, plan, admission,
                    CareerQualityRuntimeFingerprint, out var replacement, out var entry))
                return new(CharacterCareerQualityCommitOutcome.Conflict, CurrentRevision: saved.ContentRevision,
                    Error: "quality_authority_changed_or_unavailable");
            if (!IsValidAuxiliaryState(id, entry!.Plan.TargetWorkspaceRevision, replacement!.AuxiliaryState))
                return new(CharacterCareerQualityCommitOutcome.Corrupt, Error: "quality_auxiliary_invalid");
            var record = BuildPersistedRecord(replacement, plan.TargetWorkspaceRevision, plan.TargetSavedRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            cancellationToken.ThrowIfCancellationRequested();
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting,
                beforeTargetReplace: () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!admission.IsCurrent()) throw new IOException("quality_authority_changed_before_replace");
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
        if (!observed.Success || observed.Value is not { } current) return QualityReadFailure(observed);
        var recovered = CharacterCareerQualityTransaction.Lookup(owner, current, plan);
        if (recovered.Outcome == CharacterCareerQualityCommitOutcome.Replayed && wrote)
            return recovered with { Outcome = CharacterCareerQualityCommitOutcome.Applied };
        return recovered.Outcome == CharacterCareerQualityCommitOutcome.NotFound
            ? new(CharacterCareerQualityCommitOutcome.Unavailable, CurrentRevision: current.ContentRevision,
                Error: "quality_commit_not_observed") : recovered;
    }

    private static CharacterCareerQualityCommitResult QualityReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome switch
        {
            WorkspaceOperationOutcome.Missing => CharacterCareerQualityCommitOutcome.Missing,
            WorkspaceOperationOutcome.Conflict => CharacterCareerQualityCommitOutcome.Conflict,
            WorkspaceOperationOutcome.Corrupt => CharacterCareerQualityCommitOutcome.Corrupt,
            _ => CharacterCareerQualityCommitOutcome.Unavailable
        }, Error: "quality_workspace_unavailable");
}
