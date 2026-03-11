using System.Text.Json;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Infrastructure.Files;

public sealed class FileRulePackInstallStateStore : IRulePackInstallStateStore
{
    private readonly string _stateDirectory;

    public FileRulePackInstallStateStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public IReadOnlyList<RulePackInstallRecord> List(OwnerScope owner, string? rulesetId = null)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        return Load(owner)
            .Where(record => normalizedRulesetId is null
                || string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal))
            .ToArray();
    }

    public RulePackInstallRecord? Get(OwnerScope owner, string packId, string version, string rulesetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);

        return Load(owner).FirstOrDefault(
            record => string.Equals(record.PackId, packId, StringComparison.Ordinal)
                && string.Equals(record.Version, version, StringComparison.Ordinal)
                && string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal));
    }

    public RulePackInstallRecord Upsert(OwnerScope owner, RulePackInstallRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.PackId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Version);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(record.RulesetId);
        RulePackInstallRecord normalizedRecord = record with { RulesetId = normalizedRulesetId };

        List<RulePackInstallRecord> records = Load(owner).ToList();
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

    private IReadOnlyList<RulePackInstallRecord> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        List<RulePackInstallRecord>? records = JsonSerializer.Deserialize<List<RulePackInstallRecord>>(File.ReadAllText(path));
        return records ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<RulePackInstallRecord> records)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(records));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "rulepacks", "install-state.json");
    }
}
