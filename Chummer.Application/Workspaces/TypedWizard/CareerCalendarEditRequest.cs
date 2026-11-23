// Existing typed adapter from the paired Presentation snapshot; Core executes it
// only after store admission. Rules remain in Chummer.Contracts.Characters.
using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces.TypedWizard;

internal sealed record CareerCalendarEditorState(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    CharacterCareerCalendarState Calendar,
    bool CanChangeStartingDate,
    string ChangeStartingDateBlocker)
{
    public IReadOnlyList<CharacterCareerCalendarWeekState> Weeks { get; } = Calendar.Weeks
        .OrderByDescending(static candidate => candidate.Year)
        .ThenByDescending(static candidate => candidate.Week)
        .ToArray();

    public string CalendarRevision => Calendar.Revision;
    public string SourceAuthorityDigest => Calendar.SourceAuthorityDigest;
}

internal sealed record CareerCalendarAddRequest(
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    string ExpectedCalendarRevision,
    string ExpectedSourceAuthorityDigest,
    CharacterCareerCalendarWeekIdentity NewIdentity,
    int RequestedFirstYear,
    int RequestedFirstWeek);

internal sealed record CareerCalendarEditRequest(
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    string ExpectedCalendarRevision,
    string ExpectedSourceAuthorityDigest,
    CharacterCareerCalendarWeekState ExpectedWeek,
    string ExpectedLogicalRevision,
    string ExpectedSourceRevision,
    string Notes,
    string NotesColor);

internal sealed record CareerCalendarDeleteRequest(
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    string ExpectedCalendarRevision,
    string ExpectedSourceAuthorityDigest,
    CharacterCareerCalendarWeekState ExpectedWeek,
    string ExpectedLogicalRevision,
    string ExpectedSourceRevision,
    bool Confirmed);

internal static class CareerCalendarEditorProjector
{
    internal const string ChangeStartingDateBlocker =
        "Change Starting Date is unavailable because the pinned Chummer5 CalendarWeek.ModifyWeekAsync implementation ignores its requested offset.";

    public static CareerCalendarEditorState Project(
        string xml,
        CharacterWorkspaceId workspaceId,
        long contentRevision)
    {
        if (string.IsNullOrWhiteSpace(workspaceId.Value))
        {
            throw new InvalidOperationException(
                "A nonblank dossier identity is required for calendar editing.");
        }
        if (contentRevision <= 0)
        {
            throw new InvalidOperationException(
                "Dossier revision is unavailable. Reload before editing the calendar.");
        }

        return new CareerCalendarEditorState(
            workspaceId,
            contentRevision,
            ProjectCalendarState(xml),
            CanChangeStartingDate: false,
            ChangeStartingDateBlocker);
    }

    internal static IReadOnlyList<CharacterCareerCalendarWeekState> ProjectState(string xml)
        => ProjectCalendarState(xml).Weeks
            .OrderByDescending(static candidate => candidate.Year)
            .ThenByDescending(static candidate => candidate.Week)
            .ToArray();

    internal static CharacterCareerCalendarState ProjectCalendarState(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement root = RequireCharacterRoot(document);
        bool isCareer = ReadRequiredBool(root, "created");
        if (!isCareer)
        {
            throw new InvalidOperationException(
                "Calendar editing is available only for career runners.");
        }

        XElement[] calendars = root.Elements("calendar").Take(2).ToArray();
        if (calendars.Length > 1)
        {
            throw new InvalidOperationException(
                "The saved runner has duplicate <calendar> containers.");
        }

        List<string> rawWeekElements = [];
        HashSet<Guid> identities = [];
        HashSet<(int Year, int Week)> coordinates = [];
        foreach (XElement weekElement in calendars.SingleOrDefault()?.Elements("week") ?? [])
        {
            Guid id = ReadRequiredGuid(weekElement, "guid");
            int year = ReadRequiredInt(weekElement, "year");
            int week = ReadRequiredInt(weekElement, "week");
            if (!identities.Add(id))
            {
                throw new InvalidOperationException(
                    "The saved runner has duplicate calendar-week GUIDs.");
            }
            if (!coordinates.Add((year, week)))
            {
                throw new InvalidOperationException(
                    "The saved runner has duplicate calendar year/week coordinates.");
            }

            string notes = ReadOptionalText(weekElement, "notes", string.Empty);
            string notesColor = ReadOptionalText(
                weekElement,
                "notesColor",
                CharacterCareerCalendarRules.DefaultNotesColor);
            if (!CharacterCareerCalendarRules.TryNormalizeNotesColor(
                    notesColor,
                    out string normalizedColor))
            {
                throw new InvalidOperationException(
                    $"Calendar week {id:D} is outside Chummer5's editable bounds.");
            }
            rawWeekElements.Add(new XElement(
                    "week",
                    new XElement("guid", id.ToString("D")),
                    new XElement("year", year.ToString(CultureInfo.InvariantCulture)),
                    new XElement("week", week.ToString(CultureInfo.InvariantCulture)),
                    new XElement("notes", notes),
                    new XElement("notesColor", normalizedColor))
                .ToString(SaveOptions.DisableFormatting));
        }

        if (!CharacterCareerCalendarRules.TryCreateCalendar(
                isCareer,
                CharacterCareerCalendarRules.PinnedSourceAuthority,
                rawWeekElements,
                out CharacterCareerCalendarState calendar)
            || !CharacterCareerCalendarRules.IsCoherent(calendar))
        {
            throw new InvalidOperationException(
                "The saved calendar is not coherent with the pinned Chummer5 source authority.");
        }
        return calendar;
    }

    internal static XElement RequireCharacterRoot(XDocument document)
        => document.Root is { } root && root.Name == XName.Get("character")
            ? root
            : throw new InvalidOperationException(
                "Workspace XML must use <character> as the root node.");

    internal static bool ReadRequiredBool(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1
            || !bool.TryParse(values[0].Value.Trim(), out bool value))
        {
            throw new InvalidOperationException(
                $"The saved runner has an invalid or duplicate <{name}> value.");
        }
        return value;
    }

    internal static Guid ReadRequiredGuid(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1
            || !Guid.TryParse(values[0].Value.Trim(), out Guid value)
            || value == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"A calendar week has an invalid or duplicate <{name}> value.");
        }
        return value;
    }

    internal static int ReadRequiredInt(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1
            || !int.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value))
        {
            throw new InvalidOperationException(
                $"A calendar week has an invalid or duplicate <{name}> value.");
        }
        return value;
    }

    internal static string ReadOptionalText(
        XElement parent,
        string name,
        string fallback)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        return values.Length switch
        {
            0 => fallback,
            1 => values[0].Value,
            _ => throw new InvalidOperationException(
                $"A calendar week has duplicate <{name}> values.")
        };
    }
}
