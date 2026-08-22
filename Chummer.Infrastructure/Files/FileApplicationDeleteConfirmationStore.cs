using System.Text.Json;
using Chummer.Application.Tools;
using Chummer.Contracts.Api;

namespace Chummer.Infrastructure.Files;

public sealed class FileApplicationDeleteConfirmationStore : IApplicationDeleteConfirmationStore
{
    private const string FileName = "application-delete-confirmation.json";
    private readonly object _gate = new();
    private readonly string _stateDirectory;

    public FileApplicationDeleteConfirmationStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public ApplicationDeleteConfirmationState Load()
    {
        lock (_gate)
            return LoadCore();
    }

    public void Save(long expectedRevision, ApplicationDeleteConfirmationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            ApplicationDeleteConfirmationState current = LoadCore();
            if (current.Revision != expectedRevision)
            {
                throw new InvalidOperationException(
                    $"Application settings changed at revision {current.Revision}; expected {expectedRevision}.");
            }

            ApplicationDeleteConfirmationState normalized = ApplicationDeleteConfirmationRules.Validate(state);
            if (normalized.Revision != expectedRevision + 1)
                throw new InvalidDataException("An application settings save must advance the revision exactly once.");

            AtomicWrite(GetPath(), normalized, createBackup: true);
        }
    }

    private ApplicationDeleteConfirmationState LoadCore()
    {
        string path = GetPath();
        string backupPath = path + ".bak";
        bool primaryValid = TryLoad(path, out ApplicationDeleteConfirmationState? primary);
        bool backupValid = TryLoad(backupPath, out ApplicationDeleteConfirmationState? backup);

        if (!primaryValid && !backupValid)
        {
            if (File.Exists(path) || File.Exists(backupPath))
                throw new InvalidDataException("Application settings state and its recovery copy are invalid.");
            return ApplicationDeleteConfirmationState.Default;
        }

        ApplicationDeleteConfirmationState selected;
        bool recoverPrimary;
        if (primaryValid && backupValid)
        {
            if (primary!.Revision == backup!.Revision && primary != backup)
                throw new InvalidDataException("Application settings copies disagree at the same revision.");
            selected = primary.Revision >= backup.Revision ? primary : backup;
            recoverPrimary = selected == backup && primary != backup;
        }
        else
        {
            selected = primaryValid ? primary! : backup!;
            recoverPrimary = !primaryValid;
        }

        if (recoverPrimary)
            AtomicWrite(path, selected, createBackup: false);
        return selected;
    }

    private static bool TryLoad(string path, out ApplicationDeleteConfirmationState? state)
    {
        state = null;
        if (!File.Exists(path))
            return false;
        try
        {
            ApplicationDeleteConfirmationState? candidate =
                JsonSerializer.Deserialize<ApplicationDeleteConfirmationState>(File.ReadAllBytes(path));
            if (candidate is null)
                return false;
            state = ApplicationDeleteConfirmationRules.Validate(candidate);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            return false;
        }
    }

    private static void AtomicWrite(
        string path,
        ApplicationDeleteConfirmationState state,
        bool createBackup)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(state);
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

            if (File.Exists(path) && createBackup)
            {
                try
                {
                    File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(path, path + ".bak", overwrite: true);
                    File.Move(temporary, path, overwrite: true);
                }
            }
            else
            {
                File.Move(temporary, path, overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private string GetPath()
        => Path.Combine(_stateDirectory, FileName);
}
