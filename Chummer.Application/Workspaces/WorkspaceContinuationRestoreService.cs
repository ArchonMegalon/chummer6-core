using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// An ephemeral, single-use review owned by one service instance. It is deliberately
/// not a serializable restore command. Hosts exposing HTTP must retain this object
/// behind their own opaque review reference rather than accepting its fields back.
/// After any attempted confirmation, recovery is lookup-only. A new review gets a
/// fresh operation identity; retrying a lost create cannot resurrect a deleted runner.
/// </summary>
public sealed class WorkspaceContinuationRestoreReview : IDisposable
{
    private byte[]? _bytes;
    internal object Issuer { get; }
    internal OwnerContextStamp Owner { get; }
    public Guid OperationId { get; }
    public string? AdmissionDigest { get; }
    public string? SnapshotDigest { get; }
    public string? SourceDigest { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public WorkspaceContinuationRestoreResult Result { get; }

    internal WorkspaceContinuationRestoreReview(object issuer, OwnerContextStamp owner,
        byte[]? bytes, Guid operationId, string? admissionDigest, string? snapshotDigest,
        string? sourceDigest, DateTimeOffset expiresAtUtc, WorkspaceContinuationRestoreResult result)
    {
        Issuer = issuer; Owner = owner; _bytes = bytes; OperationId = operationId;
        AdmissionDigest = admissionDigest; SnapshotDigest = snapshotDigest;
        SourceDigest = sourceDigest; ExpiresAtUtc = expiresAtUtc; Result = result;
    }

    internal byte[]? Consume(object issuer) => ReferenceEquals(Issuer, issuer)
        ? Interlocked.Exchange(ref _bytes, null) : null;

    public void Dispose()
    {
        byte[]? bytes = Interlocked.Exchange(ref _bytes, null);
        if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
    }
}

/// <summary>
/// Store-facing Core permit. Construction is internal; invocation is single-use
/// and bound to a live owner lease. No caller-supplied callback can authorize a
/// replacement. Dispose/expiry revoke the permit even if a store retained it.
/// </summary>
public sealed class WorkspaceContinuationRestoreAdmission : IDisposable
{
    private byte[]? _bytes;
    private readonly int _maximumBytes;
    private readonly IOwnerContextLease _lease;
    private readonly Func<bool> _sourcesCurrent;
    private readonly TimeProvider _clock;
    private readonly CancellationToken _cancellation;
    private readonly DateTimeOffset _expires;
    private int _disposed;
    public OwnerContextStamp Owner { get; }
    public WorkspaceContinuationRestoreTarget Target { get; }
    public WorkspaceContinuationRestoreReceipt Receipt { get; }

    internal WorkspaceContinuationRestoreAdmission(WorkspaceContinuationRestoreReview review,
        byte[] bytes, int maximumBytes, IOwnerContextLease lease, Func<bool> sourcesCurrent,
        TimeProvider clock, CancellationToken cancellation, long contentRevision, long savedRevision)
    {
        _bytes = bytes.ToArray(); _maximumBytes = maximumBytes; _lease = lease;
        _sourcesCurrent = sourcesCurrent; _clock = clock; _cancellation = cancellation;
        _expires = review.ExpiresAtUtc; Owner = review.Owner; Target = review.Result.Target!;
        Receipt = new(review.OperationId, review.AdmissionDigest!, review.SnapshotDigest!,
            review.SourceDigest!, Guid.NewGuid().ToString("N"), contentRevision, savedRevision,
            clock.GetUtcNow());
    }

