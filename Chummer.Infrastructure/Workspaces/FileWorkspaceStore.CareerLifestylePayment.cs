using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerLifestylePaymentAtomicCommitCapability
{
    public string CareerLifestylePaymentRuntimeDigest => CharacterCareerLifestylePaymentTransaction.Hash(
        "chummer.core.sr5-lifestyle-payment-runtime/v1\0"
        + typeof(CharacterLifestyleIncrementRules).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(CharacterCareerLifestylePaymentTransaction).Module.ModuleVersionId.ToString("D") + "\0"
        + typeof(FileWorkspaceStore).Module.ModuleVersionId.ToString("D"));

    public CharacterCareerLifestylePaymentResult CommitCareerLifestylePayment(OwnerScope owner,
        CharacterCareerLifestylePaymentCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceResolver);
        if (!CharacterCareerLifestylePaymentTransaction.IsValidOwner(owner)
            || !CharacterCareerLifestylePaymentTransaction.IsConfirmedCommand(command)
            || command.Binding.OwnerId != owner.NormalizedValue)
            return new(CharacterCareerLifestylePaymentOutcome.Conflict, Error: "lifestyle_command_invalid");
        var id = command.Request.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null) return new(CharacterCareerLifestylePaymentOutcome.Missing);
        string digest = CharacterCareerLifestylePaymentTransaction.CommandDigest(command);
        bool completed = false, canceled = false;
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved) return CharacterCareerLifestylePaymentService.MapReadFailure(read);
            var prior = CharacterCareerLifestylePaymentTransaction.Lookup(owner, saved, command.Request.OperationId, digest);
            if (prior.Outcome != CharacterCareerLifestylePaymentOutcome.NotFound) return prior;
            if (cancellationToken.IsCancellationRequested)
                return new(CharacterCareerLifestylePaymentOutcome.Canceled, Error: "lifestyle_canceled_before_commit");
            if (saved.ContentRevision != command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision)
                return new(CharacterCareerLifestylePaymentOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "lifestyle_revision_conflict");
            if (!CharacterCareerLifestylePaymentTransaction.TryRead(owner, saved, command.Request.LifestyleId, sourceResolver,
                CareerLifestylePaymentRuntimeDigest, out var snapshot, out string error))
                return new(CharacterCareerLifestylePaymentOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            if (!CharacterCareerLifestylePaymentTransaction.TryBuild(owner, saved, snapshot!, command, out var replacement, out var receipt))
                return new(CharacterCareerLifestylePaymentOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "lifestyle_binding_conflict");
            if (!IsValidAuxiliaryState(id, receipt!.CommittedWorkspaceRevision, replacement!.AuxiliaryState))
                return new(CharacterCareerLifestylePaymentOutcome.Corrupt, Error: "lifestyle_auxiliary_invalid");
            var record = BuildPersistedRecord(replacement, receipt.CommittedWorkspaceRevision, receipt.CommittedWorkspaceRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            cancellationToken.ThrowIfCancellationRequested();
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CharacterCareerLifestylePaymentTransaction.IsSourceCurrent(saved, sourceResolver, command.Binding))
                    throw new IOException("lifestyle_source_changed_before_replace");
            });
            completed = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { canceled = ex is OperationCanceledException; }
        // Release the lease before a normal read reacquires it; recovery ignores cancellation.
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (!observed.Success || observed.Value is not { } current) return CharacterCareerLifestylePaymentService.MapReadFailure(observed);
        var recovered = CharacterCareerLifestylePaymentTransaction.Lookup(owner, current, command.Request.OperationId, digest);
        if (recovered.Outcome == CharacterCareerLifestylePaymentOutcome.Replayed)
            return recovered with { Outcome = completed ? CharacterCareerLifestylePaymentOutcome.Applied : CharacterCareerLifestylePaymentOutcome.Replayed };
        return recovered.Outcome == CharacterCareerLifestylePaymentOutcome.NotFound
            ? new(canceled ? CharacterCareerLifestylePaymentOutcome.Canceled : CharacterCareerLifestylePaymentOutcome.UnknownOutcome,
                CurrentWorkspaceRevision: current.ContentRevision, Error: "lifestyle_commit_not_observed") : recovered;
    }
}
