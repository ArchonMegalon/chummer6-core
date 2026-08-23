using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public sealed record CharacterCareerCalendarWeekIdentity(Guid WeekId);

public sealed record CharacterCareerCalendarWeekState(
    CharacterCareerCalendarWeekIdentity Identity,
    int Year,
    int Week,
    string Notes,
    string NotesColor,
    string LogicalRevision,
    string SourceRevision);

public sealed record CharacterCareerCalendarWeekDraft(
    CharacterCareerCalendarWeekIdentity Identity,
    int Year,
    int Week,
    string Notes,
    string NotesColor);

/// <summary>
/// Exact phone-safe authority for CharacterCareer's Calendar week collection.
/// Chummer5 stores stable week GUIDs, ISO-8601 year/week coordinates, notes and
/// ColorTranslator-compatible notes colors. Adding after an existing calendar is
/// deterministic: the new entry is the week immediately following the latest one.
/// </summary>
public static class CharacterCareerCalendarRules
{
    public const int RevisionHexLength = 64;
    public const int FirstWeekMinimumYear = 2000;
    public const int FirstWeekMaximumYear = 9000;
    public const int SupportedMinimumYear = 1;
    public const int SupportedMaximumYear = 9999;
    public const string DefaultNotesColor = "Chocolate";

    public static bool IsValidIdentity(CharacterCareerCalendarWeekIdentity? identity)
        => identity is { WeekId: var weekId } && weekId != Guid.Empty;

    public static bool TryCreateState(
        CharacterCareerCalendarWeekIdentity? identity,
        bool created,
        int year,
        int week,
        string? notes,
        string? notesColor,
        string? rawSourceState,
        out CharacterCareerCalendarWeekState state)
    {
        state = Unavailable();
        string normalizedNotes = notes ?? string.Empty;
        string normalizedColor = string.IsNullOrWhiteSpace(notesColor)
            ? DefaultNotesColor
            : notesColor.Trim();
        if (!created
            || !IsValidIdentity(identity)
            || !IsSupportedIsoWeek(year, week)
            || !IsColorTranslatorHtml(normalizedColor)
            || rawSourceState is null)
        {
            return false;
        }

        state = new CharacterCareerCalendarWeekState(
            identity!,
            year,
            week,
            normalizedNotes,
            normalizedColor,
            CalculateLogicalRevision(identity!, year, week, normalizedNotes, normalizedColor),
            Sha256(rawSourceState));
        return true;
    }

    public static bool TryPlanAdd(
        IReadOnlyList<CharacterCareerCalendarWeekState> current,
        CharacterCareerCalendarWeekIdentity? newIdentity,
        int requestedFirstYear,
        int requestedFirstWeek,
        out CharacterCareerCalendarWeekDraft draft)
    {
        ArgumentNullException.ThrowIfNull(current);
        draft = UnavailableDraft();
        if (!IsValidIdentity(newIdentity)
            || current.Any(candidate => !IsCoherent(candidate))
            || current.Select(candidate => candidate.Identity.WeekId).Distinct().Count() != current.Count
            || current.Any(candidate => candidate.Identity == newIdentity))
        {
            return false;
        }

        int year;
        int week;
        if (current.Count == 0)
        {
            if (requestedFirstYear is < FirstWeekMinimumYear or > FirstWeekMaximumYear
                || !IsSupportedIsoWeek(requestedFirstYear, requestedFirstWeek))
            {
                return false;
            }
            year = requestedFirstYear;
            week = requestedFirstWeek;
        }
        else
        {
            CharacterCareerCalendarWeekState latest = current
                .OrderByDescending(static candidate => candidate.Year)
                .ThenByDescending(static candidate => candidate.Week)
                .First();
            if (!TryNextWeek(latest.Year, latest.Week, out year, out week))
            {
                return false;
            }
        }

        if (current.Any(candidate => candidate.Year == year && candidate.Week == week))
        {
            return false;
        }

        draft = new CharacterCareerCalendarWeekDraft(
            newIdentity!,
            year,
            week,
            string.Empty,
            DefaultNotesColor);
        return true;
    }

