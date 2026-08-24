using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerCalendarRulesTests
{
    private static readonly CharacterCareerCalendarWeekIdentity FirstIdentity =
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly CharacterCareerCalendarWeekIdentity SecondIdentity =
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    private static readonly CharacterCareerCalendarSourceAuthority Authority =
        CharacterCareerCalendarRules.PinnedSourceAuthority;

    [TestMethod]
    public void Pinned_authority_binds_exact_revision_full_sources_handlers_and_callees()
    {
        Assert.AreEqual(
            "fe4355d06c98cd9b7feade89f5fc1a0e438f7ce3",
            Authority.Revision);
        Assert.HasCount(4, Authority.SourceFiles);
        Assert.HasCount(4, Authority.Handlers);
        Assert.HasCount(10, Authority.Callees);
        CollectionAssert.AreEqual(
            new[]
            {
                "CharacterCareer.cmdAddWeek_Click",
                "CharacterCareer.cmdDeleteWeek_Click",
                "CharacterCareer.cmdEditWeek_Click",
                "CharacterCareer.cmdChangeStartWeek_Click"
            },
            Authority.Handlers.Select(static slice => slice.Name).ToArray());
        CollectionAssert.Contains(
            Authority.Callees.Select(static slice => slice.Name).ToArray(),
            "CalendarWeek.ModifyWeekAsync");
        CollectionAssert.Contains(
            Authority.Callees.Select(static slice => slice.Name).ToArray(),
            "IntegerExtensions.IsYearLongYear");
        Assert.IsTrue(CharacterCareerCalendarRules.IsExactSourceAuthority(Authority));
        AssertRevision(CharacterCareerCalendarRules.PinnedSourceAuthorityDigest);
        Assert.AreEqual(
            CharacterCareerCalendarRules.PinnedSourceAuthorityDigest,
            CharacterCareerCalendarRules.CalculateSourceAuthorityDigest(Authority));
    }

    [TestMethod]
    public void Every_entry_point_is_career_only()
    {
        string source = Source(FirstIdentity, 2081, 12);
        Assert.IsFalse(CharacterCareerCalendarRules.TryCreateState(
            false, Authority, source, out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryCreateCalendar(
            false, Authority, [source], out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TrySelectStart(
            false, Authority, 2081, 12, out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryGetWeeksInYear(
            false, Authority, 2081, out _));
    }

    [TestMethod]
    public void Prior_public_surface_remains_compatible_but_is_now_full_source_bound()
    {
        string source = Source(FirstIdentity, 2081, 12);
        Assert.IsTrue(CharacterCareerCalendarRules.TryCreateState(
            FirstIdentity,
            created: true,
            2081,
            12,
            string.Empty,
            "Chocolate",
            source,
            out CharacterCareerCalendarWeekState state));
        var (identity, year, week, notes, color, logicalRevision, sourceRevision) = state;
        Assert.AreEqual(FirstIdentity, identity);
        Assert.AreEqual(2081, year);
        Assert.AreEqual(12, week);
        Assert.AreEqual(string.Empty, notes);
        Assert.AreEqual("Chocolate", color);
        AssertRevision(logicalRevision);
        AssertRevision(sourceRevision);

        Assert.IsTrue(CharacterCareerCalendarRules.TryPlanAdd(
            [state], SecondIdentity, 2000, 1, out CharacterCareerCalendarWeekDraft add));
        Assert.AreEqual(2081, add.Year);
        Assert.AreEqual(13, add.Week);
        Assert.IsTrue(CharacterCareerCalendarRules.TryEdit(
            state, state.SourceRevision, "edited", "Red", out _));
        Assert.IsTrue(CharacterCareerCalendarRules.CanDelete(
            state, FirstIdentity, state.SourceRevision, confirmed: true));

        Assert.IsFalse(CharacterCareerCalendarRules.TryCreateState(
            FirstIdentity,
            created: true,
            2081,
            12,
            "does not match source",
            "Chocolate",
            source,
            out CharacterCareerCalendarWeekState rejected));
        Assert.AreEqual(Guid.Empty, rejected.Identity.WeekId);
    }

    [TestMethod]
    public void Full_source_element_load_binds_all_fields_and_exact_bytes()
    {
        string compact = Source(
            FirstIdentity,
            2081,
            12,
            "Meet <Mr. Johnson> & survive",
            "#A52A2A");
        Assert.IsTrue(CharacterCareerCalendarRules.TryCreateState(
            true, Authority, compact, out CharacterCareerCalendarWeekState state));

        Assert.AreEqual(FirstIdentity, state.Identity);
        Assert.AreEqual(2081, state.Year);
        Assert.AreEqual(12, state.Week);
        Assert.AreEqual("Meet <Mr. Johnson> & survive", state.Notes);
        Assert.AreEqual("#A52A2A", state.NotesColor);
        AssertRevision(state.SourceRevision);
        AssertRevision(state.LogicalRevision);
        Assert.IsTrue(CharacterCareerCalendarRules.IsCoherent(state));

        string formatted = compact.Replace("><", ">\n  <", StringComparison.Ordinal);
        Assert.IsTrue(CharacterCareerCalendarRules.TryCreateState(
            true, Authority, formatted, out CharacterCareerCalendarWeekState reformatted));
        Assert.AreEqual(state.Identity, reformatted.Identity);
        Assert.AreEqual(state.Year, reformatted.Year);
        Assert.AreNotEqual(state.SourceRevision, reformatted.SourceRevision);
        Assert.AreNotEqual(state.LogicalRevision, reformatted.LogicalRevision);
    }

    [TestMethod]
    public void Missing_duplicate_reordered_or_extra_source_elements_fail_closed()
    {
        string valid = Source(FirstIdentity, 2081, 12);
        string[] invalid =
        [
            string.Empty,
            "<week />",
            valid.Replace($"<guid>{FirstIdentity.WeekId:D}</guid>", string.Empty, StringComparison.Ordinal),
            valid.Replace("<year>2081</year>", string.Empty, StringComparison.Ordinal),
            valid.Replace("<week>12</week>", string.Empty, StringComparison.Ordinal),
            valid.Replace("<notes></notes>", string.Empty, StringComparison.Ordinal),
            valid.Replace("<notesColor>Chocolate</notesColor>", string.Empty, StringComparison.Ordinal),
            valid.Replace(
                $"<guid>{FirstIdentity.WeekId:D}</guid>",
                $"<guid>{FirstIdentity.WeekId:D}</guid><guid>{SecondIdentity.WeekId:D}</guid>",
                StringComparison.Ordinal),
            valid.Replace("<year>2081</year>", "<year>2081</year><year>2082</year>", StringComparison.Ordinal),
            valid.Replace("<week>12</week>", "<week>12</week><week>13</week>", StringComparison.Ordinal),
            valid.Replace("</week>", "<future>data</future></week>", StringComparison.Ordinal),
            valid.Replace("<year>2081</year><week>12</week>", "<week>12</week><year>2081</year>", StringComparison.Ordinal),
            valid.Replace("<guid>", "<guid source=\"forged\">", StringComparison.Ordinal),
            valid + "<!-- trailing -->",
            "<?xml version=\"1.0\"?>" + valid,
            valid + "<week />",
            "<week><guid>11111111-1111-1111-1111-111111111111</guid>text<year>2081</year><week>12</week><notes></notes><notesColor>Chocolate</notesColor></week>"
        ];

        foreach (string source in invalid)
        {
            Assert.IsFalse(
                CharacterCareerCalendarRules.TryCreateState(true, Authority, source, out _),
                source);
        }
    }

    [TestMethod]
    public void Empty_or_duplicate_identity_and_duplicate_iso_coordinate_fail_closed()
    {
        Assert.IsFalse(CharacterCareerCalendarRules.TryCreateState(
            true,
            Authority,
            Source(new CharacterCareerCalendarWeekIdentity(Guid.Empty), 2081, 1),
            out _));

        Assert.IsFalse(CharacterCareerCalendarRules.TryCreateCalendar(
            true,
            Authority,
            [Source(FirstIdentity, 2081, 1), Source(FirstIdentity, 2081, 2)],
            out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryCreateCalendar(
            true,
            Authority,
            [Source(FirstIdentity, 2081, 1), Source(SecondIdentity, 2081, 1)],
            out _));
    }

    [TestMethod]
    public void Year_and_week_bounds_match_select_start_and_long_year_authority()
    {
        Assert.IsFalse(CharacterCareerCalendarRules.IsSupportedIsoWeek(1999, 1));
        Assert.IsTrue(CharacterCareerCalendarRules.IsSupportedIsoWeek(2000, 1));
        Assert.IsTrue(CharacterCareerCalendarRules.IsSupportedIsoWeek(9000, 1));
        Assert.IsFalse(CharacterCareerCalendarRules.IsSupportedIsoWeek(9001, 1));
        Assert.IsFalse(CharacterCareerCalendarRules.IsSupportedIsoWeek(2081, 0));
        Assert.IsFalse(CharacterCareerCalendarRules.IsSupportedIsoWeek(2081, 54));
        Assert.IsFalse(CharacterCareerCalendarRules.IsSupportedIsoWeek(2025, 53));
        Assert.IsTrue(CharacterCareerCalendarRules.IsSupportedIsoWeek(2026, 53));

        Assert.IsTrue(CharacterCareerCalendarRules.TryGetWeeksInYear(
            true, Authority, 2000, out int boundaryWeeks));
        Assert.AreEqual(52, boundaryWeeks);
        Assert.IsTrue(CharacterCareerCalendarRules.TryGetWeeksInYear(
            true, Authority, 2025, out int shortYearWeeks));
        Assert.AreEqual(52, shortYearWeeks);
        Assert.IsTrue(CharacterCareerCalendarRules.TryGetWeeksInYear(
            true, Authority, 2026, out int longYearWeeks));
        Assert.AreEqual(53, longYearWeeks);
        Assert.IsTrue(CharacterCareerCalendarRules.TryGetWeeksInYear(
            true, Authority, 2100, out int centuryWeeks));
        Assert.AreEqual(52, centuryWeeks);
    }

    [TestMethod]
    public void Rollover_is_exact_across_short_long_and_maximum_year_boundaries()
    {
        Assert.IsTrue(CharacterCareerCalendarRules.TryNextWeek(2025, 52, out int year, out int week));
        Assert.AreEqual(2026, year);
        Assert.AreEqual(1, week);

        Assert.IsTrue(CharacterCareerCalendarRules.TryNextWeek(2026, 52, out year, out week));
        Assert.AreEqual(2026, year);
        Assert.AreEqual(53, week);
        Assert.IsTrue(CharacterCareerCalendarRules.TryNextWeek(2026, 53, out year, out week));
        Assert.AreEqual(2027, year);
        Assert.AreEqual(1, week);

        Assert.IsTrue(CharacterCareerCalendarRules.TryGetWeeksInYear(
            true, Authority, CharacterCareerCalendarRules.MaximumYear, out int maximumWeek));
        Assert.IsFalse(CharacterCareerCalendarRules.TryNextWeek(
            CharacterCareerCalendarRules.MaximumYear,
            maximumWeek,
            out _,
            out _));
    }

    [TestMethod]
    public void Select_start_accepts_valid_coordinate_and_returns_exact_monday_to_sunday_span()
    {
        Assert.IsTrue(CharacterCareerCalendarRules.TrySelectStart(
            true,
            Authority,
            2025,
            1,
            out CharacterCareerCalendarStartSelection first));
        Assert.AreEqual(new DateOnly(2024, 12, 30), first.StartDate);
        Assert.AreEqual(new DateOnly(2025, 1, 5), first.EndDate);

        Assert.IsTrue(CharacterCareerCalendarRules.TrySelectStart(
            true,
            Authority,
            2026,
            53,
            out CharacterCareerCalendarStartSelection longYear));
        Assert.AreEqual(new DateOnly(2026, 12, 28), longYear.StartDate);
        Assert.AreEqual(new DateOnly(2027, 1, 3), longYear.EndDate);

        Assert.IsFalse(CharacterCareerCalendarRules.TrySelectStart(
            true, Authority, 2025, 53, out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TrySelectStart(
            true, Authority, 2081, 0, out _));
    }

    [TestMethod]
    public void Colors_are_canonical_hex_or_known_ColorTranslator_names_only()
    {
        AssertColor("#a52a2a", "#A52A2A");
        AssertColor("Chocolate", "Chocolate");
        AssertColor("chocolate", "Chocolate");
        AssertColor("LightGrey", "LightGrey");
        AssertColor("Transparent", "Transparent");
        AssertColor("Control", "buttonface");

        foreach (string invalid in new[]
                 {
                     "", " NotAColor", "NotAColor", "Blorp", "#FFF", "#12345678",
                     "#GG0000", "red!", "Light Grey", "123"
                 })
        {
            Assert.IsFalse(
                CharacterCareerCalendarRules.TryNormalizeNotesColor(invalid, out _),
                invalid);
        }

        Assert.IsFalse(CharacterCareerCalendarRules.TryCreateState(
            true, Authority, Source(FirstIdentity, 2081, 1, color: "#a52a2a"), out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryCreateState(
            true, Authority, Source(FirstIdentity, 2081, 1, color: "NotAColor"), out _));
    }

    [TestMethod]
    public void Add_binds_calendar_revision_unique_identity_and_canonical_replacement_element()
    {
        CharacterCareerCalendarState empty = Calendar();
        Assert.IsTrue(CharacterCareerCalendarRules.TryPlanAdd(
            empty,
            Authority,
            empty.Revision,
            FirstIdentity,
            2026,
            53,
            out CharacterCareerCalendarWeekDraft first));
        Assert.AreEqual(2026, first.Year);
        Assert.AreEqual(53, first.Week);
        Assert.AreEqual(CharacterCareerCalendarRules.DefaultNotesColor, first.NotesColor);
        Assert.AreEqual(string.Empty, first.ExpectedSourceRevision);
        AssertRevision(first.SourceRevision);
        Assert.IsTrue(CharacterCareerCalendarRules.TryCreateState(
            true, Authority, first.SourceElement, out CharacterCareerCalendarWeekState loaded));
        Assert.AreEqual(first.SourceRevision, loaded.SourceRevision);

        CharacterCareerCalendarState existing = Calendar(Source(FirstIdentity, 2026, 53));
        Assert.IsTrue(CharacterCareerCalendarRules.TryPlanAdd(
            existing,
            Authority,
            existing.Revision,
            SecondIdentity,
            2000,
            1,
            out CharacterCareerCalendarWeekDraft rollover));
        Assert.AreEqual(2027, rollover.Year);
        Assert.AreEqual(1, rollover.Week);

        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanAdd(
            existing, Authority, existing.Revision, FirstIdentity, 2000, 1, out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanAdd(
            existing, Authority, new string('0', 64), SecondIdentity, 2000, 1, out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanAdd(
            existing, DriftHandler(0), existing.Revision, SecondIdentity, 2000, 1, out _));
    }

    [TestMethod]
    public void First_add_rejects_year_and_week_boundaries_and_terminal_calendar_cannot_roll()
    {
        CharacterCareerCalendarState empty = Calendar();
        foreach ((int year, int week) in new[]
                 {
                     (1999, 1), (9001, 1), (2081, 0), (2081, 54), (2025, 53)
                 })
        {
            Assert.IsFalse(CharacterCareerCalendarRules.TryPlanAdd(
                empty, Authority, empty.Revision, FirstIdentity, year, week, out _));
        }

        Assert.IsTrue(CharacterCareerCalendarRules.TryGetWeeksInYear(
            true, Authority, CharacterCareerCalendarRules.MaximumYear, out int maximumWeek));
        CharacterCareerCalendarState terminal = Calendar(Source(
            FirstIdentity,
            CharacterCareerCalendarRules.MaximumYear,
            maximumWeek));
        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanAdd(
            terminal, Authority, terminal.Revision, SecondIdentity, 2000, 1, out _));
    }

    [TestMethod]
    public void Edit_preserves_identity_coordinate_and_binds_every_revision()
    {
        CharacterCareerCalendarState current = Calendar(Source(
            FirstIdentity,
            2081,
            12,
            "Before",
            "Chocolate"));
        CharacterCareerCalendarWeekState selected = current.Weeks[0];
        Assert.IsTrue(CharacterCareerCalendarRules.TryPlanEdit(
            current,
            Authority,
            current.Revision,
            selected.Identity,
            selected.LogicalRevision,
            selected.SourceRevision,
            "Downtime <complete>",
            "#a52a2a",
            out CharacterCareerCalendarWeekDraft edit));

        Assert.AreEqual(selected.Identity, edit.Identity);
        Assert.AreEqual(selected.Year, edit.Year);
        Assert.AreEqual(selected.Week, edit.Week);
        Assert.AreEqual("Downtime <complete>", edit.Notes);
        Assert.AreEqual("#A52A2A", edit.NotesColor);
        Assert.AreEqual(selected.LogicalRevision, edit.ExpectedLogicalRevision);
        Assert.AreEqual(selected.SourceRevision, edit.ExpectedSourceRevision);
        Assert.AreNotEqual(selected.SourceRevision, edit.SourceRevision);
        Assert.IsTrue(CharacterCareerCalendarRules.TryCreateState(
            true, Authority, edit.SourceElement, out CharacterCareerCalendarWeekState replacement));
        Assert.AreEqual(edit.SourceRevision, replacement.SourceRevision);

        AssertEditRejected(current, selected, new string('0', 64), selected.LogicalRevision, selected.SourceRevision);
        AssertEditRejected(current, selected, current.Revision, new string('0', 64), selected.SourceRevision);
        AssertEditRejected(current, selected, current.Revision, selected.LogicalRevision, new string('0', 64));
        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanEdit(
            current,
            Authority,
            current.Revision,
            selected.Identity,
            selected.LogicalRevision,
            selected.SourceRevision,
            "forged",
            "NotAColor",
            out _));
    }

    [TestMethod]
    public void Delete_requires_confirmation_identity_collection_logical_and_full_source_revisions()
    {
        CharacterCareerCalendarState current = Calendar(Source(FirstIdentity, 2081, 12));
        CharacterCareerCalendarWeekState selected = current.Weeks[0];
        Assert.IsTrue(CharacterCareerCalendarRules.CanDelete(
            current,
            Authority,
            current.Revision,
            selected.Identity,
            selected.LogicalRevision,
            selected.SourceRevision,
            confirmed: true));
        Assert.IsFalse(CharacterCareerCalendarRules.CanDelete(
            current,
            Authority,
            current.Revision,
            selected.Identity,
            selected.LogicalRevision,
            selected.SourceRevision,
            confirmed: false));
        Assert.IsFalse(CharacterCareerCalendarRules.CanDelete(
            current,
            Authority,
            current.Revision,
            SecondIdentity,
            selected.LogicalRevision,
            selected.SourceRevision,
            confirmed: true));
        Assert.IsFalse(CharacterCareerCalendarRules.CanDelete(
            current,
            Authority,
            current.Revision,
            selected.Identity,
            selected.LogicalRevision,
            new string('0', 64),
            confirmed: true));
    }

    [TestMethod]
    public void Change_start_is_source_guarded_and_deliberately_non_mutating()
    {
        CharacterCareerCalendarState current = Calendar(
            Source(FirstIdentity, 2081, 12),
            Source(SecondIdentity, 2081, 11));
        Assert.IsTrue(CharacterCareerCalendarRules.TryPlanChangeStart(
            current,
            Authority,
            current.Revision,
            2082,
            3,
            out CharacterCareerCalendarChangeStartPlan plan));
        Assert.IsFalse(plan.HasDurableMutation);
        Assert.AreEqual(current.Revision, plan.ExpectedCalendarRevision);
        Assert.AreEqual(current.Revision, plan.ResultCalendarRevision);
        Assert.AreEqual(2082, plan.RequestedStart.Year);
        Assert.AreEqual(3, plan.RequestedStart.Week);

        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanChangeStart(
            current, DriftHandler(3), current.Revision, 2082, 3, out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanChangeStart(
            current, DriftCallee("CalendarWeek.ModifyWeekAsync"), current.Revision, 2082, 3, out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanChangeStart(
            current, DriftCallee("CalendarWeek.SetWeekAsync"), current.Revision, 2082, 3, out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanChangeStart(
            current, Authority, new string('0', 64), 2082, 3, out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanChangeStart(
            Calendar(), Authority, Calendar().Revision, 2082, 3, out _));
    }

    [TestMethod]
    public void Every_source_file_handler_and_callee_drift_fails_closed()
    {
        string source = Source(FirstIdentity, 2081, 12);
        for (int index = 0; index < Authority.SourceFiles.Count; index++)
        {
            AssertAuthorityRejected(DriftSource(index), source);
        }
        for (int index = 0; index < Authority.Handlers.Count; index++)
        {
            AssertAuthorityRejected(DriftHandler(index), source);
        }
        foreach (CharacterCareerCalendarSourceSlice callee in Authority.Callees)
        {
            AssertAuthorityRejected(DriftCallee(callee.Name), source);
        }

        AssertAuthorityRejected(
            Authority with { Revision = new string('0', 40) },
            source);
    }

    [TestMethod]
    public void Forged_identity_coordinate_notes_color_or_revisions_make_collection_incoherent()
    {
        CharacterCareerCalendarState current = Calendar(Source(FirstIdentity, 2081, 12, "Original"));
        CharacterCareerCalendarWeekState week = current.Weeks[0];
        CharacterCareerCalendarWeekState[] forged =
        [
            week with { Identity = new CharacterCareerCalendarWeekIdentity(Guid.Empty) },
            week with { Year = 1999 },
            week with { Week = 0 },
            week with { Notes = "forged" },
            week with { NotesColor = "NotAColor" },
            week with { SourceRevision = new string('0', 64) },
            week with { LogicalRevision = new string('0', 64) },
            week with { SourceAuthorityDigest = new string('0', 64) }
        ];
        foreach (CharacterCareerCalendarWeekState altered in forged)
        {
            Assert.IsFalse(CharacterCareerCalendarRules.IsCoherent(
                current with { Weeks = new[] { altered } }));
        }
        Assert.IsFalse(CharacterCareerCalendarRules.IsCoherent(
            current with { IsCareer = false }));
        Assert.IsFalse(CharacterCareerCalendarRules.IsCoherent(
            current with { Revision = new string('0', 64) }));
    }

    private static CharacterCareerCalendarState Calendar(params string[] sources)
    {
        Assert.IsTrue(CharacterCareerCalendarRules.TryCreateCalendar(
            true, Authority, sources, out CharacterCareerCalendarState calendar));
        Assert.IsTrue(CharacterCareerCalendarRules.IsCoherent(calendar));
        return calendar;
    }

    private static string Source(
        CharacterCareerCalendarWeekIdentity identity,
        int year,
        int week,
        string notes = "",
        string color = CharacterCareerCalendarRules.DefaultNotesColor)
        => new XElement(
            "week",
            new XElement("guid", identity.WeekId.ToString("D")),
            new XElement("year", year),
            new XElement("week", week),
            new XElement("notes", notes),
            new XElement("notesColor", color))
            .ToString(SaveOptions.DisableFormatting);

    private static CharacterCareerCalendarSourceAuthority DriftSource(int index)
    {
        CharacterCareerCalendarSourceFile[] files = Authority.SourceFiles.ToArray();
        files[index] = files[index] with { Sha256 = new string('0', 64) };
        return Authority with { SourceFiles = files };
    }

    private static CharacterCareerCalendarSourceAuthority DriftHandler(int index)
    {
        CharacterCareerCalendarSourceSlice[] handlers = Authority.Handlers.ToArray();
        handlers[index] = handlers[index] with { Sha256 = new string('0', 64) };
        return Authority with { Handlers = handlers };
    }

    private static CharacterCareerCalendarSourceAuthority DriftCallee(string name)
    {
        CharacterCareerCalendarSourceSlice[] callees = Authority.Callees.ToArray();
        int index = Array.FindIndex(callees, candidate => candidate.Name == name);
        Assert.IsGreaterThanOrEqualTo(0, index);
        callees[index] = callees[index] with { Sha256 = new string('0', 64) };
        return Authority with { Callees = callees };
    }

    private static void AssertAuthorityRejected(
        CharacterCareerCalendarSourceAuthority drifted,
        string source)
    {
        Assert.IsFalse(CharacterCareerCalendarRules.IsExactSourceAuthority(drifted));
        Assert.AreNotEqual(
            CharacterCareerCalendarRules.PinnedSourceAuthorityDigest,
            CharacterCareerCalendarRules.CalculateSourceAuthorityDigest(drifted));
        Assert.IsFalse(CharacterCareerCalendarRules.TryCreateState(
            true, drifted, source, out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryCreateCalendar(
            true, drifted, [source], out _));
    }

    private static void AssertColor(string input, string expected)
    {
        Assert.IsTrue(CharacterCareerCalendarRules.TryNormalizeNotesColor(
            input, out string normalized));
        Assert.AreEqual(expected, normalized);
    }

    private static void AssertEditRejected(
        CharacterCareerCalendarState current,
        CharacterCareerCalendarWeekState selected,
        string expectedCalendarRevision,
        string expectedLogicalRevision,
        string expectedSourceRevision)
        => Assert.IsFalse(CharacterCareerCalendarRules.TryPlanEdit(
            current,
            Authority,
            expectedCalendarRevision,
            selected.Identity,
            expectedLogicalRevision,
            expectedSourceRevision,
            "forged",
            "Chocolate",
            out _));

    private static void AssertRevision(string value)
    {
        Assert.HasCount(64, value);
        Assert.IsTrue(value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f'));
    }
}
