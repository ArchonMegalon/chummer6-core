using System.Text.Json;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Infrastructure.Files;

public sealed class FileRuleProfileManifestStore : IRuleProfileManifestStore
{
    private readonly string _stateDirectory;

    public FileRuleProfileManifestStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public IReadOnlyList<RuleProfileManifestRecord> List(OwnerScope owner, string? rulesetId = null)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        return Load(owner)
            .Where(record => normalizedRulesetId is null
                || string.Equals(record.Manifest.RulesetId, normalizedRulesetId, StringComparison.Ordinal))
            .ToArray();
    }

    public RuleProfileManifestRecord? Get(OwnerScope owner, string profileId, string rulesetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);

        return List(owner, normalizedRulesetId).FirstOrDefault(
            record => string.Equals(record.Manifest.ProfileId, profileId, StringComparison.Ordinal));
    }

    public RuleProfileManifestRecord Upsert(OwnerScope owner, RuleProfileManifestRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Manifest.ProfileId);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(record.Manifest.RulesetId);
        RuleProfileManifestRecord normalizedRecord = record with
        {
            Manifest = record.Manifest with
            {
                RulesetId = normalizedRulesetId
            }
        };

        List<RuleProfileManifestRecord> records = Load(owner).ToList();
        int existingIndex = records.FindIndex(current =>
            string.Equals(current.Manifest.ProfileId, normalizedRecord.Manifest.ProfileId, StringComparison.Ordinal)
            && string.Equals(current.Manifest.RulesetId, normalizedRecord.Manifest.RulesetId, StringComparison.Ordinal));
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

    private IReadOnlyList<RuleProfileManifestRecord> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        List<RuleProfileManifestRecord>? records = JsonSerializer.Deserialize<List<RuleProfileManifestRecord>>(File.ReadAllText(path));
        return records ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<RuleProfileManifestRecord> records)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(records));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "profiles", "manifests.json");
    }
}
