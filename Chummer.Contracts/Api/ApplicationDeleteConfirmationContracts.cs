namespace Chummer.Contracts.Api;

/// <summary>
/// Stable typed identity for Chummer5 GlobalSettings values. The rules boundary maps each enum
/// member to its exact legacy registry key without exposing registry persistence to phone clients.
/// </summary>
public enum ApplicationSettingIdentity
{
    ConfirmDelete,
    ConfirmKarmaExpense,
    CustomDateTimeFormats,
    CustomDateFormat,
    CustomTimeFormat,
    DatesIncludeTime,
    HideMasterIndex,
    HideCharacterRoster,
    SearchInCategoryOnly,
    AllowEasterEggs,
    PreferNightlyBuilds,
    LiveUpdateCleanCharacterFiles
}

public sealed record ApplicationDeleteConfirmationState(
    long Revision,
    bool ConfirmDelete,
    bool ConfirmKarmaExpense = true,
    bool CustomDateTimeFormats = false,
    string CustomDateFormat = "",
    string CustomTimeFormat = "",
    bool DatesIncludeTime = true,
    bool HideMasterIndex = false,
    bool HideCharacterRoster = false,
    bool SearchInCategoryOnly = true,
    bool AllowEasterEggs = false,
    bool PreferNightlyBuilds = false,
    bool LiveUpdateCleanCharacterFiles = false)
{
    public static ApplicationDeleteConfirmationState Default { get; } = ForApplicationVersion(
        typeof(ApplicationDeleteConfirmationState).Assembly.GetName().Version ?? new Version(0, 0));

    /// <summary>
    /// Reproduces Chummer5's <c>!Utils.IsMilestoneVersion</c> default, where milestone means
    /// the running application's assembly version has a zero Build component.
    /// </summary>
    public static ApplicationDeleteConfirmationState ForApplicationVersion(Version applicationVersion)
    {
        ArgumentNullException.ThrowIfNull(applicationVersion);
        return new(
            Revision: 0,
            ConfirmDelete: true,
            ConfirmKarmaExpense: true,
            CustomDateTimeFormats: false,
            CustomDateFormat: "",
            CustomTimeFormat: "",
            DatesIncludeTime: true,
            HideMasterIndex: false,
            HideCharacterRoster: false,
            SearchInCategoryOnly: true,
            AllowEasterEggs: false,
            PreferNightlyBuilds: PreferNightlyBuildsByDefault(applicationVersion),
            LiveUpdateCleanCharacterFiles: false);
    }

    public static bool PreferNightlyBuildsByDefault(Version applicationVersion)
    {
        ArgumentNullException.ThrowIfNull(applicationVersion);
        return applicationVersion.Build != 0;
    }
}

public sealed record ApplicationDeleteConfirmationMutation(
    ApplicationSettingIdentity Identity,
    bool Value,
    long ExpectedRevision);

public sealed record ApplicationConfirmationSettingsMutation(
    bool ConfirmDelete,
    bool ConfirmKarmaExpense,
    long ExpectedRevision);

/// <summary>
/// Binds an application-setting value to its stable Chummer5 identity. Snapshot rules reject a
/// value supplied under the wrong identity instead of relying on positional UI arguments.
/// </summary>
public sealed record ApplicationSettingValue<T>(
    ApplicationSettingIdentity Identity,
    T Value);

/// <summary>
/// One explicit-Save transaction for the four Chummer5 date/time Global Settings controls.
/// </summary>
public sealed record ApplicationDateTimeSettingsMutation(
    ApplicationSettingValue<bool> CustomDateTimeFormats,
    ApplicationSettingValue<string> CustomDateFormat,
    ApplicationSettingValue<string> CustomTimeFormat,
    ApplicationSettingValue<bool> DatesIncludeTime,
    long ExpectedRevision);

/// <summary>
/// Atomic whole-page snapshot used when confirmations, date/time, visibility, selection behavior,
/// and update settings share one explicit Save.
/// </summary>
public sealed record ApplicationSettingsSnapshotMutation(
    bool ConfirmDelete,
    bool ConfirmKarmaExpense,
    ApplicationSettingValue<bool> CustomDateTimeFormats,
    ApplicationSettingValue<string> CustomDateFormat,
    ApplicationSettingValue<string> CustomTimeFormat,
    ApplicationSettingValue<bool> DatesIncludeTime,
    ApplicationSettingValue<bool> HideMasterIndex,
    ApplicationSettingValue<bool> HideCharacterRoster,
    ApplicationSettingValue<bool> SearchInCategoryOnly,
    ApplicationSettingValue<bool> AllowEasterEggs,
    ApplicationSettingValue<bool> PreferNightlyBuilds,
    ApplicationSettingValue<bool> LiveUpdateCleanCharacterFiles,
    long ExpectedRevision);

public enum ApplicationDateTimeFormatPhase
{
    CultureDefault,
    Custom
}

public sealed record ApplicationDateTimeFormatPreview(
    ApplicationSettingIdentity Identity,
    ApplicationDateTimeFormatPhase Phase,
    string Format,
    string Sample,
    bool IsValid);
