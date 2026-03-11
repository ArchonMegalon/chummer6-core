using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Tools;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Files;

public sealed class FileSettingsStore : ISettingsStore
{
    private readonly string _stateDirectory;

    public FileSettingsStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public JsonObject Load(string scope)
    {
        return Load(OwnerScope.LocalSingleUser, scope);
    }

    public JsonObject Load(OwnerScope owner, string scope)
    {
        string path = GetPath(owner, scope);
        if (!File.Exists(path))
            return new JsonObject();

        string text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();

        try
        {
            JsonNode? parsed = JsonNode.Parse(text);
            if (parsed is JsonObject json)
                return json;
        }
        catch
        {
            // fall through and return empty object when persisted settings are invalid.
        }

        return new JsonObject();
    }

    public void Save(string scope, JsonObject settings)
    {
        Save(OwnerScope.LocalSingleUser, scope, settings);
    }

    public void Save(OwnerScope owner, string scope, JsonObject settings)
    {
        string path = GetPath(owner, scope);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = settings.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false
        });
        File.WriteAllText(path, json);
    }

    private string GetPath(OwnerScope owner, string scope)
    {
        if (owner.IsLocalSingleUser || string.IsNullOrWhiteSpace(owner.NormalizedValue))
        {
            return Path.Combine(_stateDirectory, $"{scope}-settings.json");
        }

        return Path.Combine(
            OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner),
            "settings",
            $"{scope}-settings.json");
    }
}
