using System.Globalization;

namespace Chummer.Contracts.Characters;

public sealed record CharacterGearMergeIdentity(
    string Name,
    string Category,
    int Rating,
    string Extra,
    string GearName,
    string Notes,
    IReadOnlyList<CharacterGearMergeChildIdentity> Children);

public sealed record CharacterGearMergeChildIdentity(
    decimal Quantity,
    CharacterGearMergeIdentity Identity);

public sealed record CharacterGearCostSnapshot(
    int Rating,
    decimal Quantity,
    string CostExpression,
    decimal CostFor,
    bool DiscountedCost,
    int ChildCostMultiplier,
    IReadOnlyList<CharacterGearCostSnapshot> Children);

public static class CharacterGearQuantityRules
{
    public const decimal MaximumQuantity = 1_000_000m;

    public static bool TryResolvePrecision(
        string name,
        string category,
        int? maximumNuyenDecimals,
        out int decimalPlaces,
        out decimal minimumIncrement)
    {
        decimalPlaces = 0;
        minimumIncrement = 1m;
        if (name.StartsWith("Nuyen", StringComparison.Ordinal))
        {
            if (maximumNuyenDecimals is not >= 0 or > 28)
            {
                return false;
            }

            decimalPlaces = maximumNuyenDecimals.Value;
        }
        else if (string.Equals(category, "Currency", StringComparison.Ordinal))
        {
            decimalPlaces = 2;
        }

        for (int index = 0; index < decimalPlaces; index++)
        {
            minimumIncrement /= 10m;
        }

        return true;
    }

    public static bool IsValidAmount(decimal value, decimal minimumIncrement)
    {
        if (value < minimumIncrement || value > MaximumQuantity || minimumIncrement <= 0m)
        {
            return false;
        }

        decimal increments = value / minimumIncrement;
        return decimal.Truncate(increments) == increments;
    }

    public static bool AreIdenticalForMerge(
        CharacterGearMergeIdentity? left,
        CharacterGearMergeIdentity? right,
        bool ignoreSuperficials = true)
    {
        if (left is null || right is null
            || !string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            || !string.Equals(left.Category, right.Category, StringComparison.Ordinal)
            || left.Rating != right.Rating
            || !string.Equals(left.Extra, right.Extra, StringComparison.Ordinal)
            || !ignoreSuperficials
                && (!string.Equals(left.GearName, right.GearName, StringComparison.Ordinal)
                    || !string.Equals(left.Notes, right.Notes, StringComparison.Ordinal)))
        {
            return false;
        }

        return DeepChildrenMatch(left.Children, right.Children, ignoreSuperficials);
    }

    public static bool TryEvaluateCostExpression(string? expression, int rating, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        try
        {
            var parser = new CostExpressionParser(expression, rating);
            return parser.TryParse(out value) && value >= 0m;
        }
        catch (Exception exception) when (exception is OverflowException or DivideByZeroException)
        {
            value = 0m;
            return false;
        }
    }

    public static bool TryCalculatePurchaseUnitCost(
        CharacterGearCostSnapshot gear,
        out decimal cost)
        => TryCalculateTotalCost(gear, parentMultiplier: 1, quantityOverride: 1m, out cost);

    private static bool TryCalculateTotalCost(
        CharacterGearCostSnapshot gear,
        int parentMultiplier,
        decimal? quantityOverride,
        out decimal total)
    {
        total = 0m;
        try
        {
            if (gear.Quantity <= 0m
                || gear.CostFor <= 0m
                || gear.ChildCostMultiplier <= 0
                || !TryEvaluateCostExpression(gear.CostExpression, gear.Rating, out decimal ownCost))
            {
                return false;
            }

            if (gear.DiscountedCost)
            {
                ownCost *= 0.9m;
            }

            decimal childTotal = 0m;
            foreach (CharacterGearCostSnapshot child in gear.Children)
            {
                if (!TryCalculateTotalCost(
                        child,
                        gear.ChildCostMultiplier,
                        quantityOverride: null,
                        out decimal childCost))
                {
                    return false;
                }
                childTotal += childCost;
            }

            decimal quantity = quantityOverride ?? gear.Quantity;
            total = ownCost * quantity * parentMultiplier / gear.CostFor + childTotal * quantity;
            return total >= 0m;
        }
        catch (Exception exception) when (exception is OverflowException or DivideByZeroException)
        {
            total = 0m;
            return false;
        }
    }

