namespace Chummer.Contracts.Characters;

public static class CharacterMatrixConditionMonitorCalculator
{
    public const int BaseMatrixBoxes = 8;
    public const int MaximumConditionBoxes = 1000;

    public static bool TryCalculateMaximum(
        int totalDeviceRating,
        int totalBonusMatrixBoxes,
        out int maximum)
    {
        long calculated = BaseMatrixBoxes
            + DivideAwayFromZero(totalDeviceRating, 2)
            + totalBonusMatrixBoxes;
        if (calculated is <= 0 or > MaximumConditionBoxes)
        {
            maximum = 0;
            return false;
        }

        maximum = (int)calculated;
        return true;
    }

    private static long DivideAwayFromZero(long dividend, long divisor)
    {
        long quotient = dividend / divisor;
        long remainder = dividend % divisor;
        return remainder == 0 ? quotient : quotient + Math.Sign(dividend);
    }
}
