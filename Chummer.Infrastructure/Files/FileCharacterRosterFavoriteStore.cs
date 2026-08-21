using System.Text.Json;
using Chummer.Application.Tools;
using Chummer.Contracts.Api;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Files;

public sealed class FileCharacterRosterFavoriteStore : ICharacterRosterFavoriteStore
{
    private const string FileName = "roster-favorites.json";
    private readonly object _gate = new();
    private readonly string _stateDirectory;

    public FileCharacterRosterFavoriteStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public CharacterRosterFavoriteState Load()
        => Load(OwnerScope.LocalSingleUser);

    public CharacterRosterFavoriteState Load(OwnerScope owner)
    {
        lock (_gate)
            return LoadCore(owner);
    }

    public void Save(long expectedRevision, CharacterRosterFavoriteState state)
        => Save(OwnerScope.LocalSingleUser, expectedRevision, state);

    public void Save(OwnerScope owner, long expectedRevision, CharacterRosterFavoriteState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            CharacterRosterFavoriteState current = LoadCore(owner);
            if (current.Revision != expectedRevision)
            {
                throw new InvalidOperationException(
                    $"Roster favorites changed at revision {current.Revision}; expected {expectedRevision}.");
            }
            CharacterRosterFavoriteState normalized = CharacterRosterFavoriteRules.ValidateAndNormalize(state);
            if (normalized.Revision != expectedRevision + 1)
                throw new InvalidDataException("A roster favorite save must advance the revision exactly once.");

            string path = GetPath(owner);
            string backup = path + ".bak";
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try
            {
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(normalized);
                using (FileStream stream = new(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(payload);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temporary, path, backup, ignoreMetadataErrors: true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(path, backup, overwrite: true);
                        File.Move(temporary, path, overwrite: true);
                    }
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }

    private CharacterRosterFavoriteState LoadCore(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (TryLoad(path, out CharacterRosterFavoriteState? current))
            return current;
        if (TryLoad(path + ".bak", out CharacterRosterFavoriteState? recovered))
        {
            RecoverPrimary(path);
            return recovered;
        }
        if (File.Exists(path) || File.Exists(path + ".bak"))
            throw new InvalidDataException("Roster favorite state and its recovery copy are invalid.");
        return CharacterRosterFavoriteState.Empty;
    }

    private static void RecoverPrimary(string path)
    {
        string backup = path + ".bak";
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".recovery.tmp";
        try
        {
            File.Copy(backup, temporary, overwrite: false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static bool TryLoad(string path, out CharacterRosterFavoriteState? state)
    {
        state = null;
        if (!File.Exists(path))
            return false;
        try
        {
            CharacterRosterFavoriteState? candidate = JsonSerializer.Deserialize<CharacterRosterFavoriteState>(
                File.ReadAllBytes(path));
            if (candidate is null)
                return false;
            state = CharacterRosterFavoriteRules.ValidateAndNormalize(candidate);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            return false;
        }
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, FileName);
    }
}