    public bool TryOpenSnapshot(out WorkspaceContinuationSnapshot? snapshot)
    {
        snapshot = null;
        byte[]? bytes = Interlocked.Exchange(ref _bytes, null);
        if (bytes is null) return false;
        try
        {
            EnsureLive();
            if (!WorkspaceContinuationCodec.TryDecodeCandidate(bytes, _maximumBytes, out var candidate)
                || candidate.SnapshotDigest != Receipt.SnapshotDigest
                || candidate.Snapshot.OwnerId != Owner.Owner.NormalizedValue
                || candidate.Snapshot.Workspace.Id != Target.WorkspaceId)
                return false;
            snapshot = candidate.Snapshot;
            return true;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void ValidateFinalFence()
    {
        EnsureLive();
        if (!_sourcesCurrent()) throw new InvalidOperationException("continuation-sources-changed");
        EnsureLive();
    }

    private void EnsureLive()
    {
        _cancellation.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) != 0 || _lease.Stamp != Owner
            || _clock.GetUtcNow() >= _expires)
            throw new InvalidOperationException("continuation-admission-no-longer-live");
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        byte[]? bytes = Interlocked.Exchange(ref _bytes, null);
        if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
    }
}

public sealed class WorkspaceContinuationRestoreService
{
    private readonly IWorkspaceStore _store;
    private readonly IOwnerContextAccessor _owners;
    private readonly ICharacterSourceDataResolver _sources;
    private readonly ILifeModulesCatalogService _lifeModules;
    private readonly WorkspaceContinuationCandidateEvaluator _evaluator;
    private readonly int _maximumBytes;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _reviewLifetime;
    private readonly object _issuer = new();

    public WorkspaceContinuationRestoreService(IWorkspaceStore store, IOwnerContextAccessor owners,
        ICharacterSourceDataResolver sources, ICharacterFileQueries queries,
        ILifeModulesCatalogService lifeModules, int maximumBytes,
        TimeProvider? clock = null, TimeSpan? reviewLifetime = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        _store = store; _owners = owners; _sources = sources; _lifeModules = lifeModules;
        _evaluator = new(owners, sources, queries, lifeModules); _maximumBytes = maximumBytes;
        _clock = clock ?? TimeProvider.System;
        _reviewLifetime = reviewLifetime ?? TimeSpan.FromMinutes(5);
        if (_reviewLifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(reviewLifetime));
    }

