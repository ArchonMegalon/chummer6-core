using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Synchronous admission; no owner lease crosses an await. The caller captures
/// the full stamp before background dispatch. Stable owner-name equality cannot
/// revive an A → B → A operation. No fallback to the anonymous local partition.
/// </summary>
public sealed class OwnerBoundCharacterCareerQualityService(IWorkspaceStore store,
    IOwnerContextAccessor ownerContext, ICharacterCareerQualityAuthorityResolver authority)
{
    public CharacterCareerQualityCommitResult Commit(OwnerContextStamp expectedOwner,
        CharacterCareerQualityPlan plan, CancellationToken cancellationToken = default)
    {
        if (!CharacterCareerQualityTransaction.IsValidPlan(plan)
            || !CharacterCareerQualityTransaction.IsValidOwner(expectedOwner.Owner)
            || plan.OwnerId != expectedOwner.Owner.NormalizedValue
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCareerQualityCommitOutcome.Conflict, Error: "quality_owner_admission_rejected");
        using (lease)
        {
            var prior = LookupAdmitted(expectedOwner, plan);
            if (prior.Outcome != CharacterCareerQualityCommitOutcome.NotFound) return prior;
            if (store is not ICharacterCareerQualityAtomicCommitCapability atomic)
                return new(CharacterCareerQualityCommitOutcome.Unavailable, Error: "quality_atomic_commit_unavailable");
            try
            {
                var result = atomic.CommitCareerQuality(expectedOwner.Owner, plan, authority, cancellationToken);
                var readback = LookupAdmitted(expectedOwner, plan);
                if (readback.Outcome == CharacterCareerQualityCommitOutcome.Replayed)
                    return readback with { Outcome = result.Outcome == CharacterCareerQualityCommitOutcome.Applied
                        && JsonSerializer.Serialize(result.Entry) == JsonSerializer.Serialize(readback.Entry)
                        ? CharacterCareerQualityCommitOutcome.Applied : CharacterCareerQualityCommitOutcome.Replayed };
                if (readback.Outcome != CharacterCareerQualityCommitOutcome.NotFound) return readback;
                return result.Outcome is CharacterCareerQualityCommitOutcome.Applied or CharacterCareerQualityCommitOutcome.Replayed
                    ? new(CharacterCareerQualityCommitOutcome.Unavailable, Error: "quality_commit_not_observed")
                    : result with { Entry = null, CurrentRevision = readback.CurrentRevision };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidOperationException or OperationCanceledException)
            {
                var readback = LookupAdmitted(expectedOwner, plan);
                return readback.Outcome == CharacterCareerQualityCommitOutcome.NotFound
                    ? new(CharacterCareerQualityCommitOutcome.Unavailable, Error: "quality_commit_result_unknown") : readback;
            }
        }
    }

    private CharacterCareerQualityCommitResult LookupAdmitted(OwnerContextStamp owner, CharacterCareerQualityPlan plan)
    {
        var id = new CharacterWorkspaceId(plan.WorkspaceId);
        try
        {
            var read = owner.Owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner.Owner, id);
            if (!read.Success || read.Value is not { } saved)
                return new(read.Outcome switch
                {
                    WorkspaceOperationOutcome.Missing => CharacterCareerQualityCommitOutcome.Missing,
                    WorkspaceOperationOutcome.Corrupt => CharacterCareerQualityCommitOutcome.Corrupt,
                    _ => CharacterCareerQualityCommitOutcome.Unavailable
                }, Error: "quality_workspace_unavailable");
            return CharacterCareerQualityTransaction.Lookup(owner.Owner, saved, plan);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(CharacterCareerQualityCommitOutcome.Unavailable, Error: "quality_workspace_unavailable"); }
    }
}
