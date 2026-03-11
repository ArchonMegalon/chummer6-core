using System.Text.Json;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Infrastructure.Files;

public sealed class FileRuleProfilePublicationStore : IRuleProfilePublicationStore
{
    private readonly string _stateDirectory;

    public FileRuleProfilePublicationStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public IReadOnlyList<RuleProfilePublicationRecord> List(OwnerScope owner, string? rulesetId = null)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        return Load(owner)
            .Where(record => normalizedRulesetId is null
                || string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal))
            .ToArray();
    }

    public RuleProfilePublicationRecord? Get(OwnerScope owner, string profileId, string rulesetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);

        return Load(owner).FirstOrDefault(
            record => string.Equals(record.ProfileId, profileId, StringComparison.Ordinal)
                && string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal));
    }

    public RuleProfilePublicationRecord Upsert(OwnerScope owner, RuleProfilePublicationRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ProfileId);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(record.RulesetId);
        RuleProfilePublicationRecord normalizedRecord = record with
        {
            RulesetId = normalizedRulesetId,
            Publication = record.Publication with
            {
                PublisherId = NormalizeOptionalPublisherId(record.Publication.PublisherId)
            }
        };

        List<RuleProfilePublicationRecord> records = Load(owner).ToList();
        int existingIndex = records.FindIndex(
            current => string.Equals(current.ProfileId, normalizedRecord.ProfileId, StringComparison.Ordinal)
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

    private IReadOnlyList<RuleProfilePublicationRecord> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        List<RuleProfilePublicationRecord>? records = JsonSerializer.Deserialize<List<RuleProfilePublicationRecord>>(File.ReadAllText(path));
        return records ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<RuleProfilePublicationRecord> records)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(records));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "profiles", "publication-metadata.json");
    }

    private static string? NormalizeOptionalPublisherId(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();
}
