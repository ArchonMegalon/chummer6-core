using System.Text.Json;
using Chummer.Application.Tools;
using Chummer.Contracts.Api;

namespace Chummer.Infrastructure.Files;

public sealed class FileApplicationDeleteConfirmationStore : IApplicationDeleteConfirmationStore
{
    private const string FileName = "application-delete-confirmation.json";
    private readonly object _gate = new();
    private readonly string _stateDirectory;
    private readonly ApplicationDeleteConfirmationState _defaultState;

    public FileApplicationDeleteConfirmationStore(
        string? stateDirectory = null,
        Version? applicationVersion = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        _defaultState = ApplicationDeleteConfirmationState.ForApplicationVersion(
            applicationVersion ?? typeof(FileApplicationDeleteConfirmationStore).Assembly.GetName().Version
            ?? new Version(0, 0));
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
            return _defaultState;
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

    private bool TryLoad(string path, out ApplicationDeleteConfirmationState? state)
    {
        state = null;
        if (!File.Exists(path))
            return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Revision", out JsonElement revisionElement)
                || !revisionElement.TryGetInt64(out long revision)
                || !root.TryGetProperty("ConfirmDelete", out JsonElement confirmDeleteElement)
                || confirmDeleteElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            bool confirmKarmaExpense = true;
            if (root.TryGetProperty("ConfirmKarmaExpense", out JsonElement confirmKarmaExpenseElement))
            {
                if (confirmKarmaExpenseElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return false;
                confirmKarmaExpense = confirmKarmaExpenseElement.GetBoolean();
            }

            bool customDateTimeFormats = false;
            if (root.TryGetProperty("CustomDateTimeFormats", out JsonElement customDateTimeFormatsElement))
            {
                if (customDateTimeFormatsElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return false;
                customDateTimeFormats = customDateTimeFormatsElement.GetBoolean();
            }

            string customDateFormat = string.Empty;
            if (root.TryGetProperty("CustomDateFormat", out JsonElement customDateFormatElement))
            {
                if (customDateFormatElement.ValueKind != JsonValueKind.String)
                    return false;
                customDateFormat = customDateFormatElement.GetString()!;
            }

            string customTimeFormat = string.Empty;
            if (root.TryGetProperty("CustomTimeFormat", out JsonElement customTimeFormatElement))
            {
                if (customTimeFormatElement.ValueKind != JsonValueKind.String)
                    return false;
                customTimeFormat = customTimeFormatElement.GetString()!;
            }

            bool datesIncludeTime = true;
            if (root.TryGetProperty("DatesIncludeTime", out JsonElement datesIncludeTimeElement))
            {
                if (datesIncludeTimeElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return false;
                datesIncludeTime = datesIncludeTimeElement.GetBoolean();
            }

            bool hideMasterIndex = false;
            if (root.TryGetProperty("HideMasterIndex", out JsonElement hideMasterIndexElement))
            {
                if (hideMasterIndexElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return false;
                hideMasterIndex = hideMasterIndexElement.GetBoolean();
            }

            bool hideCharacterRoster = false;
            if (root.TryGetProperty("HideCharacterRoster", out JsonElement hideCharacterRosterElement))
            {
                if (hideCharacterRosterElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return false;
                hideCharacterRoster = hideCharacterRosterElement.GetBoolean();
            }

            bool searchInCategoryOnly = true;
            if (root.TryGetProperty("SearchInCategoryOnly", out JsonElement searchInCategoryOnlyElement))
            {
                if (searchInCategoryOnlyElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return false;
                searchInCategoryOnly = searchInCategoryOnlyElement.GetBoolean();
            }

            bool allowEasterEggs = false;
            if (root.TryGetProperty("AllowEasterEggs", out JsonElement allowEasterEggsElement))
            {
                if (allowEasterEggsElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return false;
                allowEasterEggs = allowEasterEggsElement.GetBoolean();
            }

            bool preferNightlyBuilds = _defaultState.PreferNightlyBuilds;
            if (root.TryGetProperty("PreferNightlyBuilds", out JsonElement preferNightlyBuildsElement))
            {
                if (preferNightlyBuildsElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return false;
                preferNightlyBuilds = preferNightlyBuildsElement.GetBoolean();
            }

            bool liveUpdateCleanCharacterFiles = false;
            if (root.TryGetProperty("LiveUpdateCleanCharacterFiles", out JsonElement liveUpdateCleanCharacterFilesElement))
            {
                if (liveUpdateCleanCharacterFilesElement.ValueKind
                    is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return false;
                }
                liveUpdateCleanCharacterFiles = liveUpdateCleanCharacterFilesElement.GetBoolean();
            }

            if (!TryReadOptionalBoolean(
                    root,
                    nameof(ApplicationDeleteConfirmationState.PrintToFileFirst),
                    _defaultState.PrintToFileFirst,
                    out bool printToFileFirst)
                || !TryReadOptionalBoolean(
                    root,
                    nameof(ApplicationDeleteConfirmationState.PrintSkillsWithZeroRating),
                    _defaultState.PrintSkillsWithZeroRating,
                    out bool printSkillsWithZeroRating)
                || !TryReadOptionalBoolean(
                    root,
                    nameof(ApplicationDeleteConfirmationState.PrintExpenses),
                    _defaultState.PrintExpenses,
                    out bool printExpenses)
                || !TryReadOptionalBoolean(
                    root,
                    nameof(ApplicationDeleteConfirmationState.PrintFreeExpenses),
                    _defaultState.PrintFreeExpenses,
                    out bool printFreeExpenses)
                || !TryReadOptionalBoolean(
                    root,
                    nameof(ApplicationDeleteConfirmationState.PrintNotes),
                    _defaultState.PrintNotes,
                    out bool printNotes)
                || !TryReadOptionalBoolean(
                    root,
                    nameof(ApplicationDeleteConfirmationState.InsertPdfNotesIfAvailable),
                    _defaultState.InsertPdfNotesIfAvailable,
                    out bool insertPdfNotesIfAvailable))
            {
                return false;
            }

            state = ApplicationDeleteConfirmationRules.Validate(new ApplicationDeleteConfirmationState(
                revision,
                confirmDeleteElement.GetBoolean(),
                confirmKarmaExpense,
                customDateTimeFormats,
                customDateFormat,
                customTimeFormat,
                datesIncludeTime,
                hideMasterIndex,
                hideCharacterRoster,
                searchInCategoryOnly,
                allowEasterEggs,
                preferNightlyBuilds,
                liveUpdateCleanCharacterFiles,
                printToFileFirst,
                printSkillsWithZeroRating,
                printExpenses,
                printFreeExpenses,
                printNotes,
                insertPdfNotesIfAvailable));
            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            return false;
        }
    }

    private static bool TryReadOptionalBoolean(
        JsonElement root,
        string propertyName,
        bool defaultValue,
        out bool value)
    {
        value = defaultValue;
        if (!root.TryGetProperty(propertyName, out JsonElement element))
            return true;
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;
        value = element.GetBoolean();
        return true;
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
