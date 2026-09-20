namespace Chummer.Contracts.Characters;

/// <summary>
/// Unmodified SR5 creation costs for an admitted rating interval. Bounds already
/// exclude free/point-bought ranks and account for group allocation. These
/// primitives do not apply improvements, group compensation or specializations.
/// </summary>
public static class CharacterCreationSkillCostRules
{
    public static bool TryActive(int lowerRating, int upperRating, int newSkillCost,
        int improveSkillCost, out int cost) =>
        TryCalculate(lowerRating, upperRating, newSkillCost, improveSkillCost, CostKind.Active, out cost);

    public static bool TryKnowledge(int lowerRating, int upperRating, int newSkillCost,
        int improveSkillCost, out int cost) =>
        TryCalculate(lowerRating, upperRating, newSkillCost, improveSkillCost, CostKind.Knowledge, out cost);

    public static bool TryGroup(int lowerRating, int upperRating, int newGroupCost,
        int improveGroupCost, out int cost) =>
        TryCalculate(lowerRating, upperRating, newGroupCost, improveGroupCost, CostKind.Group, out cost);

    private enum CostKind { Active, Knowledge, Group }

    private static bool TryCalculate(int lower, int upper, int newCost, int improveCost,
        CostKind kind, out int cost)
    {
        cost = 0;
        if (lower < 0 || upper < lower || newCost < 0 || improveCost < 0)
            return false;
        if (lower == upper)
            return true;
        try
        {
            long sum = ((long)upper * (upper + 1L) - (long)lower * (lower + 1L)) / 2;
            long result;
            if (kind == CostKind.Group)
            {
                // Legacy SkillGroup.CurrentKarmaCost applies the new-group cost
                // only to a one-rank total; larger totals use the improve cost.
                result = checked(sum * (sum == 1 ? newCost : improveCost));
            }
            else if (lower == 0 && (kind == CostKind.Active || improveCost > 0))
            {
                // KnowledgeSkill applies the new-skill adjustment only when its
                // improvement subtotal is positive (including zero-cost profiles).
                result = checked((sum - 1) * improveCost + newCost);
            }
            else
            {
                result = checked(sum * improveCost);
            }
            cost = checked((int)result);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
