using System.Security.Cryptography;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

// Privately issued alongside the exact two canonically admitted branches.
internal sealed class PrivateRuleUpdateOriginal(byte[] bytes, WorkspacePrivateRuleSourceClosureBinding binding, DateTimeOffset lastUpdatedUtc) : IDisposable
{
    internal const string Decision = "preserve-local-and-adopt-incoming/v1";
    internal byte[] Bytes { get; } = bytes;
    internal WorkspacePrivateRuleSourceClosureBinding Binding { get; } = binding;
    internal DateTimeOffset LastUpdatedUtc { get; } = lastUpdatedUtc;
    public void Dispose() => CryptographicOperations.ZeroMemory(Bytes);
}

public sealed partial class WorkspacePrivateRuleSourceClosureRestoreService
{
    /// <summary>Explicit branch resolution, separate from default portable restore.
    /// No ancestry is asserted. Both original identities remain unchanged.</summary>
    public WorkspacePrivateRuleSourceClosureRestoreReview ReviewUpdate(OwnerContextStamp owner,
        ReadOnlyMemory<byte> bytes, WorkspacePrivateRuleSourceClosureBinding expected,
        ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default)
        => ReviewUpdateCore(owner, bytes, expected, sources, null, cancellationToken);

    private WorkspacePrivateRuleSourceClosureRestoreReview ReviewUpdateCore(OwnerContextStamp owner,
        ReadOnlyMemory<byte> bytes, WorkspacePrivateRuleSourceClosureBinding expected,
        ICharacterSourceDataResolver sources, Guid? alternativeOperation, CancellationToken cancellationToken,
        WorkspacePrivateRuleSourceClosureBinding? authoredBaseline = null, PrivateAuthoredRuleSourceFence? authoredSource = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var expires = _clock.GetUtcNow() + _lifetime;
        byte[]? incomingBytes = null;
        byte[]? originalBytes = null;
        bool issued = false;
        PrivateRuleResolution? resolution = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_store.CanUseRuleUpdateDurability()) return Denied("rule-update-durability-unavailable");
            if (expected.OwnerId != owner.Owner.NormalizedValue || bytes.IsEmpty
                || bytes.Length > WorkspacePrivateRuleSourceClosureBinding.MaximumBytes
                || !TryAcquire(owner, out var firstLease)) return Denied("rule-update-owner-unavailable");
            FileWorkspaceStore.PrivateClosureRestoreTarget before;
            using (firstLease) before = _store.InspectPrivateClosureRestoreTarget(Normalize(owner.Owner), expected.WorkspaceId);
            if (before.Result.Target is not { Exists: true } || !before.HasCurrentEnvironment)
                return Denied("rule-update-pinned-current-environment-required");

