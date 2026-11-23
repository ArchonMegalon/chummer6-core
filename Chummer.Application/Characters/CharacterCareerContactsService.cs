using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCareerContactsService(
    IWorkspaceStore store, ICharacterSourceDataResolver sourceResolver, IOwnerContextAccessor? ownerContext) : ICharacterCareerContactsService
{
    private readonly IWorkspaceStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ICharacterSourceDataResolver _sources = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));

    private readonly IOwnerContextLeaseAccessor? owners = ownerContext as IOwnerContextLeaseAccessor;

    // The actual shared owner authority is optional only for the ordinary, unbound route.
    // No authority is synthesized from the caller's owner value or a retained document.
    public CharacterCareerContactsService(IWorkspaceStore store, ICharacterSourceDataResolver sourceResolver)
        : this(store, sourceResolver, null) { }

    public WorkspaceStoreReadResult Read(OwnerScope owner, CharacterWorkspaceId workspaceId)
        => ReadStore(owner, workspaceId);

    public WorkspaceStoreReadResult Read(OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId)
        => ReadStore(expectedOwner.Owner, workspaceId, expectedOwner);

    public CharacterCareerContactsPreviewResult Preview(OwnerContextStamp expectedOwner, CharacterCareerContactsRequest request)
        => PreviewCore(expectedOwner.Owner, request, expectedOwner);

    public CharacterCareerContactsResult Commit(OwnerContextStamp expectedOwner, CharacterCareerContactsCommand command,
        CancellationToken cancellationToken = default)
    {
        if (owners is null || _store is not ICharacterCareerContactsCurrentEnvironmentCapability)
            return new(CharacterCareerContactsOutcome.Unavailable, Error: "contact_owner_bound_service_unavailable");
        if (!Acquire(expectedOwner.Owner, out var lease, expectedOwner))
            return new(CharacterCareerContactsOutcome.Conflict, Error: "contact_owner_changed");
        // One synchronous service-owned lease covers prior Lookup, the only dispatch,
        // and exact outcome recovery. No caller lease is accepted or reacquired.
        using (lease) return CommitCore(expectedOwner.Owner, command, cancellationToken, lease);
    }

    public CharacterCareerContactsResult Lookup(OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest)
        => LookupCore(expectedOwner.Owner, workspaceId, operationId, commandDigest, expectedOwner);

    private bool Acquire(OwnerScope owner, out IOwnerContextLease? lease, OwnerContextStamp? expectedOwner = null)
    {
        lease = null;
        if (owners is null) return false;
        var stamp = expectedOwner ?? owners.Capture();
        if (!stamp.IsValid || stamp.Owner != owner || owners.Capture() != stamp
            || !owners.TryAcquire(stamp, out lease)) return false;
        if (lease.Stamp == stamp && owners.Capture() == stamp) return true;
        lease.Dispose(); lease = null; return false;
    }

    public CharacterCareerContactsPreviewResult Preview(OwnerScope owner, CharacterCareerContactsRequest request)
        => PreviewCore(owner, request);

    private CharacterCareerContactsPreviewResult PreviewCore(OwnerScope owner, CharacterCareerContactsRequest request,
        OwnerContextStamp? expectedOwner = null)
    {
        if (expectedOwner is not null && (owners is null || _store is not ICharacterCareerContactsCurrentEnvironmentCapability))
            return new(CharacterCareerContactsOutcome.Unavailable, Error: "contact_owner_bound_service_unavailable");
        if (!CharacterCareerContactsTransaction.IsValidOwner(owner)
            || !CharacterCareerContactsTransaction.IsValidRequest(request))
            return new(CharacterCareerContactsOutcome.Conflict, Error: "contact_request_invalid");
        if (_store is not ICharacterCareerContactsAtomicCommitCapability atomic)
            return new(CharacterCareerContactsOutcome.Unavailable, Error: "contact_atomic_commit_unavailable");
        if (!atomic.CareerContactsGuardsInstalled)
            return new(CharacterCareerContactsOutcome.Unavailable, Error: "contact_ledger_guards_not_installed");
        if (owners is not null && _store is ICharacterCareerContactsCurrentEnvironmentCapability current)
        {
            if (!Acquire(owner, out var lease, expectedOwner)) return new(CharacterCareerContactsOutcome.Conflict, Error: "contact_owner_changed");
            using (lease) return current.PreviewCareerContacts(owners, lease!, request, _sources);
        }
        var read = ReadStore(owner, request.WorkspaceId);
        if (!read.Success || read.Value is not { } saved)
        { var failure = MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
        if (!CharacterCareerContactsTransaction.IsValidHistory(saved))
            return new(CharacterCareerContactsOutcome.Corrupt, Error: "contact_history_invalid");
        // Do not silently quote a second edit for an already-used operation.
        if (saved.Document.AuxiliaryState.CharacterCareerContactsReceipts?
            .Any(row => row.Command.Request.OperationId == request.OperationId) == true)
            return new(CharacterCareerContactsOutcome.IdempotencyConflict, Error: "contact_operation_already_used");
        if (!CharacterCareerContactsTransaction.TryRead(owner, saved, request.Change, _sources,
            atomic.CareerContactsRuntimeDigest, out var snapshot, out string error)
            || !CharacterCareerContactsTransaction.TryPreview(snapshot!, request, out var preview, out error))
            return new(CharacterCareerContactsOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
        // Check receipt capacity and XML edit feasibility without persisting or claiming confirmation.
        if (!CharacterCareerContactsTransaction.TryBuild(owner, saved, snapshot!,
            preview!.Command with { ExplicitlyConfirmed = true }, out _, out _))
            return new(CharacterCareerContactsOutcome.Unavailable, Error: "contact_edit_unavailable");
        return new(CharacterCareerContactsOutcome.Available, preview, saved.ContentRevision);
    }

    public CharacterCareerContactsResult Commit(OwnerScope owner, CharacterCareerContactsCommand command,
        CancellationToken cancellationToken = default)
        => CommitCore(owner, command, cancellationToken);

    private CharacterCareerContactsResult CommitCore(OwnerScope owner, CharacterCareerContactsCommand command,
        CancellationToken cancellationToken, IOwnerContextLease? heldLease = null)
    {
        if (!CharacterCareerContactsTransaction.IsValidOwner(owner)
            || !CharacterCareerContactsTransaction.IsConfirmedCommand(command)
            || command.Binding.OwnerId != owner.NormalizedValue)
            return new(CharacterCareerContactsOutcome.Conflict, Error: "contact_confirmation_invalid");
        string digest = CharacterCareerContactsTransaction.CommandDigest(command);
        // Reconciliation is read-only and remains possible even if a token was canceled after a previous commit.
        var prior = LookupCore(owner, command.Request.WorkspaceId, command.Request.OperationId, digest, heldLease: heldLease);
        if (prior.Outcome != CharacterCareerContactsOutcome.NotFound) return prior;
        if (cancellationToken.IsCancellationRequested)
            return new(CharacterCareerContactsOutcome.Canceled, Error: "contact_canceled_before_commit");
        if (_store is not ICharacterCareerContactsAtomicCommitCapability atomic)
            return new(CharacterCareerContactsOutcome.Unavailable, Error: "contact_atomic_commit_unavailable");
        if (!atomic.CareerContactsGuardsInstalled)
            return new(CharacterCareerContactsOutcome.Unavailable, Error: "contact_ledger_guards_not_installed");
        CharacterCareerContactsResult? result = null;
        bool canceled = false;
        try
        {
            if (heldLease is not null && owners is not null
                && _store is ICharacterCareerContactsCurrentEnvironmentCapability heldCurrent)
                result = heldCurrent.CommitCareerContacts(owners, heldLease, command, _sources, cancellationToken);
            else if (owners is not null && _store is ICharacterCareerContactsCurrentEnvironmentCapability current)
            {
                if (!Acquire(owner, out var lease)) return new(CharacterCareerContactsOutcome.Conflict, Error: "contact_owner_changed");
                using (lease) result = current.CommitCareerContacts(owners, lease!, command, _sources, cancellationToken);
            }
            else result = atomic.CommitCareerContacts(owner, command, _sources, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { canceled = ex is OperationCanceledException; }
        // A store/adapter's success is not a durable receipt. Never compensate with a generic save.
        var recovered = LookupCore(owner, command.Request.WorkspaceId, command.Request.OperationId, digest, heldLease: heldLease);
        if (recovered.Outcome == CharacterCareerContactsOutcome.Replayed)
            return recovered with { Outcome = result?.Outcome == CharacterCareerContactsOutcome.Applied
                ? CharacterCareerContactsOutcome.Applied : CharacterCareerContactsOutcome.Replayed };
        if (recovered.Outcome is CharacterCareerContactsOutcome.Unavailable or CharacterCareerContactsOutcome.Missing)
            return new(CharacterCareerContactsOutcome.UnknownOutcome, Error: recovered.Error ?? "contact_commit_result_unknown");
        if (recovered.Outcome != CharacterCareerContactsOutcome.NotFound) return recovered;
        if (result is { Outcome: CharacterCareerContactsOutcome.Conflict or CharacterCareerContactsOutcome.Corrupt
            or CharacterCareerContactsOutcome.IdempotencyConflict or CharacterCareerContactsOutcome.Canceled }) return result;
        return new(canceled ? CharacterCareerContactsOutcome.Canceled : CharacterCareerContactsOutcome.UnknownOutcome,
            CurrentWorkspaceRevision: recovered.CurrentWorkspaceRevision, Error: "contact_commit_not_observed");
    }

    public CharacterCareerContactsResult Lookup(OwnerScope owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest)
        => LookupCore(owner, workspaceId, operationId, commandDigest);

    private CharacterCareerContactsResult LookupCore(OwnerScope owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest, OwnerContextStamp? expectedOwner = null, IOwnerContextLease? heldLease = null)
    {
        if (operationId == Guid.Empty || !CharacterCareerContactsTransaction.IsDigest(commandDigest))
            return new(CharacterCareerContactsOutcome.Conflict, Error: "contact_lookup_invalid");
        var read = ReadStore(owner, workspaceId, expectedOwner, heldLease);
        return read.Success && read.Value is { } saved
            ? CharacterCareerContactsTransaction.Lookup(owner, saved, operationId, commandDigest) : MapReadFailure(read);
    }

    private WorkspaceStoreReadResult ReadStore(OwnerScope owner, CharacterWorkspaceId id,
        OwnerContextStamp? expectedOwner = null, IOwnerContextLease? heldLease = null)
    {
        if (expectedOwner is not null && (owners is null || _store is not ICharacterCareerContactsCurrentEnvironmentCapability))
            return new(WorkspaceOperationOutcome.Unavailable, Error: "contact_owner_bound_service_unavailable");
        if (!CharacterCareerContactsTransaction.IsValidOwner(owner)
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id))
            return new(WorkspaceOperationOutcome.Corrupt, Error: "contact_workspace_identity_invalid");
        try
        {
            WorkspaceStoreReadResult read;
            if (owners is not null && _store is ICharacterCareerContactsCurrentEnvironmentCapability current)
            {
                if (heldLease is not null)
                {
                    if (heldLease.Stamp.Owner != owner || owners.Capture() != heldLease.Stamp)
                        return new(WorkspaceOperationOutcome.Conflict, Error: "contact_owner_changed");
                    read = current.ReadCareerContactsWorkspace(owners, heldLease, id);
                }
                else
                {
                    if (!Acquire(owner, out var lease, expectedOwner))
                        return new(WorkspaceOperationOutcome.Conflict, Error: "contact_owner_changed");
                    using (lease) read = current.ReadCareerContactsWorkspace(owners, lease!, id);
                }
            }
            else read = owner.IsLocalSingleUser ? _store.Get(id) : _store.Get(owner, id);
            return read.Success && (read.Value is null || read.Value.Id != id || read.Value.ContentRevision <= 0
                || read.Value.SavedRevision < 0 || read.Value.SavedRevision > read.Value.ContentRevision)
                ? new(WorkspaceOperationOutcome.Corrupt, Error: "contact_workspace_identity_mismatch") : read;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(WorkspaceOperationOutcome.Unavailable, Error: "contact_workspace_unavailable"); }
    }

    public static CharacterCareerContactsResult MapReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome switch
        {
            WorkspaceOperationOutcome.Missing => CharacterCareerContactsOutcome.Missing,
            WorkspaceOperationOutcome.Conflict => CharacterCareerContactsOutcome.Conflict,
            WorkspaceOperationOutcome.Corrupt => CharacterCareerContactsOutcome.Corrupt,
            _ => CharacterCareerContactsOutcome.Unavailable
        }, Error: read.Error);
}
