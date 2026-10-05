using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public enum WorkspaceLocalAdoptionOutcome
{
    Available, Applied, Recovered, Rejected, Conflict, Unavailable, Canceled, RecoveryRequired
}

/// <summary>Store-local ownership handoff, never portable restore authority.</summary>
public sealed record WorkspaceLocalAdoptionReceipt(
    Guid OperationId, string OwnerId, CharacterWorkspaceId WorkspaceId,
    string IncarnationId, string LocalSnapshotDigest, string AccountSnapshotDigest,
    long ContentRevision, long SavedRevision, DateTimeOffset ClaimedAtUtc)
{
    public bool IsValid(long currentRevision) => OperationId != Guid.Empty
        && !string.IsNullOrWhiteSpace(OwnerId)
        && OwnerId == new OwnerScope(OwnerId).NormalizedValue
        && !new OwnerScope(OwnerId).UsesLocalSingleUserValue
        && CharacterCreationIdIsValid(WorkspaceId)
        && Guid.TryParseExact(IncarnationId, "N", out var incarnation) && incarnation != Guid.Empty
        && incarnation.ToString("N") == IncarnationId
        && DelegatedGmCharacterEditLedgerValidator.IsSha256(LocalSnapshotDigest)
        && DelegatedGmCharacterEditLedgerValidator.IsSha256(AccountSnapshotDigest)
        && ContentRevision > 0 && ContentRevision <= currentRevision
        && SavedRevision >= 0 && SavedRevision <= ContentRevision
        && ClaimedAtUtc != default && ClaimedAtUtc.Offset == TimeSpan.Zero;

    private static bool CharacterCreationIdIsValid(CharacterWorkspaceId id) =>
        !string.IsNullOrWhiteSpace(id.Value)
        && id.Value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
}

public sealed record WorkspaceLocalAdoptionResult(WorkspaceLocalAdoptionOutcome Outcome,
    WorkspaceLocalAdoptionReceipt? Receipt = null);

/// <summary>A review issued from the actual local store, not caller-supplied JSON.</summary>
public sealed class WorkspaceLocalAdoptionReview
{
    private int _used;
    internal object Issuer { get; }
    internal OwnerContextStamp Owner { get; }
    internal WorkspaceContinuationRestoreTarget Source { get; }
    internal WorkspaceContinuationRestoreTarget Target { get; }
    internal WorkspaceLocalAdoptionReceipt Receipt { get; }
    internal DateTimeOffset ExpiresAtUtc { get; }

    public CharacterWorkspaceId WorkspaceId => Receipt.WorkspaceId;
    public Guid OperationId => Receipt.OperationId;

    internal WorkspaceLocalAdoptionReview(object issuer, OwnerContextStamp owner,
        WorkspaceContinuationRestoreTarget source, WorkspaceContinuationRestoreTarget target,
        WorkspaceLocalAdoptionReceipt receipt, DateTimeOffset expiresAtUtc)
    {
        Issuer = issuer; Owner = owner; Source = source; Target = target;
        Receipt = receipt; ExpiresAtUtc = expiresAtUtc;
    }

    internal bool TryConsume() => Interlocked.Exchange(ref _used, 1) == 0;
}

public sealed class WorkspaceLocalAdoptionAdmission
{
    private readonly Action _finalFence;
    public OwnerContextStamp Owner { get; }
    public WorkspaceContinuationRestoreTarget Source { get; }
    public WorkspaceContinuationRestoreTarget Target { get; }
    public WorkspaceLocalAdoptionReceipt Receipt { get; }

    internal WorkspaceLocalAdoptionAdmission(WorkspaceLocalAdoptionReview review, Action finalFence)
    {
        Owner = review.Owner; Source = review.Source; Target = review.Target;
        Receipt = review.Receipt; _finalFence = finalFence;
    }

    public void ValidateFinalFence() => _finalFence();
}

public interface IWorkspaceLocalAdoptionCapability
{
    WorkspaceLocalAdoptionResult AdoptLocalWorkspace(WorkspaceLocalAdoptionAdmission admission);
    WorkspaceLocalAdoptionResult RecoverLocalWorkspaceAdoption(
        OwnerScope owner, CharacterWorkspaceId id, Guid operationId);
}

