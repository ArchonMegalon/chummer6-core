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

    public CommandResult<WorkspaceContinuationSnapshot> ReadContinuation(CharacterWorkspaceId id) =>
        ReadContinuationCore(OwnerScope.LocalSingleUser, id);

    public CommandResult<WorkspaceContinuationSnapshot> ReadContinuation(OwnerScope owner, CharacterWorkspaceId id) =>
        IsInvalidScopedOwner(owner)
            ? ContinuationFailure(WorkspaceOperationOutcome.Unavailable)
            : ReadContinuationCore(owner, id);

    private CommandResult<WorkspaceContinuationSnapshot> ReadContinuationCore(OwnerScope owner, CharacterWorkspaceId id)
    {
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
            // The ledger validator proved that the entry's two private hash fields
            // equal the public receipt fields. Reusing the existing receipt loses
            // no replay identity and does not create a second audit contract.
            return new(true, new(owner.NormalizedValue,
                new(stored.Id, stored.Document, stored.LastUpdatedUtc,
                    stored.ContentRevision, stored.SavedRevision),
                ledger.Select(entry => entry.Receipt).ToArray())
                { DelegatedGmHistorySegmentStarts = stored.DelegatedGmHistorySegmentStarts.ToArray() }, null,
                WorkspaceOperationOutcome.Success);
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
