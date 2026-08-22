using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerCreateExpenseRulesTests
{
    private static readonly CharacterCareerCreateExpenseState State = new(5, 10_000m, 1_500m, 2_000m);

    [TestMethod]
    public void Exact_operation_identity_selects_mode_spend_and_exchange_description()
    {
        Assert.IsFalse(CharacterCareerCreateExpenseRules.IsNuyen(
            CharacterCareerCreateExpenseOperation.KarmaGained));
        Assert.IsTrue(CharacterCareerCreateExpenseRules.IsNuyen(
            CharacterCareerCreateExpenseOperation.NuyenSpent));
        Assert.IsTrue(CharacterCareerCreateExpenseRules.IsSpend(
            CharacterCareerCreateExpenseOperation.KarmaSpent));
        Assert.AreEqual("Working for the People", CharacterCareerCreateExpenseRules.ExchangeReason(
            CharacterCareerCreateExpenseOperation.KarmaGained));
        Assert.AreEqual("Working for the Man", CharacterCareerCreateExpenseRules.ExchangeReason(
            CharacterCareerCreateExpenseOperation.NuyenGained));
    }

    [TestMethod]
    public void Nuyen_exchange_non_multiple_rejects_and_integral_multiple_is_canonical_no_op()
    {
        Assert.IsTrue(CharacterCareerCreateExpenseRules.TryEvaluateDialog(
            State,
            CharacterCareerCreateExpenseOperation.NuyenGained,
            2_000,
            100m,
            true,
            out CharacterCareerCreateExpenseDialogOutcome rejected));
        Assert.AreEqual(CharacterCareerCreateExpenseDialogOutcome.NuyenExchangeValidationRejected, rejected);

        Assert.IsTrue(CharacterCareerCreateExpenseRules.TryEvaluateDialog(
            State,
            CharacterCareerCreateExpenseOperation.NuyenSpent,
            3_000,
            725m,
            true,
            out CharacterCareerCreateExpenseDialogOutcome noOp));
        Assert.AreEqual(CharacterCareerCreateExpenseDialogOutcome.NuyenExchangeCanonicalNoOp, noOp);
    }

    [TestMethod]
    public void Proven_commit_branches_retain_percentage_affordability_and_karma_exchange_rules()
    {
        Assert.IsTrue(CharacterCareerCreateExpenseRules.TryEvaluateDialog(
            State,
            CharacterCareerCreateExpenseOperation.NuyenGained,
            100,
            150m,
            false,
            out CharacterCareerCreateExpenseDialogOutcome nuyen));
        Assert.AreEqual(CharacterCareerCreateExpenseDialogOutcome.Commit, nuyen);

        Assert.IsTrue(CharacterCareerCreateExpenseRules.TryEvaluateDialog(
            State,
            CharacterCareerCreateExpenseOperation.NuyenSpent,
            9_999_999,
            100m,
            false,
            out CharacterCareerCreateExpenseDialogOutcome callerRejected));
        Assert.AreEqual(
            CharacterCareerCreateExpenseDialogOutcome.CallerBalanceValidationRejected,
            callerRejected);

        Assert.IsTrue(CharacterCareerCreateExpenseRules.TryEvaluateDialog(
            State,
            CharacterCareerCreateExpenseOperation.KarmaSpent,
            3,
            100m,
            true,
            out CharacterCareerCreateExpenseDialogOutcome karma));
        Assert.AreEqual(CharacterCareerCreateExpenseDialogOutcome.Commit, karma);
    }

    [TestMethod]
    public void Creation_rates_bounds_and_percent_precision_fail_closed()
    {
        Assert.IsFalse(CharacterCareerCreateExpenseRules.TryProject(
            false, 5, 0m, 1_500m, 2_000m, out _));
        Assert.IsFalse(CharacterCareerCreateExpenseRules.TryProject(
            true, 5, 0m, null, 2_000m, out _));
        Assert.IsFalse(CharacterCareerCreateExpenseRules.TryEvaluateDialog(
            State, CharacterCareerCreateExpenseOperation.KarmaGained, 0, 100m, false, out _));
        Assert.IsFalse(CharacterCareerCreateExpenseRules.TryEvaluateDialog(
            State, CharacterCareerCreateExpenseOperation.NuyenGained, 1, 100.001m, false, out _));
    }
}
