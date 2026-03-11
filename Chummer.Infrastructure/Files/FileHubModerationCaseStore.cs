using System.Text.Json;
using Chummer.Application.Hub;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Infrastructure.Files;

public sealed class FileHubModerationCaseStore : IHubModerationCaseStore
{
    private readonly string _stateDirectory;

    public FileHubModerationCaseStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public IReadOnlyList<HubModerationCaseRecord> List(OwnerScope owner, string? kind = null, string? rulesetId = null, string? state = null)
    {
        string? normalizedKind = NormalizeOptional(kind);
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        string? normalizedState = NormalizeOptional(state);

        return Load(owner)
            .Where(record => normalizedKind is null || string.Equals(record.ProjectKind, normalizedKind, StringComparison.Ordinal))
            .Where(record => normalizedRulesetId is null || string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal))
            .Where(record => normalizedState is null || string.Equals(record.State, normalizedState, StringComparison.Ordinal))
            .ToArray();
    }

    public HubModerationCaseRecord? GetByCaseId(OwnerScope owner, string caseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);

        return Load(owner).FirstOrDefault(record =>
            string.Equals(record.CaseId, caseId.Trim(), StringComparison.Ordinal));
    }

    public HubModerationCaseRecord? GetByDraftId(OwnerScope owner, string draftId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);

        return Load(owner).FirstOrDefault(record =>
            string.Equals(record.DraftId, draftId.Trim(), StringComparison.Ordinal));
    }

    public HubModerationCaseRecord? Get(OwnerScope owner, string kind, string projectId, string rulesetId)
    {
        string normalizedKind = NormalizeRequired(kind);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);
        string normalizedProjectId = NormalizeProjectId(projectId);

        return Load(owner).FirstOrDefault(record =>
            string.Equals(record.ProjectKind, normalizedKind, StringComparison.Ordinal)
            && string.Equals(record.ProjectId, normalizedProjectId, StringComparison.Ordinal)
            && string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal));
    }

    public HubModerationCaseRecord Upsert(OwnerScope owner, HubModerationCaseRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        HubModerationCaseRecord normalizedRecord = record with
        {
            ProjectKind = NormalizeRequired(record.ProjectKind),
            ProjectId = NormalizeProjectId(record.ProjectId),
            RulesetId = RulesetDefaults.NormalizeRequired(record.RulesetId),
            Title = record.Title.Trim(),
            OwnerId = owner.NormalizedValue,
            PublisherId = NormalizeOptionalPublisherId(record.PublisherId),
            State = NormalizeRequired(record.State)
        };

        List<HubModerationCaseRecord> records = Load(owner).ToList();
        int existingIndex = records.FindIndex(current =>
            string.Equals(current.ProjectKind, normalizedRecord.ProjectKind, StringComparison.Ordinal)
            && string.Equals(current.ProjectId, normalizedRecord.ProjectId, StringComparison.Ordinal)
            && string.Equals(current.RulesetId, normalizedRecord.RulesetId, StringComparison.Ordinal));

        if (existingIndex >= 0)
        {
            records[existingIndex] = normalizedRecord;
        }
        else
        {
            records.Add(normalizedRecord);
        }

        Save(owner, records);
        return normalizedRecord;
    }

    public bool DeleteByDraftId(OwnerScope owner, string draftId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);

        List<HubModerationCaseRecord> records = Load(owner).ToList();
        int removed = records.RemoveAll(record =>
            string.Equals(record.DraftId, draftId.Trim(), StringComparison.Ordinal));
        if (removed == 0)
        {
            return false;
        }

        Save(owner, records);
        return true;
    }

    private IReadOnlyList<HubModerationCaseRecord> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        List<HubModerationCaseRecord>? records = JsonSerializer.Deserialize<List<HubModerationCaseRecord>>(File.ReadAllText(path));
        return records ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<HubModerationCaseRecord> records)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(records));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "hub", "moderation-cases.json");
    }

    private static string NormalizeProjectId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    private static string NormalizeRequired(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeRequired(value);

    private static string? NormalizeOptionalPublisherId(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();
}
