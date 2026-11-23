// Existing typed adapter from the paired Presentation snapshot; Core executes it
// only after store admission. Rules remain in Chummer.Contracts.Characters.
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Workspaces.TypedWizard;

internal static class CareerCalendarMutation
{
    public static string Add(string xml, CareerCalendarAddRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequestAuthority(request.WorkspaceId.Value, request.ExpectedContentRevision);

        CharacterCareerCalendarState current =
            CareerCalendarEditorProjector.ProjectCalendarState(xml);
        RequireExactAuthority(
            current,
            request.ExpectedCalendarRevision,
            request.ExpectedSourceAuthorityDigest);
        if (!CharacterCareerCalendarRules.TryPlanAdd(
                current,
                CharacterCareerCalendarRules.PinnedSourceAuthority,
                request.ExpectedCalendarRevision,
                request.NewIdentity,
                request.RequestedFirstYear,
                request.RequestedFirstWeek,
                out CharacterCareerCalendarWeekDraft draft))
        {
            throw new InvalidOperationException(
                "The requested calendar week violates Chummer5's identity, ISO-week, or first-week rules.");
        }

        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement root = CareerCalendarEditorProjector.RequireCharacterRoot(document);
        XElement[] calendars = root.Elements("calendar").Take(2).ToArray();
        if (calendars.Length > 1)
        {
            throw new InvalidOperationException(
                "The saved runner has duplicate <calendar> containers.");
        }
        XElement calendar = calendars.SingleOrDefault() ?? new XElement("calendar");
        if (calendar.Parent is null)
        {
            root.Add(calendar);
        }
        calendar.Add(CreateWeek(draft));

        string serialized = Serialize(document);
        CharacterCareerCalendarState result =
            CareerCalendarEditorProjector.ProjectCalendarState(serialized);
        CharacterCareerCalendarWeekState[] added = result.Weeks
            .Where(candidate => candidate.Identity == draft.Identity)
            .Take(2)
            .ToArray();
        if (added.Length != 1
            || added[0].Year != draft.Year
            || added[0].Week != draft.Week
            || added[0].Notes != draft.Notes
            || added[0].NotesColor != draft.NotesColor
            || !PreservesExistingWeeks(current.Weeks, result.Weeks, ignoredIdentity: draft.Identity))
        {
            throw new InvalidOperationException(
                "The serialized calendar add did not preserve full collection authority.");
        }
        return serialized;
    }

    public static string Edit(string xml, CareerCalendarEditRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ExpectedWeek);
        ValidateRequestAuthority(request.WorkspaceId.Value, request.ExpectedContentRevision);

        CharacterCareerCalendarState current =
            CareerCalendarEditorProjector.ProjectCalendarState(xml);
        RequireExactAuthority(
            current,
            request.ExpectedCalendarRevision,
            request.ExpectedSourceAuthorityDigest);
        CharacterCareerCalendarWeekState selected = ResolveExpected(current.Weeks, request.ExpectedWeek);
        if (!CharacterCareerCalendarRules.TryPlanEdit(
                current,
                CharacterCareerCalendarRules.PinnedSourceAuthority,
                request.ExpectedCalendarRevision,
                selected.Identity,
                request.ExpectedLogicalRevision,
                request.ExpectedSourceRevision,
                request.Notes,
                request.NotesColor,
                out CharacterCareerCalendarWeekDraft draft))
        {
            throw new InvalidOperationException(
                "The selected calendar week changed or its notes/color violate Chummer5 rules.");
        }

        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement target = ResolveElement(document, draft.Identity);
        SetOptionalElement(target, "notes", draft.Notes, afterName: "week");
        SetOptionalElement(target, "notesColor", draft.NotesColor, afterName: "notes");

