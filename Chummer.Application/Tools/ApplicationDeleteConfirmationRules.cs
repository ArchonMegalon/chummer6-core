using Chummer.Contracts.Api;

namespace Chummer.Application.Tools;

/// <summary>
/// Exact Chummer5 confirmation, date/time, visibility, and selection-behavior Global Settings
/// semantics without registry or character XML.
/// </summary>
public static class ApplicationDeleteConfirmationRules
{
    public const string LegacyIdentity = "confirmdelete";
    public const string LegacyKarmaExpenseIdentity = "confirmkarmaexpense";
    public const string LegacyCustomDateTimeFormatsIdentity = "usecustomdatetime";
    public const string LegacyCustomDateFormatIdentity = "customdateformat";
    public const string LegacyCustomTimeFormatIdentity = "customtimeformat";
    public const string LegacyDatesIncludeTimeIdentity = "datesincludetime";
    public const string LegacyHideMasterIndexIdentity = "hidemasterindex";
    public const string LegacyHideCharacterRosterIdentity = "hidecharacterroster";
    public const string LegacySearchInCategoryOnlyIdentity = "searchincategoryonly";
    public const string LegacyAllowEasterEggsIdentity = "alloweastereggs";

    public static ApplicationDeleteConfirmationState Apply(
        ApplicationDeleteConfirmationState current,
        ApplicationDeleteConfirmationMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(mutation);
        Validate(current);
        if (mutation.ExpectedRevision != current.Revision)
        {
            throw new InvalidOperationException(
                $"Application settings changed at revision {current.Revision}; expected {mutation.ExpectedRevision}.");
        }
        if (!Enum.IsDefined(mutation.Identity))
            throw new ArgumentOutOfRangeException(nameof(mutation), "A known application setting identity is required.");

        bool unchanged = mutation.Identity switch
        {
            ApplicationSettingIdentity.ConfirmDelete => mutation.Value == current.ConfirmDelete,
            ApplicationSettingIdentity.ConfirmKarmaExpense => mutation.Value == current.ConfirmKarmaExpense,
            ApplicationSettingIdentity.CustomDateTimeFormats => mutation.Value == current.CustomDateTimeFormats,
            ApplicationSettingIdentity.DatesIncludeTime => mutation.Value == current.DatesIncludeTime,
            ApplicationSettingIdentity.HideMasterIndex => mutation.Value == current.HideMasterIndex,
            ApplicationSettingIdentity.HideCharacterRoster => mutation.Value == current.HideCharacterRoster,
            ApplicationSettingIdentity.SearchInCategoryOnly => mutation.Value == current.SearchInCategoryOnly,
            ApplicationSettingIdentity.AllowEasterEggs => mutation.Value == current.AllowEasterEggs,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), "A known application setting identity is required.")
        };
        if (unchanged)
            return current;

        return mutation.Identity switch
        {
            ApplicationSettingIdentity.ConfirmDelete => current with
            {
                Revision = current.Revision + 1,
                ConfirmDelete = mutation.Value
            },
            ApplicationSettingIdentity.ConfirmKarmaExpense => current with
            {
                Revision = current.Revision + 1,
                ConfirmKarmaExpense = mutation.Value
            },
            ApplicationSettingIdentity.CustomDateTimeFormats => current with
            {
                Revision = current.Revision + 1,
                CustomDateTimeFormats = mutation.Value
            },
            ApplicationSettingIdentity.DatesIncludeTime => current with
            {
                Revision = current.Revision + 1,
                DatesIncludeTime = mutation.Value
            },
            ApplicationSettingIdentity.HideMasterIndex => current with
            {
                Revision = current.Revision + 1,
                HideMasterIndex = mutation.Value
            },
            ApplicationSettingIdentity.HideCharacterRoster => current with
            {
                Revision = current.Revision + 1,
                HideCharacterRoster = mutation.Value
            },
            ApplicationSettingIdentity.SearchInCategoryOnly => current with
            {
                Revision = current.Revision + 1,
                SearchInCategoryOnly = mutation.Value
            },
            ApplicationSettingIdentity.AllowEasterEggs => current with
            {
                Revision = current.Revision + 1,
                AllowEasterEggs = mutation.Value
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), "A known application setting identity is required.")
        };
    }

    public static ApplicationDeleteConfirmationState ApplySnapshot(
        ApplicationDeleteConfirmationState current,
        ApplicationConfirmationSettingsMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(mutation);
        Validate(current);
        if (mutation.ExpectedRevision != current.Revision)
        {
            throw new InvalidOperationException(
                $"Application settings changed at revision {current.Revision}; expected {mutation.ExpectedRevision}.");
        }
        if (mutation.ConfirmDelete == current.ConfirmDelete
            && mutation.ConfirmKarmaExpense == current.ConfirmKarmaExpense)
        {
            return current;
        }

        return new ApplicationDeleteConfirmationState(
            current.Revision + 1,
            mutation.ConfirmDelete,
            mutation.ConfirmKarmaExpense,
            current.CustomDateTimeFormats,
            current.CustomDateFormat,
            current.CustomTimeFormat,
            current.DatesIncludeTime,
            current.HideMasterIndex,
            current.HideCharacterRoster,
            current.SearchInCategoryOnly,
            current.AllowEasterEggs);
    }

    /// <summary>
    /// Applies the four date/time controls with Chummer5's SaveGlobalOptions semantics. When
    /// custom formats are disabled, the two displayed culture defaults are not written over the
    /// previously stored custom strings. DatesIncludeTime remains independent of that phase.
    /// </summary>
    public static ApplicationDeleteConfirmationState ApplyDateTimeSnapshot(
        ApplicationDeleteConfirmationState current,
        ApplicationDateTimeSettingsMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(mutation);
        Validate(current);
        if (mutation.ExpectedRevision != current.Revision)
        {
            throw new InvalidOperationException(
                $"Application settings changed at revision {current.Revision}; expected {mutation.ExpectedRevision}.");
        }

        RequireIdentity(mutation.CustomDateTimeFormats, ApplicationSettingIdentity.CustomDateTimeFormats);
        RequireIdentity(mutation.CustomDateFormat, ApplicationSettingIdentity.CustomDateFormat);
        RequireIdentity(mutation.CustomTimeFormat, ApplicationSettingIdentity.CustomTimeFormat);
        RequireIdentity(mutation.DatesIncludeTime, ApplicationSettingIdentity.DatesIncludeTime);
        ArgumentNullException.ThrowIfNull(mutation.CustomDateFormat.Value);
        ArgumentNullException.ThrowIfNull(mutation.CustomTimeFormat.Value);

        bool useCustom = mutation.CustomDateTimeFormats.Value;
        string dateFormat = useCustom ? mutation.CustomDateFormat.Value : current.CustomDateFormat;
        string timeFormat = useCustom ? mutation.CustomTimeFormat.Value : current.CustomTimeFormat;
        if (useCustom == current.CustomDateTimeFormats
            && dateFormat == current.CustomDateFormat
            && timeFormat == current.CustomTimeFormat
            && mutation.DatesIncludeTime.Value == current.DatesIncludeTime)
        {
            return current;
        }

        return current with
        {
            Revision = current.Revision + 1,
            CustomDateTimeFormats = useCustom,
            CustomDateFormat = dateFormat,
            CustomTimeFormat = timeFormat,
            DatesIncludeTime = mutation.DatesIncludeTime.Value
        };
    }

    public static ApplicationDeleteConfirmationState ApplySettingsSnapshot(
        ApplicationDeleteConfirmationState current,
        ApplicationSettingsSnapshotMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(mutation);
        Validate(current);
        if (mutation.ExpectedRevision != current.Revision)
        {
            throw new InvalidOperationException(
                $"Application settings changed at revision {current.Revision}; expected {mutation.ExpectedRevision}.");
        }

        RequireIdentity(mutation.HideMasterIndex, ApplicationSettingIdentity.HideMasterIndex);
        RequireIdentity(mutation.HideCharacterRoster, ApplicationSettingIdentity.HideCharacterRoster);
        RequireIdentity(mutation.SearchInCategoryOnly, ApplicationSettingIdentity.SearchInCategoryOnly);
        RequireIdentity(mutation.AllowEasterEggs, ApplicationSettingIdentity.AllowEasterEggs);

        ApplicationDateTimeSettingsMutation dateTime = new(
            mutation.CustomDateTimeFormats,
            mutation.CustomDateFormat,
            mutation.CustomTimeFormat,
            mutation.DatesIncludeTime,
            mutation.ExpectedRevision);
        ApplicationDeleteConfirmationState dateTimeUpdated = ApplyDateTimeSnapshot(current, dateTime);
        bool changed = dateTimeUpdated.Revision != current.Revision
            || mutation.ConfirmDelete != current.ConfirmDelete
            || mutation.ConfirmKarmaExpense != current.ConfirmKarmaExpense
            || mutation.HideMasterIndex.Value != current.HideMasterIndex
            || mutation.HideCharacterRoster.Value != current.HideCharacterRoster
            || mutation.SearchInCategoryOnly.Value != current.SearchInCategoryOnly
            || mutation.AllowEasterEggs.Value != current.AllowEasterEggs;
        if (!changed)
            return current;

        return dateTimeUpdated with
        {
            Revision = current.Revision + 1,
            ConfirmDelete = mutation.ConfirmDelete,
            ConfirmKarmaExpense = mutation.ConfirmKarmaExpense,
            HideMasterIndex = mutation.HideMasterIndex.Value,
            HideCharacterRoster = mutation.HideCharacterRoster.Value,
            SearchInCategoryOnly = mutation.SearchInCategoryOnly.Value,
            AllowEasterEggs = mutation.AllowEasterEggs.Value
        };
    }

    /// <summary>
    /// Mirrors EditGlobalSettings' TextChanged preview: formatting errors are surfaced as the
    /// literal Error preview, but the raw format remains an editable draft and is not normalized.
    /// </summary>
    public static ApplicationDateTimeFormatPreview PreviewDateTimeFormat(
        ApplicationSettingIdentity identity,
        bool customDateTimeFormats,
        string customFormat,
        string cultureDefaultFormat,
        DateTime sample,
        IFormatProvider formatProvider)
    {
        if (identity is not (ApplicationSettingIdentity.CustomDateFormat or ApplicationSettingIdentity.CustomTimeFormat))
        {
            throw new ArgumentOutOfRangeException(nameof(identity), "A date or time format identity is required.");
        }
        ArgumentNullException.ThrowIfNull(customFormat);
        ArgumentNullException.ThrowIfNull(cultureDefaultFormat);
        ArgumentNullException.ThrowIfNull(formatProvider);

        ApplicationDateTimeFormatPhase phase = customDateTimeFormats
            ? ApplicationDateTimeFormatPhase.Custom
            : ApplicationDateTimeFormatPhase.CultureDefault;
        string format = customDateTimeFormats ? customFormat : cultureDefaultFormat;
        try
        {
            return new ApplicationDateTimeFormatPreview(
                identity,
                phase,
                format,
                sample.ToString(format, formatProvider),
                IsValid: true);
        }
        catch
        {
            return new ApplicationDateTimeFormatPreview(identity, phase, format, "Error", IsValid: false);
        }
    }

    public static ApplicationDeleteConfirmationState Validate(ApplicationDeleteConfirmationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Revision < 0)
            throw new InvalidDataException("Application settings revision cannot be negative.");
        if (state.CustomDateFormat is null || state.CustomTimeFormat is null)
            throw new InvalidDataException("Application date/time formats cannot be null.");
        return state;
    }

    public static bool RequiresConfirmation(ApplicationDeleteConfirmationState state)
        => Validate(state).ConfirmDelete;

    public static bool RequiresKarmaExpenseConfirmation(ApplicationDeleteConfirmationState state)
        => Validate(state).ConfirmKarmaExpense;

    private static void RequireIdentity<T>(
        ApplicationSettingValue<T> setting,
        ApplicationSettingIdentity expected)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (setting.Identity != expected)
        {
            throw new ArgumentException(
                $"Expected application setting identity {expected}, got {setting.Identity}.",
                nameof(setting));
        }
    }
}