    private static bool DeepChildrenMatch(
        IReadOnlyList<CharacterGearMergeChildIdentity> left,
        IReadOnlyList<CharacterGearMergeChildIdentity> right,
        bool ignoreSuperficials)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        bool[] matched = new bool[right.Count];
        foreach (CharacterGearMergeChildIdentity candidate in left)
        {
            bool found = false;
            for (int index = 0; index < right.Count; index++)
            {
                CharacterGearMergeChildIdentity target = right[index];
                if (!matched[index]
                    && candidate.Quantity == target.Quantity
                    && AreIdenticalForMerge(candidate.Identity, target.Identity, ignoreSuperficials))
                {
                    matched[index] = true;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class CostExpressionParser
    {
        private readonly string _expression;
        private readonly decimal _rating;
        private int _index;

        public CostExpressionParser(string expression, int rating)
        {
            _expression = expression;
            _rating = rating;
        }

        public bool TryParse(out decimal value)
        {
            _index = 0;
            if (!TryParseExpression(out value))
            {
                return false;
            }

            SkipWhitespace();
            return _index == _expression.Length;
        }

        private bool TryParseExpression(out decimal value)
        {
            if (!TryParseTerm(out value))
            {
                return false;
            }

            while (true)
            {
                SkipWhitespace();
                if (!TryConsume('+') && !TryConsume('-'))
                {
                    return true;
                }

                char operation = _expression[_index - 1];
                if (!TryParseTerm(out decimal right))
                {
                    return false;
                }

                value = operation == '+' ? value + right : value - right;
            }
        }

        private bool TryParseTerm(out decimal value)
        {
            if (!TryParseFactor(out value))
            {
                return false;
            }

            while (true)
            {
                SkipWhitespace();
                if (!TryConsume('*') && !TryConsume('/'))
                {
                    return true;
                }

                char operation = _expression[_index - 1];
                if (!TryParseFactor(out decimal right))
                {
                    return false;
                }

                value = operation == '*' ? value * right : value / right;
            }
        }

        private bool TryParseFactor(out decimal value)
        {
            SkipWhitespace();
            bool negate = TryConsume('-');
            SkipWhitespace();

            if (TryConsume('('))
            {
                if (!TryParseExpression(out value))
                {
                    return false;
                }
                SkipWhitespace();
                if (!TryConsume(')'))
                {
                    return false;
                }
            }
            else if (TryConsumeWord("Rating"))
            {
                value = _rating;
            }
            else if (!TryParseNumber(out value))
            {
                return false;
            }

            if (negate)
            {
                value = -value;
            }
            return true;
        }

        private bool TryParseNumber(out decimal value)
        {
            value = 0m;
            int start = _index;
            bool dotSeen = false;
            while (_index < _expression.Length)
            {
                char character = _expression[_index];
                if (char.IsDigit(character))
                {
                    _index++;
                    continue;
                }
                if (character == '.' && !dotSeen)
                {
                    dotSeen = true;
                    _index++;
                    continue;
                }
                break;
            }

            return start != _index
                && decimal.TryParse(
                    _expression.AsSpan(start, _index - start),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out value);
        }

        private bool TryConsumeWord(string value)
        {
            if (_index + value.Length > _expression.Length
                || !_expression.AsSpan(_index, value.Length).Equals(value.AsSpan(), StringComparison.Ordinal))
            {
                return false;
            }

            _index += value.Length;
            return true;
        }

        private bool TryConsume(char value)
        {
            if (_index >= _expression.Length || _expression[_index] != value)
            {
                return false;
            }

            _index++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_index < _expression.Length && char.IsWhiteSpace(_expression[_index]))
            {
                _index++;
            }
        }
    }
}
