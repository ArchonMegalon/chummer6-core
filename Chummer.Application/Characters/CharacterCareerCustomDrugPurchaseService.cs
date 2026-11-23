using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCareerCustomDrugPurchaseService(
    IWorkspaceStore store, ICharacterSourceDataResolver sourceResolver,
    ICharacterCareerCustomDrugPurchaseComponentValidator componentValidator) : ICharacterCareerCustomDrugPurchaseService
{
    private readonly IWorkspaceStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ICharacterSourceDataResolver _sources = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));

    private readonly ICharacterCareerCustomDrugPurchaseComponentValidator _components = componentValidator
        ?? throw new ArgumentNullException(nameof(componentValidator));

    public CharacterCareerCustomDrugPurchaseCatalogResult QueryCatalog(OwnerScope owner, CharacterCareerCustomDrugPurchaseCatalogQuery query)
    {
        if (!CharacterCareerCustomDrugPurchaseTransaction.IsValidOwner(owner) || query is null
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(query.WorkspaceId)
            || query.ExpectedWorkspaceRevision is <= 0 or long.MaxValue)
            return new(CharacterCareerCustomDrugPurchaseOutcome.Conflict, null, null, ["custom_drug_catalog_query_invalid"]);
        if (_store is not ICharacterCareerCustomDrugPurchaseAtomicCommitCapability atomic || !atomic.CareerCustomDrugPurchaseGuardsInstalled)
            return new(CharacterCareerCustomDrugPurchaseOutcome.Unavailable, null, null, ["custom_drug_atomic_commit_unavailable"]);
        var read = ReadStore(owner, query.WorkspaceId);
        if (!read.Success || read.Value is not { } saved)
        {
            var failure = MapReadFailure(read);
            return new(failure.Outcome, null, failure.CurrentWorkspaceRevision, [failure.Error ?? "custom_drug_workspace_unavailable"]);
        }
        if (saved.ContentRevision != query.ExpectedWorkspaceRevision)
            return new(CharacterCareerCustomDrugPurchaseOutcome.Conflict, null, saved.ContentRevision, ["custom_drug_catalog_revision_conflict"]);
        if (!CharacterCareerCustomDrugPurchaseTransaction.IsValidHistory(saved))
            return new(CharacterCareerCustomDrugPurchaseOutcome.Corrupt, null, saved.ContentRevision, ["custom_drug_history_invalid"]);
        if (!CharacterCareerCustomDrugPurchaseTransaction.TryReadCatalog(owner, saved, _sources, _components, atomic.CareerCustomDrugPurchaseRuntimeDigest,
            out var catalog, out string error))
            return new(CharacterCareerCustomDrugPurchaseOutcome.Unavailable, null, saved.ContentRevision, [error]);
        var after = ReadStore(owner, query.WorkspaceId);
        if (!after.Success || after.Value is not { } current)
        {
            var failure = MapReadFailure(after);
            return new(failure.Outcome, null, failure.CurrentWorkspaceRevision, [failure.Error ?? "custom_drug_workspace_unavailable"]);
        }
        if (!CharacterCareerCustomDrugPurchaseTransaction.IsWorkspaceCurrent(current, catalog!.Snapshot)
            || !atomic.CareerCustomDrugPurchaseGuardsInstalled || atomic.CareerCustomDrugPurchaseRuntimeDigest != catalog.Snapshot.RuntimeDigest)
            return new(CharacterCareerCustomDrugPurchaseOutcome.Conflict, null, current.ContentRevision, ["custom_drug_catalog_snapshot_changed"]);
        return new(CharacterCareerCustomDrugPurchaseOutcome.Available, catalog, saved.ContentRevision, []);
    }

    public CharacterCareerCustomDrugPurchasePreviewResult Preview(OwnerScope owner, CharacterCareerCustomDrugPurchaseRequest request,
        CharacterCareerCustomDrugPurchaseCatalogSelection selection)
        => selection is null ? new(CharacterCareerCustomDrugPurchaseOutcome.Conflict, Error: "custom_drug_catalog_selection_invalid")
            : PreviewCore(owner, request, selection);

    private CharacterCareerCustomDrugPurchasePreviewResult PreviewCore(OwnerScope owner, CharacterCareerCustomDrugPurchaseRequest request,
        CharacterCareerCustomDrugPurchaseCatalogSelection selection)
    {
        if (!CharacterCareerCustomDrugPurchaseTransaction.IsValidOwner(owner)
            || !CharacterCareerCustomDrugPurchaseTransaction.IsValidRequest(request))
            return new(CharacterCareerCustomDrugPurchaseOutcome.Conflict, Error: "custom_drug_request_invalid");
        if (selection.TargetDrugId != request.TargetDrugId
            || !CharacterCareerCustomDrugPurchaseTransaction.IsBoundCommand(new(request, selection.Binding))
            || selection.Binding.OwnerId != owner.NormalizedValue)
            return new(CharacterCareerCustomDrugPurchaseOutcome.Conflict, Error: "custom_drug_catalog_selection_invalid");
        if (_store is not ICharacterCareerCustomDrugPurchaseAtomicCommitCapability atomic || !atomic.CareerCustomDrugPurchaseGuardsInstalled)
            return new(CharacterCareerCustomDrugPurchaseOutcome.Unavailable, Error: "custom_drug_atomic_commit_unavailable");
        var read = ReadStore(owner, request.WorkspaceId);
        if (!read.Success || read.Value is not { } saved)
        { var failure = MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
        if (!CharacterCareerCustomDrugPurchaseTransaction.IsValidHistory(saved))
            return new(CharacterCareerCustomDrugPurchaseOutcome.Corrupt, Error: "custom_drug_history_invalid");
        // Do not silently quote a second purchase for an already-used operation.
        if (saved.Document.AuxiliaryState.CharacterCareerCustomDrugPurchaseReceipts?
            .Any(row => row.Command.Request.OperationId == request.OperationId) == true)
            return new(CharacterCareerCustomDrugPurchaseOutcome.IdempotencyConflict, Error: "custom_drug_operation_already_used");
        if (!CharacterCareerCustomDrugPurchaseTransaction.TryRead(owner, saved, request.TargetDrugId, _sources, _components,
            atomic.CareerCustomDrugPurchaseRuntimeDigest, out var snapshot, out string error))
            return new(CharacterCareerCustomDrugPurchaseOutcome.Conflict,
                CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
        if (selection.Binding != snapshot!.Binding)
            return new(CharacterCareerCustomDrugPurchaseOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                Error: "custom_drug_catalog_selection_stale");
        if (!CharacterCareerCustomDrugPurchaseTransaction.TryPreview(snapshot!, request, out var preview, out error))
            return new(CharacterCareerCustomDrugPurchaseOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
        // Check receipt capacity and XML edit feasibility without persisting or claiming confirmation.
        if (!CharacterCareerCustomDrugPurchaseTransaction.TryBuild(owner, saved, snapshot!,
            preview!.Command with { ExplicitlyConfirmed = true }, out _, out _))
            return new(CharacterCareerCustomDrugPurchaseOutcome.Unavailable, Error: "custom_drug_purchase_unavailable");
        return new(CharacterCareerCustomDrugPurchaseOutcome.Available, preview, saved.ContentRevision);
    }

    public CharacterCareerCustomDrugPurchaseResult Commit(OwnerScope owner, CharacterCareerCustomDrugPurchaseCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!CharacterCareerCustomDrugPurchaseTransaction.IsValidOwner(owner)
            || !CharacterCareerCustomDrugPurchaseTransaction.IsConfirmedCommand(command)
            || command.Binding.OwnerId != owner.NormalizedValue)
            return new(CharacterCareerCustomDrugPurchaseOutcome.Conflict, Error: "custom_drug_confirmation_invalid");
        string digest = CharacterCareerCustomDrugPurchaseTransaction.CommandDigest(command);
        // Reconciliation is read-only and remains possible even if a token was canceled after a previous commit.
        var prior = Lookup(owner, command.Request.WorkspaceId, command.Request.OperationId, digest);
        if (prior.Outcome != CharacterCareerCustomDrugPurchaseOutcome.NotFound) return prior;
        if (cancellationToken.IsCancellationRequested)
            return new(CharacterCareerCustomDrugPurchaseOutcome.Canceled, Error: "custom_drug_canceled_before_commit");
        if (_store is not ICharacterCareerCustomDrugPurchaseAtomicCommitCapability atomic || !atomic.CareerCustomDrugPurchaseGuardsInstalled)
            return new(CharacterCareerCustomDrugPurchaseOutcome.Unavailable, Error: "custom_drug_atomic_commit_unavailable");
        CharacterCareerCustomDrugPurchaseResult? result = null;
        bool canceled = false;
        try { result = atomic.CommitCareerCustomDrugPurchase(owner, command, _sources, _components, cancellationToken); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { canceled = ex is OperationCanceledException; }
        // A store/adapter's success is not a durable receipt. Never compensate with a generic save.
        var recovered = Lookup(owner, command.Request.WorkspaceId, command.Request.OperationId, digest);
        if (recovered.Outcome == CharacterCareerCustomDrugPurchaseOutcome.Replayed)
            return recovered with { Outcome = result?.Outcome == CharacterCareerCustomDrugPurchaseOutcome.Applied
                ? CharacterCareerCustomDrugPurchaseOutcome.Applied : CharacterCareerCustomDrugPurchaseOutcome.Replayed };
        if (recovered.Outcome is CharacterCareerCustomDrugPurchaseOutcome.Unavailable or CharacterCareerCustomDrugPurchaseOutcome.Missing)
            return new(CharacterCareerCustomDrugPurchaseOutcome.UnknownOutcome, Error: recovered.Error ?? "custom_drug_commit_result_unknown");
        if (recovered.Outcome != CharacterCareerCustomDrugPurchaseOutcome.NotFound) return recovered;
        if (result is { Outcome: CharacterCareerCustomDrugPurchaseOutcome.Conflict or CharacterCareerCustomDrugPurchaseOutcome.Corrupt
            or CharacterCareerCustomDrugPurchaseOutcome.IdempotencyConflict or CharacterCareerCustomDrugPurchaseOutcome.Canceled }) return result;
        return new(canceled ? CharacterCareerCustomDrugPurchaseOutcome.Canceled : CharacterCareerCustomDrugPurchaseOutcome.UnknownOutcome,
            CurrentWorkspaceRevision: recovered.CurrentWorkspaceRevision, Error: "custom_drug_commit_not_observed");
    }

    public CharacterCareerCustomDrugPurchaseResult Lookup(OwnerScope owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest)
    {
        if (operationId == Guid.Empty || !CharacterCareerCustomDrugPurchaseTransaction.IsDigest(commandDigest))
            return new(CharacterCareerCustomDrugPurchaseOutcome.Conflict, Error: "custom_drug_lookup_invalid");
        var read = ReadStore(owner, workspaceId);
        return read.Success && read.Value is { } saved
            ? CharacterCareerCustomDrugPurchaseTransaction.Lookup(owner, saved, operationId, commandDigest) : MapReadFailure(read);
    }

    private WorkspaceStoreReadResult ReadStore(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (!CharacterCareerCustomDrugPurchaseTransaction.IsValidOwner(owner)
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id))
            return new(WorkspaceOperationOutcome.Corrupt, Error: "custom_drug_workspace_identity_invalid");
        try
        {
            var read = owner.IsLocalSingleUser ? _store.Get(id) : _store.Get(owner, id);
            return read.Success && (read.Value is null || read.Value.Id != id || read.Value.ContentRevision <= 0
                || read.Value.SavedRevision < 0 || read.Value.SavedRevision > read.Value.ContentRevision)
                ? new(WorkspaceOperationOutcome.Corrupt, Error: "custom_drug_workspace_identity_mismatch") : read;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(WorkspaceOperationOutcome.Unavailable, Error: "custom_drug_workspace_unavailable"); }
    }

    public static CharacterCareerCustomDrugPurchaseResult MapReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome switch
        {
            WorkspaceOperationOutcome.Missing => CharacterCareerCustomDrugPurchaseOutcome.Missing,
            WorkspaceOperationOutcome.Conflict => CharacterCareerCustomDrugPurchaseOutcome.Conflict,
            WorkspaceOperationOutcome.Corrupt => CharacterCareerCustomDrugPurchaseOutcome.Corrupt,
            _ => CharacterCareerCustomDrugPurchaseOutcome.Unavailable
        }, Error: read.Error);
}
