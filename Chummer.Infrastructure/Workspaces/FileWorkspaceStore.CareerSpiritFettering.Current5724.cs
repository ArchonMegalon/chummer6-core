using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerSpiritFetteringCurrentEnvironmentCapability
{
    public WorkspaceStoreReadResult ReadCareerSpiritFetteringWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id)
    {
        var stamp = lease.Stamp;
        if (!CareerSpiritFetteringGuardsInstalled || !LearningOwnerCurrent5703(owners, lease, stamp)) return UnavailableRead();
        try
        {
            string? path = TryGetPath(stamp.Owner, id);
            if (path is null) return new(WorkspaceOperationOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(stamp.Owner, id, path, owners, lease, stamp, out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return read;
            if (!CharacterCareerSpiritFetteringTransaction.IsValidHistory(saved))
                return new(WorkspaceOperationOutcome.Corrupt, Error: "spirit_fettering_history_invalid");
            current?.ValidateReadObservation();
            return LearningOwnerCurrent5703(owners, lease, stamp) ? read : UnavailableRead();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return UnavailableRead(); }
    }

    public CharacterCareerSpiritFetteringListResult ListCareerSpiritFettering(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, long expectedRevision)
    {
        var read = ReadCareerSpiritFetteringWorkspace(owners, lease, id);
        if (!read.Success || read.Value is not { } saved)
            return new(CharacterCareerSpiritFetteringService.MapReadFailure(read).Outcome, Error: read.Error);
        if (saved.ContentRevision != expectedRevision || saved.SavedRevision != saved.ContentRevision
            || !CharacterCareerPowerGovernance.Allows(saved)) return new(CharacterCareerSpiritFetteringOutcome.Conflict);
        try
        {
            var entries = CharacterCareerSpiritFetteringTransaction.ReadEntries(saved.Document.Content);
            return LearningOwnerCurrent5703(owners, lease, lease.Stamp)
                ? new(CharacterCareerSpiritFetteringOutcome.Available, entries, saved.ContentRevision)
                : new(CharacterCareerSpiritFetteringOutcome.Conflict);
        }
        catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException or InvalidOperationException or FormatException)
        { return new(CharacterCareerSpiritFetteringOutcome.Unavailable, Error: "spirit_fettering_collection_unavailable"); }
    }

    public CharacterCareerSpiritFetteringPreviewResult PreviewCareerSpiritFettering(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerSpiritFetteringRequest request, ICharacterSourceDataResolver sources)
    {
        var stamp = lease.Stamp; var owner = stamp.Owner;
        if (!CareerSpiritFetteringGuardsInstalled || !LearningOwnerCurrent5703(owners, lease, stamp)
            || !CharacterCareerSpiritFetteringTransaction.IsValidRequest(request))
            return new(CharacterCareerSpiritFetteringOutcome.Conflict, Error: "spirit_fettering_request_invalid");
        try
        {
            string? path = TryGetPath(owner, request.WorkspaceId);
            if (path is null) return new(CharacterCareerSpiritFetteringOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, request.WorkspaceId, path, owners, lease, stamp,
                out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved)
            { var failure = CharacterCareerSpiritFetteringService.MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
            if (!CharacterCareerSpiritFetteringTransaction.IsValidHistory(saved))
                return new(CharacterCareerSpiritFetteringOutcome.Corrupt, Error: "spirit_fettering_history_invalid");
            // Do not silently quote a second edit for an already-used operation.
            if (saved.Document.AuxiliaryState.CharacterCareerSpiritFetteringReceipts?
                .Any(row => row.Command.Request.OperationId == request.OperationId) == true)
                return new(CharacterCareerSpiritFetteringOutcome.IdempotencyConflict, Error: "spirit_fettering_operation_already_used");
            var effective = LearningSources5703(current, sources);
            string? slot = ReadContinuationSlotUnderLease(path);
            var imported = LearningContext5703(current, saved);
            if (!CharacterCareerSpiritFetteringTransaction.TryReadCurrent5724(stamp, saved, request, effective,
                LearningRuntime5703(CareerSpiritFetteringRuntimeDigest, current, slot, stamp), imported, out var snapshot, out string error)
                || !CharacterCareerSpiritFetteringTransaction.TryPreview(snapshot!, request, out var preview))
                return new(CharacterCareerSpiritFetteringOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            // Check receipt capacity and XML edit feasibility without persisting or claiming confirmation.
            if (!CharacterCareerSpiritFetteringTransaction.TryBuild(stamp, saved, snapshot!,
                preview!.Command with { ExplicitlyConfirmed = true }, out var replacement, out var receipt)
                || !CharacterCareerSpiritFetteringTransaction.IsSourceCurrent(stamp, saved, effective, request, snapshot!))
                return new(CharacterCareerSpiritFetteringOutcome.Unavailable, Error: "spirit_fettering_edit_unavailable");
            if (current is not null)
            {
                var proposed = saved with { Document = replacement!, ContentRevision = receipt!.CommittedWorkspaceRevision,
                    SavedRevision = receipt.CommittedWorkspaceRevision };
                var admission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                    _retainedCharacterQueries!, proposed);
                if (admission.AfterCapture is null || admission.AfterExecution is null || admission.Blockers.Count != 0)
                    return new(CharacterCareerSpiritFetteringOutcome.Unavailable, Error: "spirit_fettering_proposed_environment_unavailable");
            }
            current?.ValidateReadObservation();
            if (!LearningOwnerCurrent5703(owners, lease, stamp) || slot != ReadContinuationSlotUnderLease(path))
                return new(CharacterCareerSpiritFetteringOutcome.Conflict, Error: "spirit_fettering_owner_changed");
            return new(CharacterCareerSpiritFetteringOutcome.Available, preview, saved.ContentRevision);

        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(CharacterCareerSpiritFetteringOutcome.Unavailable, Error: "spirit_fettering_current_sources_unavailable"); }
    }
}
