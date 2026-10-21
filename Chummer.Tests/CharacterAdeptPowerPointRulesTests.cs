using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterAdeptPowerPointRulesTests
{
    [TestMethod]
    public void Saved_adept_power_preserves_its_fixed_rating_cost()
    {
        XElement runner = SavedRunner("Apex Predator.chum5");
        Assert.AreEqual("True", runner.Element("adept")!.Value);
        Assert.AreEqual("False", runner.Element("magician")!.Value);
        XElement power = runner.Element("powers")!.Elements("power").Single();
        Assert.AreEqual("Master Archer", power.Element("name")!.Value);
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryCalculateCost(SavedPower(power), out var cost));
        Assert.AreEqual(1m, cost!.UsedPoints);
        Assert.AreEqual(1, cost.TotalRating);
    }

    [TestMethod]
    public void Saved_mystic_adept_power_entries_keep_fractional_costs()
    {
        // Real existing saved runner, not a fabricated whole-point power list.
        // This is arithmetic parity only: the fixture is still in Creation.
        XElement runner = SavedRunner("Harmony.chum5");
        Assert.AreEqual("True", runner.Element("adept")!.Value);
        Assert.AreEqual("True", runner.Element("magician")!.Value);
        decimal used = 0m;
        foreach (var power in runner.Element("powers")!.Elements("power"))
        {
            Assert.IsTrue(CharacterAdeptPowerPointRules.TryCalculateCost(SavedPower(power), out var cost));
            used += cost!.UsedPoints;
        }
        Assert.AreEqual(6m, used);
    }

    [TestMethod]
    public void Mystic_pool_is_saved_purchase_plus_improvements_not_magic_or_grade()
    {
        var pool = new CharacterAdeptPowerPointPoolInput(true, true, false, 6, 2, 0.5m);
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryCalculatePool(pool, out decimal total));
        Assert.AreEqual(2.5m, total);
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryCalculatePool(
            pool with { UsesSeparateAdeptMagic = true, EffectiveAdeptMagic = 3 }, out total));
        Assert.AreEqual(3.5m, total);
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryCalculatePool(
            pool with { IsMagician = false }, out total));
        Assert.AreEqual(6.5m, total);
        Assert.IsFalse(CharacterAdeptPowerPointRules.TryCalculatePool(
            pool with { IsAdept = false }, out _));
    }

    [TestMethod]
    public void Way_discount_is_applied_once_not_per_level()
    {
        // Combat Sense's source price is .5 and its admitted Way discount .25.
        var power = Power() with { PaidRating = 2, AppliedWayDiscount = 0.25m };
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryQuoteIncrease(power, 3, 6m, 0m, out var quote));
        Assert.AreEqual(0.75m, quote!.Current.UsedPoints);
        Assert.AreEqual(1.25m, quote.Proposed.UsedPoints);
        Assert.AreEqual(0.5m, quote.IncrementalCost);
    }

    [TestMethod]
    public void Partial_free_points_are_not_lost_or_counted_twice()
    {
        var power = Power() with { PaidRating = 1, FreePointQuarterUnits = 3 };
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryCalculateCost(power, out var cost));
        Assert.AreEqual(1, cost!.FreeLevels);
        Assert.AreEqual(2, cost.TotalRating);
        Assert.AreEqual(0.75m, cost.FreePoints);
        Assert.AreEqual(0.25m, cost.UsedPoints);
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryCalculateCost(
            power with { GrantedLevels = 1 }, out cost));
        Assert.AreEqual(2, cost!.FreeLevels);
        Assert.AreEqual(3, cost.TotalRating);
        Assert.AreEqual(0.5m, cost.UsedPoints);
    }

    [TestMethod]
    public void Free_only_and_non_levelled_granted_powers_cost_no_paid_points()
    {
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryCalculateCost(
            Power() with { PaidRating = 0, FreePointQuarterUnits = 4 }, out var free));
        Assert.AreEqual(2, free!.TotalRating);
        Assert.AreEqual(0m, free.UsedPoints);
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryCalculateCost(
            Power() with { LevelsEnabled = false, MaximumTotalRating = 1, GrantedLevels = 1 },
            out var fixedPower));
        Assert.AreEqual(1, fixedPower!.TotalRating);
        Assert.AreEqual(0m, fixedPower.UsedPoints);
    }

    [TestMethod]
    public void Improved_reflexes_entry_fee_is_not_recharged_on_increase()
    {
        var reflexes = Power() with { PointsPerLevel = 1m, ExtraPointCost = 0.5m, MaximumTotalRating = 3 };
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryQuoteIncrease(reflexes, 2, 6m, 0m, out var quote));
        Assert.AreEqual(1.5m, quote!.Current.UsedPoints);
        Assert.AreEqual(2.5m, quote.Proposed.UsedPoints);
        Assert.AreEqual(1m, quote.IncrementalCost);
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryCalculateCost(
            reflexes with { PaidRating = 0, FreePointQuarterUnits = 5 }, out var tooFew));
        Assert.AreEqual(0, tooFew!.TotalRating);
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryCalculateCost(
            reflexes with { PaidRating = 0, FreePointQuarterUnits = 6 }, out var firstFree));
        Assert.AreEqual(1, firstFree!.TotalRating);
    }

    [TestMethod]
    public void Entry_fee_free_levels_match_legacy_whole_point_branch()
    {
        // Deliberately not a new rule interpretation: preserve Power.FreeLevels.
        var power = Power() with { PaidRating = 0, PointsPerLevel = 0.25m,
            ExtraPointCost = 0.5m, FreePointQuarterUnits = 7 };
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryCalculateCost(power, out var cost));
        Assert.AreEqual(2, cost!.TotalRating);
    }

    [TestMethod]
    public void Insufficient_points_quote_retains_exact_before_after_numbers()
    {
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryQuoteIncrease(Power(), 2, 1.25m, 0.5m, out var quote));
        Assert.AreEqual(0.25m, quote!.CurrentRemainingPoints);
        Assert.AreEqual(-0.25m, quote.ProposedRemainingPoints);
        Assert.IsFalse(quote.HasEnoughPoints);
    }

    [TestMethod]
    public void Duplicate_decrease_over_cap_and_ineffective_increases_are_rejected()
    {
        Assert.IsFalse(CharacterAdeptPowerPointRules.TryQuoteIncrease(Power(), 1, 6m, 0m, out _));
        Assert.IsFalse(CharacterAdeptPowerPointRules.TryQuoteIncrease(Power(), 0, 6m, 0m, out _));
        Assert.IsFalse(CharacterAdeptPowerPointRules.TryQuoteIncrease(Power(), 7, 6m, 0m, out _));
        Assert.IsFalse(CharacterAdeptPowerPointRules.TryQuoteIncrease(
            Power() with { GrantedLevels = 5 }, 2, 6m, 0m, out _));
    }

    [TestMethod]
    public void Free_level_magic_cap_is_distinct_from_source_or_skill_cap()
    {
        var power = Power() with { MaximumTotalRating = 3, FreeLevelMagicLimit = 2,
            GrantedLevels = 10, PaidRating = 2 };
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryCalculateCost(power, out var cost));
        Assert.AreEqual(2, cost!.FreeLevels);
        Assert.AreEqual(3, cost.TotalRating);
        Assert.AreEqual(1m, cost.UsedPoints);
    }

    [TestMethod]
    public void Invalid_and_overflowing_inputs_do_not_produce_a_quote()
    {
        Assert.IsFalse(CharacterAdeptPowerPointRules.TryCalculateCost(
            Power() with { PointsPerLevel = -0.25m }, out _));
        Assert.IsFalse(CharacterAdeptPowerPointRules.TryCalculateCost(
            Power() with { PaidRating = 2, PointsPerLevel = decimal.MaxValue }, out _));
        Assert.IsFalse(CharacterAdeptPowerPointRules.TryCalculatePool(
            new(true, false, false, 6, 0, decimal.MaxValue), out _));
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryCalculateCost(
            Power() with { PointsPerLevel = 0.0000000000000000000000000001m,
                FreePointQuarterUnits = int.MaxValue }, out var tiny));
        Assert.AreEqual(6, tiny!.FreeLevels);
    }

    private static CharacterAdeptPowerPointInput Power() => new(
        PaidRating: 1, LevelsEnabled: true, MaximumTotalRating: 6, FreeLevelMagicLimit: 6,
        PointsPerLevel: 0.5m, ExtraPointCost: 0m, AppliedWayDiscount: 0m,
        GrantedLevels: 0, FreePointQuarterUnits: 0);

    private static XElement SavedRunner(string name) =>
        XElement.Load(Path.Combine(AppContext.BaseDirectory, "TestFiles", name));

    private static CharacterAdeptPowerPointInput SavedPower(XElement power)
    {
        // A fixture adapter for arithmetic tests only. It intentionally does not
        // pretend saved numeric fields provide enabled-source or Career authority.
        bool levels = bool.Parse(power.Element("levels")!.Value);
        Assert.AreEqual("False", power.Element("discounted")!.Value);
        return new(int.Parse(power.Element("rating")!.Value, CultureInfo.InvariantCulture),
            levels, levels ? 6 : 1, 6,
            decimal.Parse(power.Element("pointsperlevel")!.Value, CultureInfo.InvariantCulture),
            decimal.Parse(power.Element("extrapointcost")!.Value, CultureInfo.InvariantCulture),
            0m, 0, 0);
    }
}
