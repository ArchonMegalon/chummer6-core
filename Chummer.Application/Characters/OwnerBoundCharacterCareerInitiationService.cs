using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class OwnerBoundCharacterCareerInitiationService(IWorkspaceStore store,
    IOwnerContextAccessor owners, ICharacterCareerInitiationAuthorityResolver authority)
{
    public CharacterCareerInitiationQuote? Read(OwnerContextStamp owner, CharacterWorkspaceId workspace,
        CharacterCareerInitiationSelection selection)
    {
        if (!OwnerContextAdmission.TryAcquire(owners, owner, out var lease)) return null;
        using (lease)
        {
            try
            {
                if (store is not ICharacterCareerInitiationAtomicCommitCapability atomic) return null;
                var read = atomic.ReadCareerInitiationWorkspace(owner, workspace);
                if (!read.Success || read.Value is not { } saved
                    || !CharacterCareerInitiationTransaction.IsValidLedger(workspace, saved.ContentRevision,
                        saved.Document.AuxiliaryState.CharacterCareerInitiationTransactions)) return null;
                using var admission = authority.Open(owner, saved, selection, atomic.CareerInitiationRuntimeFingerprint);
                return admission?.IsCurrent() == true ? admission.Quote : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            { return null; }
        }
    }

    public CharacterCareerInitiationResult Commit(OwnerContextStamp owner, CharacterCareerInitiationPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (!CharacterCareerInitiationRules.IsValid(plan) || plan.OwnerId != owner.Owner.NormalizedValue
            || !OwnerContextAdmission.TryAcquire(owners, owner, out var lease))
            return new(CharacterCareerInitiationOutcome.Conflict, Error: "initiation_owner_admission_rejected");
        using (lease)
        {
            // Exact operation lookup comes BEFORE fresh revision, source, affordability or transition checks.
            // A reopened session may recover its durable result; it cannot revive an uncommitted old quote.
            var prior = Lookup(owner, plan);
            if (prior.Outcome != CharacterCareerInitiationOutcome.NotFound) return prior;
            if (owner.AuthorityInstanceId != plan.Quote.Binding.OwnerAuthorityInstanceId
                || owner.TransitionRevision != plan.Quote.Binding.OwnerTransitionRevision)
                return new(CharacterCareerInitiationOutcome.Conflict, Error: "initiation_owner_transition_changed");
            if (store is not ICharacterCareerInitiationAtomicCommitCapability atomic)
                return new(CharacterCareerInitiationOutcome.Unavailable, Error: "initiation_atomic_commit_unavailable");
            try
            {
                var result = atomic.CommitCareerInitiation(owner, plan, authority, cancellationToken);
                var observed = Lookup(owner, plan);
                if (observed.Outcome == CharacterCareerInitiationOutcome.Replayed)
                    return observed with { Outcome = result.Outcome == CharacterCareerInitiationOutcome.Applied
                        && CharacterCareerInitiationRules.Same(result.Entry, observed.Entry)
                            ? CharacterCareerInitiationOutcome.Applied : CharacterCareerInitiationOutcome.Replayed };
                if (observed.Outcome != CharacterCareerInitiationOutcome.NotFound) return observed;
                return result.Outcome is CharacterCareerInitiationOutcome.Applied or CharacterCareerInitiationOutcome.Replayed
                    ? new(CharacterCareerInitiationOutcome.Unavailable, Error: "initiation_commit_not_observed")
                    : result with { Entry = null, CurrentRevision = observed.CurrentRevision };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            {
                var observed = Lookup(owner, plan);
                return observed.Outcome == CharacterCareerInitiationOutcome.NotFound
                    ? new(CharacterCareerInitiationOutcome.Unavailable, Error: "initiation_commit_result_unknown") : observed;
            }
        }
    }

    private CharacterCareerInitiationResult Lookup(OwnerContextStamp owner, CharacterCareerInitiationPlan plan)
    {
        try
        {
            var id = new CharacterWorkspaceId(plan.WorkspaceId);
            var read = owner.Owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner.Owner, id);
            return read.Success && read.Value is { } saved
                ? CharacterCareerInitiationTransaction.Lookup(owner.Owner, saved, plan)
                : new(read.Outcome switch
                {
                    WorkspaceOperationOutcome.Missing => CharacterCareerInitiationOutcome.Missing,
                    WorkspaceOperationOutcome.Corrupt => CharacterCareerInitiationOutcome.Corrupt,
                    _ => CharacterCareerInitiationOutcome.Unavailable
                }, Error: "initiation_workspace_unavailable");
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
        { return new(CharacterCareerInitiationOutcome.Unavailable, Error: "initiation_workspace_unavailable"); }
    }
}
