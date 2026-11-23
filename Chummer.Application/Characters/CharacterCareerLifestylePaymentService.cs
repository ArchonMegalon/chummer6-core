using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCareerLifestylePaymentService(
    IWorkspaceStore store, ICharacterSourceDataResolver sourceResolver) : ICharacterCareerLifestylePaymentService
{
    private readonly IWorkspaceStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ICharacterSourceDataResolver _sources = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));

    public CharacterCareerLifestylePaymentPreviewResult Preview(OwnerScope owner, CharacterCareerLifestylePaymentRequest request)
    {
        if (!CharacterCareerLifestylePaymentTransaction.IsValidOwner(owner)
            || !CharacterCareerLifestylePaymentTransaction.IsValidRequest(request))
            return new(CharacterCareerLifestylePaymentOutcome.Conflict, Error: "lifestyle_request_invalid");
        if (_store is not ICharacterCareerLifestylePaymentAtomicCommitCapability atomic)
            return new(CharacterCareerLifestylePaymentOutcome.Unavailable, Error: "lifestyle_atomic_commit_unavailable");
        var read = ReadStore(owner, request.WorkspaceId);
        if (!read.Success || read.Value is not { } saved)
        { var failure = MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
        return PreviewSaved(owner, saved, request, _sources, atomic.CareerLifestylePaymentRuntimeDigest);
    }

    public static CharacterCareerLifestylePaymentPreviewResult PreviewSaved(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerLifestylePaymentRequest request, ICharacterSourceDataResolver sources, string runtimeDigest)
    {
        if (!CharacterCareerLifestylePaymentTransaction.IsValidOwner(owner)
            || !CharacterCareerLifestylePaymentTransaction.IsValidRequest(request))
            return new(CharacterCareerLifestylePaymentOutcome.Conflict, Error: "lifestyle_request_invalid");
        if (!CharacterCareerLifestylePaymentTransaction.IsValidHistory(saved))
            return new(CharacterCareerLifestylePaymentOutcome.Corrupt, Error: "lifestyle_history_invalid");
        // Do not silently quote a second purchase for an already-used operation.
        if (saved.Document.AuxiliaryState.CharacterCareerLifestylePaymentReceipts?
            .Any(row => row.Command.Request.OperationId == request.OperationId) == true)
            return new(CharacterCareerLifestylePaymentOutcome.IdempotencyConflict, Error: "lifestyle_operation_already_used");
        if (!CharacterCareerLifestylePaymentTransaction.TryRead(owner, saved, request.LifestyleId, sources,
            runtimeDigest, out var snapshot, out string error)
            || !CharacterCareerLifestylePaymentTransaction.TryPreview(snapshot!, request, out var preview, out error))
            return new(CharacterCareerLifestylePaymentOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
        // Check receipt capacity and XML edit feasibility without persisting or claiming confirmation.
        if (!CharacterCareerLifestylePaymentTransaction.TryBuild(owner, saved, snapshot!,
            preview!.Command with { ExplicitlyConfirmed = true }, out _, out _))
            return new(CharacterCareerLifestylePaymentOutcome.Unavailable, Error: "lifestyle_payment_unavailable");
        return new(CharacterCareerLifestylePaymentOutcome.Available, preview, saved.ContentRevision);
    }

    public CharacterCareerLifestylePaymentResult Commit(OwnerScope owner, CharacterCareerLifestylePaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!CharacterCareerLifestylePaymentTransaction.IsValidOwner(owner)
            || !CharacterCareerLifestylePaymentTransaction.IsConfirmedCommand(command)
            || command.Binding.OwnerId != owner.NormalizedValue)
            return new(CharacterCareerLifestylePaymentOutcome.Conflict, Error: "lifestyle_confirmation_invalid");
        string digest = CharacterCareerLifestylePaymentTransaction.CommandDigest(command);
        // Reconciliation is read-only and remains possible even if a token was canceled after a previous commit.
        var prior = Lookup(owner, command.Request.WorkspaceId, command.Request.OperationId, digest);
        if (prior.Outcome != CharacterCareerLifestylePaymentOutcome.NotFound) return prior;
        if (cancellationToken.IsCancellationRequested)
            return new(CharacterCareerLifestylePaymentOutcome.Canceled, Error: "lifestyle_canceled_before_commit");
        if (_store is not ICharacterCareerLifestylePaymentAtomicCommitCapability atomic)
            return new(CharacterCareerLifestylePaymentOutcome.Unavailable, Error: "lifestyle_atomic_commit_unavailable");
        CharacterCareerLifestylePaymentResult? result = null;
        bool canceled = false;
        try { result = atomic.CommitCareerLifestylePayment(owner, command, _sources, cancellationToken); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { canceled = ex is OperationCanceledException; }
        // A store/adapter's success is not a durable receipt. Never compensate with a generic save.
        var recovered = Lookup(owner, command.Request.WorkspaceId, command.Request.OperationId, digest);
        if (recovered.Outcome == CharacterCareerLifestylePaymentOutcome.Replayed)
            return recovered with { Outcome = result?.Outcome == CharacterCareerLifestylePaymentOutcome.Applied
                ? CharacterCareerLifestylePaymentOutcome.Applied : CharacterCareerLifestylePaymentOutcome.Replayed };
        if (recovered.Outcome is CharacterCareerLifestylePaymentOutcome.Unavailable or CharacterCareerLifestylePaymentOutcome.Missing)
            return new(CharacterCareerLifestylePaymentOutcome.UnknownOutcome, Error: recovered.Error ?? "lifestyle_commit_result_unknown");
        if (recovered.Outcome != CharacterCareerLifestylePaymentOutcome.NotFound) return recovered;
        if (result is { Outcome: CharacterCareerLifestylePaymentOutcome.Conflict or CharacterCareerLifestylePaymentOutcome.Corrupt
            or CharacterCareerLifestylePaymentOutcome.IdempotencyConflict or CharacterCareerLifestylePaymentOutcome.Canceled }) return result;
        return new(canceled ? CharacterCareerLifestylePaymentOutcome.Canceled : CharacterCareerLifestylePaymentOutcome.UnknownOutcome,
            CurrentWorkspaceRevision: recovered.CurrentWorkspaceRevision, Error: "lifestyle_commit_not_observed");
    }

    public CharacterCareerLifestylePaymentResult Lookup(OwnerScope owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest)
    {
        if (operationId == Guid.Empty || !CharacterCareerLifestylePaymentTransaction.IsDigest(commandDigest))
            return new(CharacterCareerLifestylePaymentOutcome.Conflict, Error: "lifestyle_lookup_invalid");
        var read = ReadStore(owner, workspaceId);
        return read.Success && read.Value is { } saved
            ? CharacterCareerLifestylePaymentTransaction.Lookup(owner, saved, operationId, commandDigest) : MapReadFailure(read);
    }

    private WorkspaceStoreReadResult ReadStore(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (!CharacterCareerLifestylePaymentTransaction.IsValidOwner(owner)
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id))
            return new(WorkspaceOperationOutcome.Corrupt, Error: "lifestyle_workspace_identity_invalid");
        try
        {
            var read = owner.IsLocalSingleUser ? _store.Get(id) : _store.Get(owner, id);
            return read.Success && (read.Value is null || read.Value.Id != id || read.Value.ContentRevision <= 0
                || read.Value.SavedRevision < 0 || read.Value.SavedRevision > read.Value.ContentRevision)
                ? new(WorkspaceOperationOutcome.Corrupt, Error: "lifestyle_workspace_identity_mismatch") : read;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(WorkspaceOperationOutcome.Unavailable, Error: "lifestyle_workspace_unavailable"); }
    }

    public static CharacterCareerLifestylePaymentResult MapReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome switch
        {
            WorkspaceOperationOutcome.Missing => CharacterCareerLifestylePaymentOutcome.Missing,
            WorkspaceOperationOutcome.Conflict => CharacterCareerLifestylePaymentOutcome.Conflict,
            WorkspaceOperationOutcome.Corrupt => CharacterCareerLifestylePaymentOutcome.Corrupt,
            _ => CharacterCareerLifestylePaymentOutcome.Unavailable
        }, Error: read.Error);
}
