namespace Chummer.Contracts.Characters;

public sealed record CharacterMatrixImprovementFragment(
    string Expression,
    decimal Value,
    string UniqueName,
    bool Custom);

public static class CharacterMatrixImprovementSelector
{
    public static bool TrySelectExpressions(
        IReadOnlyList<CharacterMatrixImprovementFragment> fragments,
        out IReadOnlyList<string> expressions)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        try
        {
            var selectedByExpression = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var expressionOrder = new List<string>();
            var nonCustomExpressions = new HashSet<string>(StringComparer.Ordinal);
            if (!TryAppendPhase(
                    fragments.Where(fragment => !fragment.Custom),
                    customPhase: false,
                    selectedByExpression,
                    expressionOrder,
                    nonCustomExpressions)
                || !TryAppendPhase(
                    fragments.Where(fragment => fragment.Custom),
                    customPhase: true,
                    selectedByExpression,
                    expressionOrder,
                    nonCustomExpressions))
            {
                expressions = Array.Empty<string>();
                return false;
            }

            expressions = expressionOrder
                .SelectMany(expression => selectedByExpression[expression])
                .ToArray();
            return true;
        }
        catch (OverflowException)
        {
            expressions = Array.Empty<string>();
            return false;
        }
    }

    private static bool TryAppendPhase(
        IEnumerable<CharacterMatrixImprovementFragment> fragments,
        bool customPhase,
        Dictionary<string, List<string>> selectedByExpression,
        List<string> expressionOrder,
        HashSet<string> nonCustomExpressions)
    {
        var groups = new Dictionary<string, List<CharacterMatrixImprovementFragment>>(StringComparer.Ordinal);
        var groupOrder = new List<string>();
        foreach (CharacterMatrixImprovementFragment fragment in fragments)
        {
            if (!groups.TryGetValue(fragment.Expression, out List<CharacterMatrixImprovementFragment>? group))
            {
                group = [];
                groups.Add(fragment.Expression, group);
                groupOrder.Add(fragment.Expression);
            }
            group.Add(fragment);
        }

        foreach (string expression in groupOrder)
        {
            if (!selectedByExpression.TryGetValue(expression, out List<string>? selectedExpressions))
            {
                selectedExpressions = [];
                selectedByExpression.Add(expression, selectedExpressions);
                expressionOrder.Add(expression);
            }

            if (!customPhase)
            {
                nonCustomExpressions.Add(expression);
                selectedExpressions.AddRange(
                    SelectNonCustomGroup(groups[expression]).Select(fragment => fragment.Expression));
                continue;
            }

            List<CharacterMatrixImprovementFragment> customUnique = SelectCustomUniqueGroup(groups[expression]);
            // Chummer5's custom pass appends unique winners through the non-custom selection
            // dictionary. Consequently, a custom-unique-only expression has no returned fragment.
            if (nonCustomExpressions.Contains(expression))
            {
                selectedExpressions.AddRange(customUnique.Select(fragment => fragment.Expression));
            }
            selectedExpressions.AddRange(groups[expression]
                .Where(fragment => string.IsNullOrEmpty(fragment.UniqueName))
                .Select(fragment => fragment.Expression));
        }

        return true;
    }

    private static IReadOnlyList<CharacterMatrixImprovementFragment> SelectNonCustomGroup(
        IReadOnlyList<CharacterMatrixImprovementFragment> group)
    {
        CharacterMatrixImprovementFragment[] nonUnique = group
            .Where(fragment => string.IsNullOrEmpty(fragment.UniqueName))
            .ToArray();
        decimal baseline = nonUnique.Aggregate(
            0m,
            (current, fragment) => checked(current + fragment.Value));
        CharacterMatrixImprovementFragment[] unique = group
            .Where(fragment => !string.IsNullOrEmpty(fragment.UniqueName))
            .ToArray();
        if (unique.Length == 0)
        {
            return nonUnique;
        }

        if (unique.Any(fragment => string.Equals(fragment.UniqueName, "precedence0", StringComparison.Ordinal)))
        {
            CharacterMatrixImprovementFragment highest = SelectHighest(
                unique.Where(fragment => string.Equals(fragment.UniqueName, "precedence0", StringComparison.Ordinal)));
            var precedence = new List<CharacterMatrixImprovementFragment> { highest };
            decimal precedenceValue = highest.Value;
            foreach (CharacterMatrixImprovementFragment fragment in unique.Where(fragment =>
                         string.Equals(fragment.UniqueName, "precedence-1", StringComparison.Ordinal)))
            {
                precedenceValue = checked(precedenceValue + fragment.Value);
                precedence.Add(fragment);
            }
            return baseline < precedenceValue ? precedence : nonUnique;
        }

        if (unique.Any(fragment => string.Equals(fragment.UniqueName, "precedence1", StringComparison.Ordinal)))
        {
            CharacterMatrixImprovementFragment[] precedence = unique
                .Where(fragment => string.Equals(fragment.UniqueName, "precedence1", StringComparison.Ordinal)
                                   || string.Equals(fragment.UniqueName, "precedence-1", StringComparison.Ordinal))
                .ToArray();
            decimal precedenceValue = precedence.Aggregate(
                0m,
                (current, fragment) => checked(current + fragment.Value));
            return baseline < precedenceValue ? precedence : nonUnique;
        }

        var selected = new List<CharacterMatrixImprovementFragment>(nonUnique);
        foreach (IGrouping<string, CharacterMatrixImprovementFragment> uniqueGroup in unique
                     .GroupBy(fragment => fragment.UniqueName, StringComparer.Ordinal))
        {
            CharacterMatrixImprovementFragment highest = SelectHighest(uniqueGroup);
            baseline = checked(baseline + highest.Value);
            selected.Add(highest);
        }
        return selected;
    }

    private static List<CharacterMatrixImprovementFragment> SelectCustomUniqueGroup(
        IReadOnlyList<CharacterMatrixImprovementFragment> group)
    {
        decimal value = group
            .Where(fragment => string.IsNullOrEmpty(fragment.UniqueName))
            .Aggregate(
            0m,
            (current, fragment) => checked(current + fragment.Value));
        var selected = new List<CharacterMatrixImprovementFragment>();
        foreach (IGrouping<string, CharacterMatrixImprovementFragment> uniqueGroup in group
                     .Where(fragment => !string.IsNullOrEmpty(fragment.UniqueName))
                     .GroupBy(fragment => fragment.UniqueName, StringComparer.Ordinal))
        {
            CharacterMatrixImprovementFragment highest = SelectHighest(uniqueGroup);
            value = checked(value + highest.Value);
            selected.Add(highest);
        }
        return selected;
    }

    private static CharacterMatrixImprovementFragment SelectHighest(
        IEnumerable<CharacterMatrixImprovementFragment> fragments)
    {
        using IEnumerator<CharacterMatrixImprovementFragment> enumerator = fragments.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            throw new InvalidOperationException("At least one Matrix improvement fragment is required.");
        }

        CharacterMatrixImprovementFragment highest = enumerator.Current;
        while (enumerator.MoveNext())
        {
            if (highest.Value < enumerator.Current.Value)
            {
                highest = enumerator.Current;
            }
        }
        return highest;
    }
}