            // Export owns its own synchronous owner and workspace leases. Never nest owner leases.
            var original = _store.ExportCurrentRuleEnvironmentClosure(_owners, owner, expected.WorkspaceId,
                WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, sources, cancellationToken);
            if (authoredBaseline is not null && original.Binding != authoredBaseline)
                return Denied("rule-update-authoring-baseline-changed");
            authoredSource?.Validate(cancellationToken);
            originalBytes = original.ExportPrivateBytes();
            incomingBytes = bytes.ToArray();
            if (!TryAcquire(owner, out var lease)) return Denied("rule-update-owner-changed");
            using (lease)
            {
                using var semantics = new RestoreSemanticOperation3287(lease!, owner, _clock, expires, cancellationToken);
                var observed = _store.InspectPrivateClosureRestoreTarget(Normalize(owner.Owner), expected.WorkspaceId);
                if (observed.Result.Outcome != WorkspaceContinuationRestoreOutcome.Available
                    || observed.Result.Target != before.Result.Target || observed.RecordDigest != before.RecordDigest
                    || original.Binding.ContinuationDigest != observed.Result.Target!.SnapshotDigest)
                    return Denied("rule-update-current-changed");
                var current = WorkspacePrivateRuleSourceClosure.DecodeForRestore(originalBytes, original.Binding, _queries, cancellationToken);
                var incoming = WorkspacePrivateRuleSourceClosure.DecodeForRestore(incomingBytes, expected, _queries, cancellationToken);
                if (current.CurrentEnvironment is null || incoming.CurrentEnvironment is null
                    || !PrivateCurrentRuleEnvironmentAdmission.TryAdmit(current, _queries, cancellationToken, out var oldAdmission, out _, semantics, RestoreSemanticRole3287.Original)
                    || oldAdmission is null
                    || !PrivateCurrentRuleEnvironmentAdmission.TryAdmit(incoming, _queries, cancellationToken, out var newAdmission, out _, semantics)
                    || newAdmission is null) return Denied("rule-update-canonical-evaluation-unavailable");
                var calculated = newAdmission.CompareCalculatedUpdateFrom(oldAdmission, cancellationToken);
                if (calculated is null) return Denied("rule-update-calculated-observation-unavailable");
                string coverageExplanation = calculated.Changes.Count == 0
                    ? "The available calculated values match. Review the coverage and reasons for both branches; unknown sections are not unchanged totals."
                    : "Review the calculated changes and the coverage and reasons for both branches; unavailable values are not zero.";
                var preview = new WorkspaceRuleUpdatePreview(current.CurrentEnvironment, incoming.CurrentEnvironment,
                    calculated.SourceChanges,
                    "The local and incoming runners may have been edited independently. A higher revision can still contain different edits. "
                    + "Keeping the current runner leaves its rules and drafts unchanged. Updating preserves its exact private branch first, then adopts the incoming character and rules together. "
                    + coverageExplanation)
                {
                    Calculated = calculated.Display
                };
                if (alternativeOperation.HasValue || expected.ContentRevision <= observed.Result.Target.ContentRevision)
                {
                    resolution = PrivateRuleResolution.Prepare(incoming, observed.Result.Target,
                        _clock.GetUtcNow(), _store, _queries, cancellationToken, semantics);
                    preview = preview with
                    {
                        NewLocalRevision = resolution.Binding.ContentRevision,
                        RestoresSavedAlternative = alternativeOperation.HasValue,
                        Explanation = "Use this complete saved branch as a new change to the current runner? "
                            + "Its original character history and private rules stay intact. The current branch is preserved first. "
                            + "A new local revision is saved; this does not rewind history or reuse an earlier restore. "
                            + coverageExplanation
                    };
                }
                if (lease!.Stamp != owner || _clock.GetUtcNow() >= expires) return Denied("rule-update-review-expired");
                oldAdmission.Revalidate(cancellationToken);
                newAdmission.Revalidate(cancellationToken);
                authoredSource?.Validate(cancellationToken);
                if (lease.Stamp != owner || _clock.GetUtcNow() >= expires)
                    return Denied("rule-update-review-expired");
                cancellationToken.ThrowIfCancellationRequested();
                Guid operation = Guid.NewGuid();
                // Restore receipt admission IDs use bare lowercase SHA-256; preserve all hashed contract fields.
                string digest = ComputeRuleUpdateReviewDigest(operation, owner, observed.Result.Target,
                    observed.RecordDigest, expected, original.Binding, incomingBytes, originalBytes,
                    preview, resolution?.Binding, alternativeOperation);
                issued = true;
                return new(_issuer, owner, incomingBytes, expected, expires, observed.Result,
                    observed.RecordDigest, operation, digest)
                {
                    UpdatePreview = preview,
                    AuthoredSource = authoredSource,
                    LocalResolution = resolution,
                    AlternativeOperation = alternativeOperation,
                    UpdateOriginal = new(originalBytes, original.Binding, current.Snapshot.Workspace.LastUpdatedUtc)
                };
            }
        }
        catch (OperationCanceledException) { return Denied("rule-update-canceled"); }
        catch (Exception error) when (IsFailure(error)) { return Denied("rule-update-unavailable"); }
        finally
        {
            if (!issued) resolution?.Dispose();
            if (!issued && incomingBytes is not null) CryptographicOperations.ZeroMemory(incomingBytes);
            if (!issued && originalBytes is not null) CryptographicOperations.ZeroMemory(originalBytes);
        }

        WorkspacePrivateRuleSourceClosureRestoreReview Denied(string blocker, WorkspaceRuleUpdatePreview? preview = null)
            => new(_issuer, owner, null, expected, expires,
                new(WorkspaceContinuationRestoreOutcome.Conflict, Blockers: [blocker])) { UpdatePreview = preview };
    }

    public IReadOnlyList<WorkspaceRuleUpdateAlternative> ListUpdateAlternatives(OwnerContextStamp owner, CharacterWorkspaceId id)
    {
        if (!TryAcquire(owner, out var lease)) throw WorkspacePrivateRuleSourceClosure.Rejected();
        using (lease) return _store.ListRuleUpdateAlternatives(Normalize(owner.Owner), id);
    }

    public WorkspacePrivateRuleSourceClosureRestoreReview ReviewUpdateAlternative(OwnerContextStamp owner,
        CharacterWorkspaceId id, Guid originalOperation, ICharacterSourceDataResolver sources, CancellationToken ct = default)
    {
        byte[] bytes;
        WorkspacePrivateRuleSourceClosureBinding binding;
        if (!TryAcquire(owner, out var lease)) throw WorkspacePrivateRuleSourceClosure.Rejected();
        using (lease) (bytes, binding) = _store.ReadRuleUpdateAlternative(Normalize(owner.Owner), id, originalOperation);
        try { return ReviewUpdateCore(owner, bytes, binding, sources, originalOperation, ct); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    // The same existing review contract binds both original byte streams and the complete
    // display. Confirmation compares to this service-issued digest, never a caller digest.
    private static string ComputeRuleUpdateReviewDigest(Guid operation, OwnerContextStamp owner,
        WorkspaceContinuationRestoreTarget target, string? recordDigest,
        WorkspacePrivateRuleSourceClosureBinding incoming, WorkspacePrivateRuleSourceClosureBinding original,
        ReadOnlySpan<byte> incomingBytes, ReadOnlySpan<byte> originalBytes, WorkspaceRuleUpdatePreview preview,
        WorkspacePrivateRuleSourceClosureBinding? localResolution, Guid? alternativeOperation)
        => CharacterCreationFinalizationDigest.Compute(new
        {
            Contract = "chummer.reviewed-rule-update/v1", Operation = operation, Owner = owner,
            Target = target, RecordDigest = recordDigest, Incoming = incoming,
            Original = original, Decision = PrivateRuleUpdateOriginal.Decision,
            IncomingBytes = Convert.ToHexStringLower(SHA256.HashData(incomingBytes)),
            OriginalBytes = Convert.ToHexStringLower(SHA256.HashData(originalBytes)), Preview = preview,
            LocalResolution = localResolution, AlternativeOperation = alternativeOperation
        })["sha256:".Length..];
}
