using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterGearOverclockerRulesTests
{
    private static readonly Guid RootId = Guid.Parse("a9151111-1511-4511-8511-151111111111");
    private static readonly Guid ChildId = Guid.Parse("b9151111-1511-4511-8511-151111111111");

    [TestMethod]
    public void Career_eligible_nested_cyberdeck_has_exact_zero_economic_state()
    {
        var identity = new CharacterGearOverclockerIdentity([RootId, ChildId]);

        Assert.IsTrue(CharacterGearOverclockerRules.TryCreateState(
            identity, true, true, "Cyberdecks", "Root > Deck", "Data Processing",
            out CharacterGearOverclockerState state));
        Assert.AreEqual(CharacterGearOverclockerPhase.Career, state.Phase);
        Assert.AreEqual(CharacterGearOverclockerAttribute.DataProcessing, state.Attribute);
        Assert.AreEqual(0m, state.Economics.NuyenDelta);
        Assert.AreEqual(0, state.Economics.KarmaDelta);
        Assert.AreEqual(CharacterGearOverclockerRules.RevisionHexLength, state.Revision.Length);
    }

    [TestMethod]
    public void Creation_missing_improvement_wrong_category_and_invalid_value_fail_closed()
    {
        var identity = new CharacterGearOverclockerIdentity([RootId]);
        Assert.IsFalse(CharacterGearOverclockerRules.TryCreateState(
            identity, false, true, "Cyberdecks", "Deck", "Attack", out _));
        Assert.IsFalse(CharacterGearOverclockerRules.TryCreateState(
            identity, true, false, "Cyberdecks", "Deck", "Attack", out _));
        Assert.IsFalse(CharacterGearOverclockerRules.TryCreateState(
            identity, true, true, "Commlinks", "Deck", "Attack", out _));
        Assert.IsFalse(CharacterGearOverclockerRules.TryCreateState(
            identity, true, true, "Cyberdecks", "Deck", "Device Rating", out _));
    }

    [TestMethod]
    public void Revision_binds_path_and_value_and_mutation_rejects_stale_or_noop()
    {
        var identity = new CharacterGearOverclockerIdentity([RootId]);
        Assert.IsTrue(CharacterGearOverclockerRules.TryCreateState(
            identity, true, true, "Cyberdecks", "Deck", "Attack",
            out CharacterGearOverclockerState current));
        Assert.IsTrue(CharacterGearOverclockerRules.TryCreateState(
            new CharacterGearOverclockerIdentity([RootId, ChildId]),
            true, true, "Cyberdecks", "Deck > Nested", "Attack",
            out CharacterGearOverclockerState moved));

        Assert.AreNotEqual(current.Revision, moved.Revision);
        Assert.IsFalse(CharacterGearOverclockerRules.TryValidateMutation(
            current, new string('0', 64), CharacterGearOverclockerAttribute.Firewall));
        Assert.IsFalse(CharacterGearOverclockerRules.TryValidateMutation(
            current, current.Revision, CharacterGearOverclockerAttribute.Attack));
        Assert.IsTrue(CharacterGearOverclockerRules.TryValidateMutation(
            current, current.Revision, CharacterGearOverclockerAttribute.Firewall));
        Assert.AreEqual("Data Processing", CharacterGearOverclockerRules.ToSavedValue(
            CharacterGearOverclockerAttribute.DataProcessing));
    }
}
