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
            karmaUndoTypeElementPresent: true,
            rawKarmaUndoType: "ManualAdd",
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
        Assert.IsTrue(gained.Expense.KarmaUndoTypeElementPresent);
        Assert.AreEqual("ManualAdd", gained.Expense.RawKarmaUndoType);

        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            new DateTime(2081, 5, 12),
            -10.75m,
            "Training",
            refund: true,
            forceCareerVisible: false,
            karmaUndoTypeElementPresent: true,
            rawKarmaUndoType: "ManualSubtract",
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
            karmaUndoTypeElementPresent: true,
            rawKarmaUndoType: "ManualAdd",
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
            karmaUndoTypeElementPresent: true,
            rawKarmaUndoType: "ManualAdd",
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
    public void Absent_karmatype_stays_default_locked_but_present_blank_falls_back_to_manual_add()
    {
        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            new DateTime(2081, 5, 12),
            -5m,
            "Attribute",
            refund: false,
            forceCareerVisible: true,
            karmaUndoTypeElementPresent: false,
            rawKarmaUndoType: null,
            out CharacterCareerKarmaExpenseEntry? absent));
        Assert.IsFalse(absent!.KarmaUndoTypeElementPresent);
        Assert.IsNull(absent.RawKarmaUndoType);
        Assert.IsFalse(absent.AmountEditable);
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryEdit(
            absent, -4m, "Attribute", absent.ExpenseDateLocal, out _));
        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryEdit(
            absent,
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
            "Present blank",
            refund: false,
            forceCareerVisible: false,
            karmaUndoTypeElementPresent: true,
            rawKarmaUndoType: string.Empty,
            out CharacterCareerKarmaExpenseEntry? presentBlank));
        Assert.IsTrue(presentBlank!.KarmaUndoTypeElementPresent);
        Assert.AreEqual(string.Empty, presentBlank.RawKarmaUndoType);
        Assert.IsTrue(presentBlank.AmountEditable);
        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryEdit(
            presentBlank,
            6m,
            presentBlank.Reason,
            presentBlank.ExpenseDateLocal,
            out CharacterCareerKarmaExpenseEditResult? blankEdit));
        Assert.AreEqual(1, blankEdit!.KarmaDelta);
    }

    [TestMethod]
    public void Present_wrong_case_and_unknown_names_use_Chummer5_manual_add_fallback()
    {
        foreach (string raw in new[] { "manualadd", "MANUALSUBTRACT", "NotARealKarmaType" })
        {
            Assert.IsTrue(
                CharacterCareerKarmaExpenseEditRules.IsAmountEditable(
                    karmaUndoTypeElementPresent: true,
                    rawKarmaUndoType: raw),
                $"Present source text '{raw}' should load as Chummer5 ManualAdd fallback.");
            Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
                ExpenseId,
                new DateTime(2081, 5, 12),
                5m,
                "Fallback",
                false,
                false,
                karmaUndoTypeElementPresent: true,
                rawKarmaUndoType: raw,
                out CharacterCareerKarmaExpenseEntry? entry));
            Assert.AreEqual(raw, entry!.RawKarmaUndoType);
            Assert.IsTrue(entry.AmountEditable);
            Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryEdit(
                entry,
                6m,
                "Edited fallback",
                entry.ExpenseDateLocal,
                out CharacterCareerKarmaExpenseEditResult? edited));
            Assert.IsTrue(edited!.Expense.KarmaUndoTypeElementPresent);
            Assert.AreEqual(raw, edited.Expense.RawKarmaUndoType);
        }
    }

    [TestMethod]
    public void Canonical_and_numeric_karmatype_values_follow_case_sensitive_Enum_TryParse()
    {
        (string Raw, bool Editable)[] cases =
        [
            ("ManualAdd", true),
            ("ManualSubtract", true),
            ("ImproveAttribute", false),
            ("12", true),
            ("13", true),
            ("0", false),
            ("999", false),
            ("-1", false),
            ("2147483648", true)
        ];

        foreach ((string raw, bool expected) in cases)
        {
            Assert.AreEqual(
                expected,
                CharacterCareerKarmaExpenseEditRules.IsAmountEditable(
                    karmaUndoTypeElementPresent: true,
                    rawKarmaUndoType: raw),
                $"Unexpected Chummer5 Enum.TryParse authority for '{raw}'.");
        }
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
            true,
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
            true,
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
            true,
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
            Guid.Empty, date, 1m, "Bad", false, false, true, "ManualAdd", out _));
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId, date, CharacterCareerKarmaExpenseEditRules.MaximumAmount + 1m, "Bad", false, false, true, "ManualAdd", out _));
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId, date, -CharacterCareerKarmaExpenseEditRules.MaximumAmount - 1m, "Bad", false, false, true, "ManualSubtract", out _));
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            CharacterCareerKarmaExpenseEditRules.MinimumDate.AddTicks(-1),
            1m,
            "Bad",
            false,
            false,
            true,
            "ManualAdd",
            out _));
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            date,
            1m,
            new string('x', CharacterCareerKarmaExpenseEditRules.MaximumReasonLength + 1),
            false,
            false,
            true,
            "ManualAdd",
            out _));
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId, date, 1m, "Bad", false, false, false, "ManualAdd", out _));

        CharacterCareerKarmaExpenseEntry incoherent = new(
            ExpenseId,
            date,
            1m,
            "Bad",
            false,
            false,
            KarmaUndoTypeElementPresent: true,
            RawKarmaUndoType: "ImproveAttribute",
            AmountEditable: true);
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryEdit(
            incoherent, 2m, incoherent.Reason, incoherent.ExpenseDateLocal, out _));

        CharacterCareerKarmaExpenseEntry overflowing = new(
            ExpenseId,
            date,
            decimal.MaxValue,
            "Bad",
            false,
            false,
            KarmaUndoTypeElementPresent: true,
            RawKarmaUndoType: "ManualAdd",
            AmountEditable: true);
        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryEdit(
            overflowing, 1m, overflowing.Reason, overflowing.ExpenseDateLocal, out _));
    }

    [TestMethod]
    public void Factory_impossible_current_entries_cannot_be_repaired_through_edit()
    {
        DateTime date = new(2081, 5, 12);
        Assert.IsTrue(CharacterCareerKarmaExpenseEditRules.TryCreateEntry(
            ExpenseId,
            date,
            5m,
            "Valid",
            false,
            false,
            karmaUndoTypeElementPresent: true,
            rawKarmaUndoType: "ManualAdd",
            out CharacterCareerKarmaExpenseEntry? valid));

        CharacterCareerKarmaExpenseEntry[] impossible =
        [
            valid! with { RawKarmaUndoType = null },
            valid! with
            {
                KarmaUndoTypeElementPresent = false,
                RawKarmaUndoType = "ManualAdd",
                AmountEditable = false
            },
            valid! with
            {
                ExpenseDateLocal = CharacterCareerKarmaExpenseEditRules.MinimumDate.AddTicks(-1)
            },
            valid! with
            {
                ExpenseDateLocal = CharacterCareerKarmaExpenseEditRules.MaximumDate.AddTicks(1)
            },
            valid! with
            {
                ExpenseDateLocal = DateTime.SpecifyKind(date, DateTimeKind.Utc)
            },
            valid! with { Reason = null! },
            valid! with
            {
                Reason = new string('x', CharacterCareerKarmaExpenseEditRules.MaximumReasonLength + 1)
            }
        ];

        Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.IsAmountEditable(
            karmaUndoTypeElementPresent: true,
            rawKarmaUndoType: null));

        foreach (CharacterCareerKarmaExpenseEntry current in impossible)
        {
            Assert.IsFalse(CharacterCareerKarmaExpenseEditRules.TryEdit(
                current,
                6m,
                "Repaired",
                date,
                out _));
        }
    }
}
