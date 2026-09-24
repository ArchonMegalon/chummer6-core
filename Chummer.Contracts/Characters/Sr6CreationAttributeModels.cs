namespace Chummer.Contracts.Characters;

public static class Sr6CreationAttributeIds
{
    public static IReadOnlyList<string> Ordered { get; } = Array.AsReadOnly<string>(
        ["Body", "Agility", "Reaction", "Strength", "Willpower", "Logic", "Intuition", "Charisma", "Edge", "Magic", "Resonance"]);
}

public sealed record Sr6CreationAttributeSpend(string AttributeId, int AttributePoints, int AdjustmentPoints);
public sealed record Sr6CreationAttributeSelection(IReadOnlyList<Sr6CreationAttributeSpend> Allocations);
public sealed record Sr6CreationAttributeOption(string AttributeId, int BaseValue, int Maximum,
    bool AllowsAttributePoints, bool AllowsAdjustmentPoints);
public sealed record Sr6CreationAttributeValue(string AttributeId, int BaseValue, int Maximum,
    int AttributePoints, int AdjustmentPoints, int Value);

/// <summary>Unaugmented allocation in the pending draft, not a Career or character-effect grant.</summary>
public sealed record Sr6CreationAttributePreview(IReadOnlyList<Sr6CreationAttributeValue> Values,
    int AttributePointsSpent, int AttributePointsRemaining, int AdjustmentPointsSpent, int AdjustmentPointsRemaining,
    bool AllPointsSpent, string AuthorityDigest, IReadOnlyList<string> SourceAnchorIds);

public static class Sr6CreationAttributeBlockers
{
    public const string InvalidAllocation = "sr6-attributes-invalid-allocation";
    public const string PointKindUnavailable = "sr6-attributes-point-kind-unavailable";
    public const string RatingExceeded = "sr6-attributes-rating-exceeded";
    public const string MaximumCountExceeded = "sr6-attributes-maximum-count-exceeded";
    public const string AttributeBudgetExceeded = "sr6-attributes-attribute-budget-exceeded";
    public const string AdjustmentBudgetExceeded = "sr6-attributes-adjustment-budget-exceeded";
}
