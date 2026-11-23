using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public WorkspaceStoreReadResult ReadCareerComplexFormLearningWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id)
    {
        var stamp = lease.Stamp;
        if (!CareerComplexFormLearningGuardsInstalled || !LearningOwnerCurrent5703(owners, lease, stamp)) return UnavailableRead();
        try
        {
            string? path = TryGetPath(stamp.Owner, id);
            if (path is null) return new(WorkspaceOperationOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(stamp.Owner, id, path, owners, lease, stamp, out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return read;
            if (!CharacterCareerComplexFormLearningTransaction.IsValidHistory(saved))
                return new(WorkspaceOperationOutcome.Corrupt, Error: "complex_form_learning_history_invalid");
            current?.ValidateReadObservation();
            return LearningOwnerCurrent5703(owners, lease, stamp) ? read : UnavailableRead();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return UnavailableRead(); }
    }

    public CharacterCareerComplexFormLearningCatalogResult CatalogCareerComplexFormLearning(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerComplexFormLearningCatalogRequest request, ICharacterSourceDataResolver sources)
    {
        var stamp = lease.Stamp; var owner = stamp.Owner;
        if (!CareerComplexFormLearningGuardsInstalled || !LearningOwnerCurrent5703(owners, lease, stamp)
            || request is null || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
            || request.ExpectedWorkspaceRevision is <= 0 or long.MaxValue)
            return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_request_invalid");
        try
        {
            string? path = TryGetPath(owner, request.WorkspaceId);
            if (path is null) return new(CharacterCareerComplexFormLearningOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, request.WorkspaceId, path, owners, lease, stamp,
                out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved)
            { var failure = CharacterCareerComplexFormLearningService.MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
            if (!CharacterCareerComplexFormLearningTransaction.IsValidHistory(saved))
                return new(CharacterCareerComplexFormLearningOutcome.Corrupt, Error: "complex_form_learning_history_invalid");
            if (request.ExpectedWorkspaceRevision is { } revision && revision != saved.ContentRevision)
                return new(CharacterCareerComplexFormLearningOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "complex_form_learning_catalog_revision_changed");
            var effective = LearningSources5703(current, sources);
            string? slot = ReadContinuationSlotUnderLease(path);
            var imported = LearningContext5703(current, saved);
            if (!CharacterCareerComplexFormLearningTransaction.TryCatalog(owner, saved, effective,
                    LearningRuntime5703(CareerComplexFormLearningRuntimeDigest, current, slot, stamp), out var snapshot, out var catalog, out string error, imported))
                return new(CharacterCareerComplexFormLearningOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            if (!CharacterCareerComplexFormLearningTransaction.IsCatalogCurrent(owner, saved, effective, snapshot!))
                return new(CharacterCareerComplexFormLearningOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "complex_form_learning_catalog_changed");
            current?.ValidateReadObservation();
            if (!LearningOwnerCurrent5703(owners, lease, stamp) || slot != ReadContinuationSlotUnderLease(path))
                return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_owner_changed");
            return new(CharacterCareerComplexFormLearningOutcome.Available, catalog, saved.ContentRevision);

        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_current_sources_unavailable"); }
    }

    public CharacterCareerComplexFormLearningPreviewResult PreviewCareerComplexFormLearning(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerComplexFormLearningRequest request, ICharacterSourceDataResolver sources)
    {
        var stamp = lease.Stamp; var owner = stamp.Owner;
        if (!CareerComplexFormLearningGuardsInstalled || !LearningOwnerCurrent5703(owners, lease, stamp)
            || !CharacterCareerComplexFormLearningTransaction.IsValidRequest(request))
            return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_request_invalid");
        try
        {
            string? path = TryGetPath(owner, request.WorkspaceId);
            if (path is null) return new(CharacterCareerComplexFormLearningOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, request.WorkspaceId, path, owners, lease, stamp,
                out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved)
            { var failure = CharacterCareerComplexFormLearningService.MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
            if (!CharacterCareerComplexFormLearningTransaction.IsValidHistory(saved))
                return new(CharacterCareerComplexFormLearningOutcome.Corrupt, Error: "complex_form_learning_history_invalid");
            // Do not silently quote a second edit for an already-used operation.
            if (saved.Document.AuxiliaryState.CharacterCareerComplexFormLearningReceipts?
                .Any(row => row.Command.Request.OperationId == request.OperationId) == true)
                return new(CharacterCareerComplexFormLearningOutcome.IdempotencyConflict, Error: "complex_form_learning_operation_already_used");
            var effective = LearningSources5703(current, sources);
            string? slot = ReadContinuationSlotUnderLease(path);
            var imported = LearningContext5703(current, saved);
            if (!CharacterCareerComplexFormLearningTransaction.TryReadCurrent5703(owner, saved, request, effective,
                LearningRuntime5703(CareerComplexFormLearningRuntimeDigest, current, slot, stamp), imported, out var snapshot, out string error)
                || !CharacterCareerComplexFormLearningTransaction.TryPreview(snapshot!, request, out var preview, out error))
                return new(CharacterCareerComplexFormLearningOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            // Check receipt capacity and XML edit feasibility without persisting or claiming confirmation.
            if (!CharacterCareerComplexFormLearningTransaction.TryBuild(owner, saved, snapshot!,
                preview!.Command with { ExplicitlyConfirmed = true }, out var replacement, out var receipt)
                || !CharacterCareerComplexFormLearningTransaction.IsSourceCurrent(owner, saved, effective, request, snapshot!))
                return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_edit_unavailable");
            if (current is not null)
            {
                var proposed = saved with { Document = replacement!, ContentRevision = receipt!.CommittedWorkspaceRevision,
                    SavedRevision = receipt.CommittedWorkspaceRevision };
                var admission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                    _retainedCharacterQueries!, proposed);
                if (admission.AfterCapture is null || admission.AfterExecution is null || admission.Blockers.Count != 0)
                    return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_proposed_environment_unavailable");
            }
            current?.ValidateReadObservation();
            if (!LearningOwnerCurrent5703(owners, lease, stamp) || slot != ReadContinuationSlotUnderLease(path))
                return new(CharacterCareerComplexFormLearningOutcome.Conflict, Error: "complex_form_learning_owner_changed");
            return new(CharacterCareerComplexFormLearningOutcome.Available, preview, saved.ContentRevision);

        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(CharacterCareerComplexFormLearningOutcome.Unavailable, Error: "complex_form_learning_current_sources_unavailable"); }
    }
}
