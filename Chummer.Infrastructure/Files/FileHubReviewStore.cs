using System.Text.Json;
using Chummer.Application.Hub;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Infrastructure.Files;

public sealed class FileHubReviewStore : IHubReviewStore
{
    private readonly string _stateDirectory;

    public FileHubReviewStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public IReadOnlyList<HubReviewRecord> List(OwnerScope owner, string? kind = null, string? itemId = null, string? rulesetId = null)
    {
        return ApplyFilters(Load(owner), kind, itemId, rulesetId);
    }

    public IReadOnlyList<HubReviewRecord> ListAll(string? kind = null, string? itemId = null, string? rulesetId = null)
    {
        return ApplyFilters(EnumerateAllRecords(), kind, itemId, rulesetId);
    }

    public HubReviewRecord? Get(OwnerScope owner, string kind, string itemId, string rulesetId)
    {
        string normalizedKind = NormalizeRequired(kind);
        string normalizedItemId = NormalizeItemId(itemId);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);

        return Load(owner).FirstOrDefault(record =>
            string.Equals(record.ProjectKind, normalizedKind, StringComparison.Ordinal)
            && string.Equals(record.ProjectId, normalizedItemId, StringComparison.Ordinal)
            && string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal));
    }

    public HubReviewRecord Upsert(OwnerScope owner, HubReviewRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        HubReviewRecord normalizedRecord = record with
        {
            ProjectKind = NormalizeRequired(record.ProjectKind),
            ProjectId = NormalizeItemId(record.ProjectId),
            RulesetId = RulesetDefaults.NormalizeRequired(record.RulesetId),
            OwnerId = owner.NormalizedValue,
            RecommendationState = NormalizeRequired(record.RecommendationState),
            ReviewText = NormalizeOptional(record.ReviewText)
        };

        List<HubReviewRecord> records = Load(owner).ToList();
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

    private IReadOnlyList<HubReviewRecord> Load(OwnerScope owner)
    {
        return Load(GetPath(owner));
    }

    private IReadOnlyList<HubReviewRecord> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        List<HubReviewRecord>? records = JsonSerializer.Deserialize<List<HubReviewRecord>>(File.ReadAllText(path));
        return records ?? [];
    }

    private IReadOnlyList<HubReviewRecord> ApplyFilters(IEnumerable<HubReviewRecord> records, string? kind, string? itemId, string? rulesetId)
    {
        string? normalizedKind = NormalizeOptional(kind);
        string? normalizedItemId = NormalizeItemIdOptional(itemId);
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);

        return records
            .Where(record => normalizedKind is null || string.Equals(record.ProjectKind, normalizedKind, StringComparison.Ordinal))
            .Where(record => normalizedItemId is null || string.Equals(record.ProjectId, normalizedItemId, StringComparison.Ordinal))
            .Where(record => normalizedRulesetId is null || string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal))
            .ToArray();
    }

    private IEnumerable<HubReviewRecord> EnumerateAllRecords()
    {
        foreach (HubReviewRecord record in Load(Path.Combine(_stateDirectory, "hub", "reviews.json")))
        {
            yield return record;
        }

        string ownersDirectory = Path.Combine(_stateDirectory, "owners");
        if (!Directory.Exists(ownersDirectory))
        {
            yield break;
        }

        foreach (string ownerDirectory in Directory.EnumerateDirectories(ownersDirectory))
        {
            foreach (HubReviewRecord record in Load(Path.Combine(ownerDirectory, "hub", "reviews.json")))
            {
                yield return record;
            }
        }
    }

    private void Save(OwnerScope owner, IReadOnlyList<HubReviewRecord> records)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(records));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "hub", "reviews.json");
    }

    private static string NormalizeRequired(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeItemId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string? NormalizeItemIdOptional(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