    public WorkspaceContinuationRestoreReview Review(OwnerContextStamp owner, ReadOnlyMemory<byte> bytes)
    {
        DateTimeOffset expires = _clock.GetUtcNow() + _reviewLifetime;
        if (bytes.IsEmpty || bytes.Length > _maximumBytes)
            return Denied("continuation-wire-size-invalid");
        byte[] captured = bytes.ToArray();
        bool transferred = false;
        try
        {
            // Evaluator owns its own lease. Reacquire afterward, never nest leases
            // on an accessor whose exclusion need not be reentrant.
            var evaluated = _evaluator.Evaluate(owner, captured, _maximumBytes);
            if (!evaluated.CurrentDraftChecksPassed || evaluated.Candidate is null)
                return Denied("continuation-candidate-rejected");
            var candidate = evaluated.Candidate;
            if (candidate.Snapshot.Workspace.LastUpdatedUtc.Offset != TimeSpan.Zero)
                return Denied("continuation-timestamp-representation-unsupported");
            if (_store is not IWorkspaceContinuationRestoreCapability { SupportsWorkspaceContinuationRestore: true } capability
                || !OwnerContextAdmission.TryAcquire(_owners, owner, out var lease))
                return Denied("continuation-restore-authority-unavailable");
            using (lease)
            {
                var target = capability.InspectRestoreTarget(owner.Owner, candidate.Snapshot.Workspace.Id);
                if (target.Outcome != WorkspaceContinuationRestoreOutcome.Available || target.Target is null)
                    return Denied("continuation-target-unavailable", target);
                if (target.Target.Exists && target.Target.SnapshotDigest == candidate.SnapshotDigest)
                    return Denied("continuation-already-current",
                        target with { Outcome = WorkspaceContinuationRestoreOutcome.AlreadyCurrent });
                if (target.Target.Exists && candidate.Snapshot.Workspace.ContentRevision <= target.Target.ContentRevision)
                    return Denied("continuation-target-revision-conflict",
                        target with { Outcome = WorkspaceContinuationRestoreOutcome.Conflict });
                if (!SourcesMatch(candidate.Snapshot.Workspace.Document.Content, evaluated.SourceDigest!))
                    return Denied("continuation-sources-changed");
                Guid operation = Guid.NewGuid();
                string digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                    new { Contract = "chummer.local-continuation-restore-admission/v1", Operation = operation,
                        Owner = owner, Target = target.Target, Candidate = candidate.SnapshotDigest,
                        Source = evaluated.SourceDigest })));
                transferred = true;
                return new(_issuer, owner, captured, operation, digest, candidate.SnapshotDigest,
                    evaluated.SourceDigest, expires, target);
            }
        }
        finally { if (!transferred) CryptographicOperations.ZeroMemory(captured); }

        WorkspaceContinuationRestoreReview Denied(string blocker, WorkspaceContinuationRestoreResult? result = null)
            => new(_issuer, owner, null, Guid.Empty, null, null, null, expires,
                result is null ? new(WorkspaceContinuationRestoreOutcome.Rejected, Blockers: [blocker])
                    : result with { Blockers = [blocker] });
    }

    public WorkspaceContinuationRestoreResult Confirm(WorkspaceContinuationRestoreReview review,
        bool explicitlyConfirmed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!explicitlyConfirmed) return new(WorkspaceContinuationRestoreOutcome.Rejected,
            Blockers: ["explicit-review-required"]);
        byte[]? captured = review.Consume(_issuer);
        if (captured is null) return new(WorkspaceContinuationRestoreOutcome.ReviewConsumed);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_clock.GetUtcNow() >= review.ExpiresAtUtc)
                return new(WorkspaceContinuationRestoreOutcome.ReviewExpired);
            // Recompute all present drafts at confirmation; a review's hashes
            // are not authority for a changed rule environment.
            var evaluated = _evaluator.Evaluate(review.Owner, captured, _maximumBytes);
            if (!evaluated.CurrentDraftChecksPassed || evaluated.Candidate is null
                || evaluated.Candidate.SnapshotDigest != review.SnapshotDigest
                || evaluated.SourceDigest != review.SourceDigest)
                return new(WorkspaceContinuationRestoreOutcome.Rejected, Blockers: ["continuation-review-stale"]);
            if (_store is not IWorkspaceContinuationRestoreCapability { SupportsWorkspaceContinuationRestore: true } capability
                || !OwnerContextAdmission.TryAcquire(_owners, review.Owner, out var lease))
                return new(WorkspaceContinuationRestoreOutcome.Unavailable);
            using (lease)
            using (var admission = new WorkspaceContinuationRestoreAdmission(review, captured, _maximumBytes, lease,
                       () => SourcesMatch(evaluated.Candidate.Snapshot.Workspace.Document.Content, review.SourceDigest!),
                       _clock, cancellationToken, evaluated.Candidate.Snapshot.Workspace.ContentRevision,
                       evaluated.Candidate.Snapshot.Workspace.SavedRevision))
                return capability.RestoreContinuation(admission);
        }
        catch (OperationCanceledException) { return new(WorkspaceContinuationRestoreOutcome.Canceled); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // No automatic second write. An unobserved result is recovered only
            // through the local operation receipt, even if the target is missing.
            return new(WorkspaceContinuationRestoreOutcome.Unavailable, Blockers: ["restore-outcome-requires-recovery"]);
        }
        finally { CryptographicOperations.ZeroMemory(captured); }
    }

    public WorkspaceContinuationRestoreResult Recover(OwnerContextStamp owner, CharacterWorkspaceId id,
        Guid operationId, string admissionDigest)
    {
        if (_store is not IWorkspaceContinuationRestoreCapability { SupportsWorkspaceContinuationRestore: true } capability
            || operationId == Guid.Empty || !DelegatedGmCharacterEditLedgerValidator.IsSha256(admissionDigest)
            || !OwnerContextAdmission.TryAcquire(_owners, owner, out var lease))
            return new(WorkspaceContinuationRestoreOutcome.Unavailable);
        using (lease) return capability.RecoverContinuationRestore(owner.Owner, id, operationId, admissionDigest);
    }

    private bool SourcesMatch(string xml, string expectedDigest)
    {
        try
        {
            ICharacterSourceDataContext? current = _sources.TryCreateContext(xml);
            return current is not null && WorkspaceContinuationSourceCapture.TryCapture(current, xml, _lifeModules,
                out var observed) && observed.Digest == expectedDigest;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException
            or UnauthorizedAccessException or System.Xml.XmlException or JsonException or FormatException
            or OverflowException or NullReferenceException or NotSupportedException) { return false; }
    }
}
