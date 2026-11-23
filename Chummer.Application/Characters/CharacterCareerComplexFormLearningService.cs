using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCareerComplexFormLearningService(
    IWorkspaceStore store, ICharacterSourceDataResolver sourceResolver, IOwnerContextAccessor? ownerContext) : ICharacterCareerComplexFormLearningService, IOwnerBoundCharacterCareerComplexFormLearningService5703
{
    private readonly IWorkspaceStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ICharacterSourceDataResolver _sources = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));

    private readonly IOwnerContextLeaseAccessor? owners = ownerContext as IOwnerContextLeaseAccessor;
    public CharacterCareerComplexFormLearningService(IWorkspaceStore store, ICharacterSourceDataResolver sourceResolver)
        : this(store, sourceResolver, null) { }

    private bool Acquire5703(OwnerScope owner, out IOwnerContextLease? lease, OwnerContextStamp? expected = null)
    {
        lease = null;
        if (owners is null) return false;
        var stamp = expected ?? owners.Capture();
        if (!stamp.IsValid || stamp.Owner != owner || owners.Capture() != stamp || !owners.TryAcquire(stamp, out lease)) return false;
        if (lease.Stamp == stamp && owners.Capture() == stamp) return true;
        lease.Dispose(); lease = null; return false;
    }

    public CharacterCareerComplexFormLearningCatalogResult Catalog(OwnerScope owner, CharacterCareerComplexFormLearningCatalogRequest request)
        => CatalogCore5703(owner, request);

    public CharacterCareerComplexFormLearningCatalogResult Catalog(OwnerContextStamp expected, CharacterCareerComplexFormLearningCatalogRequest request)
        => CatalogCore5703(expected.Owner, request, expected);

    private CharacterCareerComplexFormLearningCatalogResult CatalogCore5703(OwnerScope owner, CharacterCareerComplexFormLearningCatalogRequest request,
        OwnerContextStamp? expected = null)
    {
        if (expected is not null && (owners is null || _store is not ICharacterCareerComplexFormLearningCurrentEnvironmentCapability))
            return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_owner_bound_service_unavailable");
        if (!CharacterCareerComplexFormLearningTransaction.IsValidOwner(owner) || request is null
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
            || request.ExpectedWorkspaceRevision is <= 0 or long.MaxValue)
            return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_catalog_request_invalid");
        if (_store is not ICharacterCareerComplexFormLearningAtomicCommitCapability atomic)
            return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_atomic_commit_unavailable");
        if (!atomic.CareerComplexFormLearningGuardsInstalled)
            return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_ledger_guards_not_installed");
        if (owners is not null && _store is ICharacterCareerComplexFormLearningCurrentEnvironmentCapability current)
        {
            if (!Acquire5703(owner, out var lease, expected)) return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_owner_changed");
            using (lease) return current.CatalogCareerComplexFormLearning(owners, lease!, request, _sources);
        }
        var read = ReadStore(owner, request.WorkspaceId);
        if (!read.Success || read.Value is not { } saved)
        { var failure = MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
        if (!CharacterCareerComplexFormLearningTransaction.IsValidHistory(saved))
            return new(CharacterCareerComplexFormLearningOutcome.Corrupt, Error: "complex_form_learning_history_invalid");
        if (request.ExpectedWorkspaceRevision is { } revision && revision != saved.ContentRevision)
            return new(CharacterCareerComplexFormLearningOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                Error: "complex_form_learning_catalog_revision_changed");
        if (!CharacterCareerComplexFormLearningTransaction.TryCatalog(owner, saved, _sources,
                atomic.CareerComplexFormLearningRuntimeDigest, out var snapshot, out var catalog, out string error))
            return new(CharacterCareerComplexFormLearningOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
        if (!CharacterCareerComplexFormLearningTransaction.IsCatalogCurrent(owner, saved, _sources, snapshot!))
            return new(CharacterCareerComplexFormLearningOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                Error: "complex_form_learning_catalog_changed");
        var finalRead = ReadStore(owner, request.WorkspaceId);
        if (!finalRead.Success || finalRead.Value is not { } finalSaved
            || finalSaved.ContentRevision != saved.ContentRevision || finalSaved.SavedRevision != saved.SavedRevision
            || finalSaved.Document.Content != saved.Document.Content
            || finalSaved.Document.AuxiliaryStateDigest != saved.Document.AuxiliaryStateDigest
            || finalSaved.Document.Format != saved.Document.Format || finalSaved.Document.RulesetId != saved.Document.RulesetId
            || finalSaved.Document.SchemaVersion != saved.Document.SchemaVersion
            || finalSaved.Document.PayloadKind != saved.Document.PayloadKind)
            return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_catalog_changed");
        return new(CharacterCareerComplexFormLearningOutcome.Available, catalog, saved.ContentRevision);
    }

    public CharacterCareerComplexFormLearningPreviewResult Preview(OwnerScope owner, CharacterCareerComplexFormLearningRequest request)
        => PreviewCore5703(owner, request);

    public CharacterCareerComplexFormLearningPreviewResult Preview(OwnerContextStamp expected, CharacterCareerComplexFormLearningRequest request)
        => PreviewCore5703(expected.Owner, request, expected);

    private CharacterCareerComplexFormLearningPreviewResult PreviewCore5703(OwnerScope owner, CharacterCareerComplexFormLearningRequest request,
        OwnerContextStamp? expected = null)
    {
        if (expected is not null && (owners is null || _store is not ICharacterCareerComplexFormLearningCurrentEnvironmentCapability))
            return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_owner_bound_service_unavailable");
        if (!CharacterCareerComplexFormLearningTransaction.IsValidOwner(owner)
            || !CharacterCareerComplexFormLearningTransaction.IsValidRequest(request))
            return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_request_invalid");
        if (_store is not ICharacterCareerComplexFormLearningAtomicCommitCapability atomic)
            return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_atomic_commit_unavailable");
        if (!atomic.CareerComplexFormLearningGuardsInstalled)
            return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_ledger_guards_not_installed");
        if (owners is not null && _store is ICharacterCareerComplexFormLearningCurrentEnvironmentCapability current)
        {
            if (!Acquire5703(owner, out var lease, expected)) return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_owner_changed");
            using (lease) return current.PreviewCareerComplexFormLearning(owners, lease!, request, _sources);
        }
        var read = ReadStore(owner, request.WorkspaceId);
        if (!read.Success || read.Value is not { } saved)
        { var failure = MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
        if (!CharacterCareerComplexFormLearningTransaction.IsValidHistory(saved))
            return new(CharacterCareerComplexFormLearningOutcome.Corrupt, Error: "complex_form_learning_history_invalid");
        // Do not silently quote a second edit for an already-used operation.
        if (saved.Document.AuxiliaryState.CharacterCareerComplexFormLearningReceipts?
            .Any(row => row.Command.Request.OperationId == request.OperationId) == true)
            return new(CharacterCareerComplexFormLearningOutcome.IdempotencyConflict, Error: "complex_form_learning_operation_already_used");
        if (!CharacterCareerComplexFormLearningTransaction.TryRead(owner, saved, request, _sources,
            atomic.CareerComplexFormLearningRuntimeDigest, out var snapshot, out string error)
            || !CharacterCareerComplexFormLearningTransaction.TryPreview(snapshot!, request, out var preview, out error))
            return new(CharacterCareerComplexFormLearningOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
        // Check receipt capacity and XML edit feasibility without persisting or claiming confirmation.
        if (!CharacterCareerComplexFormLearningTransaction.TryBuild(owner, saved, snapshot!,
            preview!.Command with { ExplicitlyConfirmed = true }, out _, out _)
            || !CharacterCareerComplexFormLearningTransaction.IsSourceCurrent(owner, saved, _sources, request, snapshot!))
            return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_edit_unavailable");
        var finalRead = ReadStore(owner, request.WorkspaceId);
        if (!finalRead.Success || finalRead.Value is not { } finalSaved
            || finalSaved.ContentRevision != saved.ContentRevision || finalSaved.SavedRevision != saved.SavedRevision
            || finalSaved.Document.Content != saved.Document.Content
            || finalSaved.Document.AuxiliaryStateDigest != saved.Document.AuxiliaryStateDigest)
            return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_preview_changed");
        return new(CharacterCareerComplexFormLearningOutcome.Available, preview, saved.ContentRevision);
    }

    public CharacterCareerComplexFormLearningResult Commit(OwnerScope owner, CharacterCareerComplexFormLearningCommand command,
        CancellationToken cancellationToken = default)
    {
        if (owners is not null && _store is ICharacterCareerComplexFormLearningCurrentEnvironmentCapability)
        {
            if (!Acquire5703(owner, out var lease)) return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_owner_changed");
            // Hold one owner generation over lookup, the sole dispatch and reconciliation.
            using (lease) return CommitCore5703(owner, command, cancellationToken, lease);
        }
        return CommitCore5703(owner, command, cancellationToken, null);
    }

    public CharacterCareerComplexFormLearningResult Commit(OwnerContextStamp expected, CharacterCareerComplexFormLearningCommand command,
        CancellationToken cancellationToken = default)
    {
        if (owners is null || _store is not ICharacterCareerComplexFormLearningCurrentEnvironmentCapability)
            return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_owner_bound_service_unavailable");
        if (!Acquire5703(expected.Owner, out var lease, expected))
            return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_owner_changed");
        using (lease) return CommitCore5703(expected.Owner, command, cancellationToken, lease);
    }

    public CharacterCareerComplexFormLearningResult Lookup(OwnerContextStamp expected, CharacterWorkspaceId id,
        Guid operationId, string commandDigest)
    {
        if (owners is null || _store is not ICharacterCareerComplexFormLearningCurrentEnvironmentCapability)
            return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_owner_bound_service_unavailable");
        if (!Acquire5703(expected.Owner, out var lease, expected))
            return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_owner_changed");
        using (lease) return LookupCore5703(expected.Owner, id, operationId, commandDigest, lease);
    }

    private CharacterCareerComplexFormLearningResult CommitCore5703(OwnerScope owner, CharacterCareerComplexFormLearningCommand command,
        CancellationToken cancellationToken, IOwnerContextLease? lease)
    {
        if (!CharacterCareerComplexFormLearningTransaction.IsValidOwner(owner)
            || !CharacterCareerComplexFormLearningTransaction.IsConfirmedCommand(command)
            || command.Binding.OwnerId != owner.NormalizedValue)
            return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_confirmation_invalid");
        string digest = CharacterCareerComplexFormLearningTransaction.CommandDigest(command);
        // Reconciliation is read-only and remains possible even if a token was canceled after a previous commit.
        var prior = LookupCore5703(owner, command.Request.WorkspaceId, command.Request.OperationId, digest, lease);
        if (prior.Outcome != CharacterCareerComplexFormLearningOutcome.NotFound) return prior;
        if (cancellationToken.IsCancellationRequested)
            return new(CharacterCareerComplexFormLearningOutcome.Canceled, Error: "complex_form_learning_canceled_before_commit");
        if (_store is not ICharacterCareerComplexFormLearningAtomicCommitCapability atomic)
            return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_atomic_commit_unavailable");
        if (!atomic.CareerComplexFormLearningGuardsInstalled)
            return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_ledger_guards_not_installed");
        CharacterCareerComplexFormLearningResult? result = null;
        bool canceled = false;
        try { result = lease is not null && owners is not null && _store is ICharacterCareerComplexFormLearningCurrentEnvironmentCapability current
                ? current.CommitCareerComplexFormLearning(owners, lease, command, _sources, cancellationToken)
                : atomic.CommitCareerComplexFormLearning(owner, command, _sources, cancellationToken); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { canceled = ex is OperationCanceledException; }
        // A store/adapter's success is not a durable receipt. Never compensate with a generic save.
        var recovered = LookupCore5703(owner, command.Request.WorkspaceId, command.Request.OperationId, digest, lease);
        if (recovered.Outcome == CharacterCareerComplexFormLearningOutcome.Replayed)
            return recovered with { Outcome = result?.Outcome == CharacterCareerComplexFormLearningOutcome.Applied
                ? CharacterCareerComplexFormLearningOutcome.Applied : CharacterCareerComplexFormLearningOutcome.Replayed };
        if (recovered.Outcome is CharacterCareerComplexFormLearningOutcome.Unavailable or CharacterCareerComplexFormLearningOutcome.Missing)
            return new(CharacterCareerComplexFormLearningOutcome.UnknownOutcome, Error: recovered.Error ?? "complex_form_learning_commit_result_unknown");
        if (recovered.Outcome != CharacterCareerComplexFormLearningOutcome.NotFound) return recovered;
        if (result is { Outcome: CharacterCareerComplexFormLearningOutcome.Conflict or CharacterCareerComplexFormLearningOutcome.Corrupt
            or CharacterCareerComplexFormLearningOutcome.IdempotencyConflict or CharacterCareerComplexFormLearningOutcome.Canceled }) return result;
        return new(canceled ? CharacterCareerComplexFormLearningOutcome.Canceled : CharacterCareerComplexFormLearningOutcome.UnknownOutcome,
            CurrentWorkspaceRevision: recovered.CurrentWorkspaceRevision, Error: "complex_form_learning_commit_not_observed");
    }

    public CharacterCareerComplexFormLearningResult Lookup(OwnerScope owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest)
        => LookupCore5703(owner, workspaceId, operationId, commandDigest, null);

    private CharacterCareerComplexFormLearningResult LookupCore5703(OwnerScope owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest, IOwnerContextLease? lease)
    {
        if (operationId == Guid.Empty || !CharacterCareerComplexFormLearningTransaction.IsDigest(commandDigest))
            return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_lookup_invalid");
        var read = ReadStore(owner, workspaceId, lease);
        return read.Success && read.Value is { } saved
            ? CharacterCareerComplexFormLearningTransaction.Lookup(owner, saved, operationId, commandDigest) : MapReadFailure(read);
    }

    private WorkspaceStoreReadResult ReadStore(OwnerScope owner, CharacterWorkspaceId id, IOwnerContextLease? lease = null)
    {
        if (!CharacterCareerComplexFormLearningTransaction.IsValidOwner(owner)
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id))
            return new(WorkspaceOperationOutcome.Corrupt, Error: "complex_form_learning_workspace_identity_invalid");
        try
        {
            WorkspaceStoreReadResult read;
            if (owners is not null && _store is ICharacterCareerComplexFormLearningCurrentEnvironmentCapability current)
            {
                if (lease is not null) read = current.ReadCareerComplexFormLearningWorkspace(owners, lease, id);
                else
                {
                    if (!Acquire5703(owner, out var acquired))
                        return new(WorkspaceOperationOutcome.Conflict, Error: "complex_form_learning_owner_changed");
                    using (acquired) read = current.ReadCareerComplexFormLearningWorkspace(owners, acquired!, id);
                }
            }
            else read = owner.IsLocalSingleUser ? _store.Get(id) : _store.Get(owner, id);
            return read.Success && (read.Value is null || read.Value.Id != id || read.Value.ContentRevision <= 0
                || read.Value.SavedRevision < 0 || read.Value.SavedRevision > read.Value.ContentRevision)
                ? new(WorkspaceOperationOutcome.Corrupt, Error: "complex_form_learning_workspace_identity_mismatch") : read;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(WorkspaceOperationOutcome.Unavailable, Error: "complex_form_learning_workspace_unavailable"); }
    }

    public static CharacterCareerComplexFormLearningResult MapReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome switch
        {
            WorkspaceOperationOutcome.Missing => CharacterCareerComplexFormLearningOutcome.Missing,
            WorkspaceOperationOutcome.Conflict => CharacterCareerComplexFormLearningOutcome.Conflict,
            WorkspaceOperationOutcome.Corrupt => CharacterCareerComplexFormLearningOutcome.Corrupt,
            _ => CharacterCareerComplexFormLearningOutcome.Unavailable
        }, Error: read.Error);
}
