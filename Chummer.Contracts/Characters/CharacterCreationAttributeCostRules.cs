namespace Chummer.Contracts.Characters;

/// <summary>
/// SR5 creation cost for consecutive paid attribute levels. The caller supplies
/// the source-bound cost base (after any admitted metatype/priority adjustment)
/// and profile multiplier. This does not authorize an attribute or spending.
/// </summary>
public static class CharacterCreationAttributeCostRules
{
    public static bool TryCalculate(int startingRating, int purchasedLevels, int karmaPerRating, out int cost)
    {
        cost = 0;
        if (startingRating < 0 || purchasedLevels < 0 || karmaPerRating <= 0)
            return false;
        try
        {
            // Sum the new ratings: (base+1) + ... + (base+levels).
            // Wide intermediates avoid rejecting a valid result solely because
            // the numerator exceeds Int32 before the exact division by two.
            long levels = purchasedLevels;
            long sum = checked((2L * startingRating + levels + 1) * levels / 2);
            cost = checked((int)(sum * karmaPerRating));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
