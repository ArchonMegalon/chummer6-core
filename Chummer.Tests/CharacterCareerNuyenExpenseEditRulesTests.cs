using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerNuyenExpenseEditRulesTests
{
    private static readonly Guid ExpenseId = Guid.Parse("65da27db-24a8-4b6e-b42c-30f4bb13a4f8");

    [TestMethod]
    public void Manual_entries_can_change_amount_and_return_exact_balance_delta()
    {
        Assert.IsTrue(CharacterCareerNuyenExpenseEditRules.TryCreateEntry(
            ExpenseId,
            new DateTime(2081, 5, 12, 14, 30, 0),
            -250m,
            "Ammo",
            refund: false,
            forceCareerVisible: true,
            "ManualSubtract",
            out CharacterCareerNuyenExpenseEntry? entry));

        Assert.IsTrue(CharacterCareerNuyenExpenseEditRules.TryEdit(
            entry!,
            -175m,
            "Less ammo",
            new DateTime(2081, 5, 13, 9, 15, 0),
            out CharacterCareerNuyenExpenseEditResult? result));
        Assert.AreEqual(75m, result!.NuyenDelta);
        Assert.AreEqual(-175m, result.Expense.Amount);
        Assert.AreEqual("Less ammo", result.Expense.Reason);
        Assert.IsFalse(result.Expense.Refund);
        Assert.IsTrue(result.Expense.ForceCareerVisible);
        Assert.AreEqual("ManualSubtract", result.Expense.NuyenUndoType);
    }

    [TestMethod]
    public void Nonmanual_entries_lock_amount_but_still_allow_date_and_reason_changes()
    {
        Assert.IsTrue(CharacterCareerNuyenExpenseEditRules.TryCreateEntry(
            ExpenseId,
            new DateTime(2081, 5, 12),
            -500m,
            "Armor",
            refund: false,
            forceCareerVisible: false,
            "AddArmor",
            out CharacterCareerNuyenExpenseEntry? entry));

        Assert.IsFalse(CharacterCareerNuyenExpenseEditRules.TryEdit(
            entry!, -499m, "Armor", entry!.ExpenseDateLocal, out _));
        Assert.IsTrue(CharacterCareerNuyenExpenseEditRules.TryEdit(
            entry!, -500m, "Repaired armor", new DateTime(2081, 5, 14), out CharacterCareerNuyenExpenseEditResult? result));
        Assert.AreEqual(0m, result!.NuyenDelta);
        Assert.AreEqual("Repaired armor", result.Expense.Reason);
    }

    [TestMethod]
    public void Dynamic_legacy_bounds_and_invalid_identity_fail_closed()
    {
        Assert.IsFalse(CharacterCareerNuyenExpenseEditRules.TryCreateEntry(
            Guid.Empty,
            new DateTime(2081, 5, 12),
            1m,
            "Bad",
            false,
            false,
            "ManualAdd",
            out _));
        Assert.IsTrue(CharacterCareerNuyenExpenseEditRules.TryCreateEntry(
            ExpenseId,
            new DateTime(2081, 5, 12),
            10m,
            "Income",
            false,
            false,
            "ManualAdd",
            out CharacterCareerNuyenExpenseEntry? positive));
        Assert.IsFalse(CharacterCareerNuyenExpenseEditRules.TryEdit(
            positive!, 0m, "Income", positive!.ExpenseDateLocal, out _));
        Assert.IsFalse(CharacterCareerNuyenExpenseEditRules.TryEdit(
            positive!, 10m, new string('x', CharacterCareerNuyenExpenseEditRules.MaximumReasonLength + 1), positive!.ExpenseDateLocal, out _));
    }
}
