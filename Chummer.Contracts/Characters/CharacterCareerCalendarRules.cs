using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

public sealed record CharacterCareerCalendarSourceFile(
    string Path,
    string Sha256);

public sealed record CharacterCareerCalendarSourceSlice(
    string Name,
    string Path,
    int FirstLine,
    int LastLine,
    string Sha256);

/// <summary>
/// Reproducible source authority for the exact Chummer5 calendar behavior. File
/// digests cover the complete source files; handler and callee digests cover the
/// inclusive line spans recorded at <see cref="CharacterCareerCalendarRules.PinnedChummer5Revision"/>.
/// </summary>
public sealed record CharacterCareerCalendarSourceAuthority(
    string Revision,
    IReadOnlyList<CharacterCareerCalendarSourceFile> SourceFiles,
    IReadOnlyList<CharacterCareerCalendarSourceSlice> Handlers,
    IReadOnlyList<CharacterCareerCalendarSourceSlice> Callees);

public sealed record CharacterCareerCalendarWeekIdentity(Guid WeekId);

public sealed record CharacterCareerCalendarWeekState(
    CharacterCareerCalendarWeekIdentity Identity,
    int Year,
    int Week,
    string Notes,
    string NotesColor,
    string LogicalRevision,
    string SourceRevision,
    string SourceAuthorityDigest)
{
    public CharacterCareerCalendarWeekState(
        CharacterCareerCalendarWeekIdentity identity,
        int year,
        int week,
        string notes,
        string notesColor,
        string logicalRevision,
        string sourceRevision)
        : this(
            identity,
            year,
            week,
            notes,
            notesColor,
            logicalRevision,
            sourceRevision,
            CharacterCareerCalendarRules.PinnedSourceAuthorityDigest)
    {
    }

    public void Deconstruct(
        out CharacterCareerCalendarWeekIdentity identity,
        out int year,
        out int week,
        out string notes,
        out string notesColor,
        out string logicalRevision,
        out string sourceRevision)
    {
        identity = Identity;
        year = Year;
        week = Week;
        notes = Notes;
        notesColor = NotesColor;
        logicalRevision = LogicalRevision;
        sourceRevision = SourceRevision;
    }
}

public sealed record CharacterCareerCalendarState(
    bool IsCareer,
    IReadOnlyList<CharacterCareerCalendarWeekState> Weeks,
    string Revision,
    string SourceAuthorityDigest);

public sealed record CharacterCareerCalendarWeekDraft(
    CharacterCareerCalendarWeekIdentity Identity,
    int Year,
    int Week,
    string Notes,
    string NotesColor,
    string ExpectedLogicalRevision,
    string ExpectedSourceRevision,
    string SourceElement,
    string SourceRevision)
{
    public CharacterCareerCalendarWeekDraft(
        CharacterCareerCalendarWeekIdentity identity,
        int year,
        int week,
        string notes,
        string notesColor)
        : this(
            identity,
            year,
            week,
            notes,
            notesColor,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty)
    {
    }

    public void Deconstruct(
        out CharacterCareerCalendarWeekIdentity identity,
        out int year,
        out int week,
        out string notes,
        out string notesColor)
    {
        identity = Identity;
        year = Year;
        week = Week;
        notes = Notes;
        notesColor = NotesColor;
    }
}

