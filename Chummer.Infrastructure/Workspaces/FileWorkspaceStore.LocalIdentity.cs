using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : IWorkspaceLocalIdentityReadCapability
{
    public CommandResult<WorkspaceLocalIdentityObservation> ReadLocalIdentity(
        OwnerScope owner, CharacterWorkspaceId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (owner.UsesLocalSingleUserValue && !owner.IsLocalSingleUser
            || !owner.IsLocalSingleUser && IsInvalidScopedOwner(owner))
            return LocalIdentityFailure(WorkspaceOperationOutcome.Unavailable);
        owner = owner.IsLocalSingleUser ? OwnerScope.LocalSingleUser : new(owner.NormalizedValue);
        if (TryGetPath(owner, id) is not { } path)
            return LocalIdentityFailure(WorkspaceOperationOutcome.Missing);
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return LocalIdentityFailure(WorkspaceOperationOutcome.Unavailable);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            cancellationToken.ThrowIfCancellationRequested();
            using var history = WorkspacePrivateRuleSourceClosureCompletion.BeginHistoryObservation();
            var before = ReadLocalIdentityRecord(path);
            // Reject migration before the ordinary reader can perform it. Identity
            // observation never invents revisions, provenance or a new checkpoint.
            if (before.RecordSchemaVersion is null or < 3
                || !TryMaterializeRecord(before, out var document, out _, out _, out bool migration)
                || migration || before.Envelope != document.PayloadEnvelope)
                return LocalIdentityFailure(WorkspaceOperationOutcome.Unavailable);
            string beforeDigest = CharacterCreationFinalizationDigest.Compute(before);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var ledger);
            if (!read.Success || read.Value is not { } stored)
                return LocalIdentityFailure(read.Outcome);
            var record = ReadLocalIdentityRecord(path);
            if (CharacterCreationFinalizationDigest.Compute(record) != beforeDigest)
                return LocalIdentityFailure(WorkspaceOperationOutcome.Conflict);
            // The ordinary reader admits exact receipts/source uses/campaign
            // history and local delegated-GM segments. Reopen retained inputs for
            // a final source fence; no private closure serialization or export.
            using var recovery = OpenRecordRuleSources(owner, id, record);
            var snapshot = new WorkspaceContinuationSnapshot(owner.NormalizedValue,
                new(stored.Id, stored.Document, stored.LastUpdatedUtc, stored.ContentRevision, stored.SavedRevision),
                ledger.Select(entry => entry.Receipt).ToArray())
            { DelegatedGmHistorySegmentStarts = stored.DelegatedGmHistorySegmentStarts.ToArray() };
            var drafts = stored.Document.AuxiliaryState.CharacterCreationCompletionDrafts;
            string? localOrigin = owner.IsLocalSingleUser
                && stored.LocalHistory is { ImportedThroughRevision: 0, ImportedSnapshotDigest: null,
                    LastRestore: null, LocalAdoption: null } origin
                && origin.IsValid(stored.ContentRevision) && !stored.HasDelegatedGmHistory
                && stored.DelegatedGmHistorySegmentStarts.Count == 0
                ? CharacterCreationFinalizationDigest.Compute(new
                {
                    Schema = "chummer.local-unimported-workspace-history/v1", OwnerId = owner.NormalizedValue,
                    WorkspaceId = id, origin.IncarnationId
                }) : null;
            var result = new WorkspaceLocalIdentityObservation(owner.NormalizedValue, id,
                stored.ContentRevision, stored.SavedRevision, WorkspaceContinuationSnapshotDigest.Compute(snapshot),
                CharacterCreationFinalizationDigest.ComputeUtf8(stored.Document.Content),
                stored.Document.AuxiliaryStateDigest,
                drafts is { Count: > 0 } ? drafts[^1].Baseline.Review.Review.Request.Selections.Loadout is not null : null,
                localOrigin)
            {
                CreationReservation = !owner.IsLocalSingleUser
                    && stored.LocalHistory is { ImportedThroughRevision: 0, ImportedSnapshotDigest: null,
                        LastRestore: null, LocalAdoption: null, CreationReservation: { } reservation }
                    && stored.LocalHistory.IncarnationId == reservation.IncarnationId
                    && reservation.OwnerId == owner.NormalizedValue && reservation.WorkspaceId == id
                    && CharacterCreationReservation.IsValid(reservation)
                    && HasCreationReservationMarker(path, reservation)
                    ? reservation : null
            };
            cancellationToken.ThrowIfCancellationRequested();
            if (recovery is not null && !recovery.ValidateRetainedRuleSources()
                || CharacterCreationFinalizationDigest.Compute(ReadLocalIdentityRecord(path)) != beforeDigest)
                return LocalIdentityFailure(WorkspaceOperationOutcome.Conflict);
            cancellationToken.ThrowIfCancellationRequested();
            return new(true, result, null, WorkspaceOperationOutcome.Success);
        }
        catch (FileNotFoundException) { return LocalIdentityFailure(WorkspaceOperationOutcome.Missing); }
        catch (Exception error) when (error is InvalidDataException || WorkspacePrivateRuleSourceClosure.IsFailure(error))
        { return LocalIdentityFailure(WorkspaceOperationOutcome.Unavailable); }
    }

    private PersistedWorkspaceRecord ReadLocalIdentityRecord(string path)
    {
        ThrowIfLinkOrReparsePoint(path, "workspace target");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumRetainedWorkspaceRecordBytes)
            throw new InvalidOperationException("Local workspace identity is unavailable.");
        return ReadExactContinuationRecord(stream)
            ?? throw new JsonException("Local workspace identity is unavailable.");
    }

    private static CommandResult<WorkspaceLocalIdentityObservation> LocalIdentityFailure(WorkspaceOperationOutcome outcome) =>
        new(false, null, "Local workspace identity is unavailable.",
            outcome == WorkspaceOperationOutcome.Success ? WorkspaceOperationOutcome.Unavailable : outcome);
}
