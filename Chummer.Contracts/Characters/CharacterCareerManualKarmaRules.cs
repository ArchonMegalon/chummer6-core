namespace Chummer.Contracts.Characters;

/// <summary>
/// Exact saved/profile inputs behind CharacterCareer.cmdKarmaGained and cmdKarmaSpent.
/// </summary>
public sealed record CharacterCareerManualKarmaState(
    int AvailableKarma,
    decimal AvailableNuyen,
    decimal NuyenPerKarmaWorkingForPeople,
    decimal NuyenPerKarmaWorkingForMan);

public enum CharacterCareerManualKarmaAction
{
    Gain,
    Spend
}

public sealed record CharacterCareerManualKarmaQuote(
    CharacterCareerManualKarmaAction Action,
    int Amount,
    bool KarmaNuyenExchange,
    int KarmaExpenseAmount,
    int UpdatedKarma,
    decimal NuyenExpenseAmount,
    decimal NuyenBalanceDelta,
    decimal UpdatedNuyen);

public static class CharacterCareerManualKarmaRules
{
    public const int MinimumAmount = 1;
    public const int MaximumAmount = 9_999_999;
    public const int MaximumReasonLength = 32_767;

    public static bool TryProject(
        bool created,
        int availableKarma,
        decimal availableNuyen,
        decimal? nuyenPerKarmaWorkingForPeople,
        decimal? nuyenPerKarmaWorkingForMan,
        out CharacterCareerManualKarmaState? state)
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

        state = new CharacterCareerManualKarmaState(
            availableKarma,
            availableNuyen,
            nuyenPerKarmaWorkingForPeople.Value,
            nuyenPerKarmaWorkingForMan.Value);
        return true;
    }

    public static bool TryQuote(
        CharacterCareerManualKarmaState state,
        CharacterCareerManualKarmaAction action,
        int amount,
        bool karmaNuyenExchange,
        out CharacterCareerManualKarmaQuote? quote)
    {
        ArgumentNullException.ThrowIfNull(state);
        quote = null;
        if (amount is < MinimumAmount or > MaximumAmount
            || (action == CharacterCareerManualKarmaAction.Spend && amount > state.AvailableKarma)
            || action is not (CharacterCareerManualKarmaAction.Gain or CharacterCareerManualKarmaAction.Spend))
        {
            return false;
        }

        try
        {
            int karmaExpenseAmount = action == CharacterCareerManualKarmaAction.Gain
                ? amount
                : -amount;
            int updatedKarma = checked(state.AvailableKarma + karmaExpenseAmount);
            decimal nuyenExpenseAmount = 0m;
            decimal nuyenBalanceDelta = 0m;
            if (karmaNuyenExchange)
            {
                if (action == CharacterCareerManualKarmaAction.Gain)
                {
                    // Preserve Chummer5's exact Working for the People asymmetry:
                    // the expense uses P while the actual balance mutation uses M.
                    nuyenExpenseAmount = checked(-amount * state.NuyenPerKarmaWorkingForPeople);
                    nuyenBalanceDelta = checked(-amount * state.NuyenPerKarmaWorkingForMan);
                }
                else
                {
                    nuyenExpenseAmount = checked(amount * state.NuyenPerKarmaWorkingForMan);
                    nuyenBalanceDelta = nuyenExpenseAmount;
                }
            }

            quote = new CharacterCareerManualKarmaQuote(
                action,
                amount,
                karmaNuyenExchange,
                karmaExpenseAmount,
                updatedKarma,
                nuyenExpenseAmount,
                nuyenBalanceDelta,
                checked(state.AvailableNuyen + nuyenBalanceDelta));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
