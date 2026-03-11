using System.Text.Json;
using Chummer.Application.Tools;
using Chummer.Contracts.Api;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Files;

public sealed class FileRosterStore : IRosterStore
{
    private readonly string _stateDirectory;

    public FileRosterStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public IReadOnlyList<RosterEntry> Load()
    {
        return Load(OwnerScope.LocalSingleUser);
    }

    public IReadOnlyList<RosterEntry> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
            return Array.Empty<RosterEntry>();

        List<RosterEntry>? entries = JsonSerializer.Deserialize<List<RosterEntry>>(File.ReadAllText(path));
        return entries ?? [];
    }

    public IReadOnlyList<RosterEntry> Upsert(RosterEntry entry)
    {
        return Upsert(OwnerScope.LocalSingleUser, entry);
    }

    public IReadOnlyList<RosterEntry> Upsert(OwnerScope owner, RosterEntry entry)
    {
        IReadOnlyList<RosterEntry> existing = Load(owner);

        List<RosterEntry> merged = [entry];
        foreach (RosterEntry current in existing)
        {
            if (string.Equals(current.Name, entry.Name, StringComparison.Ordinal)
                && string.Equals(current.Alias, entry.Alias, StringComparison.Ordinal))
            {
                continue;
            }

            merged.Add(current);
        }

        if (merged.Count > 50)
            merged = merged.Take(50).ToList();

        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(merged));
        return merged;
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "roster.json");
    }
}
