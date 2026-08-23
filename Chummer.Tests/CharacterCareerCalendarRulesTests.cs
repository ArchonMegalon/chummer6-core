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

    [TestMethod]
    public void Career_week_state_is_stable_identity_iso_week_and_source_revision_bound()
    {
        Assert.IsTrue(CharacterCareerCalendarRules.TryCreateState(
            FirstIdentity,
            created: true,
            2081,
            12,
            "Meet Mr. Johnson",
            "#A52A2A",
            "<week><guid>11111111-1111-1111-1111-111111111111</guid></week>",
            out CharacterCareerCalendarWeekState state));

        Assert.IsTrue(CharacterCareerCalendarRules.IsCoherent(state));
        Assert.AreEqual(64, state.LogicalRevision.Length);
        Assert.AreEqual(64, state.SourceRevision.Length);
        Assert.AreEqual("Meet Mr. Johnson", state.Notes);
        Assert.AreEqual("#A52A2A", state.NotesColor);
    }

    [TestMethod]
    public void First_add_matches_select_calendar_start_bounds_and_long_year_week_count()
    {
        Assert.IsTrue(CharacterCareerCalendarRules.TryPlanAdd(
            [], FirstIdentity, 2026, 53, out CharacterCareerCalendarWeekDraft longYear));
        Assert.AreEqual(2026, longYear.Year);
        Assert.AreEqual(53, longYear.Week);
        Assert.AreEqual(CharacterCareerCalendarRules.DefaultNotesColor, longYear.NotesColor);

        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanAdd(
            [], FirstIdentity, 2025, 53, out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanAdd(
            [], FirstIdentity, 1999, 1, out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanAdd(
            [], FirstIdentity, 9001, 1, out _));
    }

    [TestMethod]
    public void Subsequent_add_uses_latest_saved_week_and_rolls_iso_year()
    {
        CharacterCareerCalendarWeekState earlier = State(FirstIdentity, 2081, 52);
        Assert.IsTrue(CharacterCareerCalendarRules.TryPlanAdd(
            [earlier], SecondIdentity, 2000, 1, out CharacterCareerCalendarWeekDraft next));
        Assert.AreEqual(2082, next.Year);
        Assert.AreEqual(1, next.Week);
    }

    [TestMethod]
    public void Edit_preserves_identity_coordinate_and_requires_both_revisions_to_be_coherent()
    {
        CharacterCareerCalendarWeekState current = State(FirstIdentity, 2081, 12);
        Assert.IsTrue(CharacterCareerCalendarRules.TryEdit(
            current,
            current.SourceRevision,
            "Downtime complete",
            "Chocolate",
            out CharacterCareerCalendarWeekDraft edited));
        Assert.AreEqual(current.Identity, edited.Identity);
        Assert.AreEqual(current.Year, edited.Year);
        Assert.AreEqual(current.Week, edited.Week);
        Assert.AreEqual("Downtime complete", edited.Notes);

        Assert.IsFalse(CharacterCareerCalendarRules.TryEdit(
            current with { LogicalRevision = new string('0', 64) },
            current.SourceRevision,
            "forged",
            "Chocolate",
            out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryEdit(
            current,
            new string('0', 64),
            "stale",
            "Chocolate",
            out _));
    }

    [TestMethod]
    public void Delete_requires_confirmation_stable_identity_and_exact_source_revision()
    {
        CharacterCareerCalendarWeekState current = State(FirstIdentity, 2081, 12);
        Assert.IsTrue(CharacterCareerCalendarRules.CanDelete(
            current, FirstIdentity, current.SourceRevision, confirmed: true));
        Assert.IsFalse(CharacterCareerCalendarRules.CanDelete(
            current, FirstIdentity, current.SourceRevision, confirmed: false));
        Assert.IsFalse(CharacterCareerCalendarRules.CanDelete(
            current, SecondIdentity, current.SourceRevision, confirmed: true));
        Assert.IsFalse(CharacterCareerCalendarRules.CanDelete(
            current, FirstIdentity, new string('0', 64), confirmed: true));
    }

    [TestMethod]
    public void Creation_malformed_guid_color_and_duplicate_authority_fail_closed()
    {
        Assert.IsFalse(CharacterCareerCalendarRules.TryCreateState(
            FirstIdentity, false, 2081, 1, string.Empty, "Chocolate", "<week />", out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryCreateState(
            new CharacterCareerCalendarWeekIdentity(Guid.Empty), true, 2081, 1,
            string.Empty, "Chocolate", "<week />", out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryCreateState(
            FirstIdentity, true, 2081, 53, string.Empty, "Chocolate", "<week />", out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryCreateState(
            FirstIdentity, true, 2081, 1, string.Empty, "not a color", "<week />", out _));

        CharacterCareerCalendarWeekState current = State(FirstIdentity, 2081, 1);
        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanAdd(
            [current], FirstIdentity, 2081, 2, out _));
        Assert.IsFalse(CharacterCareerCalendarRules.TryPlanAdd(
            [current, current], SecondIdentity, 2081, 2, out _));
    }

    private static CharacterCareerCalendarWeekState State(
        CharacterCareerCalendarWeekIdentity identity,
        int year,
        int week)
    {
        Assert.IsTrue(CharacterCareerCalendarRules.TryCreateState(
            identity,
            created: true,
            year,
            week,
            string.Empty,
            CharacterCareerCalendarRules.DefaultNotesColor,
            $"<week><guid>{identity.WeekId:D}</guid><year>{year}</year><week>{week}</week></week>",
            out CharacterCareerCalendarWeekState state));
        return state;
    }
}
