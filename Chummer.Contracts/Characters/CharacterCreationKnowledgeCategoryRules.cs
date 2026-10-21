namespace Chummer.Contracts.Characters;

/// <summary>Source-compiled occurrence, not caller-supplied pricing authority. Rebuilt
/// from admitted quality XML on full review and frozen selected replay.</summary>
public sealed record CharacterCreationKnowledgeCategoryEffect(
    string ImprovementType, string Category, decimal Value, int Minimum, int Maximum, string Condition,
    Guid SourceId, string SourceNodeDigest, string OptionId, int Level, string Container, int Position,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationKnowledgeCategoryPrice(
    string Category, int Lower, int Upper, int BasePoints, int PointSpecializations, int KarmaSpecializations,
    string Lifecycle, decimal RawInterval, decimal IntervalMultiplier, decimal IntervalAddition,
    decimal RawSpecializations, decimal SpecializationMultiplier, decimal PointMultiplier,
    decimal BeforeRounding, int KarmaCost, int PointCost,
    IReadOnlyList<CharacterCreationKnowledgeCategoryEffect> Effects);

/// <summary>KnowledgeSkill.CurrentKarmaCost/CurrentSpCost creation semantics.
/// Never sum career next-rating quotes. Native StandardRound is away from zero;
/// interval and paid specialization are rounded together, then clamped.</summary>
public static class CharacterCreationKnowledgeCategoryRules
{
    public static bool IsSupportedType(string type) => type is "SkillCategoryKarmaCost"
        or "SkillCategoryKarmaCostMultiplier" or "SkillCategoryPointCostMultiplier"
        or "SkillCategorySpecializationKarmaCostMultiplier";

    // Source XPath-shaped lifecycle literals are not native Improvement.Condition tokens.
    // Admit only these exact forms; do not evaluate XPath or normalize unknown text.
    public static bool TryLowerNativeCondition(string condition, out string nativeCondition)
    {
        nativeCondition = condition switch
        {
            "" => "",
            "create" or "/character/created = false" => "create",
            "career" or "/character/created" => "career",
            _ => null!
        };
        return nativeCondition is not null;
    }

    public static bool IsSupportedCondition(string condition)
        => TryLowerNativeCondition(condition, out _);

    public static bool TryPrice(string category, int lower, int upper, int basePoints,
        int pointSpecializations, int karmaSpecializations, int newRate, int improveRate, int specializationRate,
        IReadOnlyList<CharacterCreationKnowledgeCategoryEffect> effects,
        out CharacterCreationKnowledgeCategoryPrice? price)
    {
        price = null;
        if (string.IsNullOrWhiteSpace(category) || lower < 0 || upper < lower
            || upper > CharacterCreationStandardPrioritySkillsRules.MaximumRatingAtCreation
            || basePoints < 0 || basePoints > lower || pointSpecializations is < 0 or > 1
            || karmaSpecializations is < 0 or > 1 || pointSpecializations + karmaSpecializations > 1
            || newRate < 0 || improveRate < 0 || specializationRate < 0 || effects is null
            || effects.Any(effect => effect is null || !IsSupportedType(effect.ImprovementType)
                || !IsSupportedCondition(effect.Condition) || string.IsNullOrWhiteSpace(effect.Category)
                || effect.Minimum < 0 || effect.Maximum < 0 || effect.Maximum != 0 && effect.Maximum < effect.Minimum
                || effect.ImprovementType != "SkillCategoryKarmaCost" && effect.Value < 0
                || effect.SourceId == Guid.Empty || !CharacterCreationSkillsDigest.IsCanonical(effect.SourceNodeDigest)
                || string.IsNullOrWhiteSpace(effect.OptionId) || effect.Level < 1 || effect.Position < 0
                || effect.Container is not ("bonus" or "firstlevelbonus") || effect.SourceAnchorIds is null))
            return false;
        try
        {
            decimal raw = checked(((decimal)upper * (upper + 1) - (decimal)lower * (lower + 1)) / 2 * improveRate);
            if (lower == 0 && raw > 0) raw = checked(raw + newRate - improveRate);
            decimal multiplier = 1, extra = 0, specMultiplier = 1, pointMultiplier = 1;
            foreach (var effect in effects)
            {
                // Retain inactive effects in the receipt and native improvement graph.
                if (effect.Category != category) continue;
                if (!TryLowerNativeCondition(effect.Condition, out string nativeCondition)) return false;
                if (nativeCondition == "career") continue;
                if (effect.ImprovementType == "SkillCategoryPointCostMultiplier")
                {
                    if (effect.Minimum <= basePoints) pointMultiplier = checked(pointMultiplier * effect.Value / 100m);
                    continue;
                }
                if (effect.Minimum > upper) continue;
                switch (effect.ImprovementType)
                {
                    case "SkillCategoryKarmaCost":
                        // Preserve native interval bound semantics, including Maximum=0.
                        extra = checked(extra + effect.Value * (Math.Min(upper,
                            effect.Maximum == 0 ? int.MaxValue : effect.Maximum) - Math.Max(lower, effect.Minimum - 1)));
                        break;
                    case "SkillCategoryKarmaCostMultiplier":
                        multiplier = checked(multiplier * effect.Value / 100m);
                        break;
                    case "SkillCategorySpecializationKarmaCostMultiplier":
                        specMultiplier = checked(specMultiplier * effect.Value / 100m);
                        break;
                }
            }
            decimal specs = checked((decimal)specializationRate * karmaSpecializations);
            decimal total = checked(raw * multiplier + extra + specs * specMultiplier);
            int karma = RoundClamp(total);
            int points = RoundClamp(checked((basePoints + pointSpecializations) * pointMultiplier));
            price = new(category, lower, upper, basePoints, pointSpecializations, karmaSpecializations,
                "creation", raw, multiplier, extra, specs, specMultiplier, pointMultiplier, total, karma, points, effects.ToArray());
            return true;
        }
        catch (OverflowException) { return false; }
    }

    private static int RoundClamp(decimal value)
        => checked((int)Math.Max(0m, value >= 0 ? decimal.Ceiling(value) : decimal.Floor(value)));
}
