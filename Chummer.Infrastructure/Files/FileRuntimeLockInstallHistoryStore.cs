using System.Text.Json;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Infrastructure.Files;

public sealed class FileRuntimeLockInstallHistoryStore : IRuntimeLockInstallHistoryStore
{
    private readonly string _stateDirectory;

    public FileRuntimeLockInstallHistoryStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public IReadOnlyList<RuntimeLockInstallHistoryRecord> List(OwnerScope owner, string? rulesetId = null)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        return Load(owner)
            .Where(record => normalizedRulesetId is null
                || string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal))
            .OrderByDescending(record => record.Entry.AppliedAtUtc)
            .ToArray();
    }

    public IReadOnlyList<RuntimeLockInstallHistoryRecord> GetHistory(OwnerScope owner, string lockId, string rulesetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockId);
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);

        return List(owner, normalizedRulesetId)
            .Where(record => string.Equals(record.LockId, lockId, StringComparison.Ordinal))
            .ToArray();
    }

    public RuntimeLockInstallHistoryRecord Append(OwnerScope owner, RuntimeLockInstallHistoryRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.LockId);
        RuntimeLockInstallHistoryRecord normalizedRecord = record with
        {
            RulesetId = RulesetDefaults.NormalizeRequired(record.RulesetId)
        };

        List<RuntimeLockInstallHistoryRecord> records = Load(owner).ToList();
        records.Add(normalizedRecord);
        Save(owner, records);
        return normalizedRecord;
    }

    private IReadOnlyList<RuntimeLockInstallHistoryRecord> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        List<RuntimeLockInstallHistoryRecord>? records = JsonSerializer.Deserialize<List<RuntimeLockInstallHistoryRecord>>(File.ReadAllText(path));
        return records ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<RuntimeLockInstallHistoryRecord> records)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(records));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "runtime-locks", "install-history.json");
    }
}