    public static bool TryEdit(
        CharacterCareerCalendarWeekState? current,
        string? expectedSourceRevision,
        string? notes,
        string? notesColor,
        out CharacterCareerCalendarWeekDraft draft)
    {
        draft = UnavailableDraft();
        if (!IsCoherent(current)
            || !RevisionMatches(current!.SourceRevision, expectedSourceRevision))
        {
            return false;
        }

        string normalizedNotes = notes ?? string.Empty;
        string normalizedColor = string.IsNullOrWhiteSpace(notesColor)
            ? DefaultNotesColor
            : notesColor.Trim();
        if (!IsColorTranslatorHtml(normalizedColor))
        {
            return false;
        }

        draft = new CharacterCareerCalendarWeekDraft(
            current.Identity,
            current.Year,
            current.Week,
            normalizedNotes,
            normalizedColor);
        return true;
    }

    public static bool CanDelete(
        CharacterCareerCalendarWeekState? current,
        CharacterCareerCalendarWeekIdentity? identity,
        string? expectedSourceRevision,
        bool confirmed)
        => confirmed
            && IsCoherent(current)
            && IsValidIdentity(identity)
            && current!.Identity == identity
            && RevisionMatches(current.SourceRevision, expectedSourceRevision);

    public static bool IsCoherent(CharacterCareerCalendarWeekState? state)
        => state is not null
            && IsValidIdentity(state.Identity)
            && IsSupportedIsoWeek(state.Year, state.Week)
            && state.Notes is not null
            && IsColorTranslatorHtml(state.NotesColor)
            && RevisionMatches(
                CalculateLogicalRevision(
                    state.Identity,
                    state.Year,
                    state.Week,
                    state.Notes,
                    state.NotesColor),
                state.LogicalRevision)
            && IsLowerHexRevision(state.SourceRevision);

    public static bool IsSupportedIsoWeek(int year, int week)
        => year is >= SupportedMinimumYear and <= SupportedMaximumYear
            && week >= 1
            && week <= ISOWeek.GetWeeksInYear(year);

    public static bool TryNextWeek(int year, int week, out int nextYear, out int nextWeek)
    {
        nextYear = 0;
        nextWeek = 0;
        if (!IsSupportedIsoWeek(year, week))
        {
            return false;
        }

        int maximum = ISOWeek.GetWeeksInYear(year);
        if (week < maximum)
        {
            nextYear = year;
            nextWeek = week + 1;
            return true;
        }
        if (year == SupportedMaximumYear)
        {
            return false;
        }

        nextYear = year + 1;
        nextWeek = 1;
        return true;
    }

    private static bool IsColorTranslatorHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            return false;
        }
        if (value.Length == 7 && value[0] == '#')
        {
            return value.AsSpan(1).ToString().All(Uri.IsHexDigit);
        }
        return value.All(static character => char.IsAsciiLetter(character));
    }

    private static string CalculateLogicalRevision(
        CharacterCareerCalendarWeekIdentity identity,
        int year,
        int week,
        string notes,
        string notesColor)
        => Sha256(string.Join('\0',
            identity.WeekId.ToString("D"),
            year.ToString(CultureInfo.InvariantCulture),
            week.ToString(CultureInfo.InvariantCulture),
            notes,
            notesColor));

    private static bool RevisionMatches(string actual, string? expected)
        => IsLowerHexRevision(actual)
            && string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool IsLowerHexRevision(string? value)
        => value is { Length: RevisionHexLength }
            && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static CharacterCareerCalendarWeekState Unavailable()
        => new(
            new CharacterCareerCalendarWeekIdentity(Guid.Empty),
            0,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

    private static CharacterCareerCalendarWeekDraft UnavailableDraft()
        => new(
            new CharacterCareerCalendarWeekIdentity(Guid.Empty),
            0,
            0,
            string.Empty,
            string.Empty);
}
