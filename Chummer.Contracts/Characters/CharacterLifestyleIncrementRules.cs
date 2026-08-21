namespace Chummer.Contracts.Characters;

public enum CharacterLifestyleIncrementAction
{
    SetCreation,
    IncreaseCareer,
    DecreaseCareer
}

public enum CharacterLifestyleIncrementUnit
{
    Day,
    Week,
    Month
}

public sealed record CharacterLifestyleIncrementState(
    Guid LifestyleId,
    int Increments,
    CharacterLifestyleIncrementUnit Unit,
    bool CareerMode,
    decimal Nuyen,
    bool NuyenExact,
    decimal TotalIncrementCost,
    bool TotalIncrementCostExact,
    string DisplayName);

public sealed record CharacterLifestyleIncrementQuote(
    CharacterLifestyleIncrementAction Action,
    int UpdatedIncrements,
    decimal NuyenDelta,
    bool Exact,
    string? Blocker = null);

public static class CharacterLifestyleIncrementRules
{
    public const int CreationMinimum = 1;
    public const int CreationMaximum = 100;

    public static CharacterLifestyleIncrementUnit ParseUnit(string? value)
        => value?.Trim().ToUpperInvariant() switch
        {
            "DAY" => CharacterLifestyleIncrementUnit.Day,
            "WEEK" => CharacterLifestyleIncrementUnit.Week,
            _ => CharacterLifestyleIncrementUnit.Month
        };

    public static int IncrementsRequiredForPermanent(CharacterLifestyleIncrementUnit unit)
        => unit switch
        {
            CharacterLifestyleIncrementUnit.Day => 3_044,
            CharacterLifestyleIncrementUnit.Week => 435,
            _ => 100
        };

    public static CharacterLifestyleIncrementQuote Quote(
        CharacterLifestyleIncrementState state,
        CharacterLifestyleIncrementAction action,
        int? requestedIncrements = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.LifestyleId == Guid.Empty || string.IsNullOrWhiteSpace(state.DisplayName))
        {
            return Blocked(action, state.Increments, "Lifestyle increment editing requires exact stable identity and display name authority.");
        }

        try
        {
            return action switch
            {
                CharacterLifestyleIncrementAction.SetCreation => QuoteCreation(state, requestedIncrements),
                CharacterLifestyleIncrementAction.IncreaseCareer => QuoteCareerIncrease(state),
                CharacterLifestyleIncrementAction.DecreaseCareer => QuoteCareerDecrease(state),
                _ => Blocked(action, state.Increments, "Unsupported Lifestyle increment action.")
            };
        }
        catch (OverflowException)
        {
            return Blocked(action, state.Increments, "Lifestyle increment arithmetic overflowed.");
        }
    }

    private static CharacterLifestyleIncrementQuote QuoteCreation(
        CharacterLifestyleIncrementState state,
        int? requestedIncrements)
    {
        if (state.CareerMode)
        {
            return Blocked(CharacterLifestyleIncrementAction.SetCreation, state.Increments, "Direct Lifestyle interval entry is creation-only.");
        }
        if (requestedIncrements is not >= CreationMinimum or > CreationMaximum)
        {
            return Blocked(
                CharacterLifestyleIncrementAction.SetCreation,
                state.Increments,
                $"Creation Lifestyle intervals must be between {CreationMinimum} and {CreationMaximum}.");
        }
        if (!state.TotalIncrementCostExact || state.TotalIncrementCost < 0m)
        {
            return Blocked(
                CharacterLifestyleIncrementAction.SetCreation,
                state.Increments,
                "Exact saved total interval cost authority is required to update derived Lifestyle totals.");
        }

        return new CharacterLifestyleIncrementQuote(
            CharacterLifestyleIncrementAction.SetCreation,
            requestedIncrements.Value,
            0m,
            Exact: true);
    }

    private static CharacterLifestyleIncrementQuote QuoteCareerIncrease(CharacterLifestyleIncrementState state)
    {
        if (!state.CareerMode)
        {
            return Blocked(CharacterLifestyleIncrementAction.IncreaseCareer, state.Increments, "Purchasing a Lifestyle interval is career-only.");
        }
        if (!state.NuyenExact || !state.TotalIncrementCostExact || state.Nuyen < 0m || state.TotalIncrementCost < 0m)
        {
            return Blocked(CharacterLifestyleIncrementAction.IncreaseCareer, state.Increments, "Exact Nuyen and saved total interval cost authority is required.");
        }
        if (state.TotalIncrementCost > state.Nuyen)
        {
            return Blocked(CharacterLifestyleIncrementAction.IncreaseCareer, state.Increments, "The runner does not have enough Nuyen.");
        }

        return new CharacterLifestyleIncrementQuote(
            CharacterLifestyleIncrementAction.IncreaseCareer,
            checked(state.Increments + 1),
            -state.TotalIncrementCost,
            Exact: true);
    }

    private static CharacterLifestyleIncrementQuote QuoteCareerDecrease(CharacterLifestyleIncrementState state)
    {
        if (!state.CareerMode)
        {
            return Blocked(CharacterLifestyleIncrementAction.DecreaseCareer, state.Increments, "Decrementing a Lifestyle interval is career-only.");
        }
        if (!state.TotalIncrementCostExact || state.TotalIncrementCost < 0m)
        {
            return Blocked(CharacterLifestyleIncrementAction.DecreaseCareer, state.Increments, "Exact saved total interval cost authority is required to update derived Lifestyle totals.");
        }

        // Chummer5 intentionally does not impose a lower bound here.
        return new CharacterLifestyleIncrementQuote(
            CharacterLifestyleIncrementAction.DecreaseCareer,
            checked(state.Increments - 1),
            0m,
            Exact: true);
    }

    private static CharacterLifestyleIncrementQuote Blocked(
        CharacterLifestyleIncrementAction action,
        int current,
        string blocker)
        => new(action, current, 0m, Exact: false, blocker);
}
