using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerKarmaExpenseEditRulesTests
{
    private static readonly Guid ExpenseId = Guid.Parse("e646db17-e13b-4a84-b63e-cd8b776334dd");

    [TestMethod]
    public void Manual_add_and_subtract_truncate_toward_zero_and_return_exact_karma_delta()
    {
        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            new DateTime(2081, 5, 12, 14, 30, 0),
            10.75m,
            "Run reward",
            refund: false,
            forceCareerVisible: true,
            "ManualAdd",
            out CharacterCareerKarmaExpenseEntry? added));

        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryEdit(
            added!,
            12.99m,
            "Corrected reward",
            new DateTime(2081, 5, 13, 9, 15, 0),
            out CharacterCareerKarmaExpenseEditResult? gained));
        Assert.AreEqual(2, gained!.KarmaDelta);
        Assert.AreEqual(12m, gained.Expense.Amount);
        Assert.AreEqual("Corrected reward", gained.Expense.Reason);
        Assert.IsFalse(gained.Expense.Refund);
        Assert.IsTrue(gained.Expense.ForceCareerVisible);
        Assert.AreEqual("ManualAdd", gained.Expense.KarmaUndoType);

        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            new DateTime(2081, 5, 12),
            -10.75m,
            "Training",
            refund: true,
            forceCareerVisible: false,
            "ManualSubtract",
            out CharacterCareerKarmaExpenseEntry? subtracted));
        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryEdit(
            subtracted!,
            -12.99m,
            "Longer training",
            subtracted!.ExpenseDateLocal,
            out CharacterCareerKarmaExpenseEditResult? spent));
        Assert.AreEqual(-2, spent!.KarmaDelta);
        Assert.AreEqual(-12m, spent.Expense.Amount);
        Assert.IsTrue(spent.Expense.Refund);
        Assert.IsFalse(spent.Expense.ForceCareerVisible);
    }

    [TestMethod]
    public void Fractional_change_with_same_truncated_integer_preserves_saved_amount()
    {
        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            new DateTime(2081, 5, 12),
            10.75m,
            "Run reward",
            refund: false,
            forceCareerVisible: false,
            "ManualAdd",
            out CharacterCareerKarmaExpenseEntry? entry));

        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryEdit(
            entry!,
            10.01m,
            "Renamed reward",
            new DateTime(2081, 5, 14),
            out CharacterCareerKarmaExpenseEditResult? result));
        Assert.AreEqual(0, result!.KarmaDelta);
        Assert.AreEqual(10.75m, result.Expense.Amount);
        Assert.AreEqual("Renamed reward", result.Expense.Reason);
        Assert.AreEqual(new DateTime(2081, 5, 14), result.Expense.ExpenseDateLocal);
    }

    [TestMethod]
    public void Karma_delta_truncates_each_amount_before_subtraction()
    {
        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            new DateTime(2081, 5, 12),
            1.9m,
            "Run reward",
            refund: false,
            forceCareerVisible: false,
            "ManualAdd",
            out CharacterCareerKarmaExpenseEntry? entry));

        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryEdit(
            entry!,
            2.1m,
            entry!.Reason,
            entry.ExpenseDateLocal,
            out CharacterCareerKarmaExpenseEditResult? result));
        Assert.AreEqual(1, result!.KarmaDelta);
        Assert.AreEqual(2m, result.Expense.Amount);
    }

    [TestMethod]
    public void Default_nonmanual_and_wrong_case_undo_types_lock_amount()
    {
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.IsAmountEditable(null));
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.IsAmountEditable("manualadd"));
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.IsAmountEditable("MANUALSUBTRACT"));
        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.IsAmountEditable("ManualAdd"));
        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.IsAmountEditable("ManualSubtract"));

        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            new DateTime(2081, 5, 12),
            -5m,
            "Attribute",
            refund: false,
            forceCareerVisible: true,
            karmaUndoType: null,
            out CharacterCareerKarmaExpenseEntry? entry));
        Assert.AreEqual(CharacterCareerKarmaExpenseEditRules.DefaultKarmaUndoType, entry!.KarmaUndoType);
        Assert.IsFalse(entry.AmountEditable);
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryEdit(
            entry, -4m, "Attribute", entry.ExpenseDateLocal, out _));
        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryEdit(
            entry,
            -5m,
            "Corrected attribute label",
            new DateTime(2081, 5, 15),
            out CharacterCareerKarmaExpenseEditResult? result));
        Assert.AreEqual(0, result!.KarmaDelta);
        Assert.AreEqual(-5m, result.Expense.Amount);
        Assert.AreEqual("Corrected attribute label", result.Expense.Reason);

        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            new DateTime(2081, 5, 12),
            5m,
            "Wrong case",
            refund: false,
            forceCareerVisible: false,
            "manualadd",
            out CharacterCareerKarmaExpenseEntry? wrongCase));
        Assert.IsFalse(wrongCase!.AmountEditable);
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryEdit(
            wrongCase, 6m, wrongCase.Reason, wrongCase.ExpenseDateLocal, out _));
    }

    [TestMethod]
    public void Legacy_dynamic_amount_bounds_are_preserved()
    {
        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            new DateTime(2081, 5, 12),
            1m,
            "Positive",
            false,
            false,
            "ManualAdd",
            out CharacterCareerKarmaExpenseEntry? positive));
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryEdit(
            positive!, 0m, positive!.Reason, positive.ExpenseDateLocal, out _));

        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            new DateTime(2081, 5, 12),
            0m,
            "Zero",
            false,
            false,
            "ManualAdd",
            out CharacterCareerKarmaExpenseEntry? zero));
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryEdit(
            zero!, -1m, zero!.Reason, zero.ExpenseDateLocal, out _));

        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            new DateTime(2081, 5, 12),
            -1m,
            "Negative",
            false,
            false,
            "ManualSubtract",
            out CharacterCareerKarmaExpenseEntry? negative));
        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryEdit(
            negative!, 1m, negative!.Reason, negative.ExpenseDateLocal, out CharacterCareerKarmaExpenseEditResult? crossed));
        Assert.AreEqual(2, crossed!.KarmaDelta);
        Assert.AreEqual(1m, crossed.Expense.Amount);
    }

    [TestMethod]
    public void Invalid_identity_bounds_and_incoherent_or_overflowing_entries_fail_closed()
    {
        DateTime date = new(2081, 5, 12);
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            Guid.Empty, date, 1m, "Bad", false, false, "ManualAdd", out _));
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId, date, CharacterCareerKarmaExpenseEditRules.MaximumAmount + 1m, "Bad", false, false, "ManualAdd", out _));
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId, date, -CharacterCareerKarmaExpenseEditRules.MaximumAmount - 1m, "Bad", false, false, "ManualSubtract", out _));
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            CharacterCareerKarmaExpenseEditRules.MinimumDate.AddTicks(-1),
            1m,
            "Bad",
            false,
            false,
            "ManualAdd",
            out _));
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            date,
            1m,
            new string('x', CharacterCareerKarmaExpenseEditRules.MaximumReasonLength + 1),
            false,
            false,
            "ManualAdd",
            out _));

        CharacterCareerKarmaExpenseEntry incoherent = new(
            ExpenseId, date, 1m, "Bad", false, false, "ImproveAttribute", AmountEditable: true);
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryEdit(
            incoherent, 2m, incoherent.Reason, incoherent.ExpenseDateLocal, out _));

        CharacterCareerKarmaExpenseEntry overflowing = new(
            ExpenseId, date, decimal.MaxValue, "Bad", false, false, "ManualAdd", AmountEditable: true);
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryEdit(
            overflowing, 1m, overflowing.Reason, overflowing.ExpenseDateLocal, out _));
    }
}
