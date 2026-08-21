namespace Chummer.Contracts.Characters;

/// <summary>
/// Exact saved/profile inputs behind CharacterCareer.cmdNuyenGained and cmdNuyenSpent.
/// </summary>
public sealed record CharacterCareerManualNuyenState(
    int AvailableKarma,
    decimal AvailableNuyen,
    decimal NuyenPerKarmaWorkingForPeople,
    decimal NuyenPerKarmaWorkingForMan);

public enum CharacterCareerManualNuyenAction
{
    Gain,
    Spend
}

public sealed record CharacterCareerManualNuyenQuote(
    CharacterCareerManualNuyenAction Action,
    int EnteredAmount,
    decimal Percent,
    bool KarmaNuyenExchange,
    decimal NuyenExpenseAmount,
    decimal UpdatedNuyen,
    int KarmaExpenseAmount,
    int UpdatedKarma);

public static class CharacterCareerManualNuyenRules
{
    public const int MinimumAmount = 1;
    public const int MaximumAmount = 9_999_999;
    public const decimal MinimumPercent = 0m;
    public const decimal MaximumPercent = 1_000m;
    public const int MaximumPercentDecimalPlaces = 2;
    public const int MaximumReasonLength = 32_767;

    public static bool TryProject(
        bool created,
        int availableKarma,
        decimal availableNuyen,
        decimal? nuyenPerKarmaWorkingForPeople,
        decimal? nuyenPerKarmaWorkingForMan,
        out CharacterCareerManualNuyenState? state)
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

        state = new CharacterCareerManualNuyenState(
            availableKarma,
            availableNuyen,
            nuyenPerKarmaWorkingForPeople.Value,
            nuyenPerKarmaWorkingForMan.Value);
        return true;
    }

    public static bool TryQuote(
        CharacterCareerManualNuyenState state,
        CharacterCareerManualNuyenAction action,
        int enteredAmount,
        decimal percent,
        bool karmaNuyenExchange,
        out CharacterCareerManualNuyenQuote? quote)
    {
        ArgumentNullException.ThrowIfNull(state);
        quote = null;
        if (enteredAmount is < MinimumAmount or > MaximumAmount
            || percent is < MinimumPercent or > MaximumPercent
            || decimal.Round(percent, MaximumPercentDecimalPlaces) != percent
            || action is not (CharacterCareerManualNuyenAction.Gain or CharacterCareerManualNuyenAction.Spend))
        {
            return false;
        }

        try
        {
            decimal nuyenAmount = karmaNuyenExchange
                ? enteredAmount
                : checked(enteredAmount * percent / 100m);
            if (action == CharacterCareerManualNuyenAction.Spend
                && nuyenAmount > state.AvailableNuyen)
            {
                return false;
            }

            int karmaExpenseAmount = 0;
            if (karmaNuyenExchange)
            {
                // CreateExpense validates every Nuyen exchange against the Working for the People rate,
                // even though Nuyen gained converts Karma with the Working for the Man rate.
                decimal exchangeUnits = nuyenAmount / state.NuyenPerKarmaWorkingForPeople;
                if (decimal.Floor(exchangeUnits) != decimal.Ceiling(exchangeUnits))
                {
                    return false;
                }

                decimal conversionRate = action == CharacterCareerManualNuyenAction.Gain
                    ? state.NuyenPerKarmaWorkingForMan
                    : state.NuyenPerKarmaWorkingForPeople;
                int convertedKarma = decimal.ToInt32(nuyenAmount / conversionRate);
                karmaExpenseAmount = action == CharacterCareerManualNuyenAction.Gain
                    ? checked(-convertedKarma)
                    : convertedKarma;
            }

            decimal nuyenExpenseAmount = action == CharacterCareerManualNuyenAction.Gain
                ? nuyenAmount
                : checked(-nuyenAmount);
            quote = new CharacterCareerManualNuyenQuote(
                action,
                enteredAmount,
                percent,
                karmaNuyenExchange,
                nuyenExpenseAmount,
                checked(state.AvailableNuyen + nuyenExpenseAmount),
                karmaExpenseAmount,
                checked(state.AvailableKarma + karmaExpenseAmount));
            return true;
        }
        catch (Exception exception) when (exception is OverflowException or DivideByZeroException)
        {
            return false;
        }
    }
}
