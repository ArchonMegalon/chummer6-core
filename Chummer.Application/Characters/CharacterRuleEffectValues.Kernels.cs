using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static partial class CharacterRuleEffectValues2912
{
    internal static IEnumerable<CharacterRuleEffectValue> Budget(CharacterCreationBudgetState? budget)
    {
        if (budget is null) yield break;
        yield return Number(budget.BudgetId, budget.Label, CharacterRuleEffectMetric.BudgetTotal, budget.Total);
        yield return Number(budget.BudgetId, budget.Label, CharacterRuleEffectMetric.BudgetUsed, budget.Used);
        yield return Number(budget.BudgetId, budget.Label, CharacterRuleEffectMetric.BudgetRemaining, budget.Remaining);
        yield return Flag(budget.BudgetId, budget.Label, CharacterRuleEffectMetric.Available, budget.IsExact);
    }

    internal static IEnumerable<CharacterRuleEffectValue> Attributes(IEnumerable<CharacterCreationAttributeProjection> attributes)
    {
        foreach (var a in attributes)
        {
            yield return Number(a.AttributeId, a.AttributeId, CharacterRuleEffectMetric.Unaugmented, a.UnaugmentedCurrent);
            yield return Number(a.AttributeId, a.AttributeId, CharacterRuleEffectMetric.Minimum, a.Minimum);
            yield return Number(a.AttributeId, a.AttributeId, CharacterRuleEffectMetric.Maximum, a.Maximum);
            yield return Number(a.AttributeId, a.AttributeId, CharacterRuleEffectMetric.AugmentedMaximum, a.AugmentedMaximum);
            yield return Number(a.AttributeId, a.AttributeId, CharacterRuleEffectMetric.PriorityCost, a.PriorityPointCost);
            yield return Number(a.AttributeId, a.AttributeId, CharacterRuleEffectMetric.KarmaCost, a.KarmaCost);
            yield return Flag(a.AttributeId, a.AttributeId, CharacterRuleEffectMetric.Enabled, a.IsEnabled);
            foreach (string reason in a.DisableReasons)
                yield return Text(a.AttributeId, a.AttributeId, CharacterRuleEffectMetric.Condition, reason, reason);
        }
    }

    internal static IEnumerable<CharacterRuleEffectValue> CareerAttributes(IEnumerable<CharacterCareerAttributeAdvanceQuote> attributes)
    {
        foreach (var a in attributes)
        {
            string id = a.Identity.ToString();
            yield return Number(id, a.DisplayName, CharacterRuleEffectMetric.Unaugmented, a.EffectiveValue);
            yield return Number(id, a.DisplayName, CharacterRuleEffectMetric.Maximum, a.NaturalMaximum);
            yield return Number(id, a.DisplayName, CharacterRuleEffectMetric.Minimum, a.MetatypeMinimum);
            yield return AdvanceCost(id, a.DisplayName, a.KarmaCost, a.Blocker.ToString());
            yield return Number(id, a.DisplayName, CharacterRuleEffectMetric.KarmaRemaining, a.AvailableKarma);
            yield return Flag(id, a.DisplayName, CharacterRuleEffectMetric.Available, a.CanAdvance, "next-advance");
            yield return Text(id, a.DisplayName, CharacterRuleEffectMetric.Condition, a.Blocker.ToString(), "next-advance");
        }
    }

    internal static CharacterRuleEffectValue AdvanceCost(string id, string label, int cost, string blocker)
        => Number(id, label, CharacterRuleEffectMetric.KarmaCost, cost >= 0 ? cost : null,
            "next-advance", reason: blocker);

    internal static IEnumerable<CharacterRuleEffectValue> Resources(CharacterCreationResourcesBudget? budget)
    {
        if (budget is null) yield break;
        const string id = "resources";
        yield return Number(id, id, CharacterRuleEffectMetric.PriorityNuyen, budget.PriorityNuyen);
        yield return Number(id, id, CharacterRuleEffectMetric.KarmaInvestment, budget.KarmaInvestment);
        yield return Number(id, id, CharacterRuleEffectMetric.NuyenFromKarma, budget.NuyenFromKarma);
        yield return Number(id, id, CharacterRuleEffectMetric.StartingNuyen, budget.TotalStartingNuyen);
        yield return Number(id, id, CharacterRuleEffectMetric.PurchaseCost, budget.KnownPurchaseCost);
        yield return Number(id, id, CharacterRuleEffectMetric.RemainingNuyen, budget.RemainingNuyen);
        yield return Number(id, id, CharacterRuleEffectMetric.Overspend, budget.Overspend);
        yield return Number(id, id, CharacterRuleEffectMetric.CarryoverLimit, budget.CarryoverLimit);
        yield return Number(id, id, CharacterRuleEffectMetric.CarryoverExcess, budget.CarryoverExcess);
        yield return Flag(id, id, CharacterRuleEffectMetric.Available, budget.IsExact);
    }

    internal static IEnumerable<CharacterRuleEffectValue> Gear(IEnumerable<CharacterCreationGearLine> lines,
        CharacterCreationGearBudget? budget)
    {
        foreach (var line in lines)
        {
            foreach (var row in Purchase(line.OptionId, line.Name, line.PackageCost, line.TotalCost, line.Quantity,
                line.Availability, line.Legality)) yield return row;
            yield return Number(line.OptionId, line.Name, CharacterRuleEffectMetric.PackageQuantity, line.PackageQuantity);
        }
        if (budget is null) yield break;
        yield return Number("gear-budget", "Gear", CharacterRuleEffectMetric.StartingNuyen, budget.TotalStartingNuyen);
        yield return Number("gear-budget", "Gear", CharacterRuleEffectMetric.PurchaseCost, budget.BasketCost);
        yield return Number("gear-budget", "Gear", CharacterRuleEffectMetric.RemainingNuyen, budget.RemainingNuyen);
        yield return Number("gear-budget", "Gear", CharacterRuleEffectMetric.Overspend, budget.Overspend);
        yield return Flag("gear-budget", "Gear", CharacterRuleEffectMetric.Available, budget.IsExact);
    }

    internal static IEnumerable<CharacterRuleEffectValue> Purchase(string id, string name, decimal unit,
        decimal total, decimal quantity, int availability, string legality)
    {
        yield return Number(id, name, CharacterRuleEffectMetric.UnitCost, unit);
        yield return Number(id, name, CharacterRuleEffectMetric.TotalCost, total);
        yield return Number(id, name, CharacterRuleEffectMetric.Quantity, quantity);
        yield return Number(id, name, CharacterRuleEffectMetric.Availability, availability);
        yield return Text(id, name, CharacterRuleEffectMetric.Legality, legality);
    }

    internal static IEnumerable<CharacterRuleEffectValue> Loadout(IEnumerable<CharacterCreationLoadoutLine> lines, decimal value,
        object native)
    {
        foreach (var line in lines)
            foreach (var row in Purchase(line.OptionId, line.Name, line.UnitCost, line.TotalCost, line.Quantity,
                line.Availability, line.Legality)) yield return row;
        yield return Number("loadout", "Current loadout value", CharacterRuleEffectMetric.TotalCost, value);
        if (native is CharacterCreationNativeLoadoutProjection1432 rules)
        {
            yield return Number("loadout", "Equipped armor encumbrance", CharacterRuleEffectMetric.Encumbrance,
                rules.ArmorEncumbrance, reason: "The native loadout consumer does not support this armor encumbrance branch.");
            yield return Text("loadout", "Capacity", CharacterRuleEffectMetric.Condition, rules.CapacityBranch.ToString(), "capacity");
            yield return Text("loadout", "Armor", CharacterRuleEffectMetric.Condition, rules.ArmorEncumbranceBranch.ToString(), "encumbrance");
        }
    }

    internal static IEnumerable<CharacterRuleEffectValue> Qualities(CharacterCreationQualitiesPreview preview)
    {
        foreach (var quality in preview.Selections)
        {
            string id = quality.SelectionKey;
            yield return Number(id, quality.Name, CharacterRuleEffectMetric.Rating, quality.Rating);
            yield return Number(id, quality.Name, CharacterRuleEffectMetric.KarmaCost, quality.KarmaCost);
            yield return Text(id, quality.Name, CharacterRuleEffectMetric.Selection, quality.FollowUpChoiceLabel ?? "");
        }
        foreach (var quality in preview.GrantedQualities)
        {
            string id = "grant/" + quality.GrantId;
            yield return Number(id, quality.Name, CharacterRuleEffectMetric.Rating, quality.Rating);
            yield return Number(id, quality.Name, CharacterRuleEffectMetric.KarmaCost, quality.KarmaCost);
            yield return Text(id, quality.Name, CharacterRuleEffectMetric.Selection, quality.Origin);
        }
        yield return Number("qualities", "Positive qualities", CharacterRuleEffectMetric.PositiveKarma, preview.PositiveQualityBudget.Used);
        yield return Number("qualities", "Negative qualities", CharacterRuleEffectMetric.NegativeKarma, preview.NegativeQualityBudget.Used);
        yield return Number("qualities", "Creation Karma", CharacterRuleEffectMetric.KarmaRemaining, preview.KarmaRemaining);
    }

    internal static IEnumerable<CharacterRuleEffectValue> Lifestyles(IEnumerable<CharacterCreationLifestyleProjection> lifestyles)
    {
        foreach (var lifestyle in lifestyles)
        {
            string id = lifestyle.Configuration.LifestyleId.ToString("D");
            yield return Number(id, lifestyle.BaseLifestyleName, CharacterRuleEffectMetric.UnitCost, lifestyle.Economics.CostPerIncrement);
            yield return Number(id, lifestyle.BaseLifestyleName, CharacterRuleEffectMetric.TotalCost, lifestyle.Economics.TotalCost);
            yield return Number(id, lifestyle.BaseLifestyleName, CharacterRuleEffectMetric.BudgetTotal, lifestyle.Economics.LifestylePointsTotal);
            yield return Number(id, lifestyle.BaseLifestyleName, CharacterRuleEffectMetric.BudgetRemaining, lifestyle.Economics.LifestylePointsRemaining);
            yield return Flag(id, lifestyle.BaseLifestyleName, CharacterRuleEffectMetric.Available, lifestyle.Economics.CoveredByTrustFund, "trust-fund");
        }
    }

    internal static IEnumerable<CharacterRuleEffectValue> Magic(IEnumerable<CharacterCreationMagicResonanceOptionFinalizationSource> options)
    {
        foreach (var option in options)
        {
            string id = option.Identity.ToString();
            yield return Number(id, option.Name, CharacterRuleEffectMetric.Rating, option.Levels);
            yield return Number(id, option.Name, CharacterRuleEffectMetric.RulePointCost, option.PointCost, "selected-option-cost");
            yield return Text(id, option.Name, CharacterRuleEffectMetric.SourceBook, option.SourceBook);
        }
    }
}
