using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCareerSpellLearningService(
    IWorkspaceStore store, ICharacterSourceDataResolver sourceResolver, IOwnerContextAccessor? ownerContext) : ICharacterCareerSpellLearningService, IOwnerBoundCharacterCareerSpellLearningService5703
{
    private readonly IWorkspaceStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ICharacterSourceDataResolver _sources = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));

    private readonly IOwnerContextLeaseAccessor? owners = ownerContext as IOwnerContextLeaseAccessor;
    public CharacterCareerSpellLearningService(IWorkspaceStore store, ICharacterSourceDataResolver sourceResolver)
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

    public CharacterCareerSpellLearningCatalogResult Catalog(OwnerScope owner, CharacterCareerSpellLearningCatalogRequest request)
        => CatalogCore5703(owner, request);

    public CharacterCareerSpellLearningCatalogResult Catalog(OwnerContextStamp expected, CharacterCareerSpellLearningCatalogRequest request)
        => CatalogCore5703(expected.Owner, request, expected);

    private CharacterCareerSpellLearningCatalogResult CatalogCore5703(OwnerScope owner, CharacterCareerSpellLearningCatalogRequest request,
        OwnerContextStamp? expected = null)
    {
        if (expected is not null && (owners is null || _store is not ICharacterCareerSpellLearningCurrentEnvironmentCapability))
            return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_owner_bound_service_unavailable");
        if (!CharacterCareerSpellLearningTransaction.IsValidOwner(owner) || request is null
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
            || request.ExpectedWorkspaceRevision is <= 0 or long.MaxValue)
            return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_catalog_request_invalid");
        if (_store is not ICharacterCareerSpellLearningAtomicCommitCapability atomic)
            return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_atomic_commit_unavailable");
        if (!atomic.CareerSpellLearningGuardsInstalled)
            return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_ledger_guards_not_installed");
        if (owners is not null && _store is ICharacterCareerSpellLearningCurrentEnvironmentCapability current)
        {
            if (!Acquire5703(owner, out var lease, expected)) return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_owner_changed");
            using (lease) return current.CatalogCareerSpellLearning(owners, lease!, request, _sources);
        }
        var read = ReadStore(owner, request.WorkspaceId);
        if (!read.Success || read.Value is not { } saved)
        { var failure = MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
        if (!CharacterCareerSpellLearningTransaction.IsValidHistory(saved))
            return new(CharacterCareerSpellLearningOutcome.Corrupt, Error: "spell_learning_history_invalid");
        if (request.ExpectedWorkspaceRevision is { } revision && revision != saved.ContentRevision)
            return new(CharacterCareerSpellLearningOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                Error: "spell_learning_catalog_revision_changed");
        if (!CharacterCareerSpellLearningTransaction.TryCatalog(owner, saved, _sources,
                atomic.CareerSpellLearningRuntimeDigest, out var snapshot, out var catalog, out string error))
            return new(CharacterCareerSpellLearningOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
        if (!CharacterCareerSpellLearningTransaction.IsCatalogCurrent(owner, saved, _sources, snapshot!))
            return new(CharacterCareerSpellLearningOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                Error: "spell_learning_catalog_changed");
        var finalRead = ReadStore(owner, request.WorkspaceId);
        if (!finalRead.Success || finalRead.Value is not { } finalSaved
            || finalSaved.ContentRevision != saved.ContentRevision || finalSaved.SavedRevision != saved.SavedRevision
            || finalSaved.Document.Content != saved.Document.Content
            || finalSaved.Document.AuxiliaryStateDigest != saved.Document.AuxiliaryStateDigest
            || finalSaved.Document.Format != saved.Document.Format || finalSaved.Document.RulesetId != saved.Document.RulesetId
            || finalSaved.Document.SchemaVersion != saved.Document.SchemaVersion
            || finalSaved.Document.PayloadKind != saved.Document.PayloadKind)
            return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_catalog_changed");
        return new(CharacterCareerSpellLearningOutcome.Available, catalog, saved.ContentRevision);
    }

    public CharacterCareerSpellLearningPreviewResult Preview(OwnerScope owner, CharacterCareerSpellLearningRequest request)
        => PreviewCore5703(owner, request);

    public CharacterCareerSpellLearningPreviewResult Preview(OwnerContextStamp expected, CharacterCareerSpellLearningRequest request)
        => PreviewCore5703(expected.Owner, request, expected);

    private CharacterCareerSpellLearningPreviewResult PreviewCore5703(OwnerScope owner, CharacterCareerSpellLearningRequest request,
        OwnerContextStamp? expected = null)
    {
        if (expected is not null && (owners is null || _store is not ICharacterCareerSpellLearningCurrentEnvironmentCapability))
            return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_owner_bound_service_unavailable");
        if (!CharacterCareerSpellLearningTransaction.IsValidOwner(owner)
            || !CharacterCareerSpellLearningTransaction.IsValidRequest(request))
            return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_request_invalid");
        if (_store is not ICharacterCareerSpellLearningAtomicCommitCapability atomic)
            return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_atomic_commit_unavailable");
        if (!atomic.CareerSpellLearningGuardsInstalled)
            return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_ledger_guards_not_installed");
        if (owners is not null && _store is ICharacterCareerSpellLearningCurrentEnvironmentCapability current)
        {
            if (!Acquire5703(owner, out var lease, expected)) return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_owner_changed");
            using (lease) return current.PreviewCareerSpellLearning(owners, lease!, request, _sources);
        }
        var read = ReadStore(owner, request.WorkspaceId);
        if (!read.Success || read.Value is not { } saved)
        { var failure = MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
        if (!CharacterCareerSpellLearningTransaction.IsValidHistory(saved))
            return new(CharacterCareerSpellLearningOutcome.Corrupt, Error: "spell_learning_history_invalid");
        // Do not silently quote a second edit for an already-used operation.
        if (saved.Document.AuxiliaryState.CharacterCareerSpellLearningReceipts?
            .Any(row => row.Command.Request.OperationId == request.OperationId) == true)
            return new(CharacterCareerSpellLearningOutcome.IdempotencyConflict, Error: "spell_learning_operation_already_used");
        if (!CharacterCareerSpellLearningTransaction.TryRead(owner, saved, request, _sources,
            atomic.CareerSpellLearningRuntimeDigest, out var snapshot, out string error)
            || !CharacterCareerSpellLearningTransaction.TryPreview(snapshot!, request, out var preview, out error))
            return new(CharacterCareerSpellLearningOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
        // Check receipt capacity and XML edit feasibility without persisting or claiming confirmation.
        if (!CharacterCareerSpellLearningTransaction.TryBuild(owner, saved, snapshot!,
            preview!.Command with { ExplicitlyConfirmed = true }, out _, out _)
            || !CharacterCareerSpellLearningTransaction.IsSourceCurrent(saved, _sources, request, snapshot!))
            return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_edit_unavailable");
        var finalRead = ReadStore(owner, request.WorkspaceId);
        if (!finalRead.Success || finalRead.Value is not { } finalSaved
            || finalSaved.ContentRevision != saved.ContentRevision || finalSaved.SavedRevision != saved.SavedRevision
            || finalSaved.Document.Content != saved.Document.Content
            || finalSaved.Document.AuxiliaryStateDigest != saved.Document.AuxiliaryStateDigest)
            return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_preview_changed");
        return new(CharacterCareerSpellLearningOutcome.Available, preview, saved.ContentRevision);
    }

    public CharacterCareerSpellLearningResult Commit(OwnerScope owner, CharacterCareerSpellLearningCommand command,
        CancellationToken cancellationToken = default)
    {
        if (owners is not null && _store is ICharacterCareerSpellLearningCurrentEnvironmentCapability)
        {
            if (!Acquire5703(owner, out var lease)) return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_owner_changed");
            // Hold one owner generation over lookup, the sole dispatch and reconciliation.
            using (lease) return CommitCore5703(owner, command, cancellationToken, lease);
        }
        return CommitCore5703(owner, command, cancellationToken, null);
    }

    public CharacterCareerSpellLearningResult Commit(OwnerContextStamp expected, CharacterCareerSpellLearningCommand command,
        CancellationToken cancellationToken = default)
    {
        if (owners is null || _store is not ICharacterCareerSpellLearningCurrentEnvironmentCapability)
            return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_owner_bound_service_unavailable");
        if (!Acquire5703(expected.Owner, out var lease, expected))
            return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_owner_changed");
        using (lease) return CommitCore5703(expected.Owner, command, cancellationToken, lease);
    }

    public CharacterCareerSpellLearningResult Lookup(OwnerContextStamp expected, CharacterWorkspaceId id,
        Guid operationId, string commandDigest)
    {
        if (owners is null || _store is not ICharacterCareerSpellLearningCurrentEnvironmentCapability)
            return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_owner_bound_service_unavailable");
        if (!Acquire5703(expected.Owner, out var lease, expected))
            return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_owner_changed");
        using (lease) return LookupCore5703(expected.Owner, id, operationId, commandDigest, lease);
    }

    private CharacterCareerSpellLearningResult CommitCore5703(OwnerScope owner, CharacterCareerSpellLearningCommand command,
        CancellationToken cancellationToken, IOwnerContextLease? lease)
    {
        if (!CharacterCareerSpellLearningTransaction.IsValidOwner(owner)
            || !CharacterCareerSpellLearningTransaction.IsConfirmedCommand(command)
            || command.Binding.OwnerId != owner.NormalizedValue)
            return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_confirmation_invalid");
        string digest = CharacterCareerSpellLearningTransaction.CommandDigest(command);
        // Reconciliation is read-only and remains possible even if a token was canceled after a previous commit.
        var prior = LookupCore5703(owner, command.Request.WorkspaceId, command.Request.OperationId, digest, lease);
        if (prior.Outcome != CharacterCareerSpellLearningOutcome.NotFound) return prior;
        if (cancellationToken.IsCancellationRequested)
            return new(CharacterCareerSpellLearningOutcome.Canceled, Error: "spell_learning_canceled_before_commit");
        if (_store is not ICharacterCareerSpellLearningAtomicCommitCapability atomic)
            return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_atomic_commit_unavailable");
        if (!atomic.CareerSpellLearningGuardsInstalled)
            return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_ledger_guards_not_installed");
        CharacterCareerSpellLearningResult? result = null;
        bool canceled = false;
        try { result = lease is not null && owners is not null && _store is ICharacterCareerSpellLearningCurrentEnvironmentCapability current
                ? current.CommitCareerSpellLearning(owners, lease, command, _sources, cancellationToken)
                : atomic.CommitCareerSpellLearning(owner, command, _sources, cancellationToken); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { canceled = ex is OperationCanceledException; }
        // A store/adapter's success is not a durable receipt. Never compensate with a generic save.
        var recovered = LookupCore5703(owner, command.Request.WorkspaceId, command.Request.OperationId, digest, lease);
        if (recovered.Outcome == CharacterCareerSpellLearningOutcome.Replayed)
            return recovered with { Outcome = result?.Outcome == CharacterCareerSpellLearningOutcome.Applied
                ? CharacterCareerSpellLearningOutcome.Applied : CharacterCareerSpellLearningOutcome.Replayed };
        if (recovered.Outcome is CharacterCareerSpellLearningOutcome.Unavailable or CharacterCareerSpellLearningOutcome.Missing)
            return new(CharacterCareerSpellLearningOutcome.UnknownOutcome, Error: recovered.Error ?? "spell_learning_commit_result_unknown");
        if (recovered.Outcome != CharacterCareerSpellLearningOutcome.NotFound) return recovered;
        if (result is { Outcome: CharacterCareerSpellLearningOutcome.Conflict or CharacterCareerSpellLearningOutcome.Corrupt
            or CharacterCareerSpellLearningOutcome.IdempotencyConflict or CharacterCareerSpellLearningOutcome.Canceled }) return result;
        return new(canceled ? CharacterCareerSpellLearningOutcome.Canceled : CharacterCareerSpellLearningOutcome.UnknownOutcome,
            CurrentWorkspaceRevision: recovered.CurrentWorkspaceRevision, Error: "spell_learning_commit_not_observed");
    }

    public CharacterCareerSpellLearningResult Lookup(OwnerScope owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest)
        => LookupCore5703(owner, workspaceId, operationId, commandDigest, null);

    private CharacterCareerSpellLearningResult LookupCore5703(OwnerScope owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest, IOwnerContextLease? lease)
    {
        if (operationId == Guid.Empty || !CharacterCareerSpellLearningTransaction.IsDigest(commandDigest))
            return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_lookup_invalid");
        var read = ReadStore(owner, workspaceId, lease);
        return read.Success && read.Value is { } saved
            ? CharacterCareerSpellLearningTransaction.Lookup(owner, saved, operationId, commandDigest) : MapReadFailure(read);
    }

    private WorkspaceStoreReadResult ReadStore(OwnerScope owner, CharacterWorkspaceId id, IOwnerContextLease? lease = null)
    {
        if (!CharacterCareerSpellLearningTransaction.IsValidOwner(owner)
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id))
            return new(WorkspaceOperationOutcome.Corrupt, Error: "spell_learning_workspace_identity_invalid");
        try
        {
            WorkspaceStoreReadResult read;
            if (owners is not null && _store is ICharacterCareerSpellLearningCurrentEnvironmentCapability current)
            {
                if (lease is not null) read = current.ReadCareerSpellLearningWorkspace(owners, lease, id);
                else
                {
                    if (!Acquire5703(owner, out var acquired))
                        return new(WorkspaceOperationOutcome.Conflict, Error: "spell_learning_owner_changed");
                    using (acquired) read = current.ReadCareerSpellLearningWorkspace(owners, acquired!, id);
                }
            }
            else read = owner.IsLocalSingleUser ? _store.Get(id) : _store.Get(owner, id);
            return read.Success && (read.Value is null || read.Value.Id != id || read.Value.ContentRevision <= 0
                || read.Value.SavedRevision < 0 || read.Value.SavedRevision > read.Value.ContentRevision)
                ? new(WorkspaceOperationOutcome.Corrupt, Error: "spell_learning_workspace_identity_mismatch") : read;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(WorkspaceOperationOutcome.Unavailable, Error: "spell_learning_workspace_unavailable"); }
    }

    public static CharacterCareerSpellLearningResult MapReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome switch
        {
            WorkspaceOperationOutcome.Missing => CharacterCareerSpellLearningOutcome.Missing,
            WorkspaceOperationOutcome.Conflict => CharacterCareerSpellLearningOutcome.Conflict,
            WorkspaceOperationOutcome.Corrupt => CharacterCareerSpellLearningOutcome.Corrupt,
            _ => CharacterCareerSpellLearningOutcome.Unavailable
        }, Error: read.Error);
}