/// <summary>
/// Explicitly claims an unowned runner on this installation for the current
/// account. This is not account-to-account transfer, restore, upload or a rules edit.
/// </summary>
public sealed class WorkspaceLocalAdoptionService(
    IWorkspaceStore store, IOwnerContextAccessor ownerContext, TimeProvider? timeProvider = null)
{
    private readonly object _issuer = new();
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public WorkspaceLocalAdoptionReview? Review(OwnerContextStamp owner, CharacterWorkspaceId id)
    {
        if (!IsAccount(owner.Owner)
            || store is not IWorkspaceLocalAdoptionCapability
            || store is not IWorkspaceContinuationReadCapability { SupportsWorkspaceContinuationRead: true } reader
            || store is not IWorkspaceContinuationRestoreCapability { SupportsWorkspaceContinuationRestore: true } targets
            || !OwnerContextAdmission.TryAcquire(ownerContext, owner, out var lease))
            return null;
        using (lease)
        {
            var source = targets.InspectRestoreTarget(OwnerScope.LocalSingleUser, id);
            var target = targets.InspectRestoreTarget(owner.Owner, id);
            var read = reader.ReadContinuation(id);
            if (source.Outcome != WorkspaceContinuationRestoreOutcome.Available
                || source.Target is not { Exists: true, IncarnationId: not null } sourceIdentity
                || target.Outcome != WorkspaceContinuationRestoreOutcome.Available
                || target.Target is not { Exists: false } targetIdentity
                || !read.Success || read.Value is not { } local || local.Workspace.Id != id
                || WorkspaceContinuationSnapshotDigest.Compute(local) != sourceIdentity.SnapshotDigest
                || !WorkspaceContinuationHistoryIntegrity.TryValidate(OwnerScope.LocalSingleUser, local))
                return null;

            var account = local with { OwnerId = owner.Owner.NormalizedValue };
            // Historical account-specific permissions cannot be rewritten as if
            // the new owner had issued them. Preserve every accepted history byte.
            if (!WorkspaceContinuationHistoryIntegrity.TryValidate(owner.Owner, account)) return null;
            DateTimeOffset now = _clock.GetUtcNow();
            var receipt = new WorkspaceLocalAdoptionReceipt(Guid.NewGuid(), owner.Owner.NormalizedValue,
                id, sourceIdentity.IncarnationId, sourceIdentity.SnapshotDigest!,
                WorkspaceContinuationSnapshotDigest.Compute(account), local.Workspace.ContentRevision,
                local.Workspace.SavedRevision, now);
            return new(_issuer, owner, sourceIdentity, targetIdentity, receipt, now.AddMinutes(5));
        }
    }

    public WorkspaceLocalAdoptionResult Confirm(OwnerContextStamp owner,
        WorkspaceLocalAdoptionReview review, bool explicitlyConfirmed, CancellationToken ct = default)
    {
        if (!explicitlyConfirmed || review is null || !ReferenceEquals(review.Issuer, _issuer)
            || review.Owner != owner || !IsAccount(owner.Owner)
            || store is not IWorkspaceLocalAdoptionCapability capability
            || !OwnerContextAdmission.TryAcquire(ownerContext, owner, out var lease))
            return new(WorkspaceLocalAdoptionOutcome.Rejected);
        using (lease)
        {
            if (ct.IsCancellationRequested) return new(WorkspaceLocalAdoptionOutcome.Canceled);
            if (_clock.GetUtcNow() >= review.ExpiresAtUtc || !review.TryConsume())
                return new(WorkspaceLocalAdoptionOutcome.Rejected);
            return capability.AdoptLocalWorkspace(new(review, () =>
            {
                ct.ThrowIfCancellationRequested();
                if (_clock.GetUtcNow() >= review.ExpiresAtUtc)
                    throw new InvalidOperationException("Local runner adoption review expired.");
            }));
        }
    }

    public WorkspaceLocalAdoptionResult Recover(OwnerContextStamp owner, CharacterWorkspaceId id, Guid operationId)
    {
        if (!IsAccount(owner.Owner) || operationId == Guid.Empty
            || store is not IWorkspaceLocalAdoptionCapability capability
            || !OwnerContextAdmission.TryAcquire(ownerContext, owner, out var lease))
            return new(WorkspaceLocalAdoptionOutcome.Rejected);
        using (lease) return capability.RecoverLocalWorkspaceAdoption(owner.Owner, id, operationId);
    }

    private static bool IsAccount(OwnerScope owner) => !string.IsNullOrWhiteSpace(owner.NormalizedValue)
        && !owner.UsesLocalSingleUserValue;
}
