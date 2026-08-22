using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterMartialArtNotesRulesTests
{
    private static readonly Guid ArtId = Guid.Parse("91111111-9111-9111-9111-911111111111");
    private static readonly Guid TechniqueId = Guid.Parse("92222222-9222-9222-9222-922222222222");

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Creation_and_career_use_identical_zero_cost_rules(bool created)
    {
        var identity = new CharacterMartialArtNotesIdentity(ArtId, TechniqueId);
        Assert.IsTrue(CharacterMartialArtNotesRules.TryCreateState(
            identity, created, "Aikido", "Disarm", "old", "Chocolate", out CharacterMartialArtNotesState state));
        Assert.AreEqual(0, state.Economics.KarmaDelta);
        Assert.AreEqual(0m, state.Economics.NuyenDelta);
        Assert.IsTrue(CharacterMartialArtNotesRules.TryValidateMutation(
            state, identity, state.Revision, "new", "#112233"));
    }

    [TestMethod]
    public void Parent_identity_stale_revision_invalid_color_and_noop_fail_closed()
    {
        var identity = new CharacterMartialArtNotesIdentity(ArtId, TechniqueId);
        Assert.IsTrue(CharacterMartialArtNotesRules.TryCreateState(
            identity, true, "Aikido", "Disarm", "old", "Chocolate", out CharacterMartialArtNotesState state));
        Assert.IsFalse(CharacterMartialArtNotesRules.TryValidateMutation(
            state, identity with { MartialArtId = Guid.NewGuid() }, state.Revision, "new", "#112233"));
        Assert.IsFalse(CharacterMartialArtNotesRules.TryValidateMutation(
            state, identity, new string('0', 64), "new", "#112233"));
        Assert.IsFalse(CharacterMartialArtNotesRules.TryValidateMutation(
            state, identity, state.Revision, "new", "not a color"));
        Assert.IsFalse(CharacterMartialArtNotesRules.TryValidateMutation(
            state, identity, state.Revision, state.Notes, state.NotesColor));
    }

    [TestMethod]
    public void Empty_art_or_technique_identity_fails_closed()
    {
        Assert.IsFalse(CharacterMartialArtNotesRules.TryCreateState(
            new CharacterMartialArtNotesIdentity(Guid.Empty, null), false, "A", "A", "", "Chocolate", out _));
        Assert.IsFalse(CharacterMartialArtNotesRules.TryCreateState(
            new CharacterMartialArtNotesIdentity(ArtId, Guid.Empty), false, "A", "T", "", "Chocolate", out _));
    }
}
