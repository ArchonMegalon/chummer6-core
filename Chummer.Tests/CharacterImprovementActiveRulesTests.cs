using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterImprovementActiveRulesTests
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
    public void Career_direct_improvement_projects_with_revision()
    {
        Assert.IsTrue(CharacterImprovementActiveRules.TryCreateState(
            Identity,
            created: true,
            displayName: "Body bonus",
            enabled: true,
            out CharacterImprovementActiveState state));
        Assert.AreEqual(CharacterImprovementActiveRules.RevisionHexLength, state.Revision.Length);
        Assert.IsTrue(CharacterImprovementActiveRules.IdentityEquals(Identity, state.Identity));
    }

    [TestMethod]
    public void Creation_missing_identity_stale_revision_and_noop_fail_closed()
    {
        Assert.IsFalse(CharacterImprovementActiveRules.TryCreateState(
            Identity, created: false, "Body bonus", enabled: true, out _));
        Assert.IsFalse(CharacterImprovementActiveRules.TryCreateState(
            Identity with { SourceName = string.Empty }, true, "Body bonus", true, out _));

        Assert.IsTrue(CharacterImprovementActiveRules.TryCreateState(
            Identity, true, "Body bonus", true, out CharacterImprovementActiveState state));
        Assert.IsFalse(CharacterImprovementActiveRules.TryValidateMutation(
            state, new string('0', 64), enabled: false));
        Assert.IsFalse(CharacterImprovementActiveRules.TryValidateMutation(
            state, state.Revision, enabled: true));
        Assert.IsTrue(CharacterImprovementActiveRules.TryValidateMutation(
            state, state.Revision, enabled: false));
    }

    [TestMethod]
    public void Revision_binds_semantic_identity_and_enabled_state()
    {
        Assert.IsTrue(CharacterImprovementActiveRules.TryCreateState(
            Identity, true, "Body bonus", true, out CharacterImprovementActiveState original));
        Assert.IsTrue(CharacterImprovementActiveRules.TryCreateState(
            Identity with { ImprovedName = "AGI" }, true, "Body bonus", true,
            out CharacterImprovementActiveState otherEffect));
        Assert.IsTrue(CharacterImprovementActiveRules.TryCreateState(
            Identity, true, "Body bonus", false, out CharacterImprovementActiveState disabled));

        Assert.AreNotEqual(original.Revision, otherEffect.Revision);
        Assert.AreNotEqual(original.Revision, disabled.Revision);
    }
}
