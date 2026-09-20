namespace Chummer.Contracts.Characters;

/// <summary>
/// Cost of an already admitted contact. Does not resolve source improvements,
/// enforce selection limits, or authorize a mutation. Mirrors Contact.ContactPoints
/// and DecimalExtensions.StandardRound, including fractional source modifiers.
/// </summary>
public static class CharacterCreationContactCostRules
{
    public static bool TryCalculate(int connection, int loyalty, bool free, bool family,
        bool blackmail, decimal discount, decimal minimum, out int points)
    {
        points = 0;
        if (connection is < 1 or > 12 || loyalty is < 1 or > 6) return false;
        if (free) return true;
        try
        {
            decimal raw = checked(connection + (decimal)loyalty + (family ? 1 : 0)
                + (blackmail ? 2 : 0) + discount);
            decimal floor = checked(2m + minimum);
            // Legacy StandardRound rounds every fraction away from zero, not
            // merely .5 ties. A 4.1-point contact costs five, not four.
            decimal exact = Math.Max(raw, floor);
            if (exact < 0) return false;
            decimal cost = decimal.Ceiling(exact);
            if (cost is < 0 or > int.MaxValue) return false;
            points = decimal.ToInt32(cost);
            return true;
        }
        catch (OverflowException) { return false; }
    }
}
