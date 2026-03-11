using System.Text.Json;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Infrastructure.Files;

public sealed class FileRuleProfileInstallHistoryStore : IRuleProfileInstallHistoryStore
{
    private readonly string _stateDirectory;

    public FileRuleProfileInstallHistoryStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public IReadOnlyList<RuleProfileInstallHistoryRecord> List(OwnerScope owner, string? rulesetId = null)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        return Load(owner)
            .Where(record => normalizedRulesetId is null
                || string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal))
            .OrderByDescending(record => record.Entry.AppliedAtUtc)
            .ToArray();
    }

    public IReadOnlyList<RuleProfileInstallHistoryRecord> GetHistory(OwnerScope owner, string profileId, string rulesetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);

        return List(owner, normalizedRulesetId)
            .Where(record => string.Equals(record.ProfileId, profileId, StringComparison.Ordinal))
            .ToArray();
    }

    public RuleProfileInstallHistoryRecord Append(OwnerScope owner, RuleProfileInstallHistoryRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ProfileId);
        RuleProfileInstallHistoryRecord normalizedRecord = record with
        {
            RulesetId = RulesetDefaults.NormalizeRequired(record.RulesetId)
        };

        List<RuleProfileInstallHistoryRecord> records = Load(owner).ToList();
        records.Add(normalizedRecord);
        Save(owner, records);
        return normalizedRecord;
    }

    private IReadOnlyList<RuleProfileInstallHistoryRecord> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        List<RuleProfileInstallHistoryRecord>? records = JsonSerializer.Deserialize<List<RuleProfileInstallHistoryRecord>>(File.ReadAllText(path));
        return records ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<RuleProfileInstallHistoryRecord> records)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(records));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "profiles", "install-history.json");
    }
}
