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
    private static readonly CharacterCareerCalendarWeekIdentity ThirdIdentity =
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"));
    private static readonly CharacterCareerCalendarSourceAuthority Authority =
        CharacterCareerCalendarRules.PinnedSourceAuthority;

    [TestMethod]
    public void Pinned_authority_binds_exact_revision_full_sources_handlers_and_callees()
    {
        Assert.AreEqual(
            "fe4355d06c98cd9b7feade89f5fc1a0e438f7ce3",
            Authority.Revision);
        Assert.HasCount(6, Authority.SourceFiles);
        Assert.HasCount(4, Authority.Handlers);
        Assert.HasCount(14, Authority.Callees);
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
        CollectionAssert.Contains(
            Authority.SourceFiles.Select(static source => source.Path).ToArray(),
            "Chummer/Forms/Selection Forms/SelectCalendarStart.Designer.cs");
        CollectionAssert.Contains(
            Authority.SourceFiles.Select(static source => source.Path).ToArray(),
            "Chummer/Backend/Static/Extensions/StringExtensions.cs");
        CollectionAssert.Contains(
            Authority.Callees.Select(static slice => slice.Name).ToArray(),
            "SelectCalendarStart.Designer.yearAndWeekBounds");
        CollectionAssert.Contains(
            Authority.Callees.Select(static slice => slice.Name).ToArray(),
            "StringExtensions.CleanOfXmlInvalidUnicodeChars");
        CollectionAssert.Contains(
            Authority.Callees.Select(static slice => slice.Name).ToArray(),
            "StringExtensions.XmlInvalidUnicodeCharacterSet");
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
    public void Prior_loader_surface_remains_source_compatible_and_full_source_bound()
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
    public void Every_legacy_ungated_mutator_and_planner_signature_fails_closed()
    {
        CharacterCareerCalendarState calendar = Calendar(Source(FirstIdentity, 2081, 12));
        CharacterCareerCalendarWeekState state = calendar.Weeks[0];

        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanAdd(
            [state], SecondIdentity, 2000, 1, out CharacterCareerCalendarWeekDraft add));
        Assert.AreEqual(Guid.Empty, add.Identity.WeekId);
        Assert.AreEqual(string.Empty, add.SourceElement);

        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanAdd(
            null!, SecondIdentity, 2000, 1, out add));
        Assert.AreEqual(Guid.Empty, add.Identity.WeekId);

        Assert.IsFalse(CharacterCareerCalendarRules.TryEdit(
            state, state.SourceRevision, "edited", "Red", out CharacterCareerCalendarWeekDraft edit));
        Assert.AreEqual(Guid.Empty, edit.Identity.WeekId);
        Assert.AreEqual(string.Empty, edit.SourceElement);
        Assert.IsFalse(CharacterCareerCalendarRules.TryEdit(
            null, state.SourceRevision, "edited", "Red", out edit));
        Assert.AreEqual(Guid.Empty, edit.Identity.WeekId);

        Assert.IsFalse(CharacterCareerCalendarRules.CanDelete(
            state, FirstIdentity, state.SourceRevision, confirmed: true));
        Assert.IsFalse(CharacterCareerCalendarRules.CanDelete(
            state, FirstIdentity, state.SourceRevision, confirmed: false));
        Assert.IsFalse(CharacterCareerCalendarRules.CanDelete(
            null, FirstIdentity, state.SourceRevision, confirmed: true));
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
    public void Empty_or_duplicate_identity_and_duplicate_calendar_coordinate_fail_closed()
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
        Assert.IsFalse(CharacterCareerCalendarRules.IsSupportedIsoWeek(2025, 53));
        Assert.IsTrue(CharacterCareerCalendarRules.IsSupportedIsoWeek(2026, 53));
        Assert.IsFalse(CharacterCareerCalendarRules.IsSupportedCalendarCoordinate(1999, 1));
        Assert.IsTrue(CharacterCareerCalendarRules.IsSupportedCalendarCoordinate(2000, 1));
        Assert.IsTrue(CharacterCareerCalendarRules.IsSupportedCalendarCoordinate(9000, 1));
        Assert.IsFalse(CharacterCareerCalendarRules.IsSupportedCalendarCoordinate(9001, 1));
        Assert.IsFalse(CharacterCareerCalendarRules.IsSupportedCalendarCoordinate(2081, 0));
        Assert.IsFalse(CharacterCareerCalendarRules.IsSupportedCalendarCoordinate(2081, 54));
        Assert.IsFalse(CharacterCareerCalendarRules.IsSupportedCalendarCoordinate(2020, 53));
        Assert.IsFalse(CharacterCareerCalendarRules.IsSupportedCalendarCoordinate(2025, 53));
        Assert.IsTrue(CharacterCareerCalendarRules.IsSupportedCalendarCoordinate(2026, 53));

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
    public void Every_supported_year_matches_the_pinned_Chummer5_long_year_algorithm()
    {
        for (int year = CharacterCareerCalendarRules.MinimumYear;
             year <= CharacterCareerCalendarRules.MaximumYear;
             year++)
        {
            int expectedWeeks = PinnedChummer5LongYear(year) ? 53 : 52;
            Assert.IsTrue(CharacterCareerCalendarRules.TryGetWeeksInYear(
                true,
                Authority,
                year,
                out int actualWeeks));
            Assert.AreEqual(expectedWeeks, actualWeeks, $"Chummer5 year {year}");
            Assert.AreEqual(
                expectedWeeks == 53,
                CharacterCareerCalendarRules.IsSupportedCalendarCoordinate(year, 53),
                $"Chummer5 year {year}, week 53");
        }
    }

    [TestMethod]
    public void Rollover_is_exact_across_short_long_and_maximum_year_boundaries()
    {
        Assert.IsTrue(CharacterCareerCalendarRules.TryNextWeek(2020, 52, out int year, out int week));
        Assert.AreEqual(2021, year);
        Assert.AreEqual(1, week);
        Assert.IsFalse(CharacterCareerCalendarRules.TryNextWeek(2020, 53, out _, out _));

        Assert.IsTrue(CharacterCareerCalendarRules.TryNextWeek(2025, 52, out year, out week));
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
        Assert.IsEmpty(first.ExpectedSourceRevision);
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
        Assert.AreEqual(edit.ExpectedLogicalRevision, selected.LogicalRevision);
        Assert.AreEqual(edit.ExpectedSourceRevision, selected.SourceRevision);
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
    public void Add_and_edit_reentry_rebuilds_authority_and_rejects_every_old_revision()
    {
        CharacterCareerCalendarState empty = Calendar();
        Assert.IsTrue(CharacterCareerCalendarRules.TryPlanAdd(
            empty,
            Authority,
            empty.Revision,
            FirstIdentity,
            2081,
            12,
            out CharacterCareerCalendarWeekDraft add));
        CharacterCareerCalendarState added = Calendar(add.SourceElement);
        CharacterCareerCalendarWeekState addedWeek = added.Weeks.Single();
        Assert.AreNotEqual(empty.Revision, added.Revision);
        Assert.AreEqual(add.SourceRevision, addedWeek.SourceRevision);

        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanAdd(
            added,
            Authority,
            empty.Revision,
            SecondIdentity,
            2000,
            1,
            out _),
            "The calendar revision captured before Add must not authorize reentry.");
        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanAdd(
            added,
            Authority,
            added.Revision,
            FirstIdentity,
            2000,
            1,
            out _),
            "Replaying the already-added identity must fail even with the rebuilt revision.");

        Assert.IsTrue(CharacterCareerCalendarRules.TryPlanEdit(
            added,
            Authority,
            added.Revision,
            addedWeek.Identity,
            addedWeek.LogicalRevision,
            addedWeek.SourceRevision,
            "After the run",
            "Chocolate",
            out CharacterCareerCalendarWeekDraft edit));
        CharacterCareerCalendarState edited = Calendar(edit.SourceElement);
        CharacterCareerCalendarWeekState editedWeek = edited.Weeks.Single();
        Assert.AreNotEqual(added.Revision, edited.Revision);
        Assert.AreNotEqual(addedWeek.LogicalRevision, editedWeek.LogicalRevision);
        Assert.AreNotEqual(addedWeek.SourceRevision, editedWeek.SourceRevision);
        Assert.AreEqual(edit.SourceRevision, editedWeek.SourceRevision);

        AssertEditRejected(
            edited,
            editedWeek,
            added.Revision,
            editedWeek.LogicalRevision,
            editedWeek.SourceRevision);
        AssertEditRejected(
            edited,
            editedWeek,
            edited.Revision,
            addedWeek.LogicalRevision,
            editedWeek.SourceRevision);
        AssertEditRejected(
            edited,
            editedWeek,
            edited.Revision,
            editedWeek.LogicalRevision,
            addedWeek.SourceRevision);
        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanEdit(
            edited,
            Authority,
            added.Revision,
            addedWeek.Identity,
            addedWeek.LogicalRevision,
            addedWeek.SourceRevision,
            edit.Notes,
            edit.NotesColor,
            out _),
            "Replaying the edit draft with its captured pre-edit authorities must fail.");

        Assert.IsTrue(CharacterCareerCalendarRules.TryPlanAdd(
            edited,
            Authority,
            edited.Revision,
            ThirdIdentity,
            2000,
            1,
            out _),
            "The rebuilt calendar remains usable with its current authority.");
    }

    [TestMethod]
    public void Edit_cleans_exact_Chummer5_invalid_character_set_before_source_persistence()
    {
        CharacterCareerCalendarState current = Calendar(Source(FirstIdentity, 2081, 12));
        CharacterCareerCalendarWeekState selected = current.Weeks[0];
        string invalid = new(
        [
            '\u0000', '\u0001', '\u0002', '\u0003', '\u0004', '\u0005', '\u0006', '\u0007',
            '\u0008', '\u000B', '\u000C', '\u000E', '\u000F', '\u0010', '\u0011', '\u0012',
            '\u0013', '\u0014', '\u0015', '\u0016', '\u0017', '\u0018', '\u0019', '\u001A',
            '\u001B', '\u001C', '\u001D', '\u001E', '\u001F'
        ]);
        string notes = "before" + invalid + "\t\n\r🚀after";

        Assert.IsTrue(CharacterCareerCalendarRules.TryPlanEdit(
            current,
            Authority,
            current.Revision,
            selected.Identity,
            selected.LogicalRevision,
            selected.SourceRevision,
            notes,
            "Chocolate",
            out CharacterCareerCalendarWeekDraft draft));

        Assert.AreEqual("before\t\n\r🚀after", draft.Notes);
        Assert.IsFalse(draft.Notes.Any(invalid.Contains));
        Assert.IsTrue(CharacterCareerCalendarRules.TryCreateState(
            true,
            Authority,
            draft.SourceElement,
            out CharacterCareerCalendarWeekState persisted));
        Assert.AreEqual(draft.Notes, persisted.Notes);
        Assert.AreEqual(draft.SourceRevision, persisted.SourceRevision);
    }

    [TestMethod]
    public void Edit_rejects_unpaired_surrogates_and_unsanitized_noncharacters_without_a_draft()
    {
        CharacterCareerCalendarState current = Calendar(Source(FirstIdentity, 2081, 12));
        CharacterCareerCalendarWeekState selected = current.Weeks[0];
        string[] invalid = ["\uD800", "\uDC00", "before\uD800after", "\uFFFE", "\uFFFF"];

        foreach (string notes in invalid)
        {
            Assert.IsFalse(CharacterCareerCalendarRules.TryPlanEdit(
                current,
                Authority,
                current.Revision,
                selected.Identity,
                selected.LogicalRevision,
                selected.SourceRevision,
                notes,
                "Chocolate",
                out CharacterCareerCalendarWeekDraft draft),
                Convert.ToHexString(System.Text.Encoding.Unicode.GetBytes(notes)));
            Assert.AreEqual(Guid.Empty, draft.Identity.WeekId);
            Assert.AreEqual(string.Empty, draft.SourceElement);
        }
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
        Assert.AreEqual(plan.ExpectedCalendarRevision, current.Revision);
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

    private static bool PinnedChummer5LongYear(int year)
    {
        int yearDiv4 = Math.DivRem(year, 4, out int yearMod4);
        int yearDiv100 = Math.DivRem(year, 100, out int yearMod100);
        int yearDiv400 = Math.DivRem(year, 400, out int yearMod400);
        bool leapYear = yearMod4 == 0 && (yearMod100 != 0 || yearMod400 == 0);
        int dayOfWeekOfDecember31 = (year + yearDiv4 - yearDiv100 + yearDiv400) % 7;
        return dayOfWeekOfDecember31 == (leapYear ? 5 : 4);
    }
}
