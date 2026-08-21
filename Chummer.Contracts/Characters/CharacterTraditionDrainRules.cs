namespace Chummer.Contracts.Characters;

/// <summary>
/// Exact Chummer5 rules for the Create/Career magical-tradition drain selector.
/// </summary>
public static class CharacterTraditionDrainRules
{
    public const int MaximumExpressionLength = 32_767;

    public static bool TryCreateSemantics(
        Guid traditionId,
        Guid sourceId,
        string? traditionType,
        bool adeptEnabled,
        bool magicianEnabled,
        string? currentExpression,
        IReadOnlyList<string>? sourceExpressions,
        out CharacterTraditionDrainSemantics semantics)
    {
        semantics = CharacterTraditionDrainSemantics.Unavailable;
        if (traditionId == Guid.Empty
            || sourceId == Guid.Empty
            || !string.Equals(traditionType, "MAG", StringComparison.Ordinal)
            || (adeptEnabled && !magicianEnabled)
            || sourceExpressions is null
            || sourceExpressions.Count == 0
            || !TryNormalizeSourceExpressions(sourceExpressions, out string[] allowed))
        {
            return false;
        }

        string current = currentExpression ?? string.Empty;
        if (!TryValidateExpression(current, allowed, out current))
        {
            return false;
        }

        bool custom = sourceId == CharacterTraditionNameRules.CustomMagicalTraditionSourceId;
        if (!custom && current.Length != 0)
        {
            return false;
        }

        semantics = new CharacterTraditionDrainSemantics(
            traditionId,
            sourceId,
            current,
            [string.Empty, .. allowed]);
        return true;
    }

    public static bool TryValidateRequestedExpression(
        string? expression,
        IReadOnlyList<string>? allowedExpressions,
        out string validated)
    {
        validated = string.Empty;
        if (allowedExpressions is null || allowedExpressions.Count < 2)
        {
            return false;
        }

        if (allowedExpressions[0] is not { Length: 0 })
        {
            return false;
        }

        string[] sourceExpressions = allowedExpressions
            .Skip(1)
            .Where(static value => value is not null)
            .ToArray()!;
        return sourceExpressions.Length == allowedExpressions.Count - 1
            && TryNormalizeSourceExpressions(sourceExpressions, out string[] normalized)
            && TryValidateExpression(expression ?? string.Empty, normalized, out validated);
    }

    private static bool TryNormalizeSourceExpressions(
        IReadOnlyList<string> sourceExpressions,
        out string[] normalized)
    {
        normalized = [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<string>(sourceExpressions.Count);
        foreach (string? candidate in sourceExpressions)
        {
            if (string.IsNullOrWhiteSpace(candidate)
                || candidate.Length > MaximumExpressionLength
                || candidate.IndexOfAny(['\r', '\n', '\0']) >= 0
                || !seen.Add(candidate))
            {
                return false;
            }
            values.Add(candidate);
        }
        normalized = values.ToArray();
        return normalized.Length != 0;
    }

    private static bool TryValidateExpression(
        string expression,
        HashSet<string> allowed,
        out string validated)
    {
        validated = string.Empty;
        if (expression.Length > MaximumExpressionLength
            || expression.IndexOfAny(['\r', '\n', '\0']) >= 0
            || (expression.Length != 0 && !allowed.Contains(expression)))
        {
            return false;
        }
        validated = expression;
        return true;
    }

    private static bool TryValidateExpression(
        string expression,
        IReadOnlyList<string> allowed,
        out string validated)
        => TryValidateExpression(expression, allowed.ToHashSet(StringComparer.Ordinal), out validated);
}

public sealed record CharacterTraditionDrainSemantics(
    Guid TraditionId,
    Guid SourceId,
    string CurrentExpression,
    IReadOnlyList<string> AllowedExpressions)
{
    public static CharacterTraditionDrainSemantics Unavailable { get; } = new(
        Guid.Empty,
        Guid.Empty,
        string.Empty,
        Array.Empty<string>());
}
