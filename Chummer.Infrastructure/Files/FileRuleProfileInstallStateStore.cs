using System.Text.Json;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Infrastructure.Files;

public sealed class FileRuleProfileInstallStateStore : IRuleProfileInstallStateStore
{
    private readonly string _stateDirectory;

    public FileRuleProfileInstallStateStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public IReadOnlyList<RuleProfileInstallRecord> List(OwnerScope owner, string? rulesetId = null)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        return Load(owner)
            .Where(record => normalizedRulesetId is null
                || string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal))
            .ToArray();
    }

    public RuleProfileInstallRecord? Get(OwnerScope owner, string profileId, string rulesetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);

        return Load(owner).FirstOrDefault(
            record => string.Equals(record.ProfileId, profileId, StringComparison.Ordinal)
                && string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal));
    }

    public RuleProfileInstallRecord Upsert(OwnerScope owner, RuleProfileInstallRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ProfileId);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(record.RulesetId);
        RuleProfileInstallRecord normalizedRecord = record with { RulesetId = normalizedRulesetId };

        List<RuleProfileInstallRecord> records = Load(owner).ToList();
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

    private IReadOnlyList<RuleProfileInstallRecord> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        List<RuleProfileInstallRecord>? records = JsonSerializer.Deserialize<List<RuleProfileInstallRecord>>(File.ReadAllText(path));
        return records ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<RuleProfileInstallRecord> records)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(records));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "profiles", "install-state.json");
    }
}
