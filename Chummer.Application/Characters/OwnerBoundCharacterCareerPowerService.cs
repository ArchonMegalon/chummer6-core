using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class OwnerBoundCharacterCareerPowerService(IWorkspaceStore store,
    IOwnerContextAccessor owners, ICharacterCareerPowerAuthorityResolver authority)
{
    public CharacterCareerPowerPlan? Review(OwnerContextStamp owner, CharacterWorkspaceId workspace,
        CharacterCareerPowerCommand command)
        => ReviewDetailed(owner, workspace, command).Plan;

    public CharacterCareerPowerReview ReviewDetailed(OwnerContextStamp owner, CharacterWorkspaceId workspace,
        CharacterCareerPowerCommand command)
    {
        if (!OwnerContextAdmission.TryAcquire(owners, owner, out var lease)) return new(null, "power_owner_admission_rejected");
        using (lease)
            return ReviewAdmitted(owners, lease, workspace, command);
    }

    // Synchronous composition under the caller's live lease; owner accessors need
    // not support recursive admission. Only the same authority can lend its lease.
    internal CharacterCareerPowerReview ReviewAdmitted(IOwnerContextAccessor admissionAuthority,
        IOwnerContextLease lease, CharacterWorkspaceId workspace, CharacterCareerPowerCommand command)
    {
        if (!ReferenceEquals(owners, admissionAuthority)) return new(null, "power_owner_admission_rejected");
        try
        {
            var owner = lease.Stamp;
            if (!owner.IsValid) return new(null, "power_owner_admission_rejected");
            if (store is not ICharacterCareerPowerAtomicCommitCapability atomic) return new(null, "power_atomic_commit_unavailable");
            var read = Read(owner, workspace);
            if (!read.Success || read.Value is not { } saved) return new(null, "power_workspace_unavailable");
            var binding = command.ReadBinding ?? CharacterCareerMagicReadBinding.Create(owner, saved);
            if (!CharacterCareerMagicReadBinding.MatchesOwner(binding, owner)
                || !CharacterCareerMagicReadBinding.MatchesSaved(binding, owner.Owner, saved))
                return new(null, "power_read_binding_changed");
            command = command with { ReadBinding = binding };
            using var admission = authority.OpenDetailed(owner.Owner, saved, command,
                atomic.CareerPowerRuntimeFingerprint, out var blocker);
            if (admission is null) return new(null, blocker);
            return admission.IsCurrent() && CharacterCareerPowerTransaction.IsValidPlan(admission.Plan)
                ? new(admission.Plan, null) : new(null, "power_authority_changed");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(null, "power_review_unavailable"); }
    }

    // Retain this exact plan/key across a cancelled request or restart. Lookup does
    // not require the old catalog to remain available and never submits a new spend.
    public CharacterCareerPowerResult Lookup(OwnerContextStamp owner, CharacterCareerPowerPlan plan)
    {
        if (!Admit(owner, plan, out var lease)) return new(CharacterCareerPowerOutcome.Conflict, Error: "power_owner_admission_rejected");
        using (lease) return LookupAdmitted(owner, plan);
    }

    public CharacterCareerPowerResult Commit(OwnerContextStamp owner, CharacterCareerPowerPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (!Admit(owner, plan, out var lease)) return new(CharacterCareerPowerOutcome.Conflict, Error: "power_owner_admission_rejected");
        using (lease)
        {
            var prior = LookupAdmitted(owner, plan);
            if (prior.Outcome != CharacterCareerPowerOutcome.NotFound) return prior;
            if (plan.Command.ReadBinding is not { } binding || !CharacterCareerMagicReadBinding.MatchesOwner(binding, owner))
                return new(CharacterCareerPowerOutcome.Conflict, Error: "power_read_owner_changed");
            if (store is not ICharacterCareerPowerAtomicCommitCapability atomic)
                return new(CharacterCareerPowerOutcome.Unavailable, Error: "power_atomic_commit_unavailable");
            try
            {
                var result = atomic.CommitCareerPower(owner.Owner, plan, authority, cancellationToken);
                var observed = LookupAdmitted(owner, plan);
                if (observed.Outcome == CharacterCareerPowerOutcome.Replayed)
                    return observed with { Outcome = result.Outcome == CharacterCareerPowerOutcome.Applied
                        && CharacterCareerPowerTransaction.Same(result.Entry, observed.Entry)
                        ? CharacterCareerPowerOutcome.Applied : CharacterCareerPowerOutcome.Replayed };
                return observed.Outcome != CharacterCareerPowerOutcome.NotFound ? observed
                    : result.Outcome is CharacterCareerPowerOutcome.Applied or CharacterCareerPowerOutcome.Replayed
                        ? new(CharacterCareerPowerOutcome.Unavailable, Error: "power_commit_not_observed")
                        : result with { Entry = null };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            {
                var observed = LookupAdmitted(owner, plan);
                return observed.Outcome == CharacterCareerPowerOutcome.NotFound
                    ? new(CharacterCareerPowerOutcome.Unavailable, Error: "power_commit_result_unknown") : observed;
            }
        }
    }

    private bool Admit(OwnerContextStamp owner, CharacterCareerPowerPlan plan, out IOwnerContextLease? lease)
    {
        lease = null;
        return CharacterCareerPowerTransaction.IsValidPlan(plan)
            && CharacterCareerQualityTransaction.IsValidOwner(owner.Owner)
            && plan.OwnerId == owner.Owner.NormalizedValue
            && OwnerContextAdmission.TryAcquire(owners, owner, out lease);
    }
    private WorkspaceStoreReadResult Read(OwnerContextStamp owner, CharacterWorkspaceId id) =>
        owner.Owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner.Owner, id);
    private CharacterCareerPowerResult LookupAdmitted(OwnerContextStamp owner, CharacterCareerPowerPlan plan)
    {
        try
        {
            var read = Read(owner, new(plan.WorkspaceId));
            return read.Success && read.Value is { } saved
                ? CharacterCareerPowerTransaction.Lookup(owner.Owner, saved, plan)
                : new(read.Outcome switch {
                    WorkspaceOperationOutcome.Missing => CharacterCareerPowerOutcome.Missing,
                    WorkspaceOperationOutcome.Corrupt => CharacterCareerPowerOutcome.Corrupt,
                    _ => CharacterCareerPowerOutcome.Unavailable }, Error: "power_workspace_unavailable");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(CharacterCareerPowerOutcome.Unavailable, Error: "power_workspace_unavailable"); }
    }
}
