using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCareerLifestyleMaintenanceService(
    IWorkspaceStore store, ICharacterSourceDataResolver sourceResolver) : ICharacterCareerLifestyleMaintenanceService
{
    private readonly IWorkspaceStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ICharacterSourceDataResolver _sources = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));

    public CharacterCareerLifestyleMaintenanceLoadResult Load(OwnerScope owner, CharacterWorkspaceId workspaceId)
    {
        if (_store is not ICharacterCareerLifestyleMaintenanceAtomicCommitCapability)
            return new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_maintenance_atomic_commit_unavailable");
        var read = ReadStore(owner, workspaceId);
        if (!read.Success || read.Value is not { } saved)
        { var failure = MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
        if (!CharacterCareerLifestyleMaintenanceTransaction.IsValidHistory(saved))
            return new(CharacterCareerLifestyleMaintenanceOutcome.Corrupt, Error: "lifestyle_maintenance_history_invalid");
        return CharacterCareerLifestyleMaintenanceProjection.Load(saved, _sources);
    }

    public CharacterCareerLifestyleMaintenancePreviewResult Preview(OwnerScope owner, CharacterCareerLifestyleMaintenanceRequest request)
    {
        request = CharacterCareerLifestyleMaintenanceTransaction.Freeze(request)!;
        if (!CharacterCareerLifestyleMaintenanceTransaction.IsValidOwner(owner)
            || !CharacterCareerLifestyleMaintenanceTransaction.IsValidRequest(request))
            return new(CharacterCareerLifestyleMaintenanceOutcome.Conflict, Error: "lifestyle_maintenance_request_invalid");
        if (_store is not ICharacterCareerLifestyleMaintenanceAtomicCommitCapability atomic)
            return new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_maintenance_atomic_commit_unavailable");
        var read = ReadStore(owner, request.WorkspaceId);
        if (!read.Success || read.Value is not { } saved)
        { var failure = MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
        return PreviewSaved(owner, saved, request, _sources, atomic.CareerLifestyleMaintenanceRuntimeDigest);
    }

    public static CharacterCareerLifestyleMaintenancePreviewResult PreviewSaved(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerLifestyleMaintenanceRequest request, ICharacterSourceDataResolver sources, string runtimeDigest)
    {
        if (!CharacterCareerLifestyleMaintenanceTransaction.IsValidOwner(owner)
            || !CharacterCareerLifestyleMaintenanceTransaction.IsValidRequest(request))
            return new(CharacterCareerLifestyleMaintenanceOutcome.Conflict, Error: "lifestyle_request_invalid");
        if (!CharacterCareerLifestyleMaintenanceTransaction.IsValidHistory(saved))
            return new(CharacterCareerLifestyleMaintenanceOutcome.Corrupt, Error: "lifestyle_maintenance_history_invalid");
        // Do not silently quote a second change for an already-used operation.
        if (saved.Document.AuxiliaryState.CharacterCareerLifestyleMaintenanceReceipts?
            .Any(row => row.Command.Request.OperationId == request.OperationId) == true)
            return new(CharacterCareerLifestyleMaintenanceOutcome.IdempotencyConflict, Error: "lifestyle_maintenance_operation_already_used");
        if (!CharacterCareerLifestyleMaintenanceTransaction.TryRead(owner, saved, request, sources,
            runtimeDigest, out var snapshot, out string error)
            || !CharacterCareerLifestyleMaintenanceTransaction.TryPreview(snapshot!, request, out var preview, out error))
            return new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
        // Check receipt capacity and XML edit feasibility without persisting or claiming confirmation.
        if (!CharacterCareerLifestyleMaintenanceTransaction.TryBuild(owner, saved, snapshot!,
            preview!.Command with { ExplicitlyConfirmed = true }, out _, out _))
            return new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_maintenance_receipt_or_serialization_unavailable");
        return new(CharacterCareerLifestyleMaintenanceOutcome.Available, preview, saved.ContentRevision);
    }

    public CharacterCareerLifestyleMaintenanceResult Commit(OwnerScope owner, CharacterCareerLifestyleMaintenanceCommand command,
        CancellationToken cancellationToken = default)
    {
        command = CharacterCareerLifestyleMaintenanceTransaction.Freeze(command)!;
        if (!CharacterCareerLifestyleMaintenanceTransaction.IsValidOwner(owner)
            || !CharacterCareerLifestyleMaintenanceTransaction.IsConfirmedCommand(command)
            || command.Binding.OwnerId != owner.NormalizedValue)
            return new(CharacterCareerLifestyleMaintenanceOutcome.Conflict, Error: "lifestyle_maintenance_confirmation_invalid");
        // Reconciliation is read-only and remains possible even if a token was canceled after a previous commit.
        var prior = Lookup(owner, command);
        if (prior.Outcome != CharacterCareerLifestyleMaintenanceOutcome.NotFound) return prior;
        if (cancellationToken.IsCancellationRequested)
            return new(CharacterCareerLifestyleMaintenanceOutcome.Canceled, Error: "lifestyle_maintenance_canceled_before_commit");
        if (_store is not ICharacterCareerLifestyleMaintenanceAtomicCommitCapability atomic)
            return new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_maintenance_atomic_commit_unavailable");
        CharacterCareerLifestyleMaintenanceResult? result = null;
        bool canceled = false;
        try { result = atomic.CommitCareerLifestyleMaintenance(owner, command, _sources, cancellationToken); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { canceled = ex is OperationCanceledException; }
        // A store/adapter's success is not a durable receipt. Never compensate with a generic save.
        var recovered = Lookup(owner, command);
        if (recovered.Outcome == CharacterCareerLifestyleMaintenanceOutcome.Replayed)
            return recovered with { Outcome = result?.Outcome == CharacterCareerLifestyleMaintenanceOutcome.Applied
                ? CharacterCareerLifestyleMaintenanceOutcome.Applied : CharacterCareerLifestyleMaintenanceOutcome.Replayed };
        if (recovered.Outcome is CharacterCareerLifestyleMaintenanceOutcome.Unavailable or CharacterCareerLifestyleMaintenanceOutcome.Missing)
            return new(CharacterCareerLifestyleMaintenanceOutcome.UnknownOutcome, Error: recovered.Error ?? "lifestyle_maintenance_commit_result_unknown");
        if (recovered.Outcome != CharacterCareerLifestyleMaintenanceOutcome.NotFound) return recovered;
        if (result is { Outcome: CharacterCareerLifestyleMaintenanceOutcome.Conflict or CharacterCareerLifestyleMaintenanceOutcome.Corrupt
            or CharacterCareerLifestyleMaintenanceOutcome.IdempotencyConflict or CharacterCareerLifestyleMaintenanceOutcome.Canceled }) return result;
        return new(canceled ? CharacterCareerLifestyleMaintenanceOutcome.Canceled : CharacterCareerLifestyleMaintenanceOutcome.UnknownOutcome,
            CurrentWorkspaceRevision: recovered.CurrentWorkspaceRevision, Error: "lifestyle_maintenance_commit_not_observed");
    }

    public CharacterCareerLifestyleMaintenanceResult Lookup(OwnerScope owner, CharacterCareerLifestyleMaintenanceCommand original)
    {
        if (!CharacterCareerLifestyleMaintenanceTransaction.IsConfirmedCommand(original))
            return new(CharacterCareerLifestyleMaintenanceOutcome.Conflict, Error: "lifestyle_maintenance_lookup_invalid");
        var read = ReadStore(owner, original.Request.WorkspaceId);
        return read.Success && read.Value is { } saved
            ? CharacterCareerLifestyleMaintenanceTransaction.Lookup(owner, saved, original) : MapReadFailure(read);
    }

    private WorkspaceStoreReadResult ReadStore(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (!CharacterCareerLifestyleMaintenanceTransaction.IsValidOwner(owner)
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id))
            return new(WorkspaceOperationOutcome.Corrupt, Error: "lifestyle_maintenance_workspace_identity_invalid");
        try
        {
            var read = owner.IsLocalSingleUser ? _store.Get(id) : _store.Get(owner, id);
            return read.Success && (read.Value is null || read.Value.Id != id || read.Value.ContentRevision <= 0
                || read.Value.SavedRevision < 0 || read.Value.SavedRevision > read.Value.ContentRevision)
                ? new(WorkspaceOperationOutcome.Corrupt, Error: "lifestyle_maintenance_workspace_identity_mismatch") : read;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(WorkspaceOperationOutcome.Unavailable, Error: "lifestyle_maintenance_workspace_unavailable"); }
    }

    public static CharacterCareerLifestyleMaintenanceResult MapReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome switch
        {
            WorkspaceOperationOutcome.Missing => CharacterCareerLifestyleMaintenanceOutcome.Missing,
            WorkspaceOperationOutcome.Conflict => CharacterCareerLifestyleMaintenanceOutcome.Conflict,
            WorkspaceOperationOutcome.Corrupt => CharacterCareerLifestyleMaintenanceOutcome.Corrupt,
            _ => CharacterCareerLifestyleMaintenanceOutcome.Unavailable
        }, Error: read.Error);
}
