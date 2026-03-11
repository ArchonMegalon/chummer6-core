using System.Text.Json;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Infrastructure.Files;

public sealed class FileRulePackInstallHistoryStore : IRulePackInstallHistoryStore
{
    private readonly string _stateDirectory;

    public FileRulePackInstallHistoryStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public IReadOnlyList<RulePackInstallHistoryRecord> List(OwnerScope owner, string? rulesetId = null)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        return Load(owner)
            .Where(record => normalizedRulesetId is null
                || string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal))
            .OrderByDescending(record => record.Entry.AppliedAtUtc)
            .ToArray();
    }

    public IReadOnlyList<RulePackInstallHistoryRecord> GetHistory(OwnerScope owner, string packId, string version, string rulesetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);

        return List(owner, normalizedRulesetId)
            .Where(record => string.Equals(record.PackId, packId, StringComparison.Ordinal)
                && string.Equals(record.Version, version, StringComparison.Ordinal))
            .ToArray();
    }

    public RulePackInstallHistoryRecord Append(OwnerScope owner, RulePackInstallHistoryRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.PackId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Version);
        RulePackInstallHistoryRecord normalizedRecord = record with
        {
            RulesetId = RulesetDefaults.NormalizeRequired(record.RulesetId)
        };

        List<RulePackInstallHistoryRecord> records = Load(owner).ToList();
        records.Add(normalizedRecord);
        Save(owner, records);
        return normalizedRecord;
    }

    private IReadOnlyList<RulePackInstallHistoryRecord> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        List<RulePackInstallHistoryRecord>? records = JsonSerializer.Deserialize<List<RulePackInstallHistoryRecord>>(File.ReadAllText(path));
        return records ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<RulePackInstallHistoryRecord> records)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(records));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "rulepacks", "install-history.json");
    }
}
