using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;

namespace Chummer.Infrastructure.Workspaces;

public sealed class FileWorkspaceStore : IWorkspaceStore
{
    private const int CurrentWorkspaceSchemaVersion = 1;
    private const string WorkspacePayloadKind = "workspace";
    private readonly string _stateDirectory;

    public FileWorkspaceStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
        Directory.CreateDirectory(GetWorkspaceDirectory(OwnerScope.LocalSingleUser));
    }

    public CharacterWorkspaceId Create(WorkspaceDocument document)
    {
        return Create(OwnerScope.LocalSingleUser, document);
    }

    public CharacterWorkspaceId Create(OwnerScope owner, WorkspaceDocument document)
    {
        string id = Guid.NewGuid().ToString("N");
        CharacterWorkspaceId workspaceId = new(id);
        Save(owner, workspaceId, document);
        return workspaceId;
    }

    public IReadOnlyList<WorkspaceStoreEntry> List()
    {
        return List(OwnerScope.LocalSingleUser);
    }

    public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner)
    {
        string workspaceDirectory = GetWorkspaceDirectory(owner);
        if (!Directory.Exists(workspaceDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(workspaceDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new
            {
                FileName = Path.GetFileNameWithoutExtension(path),
                LastUpdatedUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.FileName))
            .OrderByDescending(item => item.LastUpdatedUtc)
            .Select(item => new WorkspaceStoreEntry(
                Id: new CharacterWorkspaceId(item.FileName),
                LastUpdatedUtc: item.LastUpdatedUtc))
            .Where(entry => TryGetPath(owner, entry.Id) is not null)
            .ToArray();
    }

    public bool TryGet(CharacterWorkspaceId id, out WorkspaceDocument document)
    {
        return TryGet(OwnerScope.LocalSingleUser, id, out document);
    }

    public bool TryGet(OwnerScope owner, CharacterWorkspaceId id, out WorkspaceDocument document)
    {
        string? path = TryGetPath(owner, id);
        if (path is null || !File.Exists(path))
        {
            document = null!;
            return false;
        }

        PersistedWorkspaceRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<PersistedWorkspaceRecord>(File.ReadAllText(path));
        }
        catch (IOException)
        {
            document = null!;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            document = null!;
            return false;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }

        if (record is null)
        {
            document = null!;
            return false;
        }

        string? content = ResolveContent(record);

        if (string.IsNullOrWhiteSpace(content))
        {
            document = null!;
            return false;
        }

        WorkspaceDocumentFormat format = ParseFormat(record.Format);
        string rulesetId = ResolveRulesetId(record);
        WorkspaceDocumentState state = ResolveState(record, content, rulesetId);
        document = new WorkspaceDocument(state, format);
        return true;
    }

    public void Save(CharacterWorkspaceId id, WorkspaceDocument document)
    {
        Save(OwnerScope.LocalSingleUser, id, document);
    }

    public void Save(OwnerScope owner, CharacterWorkspaceId id, WorkspaceDocument document)
    {
        string? path = TryGetPath(owner, id);
        if (path is null)
            throw new InvalidOperationException("Workspace id contains unsupported characters.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        PersistedWorkspaceRecord record = new(document.Format.ToString())
        {
            Envelope = NormalizeEnvelope(document.State)
        };
        string tempPath = $"{path}.tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(record));
        File.Move(tempPath, path, overwrite: true);
    }

    public bool Delete(CharacterWorkspaceId id)
    {
        return Delete(OwnerScope.LocalSingleUser, id);
    }

    public bool Delete(OwnerScope owner, CharacterWorkspaceId id)
    {
        string? path = TryGetPath(owner, id);
        if (path is null || !File.Exists(path))
            return false;

        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string? TryGetPath(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
            return null;

        foreach (char character in id.Value)
        {
            if (!(char.IsLetterOrDigit(character) || character is '-' or '_'))
                return null;
        }

        return Path.Combine(GetWorkspaceDirectory(owner), $"{id.Value}.json");
    }

    private string GetWorkspaceDirectory(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        return Path.Combine(ownerDirectory, "workspaces");
    }

    private static WorkspaceDocumentFormat ParseFormat(string? format)
    {
        if (Enum.TryParse(format, ignoreCase: true, out WorkspaceDocumentFormat parsed))
            return parsed;

        return WorkspaceDocumentFormat.NativeXml;
    }

    private static string? ResolveContent(PersistedWorkspaceRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.Envelope?.Payload))
        {
            return record.Envelope.Payload;
        }

        if (!string.IsNullOrWhiteSpace(record.Content))
        {
            return record.Content;
        }

        return record.Xml;
    }

    private static string ResolveRulesetId(PersistedWorkspaceRecord record)
    {
        return RulesetDefaults.NormalizeOptional(record.Envelope?.RulesetId)
            ?? RulesetDefaults.NormalizeOptional(record.RulesetId)
            ?? WorkspaceRulesetDetection.Detect(record.Envelope?.PayloadKind, record.Envelope?.Payload ?? record.Content ?? record.Xml)
            ?? string.Empty;
    }

    private static WorkspaceDocumentState ResolveState(
        PersistedWorkspaceRecord record,
        string content,
        string fallbackRulesetId)
    {
        WorkspacePayloadEnvelope? envelope = record.Envelope;
        string normalizedRulesetId = RulesetDefaults.NormalizeOptional(envelope?.RulesetId)
            ?? RulesetDefaults.NormalizeOptional(fallbackRulesetId)
            ?? WorkspaceRulesetDetection.Detect(envelope?.PayloadKind, envelope?.Payload ?? content)
            ?? string.Empty;
        int schemaVersion = envelope?.SchemaVersion is > 0
            ? envelope.SchemaVersion
            : CurrentWorkspaceSchemaVersion;
        string payloadKind = string.IsNullOrWhiteSpace(envelope?.PayloadKind)
            ? WorkspacePayloadKind
            : envelope.PayloadKind;
        string payload = envelope?.Payload ?? content;
        return new WorkspaceDocumentState(
            rulesetId: normalizedRulesetId,
            schemaVersion: schemaVersion,
            payloadKind: payloadKind,
            payload: payload);
    }

    private static WorkspacePayloadEnvelope NormalizeEnvelope(WorkspaceDocumentState state)
    {
        int schemaVersion = state.SchemaVersion > 0
            ? state.SchemaVersion
            : CurrentWorkspaceSchemaVersion;
        string payloadKind = string.IsNullOrWhiteSpace(state.PayloadKind)
            ? WorkspacePayloadKind
            : state.PayloadKind;
        return new WorkspacePayloadEnvelope(
            RulesetId: state.RulesetId,
            SchemaVersion: schemaVersion,
            PayloadKind: payloadKind,
            Payload: state.Payload);
    }

    private sealed record PersistedWorkspaceRecord(string Format)
    {
        public WorkspacePayloadEnvelope? Envelope { get; init; }

        // Backward compatibility for older persisted payloads.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Content { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RulesetId { get; init; }

        // Backward compatibility for legacy persisted payloads.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Xml { get; init; }
    }
}
