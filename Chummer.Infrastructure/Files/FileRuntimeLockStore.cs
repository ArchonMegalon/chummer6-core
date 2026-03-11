using System.Text.Json;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Infrastructure.Files;

public sealed class FileRuntimeLockStore : IRuntimeLockStore
{
    private readonly string _stateDirectory;

    public FileRuntimeLockStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public RuntimeLockRegistryPage List(OwnerScope owner, string? rulesetId = null)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        RuntimeLockRegistryEntry[] entries = Load(owner)
            .Where(entry => normalizedRulesetId is null
                || string.Equals(entry.RuntimeLock.RulesetId, normalizedRulesetId, StringComparison.Ordinal))
            .OrderBy(entry => entry.Title, StringComparer.Ordinal)
            .ToArray();

        return new RuntimeLockRegistryPage(entries, entries.Length);
    }

    public RuntimeLockRegistryEntry? Get(OwnerScope owner, string lockId, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockId);
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);

        return Load(owner).FirstOrDefault(
            entry => string.Equals(entry.LockId, lockId, StringComparison.Ordinal)
                && (normalizedRulesetId is null
                    || string.Equals(entry.RuntimeLock.RulesetId, normalizedRulesetId, StringComparison.Ordinal)));
    }

    public RuntimeLockRegistryEntry Upsert(OwnerScope owner, RuntimeLockRegistryEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.LockId);
        RuntimeLockRegistryEntry normalizedEntry = entry with
        {
            RuntimeLock = entry.RuntimeLock with
            {
                RulesetId = RulesetDefaults.NormalizeRequired(entry.RuntimeLock.RulesetId)
            },
            Install = NormalizeInstall(entry.Install, entry.RuntimeLock.RuntimeFingerprint)
        };

        List<RuntimeLockRegistryEntry> entries = Load(owner).ToList();
        int existingIndex = entries.FindIndex(current => string.Equals(current.LockId, normalizedEntry.LockId, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            entries[existingIndex] = normalizedEntry;
        }
        else
        {
            entries.Add(normalizedEntry);
        }

        Save(owner, entries);
        return normalizedEntry;
    }

    private IReadOnlyList<RuntimeLockRegistryEntry> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        List<RuntimeLockRegistryEntry>? entries = JsonSerializer.Deserialize<List<RuntimeLockRegistryEntry>>(File.ReadAllText(path));
        return entries?.Select(entry => entry with
        {
            Install = NormalizeInstall(entry.Install, entry.RuntimeLock.RuntimeFingerprint)
        }).ToArray() ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<RuntimeLockRegistryEntry> entries)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(entries));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "runtime-locks", "registry.json");
    }

    private static ArtifactInstallState NormalizeInstall(ArtifactInstallState? install, string runtimeFingerprint)
    {
        ArtifactInstallState normalized = install ?? new ArtifactInstallState(ArtifactInstallStates.Available);
        return string.IsNullOrWhiteSpace(normalized.RuntimeFingerprint)
            ? normalized with { RuntimeFingerprint = runtimeFingerprint }
            : normalized;
    }
}
