using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerGearPurchaseAtomicCommitCapability
{
    // Implemented by root only after both shared ledger guards are integrated.
    static partial void VerifyCareerGearPurchaseGuards(ref bool installed);
    public bool CareerGearPurchaseGuardsInstalled
    {
        get { bool installed = false; VerifyCareerGearPurchaseGuards(ref installed); return installed; }
    }
    public string CareerGearPurchaseRuntimeDigest => CharacterCareerGearPurchaseTransaction.Hash(
        "chummer.core.sr5-career-gear-purchase-runtime/v1\0"
        + typeof(CharacterCareerGearPurchaseRules).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(CharacterCareerGearPurchaseTransaction).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(FileWorkspaceStore).Module.ModuleVersionId.ToString("D"));

    public CharacterCareerGearPurchaseResult CommitCareerGearPurchase(OwnerScope owner,
        CharacterCareerGearPurchaseCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceResolver);
        if (!CareerGearPurchaseGuardsInstalled)
            return new(CharacterCareerGearPurchaseOutcome.Unavailable, Error: "gear_ledger_guards_not_installed");
        if (!CharacterCareerGearPurchaseTransaction.IsValidOwner(owner)
            || !CharacterCareerGearPurchaseTransaction.IsConfirmedCommand(command)
            || command.Binding.OwnerId != owner.NormalizedValue)
            return new(CharacterCareerGearPurchaseOutcome.Conflict, Error: "gear_command_invalid");
        var id = command.Request.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null) return new(CharacterCareerGearPurchaseOutcome.Missing);
        string digest = CharacterCareerGearPurchaseTransaction.CommandDigest(command);
        bool completed = false, canceled = false;
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved) return CharacterCareerGearPurchaseService.MapReadFailure(read);
            var prior = CharacterCareerGearPurchaseTransaction.Lookup(owner, saved, command.Request.OperationId, digest);
            if (prior.Outcome != CharacterCareerGearPurchaseOutcome.NotFound) return prior;
            if (cancellationToken.IsCancellationRequested)
                return new(CharacterCareerGearPurchaseOutcome.Canceled, Error: "gear_canceled_before_commit");
            if (saved.ContentRevision != command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision)
                return new(CharacterCareerGearPurchaseOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "gear_revision_conflict");
            if (!CharacterCareerGearPurchaseTransaction.TryRead(owner, saved, command.Request.SourceId, sourceResolver,
                CareerGearPurchaseRuntimeDigest, out var snapshot, out string error))
                return new(CharacterCareerGearPurchaseOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            if (!CharacterCareerGearPurchaseTransaction.TryBuild(owner, saved, snapshot!, command, out var replacement, out var receipt))
                return new(CharacterCareerGearPurchaseOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "gear_binding_conflict");
            if (!IsValidAuxiliaryState(id, receipt!.CommittedWorkspaceRevision, replacement!.AuxiliaryState))
                return new(CharacterCareerGearPurchaseOutcome.Corrupt, Error: "gear_auxiliary_invalid");
            var record = BuildPersistedRecord(replacement, receipt.CommittedWorkspaceRevision, receipt.CommittedWorkspaceRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            cancellationToken.ThrowIfCancellationRequested();
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CharacterCareerGearPurchaseTransaction.IsSourceCurrent(saved, sourceResolver, command.Request.SourceId, command.Binding))
                    throw new IOException("gear_source_changed_before_replace");
            });
            completed = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { canceled = ex is OperationCanceledException; }
        // Release the lease before a normal read reacquires it; recovery ignores cancellation.
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (!observed.Success || observed.Value is not { } current) return CharacterCareerGearPurchaseService.MapReadFailure(observed);
        var recovered = CharacterCareerGearPurchaseTransaction.Lookup(owner, current, command.Request.OperationId, digest);
        if (recovered.Outcome == CharacterCareerGearPurchaseOutcome.Replayed)
            return recovered with { Outcome = completed ? CharacterCareerGearPurchaseOutcome.Applied : CharacterCareerGearPurchaseOutcome.Replayed };
        return recovered.Outcome == CharacterCareerGearPurchaseOutcome.NotFound
            ? new(canceled ? CharacterCareerGearPurchaseOutcome.Canceled : CharacterCareerGearPurchaseOutcome.UnknownOutcome,
                CurrentWorkspaceRevision: current.ContentRevision, Error: "gear_commit_not_observed") : recovered;
    }
}