public sealed record CharacterCareerCalendarStartSelection(
    int Year,
    int Week,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>
/// Chummer5's pinned change-start handler calculates a delta but the pinned
/// ModifyWeekAsync implementation ignores it. This plan therefore deliberately
/// carries no replacement weeks and cannot be mistaken for a durable mutation.
/// </summary>
public sealed record CharacterCareerCalendarChangeStartPlan(
    CharacterCareerCalendarStartSelection RequestedStart,
    string ExpectedCalendarRevision,
    string ResultCalendarRevision,
    bool HasDurableMutation);

/// <summary>
/// Deterministic, phone-safe authority for CharacterCareer's Calendar week
/// collection at pinned Chummer5 revision fe4355d. Source loading binds every
/// durable field to the complete saved &lt;week&gt; element. Mutations require the
/// exact calendar, element, logical, and Chummer5 source authorities.
/// </summary>
public static class CharacterCareerCalendarRules
{
    public const int RevisionHexLength = 64;
    public const int MinimumYear = 2000;
    public const int MaximumYear = 9000;
    public const int FirstWeekMinimumYear = MinimumYear;
    public const int FirstWeekMaximumYear = MaximumYear;
    public const int SupportedMinimumYear = MinimumYear;
    public const int SupportedMaximumYear = MaximumYear;
    public const string DefaultNotesColor = "Chocolate";
    public const string PinnedChummer5Revision = "fe4355d06c98cd9b7feade89f5fc1a0e438f7ce3";

    private static readonly ReadOnlyCollection<CharacterCareerCalendarSourceFile> PinnedSourceFiles =
        Array.AsReadOnly<CharacterCareerCalendarSourceFile>(
        [
            new(
                "Chummer/Forms/Character Forms/CharacterCareer.cs",
                "b1f58def07884877638e7c31a5af194a5ce8869c0020447154f827ba56e813ea"),
            new(
                "Chummer/Forms/Selection Forms/SelectCalendarStart.cs",
                "9b34a19f5549fd6233e94814e41d93aaf80e58d9575d23bebc707d89ff8eeb59"),
            new(
                "Chummer/Backend/Uniques/CalendarWeek.cs",
                "151aa754683428f30fc1c781867b29837fbf07c8510b53ec4702fab7916eed1c"),
            new(
                "Chummer/Backend/Static/Extensions/IntegerExtensions.cs",
                "8f93cac323ff86ae873839ea90001b744238435e59703c4d7e7a4a0a8ea6e710")
        ]);

    private static readonly ReadOnlyCollection<CharacterCareerCalendarSourceSlice> PinnedHandlers =
        Array.AsReadOnly<CharacterCareerCalendarSourceSlice>(
        [
            new(
                "CharacterCareer.cmdAddWeek_Click",
                "Chummer/Forms/Character Forms/CharacterCareer.cs",
                9715,
                9757,
                "862e75f41f1fbc4291cc359b0a7c6258255e285e5e65e7b8a5b18f8d4ba9a07c"),
            new(
                "CharacterCareer.cmdDeleteWeek_Click",
                "Chummer/Forms/Character Forms/CharacterCareer.cs",
                9759,
                9790,
                "5b3592a20fd6aa9d1d034671a781e55eaee76a096b2780b0f5c2e24fe8fdddbc"),
            new(
                "CharacterCareer.cmdEditWeek_Click",
                "Chummer/Forms/Character Forms/CharacterCareer.cs",
                9792,
                9830,
                "45acea10da466b40eab548d2ab7934b9456b548afb9cc7d44a289a739c550ff4"),
            new(
                "CharacterCareer.cmdChangeStartWeek_Click",
                "Chummer/Forms/Character Forms/CharacterCareer.cs",
                9832,
                9879,
                "b910327763be4ed61e35f23b3cfe39df7ed17e0a1dcf4a799cffdf679a984466")
        ]);

    private static readonly ReadOnlyCollection<CharacterCareerCalendarSourceSlice> PinnedCallees =
        Array.AsReadOnly<CharacterCareerCalendarSourceSlice>(
        [
            new(
                "SelectCalendarStart.AcceptForm",
                "Chummer/Forms/Selection Forms/SelectCalendarStart.cs",
                86,
                92,
                "82a72c9e32df1feead68c5c947128dbf313bd134de7df56572c819759505ea57"),
            new(
                "SelectCalendarStart.nudYear_ValueChanged",
                "Chummer/Forms/Selection Forms/SelectCalendarStart.cs",
                94,
                98,
                "4a176161faf6222093b0a1469f6d6496b4ef9845706a26dc0e7f6f1c59bd3863"),
            new(
                "SelectCalendarStart.UpdateDateSpan",
                "Chummer/Forms/Selection Forms/SelectCalendarStart.cs",
                101,
                120,
                "672f2499cfda9e32bb8d0a8c8f1763db23095b10915f18736a986cc009ff6f7b"),
            new(
                "CalendarWeek.construction",
                "Chummer/Backend/Uniques/CalendarWeek.cs",
                246,
                284,
                "5c0b26e913ecded4861d87536f07e8369ca3930c91747ab7f7d76eaad26c9129"),
            new(
                "CalendarWeek.Save",
                "Chummer/Backend/Uniques/CalendarWeek.cs",
                290,
                304,
                "63994d34dd8062d9d09aaa1ef1edb43603fbdddafef03cd6041efae7f60d9fc0"),
            new(
                "CalendarWeek.Load",
                "Chummer/Backend/Uniques/CalendarWeek.cs",
                310,
                323,
                "6ce808cd6cd686e3bc2a8f1b515289742e005c33814abbfbb88cc59fe3ed3945"),
            new(
                "CalendarWeek.LoadAsync",
                "Chummer/Backend/Uniques/CalendarWeek.cs",
                330,
                350,
                "3e1a100565708cfa1a2400098da3e02210d33cb8702931b898ca64012cc557cf"),
            new(
                "CalendarWeek.SetWeekAsync",
                "Chummer/Backend/Uniques/CalendarWeek.cs",
                695,
                785,
                "b6b79b76c926cbb52dbc4262981ed2e639a827ef7f3a4ad0ff80e8128253d326"),
            new(
                "CalendarWeek.ModifyWeekAsync",
                "Chummer/Backend/Uniques/CalendarWeek.cs",
                787,
                802,
                "a23197b47ecb10844478388fa1f851d292541c81618addd693f8fc7d86234128"),
            new(
                "IntegerExtensions.IsYearLongYear",
                "Chummer/Backend/Static/Extensions/IntegerExtensions.cs",
                52,
                73,
                "592cad88a5bd6978ec904d58730b731711eaeeb9dc378c45cc7d6efc01bb94e0")
        ]);

    public static CharacterCareerCalendarSourceAuthority PinnedSourceAuthority { get; } =
        new(PinnedChummer5Revision, PinnedSourceFiles, PinnedHandlers, PinnedCallees);

    public static string PinnedSourceAuthorityDigest { get; } =
        CalculateSourceAuthorityDigest(PinnedSourceAuthority);

    public static bool IsExactSourceAuthority(CharacterCareerCalendarSourceAuthority? authority)
        => authority is not null
            && string.Equals(authority.Revision, PinnedChummer5Revision, StringComparison.Ordinal)
            && SlicesMatch(authority.SourceFiles, PinnedSourceFiles)
            && SlicesMatch(authority.Handlers, PinnedHandlers)
            && SlicesMatch(authority.Callees, PinnedCallees);

    public static bool IsValidIdentity(CharacterCareerCalendarWeekIdentity? identity)
        => identity is { WeekId: var weekId } && weekId != Guid.Empty;

    public static bool TryCreateState(
        bool isCareer,
        CharacterCareerCalendarSourceAuthority? authority,
        string? rawSourceElement,
        out CharacterCareerCalendarWeekState state)
    {
        state = UnavailableWeek();
        if (!isCareer
            || !IsExactSourceAuthority(authority)
            || !TryParseSourceElement(
                rawSourceElement,
                out CharacterCareerCalendarWeekIdentity identity,
                out int year,
                out int week,
                out string notes,
                out string notesColor))
        {
            return false;
        }

        string sourceRevision = Sha256(rawSourceElement!);
        state = new CharacterCareerCalendarWeekState(
            identity,
            year,
            week,
            notes,
            notesColor,
            CalculateLogicalRevision(
                identity,
                year,
                week,
                notes,
                notesColor,
                sourceRevision,
                PinnedSourceAuthorityDigest),
            sourceRevision,
            PinnedSourceAuthorityDigest);
        return true;
    }

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
        if (!TryCreateState(
                created,
                PinnedSourceAuthority,
                rawSourceState,
                out state))
        {
            return false;
        }

        bool matches = state.Identity == identity
            && state.Year == year
            && state.Week == week
            && string.Equals(state.Notes, notes, StringComparison.Ordinal)
            && TryNormalizeNotesColor(notesColor, out string normalizedColor)
            && string.Equals(state.NotesColor, normalizedColor, StringComparison.Ordinal);
        if (!matches)
        {
            state = UnavailableWeek();
        }
        return matches;
    }

    public static bool TryCreateCalendar(
        bool isCareer,
        CharacterCareerCalendarSourceAuthority? authority,
        IReadOnlyList<string>? rawWeekElements,
        out CharacterCareerCalendarState state)
    {
        state = UnavailableCalendar();
        if (!isCareer || !IsExactSourceAuthority(authority) || rawWeekElements is null)
        {
            return false;
        }

        var weeks = new CharacterCareerCalendarWeekState[rawWeekElements.Count];
        for (int index = 0; index < rawWeekElements.Count; index++)
        {
            if (!TryCreateState(isCareer, authority, rawWeekElements[index], out weeks[index]))
            {
                return false;
            }
        }

        if (!HasUniqueIdentityAndCoordinates(weeks))
        {
            return false;
        }

        ReadOnlyCollection<CharacterCareerCalendarWeekState> exact = Array.AsReadOnly(weeks);
        state = new CharacterCareerCalendarState(
            true,
            exact,
            CalculateCalendarRevision(exact, PinnedSourceAuthorityDigest),
            PinnedSourceAuthorityDigest);
        return true;
    }

    public static bool TryPlanAdd(
        CharacterCareerCalendarState? current,
        CharacterCareerCalendarSourceAuthority? authority,
        string? expectedCalendarRevision,
        CharacterCareerCalendarWeekIdentity? newIdentity,
        int requestedFirstYear,
        int requestedFirstWeek,
        out CharacterCareerCalendarWeekDraft draft)
    {
        draft = UnavailableDraft();
        if (!IsCoherent(current)
            || !IsExactSourceAuthority(authority)
            || !RevisionMatches(current!.Revision, expectedCalendarRevision)
            || !IsValidIdentity(newIdentity)
            || current.Weeks.Any(candidate => candidate.Identity == newIdentity))
        {
            return false;
        }

        int year;
        int week;
        if (current.Weeks.Count == 0)
        {
            if (!IsSupportedIsoWeek(requestedFirstYear, requestedFirstWeek))
            {
                return false;
            }

            year = requestedFirstYear;
            week = requestedFirstWeek;
        }
        else
        {
            CharacterCareerCalendarWeekState latest = current.Weeks
                .OrderByDescending(static candidate => candidate.Year)
                .ThenByDescending(static candidate => candidate.Week)
                .First();
            if (!TryNextWeek(latest.Year, latest.Week, out year, out week))
            {
                return false;
            }
        }

        if (current.Weeks.Any(candidate => candidate.Year == year && candidate.Week == week))
        {
            return false;
        }

        draft = CreateDraft(
            newIdentity!,
            year,
            week,
            string.Empty,
            DefaultNotesColor,
            string.Empty,
            string.Empty);
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
        return TryCreateCalendarFromStates(current, out CharacterCareerCalendarState calendar)
            && TryPlanAdd(
                calendar,
                PinnedSourceAuthority,
                calendar.Revision,
                newIdentity,
                requestedFirstYear,
                requestedFirstWeek,
                out draft);
    }

    public static bool TryPlanEdit(
        CharacterCareerCalendarState? current,
        CharacterCareerCalendarSourceAuthority? authority,
        string? expectedCalendarRevision,
        CharacterCareerCalendarWeekIdentity? identity,
        string? expectedLogicalRevision,
        string? expectedSourceRevision,
        string? notes,
        string? notesColor,
        out CharacterCareerCalendarWeekDraft draft)
    {
        draft = UnavailableDraft();
        if (!IsCoherent(current)
            || !IsExactSourceAuthority(authority)
            || !RevisionMatches(current!.Revision, expectedCalendarRevision)
            || !IsValidIdentity(identity)
            || notes is null
            || !TryNormalizeNotesColor(notesColor, out string normalizedColor))
        {
            return false;
        }

        CharacterCareerCalendarWeekState? selected = current.Weeks
            .SingleOrDefault(candidate => candidate.Identity == identity);
        if (selected is null
            || !RevisionMatches(selected.LogicalRevision, expectedLogicalRevision)
            || !RevisionMatches(selected.SourceRevision, expectedSourceRevision))
        {
            return false;
        }

        draft = CreateDraft(
            selected.Identity,
            selected.Year,
            selected.Week,
            notes,
            normalizedColor,
            selected.LogicalRevision,
            selected.SourceRevision);
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
        return current is not null
            && TryCreateCalendarFromStates([current], out CharacterCareerCalendarState calendar)
            && TryPlanEdit(
                calendar,
                PinnedSourceAuthority,
                calendar.Revision,
                current.Identity,
                current.LogicalRevision,
                expectedSourceRevision,
                notes,
                notesColor,
                out draft);
    }

    public static bool CanDelete(
        CharacterCareerCalendarState? current,
        CharacterCareerCalendarSourceAuthority? authority,
        string? expectedCalendarRevision,
        CharacterCareerCalendarWeekIdentity? identity,
        string? expectedLogicalRevision,
        string? expectedSourceRevision,
        bool confirmed)
    {
        if (!confirmed
            || !IsCoherent(current)
            || !IsExactSourceAuthority(authority)
            || !RevisionMatches(current!.Revision, expectedCalendarRevision)
            || !IsValidIdentity(identity))
        {
            return false;
        }

        CharacterCareerCalendarWeekState? selected = current.Weeks
            .SingleOrDefault(candidate => candidate.Identity == identity);
        return selected is not null
            && RevisionMatches(selected.LogicalRevision, expectedLogicalRevision)
            && RevisionMatches(selected.SourceRevision, expectedSourceRevision);
    }

    public static bool CanDelete(
        CharacterCareerCalendarWeekState? current,
        CharacterCareerCalendarWeekIdentity? identity,
        string? expectedSourceRevision,
        bool confirmed)
        => current is not null
            && TryCreateCalendarFromStates([current], out CharacterCareerCalendarState calendar)
            && CanDelete(
                calendar,
                PinnedSourceAuthority,
                calendar.Revision,
                identity,
                current.LogicalRevision,
                expectedSourceRevision,
                confirmed);

    public static bool TryPlanChangeStart(
        CharacterCareerCalendarState? current,
        CharacterCareerCalendarSourceAuthority? authority,
        string? expectedCalendarRevision,
        int requestedYear,
        int requestedWeek,
        out CharacterCareerCalendarChangeStartPlan plan)
    {
        plan = UnavailableChangeStartPlan();
        if (!IsCoherent(current)
            || current!.Weeks.Count == 0
            || !IsExactSourceAuthority(authority)
            || !RevisionMatches(current.Revision, expectedCalendarRevision)
            || !TrySelectStart(
                current.IsCareer,
                authority,
                requestedYear,
                requestedWeek,
                out CharacterCareerCalendarStartSelection selection))
        {
            return false;
        }

        plan = new CharacterCareerCalendarChangeStartPlan(
            selection,
            current.Revision,
            current.Revision,
            HasDurableMutation: false);
        return true;
    }

    public static bool TrySelectStart(
        bool isCareer,
        CharacterCareerCalendarSourceAuthority? authority,
        int year,
        int week,
        out CharacterCareerCalendarStartSelection selection)
    {
        selection = new CharacterCareerCalendarStartSelection(0, 0, default, default);
        if (!isCareer || !IsExactSourceAuthority(authority) || !IsSupportedIsoWeek(year, week))
        {
            return false;
        }

        DateTime start = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday);
        selection = new CharacterCareerCalendarStartSelection(
            year,
            week,
            DateOnly.FromDateTime(start),
            DateOnly.FromDateTime(start.AddDays(6)));
        return true;
    }

    public static bool TryGetWeeksInYear(
        bool isCareer,
        CharacterCareerCalendarSourceAuthority? authority,
        int year,
        out int weeks)
    {
        weeks = 0;
        if (!isCareer
            || !IsExactSourceAuthority(authority)
            || year is < MinimumYear or > MaximumYear)
        {
            return false;
        }

        weeks = IsYearLongYear(year) ? 53 : 52;
        return true;
    }

    public static bool IsCoherent(CharacterCareerCalendarWeekState? state)
        => state is not null
            && IsValidIdentity(state.Identity)
            && IsSupportedIsoWeek(state.Year, state.Week)
            && state.Notes is not null
            && TryNormalizeNotesColor(state.NotesColor, out string normalizedColor)
            && string.Equals(state.NotesColor, normalizedColor, StringComparison.Ordinal)
            && RevisionMatches(PinnedSourceAuthorityDigest, state.SourceAuthorityDigest)
            && IsLowerHexRevision(state.SourceRevision)
            && RevisionMatches(
                CalculateLogicalRevision(
                    state.Identity,
                    state.Year,
                    state.Week,
                    state.Notes,
                    state.NotesColor,
                    state.SourceRevision,
                    state.SourceAuthorityDigest),
                state.LogicalRevision);

    public static bool IsCoherent(CharacterCareerCalendarState? state)
        => state is { IsCareer: true }
            && RevisionMatches(PinnedSourceAuthorityDigest, state.SourceAuthorityDigest)
            && state.Weeks is not null
            && state.Weeks.All(IsCoherent)
            && HasUniqueIdentityAndCoordinates(state.Weeks)
            && RevisionMatches(
                CalculateCalendarRevision(state.Weeks, state.SourceAuthorityDigest),
                state.Revision);

    public static bool IsSupportedIsoWeek(int year, int week)
        => year is >= MinimumYear and <= MaximumYear
            && week >= 1
            && week <= (IsYearLongYear(year) ? 53 : 52);

    public static bool TryNextWeek(int year, int week, out int nextYear, out int nextWeek)
    {
        nextYear = 0;
        nextWeek = 0;
        if (!IsSupportedIsoWeek(year, week))
        {
            return false;
        }

        int maximum = IsYearLongYear(year) ? 53 : 52;
        if (week < maximum)
        {
            nextYear = year;
            nextWeek = week + 1;
            return true;
        }
        if (year == MaximumYear)
        {
            return false;
        }

        nextYear = year + 1;
        nextWeek = 1;
        return true;
    }

    public static bool TryNormalizeNotesColor(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(value) || value.Length > 64)
        {
            return false;
        }
        if (value.Length == 7 && value[0] == '#')
        {
            if (!value.AsSpan(1).ToString().All(Uri.IsHexDigit))
            {
                return false;
            }

            normalized = string.Concat("#", value.AsSpan(1).ToString().ToUpperInvariant());
            return true;
        }
        if (!value.All(static character => char.IsAsciiLetter(character)))
        {
            return false;
        }

        Color translated;
        try
        {
            translated = ColorTranslator.FromHtml(value);
        }
        catch (ArgumentException)
        {
            return false;
        }
        if (!translated.IsKnownColor)
        {
            return false;
        }

        normalized = ColorTranslator.ToHtml(translated);
        return !string.IsNullOrEmpty(normalized);
    }

    public static string CalculateSourceAuthorityDigest(
        CharacterCareerCalendarSourceAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        var payload = new StringBuilder();
        payload.Append(authority.Revision).Append('\0');
        AppendSourceFiles(payload, authority.SourceFiles);
        AppendSourceSlices(payload, authority.Handlers);
        AppendSourceSlices(payload, authority.Callees);
        return Sha256(payload.ToString());
    }

    private static bool TryParseSourceElement(
        string? rawSourceElement,
        out CharacterCareerCalendarWeekIdentity identity,
        out int year,
        out int week,
        out string notes,
        out string notesColor)
    {
        identity = new CharacterCareerCalendarWeekIdentity(Guid.Empty);
        year = 0;
        week = 0;
        notes = string.Empty;
        notesColor = string.Empty;
        if (string.IsNullOrEmpty(rawSourceElement))
        {
            return false;
        }

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using var reader = XmlReader.Create(new StringReader(rawSourceElement), settings);
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return false;
        }

        XElement? root = document.Root;
        if (document.Declaration is not null
            || root is null
            || document.Nodes().Any(node => node != root
                && (node is not XText text || !string.IsNullOrWhiteSpace(text.Value)))
            || root.Name != "week"
            || root.HasAttributes
            || root.Nodes().Any(static node => node is not XElement
                && (node is not XText text || !string.IsNullOrWhiteSpace(text.Value))))
        {
            return false;
        }

        XElement[] elements = [.. root.Elements()];
        string[] expectedNames = ["guid", "year", "week", "notes", "notesColor"];
        if (elements.Length != expectedNames.Length)
        {
            return false;
        }
        for (int index = 0; index < expectedNames.Length; index++)
        {
            if (elements[index].Name != expectedNames[index]
                || elements[index].HasAttributes
                || elements[index].HasElements
                || elements[index].Nodes().Any(static node => node is not XText || node is XCData))
            {
                return false;
            }
        }

        string rawGuid = elements[0].Value;
        string rawYear = elements[1].Value;
        string rawWeek = elements[2].Value;
        notes = elements[3].Value;
        string rawColor = elements[4].Value;
        if (!Guid.TryParseExact(rawGuid, "D", out Guid weekId)
            || weekId == Guid.Empty
            || !string.Equals(rawGuid, weekId.ToString("D"), StringComparison.Ordinal)
            || !int.TryParse(rawYear, NumberStyles.None, CultureInfo.InvariantCulture, out year)
            || !string.Equals(rawYear, year.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            || !int.TryParse(rawWeek, NumberStyles.None, CultureInfo.InvariantCulture, out week)
            || !string.Equals(rawWeek, week.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            || !IsSupportedIsoWeek(year, week)
            || !TryNormalizeNotesColor(rawColor, out notesColor)
            || !string.Equals(rawColor, notesColor, StringComparison.Ordinal))
        {
            return false;
        }

        identity = new CharacterCareerCalendarWeekIdentity(weekId);
        return true;
    }

    private static CharacterCareerCalendarWeekDraft CreateDraft(
        CharacterCareerCalendarWeekIdentity identity,
        int year,
        int week,
        string notes,
        string notesColor,
        string expectedLogicalRevision,
        string expectedSourceRevision)
    {
        string sourceElement = new XElement(
            "week",
            new XElement("guid", identity.WeekId.ToString("D")),
            new XElement("year", year.ToString(CultureInfo.InvariantCulture)),
            new XElement("week", week.ToString(CultureInfo.InvariantCulture)),
            new XElement("notes", notes),
            new XElement("notesColor", notesColor))
            .ToString(SaveOptions.DisableFormatting);
        return new CharacterCareerCalendarWeekDraft(
            identity,
            year,
            week,
            notes,
            notesColor,
            expectedLogicalRevision,
            expectedSourceRevision,
            sourceElement,
            Sha256(sourceElement));
    }

    private static bool TryCreateCalendarFromStates(
        IReadOnlyList<CharacterCareerCalendarWeekState> weeks,
        out CharacterCareerCalendarState calendar)
    {
        calendar = UnavailableCalendar();
        if (weeks.Any(static week => !IsCoherent(week))
            || !HasUniqueIdentityAndCoordinates(weeks))
        {
            return false;
        }

        ReadOnlyCollection<CharacterCareerCalendarWeekState> exact =
            Array.AsReadOnly(weeks.ToArray());
        calendar = new CharacterCareerCalendarState(
            true,
            exact,
            CalculateCalendarRevision(exact, PinnedSourceAuthorityDigest),
            PinnedSourceAuthorityDigest);
        return true;
    }

    private static bool HasUniqueIdentityAndCoordinates(
        IReadOnlyList<CharacterCareerCalendarWeekState> weeks)
        => weeks.Select(static candidate => candidate.Identity.WeekId).Distinct().Count() == weeks.Count
            && weeks.Select(static candidate => (candidate.Year, candidate.Week)).Distinct().Count() == weeks.Count;

    private static bool IsYearLongYear(int year)
    {
        int yearDiv4 = Math.DivRem(year, 4, out int yearMod4);
        int yearDiv100 = Math.DivRem(year, 100, out int yearMod100);
        int yearDiv400 = Math.DivRem(year, 400, out int yearMod400);
        bool isLeapYear = yearMod4 == 0 && (yearMod100 != 0 || yearMod400 == 0);
        int dayOfWeekOfDecember31 = (year + yearDiv4 - yearDiv100 + yearDiv400) % 7;
        return dayOfWeekOfDecember31 == (isLeapYear ? 5 : 4);
    }

    private static string CalculateLogicalRevision(
        CharacterCareerCalendarWeekIdentity identity,
        int year,
        int week,
        string notes,
        string notesColor,
        string sourceRevision,
        string sourceAuthorityDigest)
        => Sha256(string.Join('\0',
            identity.WeekId.ToString("D"),
            year.ToString(CultureInfo.InvariantCulture),
            week.ToString(CultureInfo.InvariantCulture),
            notes,
            notesColor,
            sourceRevision,
            sourceAuthorityDigest));

    private static string CalculateCalendarRevision(
        IReadOnlyList<CharacterCareerCalendarWeekState> weeks,
        string sourceAuthorityDigest)
    {
        var payload = new StringBuilder();
        payload.Append(sourceAuthorityDigest).Append('\0').Append(weeks.Count).Append('\0');
        foreach (CharacterCareerCalendarWeekState week in weeks)
        {
            payload.Append(week.Identity.WeekId.ToString("D")).Append('\0')
                .Append(week.Year.ToString(CultureInfo.InvariantCulture)).Append('\0')
                .Append(week.Week.ToString(CultureInfo.InvariantCulture)).Append('\0')
                .Append(week.Notes).Append('\0')
                .Append(week.NotesColor).Append('\0')
                .Append(week.LogicalRevision).Append('\0')
                .Append(week.SourceRevision).Append('\0');
        }
        return Sha256(payload.ToString());
    }

    private static bool SlicesMatch(
        IReadOnlyList<CharacterCareerCalendarSourceFile>? actual,
        ReadOnlyCollection<CharacterCareerCalendarSourceFile> expected)
        => actual is not null
            && actual.Count == expected.Count
            && actual.Select((value, index) => value == expected[index]).All(static matches => matches);

    private static bool SlicesMatch(
        IReadOnlyList<CharacterCareerCalendarSourceSlice>? actual,
        ReadOnlyCollection<CharacterCareerCalendarSourceSlice> expected)
        => actual is not null
            && actual.Count == expected.Count
            && actual.Select((value, index) => value == expected[index]).All(static matches => matches);

    private static void AppendSourceFiles(
        StringBuilder payload,
        IReadOnlyList<CharacterCareerCalendarSourceFile> files)
    {
        payload.Append(files.Count).Append('\0');
        foreach (CharacterCareerCalendarSourceFile file in files)
        {
            payload.Append(file.Path).Append('\0').Append(file.Sha256).Append('\0');
        }
    }

    private static void AppendSourceSlices(
        StringBuilder payload,
        IReadOnlyList<CharacterCareerCalendarSourceSlice> slices)
    {
        payload.Append(slices.Count).Append('\0');
        foreach (CharacterCareerCalendarSourceSlice slice in slices)
        {
            payload.Append(slice.Name).Append('\0')
                .Append(slice.Path).Append('\0')
                .Append(slice.FirstLine.ToString(CultureInfo.InvariantCulture)).Append('\0')
                .Append(slice.LastLine.ToString(CultureInfo.InvariantCulture)).Append('\0')
                .Append(slice.Sha256).Append('\0');
        }
    }

    private static bool RevisionMatches(string actual, string? expected)
        => IsLowerHexRevision(actual)
            && string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool IsLowerHexRevision(string? value)
        => value is { Length: RevisionHexLength }
            && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static CharacterCareerCalendarWeekState UnavailableWeek()
        => new(
            new CharacterCareerCalendarWeekIdentity(Guid.Empty),
            0,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

    private static CharacterCareerCalendarState UnavailableCalendar()
        => new(false, [], string.Empty, string.Empty);

    private static CharacterCareerCalendarWeekDraft UnavailableDraft()
        => new(
            new CharacterCareerCalendarWeekIdentity(Guid.Empty),
            0,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

    private static CharacterCareerCalendarChangeStartPlan UnavailableChangeStartPlan()
        => new(
            new CharacterCareerCalendarStartSelection(0, 0, default, default),
            string.Empty,
            string.Empty,
            HasDurableMutation: false);
}
