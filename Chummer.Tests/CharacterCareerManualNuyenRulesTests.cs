using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerManualNuyenRulesTests
{
    private static readonly CharacterCareerManualNuyenState State = new(
        AvailableKarma: 5,
        AvailableNuyen: 10_000m,
        NuyenPerKarmaWorkingForPeople: 1_500m,
        NuyenPerKarmaWorkingForMan: 2_000m);

    [TestMethod]
    public void Normal_gain_and_spend_apply_exact_percentage_before_balance_checks()
    {
        Assert.IsTrue(CharacterCareerManualNuyenRules.TryQuote(
            State,
            CharacterCareerManualNuyenAction.Gain,
            enteredAmount: 100,
            percent: 150m,
            karmaNuyenExchange: false,
            out CharacterCareerManualNuyenQuote? gained));
        Assert.AreEqual(150m, gained!.NuyenExpenseAmount);
        Assert.AreEqual(10_150m, gained.UpdatedNuyen);
        Assert.AreEqual(5, gained.UpdatedKarma);

        Assert.IsTrue(CharacterCareerManualNuyenRules.TryQuote(
            State,
            CharacterCareerManualNuyenAction.Spend,
            enteredAmount: 100,
            percent: 50m,
            karmaNuyenExchange: false,
            out CharacterCareerManualNuyenQuote? spent));
        Assert.AreEqual(-50m, spent!.NuyenExpenseAmount);
        Assert.AreEqual(9_950m, spent.UpdatedNuyen);
    }

    [TestMethod]
    public void Exchange_preserves_people_validation_and_action_specific_conversion_rates()
    {
        Assert.IsTrue(CharacterCareerManualNuyenRules.TryQuote(
            State,
            CharacterCareerManualNuyenAction.Gain,
            enteredAmount: 3_000,
            percent: 725m,
            karmaNuyenExchange: true,
            out CharacterCareerManualNuyenQuote? gained));
        Assert.AreEqual(3_000m, gained!.NuyenExpenseAmount);
        Assert.AreEqual(-1, gained.KarmaExpenseAmount);
        Assert.AreEqual(4, gained.UpdatedKarma);

        Assert.IsTrue(CharacterCareerManualNuyenRules.TryQuote(
            State,
            CharacterCareerManualNuyenAction.Spend,
            enteredAmount: 3_000,
            percent: 25m,
            karmaNuyenExchange: true,
            out CharacterCareerManualNuyenQuote? spent));
        Assert.AreEqual(-3_000m, spent!.NuyenExpenseAmount);
        Assert.AreEqual(2, spent.KarmaExpenseAmount);
        Assert.AreEqual(7, spent.UpdatedKarma);
    }

    [TestMethod]
    public void Invalid_precision_exchange_multiple_and_unaffordable_spend_fail_closed()
    {
        Assert.IsFalse(CharacterCareerManualNuyenRules.TryQuote(
            State,
            CharacterCareerManualNuyenAction.Gain,
            1,
            100.001m,
            false,
            out _));
        Assert.IsFalse(CharacterCareerManualNuyenRules.TryQuote(
            State,
            CharacterCareerManualNuyenAction.Gain,
            2_000,
            100m,
            true,
            out _));
        Assert.IsFalse(CharacterCareerManualNuyenRules.TryQuote(
            State,
            CharacterCareerManualNuyenAction.Spend,
            9_999_999,
            100m,
            false,
            out _));
    }
}
