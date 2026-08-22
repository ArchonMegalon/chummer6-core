using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCyberwareMatrixSwapRulesTests
{
    private static readonly CharacterCyberwareMatrixSwapIdentity Identity =
        new(Guid.Parse("a6111111-1611-4611-8611-161111111111"));

    [TestMethod]
    public void Creation_and_career_bind_typed_root_raw_provenance_and_zero_economics()
    {
        Assert.IsTrue(CharacterCyberwareMatrixSwapRules.TryCreateState(
            Identity, false, "Creation Deck", "7", "{Rating}", "5", "4", "7,6,5,4", true,
            out CharacterCyberwareMatrixSwapState creation));
        Assert.IsTrue(CharacterCyberwareMatrixSwapRules.TryCreateState(
            Identity, true, "Career Deck", "8", "7", "{Rating}", "5", "8,7,6,5", true,
            out CharacterCyberwareMatrixSwapState career));

        Assert.AreEqual(CharacterCyberwareMatrixSwapPhase.Creation, creation.Phase);
        Assert.AreEqual(CharacterCyberwareMatrixSwapPhase.Career, career.Phase);
        Assert.AreEqual(Identity, creation.Identity);
        Assert.AreEqual("7,6,5,4", creation.Provenance.AttributeArray);
        Assert.AreEqual(0m, creation.Economics.NuyenDelta);
        Assert.AreEqual(0, career.Economics.KarmaDelta);
        Assert.AreNotEqual(creation.Revision, career.Revision);
    }

    [TestMethod]
    public void All_four_handlers_share_one_revision_bound_raw_permutation_authority()
    {
        Assert.IsTrue(CharacterCyberwareMatrixSwapRules.TryCreateState(
            Identity, false, "Deck", "7", "6", "5", "4", "7,6,5,4", true, out var state));

        foreach (CharacterCyberwareMatrixStat changed in Enum.GetValues<CharacterCyberwareMatrixStat>())
        {
            CharacterCyberwareMatrixStat target = changed == CharacterCyberwareMatrixStat.Attack
                ? CharacterCyberwareMatrixStat.Firewall
                : CharacterCyberwareMatrixStat.Attack;
            Assert.IsTrue(CharacterCyberwareMatrixSwapRules.TryValidateMutation(
                state, state.Revision, changed, target), changed.ToString());
        }

        Assert.IsFalse(CharacterCyberwareMatrixSwapRules.TryValidateMutation(
            state, new string('0', 64), CharacterCyberwareMatrixStat.Attack,
            CharacterCyberwareMatrixStat.Firewall));
        Assert.IsFalse(CharacterCyberwareMatrixSwapRules.TryValidateMutation(
            state, state.Revision, CharacterCyberwareMatrixStat.Attack,
            CharacterCyberwareMatrixStat.Attack));
        Assert.IsTrue(CharacterCyberwareMatrixSwapRules.RequiresMatrixInitiativeNotification(
            CharacterCyberwareMatrixStat.Firewall, CharacterCyberwareMatrixStat.DataProcessing));
        Assert.IsFalse(CharacterCyberwareMatrixSwapRules.RequiresMatrixInitiativeNotification(
            CharacterCyberwareMatrixStat.Firewall, CharacterCyberwareMatrixStat.Attack));
    }

    [TestMethod]
    public void Missing_root_enable_provenance_or_distinct_values_fails_closed()
    {
        Assert.IsFalse(CharacterCyberwareMatrixSwapRules.TryCreateState(
            new CharacterCyberwareMatrixSwapIdentity(Guid.Empty), false, "Deck", "7", "6", "5", "4",
            "7,6,5,4", true, out _));
        Assert.IsFalse(CharacterCyberwareMatrixSwapRules.TryCreateState(
            Identity, false, "Deck", "7", "6", "5", "4", "7,6,5,4", false, out _));
        Assert.IsTrue(CharacterCyberwareMatrixSwapRules.TryCreateState(
            Identity, false, "Deck", "7", "6", "5", "5", "7,6,5,4", true, out var equal));
        Assert.IsFalse(CharacterCyberwareMatrixSwapRules.TryValidateMutation(
            equal, equal.Revision, CharacterCyberwareMatrixStat.DataProcessing,
            CharacterCyberwareMatrixStat.Firewall));
    }
}
