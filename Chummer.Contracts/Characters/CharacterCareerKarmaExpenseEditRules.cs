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
    bool KarmaUndoTypeElementPresent,
    string? RawKarmaUndoType,
    bool AmountEditable);

public sealed record CharacterCareerKarmaExpenseEditResult(
    CharacterCareerKarmaExpenseEntry Expense,
    int KarmaDelta);

public static class CharacterCareerKarmaExpenseEditRules
{
    public const decimal MaximumAmount = 9_999_999m;
    public const int MaximumReasonLength = 32_767;
    public static readonly DateTime MinimumDate = new(1753, 1, 1);
    public static readonly DateTime MaximumDate = new(9998, 12, 31, 23, 59, 59);

    public static bool IsAmountEditable(
        bool karmaUndoTypeElementPresent,
        string? rawKarmaUndoType)
    {
        Chummer5KarmaExpenseType loaded = LoadKarmaUndoType(
            karmaUndoTypeElementPresent,
            rawKarmaUndoType);
        return loaded is Chummer5KarmaExpenseType.ManualAdd
            or Chummer5KarmaExpenseType.ManualSubtract;
    }

    public static bool TryCreateEntry(
        Guid expenseId,
        DateTime expenseDateLocal,
        decimal amount,
        string? reason,
        bool refund,
        bool forceCareerVisible,
        bool karmaUndoTypeElementPresent,
        string? rawKarmaUndoType,
        out CharacterCareerKarmaExpenseEntry? entry)
    {
        entry = null;
        string normalizedReason = reason ?? string.Empty;
        if (expenseId == Guid.Empty
            || (!karmaUndoTypeElementPresent && rawKarmaUndoType is not null)
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
            karmaUndoTypeElementPresent,
            karmaUndoTypeElementPresent ? rawKarmaUndoType ?? string.Empty : null,
            IsAmountEditable(karmaUndoTypeElementPresent, rawKarmaUndoType));
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
        bool amountEditable = IsAmountEditable(
            current.KarmaUndoTypeElementPresent,
            current.RawKarmaUndoType);
        if (current.ExpenseId == Guid.Empty
            || (!current.KarmaUndoTypeElementPresent && current.RawKarmaUndoType is not null)
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

    private static Chummer5KarmaExpenseType LoadKarmaUndoType(
        bool karmaUndoTypeElementPresent,
        string? rawKarmaUndoType)
    {
        if (!karmaUndoTypeElementPresent)
        {
            return Chummer5KarmaExpenseType.ImproveAttribute;
        }

        return Enum.TryParse(rawKarmaUndoType, out Chummer5KarmaExpenseType loaded)
            ? loaded
            : Chummer5KarmaExpenseType.ManualAdd;
    }

    // Keep these source ordinals exact: ExpenseUndo.Load accepts numeric enum text,
    // including undefined in-range values, before CharacterCareer checks the two
    // manual values. Enum.TryParse failure falls back to ManualAdd in Chummer5.
    private enum Chummer5KarmaExpenseType
    {
        ImproveAttribute = 0,
        AddQuality = 1,
        ImproveSkillGroup = 2,
        AddSkill = 3,
        ImproveSkill = 4,
        SkillSpec = 5,
        AddMartialArt = 6,
        AddSpell = 7,
        AddComplexForm = 8,
        AddMetamagic = 9,
        ImproveInitiateGrade = 10,
        RemoveQuality = 11,
        ManualAdd = 12,
        ManualSubtract = 13,
        BindFocus = 14,
        JoinGroup = 15,
        LeaveGroup = 16,
        QuickeningMetamagic = 17,
        AddPowerPoint = 18,
        AddSpecialization = 19,
        AddAIProgram = 20,
        AddAIAdvancedProgram = 21,
        AddCritterPower = 22,
        SpiritFettering = 23,
        AddMartialArtTechnique = 24
    }
}
