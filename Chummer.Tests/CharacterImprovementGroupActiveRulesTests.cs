using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterImprovementGroupActiveRulesTests
{
    private static CharacterImprovementGroupMemberState Member(string improvedName, bool enabled)
        => new(
            new CharacterImprovementIdentity(
                "a6111111-6111-6111-6111-611111111111",
                "Attribute",
                "Custom",
                improvedName,
                string.Empty,
                string.Empty,
                string.Empty,
                "Alpha"),
            enabled);

    [TestMethod]
    public void Ungrouped_and_named_group_identity_match_exact_legacy_scope()
    {
        var ungrouped = new CharacterImprovementGroupIdentity(
            CharacterImprovementGroupKind.Ungrouped,
            string.Empty);
        var named = new CharacterImprovementGroupIdentity(
            CharacterImprovementGroupKind.Named,
            "Alpha");

        Assert.IsTrue(CharacterImprovementGroupActiveRules.Includes(
            ungrouped, custom: true, customGroup: string.Empty));
        Assert.IsFalse(CharacterImprovementGroupActiveRules.Includes(
            ungrouped, custom: true, customGroup: "Alpha"));
        Assert.IsTrue(CharacterImprovementGroupActiveRules.Includes(
            named, custom: true, customGroup: "Alpha"));
        Assert.IsFalse(CharacterImprovementGroupActiveRules.Includes(
            named, custom: false, customGroup: "Alpha"));
    }

    [TestMethod]
    public void Career_revision_and_opposite_state_member_are_required()
    {
        var group = new CharacterImprovementGroupIdentity(
            CharacterImprovementGroupKind.Named,
            "Alpha");
        Assert.IsTrue(CharacterImprovementGroupActiveRules.TryCreateState(
            group,
            created: true,
            displayName: "Alpha",
            members: [Member("BOD", true), Member("AGI", false)],
            out CharacterImprovementGroupActiveState state));
        Assert.AreEqual(CharacterImprovementGroupActiveRules.RevisionHexLength, state.Revision.Length);
        Assert.IsTrue(CharacterImprovementGroupActiveRules.TryValidateMutation(
            state, state.Revision, enabled: true));
        Assert.IsTrue(CharacterImprovementGroupActiveRules.TryValidateMutation(
            state, state.Revision, enabled: false));
        Assert.IsFalse(CharacterImprovementGroupActiveRules.TryValidateMutation(
            state, new string('0', 64), enabled: true));

        Assert.IsTrue(CharacterImprovementGroupActiveRules.TryCreateState(
            group,
            true,
            "Alpha",
            [Member("BOD", true), Member("AGI", true)],
            out CharacterImprovementGroupActiveState allEnabled));
        Assert.IsFalse(CharacterImprovementGroupActiveRules.TryValidateMutation(
            allEnabled, allEnabled.Revision, enabled: true));
    }

    [TestMethod]
    public void Creation_reserved_group_and_member_changes_fail_closed_or_change_revision()
    {
        var group = new CharacterImprovementGroupIdentity(
            CharacterImprovementGroupKind.Named,
            "Alpha");
        Assert.IsFalse(CharacterImprovementGroupActiveRules.TryCreateState(
            group, created: false, "Alpha", [Member("BOD", true)], out _));
        Assert.IsFalse(CharacterImprovementGroupActiveRules.IsValidIdentity(
            new CharacterImprovementGroupIdentity(
                CharacterImprovementGroupKind.Named,
                CharacterImprovementGroupActiveRules.UngroupedLegacyNodeId)));

        Assert.IsTrue(CharacterImprovementGroupActiveRules.TryCreateState(
            group, true, "Alpha", [Member("BOD", true)], out CharacterImprovementGroupActiveState one));
        Assert.IsTrue(CharacterImprovementGroupActiveRules.TryCreateState(
            group, true, "Alpha", [Member("BOD", true), Member("AGI", false)],
            out CharacterImprovementGroupActiveState two));
        Assert.AreNotEqual(one.Revision, two.Revision);
        Assert.IsFalse(CharacterImprovementGroupActiveRules.TryCreateState(
            group, true, "Alpha", [Member("BOD", true), Member("BOD", false)], out _));
    }
}