        string serialized = Serialize(document);
        CharacterCareerCalendarState result =
            CareerCalendarEditorProjector.ProjectCalendarState(serialized);
        CharacterCareerCalendarWeekState[] edited = result.Weeks
            .Where(candidate => candidate.Identity == draft.Identity)
            .Take(2)
            .ToArray();
        if (edited.Length != 1
            || edited[0].Year != draft.Year
            || edited[0].Week != draft.Week
            || edited[0].Notes != draft.Notes
            || edited[0].NotesColor != draft.NotesColor
            || !PreservesExistingWeeks(current.Weeks, result.Weeks, ignoredIdentity: draft.Identity))
        {
            throw new InvalidOperationException(
                "The serialized calendar edit did not preserve full collection authority.");
        }
        return serialized;
    }

    public static string Delete(string xml, CareerCalendarDeleteRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ExpectedWeek);
        ValidateRequestAuthority(request.WorkspaceId.Value, request.ExpectedContentRevision);

        CharacterCareerCalendarState current =
            CareerCalendarEditorProjector.ProjectCalendarState(xml);
        RequireExactAuthority(
            current,
            request.ExpectedCalendarRevision,
            request.ExpectedSourceAuthorityDigest);
        CharacterCareerCalendarWeekState selected = ResolveExpected(current.Weeks, request.ExpectedWeek);
        if (!CharacterCareerCalendarRules.CanDelete(
                current,
                CharacterCareerCalendarRules.PinnedSourceAuthority,
                request.ExpectedCalendarRevision,
                selected.Identity,
                request.ExpectedLogicalRevision,
                request.ExpectedSourceRevision,
                request.Confirmed))
        {
            throw new InvalidOperationException(
                "Calendar deletion requires confirmation and an unchanged stable week identity.");
        }

        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        ResolveElement(document, selected.Identity).Remove();
        string serialized = Serialize(document);
        CharacterCareerCalendarState result =
            CareerCalendarEditorProjector.ProjectCalendarState(serialized);
        if (result.Weeks.Any(candidate => candidate.Identity == selected.Identity)
            || !PreservesExistingWeeks(current.Weeks, result.Weeks, ignoredIdentity: selected.Identity))
        {
            throw new InvalidOperationException(
                "The serialized calendar deletion did not preserve full collection authority.");
        }
        return serialized;
    }

    private static CharacterCareerCalendarWeekState ResolveExpected(
        IReadOnlyList<CharacterCareerCalendarWeekState> current,
        CharacterCareerCalendarWeekState expected)
    {
        CharacterCareerCalendarWeekState[] matches = current
            .Where(candidate => candidate.Identity == expected.Identity)
            .Take(2)
            .ToArray();
        if (matches.Length != 1 || matches[0] != expected)
        {
            throw new InvalidOperationException(
                "The selected calendar week changed or disappeared while the editor was open.");
        }
        return matches[0];
    }

    private static void RequireExactAuthority(
        CharacterCareerCalendarState current,
        string expectedCalendarRevision,
        string expectedSourceAuthorityDigest)
    {
        if (!CharacterCareerCalendarRules.IsCoherent(current)
            || !string.Equals(
                current.Revision,
                expectedCalendarRevision,
                StringComparison.Ordinal)
            || !string.Equals(
                current.SourceAuthorityDigest,
                expectedSourceAuthorityDigest,
                StringComparison.Ordinal)
            || !string.Equals(
                expectedSourceAuthorityDigest,
                CharacterCareerCalendarRules.PinnedSourceAuthorityDigest,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The calendar or pinned Chummer5 source authority changed before mutation.");
        }
    }

    private static XElement ResolveElement(
        XDocument document,
        CharacterCareerCalendarWeekIdentity identity)
    {
        XElement root = CareerCalendarEditorProjector.RequireCharacterRoot(document);
        XElement[] targets = root.Elements("calendar")
            .SelectMany(static calendar => calendar.Elements("week"))
            .Where(candidate =>
            {
                XElement[] values = candidate.Elements("guid").Take(2).ToArray();
                return values.Length == 1
                    && Guid.TryParse(values[0].Value.Trim(), out Guid id)
                    && id == identity.WeekId;
            })
            .Take(2)
            .ToArray();
        return targets.Length == 1
            ? targets[0]
            : throw new InvalidOperationException(
                "The selected calendar week identity is ambiguous.");
    }

    private static XElement CreateWeek(CharacterCareerCalendarWeekDraft draft)
        => new(
            "week",
            new XElement("guid", draft.Identity.WeekId.ToString("D")),
            new XElement("year", draft.Year.ToString(CultureInfo.InvariantCulture)),
            new XElement("week", draft.Week.ToString(CultureInfo.InvariantCulture)),
            new XElement("notes", draft.Notes),
            new XElement("notesColor", draft.NotesColor));

    private static void SetOptionalElement(
        XElement parent,
        string name,
        string value,
        string afterName)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length > 1)
        {
            throw new InvalidOperationException(
                $"A calendar week has duplicate <{name}> values.");
        }
        if (values.Length == 1)
        {
            values[0].Value = value;
            return;
        }

        XElement created = new(name, value);
        XElement? predecessor = parent.Elements(afterName).SingleOrDefault();
        if (predecessor is null)
        {
            parent.Add(created);
        }
        else
        {
            predecessor.AddAfterSelf(created);
        }
    }

    private static bool PreservesExistingWeeks(
        IReadOnlyList<CharacterCareerCalendarWeekState> before,
        IReadOnlyList<CharacterCareerCalendarWeekState> after,
        CharacterCareerCalendarWeekIdentity ignoredIdentity)
    {
        CharacterCareerCalendarWeekState[] expected = before
            .Where(candidate => candidate.Identity != ignoredIdentity)
            .OrderByDescending(static candidate => candidate.Year)
            .ThenByDescending(static candidate => candidate.Week)
            .ToArray();
        CharacterCareerCalendarWeekState[] actual = after
            .Where(candidate => candidate.Identity != ignoredIdentity)
            .OrderByDescending(static candidate => candidate.Year)
            .ThenByDescending(static candidate => candidate.Week)
            .ToArray();
        return expected.SequenceEqual(actual);
    }

    private static void ValidateRequestAuthority(
        string workspaceId,
        long expectedContentRevision)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new InvalidOperationException(
                "A nonblank dossier identity is required for calendar editing.");
        }
        if (expectedContentRevision <= 0)
        {
            throw new InvalidOperationException(
                "A positive dossier revision is required for calendar editing.");
        }
    }

    private static string Serialize(XDocument document)
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        document.Save(writer, SaveOptions.DisableFormatting);
        return writer.ToString();
    }
}
