using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationPriorityKarmaBudgetTests
{
    [TestMethod]
    [DataRow(25, 0, 25)]
    [DataRow(25, 10, 15)]
    [DataRow(50, 0, 50)]
    [DataRow(0, 0, 0)]
    public void Priority_balance_uses_total_investment_without_carryover_clamping(
        int afterQualities, int investment, int expected)
    {
        Assert.IsTrue(CharacterCreationPriorityKarmaBudget.TryEvaluate(afterQualities, investment, null, out int actual));
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow(false, 3, 0, 15, 5)]
    [DataRow(true, 3, 2, 5, 15)]
    public void Priority_balance_charges_the_evaluated_mystic_purchase_once(
        bool exchangeSpells, int points, int spellSlots, int expectedCost, int expectedBalance)
    {
        string xml = "<setting><id>budget-test</id><priorityspellsasadeptpowers>" +
            (exchangeSpells ? "True" : "False") +
            "</priorityspellsasadeptpowers><mysadeptsecondmagattribute>False</mysadeptsecondmagattribute>" +
            "<karmacost><karmamysadpp>5</karmamysadpp></karmacost></setting>";
        Assert.IsTrue(CharacterCreationMysticAdeptPowerPointRules.TryCreatePolicy(
            "budget-test", CharacterCreationMagicResonanceDigest.ComputeUtf8(xml), xml, out var policy));
        Assert.IsTrue(CharacterCreationMysticAdeptPowerPointRules.TryEvaluate(policy,
            CharacterCreationMagicResonanceKinds.MysticAdept, 4, spellSlots, points, out var purchase));
        Assert.AreEqual(expectedCost, purchase!.KarmaCost);
        Assert.IsTrue(CharacterCreationPriorityKarmaBudget.TryEvaluate(25, 5, purchase, out int actual));
        Assert.AreEqual(expectedBalance, actual);
    }

    [TestMethod]
    [DataRow(-1, 0, 0)]
    [DataRow(25, -1, 0)]
    [DataRow(25, 0, -1)]
    [DataRow(25, 26, 0)]
    [DataRow(25, 20, 10)]
    [DataRow(0, int.MaxValue, int.MaxValue)]
    public void Priority_balance_rejects_negative_inputs_overspend_and_overflow(
        int afterQualities, int investment, int pointCost)
    {
        // Deliberately invalid arithmetic input, not a forged admitted proposal.
        var purchase = new CharacterCreationMysticAdeptPowerPointAllocation(0, 0, 0, 0, pointCost, null!);
        Assert.IsFalse(CharacterCreationPriorityKarmaBudget.TryEvaluate(afterQualities, investment, purchase, out int actual));
        Assert.AreEqual(0, actual);
    }
}
