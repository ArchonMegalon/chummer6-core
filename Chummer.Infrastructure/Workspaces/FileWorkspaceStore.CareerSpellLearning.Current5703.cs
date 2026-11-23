using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public WorkspaceStoreReadResult ReadCareerSpellLearningWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id)
    {
        var stamp = lease.Stamp;
        if (!CareerSpellLearningGuardsInstalled || !LearningOwnerCurrent5703(owners, lease, stamp)) return UnavailableRead();
        try
        {
            string? path = TryGetPath(stamp.Owner, id);
            if (path is null) return new(WorkspaceOperationOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(stamp.Owner, id, path, owners, lease, stamp, out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return read;
            if (!CharacterCareerSpellLearningTransaction.IsValidHistory(saved))
                return new(WorkspaceOperationOutcome.Corrupt, Error: "spell_learning_history_invalid");
            current?.ValidateReadObservation();
            return LearningOwnerCurrent5703(owners, lease, stamp) ? read : UnavailableRead();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return UnavailableRead(); }
    }

    public CharacterCareerSpellLearningCatalogResult CatalogCareerSpellLearning(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerSpellLearningCatalogRequest request, ICharacterSourceDataResolver sources)
    {
        var stamp = lease.Stamp; var owner = stamp.Owner;
        if (!CareerSpellLearningGuardsInstalled || !LearningOwnerCurrent5703(owners, lease, stamp)
            || request is null || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
            || request.ExpectedWorkspaceRevision is <= 0 or long.MaxValue)
            return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_request_invalid");
        try
        {
            string? path = TryGetPath(owner, request.WorkspaceId);
            if (path is null) return new(CharacterCareerSpellLearningOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, request.WorkspaceId, path, owners, lease, stamp,
                out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved)
            { var failure = CharacterCareerSpellLearningService.MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
            if (!CharacterCareerSpellLearningTransaction.IsValidHistory(saved))
                return new(CharacterCareerSpellLearningOutcome.Corrupt, Error: "spell_learning_history_invalid");
            if (request.ExpectedWorkspaceRevision is { } revision && revision != saved.ContentRevision)
                return new(CharacterCareerSpellLearningOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "spell_learning_catalog_revision_changed");
            var effective = LearningSources5703(current, sources);
            string? slot = ReadContinuationSlotUnderLease(path);
            var imported = LearningContext5703(current, saved);
            if (!CharacterCareerSpellLearningTransaction.TryCatalog(owner, saved, effective,
                    LearningRuntime5703(CareerSpellLearningRuntimeDigest, current, slot, stamp), out var snapshot, out var catalog, out string error, imported))
                return new(CharacterCareerSpellLearningOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            if (!CharacterCareerSpellLearningTransaction.IsCatalogCurrent(owner, saved, effective, snapshot!))
                return new(CharacterCareerSpellLearningOutcome.Conflict, CurrentWorkspaceRevision: saved.ContentRevision,
                    Error: "spell_learning_catalog_changed");
            current?.ValidateReadObservation();
            if (!LearningOwnerCurrent5703(owners, lease, stamp) || slot != ReadContinuationSlotUnderLease(path))
                return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_owner_changed");
            return new(CharacterCareerSpellLearningOutcome.Available, catalog, saved.ContentRevision);

        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_current_sources_unavailable"); }
    }

    public CharacterCareerSpellLearningPreviewResult PreviewCareerSpellLearning(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerSpellLearningRequest request, ICharacterSourceDataResolver sources)
    {
        var stamp = lease.Stamp; var owner = stamp.Owner;
        if (!CareerSpellLearningGuardsInstalled || !LearningOwnerCurrent5703(owners, lease, stamp)
            || !CharacterCareerSpellLearningTransaction.IsValidRequest(request))
            return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_request_invalid");
        try
        {
            string? path = TryGetPath(owner, request.WorkspaceId);
            if (path is null) return new(CharacterCareerSpellLearningOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, request.WorkspaceId, path, owners, lease, stamp,
                out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved)
            { var failure = CharacterCareerSpellLearningService.MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
            if (!CharacterCareerSpellLearningTransaction.IsValidHistory(saved))
                return new(CharacterCareerSpellLearningOutcome.Corrupt, Error: "spell_learning_history_invalid");
            // Do not silently quote a second edit for an already-used operation.
            if (saved.Document.AuxiliaryState.CharacterCareerSpellLearningReceipts?
                .Any(row => row.Command.Request.OperationId == request.OperationId) == true)
                return new(CharacterCareerSpellLearningOutcome.IdempotencyConflict, Error: "spell_learning_operation_already_used");
            var effective = LearningSources5703(current, sources);
            string? slot = ReadContinuationSlotUnderLease(path);
            var imported = LearningContext5703(current, saved);
            if (!CharacterCareerSpellLearningTransaction.TryReadCurrent5703(owner, saved, request, effective,
                LearningRuntime5703(CareerSpellLearningRuntimeDigest, current, slot, stamp), imported, out var snapshot, out string error)
                || !CharacterCareerSpellLearningTransaction.TryPreview(snapshot!, request, out var preview, out error))
                return new(CharacterCareerSpellLearningOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            // Check receipt capacity and XML edit feasibility without persisting or claiming confirmation.
            if (!CharacterCareerSpellLearningTransaction.TryBuild(owner, saved, snapshot!,
                preview!.Command with { ExplicitlyConfirmed = true }, out var replacement, out var receipt)
                || !CharacterCareerSpellLearningTransaction.IsSourceCurrent(saved, effective, request, snapshot!))
                return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_edit_unavailable");
            if (current is not null)
            {
                var proposed = saved with { Document = replacement!, ContentRevision = receipt!.CommittedWorkspaceRevision,
                    SavedRevision = receipt.CommittedWorkspaceRevision };
                var admission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                    _retainedCharacterQueries!, proposed);
                if (admission.AfterCapture is null || admission.AfterExecution is null || admission.Blockers.Count != 0)
                    return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_proposed_environment_unavailable");
            }
            current?.ValidateReadObservation();
            if (!LearningOwnerCurrent5703(owners, lease, stamp) || slot != ReadContinuationSlotUnderLease(path))
                return new(CharacterCareerSpellLearningOutcome.Conflict, Error: "spell_learning_owner_changed");
            return new(CharacterCareerSpellLearningOutcome.Available, preview, saved.ContentRevision);

        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(CharacterCareerSpellLearningOutcome.Unavailable, Error: "spell_learning_current_sources_unavailable"); }
    }
}
