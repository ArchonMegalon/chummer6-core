using System.Text.Json;
using Chummer.Application.Session;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using Chummer.Contracts.Rulesets;

namespace Chummer.Infrastructure.Files;

public sealed class FileSessionRuntimeBundleStore : ISessionRuntimeBundleStore
{
    private readonly string _stateDirectory;

    public FileSessionRuntimeBundleStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public SessionRuntimeBundleRecord? Get(OwnerScope owner, string characterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);
        return Load(owner).FirstOrDefault(record =>
            string.Equals(record.CharacterId, characterId.Trim(), StringComparison.Ordinal));
    }

    public SessionRuntimeBundleRecord Upsert(OwnerScope owner, SessionRuntimeBundleRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        SessionRuntimeBundleRecord normalizedRecord = record with
        {
            CharacterId = record.CharacterId.Trim(),
            ProfileId = record.ProfileId.Trim(),
            RulesetId = RulesetDefaults.NormalizeRequired(record.RulesetId)
        };

        List<SessionRuntimeBundleRecord> records = Load(owner).ToList();
        int existingIndex = records.FindIndex(current =>
            string.Equals(current.CharacterId, normalizedRecord.CharacterId, StringComparison.Ordinal));
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

    private IReadOnlyList<SessionRuntimeBundleRecord> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        List<SessionRuntimeBundleRecord>? records = JsonSerializer.Deserialize<List<SessionRuntimeBundleRecord>>(File.ReadAllText(path));
        return records ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<SessionRuntimeBundleRecord> records)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(records));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "session", "runtime-bundles.json");
    }
}
