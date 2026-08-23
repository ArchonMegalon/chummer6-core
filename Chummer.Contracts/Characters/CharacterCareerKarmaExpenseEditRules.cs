namespace Chummer.Contracts.Characters;

/// <summary>
/// Stable saved identity and exact editable fields behind CharacterCareer's Karma expense editor.
/// </summary>
public sealed record CharacterCareerKarmaExpenseEntry(
    Guid ExpenseId,
    DateTime ExpenseDateLocal,
    decimal Amount,
    string Reason,
    bool Refund,
    bool ForceCareerVisible,
    string KarmaUndoType,
    bool AmountEditable);

public sealed record CharacterCareerKarmaExpenseEditResult(
    CharacterCareerKarmaExpenseEntry Expense,
    int KarmaDelta);

public static class CharacterCareerKarmaExpenseEditRules
{
    public const decimal MaximumAmount = 9_999_999m;
    public const int MaximumReasonLength = 32_767;
    public const string DefaultKarmaUndoType = "ImproveAttribute";
    public static readonly DateTime MinimumDate = new(1753, 1, 1);
    public static readonly DateTime MaximumDate = new(9998, 12, 31, 23, 59, 59);

    public static bool IsAmountEditable(string? karmaUndoType)
        => string.Equals(karmaUndoType, "ManualAdd", StringComparison.Ordinal)
            || string.Equals(karmaUndoType, "ManualSubtract", StringComparison.Ordinal);

    public static bool TryCreateEntry(
        Guid expenseId,
        DateTime expenseDateLocal,
        decimal amount,
        string? reason,
        bool refund,
        bool forceCareerVisible,
        string? karmaUndoType,
        out CharacterCareerKarmaExpenseEntry? entry)
    {
        entry = null;
        string normalizedReason = reason ?? string.Empty;
        string normalizedUndoType = string.IsNullOrWhiteSpace(karmaUndoType)
            ? DefaultKarmaUndoType
            : karmaUndoType.Trim();
        if (expenseId == Guid.Empty
            || expenseDateLocal < MinimumDate
            || expenseDateLocal > MaximumDate
            || amount < -MaximumAmount
            || amount > MaximumAmount
            || normalizedReason.Length > MaximumReasonLength)
        {
            return false;
        }

        entry = new CharacterCareerKarmaExpenseEntry(
            expenseId,
            DateTime.SpecifyKind(expenseDateLocal, DateTimeKind.Unspecified),
            amount,
            normalizedReason,
            refund,
            forceCareerVisible,
            normalizedUndoType,
            IsAmountEditable(normalizedUndoType));
        return true;
    }

    public static bool TryEdit(
        CharacterCareerKarmaExpenseEntry current,
        decimal amount,
        string? reason,
        DateTime expenseDateLocal,
        out CharacterCareerKarmaExpenseEditResult? result)
    {
        ArgumentNullException.ThrowIfNull(current);
        result = null;
        string normalizedReason = reason ?? string.Empty;
        DateTime normalizedDate = DateTime.SpecifyKind(expenseDateLocal, DateTimeKind.Unspecified);
        decimal minimumAmount = current.Amount < 0m
            ? -MaximumAmount
            : current.Amount == 0m
                ? 0m
                : 1m;
        bool amountEditable = IsAmountEditable(current.KarmaUndoType);
        if (current.ExpenseId == Guid.Empty
            || current.Amount < -MaximumAmount
            || current.Amount > MaximumAmount
            || current.AmountEditable != amountEditable
            || normalizedReason.Length > MaximumReasonLength
            || normalizedDate < MinimumDate
            || normalizedDate > MaximumDate
            || amount < minimumAmount
            || amount > MaximumAmount
            || (!amountEditable && amount != current.Amount))
        {
            return false;
        }

        try
        {
            int oldAmount = decimal.ToInt32(current.Amount);
            int newAmount = decimal.ToInt32(amount);
            int karmaDelta = 0;
            decimal savedAmount = current.Amount;
            if (amountEditable && oldAmount != newAmount)
            {
                karmaDelta = checked(newAmount - oldAmount);
                savedAmount = newAmount;
            }

            CharacterCareerKarmaExpenseEntry updated = current with
            {
                ExpenseDateLocal = normalizedDate,
                Amount = savedAmount,
                Reason = normalizedReason
            };
            result = new CharacterCareerKarmaExpenseEditResult(updated, karmaDelta);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
