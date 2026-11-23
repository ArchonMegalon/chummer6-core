using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerLifestyleMaintenanceAtomicCommitCapability
{
    public string CareerLifestyleMaintenanceRuntimeDigest => CharacterCareerLifestyleMaintenanceTransaction.Hash(
        "chummer.core.sr5-lifestyle-maintenance-runtime/v1\0"
        + typeof(CharacterCreationLifestylesRules).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(CharacterCareerLifestyleMaintenanceTransaction).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(FileWorkspaceStore).Module.ModuleVersionId.ToString("D"));

    public CharacterCareerLifestyleMaintenanceResult CommitCareerLifestyleMaintenance(OwnerScope owner,
        CharacterCareerLifestyleMaintenanceCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceResolver);
        if (!CharacterCareerLifestyleMaintenanceTransaction.IsValidOwner(owner)
            || !CharacterCareerLifestyleMaintenanceTransaction.IsConfirmedCommand(command)
            || command.Binding.OwnerId != owner.NormalizedValue)
            return new(CharacterCareerLifestyleMaintenanceOutcome.Conflict, Error: "lifestyle_maintenance_command_invalid");
        var id = command.Request.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null) return new(CharacterCareerLifestyleMaintenanceOutcome.Missing);
        bool completed = false, canceled = false;
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved) return CharacterCareerLifestyleMaintenanceService.MapReadFailure(read);
            var prior = CharacterCareerLifestyleMaintenanceTransaction.Lookup(owner, saved, command);
            if (prior.Outcome != CharacterCareerLifestyleMaintenanceOutcome.NotFound) return prior;
            if (cancellationToken.IsCancellationRequested)
                return new(CharacterCareerLifestyleMaintenanceOutcome.Canceled, Error: "lifestyle_maintenance_canceled_before_commit");
            if (saved.ContentRevision != command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision)
                return new(CharacterCareerLifestyleMaintenanceOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "lifestyle_maintenance_revision_conflict");
            if (!CharacterCareerLifestyleMaintenanceTransaction.TryRead(owner, saved, command.Request, sourceResolver,
                CareerLifestyleMaintenanceRuntimeDigest, out var snapshot, out string error))
                return new(CharacterCareerLifestyleMaintenanceOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            if (!CharacterCareerLifestyleMaintenanceTransaction.TryBuild(owner, saved, snapshot!, command, out var replacement, out var receipt))
                return new(CharacterCareerLifestyleMaintenanceOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "lifestyle_maintenance_binding_conflict");
            if (!IsValidAuxiliaryState(id, receipt!.CommittedWorkspaceRevision, replacement!.AuxiliaryState))
                return new(CharacterCareerLifestyleMaintenanceOutcome.Corrupt, Error: "lifestyle_maintenance_auxiliary_invalid");
            var record = BuildPersistedRecord(replacement, receipt.CommittedWorkspaceRevision, receipt.CommittedWorkspaceRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            cancellationToken.ThrowIfCancellationRequested();
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CharacterCareerLifestyleMaintenanceTransaction.IsSourceCurrent(saved, sourceResolver, command.Binding))
                    throw new IOException("lifestyle_maintenance_source_changed_before_replace");
            });
            completed = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { canceled = ex is OperationCanceledException; }
        // Release the lease before a normal read reacquires it; recovery ignores cancellation.
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (!observed.Success || observed.Value is not { } current) return CharacterCareerLifestyleMaintenanceService.MapReadFailure(observed);
        var recovered = CharacterCareerLifestyleMaintenanceTransaction.Lookup(owner, current, command);
        if (recovered.Outcome == CharacterCareerLifestyleMaintenanceOutcome.Replayed)
            return recovered with { Outcome = completed ? CharacterCareerLifestyleMaintenanceOutcome.Applied : CharacterCareerLifestyleMaintenanceOutcome.Replayed };
        return recovered.Outcome == CharacterCareerLifestyleMaintenanceOutcome.NotFound
            ? new(canceled ? CharacterCareerLifestyleMaintenanceOutcome.Canceled : CharacterCareerLifestyleMaintenanceOutcome.UnknownOutcome,
                CurrentWorkspaceRevision: current.ContentRevision, Error: "lifestyle_maintenance_commit_not_observed") : recovered;
    }
}
