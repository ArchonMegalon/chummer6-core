namespace Chummer.Contracts.Characters;

/// <summary>
/// Stable identity for the four CharacterCareer handlers that open CreateExpense.
/// </summary>
public enum CharacterCareerCreateExpenseOperation
{
    KarmaGained,
    KarmaSpent,
    NuyenGained,
    NuyenSpent
}

/// <summary>
/// Exact outcome of CreateExpense.cmdOK_Click. The Nuyen exchange no-op is
/// intentional: Chummer5 does not close or copy any field for an integral exchange.
/// </summary>
public enum CharacterCareerCreateExpenseDialogOutcome
{
    Commit,
    NuyenExchangeValidationRejected,
    NuyenExchangeCanonicalNoOp,
    CallerBalanceValidationRejected
}

public sealed record CharacterCareerCreateExpenseState(
    int AvailableKarma,
    decimal AvailableNuyen,
    decimal NuyenPerKarmaWorkingForPeople,
    decimal NuyenPerKarmaWorkingForMan);

public static class CharacterCareerCreateExpenseRules
{
    public const int MinimumAmount = 1;
    public const int MaximumAmount = 9_999_999;
    public const decimal MinimumPercent = 0m;
    public const decimal MaximumPercent = 1_000m;
    public const int MaximumPercentDecimalPlaces = 2;
    public const int MaximumReasonLength = 32_767;
    public const string DefaultReason = "Mission Reward";
    public static readonly DateTime MinimumDate = new(1753, 1, 1);
    public static readonly DateTime MaximumDate = new(9998, 12, 31, 23, 59, 59);

    public static bool IsNuyen(CharacterCareerCreateExpenseOperation operation)
        => operation is CharacterCareerCreateExpenseOperation.NuyenGained
            or CharacterCareerCreateExpenseOperation.NuyenSpent;

    public static bool IsSpend(CharacterCareerCreateExpenseOperation operation)
        => operation is CharacterCareerCreateExpenseOperation.KarmaSpent
            or CharacterCareerCreateExpenseOperation.NuyenSpent;

    public static string ExchangeReason(CharacterCareerCreateExpenseOperation operation)
        => operation switch
        {
            CharacterCareerCreateExpenseOperation.KarmaGained => "Working for the People",
            CharacterCareerCreateExpenseOperation.KarmaSpent => "Working for the Man",
            CharacterCareerCreateExpenseOperation.NuyenGained => "Working for the Man",
            CharacterCareerCreateExpenseOperation.NuyenSpent => "Working for the People",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    public static bool TryProject(
        bool created,
        int availableKarma,
        decimal availableNuyen,
        decimal? nuyenPerKarmaWorkingForPeople,
        decimal? nuyenPerKarmaWorkingForMan,
        out CharacterCareerCreateExpenseState? state)
    {
        state = null;
        if (!created
            || !nuyenPerKarmaWorkingForPeople.HasValue
            || !nuyenPerKarmaWorkingForMan.HasValue
            || nuyenPerKarmaWorkingForPeople.Value <= 0m
            || nuyenPerKarmaWorkingForMan.Value <= 0m)
        {
            return false;
        }

        state = new CharacterCareerCreateExpenseState(
            availableKarma,
            availableNuyen,
            nuyenPerKarmaWorkingForPeople.Value,
            nuyenPerKarmaWorkingForMan.Value);
        return true;
    }

    public static bool TryEvaluateDialog(
        CharacterCareerCreateExpenseState state,
        CharacterCareerCreateExpenseOperation operation,
        int amount,
        decimal percent,
        bool karmaNuyenExchange,
        out CharacterCareerCreateExpenseDialogOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(state);
        outcome = default;
        if (!Enum.IsDefined(operation)
            || amount is < MinimumAmount or > MaximumAmount
            || (IsNuyen(operation)
                && (percent is < MinimumPercent or > MaximumPercent
                    || decimal.Round(percent, MaximumPercentDecimalPlaces) != percent)))
        {
            return false;
        }

        if (karmaNuyenExchange && IsNuyen(operation))
        {
            decimal dividend;
            try
            {
                dividend = amount / state.NuyenPerKarmaWorkingForPeople;
            }
            catch (DivideByZeroException)
            {
                return false;
            }
            outcome = decimal.Floor(dividend) == decimal.Ceiling(dividend)
                ? CharacterCareerCreateExpenseDialogOutcome.NuyenExchangeCanonicalNoOp
                : CharacterCareerCreateExpenseDialogOutcome.NuyenExchangeValidationRejected;
            return true;
        }

        if (IsNuyen(operation))
        {
            decimal effectiveAmount;
            try
            {
                effectiveAmount = checked(amount * percent / 100m);
            }
            catch (OverflowException)
            {
                return false;
            }
            if (operation == CharacterCareerCreateExpenseOperation.NuyenSpent
                && effectiveAmount > state.AvailableNuyen)
            {
                outcome = CharacterCareerCreateExpenseDialogOutcome.CallerBalanceValidationRejected;
                return true;
            }
            CharacterCareerManualNuyenState nuyen = new(
                state.AvailableKarma,
                state.AvailableNuyen,
                state.NuyenPerKarmaWorkingForPeople,
                state.NuyenPerKarmaWorkingForMan);
            CharacterCareerManualNuyenAction action = operation == CharacterCareerCreateExpenseOperation.NuyenGained
                ? CharacterCareerManualNuyenAction.Gain
                : CharacterCareerManualNuyenAction.Spend;
            if (!CharacterCareerManualNuyenRules.TryQuote(
                    nuyen,
                    action,
                    amount,
                    percent,
                    karmaNuyenExchange: false,
                    out _))
            {
                return false;
            }
        }
        else
        {
            if (operation == CharacterCareerCreateExpenseOperation.KarmaSpent
                && amount > state.AvailableKarma)
            {
                outcome = CharacterCareerCreateExpenseDialogOutcome.CallerBalanceValidationRejected;
                return true;
            }
            CharacterCareerManualKarmaState karma = new(
                state.AvailableKarma,
                state.AvailableNuyen,
                state.NuyenPerKarmaWorkingForPeople,
                state.NuyenPerKarmaWorkingForMan);
            CharacterCareerManualKarmaAction action = operation == CharacterCareerCreateExpenseOperation.KarmaGained
                ? CharacterCareerManualKarmaAction.Gain
                : CharacterCareerManualKarmaAction.Spend;
            if (!CharacterCareerManualKarmaRules.TryQuote(
                    karma,
                    action,
                    amount,
                    karmaNuyenExchange,
                    out _))
            {
                return false;
            }
        }

        outcome = CharacterCareerCreateExpenseDialogOutcome.Commit;
        return true;
    }
}
