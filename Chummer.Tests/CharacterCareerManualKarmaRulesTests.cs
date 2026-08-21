using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerManualKarmaRulesTests
{
    private static readonly CharacterCareerManualKarmaState State = new(5, 10_000m, 1_500m, 2_000m);

    [TestMethod]
    public void Gain_and_spend_without_exchange_write_exact_karma_deltas()
    {
        Assert.IsTrue(CharacterCareerManualKarmaRules.TryQuote(
            State, CharacterCareerManualKarmaAction.Gain, 2, false, out CharacterCareerManualKarmaQuote? gain));
        Assert.IsNotNull(gain);
        Assert.AreEqual(7, gain.UpdatedKarma);
        Assert.AreEqual(2, gain.KarmaExpenseAmount);
        Assert.AreEqual(10_000m, gain.UpdatedNuyen);

        Assert.IsTrue(CharacterCareerManualKarmaRules.TryQuote(
            State, CharacterCareerManualKarmaAction.Spend, 3, false, out CharacterCareerManualKarmaQuote? spend));
        Assert.IsNotNull(spend);
        Assert.AreEqual(2, spend.UpdatedKarma);
        Assert.AreEqual(-3, spend.KarmaExpenseAmount);
        Assert.AreEqual(0m, spend.NuyenExpenseAmount);
    }

    [TestMethod]
    public void Exchange_preserves_gain_rate_asymmetry_and_spend_rate()
    {
        Assert.IsTrue(CharacterCareerManualKarmaRules.TryQuote(
            State, CharacterCareerManualKarmaAction.Gain, 2, true, out CharacterCareerManualKarmaQuote? gain));
        Assert.IsNotNull(gain);
        Assert.AreEqual(-3_000m, gain.NuyenExpenseAmount);
        Assert.AreEqual(-4_000m, gain.NuyenBalanceDelta);
        Assert.AreEqual(6_000m, gain.UpdatedNuyen);

        Assert.IsTrue(CharacterCareerManualKarmaRules.TryQuote(
            State, CharacterCareerManualKarmaAction.Spend, 3, true, out CharacterCareerManualKarmaQuote? spend));
        Assert.IsNotNull(spend);
        Assert.AreEqual(6_000m, spend.NuyenExpenseAmount);
        Assert.AreEqual(16_000m, spend.UpdatedNuyen);
    }

    [TestMethod]
    public void Creation_missing_rates_invalid_amount_and_unaffordable_spend_fail_closed()
    {
        Assert.IsFalse(CharacterCareerManualKarmaRules.TryProject(false, 5, 0m, 1m, 1m, out _));
        Assert.IsFalse(CharacterCareerManualKarmaRules.TryProject(true, 5, 0m, null, 1m, out _));
        Assert.IsFalse(CharacterCareerManualKarmaRules.TryQuote(
            State, CharacterCareerManualKarmaAction.Gain, 0, false, out _));
        Assert.IsFalse(CharacterCareerManualKarmaRules.TryQuote(
            State, CharacterCareerManualKarmaAction.Spend, 6, false, out _));
    }
}
