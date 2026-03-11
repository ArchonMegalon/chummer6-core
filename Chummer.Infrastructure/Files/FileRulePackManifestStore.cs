using System.Text.Json;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Infrastructure.Files;

public sealed class FileRulePackManifestStore : IRulePackManifestStore
{
    private readonly string _stateDirectory;

    public FileRulePackManifestStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public IReadOnlyList<RulePackManifestRecord> List(OwnerScope owner, string? rulesetId = null)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        return Load(owner)
            .Where(record => normalizedRulesetId is null
                || record.Manifest.Targets.Contains(normalizedRulesetId, StringComparer.Ordinal))
            .ToArray();
    }

    public RulePackManifestRecord? Get(OwnerScope owner, string packId, string version, string rulesetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);

        return List(owner, normalizedRulesetId).FirstOrDefault(
            record => string.Equals(record.Manifest.PackId, packId, StringComparison.Ordinal)
                && string.Equals(record.Manifest.Version, version, StringComparison.Ordinal));
    }

    public RulePackManifestRecord Upsert(OwnerScope owner, RulePackManifestRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Manifest.PackId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Manifest.Version);
        string[] normalizedTargets = record.Manifest.Targets
            .Select(RulesetDefaults.NormalizeRequired)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedTargets.Length == 0)
        {
            throw new ArgumentException("RulePack manifest must target at least one ruleset.", nameof(record));
        }

        RulePackManifestRecord normalizedRecord = record with
        {
            Manifest = record.Manifest with
            {
                Targets = normalizedTargets
            }
        };

        List<RulePackManifestRecord> records = Load(owner).ToList();
        int existingIndex = records.FindIndex(current =>
            string.Equals(current.Manifest.PackId, normalizedRecord.Manifest.PackId, StringComparison.Ordinal)
            && string.Equals(current.Manifest.Version, normalizedRecord.Manifest.Version, StringComparison.Ordinal));
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

    private IReadOnlyList<RulePackManifestRecord> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        List<RulePackManifestRecord>? records = JsonSerializer.Deserialize<List<RulePackManifestRecord>>(File.ReadAllText(path));
        return records ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<RulePackManifestRecord> records)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(records));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "rulepacks", "manifests.json");
    }
}
