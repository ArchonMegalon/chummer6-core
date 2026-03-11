using System.Text.Json;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Infrastructure.Files;

public sealed class FileRulePackPublicationStore : IRulePackPublicationStore
{
    private readonly string _stateDirectory;

    public FileRulePackPublicationStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public IReadOnlyList<RulePackPublicationRecord> List(OwnerScope owner, string? rulesetId = null)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        return Load(owner)
            .Where(record => normalizedRulesetId is null
                || string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal))
            .ToArray();
    }

    public RulePackPublicationRecord? Get(OwnerScope owner, string packId, string version, string rulesetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);

        return Load(owner).FirstOrDefault(
            record => string.Equals(record.PackId, packId, StringComparison.Ordinal)
                && string.Equals(record.Version, version, StringComparison.Ordinal)
                && string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal));
    }

    public RulePackPublicationRecord Upsert(OwnerScope owner, RulePackPublicationRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.PackId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Version);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(record.RulesetId);
        RulePackPublicationRecord normalizedRecord = record with
        {
            RulesetId = normalizedRulesetId,
            Publication = record.Publication with
            {
                PublisherId = NormalizeOptionalPublisherId(record.Publication.PublisherId)
            }
        };

        List<RulePackPublicationRecord> records = Load(owner).ToList();
        int existingIndex = records.FindIndex(
            current => string.Equals(current.PackId, normalizedRecord.PackId, StringComparison.Ordinal)
                && string.Equals(current.Version, normalizedRecord.Version, StringComparison.Ordinal)
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

    private IReadOnlyList<RulePackPublicationRecord> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        List<RulePackPublicationRecord>? records = JsonSerializer.Deserialize<List<RulePackPublicationRecord>>(File.ReadAllText(path));
        return records ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<RulePackPublicationRecord> records)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(records));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "rulepacks", "publication-metadata.json");
    }

    private static string? NormalizeOptionalPublisherId(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();
}
