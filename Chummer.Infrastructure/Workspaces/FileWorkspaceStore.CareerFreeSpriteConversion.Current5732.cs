using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerFreeSpriteConversionCurrentEnvironmentCapability
{
    public WorkspaceStoreReadResult ReadCareerFreeSpriteConversionWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id)
    {
        var stamp = lease.Stamp;
        if (!CareerFreeSpriteConversionGuardsInstalled || !LearningOwnerCurrent5703(owners, lease, stamp)) return UnavailableRead();
        try
        {
            string? path = TryGetPath(stamp.Owner, id);
            if (path is null) return new(WorkspaceOperationOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadFreeSpriteConversionUnderLease(owners, lease, stamp, id, path, out _, out var environment);
            using var current = environment;
            return read;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return UnavailableRead(); }
    }

    // The caller owns the operation lease and must dispose this observation before
    // publication readback. No evaluation/capture is reused across that boundary.
    private WorkspaceStoreReadResult ReadFreeSpriteConversionUnderLease(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, OwnerContextStamp stamp, CharacterWorkspaceId id, string path,
        out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> delegated, out GearCurrentEnvironment? current)
    {
        delegated = []; current = null;
        if (!CareerFreeSpriteConversionGuardsInstalled || !LearningOwnerCurrent5703(owners, lease, stamp)) return UnavailableRead();
        GearCurrentEnvironment? pending = null;
        try
        {
            var read = ReadGearWorkspaceCoreUnderLease(stamp.Owner, id, path, owners, lease, stamp,
                deferFreeSpriteReadObservation: true, out delegated, out pending);
            if (!read.Success || read.Value is not { } saved) return read;
            if (!CharacterCareerFreeSpriteConversionTransaction.IsValidHistory(saved))
                return new(WorkspaceOperationOutcome.Corrupt, Error: "free_sprite_conversion_history_invalid");
            // The inner read evaluation has ended. No unchecked retained read can
            // escape this helper, including public reads and publication readback.
            pending?.ValidateFreeSpriteReadObservation();
            if (!LearningOwnerCurrent5703(owners, lease, stamp)) return UnavailableRead();
            current = pending;
            pending = null; // Transfer only the final checked observation to the caller.
            return read;
        }
        finally
        {
            // Includes history refusal, changed owner, source failure and exceptions.
            pending?.Dispose();
        }
    }

    public CharacterCareerFreeSpriteConversionPreviewResult PreviewCareerFreeSpriteConversion(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerFreeSpriteConversionRequest request, ICharacterSourceDataResolver sources)
    {
        var stamp = lease.Stamp; var owner = stamp.Owner;
        if (!CareerFreeSpriteConversionGuardsInstalled || !LearningOwnerCurrent5703(owners, lease, stamp)
            || !CharacterCareerFreeSpriteConversionTransaction.IsValidRequest(request))
            return new(CharacterCareerFreeSpriteConversionOutcome.Conflict, Error: "free_sprite_conversion_request_invalid");
        try
        {
            string? path = TryGetPath(owner, request.WorkspaceId);
            if (path is null) return new(CharacterCareerFreeSpriteConversionOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path);
            var read = ReadGearWorkspaceUnderLease(owner, request.WorkspaceId, path, owners, lease, stamp,
                out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved)
            { var failure = CharacterCareerFreeSpriteConversionService.MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
            if (!CharacterCareerFreeSpriteConversionTransaction.IsValidHistory(saved))
                return new(CharacterCareerFreeSpriteConversionOutcome.Corrupt, Error: "free_sprite_conversion_history_invalid");
            // Do not silently quote a second edit for an already-used operation.
            if (saved.Document.AuxiliaryState.CharacterCareerFreeSpriteConversionReceipts?
                .Any(row => row.Command.Request.OperationId == request.OperationId) == true)
                return new(CharacterCareerFreeSpriteConversionOutcome.IdempotencyConflict, Error: "free_sprite_conversion_operation_already_used");
            var effective = LearningSources5703(current, sources);
            string? slot = ReadContinuationSlotUnderLease(path);
            var imported = LearningContext5703(current, saved);
            if (!CharacterCareerFreeSpriteConversionTransaction.TryReadCurrent5732(stamp, saved, request, effective,
                LearningRuntime5703(CareerFreeSpriteConversionRuntimeDigest, current, slot, stamp), imported, out var snapshot, out string error)
                || !CharacterCareerFreeSpriteConversionTransaction.TryPreview(snapshot!, request, out var preview))
                return new(CharacterCareerFreeSpriteConversionOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            // Check receipt capacity and XML edit feasibility without persisting or claiming confirmation.
            if (!CharacterCareerFreeSpriteConversionTransaction.TryBuild(stamp, saved, snapshot!,
                preview!.Command with { ExplicitlyConfirmed = true }, out var replacement, out var receipt)
                || !CharacterCareerFreeSpriteConversionTransaction.IsSourceCurrent(stamp, saved, effective, request, snapshot!))
                return new(CharacterCareerFreeSpriteConversionOutcome.Unavailable, Error: "free_sprite_conversion_edit_unavailable");
            if (current is not null)
            {
                var proposed = saved with { Document = replacement!, ContentRevision = receipt!.CommittedWorkspaceRevision,
                    SavedRevision = receipt.CommittedWorkspaceRevision };
                var admission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                    _retainedCharacterQueries!, proposed);
                if (admission.AfterCapture is null || admission.AfterExecution is null || admission.Blockers.Count != 0)
                {
                    return new(CharacterCareerFreeSpriteConversionOutcome.Unavailable, Error: "free_sprite_conversion_proposed_environment_unavailable");
                }
            }
            current?.ValidateReadObservation();
            if (!LearningOwnerCurrent5703(owners, lease, stamp) || slot != ReadContinuationSlotUnderLease(path))
                return new(CharacterCareerFreeSpriteConversionOutcome.Conflict, Error: "free_sprite_conversion_owner_changed");
            return new(CharacterCareerFreeSpriteConversionOutcome.Available, preview, saved.ContentRevision);

        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(CharacterCareerFreeSpriteConversionOutcome.Unavailable, Error: "free_sprite_conversion_current_sources_unavailable"); }
    }
}
