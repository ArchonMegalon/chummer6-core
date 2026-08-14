using System.Globalization;

namespace Chummer.Contracts.Characters;

public sealed record CharacterVehicleConditionModifierBasis(
    bool IncludedInVehicle,
    bool Equipped,
    int ConditionMonitorBonus,
    int? EffectiveBodyBonus,
    bool Exact = true);

public static class CharacterVehicleConditionMonitorCalculator
{
    public const int MaximumConditionBoxes = 1000;
    private static readonly string[] CharacterAttributeNames =
    [
        "BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL", "EDG", "MAG", "MAGAdept",
        "RES", "ESS", "DEP"
    ];

    public static bool TryCalculatePhysicalMaximum(
        string? category,
        int baseBody,
        IReadOnlyList<CharacterVehicleConditionModifierBasis> modifiers,
        out int maximum)
    {
        ArgumentNullException.ThrowIfNull(modifiers);
        maximum = 0;
        long totalBody = baseBody;
        long conditionBonus = 0;
        foreach (CharacterVehicleConditionModifierBasis modifier in modifiers)
        {
            if (!modifier.Exact)
            {
                return false;
            }
            conditionBonus += modifier.ConditionMonitorBonus;
            if (!modifier.IncludedInVehicle && modifier.Equipped)
            {
                if (modifier.EffectiveBodyBonus is not int bodyBonus)
                {
                    return false;
                }
                totalBody += bodyBonus;
            }
        }

        long baseBoxes = IsDrone(category)
            ? string.Equals(category?.Trim(), "Drones: Anthro", StringComparison.OrdinalIgnoreCase) ? 8 : 6
            : 12;
        long calculated = baseBoxes + DivideAwayFromZero(totalBody, 2) + conditionBonus;
        if (calculated is <= 0 or > MaximumConditionBoxes)
        {
            return false;
        }

        maximum = (int)calculated;
        return true;
    }

    public static bool TryResolveRatingExpression(string? expression, int rating, out int value)
        => TryResolveRatingExpression(
            expression,
            rating,
            savedAttributeTotals: null,
            out value);

    public static bool TryResolveRatingExpression(
        string? expression,
        int rating,
        IReadOnlyDictionary<string, int>? savedAttributeTotals,
        out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        string ratingText = rating.ToString(CultureInfo.InvariantCulture);
        string normalized = expression.Replace(
            "{Rating}",
            ratingText,
            StringComparison.OrdinalIgnoreCase);
        if (savedAttributeTotals is not null)
        {
            foreach (string attributeName in CharacterAttributeNames)
            {
                if (savedAttributeTotals.TryGetValue(attributeName, out int total))
                {
                    normalized = normalized.Replace(
                        $"{{{attributeName}}}",
                        total.ToString(CultureInfo.InvariantCulture),
                        StringComparison.Ordinal);
                }
            }
        }
        normalized = normalized
            .Replace("Rating", ratingText, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();
        try
        {
            int offset = 0;
            if (!TryParseAdditiveExpression(normalized, ref offset, out long parsed)
                || offset != normalized.Length
                || parsed is < int.MinValue or > int.MaxValue)
            {
                return false;
            }

            value = (int)parsed;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryParseAdditiveExpression(string expression, ref int offset, out long value)
    {
        if (!TryParseProduct(expression, ref offset, out value))
        {
            return false;
        }

        while (offset < expression.Length && expression[offset] is '+' or '-')
        {
            char operation = expression[offset++];
            if (!TryParseProduct(expression, ref offset, out long operand))
            {
                return false;
            }
            value = operation == '+' ? checked(value + operand) : checked(value - operand);
        }
        return true;
    }

    private static bool TryParseProduct(string expression, ref int offset, out long value)
    {
        if (!TryParseFactor(expression, ref offset, out value))
        {
            return false;
        }

        while (offset < expression.Length && expression[offset] == '*')
        {
            offset++;
            if (!TryParseFactor(expression, ref offset, out long operand))
            {
                return false;
            }
            value = checked(value * operand);
        }
        return true;
    }

    private static bool TryParseFactor(string expression, ref int offset, out long value)
    {
        value = 0;
        if (offset >= expression.Length)
        {
            return false;
        }

        int sign = 1;
        if (expression[offset] is '+' or '-')
        {
            sign = expression[offset++] == '-' ? -1 : 1;
            if (offset >= expression.Length)
            {
                return false;
            }
        }

        if (expression[offset] == '(')
        {
            offset++;
            if (!TryParseAdditiveExpression(expression, ref offset, out value)
                || offset >= expression.Length
                || expression[offset] != ')')
            {
                return false;
            }
            offset++;
            value = checked(sign * value);
            return true;
        }

        int start = offset;
        while (offset < expression.Length && char.IsAsciiDigit(expression[offset]))
        {
            offset++;
        }
        if (start == offset
            || !long.TryParse(
                expression.AsSpan(start, offset - start),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long magnitude))
        {
            return false;
        }

        value = checked(sign * magnitude);
        return true;
    }

    private static bool IsDrone(string? category)
        => category?.Contains("Drone", StringComparison.OrdinalIgnoreCase) == true;

    private static long DivideAwayFromZero(long dividend, long divisor)
    {
        long quotient = dividend / divisor;
        long remainder = dividend % divisor;
        return remainder == 0 ? quotient : quotient + Math.Sign(dividend);
    }
}
