using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterGearMatrixSwapRulesTests
{
    [TestMethod]
    public void SleazeUsesTypedSharedRawSwapAuthorityInBothPhases()
    {
        var identity = new CharacterGearMatrixSwapIdentity([Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]);
        Assert.IsTrue(CharacterGearMatrixSwapRules.TryCreateState(
            identity, false, true, "Deck", "7", "{Rating}", "5", "4", out var creation));
        Assert.AreEqual(CharacterGearMatrixSwapPhase.Creation, creation.Phase);
        Assert.IsTrue(CharacterGearMatrixSwapRules.TryValidateMutation(creation, creation.Revision,
            CharacterGearMatrixStat.Sleaze, CharacterGearMatrixStat.DataProcessing));
        Assert.IsFalse(CharacterGearMatrixSwapRules.TryValidateMutation(creation, creation.Revision,
            CharacterGearMatrixStat.Sleaze, CharacterGearMatrixStat.Sleaze));
        Assert.AreEqual("{Rating}", CharacterGearMatrixSwapRules.Read(creation, CharacterGearMatrixStat.Sleaze));
        Assert.AreEqual("dataprocessing", CharacterGearMatrixSwapRules.ElementName(CharacterGearMatrixStat.DataProcessing));
        Assert.IsTrue(CharacterGearMatrixSwapRules.TryCreateState(
            identity, true, true, "Deck", "7", "{Rating}", "5", "4", out var career));
        Assert.AreEqual(CharacterGearMatrixSwapPhase.Career, career.Phase);
        Assert.AreNotEqual(creation.Revision, career.Revision);
    }

    [TestMethod]
    public void DataProcessingAndFirewallUseExplicitTypedRawSwapAuthority()
    {
        var identity = new CharacterGearMatrixSwapIdentity(
            [Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
             Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")]);
        Assert.IsTrue(CharacterGearMatrixSwapRules.TryCreateState(
            identity, false, true, "Root > Deck", "7", "{Rating}", "5", "4", out var state));

        Assert.IsTrue(CharacterGearMatrixSwapRules.TryValidateDataProcessingOrFirewallMutation(
            state, state.Revision, CharacterGearMatrixStat.DataProcessing, CharacterGearMatrixStat.Attack));
        Assert.IsTrue(CharacterGearMatrixSwapRules.TryValidateDataProcessingOrFirewallMutation(
            state, state.Revision, CharacterGearMatrixStat.Firewall, CharacterGearMatrixStat.DataProcessing));
        Assert.IsFalse(CharacterGearMatrixSwapRules.TryValidateDataProcessingOrFirewallMutation(
            state, state.Revision, CharacterGearMatrixStat.Attack, CharacterGearMatrixStat.DataProcessing));
        Assert.IsFalse(CharacterGearMatrixSwapRules.TryValidateDataProcessingOrFirewallMutation(
            state, new string('0', CharacterGearMatrixSwapRules.RevisionHexLength),
            CharacterGearMatrixStat.Firewall, CharacterGearMatrixStat.DataProcessing));
        Assert.IsTrue(CharacterGearMatrixSwapRules.RequiresMatrixInitiativeNotification(
            CharacterGearMatrixStat.Firewall, CharacterGearMatrixStat.DataProcessing));
        Assert.IsFalse(CharacterGearMatrixSwapRules.RequiresMatrixInitiativeNotification(
            CharacterGearMatrixStat.Firewall, CharacterGearMatrixStat.Attack));
    }

    [TestMethod]
    public void DataProcessingFirewallSliceRejectsIneligibleOrEqualRawValues()
    {
        var identity = new CharacterGearMatrixSwapIdentity([Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")]);
        Assert.IsFalse(CharacterGearMatrixSwapRules.TryCreateState(
            identity, true, false, "Deck", "7", "6", "5", "4", out _));
        Assert.IsTrue(CharacterGearMatrixSwapRules.TryCreateState(
            identity, true, true, "Deck", "7", "6", "5", "5", out var equal));
        Assert.AreEqual(CharacterGearMatrixSwapPhase.Career, equal.Phase);
        Assert.IsFalse(CharacterGearMatrixSwapRules.TryValidateDataProcessingOrFirewallMutation(
            equal, equal.Revision, CharacterGearMatrixStat.DataProcessing, CharacterGearMatrixStat.Firewall));
    }
}
