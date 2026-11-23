using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerFocusBondingCurrentEnvironmentCapability
{
    // Original-operation history is observed without source activation, migration,
    // temp-file repair or mutation. It cannot quote or authorize a fresh operation.
    public CharacterCareerFocusBondingResult LookupCareerFocusBonding(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, Guid operationId, string commandDigest)
    {
        var stamp = lease.Stamp;
        if (!LearningOwnerCurrent5703(owners, lease, stamp)
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id)
            || operationId == Guid.Empty || !CharacterCareerFocusBondingTransaction.IsDigest(commandDigest))
            return new(CharacterCareerFocusBondingOutcome.Conflict);
        try
        {
            string? path = TryGetPath(stamp.Owner, id);
            if (path is null || !File.Exists(path)) return new(CharacterCareerFocusBondingOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var result = LookupFocusBondingUnderLease6495(path, stamp.Owner, id, operationId, commandDigest);
            return LearningOwnerCurrent5703(owners, lease, stamp) ? result : new(CharacterCareerFocusBondingOutcome.Conflict);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(CharacterCareerFocusBondingOutcome.Unavailable, Error: "focus_bonding_history_unavailable"); }
    }

    private CharacterCareerFocusBondingResult LookupFocusBondingUnderLease6495(string path,
        Chummer.Contracts.Owners.OwnerScope owner, CharacterWorkspaceId id, Guid operationId, string digest)
    {
        var record = ReadPrivateRecord(path);
        if (!TryMaterializeRecord(record, out var document, out long revision, out long savedRevision, out bool migration))
            return new(CharacterCareerFocusBondingOutcome.Corrupt, Error: "focus_bonding_history_invalid");
        // A valid pre-ledger ordinary save has no operation to recover. Its normal
        // domain read still owns legacy migration; historical lookup never writes.
        if ((migration || record.LocalHistory is null) && record.RetainedRuleSources is null
            && document.AuxiliaryState.CharacterCareerFocusBondingReceipts is not { Count: > 0 })
            return new(CharacterCareerFocusBondingOutcome.NotFound, CurrentWorkspaceRevision: revision);
        if (migration || record.LocalHistory is not { } local || !local.IsValid(revision))
            return new(CharacterCareerFocusBondingOutcome.Corrupt, Error: "focus_bonding_history_invalid");
        var saved = new WorkspaceStoredDocument(id, document, revision, savedRevision,
            new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)) { LocalHistory = local };
        return CharacterCareerFocusBondingTransaction.Lookup(owner, saved, operationId, digest);
    }

    public WorkspaceStoreReadResult ReadCareerFocusBondingWorkspace(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id)
    {
        var stamp = lease.Stamp;
        if (!CareerFocusBondingGuardsInstalled || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id)
            || !LearningOwnerCurrent5703(owners, lease, stamp)) return UnavailableRead();
        try
        {
            string? path = TryGetPath(stamp.Owner, id);
            if (path is null) return new(WorkspaceOperationOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var read = ReadGearWorkspaceUnderLease(stamp.Owner, id, path, owners, lease, stamp, out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved) return read;
            if (!CharacterCareerFocusBondingTransaction.IsValidHistory(saved))
                return new(WorkspaceOperationOutcome.Corrupt, Error: "focus_bonding_history_invalid");
            current?.ValidateFreeSpriteReadObservation();
            return LearningOwnerCurrent5703(owners, lease, stamp) ? read : UnavailableRead();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return UnavailableRead(); }
    }

    public CharacterCareerFocusBondingListResult ListCareerFocusBonding(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, long expectedRevision)
    {
        var read = ReadCareerFocusBondingWorkspace(owners, lease, id);
        if (!read.Success || read.Value is not { } saved)
            return new(CharacterCareerFocusBondingService.MapReadFailure(read).Outcome, Error: read.Error);
        if (saved.ContentRevision != expectedRevision || saved.SavedRevision != saved.ContentRevision
            || !CharacterCareerPowerGovernance.Allows(saved)) return new(CharacterCareerFocusBondingOutcome.Conflict);
        try
        {
            var entries = CharacterCareerFocusBondingTransaction.ReadEntries(saved.Document.Content);
            return LearningOwnerCurrent5703(owners, lease, lease.Stamp)
                ? new(CharacterCareerFocusBondingOutcome.Available, entries, saved.ContentRevision)
                : new(CharacterCareerFocusBondingOutcome.Conflict);
        }
        catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        { return new(CharacterCareerFocusBondingOutcome.Unavailable, Error: "focus_bonding_collection_unavailable"); }
    }

    public CharacterCareerFocusBondingPreviewResult PreviewCareerFocusBonding(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerFocusBondingRequest request, ICharacterSourceDataResolver sources)
    {
        var stamp = lease.Stamp; var owner = stamp.Owner;
        if (!CareerFocusBondingGuardsInstalled || !LearningOwnerCurrent5703(owners, lease, stamp)
            || !CharacterCareerFocusBondingTransaction.IsValidRequest(request))
            return new(CharacterCareerFocusBondingOutcome.Conflict, Error: "focus_bonding_request_invalid");
        try
        {
            string? path = TryGetPath(owner, request.WorkspaceId);
            if (path is null) return new(CharacterCareerFocusBondingOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var read = ReadGearWorkspaceUnderLease(owner, request.WorkspaceId, path, owners, lease, stamp,
                out _, out var environment);
            using var current = environment;
            if (!read.Success || read.Value is not { } saved)
            { var failure = CharacterCareerFocusBondingService.MapReadFailure(read); return new(failure.Outcome, Error: failure.Error); }
            if (!CharacterCareerFocusBondingTransaction.IsValidHistory(saved))
                return new(CharacterCareerFocusBondingOutcome.Corrupt, Error: "focus_bonding_history_invalid");
            // Do not silently quote a second edit for an already-used operation.
            if (saved.Document.AuxiliaryState.CharacterCareerFocusBondingReceipts?
                .Any(row => row.Command.Request.OperationId == request.OperationId) == true)
                return new(CharacterCareerFocusBondingOutcome.IdempotencyConflict, Error: "focus_bonding_operation_already_used");
            var effective = LearningSources5703(current, sources);
            string? slot = ReadContinuationSlotUnderLease(path);
            if (!CharacterCareerFocusBondingTransaction.TryRead(stamp, saved, request, effective,
                LearningRuntime5703(CareerFocusBondingRuntimeDigest, current, slot, stamp), out var snapshot, out string error)
                || !CharacterCareerFocusBondingTransaction.TryPreview(snapshot!, request, out var preview))
                return new(CharacterCareerFocusBondingOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
            // Check receipt capacity and XML edit feasibility without persisting or claiming confirmation.
            if (!CharacterCareerFocusBondingTransaction.TryBuild(stamp, saved, snapshot!,
                preview!.Command with { ExplicitlyConfirmed = true }, out var replacement, out var receipt)
                || !CharacterCareerFocusBondingTransaction.IsSourceCurrent(stamp, saved, effective, request, snapshot!))
                return new(CharacterCareerFocusBondingOutcome.Unavailable, Error: "focus_bonding_edit_unavailable");
            if (current is not null)
            {
                var proposed = saved with { Document = replacement!, ContentRevision = receipt!.CommittedWorkspaceRevision,
                    SavedRevision = receipt.CommittedWorkspaceRevision };
                var admission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                    _retainedCharacterQueries!, proposed);
                if (admission.AfterCapture is null || admission.AfterExecution is null || admission.Blockers.Count != 0)
                    return new(CharacterCareerFocusBondingOutcome.Unavailable, Error: "focus_bonding_proposed_environment_unavailable");
            }
            current?.ValidateFreeSpriteReadObservation();
            if (!LearningOwnerCurrent5703(owners, lease, stamp) || slot != ReadContinuationSlotUnderLease(path))
                return new(CharacterCareerFocusBondingOutcome.Conflict, Error: "focus_bonding_owner_changed");
            return new(CharacterCareerFocusBondingOutcome.Available, preview, saved.ContentRevision);

        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(CharacterCareerFocusBondingOutcome.Unavailable, Error: "focus_bonding_current_sources_unavailable"); }
    }
}
