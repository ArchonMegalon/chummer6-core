using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerCustomDrugPurchaseAtomicCommitCapability
{
    // Implemented by root only after both shared ledger guards are integrated.
    static partial void VerifyCareerCustomDrugPurchaseGuards(ref bool installed);
    public bool CareerCustomDrugPurchaseGuardsInstalled
    {
        get { bool installed = false; VerifyCareerCustomDrugPurchaseGuards(ref installed); return installed; }
    }
    public string CareerCustomDrugPurchaseRuntimeDigest => CharacterCareerCustomDrugPurchaseTransaction.Hash(
        "chummer.core.sr5-career-custom-drug-purchase-runtime/v1\0"
        + typeof(CharacterCustomDrugRules).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(CharacterCareerCustomDrugPurchaseTransaction).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(FileWorkspaceStore).Module.ModuleVersionId.ToString("D"));

    public CharacterCareerCustomDrugPurchaseResult CommitCareerCustomDrugPurchase(OwnerScope owner,
        CharacterCareerCustomDrugPurchaseCommand command, ICharacterSourceDataResolver sourceResolver,
        ICharacterCareerCustomDrugPurchaseComponentValidator componentValidator, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceResolver);
        ArgumentNullException.ThrowIfNull(componentValidator);
        if (!CareerCustomDrugPurchaseGuardsInstalled)
            return new(CharacterCareerCustomDrugPurchaseOutcome.Unavailable, Error: "custom_drug_ledger_guards_not_installed");
        if (!CharacterCareerCustomDrugPurchaseTransaction.IsValidOwner(owner)
            || !CharacterCareerCustomDrugPurchaseTransaction.IsConfirmedCommand(command)
            || command.Binding.OwnerId != owner.NormalizedValue)
            return new(CharacterCareerCustomDrugPurchaseOutcome.Conflict, Error: "custom_drug_command_invalid");
        var id = command.Request.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null) return new(CharacterCareerCustomDrugPurchaseOutcome.Missing);
        string digest = CharacterCareerCustomDrugPurchaseTransaction.CommandDigest(command);
        bool completed = false, canceled = false;
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved) return CharacterCareerCustomDrugPurchaseService.MapReadFailure(read);
            var prior = CharacterCareerCustomDrugPurchaseTransaction.Lookup(owner, saved, command.Request.OperationId, digest);
            if (prior.Outcome != CharacterCareerCustomDrugPurchaseOutcome.NotFound) return prior;
            if (cancellationToken.IsCancellationRequested)
                return new(CharacterCareerCustomDrugPurchaseOutcome.Canceled, Error: "custom_drug_canceled_before_commit");
            if (saved.ContentRevision != command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision)
                return new(CharacterCareerCustomDrugPurchaseOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "custom_drug_revision_conflict");
            if (!CharacterCareerCustomDrugPurchaseTransaction.TryRead(owner, saved, command.Request.TargetDrugId, sourceResolver, componentValidator,
                CareerCustomDrugPurchaseRuntimeDigest, out var snapshot, out string error))
                return new(CharacterCareerCustomDrugPurchaseOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            if (!CharacterCareerCustomDrugPurchaseTransaction.TryBuild(owner, saved, snapshot!, command, out var replacement, out var receipt))
                return new(CharacterCareerCustomDrugPurchaseOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "custom_drug_binding_conflict");
            if (!IsValidAuxiliaryState(id, receipt!.CommittedWorkspaceRevision, replacement!.AuxiliaryState))
                return new(CharacterCareerCustomDrugPurchaseOutcome.Corrupt, Error: "custom_drug_auxiliary_invalid");
            var record = BuildPersistedRecord(replacement, receipt.CommittedWorkspaceRevision, receipt.CommittedWorkspaceRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            cancellationToken.ThrowIfCancellationRequested();
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CharacterCareerCustomDrugPurchaseTransaction.IsSourceCurrent(owner, saved, sourceResolver, componentValidator, command.Request.TargetDrugId, command.Binding, CareerCustomDrugPurchaseRuntimeDigest))
                    throw new IOException("custom_drug_source_changed_before_replace");
                // Source revalidation can do I/O. Honor cancellation received during it
                // before returning to the writer's atomic replacement.
                cancellationToken.ThrowIfCancellationRequested();
            });
            completed = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { canceled = ex is OperationCanceledException; }
        // Release the lease before a normal read reacquires it; recovery ignores cancellation.
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (!observed.Success || observed.Value is not { } current) return CharacterCareerCustomDrugPurchaseService.MapReadFailure(observed);
        var recovered = CharacterCareerCustomDrugPurchaseTransaction.Lookup(owner, current, command.Request.OperationId, digest);
        if (recovered.Outcome == CharacterCareerCustomDrugPurchaseOutcome.Replayed)
            return recovered with { Outcome = completed ? CharacterCareerCustomDrugPurchaseOutcome.Applied : CharacterCareerCustomDrugPurchaseOutcome.Replayed };
        return recovered.Outcome == CharacterCareerCustomDrugPurchaseOutcome.NotFound
            ? new(canceled ? CharacterCareerCustomDrugPurchaseOutcome.Canceled : CharacterCareerCustomDrugPurchaseOutcome.UnknownOutcome,
                CurrentWorkspaceRevision: current.ContentRevision, Error: "custom_drug_commit_not_observed") : recovered;
    }
}
