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
        if (!TryResolveAmountEditable(
                karmaUndoTypeElementPresent,
                rawKarmaUndoType,
                out bool amountEditable))
        {
            return false;
        }

        return amountEditable;
    }

    private static bool TryResolveAmountEditable(
        bool karmaUndoTypeElementPresent,
        string? rawKarmaUndoType,
        out bool amountEditable)
    {
        amountEditable = false;
        if (karmaUndoTypeElementPresent == (rawKarmaUndoType is null))
        {
            return false;
        }

        Chummer5KarmaExpenseType loaded = LoadKarmaUndoType(
            karmaUndoTypeElementPresent,
            rawKarmaUndoType);
        amountEditable = loaded is Chummer5KarmaExpenseType.ManualAdd
            or Chummer5KarmaExpenseType.ManualSubtract;
        return true;
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
        string? normalizedRawKarmaUndoType = karmaUndoTypeElementPresent
            ? rawKarmaUndoType ?? string.Empty
            : rawKarmaUndoType;
        CharacterCareerKarmaExpenseEntry candidate = new(
            expenseId,
            DateTime.SpecifyKind(expenseDateLocal, DateTimeKind.Unspecified),
            amount,
            reason ?? string.Empty,
            refund,
            forceCareerVisible,
            karmaUndoTypeElementPresent,
            normalizedRawKarmaUndoType,
            IsAmountEditable(karmaUndoTypeElementPresent, normalizedRawKarmaUndoType));
        if (!IsCoherentEntry(candidate))
        {
            return false;
        }

        entry = candidate;
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
        if (!IsCoherentEntry(current))
        {
            return false;
        }

        string normalizedReason = reason ?? string.Empty;
        DateTime normalizedDate = DateTime.SpecifyKind(expenseDateLocal, DateTimeKind.Unspecified);
        decimal minimumAmount = current.Amount < 0m
            ? -MaximumAmount
            : current.Amount == 0m
                ? 0m
                : 1m;
        bool amountEditable = current.AmountEditable;
        if (normalizedReason.Length > MaximumReasonLength
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
            if (!IsCoherentEntry(updated))
            {
                return false;
            }

            result = new CharacterCareerKarmaExpenseEditResult(updated, karmaDelta);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsCoherentEntry(CharacterCareerKarmaExpenseEntry entry)
    {
        return entry.ExpenseId != Guid.Empty
            && entry.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
            && entry.ExpenseDateLocal >= MinimumDate
            && entry.ExpenseDateLocal <= MaximumDate
            && entry.Amount >= -MaximumAmount
            && entry.Amount <= MaximumAmount
            && entry.Reason is not null
            && entry.Reason.Length <= MaximumReasonLength
            && TryResolveAmountEditable(
                entry.KarmaUndoTypeElementPresent,
                entry.RawKarmaUndoType,
                out bool amountEditable)
            && entry.AmountEditable == amountEditable;
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
