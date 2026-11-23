using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCareerGearPurchaseService(
    IWorkspaceStore store, ICharacterSourceDataResolver sourceResolver) : ICharacterCareerGearPurchaseService
{
    private readonly IWorkspaceStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ICharacterSourceDataResolver _sources = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));

    public CharacterCareerGearPurchaseCatalogResult QueryCatalog(OwnerScope owner, CharacterCareerGearPurchaseCatalogQuery query)
    {
        if (!CharacterCareerGearPurchaseTransaction.IsValidOwner(owner) || query is null
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(query.WorkspaceId)
            || query.ExpectedWorkspaceRevision is <= 0 or long.MaxValue)
            return new(CharacterCareerGearPurchaseOutcome.Conflict, null, null, ["gear_catalog_query_invalid"]);
        if (_store is not ICharacterCareerGearPurchaseAtomicCommitCapability atomic || !atomic.CareerGearPurchaseGuardsInstalled)
            return new(CharacterCareerGearPurchaseOutcome.Unavailable, null, null, ["gear_atomic_commit_unavailable"]);
        var read = ReadStore(owner, query.WorkspaceId);
        if (!read.Success || read.Value is not { } saved)
        {
            var failure = MapReadFailure(read);
            return new(failure.Outcome, null, failure.CurrentWorkspaceRevision, [failure.Error ?? "gear_workspace_unavailable"]);
        }
        if (saved.ContentRevision != query.ExpectedWorkspaceRevision)
            return new(CharacterCareerGearPurchaseOutcome.Conflict, null, saved.ContentRevision, ["gear_catalog_revision_conflict"]);
        if (!CharacterCareerGearPurchaseTransaction.IsValidHistory(saved))
            return new(CharacterCareerGearPurchaseOutcome.Corrupt, null, saved.ContentRevision, ["gear_history_invalid"]);
        if (!CharacterCareerGearPurchaseTransaction.TryReadCatalog(owner, saved, _sources, atomic.CareerGearPurchaseRuntimeDigest,
            out var catalog, out string error))
            return new(CharacterCareerGearPurchaseOutcome.Unavailable, null, saved.ContentRevision, [error]);
        var after = ReadStore(owner, query.WorkspaceId);
        if (!after.Success || after.Value is not { } current)
        {
            var failure = MapReadFailure(after);
            return new(failure.Outcome, null, failure.CurrentWorkspaceRevision, [failure.Error ?? "gear_workspace_unavailable"]);
        }
        if (!CharacterCareerGearPurchaseTransaction.IsWorkspaceCurrent(current, catalog!.Snapshot)
            || !atomic.CareerGearPurchaseGuardsInstalled || atomic.CareerGearPurchaseRuntimeDigest != catalog.Snapshot.RuntimeDigest)
            return new(CharacterCareerGearPurchaseOutcome.Conflict, null, current.ContentRevision, ["gear_catalog_snapshot_changed"]);
        return new(CharacterCareerGearPurchaseOutcome.Available, catalog, saved.ContentRevision, []);
    }

    public CharacterCareerGearPurchasePreviewResult Preview(OwnerScope owner, CharacterCareerGearPurchaseRequest request,
        CharacterCareerGearPurchaseCatalogSelection selection)
        => selection is null ? new(CharacterCareerGearPurchaseOutcome.Conflict, Error: "gear_catalog_selection_invalid")
            : PreviewCore(owner, request, selection);

    public CharacterCareerGearPurchasePreviewResult Preview(OwnerScope owner, CharacterCareerGearPurchaseRequest request)
        => PreviewCore(owner, request, null);

    private CharacterCareerGearPurchasePreviewResult PreviewCore(OwnerScope owner, CharacterCareerGearPurchaseRequest request,
        CharacterCareerGearPurchaseCatalogSelection? selection)
    {
        if (!CharacterCareerGearPurchaseTransaction.IsValidOwner(owner)
            || !CharacterCareerGearPurchaseTransaction.IsValidRequest(request))
            return new(CharacterCareerGearPurchaseOutcome.Conflict, Error: "gear_request_invalid");
        if (selection is not null && (selection.SourceId != request.SourceId
            || !CharacterCareerGearPurchaseTransaction.IsBoundCommand(new(request, selection.Binding))
            || selection.Binding.OwnerId != owner.NormalizedValue))
            return new(CharacterCareerGearPurchaseOutcome.Conflict, Error: "gear_catalog_selection_invalid");
        if (_store is not ICharacterCareerGearPurchaseAtomicCommitCapability atomic || !atomic.CareerGearPurchaseGuardsInstalled)
            return new(CharacterCareerGearPurchaseOutcome.Unavailable, Error: "gear_atomic_commit_unavailable");
        var read = ReadStore(owner, request.WorkspaceId);
        if (!read.Success || read.Value is not { } saved)
        { var failure = MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
        if (!CharacterCareerGearPurchaseTransaction.IsValidHistory(saved))
            return new(CharacterCareerGearPurchaseOutcome.Corrupt, Error: "gear_history_invalid");
        // Do not silently quote a second purchase for an already-used operation.
        if (saved.Document.AuxiliaryState.CharacterCareerGearPurchaseReceipts?
            .Any(row => row.Command.Request.OperationId == request.OperationId) == true)
            return new(CharacterCareerGearPurchaseOutcome.IdempotencyConflict, Error: "gear_operation_already_used");
        if (!CharacterCareerGearPurchaseTransaction.TryRead(owner, saved, request.SourceId, _sources,
            atomic.CareerGearPurchaseRuntimeDigest, out var snapshot, out string error))
            return new(selection is null ? CharacterCareerGearPurchaseOutcome.Unavailable : CharacterCareerGearPurchaseOutcome.Conflict,
                CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
        if (selection is not null && selection.Binding != snapshot!.Binding)
            return new(CharacterCareerGearPurchaseOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                Error: "gear_catalog_selection_stale");
        if (!CharacterCareerGearPurchaseTransaction.TryPreview(snapshot!, request, out var preview, out error))
            return new(CharacterCareerGearPurchaseOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
        // Check receipt capacity and XML edit feasibility without persisting or claiming confirmation.
        if (!CharacterCareerGearPurchaseTransaction.TryBuild(owner, saved, snapshot!,
            preview!.Command with { ExplicitlyConfirmed = true }, out _, out _))
            return new(CharacterCareerGearPurchaseOutcome.Unavailable, Error: "gear_purchase_unavailable");
        return new(CharacterCareerGearPurchaseOutcome.Available, preview, saved.ContentRevision);
    }

    public CharacterCareerGearPurchaseResult Commit(OwnerScope owner, CharacterCareerGearPurchaseCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!CharacterCareerGearPurchaseTransaction.IsValidOwner(owner)
            || !CharacterCareerGearPurchaseTransaction.IsConfirmedCommand(command)
            || command.Binding.OwnerId != owner.NormalizedValue)
            return new(CharacterCareerGearPurchaseOutcome.Conflict, Error: "gear_confirmation_invalid");
        string digest = CharacterCareerGearPurchaseTransaction.CommandDigest(command);
        // Reconciliation is read-only and remains possible even if a token was canceled after a previous commit.
        var prior = Lookup(owner, command.Request.WorkspaceId, command.Request.OperationId, digest);
        if (prior.Outcome != CharacterCareerGearPurchaseOutcome.NotFound) return prior;
        if (cancellationToken.IsCancellationRequested)
            return new(CharacterCareerGearPurchaseOutcome.Canceled, Error: "gear_canceled_before_commit");
        if (_store is not ICharacterCareerGearPurchaseAtomicCommitCapability atomic || !atomic.CareerGearPurchaseGuardsInstalled)
            return new(CharacterCareerGearPurchaseOutcome.Unavailable, Error: "gear_atomic_commit_unavailable");
        CharacterCareerGearPurchaseResult? result = null;
        bool canceled = false;
        try { result = atomic.CommitCareerGearPurchase(owner, command, _sources, cancellationToken); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { canceled = ex is OperationCanceledException; }
        // A store/adapter's success is not a durable receipt. Never compensate with a generic save.
        var recovered = Lookup(owner, command.Request.WorkspaceId, command.Request.OperationId, digest);
        if (recovered.Outcome == CharacterCareerGearPurchaseOutcome.Replayed)
            return recovered with { Outcome = result?.Outcome == CharacterCareerGearPurchaseOutcome.Applied
                ? CharacterCareerGearPurchaseOutcome.Applied : CharacterCareerGearPurchaseOutcome.Replayed };
        if (recovered.Outcome is CharacterCareerGearPurchaseOutcome.Unavailable or CharacterCareerGearPurchaseOutcome.Missing)
            return new(CharacterCareerGearPurchaseOutcome.UnknownOutcome, Error: recovered.Error ?? "gear_commit_result_unknown");
        if (recovered.Outcome != CharacterCareerGearPurchaseOutcome.NotFound) return recovered;
        if (result is { Outcome: CharacterCareerGearPurchaseOutcome.Conflict or CharacterCareerGearPurchaseOutcome.Corrupt
            or CharacterCareerGearPurchaseOutcome.IdempotencyConflict or CharacterCareerGearPurchaseOutcome.Canceled }) return result;
        return new(canceled ? CharacterCareerGearPurchaseOutcome.Canceled : CharacterCareerGearPurchaseOutcome.UnknownOutcome,
            CurrentWorkspaceRevision: recovered.CurrentWorkspaceRevision, Error: "gear_commit_not_observed");
    }

    public CharacterCareerGearPurchaseResult Lookup(OwnerScope owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest)
    {
        if (operationId == Guid.Empty || !CharacterCareerGearPurchaseTransaction.IsDigest(commandDigest))
            return new(CharacterCareerGearPurchaseOutcome.Conflict, Error: "gear_lookup_invalid");
        var read = ReadStore(owner, workspaceId);
        return read.Success && read.Value is { } saved
            ? CharacterCareerGearPurchaseTransaction.Lookup(owner, saved, operationId, commandDigest) : MapReadFailure(read);
    }

    private WorkspaceStoreReadResult ReadStore(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (!CharacterCareerGearPurchaseTransaction.IsValidOwner(owner)
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id))
            return new(WorkspaceOperationOutcome.Corrupt, Error: "gear_workspace_identity_invalid");
        try
        {
            var read = owner.IsLocalSingleUser ? _store.Get(id) : _store.Get(owner, id);
            return read.Success && (read.Value is null || read.Value.Id != id || read.Value.ContentRevision <= 0
                || read.Value.SavedRevision < 0 || read.Value.SavedRevision > read.Value.ContentRevision)
                ? new(WorkspaceOperationOutcome.Corrupt, Error: "gear_workspace_identity_mismatch") : read;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(WorkspaceOperationOutcome.Unavailable, Error: "gear_workspace_unavailable"); }
    }

    public static CharacterCareerGearPurchaseResult MapReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome switch
        {
            WorkspaceOperationOutcome.Missing => CharacterCareerGearPurchaseOutcome.Missing,
            WorkspaceOperationOutcome.Conflict => CharacterCareerGearPurchaseOutcome.Conflict,
            WorkspaceOperationOutcome.Corrupt => CharacterCareerGearPurchaseOutcome.Corrupt,
            _ => CharacterCareerGearPurchaseOutcome.Unavailable
        }, Error: read.Error);
}
