using System.Security.Cryptography;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

/// <summary>
/// Shared producer/consumer binding for account roaming. Authentication, durable
/// original publication and remote CAS remain the transport's responsibility.
/// No network dispatch, source-directory inference or new persistence occurs here.
/// </summary>
public sealed class WorkspaceRoamingRuleEnvironmentBridge(
    FileWorkspaceStore store, IOwnerContextAccessor owners,
    ICharacterSourceDataResolver selectedInstallation, ICharacterFileQueries queries)
{
    private readonly WorkspacePrivateRuleSourceClosureRestoreService _restore = new(store, owners, queries);

    public WorkspacePrivateRuleSourceClosureRestoreReview ReviewLocalCapture(OwnerContextStamp owner,
        WorkspaceRoamingRuleEnvironmentExport exported, CancellationToken cancellationToken = default)
        => _restore.ReviewLocalCapture(owner, exported.Bytes, exported.Snapshot.RuleEnvironment.Binding, cancellationToken);

    public WorkspaceContinuationRestoreResult ConfirmLocalCapture(WorkspacePrivateRuleSourceClosureRestoreReview review,
        CancellationToken cancellationToken = default) => _restore.Confirm(review, true, cancellationToken);

    public WorkspaceRoamingRuleEnvironmentExport Export(OwnerContextStamp owner,
        CharacterWorkspaceId workspaceId, CancellationToken cancellationToken = default)
    {
        // Core holds owner + workspace leases and prefers the persisted capture.
        // Only a previously uncaptured runner consults this selected installation.
        var closure = store.ExportCurrentRuleEnvironmentClosure(owners, owner, workspaceId,
            WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, selectedInstallation,
            Chummer5aLegacyImportCompatibility.ProfileV2, cancellationToken);
        var observation = closure.InspectForExport(cancellationToken);
        if (observation.Environment is null)
            throw new InvalidDataException("The selected runner has no usable retained rule environment. Select its original installation and review its settings and amendments.");
        var continuation = observation.Continuation;
        byte[] bytes = closure.ExportPrivateBytes();
        try
        {
            var transfer = new WorkspaceRuleEnvironmentTransfer(WorkspaceRuleEnvironmentTransfer.SchemaV1,
                closure.Binding, bytes.Length);
            var pointer = WorkspaceRoamingRuleEnvironmentSnapshot.From(continuation, transfer);
            if (!pointer.Matches(owner.Owner.NormalizedValue, continuation) || !transfer.MatchesBytes(bytes))
                throw new InvalidDataException("The source capture changed while preparing roaming.");
            return new(pointer, continuation, bytes);
        }
        catch { CryptographicOperations.ZeroMemory(bytes); throw; }
    }

    public WorkspaceRoamingRuleEnvironmentReview Review(OwnerContextStamp owner,
        WorkspaceRoamingRuleEnvironmentSelection selected, ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default)
    {
        if (!owner.IsValid || !selected.IsValid(owner.Owner.NormalizedValue)
            || !selected.Snapshot.RuleEnvironment.MatchesBytes(bytes.Span))
            throw new InvalidDataException("The selected online rules are incomplete or belong to another runner or account.");
        byte[] captured = bytes.ToArray();
        try
        {
            var closure = WorkspacePrivateRuleSourceClosure.Import(captured, selected.Snapshot.RuleEnvironment.Binding,
                WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, queries, cancellationToken);
            if (closure.InspectCurrentEnvironment() is null
                || !selected.Snapshot.Matches(owner.Owner.NormalizedValue, closure.ExportContinuation()))
                throw new InvalidDataException("The online character, revision and retained sources do not agree.");
            // A conflict stays a conflict. The existing explicit rule-update UI can
            // separately offer ReviewUpdate; background roaming cannot select it.
            var core = _restore.ReviewRoaming(owner, captured, selected.Snapshot.RuleEnvironment.Binding, cancellationToken);
            return new(this, owner, selected, core);
        }
        finally { CryptographicOperations.ZeroMemory(captured); }
    }

    /// <summary>
    /// The caller must reread selected through its authenticated transport before
    /// calling Confirm. Core then revalidates the original local target atomically.
    /// A changed remote selection consumes this review without changing local data.
    /// </summary>
    public WorkspaceContinuationRestoreResult Confirm(OwnerContextStamp owner,
        WorkspaceRoamingRuleEnvironmentReview review, WorkspaceRoamingRuleEnvironmentSelection currentRemote,
        bool explicitlyConfirmed, CancellationToken cancellationToken = default)
    {
        if (!ReferenceEquals(review.Issuer, this) || review.Owner != owner)
            return new(WorkspaceContinuationRestoreOutcome.Rejected, Blockers: ["roaming-source-owner-changed"]);
        if (!currentRemote.IsValid(owner.Owner.NormalizedValue) || currentRemote != review.Selected)
        {
            review.Dispose();
            return new(WorkspaceContinuationRestoreOutcome.Conflict, Blockers: ["roaming-source-remote-revision-changed"]);
        }
        return _restore.Confirm(review.Core, explicitlyConfirmed, cancellationToken);
    }

    public WorkspaceContinuationRestoreResult Recover(OwnerContextStamp owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string admissionDigest)
        => _restore.Recover(owner, workspaceId, operationId, admissionDigest);
}

public sealed class WorkspaceRoamingRuleEnvironmentExport(
    WorkspaceRoamingRuleEnvironmentSnapshot snapshot, WorkspaceContinuationExport continuation, byte[] bytes) : IDisposable
{
    public WorkspaceRoamingRuleEnvironmentSnapshot Snapshot { get; } = snapshot;
    public WorkspaceContinuationExport Continuation { get; } = continuation;
    public byte[] Bytes { get; } = bytes;
    public void Dispose() => CryptographicOperations.ZeroMemory(Bytes);
    public override string ToString() => nameof(WorkspaceRoamingRuleEnvironmentExport);
}

public sealed class WorkspaceRoamingRuleEnvironmentReview : IDisposable
{
    internal WorkspaceRoamingRuleEnvironmentBridge Issuer { get; }
    internal OwnerContextStamp Owner { get; }
    internal WorkspacePrivateRuleSourceClosureRestoreReview Core { get; }
    public WorkspaceRoamingRuleEnvironmentSelection Selected { get; }
    public WorkspaceContinuationRestoreResult Result => Core.Result;
    public Guid OperationId => Core.OperationId;
    public string? AdmissionDigest => Core.AdmissionDigest;
    internal WorkspaceRoamingRuleEnvironmentReview(WorkspaceRoamingRuleEnvironmentBridge issuer,
        OwnerContextStamp owner, WorkspaceRoamingRuleEnvironmentSelection selected,
        WorkspacePrivateRuleSourceClosureRestoreReview core)
        => (Issuer, Owner, Selected, Core) = (issuer, owner, selected, core);
    public void Dispose() => Core.Dispose();
    public override string ToString() => nameof(WorkspaceRoamingRuleEnvironmentReview);
}
