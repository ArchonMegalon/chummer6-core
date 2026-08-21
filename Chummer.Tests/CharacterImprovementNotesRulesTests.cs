using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterImprovementNotesRulesTests
{
    private static readonly CharacterImprovementIdentity Identity = new(
        "a1111111-1111-1111-1111-111111111111",
        "Attribute",
        "Quality",
        "BOD",
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);

    [TestMethod]
    public void Career_direct_improvement_projects_notes_and_color_with_revision()
    {
        Assert.IsTrue(CharacterImprovementNotesRules.TryCreateState(
            Identity, true, "Body bonus", "old note", "#112233",
            out CharacterImprovementNotesState state));
        Assert.AreEqual(CharacterImprovementNotesRules.RevisionHexLength, state.Revision.Length);
        Assert.AreEqual("old note", state.Notes);
        Assert.AreEqual("#112233", state.NotesColor);
    }

    [TestMethod]
    public void Creation_invalid_color_stale_revision_and_noop_fail_closed()
    {
        Assert.IsFalse(CharacterImprovementNotesRules.TryCreateState(
            Identity, false, "Body bonus", "old", "#112233", out _));
        Assert.IsFalse(CharacterImprovementNotesRules.TryCreateState(
            Identity, true, "Body bonus", "old", "not a color", out _));
        Assert.IsTrue(CharacterImprovementNotesRules.TryCreateState(
            Identity, true, "Body bonus", "old", "WindowText",
            out CharacterImprovementNotesState state));
        Assert.IsFalse(CharacterImprovementNotesRules.TryValidateMutation(
            state, new string('0', 64), "new", "#445566"));
        Assert.IsFalse(CharacterImprovementNotesRules.TryValidateMutation(
            state, state.Revision, "old", "WindowText"));
        Assert.IsFalse(CharacterImprovementNotesRules.TryValidateMutation(
            state, state.Revision, "new", "InventedColor"));
        Assert.IsTrue(CharacterImprovementNotesRules.TryValidateMutation(
            state, state.Revision, "new", "#445566"));
    }

    [TestMethod]
    public void Revision_binds_identity_notes_and_notes_color()
    {
        Assert.IsTrue(CharacterImprovementNotesRules.TryCreateState(
            Identity, true, "Body bonus", "old", "#112233",
            out CharacterImprovementNotesState original));
        Assert.IsTrue(CharacterImprovementNotesRules.TryCreateState(
            Identity with { ImprovedName = "AGI" }, true, "Agility bonus", "old", "#112233",
            out CharacterImprovementNotesState otherIdentity));
        Assert.IsTrue(CharacterImprovementNotesRules.TryCreateState(
            Identity, true, "Body bonus", "new", "#112233",
            out CharacterImprovementNotesState otherNotes));
        Assert.IsTrue(CharacterImprovementNotesRules.TryCreateState(
            Identity, true, "Body bonus", "old", "#445566",
            out CharacterImprovementNotesState otherColor));

        Assert.AreNotEqual(original.Revision, otherIdentity.Revision);
        Assert.AreNotEqual(original.Revision, otherNotes.Revision);
        Assert.AreNotEqual(original.Revision, otherColor.Revision);
    }
}
