using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class CharacterGearQuantityRulesTests
{
    [TestMethod]
    public void Precision_matches_integer_currency_and_settings_derived_nuyen_rules()
    {
        Assert.IsTrue(CharacterGearQuantityRules.TryResolvePrecision("Ammo", "Ammunition", null, out int integerPlaces, out decimal integerStep));
        Assert.AreEqual(0, integerPlaces);
        Assert.AreEqual(1m, integerStep);
        Assert.IsTrue(CharacterGearQuantityRules.TryResolvePrecision("Certified credstick", "Currency", null, out int currencyPlaces, out decimal currencyStep));
        Assert.AreEqual(2, currencyPlaces);
        Assert.AreEqual(0.01m, currencyStep);
        Assert.IsTrue(CharacterGearQuantityRules.TryResolvePrecision("Nuyen", "Currency", 3, out int nuyenPlaces, out decimal nuyenStep));
        Assert.AreEqual(3, nuyenPlaces);
        Assert.AreEqual(0.001m, nuyenStep);
        Assert.IsFalse(CharacterGearQuantityRules.TryResolvePrecision("Nuyen", "Currency", null, out _, out _));
        Assert.IsTrue(CharacterGearQuantityRules.IsValidAmount(1.125m, nuyenStep));
        Assert.IsFalse(CharacterGearQuantityRules.IsValidAmount(1.1255m, nuyenStep));
    }

    [TestMethod]
    public void Merge_identity_ignores_only_superficials_and_deep_matches_children_as_a_multiset()
    {
        CharacterGearMergeIdentity chip = Identity("Chip", children: []);
        CharacterGearMergeIdentity module = Identity("Module", children: []);
        CharacterGearMergeIdentity left = Identity(
            "Deck",
            gearName: "Left",
            notes: "Left notes",
            children:
            [
                new(2m, chip),
                new(1m, module)
            ]);
        CharacterGearMergeIdentity right = Identity(
            "Deck",
            gearName: "Right",
            notes: "Right notes",
            children:
            [
                new(1m, module),
                new(2m, chip)
            ]);

        Assert.IsTrue(CharacterGearQuantityRules.AreIdenticalForMerge(left, right));
        Assert.IsFalse(CharacterGearQuantityRules.AreIdenticalForMerge(left, right, ignoreSuperficials: false));
        Assert.IsFalse(CharacterGearQuantityRules.AreIdenticalForMerge(
            left,
            right with { Children = [new(1m, module), new(1m, chip)] }));
    }

    [TestMethod]
    public void Cost_expression_accepts_legacy_rating_arithmetic_and_fails_closed_on_functions()
    {
        Assert.IsTrue(CharacterGearQuantityRules.TryEvaluateCostExpression("(Rating * 250) + 25", 3, out decimal value));
        Assert.AreEqual(775m, value);
        Assert.IsFalse(CharacterGearQuantityRules.TryEvaluateCostExpression("FixedValues(10,20)", 2, out _));
        Assert.IsFalse(CharacterGearQuantityRules.TryEvaluateCostExpression("Rating / 0", 2, out _));
    }

    [TestMethod]
    public void Purchase_cost_overflow_fails_closed_without_escaping()
    {
        CharacterGearCostSnapshot costlyChild = new(
            Rating: 0,
            Quantity: 2m,
            CostExpression: decimal.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CostFor: 1m,
            DiscountedCost: false,
            ChildCostMultiplier: 1,
            Children: []);
        CharacterGearCostSnapshot parent = new(
            Rating: 0,
            Quantity: 1m,
            CostExpression: "0",
            CostFor: 1m,
            DiscountedCost: false,
            ChildCostMultiplier: 1,
            Children: [costlyChild]);

        Assert.IsFalse(CharacterGearQuantityRules.TryCalculatePurchaseUnitCost(parent, out _));
    }

    private static CharacterGearMergeIdentity Identity(
        string name,
        string gearName = "",
        string notes = "",
        IReadOnlyList<CharacterGearMergeChildIdentity>? children = null)
        => new(
            Name: name,
            Category: "Electronics",
            Rating: 2,
            Extra: "",
            GearName: gearName,
            Notes: notes,
            Children: children ?? []);
}
