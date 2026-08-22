using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterMartialArtDeleteRulesTests
{
    private static readonly Guid ArtId = Guid.Parse("a1111111-a111-a111-a111-a11111111111");
    private static readonly Guid TechniqueId = Guid.Parse("a2222222-a222-a222-a222-a22222222222");

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Creation_and_career_require_confirmation_and_never_refund(bool created)
    {
        var identity = new CharacterMartialArtDeleteIdentity(ArtId, null);
        Assert.IsTrue(CharacterMartialArtDeleteRules.TryCreateState(
            identity, created, "Aikido", "Aikido", false, 2,
            "art subtree", "exact source identities", out CharacterMartialArtDeleteState state));
        Assert.AreEqual(0, state.Economics.KarmaDelta);
        Assert.AreEqual(0m, state.Economics.NuyenDelta);
        Assert.IsFalse(CharacterMartialArtDeleteRules.CanDelete(
            state, identity, state.Revision, confirmed: false));
        Assert.IsTrue(CharacterMartialArtDeleteRules.CanDelete(
            state, identity, state.Revision, confirmed: true));
    }

    [TestMethod]
    public void Quality_art_is_protected_but_its_parent_scoped_technique_is_removable()
    {
        var art = new CharacterMartialArtDeleteIdentity(ArtId, null);
        Assert.IsTrue(CharacterMartialArtDeleteRules.TryCreateState(
            art, true, "Quality Art", "Quality Art", true, 1,
            "art subtree", "improvements", out CharacterMartialArtDeleteState artState));
        Assert.IsFalse(CharacterMartialArtDeleteRules.CanDelete(
            artState, art, artState.Revision, confirmed: true));

        var technique = new CharacterMartialArtDeleteIdentity(ArtId, TechniqueId);
        Assert.IsTrue(CharacterMartialArtDeleteRules.TryCreateState(
            technique, true, "Quality Art", "Disarm", true, 0,
            "technique subtree", "improvements", out CharacterMartialArtDeleteState techniqueState));
        Assert.IsTrue(CharacterMartialArtDeleteRules.CanDelete(
            techniqueState, technique, techniqueState.Revision, confirmed: true));
    }

    [TestMethod]
    public void Empty_identity_stale_revision_and_invalid_cascade_fail_closed()
    {
        Assert.IsFalse(CharacterMartialArtDeleteRules.TryCreateState(
            new CharacterMartialArtDeleteIdentity(Guid.Empty, null), false,
            "A", "A", false, 0, "target", "improvements", out _));
        Assert.IsFalse(CharacterMartialArtDeleteRules.TryCreateState(
            new CharacterMartialArtDeleteIdentity(ArtId, TechniqueId), false,
            "A", "T", false, 1, "target", "improvements", out _));

        var identity = new CharacterMartialArtDeleteIdentity(ArtId, TechniqueId);
        Assert.IsTrue(CharacterMartialArtDeleteRules.TryCreateState(
            identity, false, "A", "T", false, 0,
            "target", "improvements", out CharacterMartialArtDeleteState state));
        Assert.IsFalse(CharacterMartialArtDeleteRules.CanDelete(
            state, identity, new string('0', 64), confirmed: true));
        Assert.IsFalse(CharacterMartialArtDeleteRules.CanDelete(
            state, identity with { MartialArtId = Guid.NewGuid() }, state.Revision, confirmed: true));
    }
}
