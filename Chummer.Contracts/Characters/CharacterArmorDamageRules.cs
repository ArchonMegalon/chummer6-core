namespace Chummer.Contracts.Characters;

public enum CharacterArmorDamageAdjustment
{
    Repair,
    Degrade
}

public sealed record CharacterArmorDamageModifierBasis(
    int Armor,
    bool Equipped,
    bool Exact = true);

public static class CharacterArmorDamageRules
{
    public static bool TryCalculateMaximum(
        string? armorExpression,
        string? armorOverrideExpression,
        int rating,
        IReadOnlyList<CharacterArmorDamageModifierBasis> modifiers,
        out int maximum)
    {
        ArgumentNullException.ThrowIfNull(modifiers);
        maximum = 0;
        if (rating < 0
            || !CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
                armorExpression,
                rating,
                out int armor)
            || (!string.IsNullOrWhiteSpace(armorOverrideExpression)
                && !CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
                    armorOverrideExpression,
                    rating,
                    out _)))
        {
            return false;
        }

        long modifierArmor = 0;
        foreach (CharacterArmorDamageModifierBasis modifier in modifiers)
        {
            if (!modifier.Exact)
            {
                return false;
            }
            if (modifier.Equipped)
            {
                modifierArmor += modifier.Armor;
            }
        }

        long limitingArmor = Math.Max(0L, (long)armor + modifierArmor);
        if (!string.IsNullOrWhiteSpace(armorOverrideExpression))
        {
            CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
                armorOverrideExpression,
                rating,
                out int overrideArmor);
            limitingArmor = Math.Min(
                limitingArmor,
                Math.Max(0L, (long)overrideArmor + modifierArmor));
        }

        long calculated = (limitingArmor + 1L) / 2L;
        if (calculated > int.MaxValue)
        {
            return false;
        }
        maximum = (int)calculated;
        return true;
    }

    public static bool CanRepair(int currentDamage) => currentDamage > 0;

    public static bool CanDegrade(int currentDamage, int maximum)
        => currentDamage >= 0 && maximum >= 0 && currentDamage < maximum;

    public static bool TryApplyAdjustment(
        int currentDamage,
        int maximum,
        CharacterArmorDamageAdjustment adjustment,
        out int updatedDamage)
    {
        updatedDamage = currentDamage;
        switch (adjustment)
        {
            case CharacterArmorDamageAdjustment.Repair when CanRepair(currentDamage):
                updatedDamage = currentDamage - 1;
                return true;
            case CharacterArmorDamageAdjustment.Degrade when CanDegrade(currentDamage, maximum):
                updatedDamage = currentDamage + 1;
                return true;
            default:
                return false;
        }
    }
}
