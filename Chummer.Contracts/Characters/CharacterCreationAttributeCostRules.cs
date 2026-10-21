namespace Chummer.Contracts.Characters;

/// <summary>
/// SR5 creation cost for consecutive paid attribute levels. The caller supplies
/// the source-bound cost base (after any admitted metatype/priority adjustment)
/// and profile multiplier. This does not authorize an attribute or spending.
/// </summary>
public static class CharacterCreationAttributeCostRules
{
    /// <summary>
    /// Creation allocation without attribute improvements: metatype/talent minimum
    /// determines the rating; the two profile rules determine only the paid base.
    /// Chummer5 TotalKarmaCost (not UpgradeKarmaCost) uses a human minimum of one
    /// for every enabled attribute, including Edge, Magic and Resonance.
    /// </summary>
    public static bool TryCalculateAllocation(int minimum, int priorityPoints, int karmaLevels,
        int karmaPerRating, bool alternateMetatypeAttributeKarma, bool reverseAttributePriorityOrder,
        out int current, out int cost)
    {
        current = 0;
        cost = 0;
        if (minimum < 0 || priorityPoints < 0 || karmaLevels < 0 || karmaPerRating <= 0)
            return false;
        try
        {
            int rating = checked(minimum + priorityPoints + karmaLevels);
            // Legacy TotalKarmaCost returns zero before computing a paid base
            // when no Karma levels exist. Do not overflow an unused alternate base.
            if (karmaLevels == 0)
            {
                current = rating;
                return true;
            }
            int costBase = checked((alternateMetatypeAttributeKarma ? 1 : minimum)
                + (reverseAttributePriorityOrder ? 0 : priorityPoints));
            if (!TryCalculate(costBase, karmaLevels, karmaPerRating, out cost))
                return false;
            current = rating;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

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
