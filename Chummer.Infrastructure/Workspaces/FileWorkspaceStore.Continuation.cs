using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : IWorkspaceContinuationReadCapability
{
    private static readonly JsonSerializerOptions ContinuationReadOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public bool SupportsWorkspaceContinuationRead => true;

    public CommandResult<WorkspaceContinuationRestoreTarget> ReadContinuationStorageTarget(
        OwnerScope owner, CharacterWorkspaceId id)
    {
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner))
            return new(false, null, "Workspace storage identity is unavailable.", WorkspaceOperationOutcome.Unavailable);
        // Retained private environments keep their canonical read authority. An
        // ordinary continuation read must never bypass that admission boundary.
        if (SupportsWorkspacePrivateRuleSourceClosureRestore)
        {
            var inspected = InspectPrivateClosureRestoreTarget(owner, id).Result;
            return inspected.Outcome == WorkspaceContinuationRestoreOutcome.Available && inspected.Target is { Exists: true }
                ? new(true, inspected.Target, null, WorkspaceOperationOutcome.Success)
                : new(false, null, "Private workspace storage identity is unavailable.", WorkspaceOperationOutcome.Unavailable);
        }
        var read = ReadContinuationCore(owner, id, out var target, captureStorageTarget: true);
        return read.Success && target is not null
            ? new(true, target, null, WorkspaceOperationOutcome.Success)
            : new(false, null, "Workspace storage identity is unavailable.", read.Outcome);
    }

    public CommandResult<WorkspaceContinuationSnapshot> ReadContinuation(CharacterWorkspaceId id) =>
        ReadContinuationCore(OwnerScope.LocalSingleUser, id);

    public CommandResult<WorkspaceContinuationSnapshot> ReadContinuation(OwnerScope owner, CharacterWorkspaceId id) =>
        IsInvalidScopedOwner(owner)
            ? ContinuationFailure(WorkspaceOperationOutcome.Unavailable)
            : ReadContinuationCore(owner, id);

    private CommandResult<WorkspaceContinuationSnapshot> ReadContinuationCore(OwnerScope owner, CharacterWorkspaceId id)
        => WithSessionPlayExport5712(owner, id, reader => ReadContinuationCore(owner, id, out _, playReader5712: reader));

    private CommandResult<WorkspaceContinuationSnapshot> ReadContinuationCore(OwnerScope owner, CharacterWorkspaceId id,
        out WorkspaceContinuationRestoreTarget? target, bool captureStorageTarget = false,
        Chummer.Application.Session.ISessionPlayReader? playReader5712 = null)
    {
        target = null;
        string? path = TryGetPath(owner, id);
        if (path is null)
            return ContinuationFailure(WorkspaceOperationOutcome.Missing);
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return ContinuationFailure(IsConfirmedMissingInventoryDirectory(GetWorkspaceDirectory(owner))
                    ? WorkspaceOperationOutcome.Missing : WorkspaceOperationOutcome.Unavailable);

            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            WorkspaceStoreReadResult read = ReadWorkspaceUnderLease(
                owner, id, path, out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> ledger,
                continuationRead: true);
            if (!read.Success || read.Value is null)
                return ContinuationFailure(read.Outcome);

            WorkspaceStoredDocument stored = read.Value;
            if (Chummer.Application.Session.SessionAmmunitionSettlementRules5712.RequiresSettlement(playReader5712,
                Chummer.Application.Session.SessionAmmunitionSettlementRules5712.Identity(stored.Document.AuxiliaryState.SessionPlaySettlement5712)))
                return new(false, null, "Save pending Play ammunition through After Run before exporting.", WorkspaceOperationOutcome.Conflict);
            // The ledger validator proved that the entry's two private hash fields
            // equal the public receipt fields. Reusing the existing receipt loses
            // no replay identity and does not create a second audit contract.
            var snapshot = new WorkspaceContinuationSnapshot(owner.NormalizedValue,
                new(stored.Id, stored.Document, stored.LastUpdatedUtc,
                    stored.ContentRevision, stored.SavedRevision),
                ledger.Select(entry => entry.Receipt).ToArray())
                { DelegatedGmHistorySegmentStarts = stored.DelegatedGmHistorySegmentStarts.ToArray() };
            if (captureStorageTarget)
            {
                if (stored.LocalHistory is null) return ContinuationFailure(WorkspaceOperationOutcome.Unavailable);
                target = new(owner.NormalizedValue, id, true, stored.LocalHistory.IncarnationId,
                    stored.ContentRevision, stored.SavedRevision, WorkspaceContinuationSnapshotDigest.Compute(snapshot),
                    ReadContinuationSlotUnderLease(path));
            }
            return new(true, snapshot, null, WorkspaceOperationOutcome.Success);
        }
        catch (IOException)
        {
            return ContinuationFailure(WorkspaceOperationOutcome.Unavailable);
        }
        catch (UnauthorizedAccessException)
        {
            return ContinuationFailure(WorkspaceOperationOutcome.Unavailable);
        }
    }

    private static CommandResult<WorkspaceContinuationSnapshot> ContinuationFailure(WorkspaceOperationOutcome outcome) =>
        new(false, null, "Complete workspace continuation state is not available.", outcome);

    private static PersistedWorkspaceRecord? ReadExactContinuationRecord(Stream stream)
    {
        using JsonDocument document = JsonDocument.Parse(stream);
        RequireUniqueContinuationProperties(document.RootElement);
        PersistedWorkspaceRecord? record = document.RootElement.Deserialize<PersistedWorkspaceRecord>(ContinuationReadOptions);
        if (record is { RecordSchemaVersion: CurrentWorkspaceRecordSchemaVersion }
            && (record.Envelope is null || record.Content is not null || record.Xml is not null || record.RulesetId is not null))
            throw new JsonException("Ambiguous continuation record envelope.");
        return record;
    }

    private void RefuseLegacyContinuationDirectory(OwnerScope owner, string ownerDirectory)
    {
        if (!OwnerScopedStatePath.TryResolveContainedLegacyOwnerDirectory(_stateDirectory, owner, out string legacyOwner)
            || PathComparer.Equals(legacyOwner, ownerDirectory))
            return;
        ThrowIfLinkOrReparsePoint(legacyOwner, "legacy workspace owner directory");
        if (File.Exists(legacyOwner))
            throw new IOException("Legacy workspace owner path is not a directory.");
        string legacyWorkspaces = Path.Combine(legacyOwner, "workspaces");
        // Confirm absence rather than treating an inaccessible path as empty.
        if (!IsConfirmedMissingInventoryDirectory(legacyWorkspaces))
            throw new IOException("Legacy workspace directory requires an explicit owner-authorized migration before export.");
    }

    private static void RequireUniqueContinuationProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new JsonException("Duplicate continuation state property.");
                RequireUniqueContinuationProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                RequireUniqueContinuationProperties(item);
        }
    }
}
