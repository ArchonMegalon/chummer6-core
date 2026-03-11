using System.Text.Json;
using Chummer.Application.Session;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using Chummer.Contracts.Rulesets;

namespace Chummer.Infrastructure.Files;

public sealed class FileSessionProfileSelectionStore : ISessionProfileSelectionStore
{
    private readonly string _stateDirectory;

    public FileSessionProfileSelectionStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public IReadOnlyList<SessionProfileBinding> List(OwnerScope owner)
        => Load(owner);

    public SessionProfileBinding? Get(OwnerScope owner, string characterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);
        return Load(owner).FirstOrDefault(binding =>
            string.Equals(binding.CharacterId, characterId.Trim(), StringComparison.Ordinal));
    }

    public SessionProfileBinding Upsert(OwnerScope owner, SessionProfileBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        SessionProfileBinding normalizedBinding = binding with
        {
            CharacterId = binding.CharacterId.Trim(),
            ProfileId = binding.ProfileId.Trim(),
            RulesetId = RulesetDefaults.NormalizeRequired(binding.RulesetId),
            RuntimeFingerprint = binding.RuntimeFingerprint.Trim()
        };

        List<SessionProfileBinding> bindings = Load(owner).ToList();
        int existingIndex = bindings.FindIndex(current =>
            string.Equals(current.CharacterId, normalizedBinding.CharacterId, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            bindings[existingIndex] = normalizedBinding;
        }
        else
        {
            bindings.Add(normalizedBinding);
        }

        Save(owner, bindings);
        return normalizedBinding;
    }

    private IReadOnlyList<SessionProfileBinding> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        List<SessionProfileBinding>? records = JsonSerializer.Deserialize<List<SessionProfileBinding>>(File.ReadAllText(path));
        return records ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<SessionProfileBinding> bindings)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(bindings));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "session", "profile-selections.json");
    }
}
