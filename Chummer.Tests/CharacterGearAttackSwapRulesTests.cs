using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterGearAttackSwapRulesTests
{
    private static readonly CharacterGearAttackSwapIdentity Identity = new(
        [Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")]);

    [TestMethod]
    public void CreatesBothPhasesFromRawSavedValues()
    {
        Assert.IsTrue(CharacterGearAttackSwapRules.TryCreateState(
            Identity, false, true, "Deck > Child", "7", "{Rating}", "5", "4", out var creation));
        Assert.AreEqual(CharacterGearAttackSwapPhase.Creation, creation.Phase);
        Assert.AreEqual("{Rating}", creation.Sleaze);
        Assert.AreEqual(64, creation.Revision.Length);

        Assert.IsTrue(CharacterGearAttackSwapRules.TryCreateState(
            Identity, true, true, "Deck > Child", "7", "{Rating}", "5", "4", out var career));
        Assert.AreEqual(CharacterGearAttackSwapPhase.Career, career.Phase);
        Assert.AreNotEqual(creation.Revision, career.Revision);
    }

    [TestMethod]
    public void RejectsIneligibleAmbiguousAndNoOpMutations()
    {
        Assert.IsFalse(CharacterGearAttackSwapRules.TryCreateState(
            Identity, false, false, "Deck", "7", "6", "5", "4", out _));
        Assert.IsFalse(CharacterGearAttackSwapRules.IsValidIdentity(new CharacterGearAttackSwapIdentity(
            [Identity.GearPath[0], Identity.GearPath[0]])));

        Assert.IsTrue(CharacterGearAttackSwapRules.TryCreateState(
            Identity, true, true, "Deck", "7", "6", "7", "4", out var state));
        Assert.IsFalse(CharacterGearAttackSwapRules.TryValidateMutation(
            state, state.Revision, CharacterGearAttackSwapTarget.DataProcessing));
        Assert.IsFalse(CharacterGearAttackSwapRules.TryValidateMutation(
            state, new string('0', 64), CharacterGearAttackSwapTarget.Sleaze));
        Assert.IsTrue(CharacterGearAttackSwapRules.TryValidateMutation(
            state, state.Revision, CharacterGearAttackSwapTarget.Firewall));
        Assert.AreEqual("dataprocessing", CharacterGearAttackSwapRules.TargetElement(
            CharacterGearAttackSwapTarget.DataProcessing));
    }
}
