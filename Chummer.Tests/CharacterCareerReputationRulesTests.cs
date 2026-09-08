using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerReputationRulesTests
{
    private static CharacterCareerReputationInputs Inputs => new(
        "sr5", true, 80, 3, 4, 2, 0, 0, 0, 0, 0, true, false);

    [TestMethod]
    public void Manual_awards_and_earned_totals_remain_distinct()
    {
        Assert.IsTrue(CharacterCareerReputationRules.TryProject(Inputs, out var projected));
        Assert.IsNotNull(projected);
        Assert.AreEqual(8, projected.CalculatedStreetCred);
        Assert.AreEqual(11, projected.TotalStreetCred);
        Assert.AreEqual(0, projected.CalculatedNotoriety);
        Assert.AreEqual(4, projected.TotalNotoriety);
        Assert.AreEqual(5, projected.CalculatedPublicAwareness);
        Assert.AreEqual(7, projected.TotalPublicAwareness);
        Assert.IsTrue(projected.CanBurnStreetCred);
        Assert.AreEqual(3, projected.Inputs.StreetCred);
    }

    [TestMethod]
    public void Burn_changes_only_burnt_counter_and_recomputes_all_effective_totals()
    {
        var inputs = Inputs;
        Assert.IsTrue(CharacterCareerReputationRules.TryQuoteBurnStreetCred(inputs, out var quote));
        Assert.IsNotNull(quote);
        Assert.AreEqual(CharacterCareerReputationOperation.BurnStreetCred, quote.Operation);
        Assert.AreEqual(inputs with { BurntStreetCred = 2 }, quote.After.Inputs);
        Assert.AreEqual(9, quote.After.TotalStreetCred);
        Assert.AreEqual(3, quote.After.TotalNotoriety);
        Assert.AreEqual(6, quote.After.TotalPublicAwareness);
        Assert.AreEqual(4, quote.After.Inputs.Notoriety, "Manual notoriety must not also lose one.");
        Assert.AreEqual(inputs, quote.Before.Inputs);
        Assert.AreEqual(0, inputs.BurntStreetCred);
    }

    [TestMethod]
    public void Manual_signed_changes_recompute_but_do_not_modify_earned_karma_or_burn_counter()
    {
        Assert.IsTrue(CharacterCareerReputationRules.TryQuoteAdjustment(Inputs, new(-2, 3, -1), out var quote));
        Assert.IsNotNull(quote);
        Assert.AreEqual(CharacterCareerReputationOperation.AdjustManualAwards, quote.Operation);
        Assert.AreEqual(Inputs with { StreetCred = 1, Notoriety = 7, PublicAwareness = 1 }, quote.After.Inputs);
        Assert.AreEqual(9, quote.After.TotalStreetCred);
        Assert.AreEqual(7, quote.After.TotalNotoriety);
        Assert.AreEqual(6, quote.After.TotalPublicAwareness);
    }

    [TestMethod]
    public void Active_improvements_divisor_and_public_awareness_setting_are_explicit_inputs()
    {
        var inputs = Inputs with
        {
            StreetCredDivisorAdjustment = 10, StreetCredImprovement = 5,
            NotorietyImprovement = -2, PublicAwarenessImprovement = 3, UseCalculatedPublicAwareness = false
        };
        Assert.IsTrue(CharacterCareerReputationRules.TryProject(inputs, out var projected));
        Assert.IsNotNull(projected);
        Assert.AreEqual(4, projected.CalculatedStreetCred);
        Assert.AreEqual(12, projected.TotalStreetCred);
        Assert.AreEqual(2, projected.TotalNotoriety);
        Assert.AreEqual(5, projected.TotalPublicAwareness);
        Assert.IsTrue(CharacterCareerReputationRules.TryQuoteBurnStreetCred(inputs, out var quote));
        Assert.AreEqual(5, quote!.After.TotalPublicAwareness, "Disabled derived awareness must stay disabled.");
    }

    [DataRow(-3, -3)]
    [DataRow(0, 0)]
    [DataRow(1, 1)]
    [DataRow(9, 1)]
    [TestMethod]
    public void Erased_caps_only_positive_public_awareness(int manual, int expected)
    {
        Assert.IsTrue(CharacterCareerReputationRules.TryProject(Inputs with
        { Erased = true, PublicAwareness = manual, UseCalculatedPublicAwareness = false }, out var projected));
        Assert.AreEqual(expected, projected!.TotalPublicAwareness);
    }

    [TestMethod]
    public void Negative_effective_notoriety_is_not_clamped_or_used_to_disable_burning()
    {
        var inputs = Inputs with { BurntStreetCred = 3, Notoriety = 0, NotorietyImprovement = -3 };
        Assert.IsTrue(CharacterCareerReputationRules.TryQuoteBurnStreetCred(inputs, out var quote));
        Assert.IsNotNull(quote);
        Assert.AreEqual(-4, quote.Before.TotalNotoriety);
        Assert.AreEqual(-5, quote.After.TotalNotoriety);
        Assert.AreEqual(5, quote.After.Inputs.BurntStreetCred);
    }

    [TestMethod]
    public void Signed_integer_division_and_negative_divisor_match_oracle_not_floor_reimplementation()
    {
        Assert.IsTrue(CharacterCareerReputationRules.TryProject(Inputs with
        { CareerKarma = -19, StreetCred = 5, Notoriety = -8, PublicAwareness = 0 }, out var negative));
        Assert.AreEqual(-1, negative!.CalculatedStreetCred);
        Assert.AreEqual(-1, negative.CalculatedPublicAwareness); // (4 + -8) / 3
        Assert.IsTrue(CharacterCareerReputationRules.TryProject(Inputs with
        { CareerKarma = 19, StreetCredDivisorAdjustment = -20 }, out var divisor));
        Assert.AreEqual(-1, divisor!.CalculatedStreetCred);
    }

    [DataRow(1, false)]
    [DataRow(2, true)]
    [DataRow(3, true)]
    [TestMethod]
    public void Burn_eligibility_uses_total_cred_including_awards(int manual, bool allowed)
    {
        Assert.AreEqual(allowed, CharacterCareerReputationRules.TryQuoteBurnStreetCred(
            Inputs with { CareerKarma = 0, StreetCred = manual }, out var quote));
        Assert.AreEqual(allowed, quote is not null);
    }

    [DataRow("sr4", true)]
    [DataRow("sr6", true)]
    [DataRow("SR5", true)]
    [DataRow("sr5 ", true)]
    [DataRow("", true)]
    [DataRow("sr5", false)]
    [TestMethod]
    public void Noncanonical_edition_or_creation_mode_cannot_quote(string ruleset, bool career)
    {
        var inputs = Inputs with { RulesetId = ruleset, IsCareer = career };
        Assert.IsFalse(CharacterCareerReputationRules.TryProject(inputs, out var state));
        Assert.IsNull(state);
        Assert.IsFalse(CharacterCareerReputationRules.TryQuoteBurnStreetCred(inputs, out _));
        Assert.IsFalse(CharacterCareerReputationRules.TryQuoteAdjustment(inputs, new(1), out _));
    }

    [TestMethod]
    public void Invalid_or_overflowing_projection_never_returns_a_partial_result()
    {
        CharacterCareerReputationInputs[] invalid =
        [
            Inputs with { BurntStreetCred = -1 },
            Inputs with { StreetCredDivisorAdjustment = -10 },
            Inputs with { StreetCredDivisorAdjustment = int.MaxValue },
            Inputs with { CareerKarma = int.MinValue, StreetCredDivisorAdjustment = -11 },
            Inputs with { StreetCred = int.MaxValue },
            Inputs with { Notoriety = int.MaxValue, NotorietyImprovement = 1 },
            Inputs with { NotorietyImprovement = int.MinValue, BurntStreetCred = 2 },
            Inputs with { PublicAwarenessImprovement = int.MaxValue },
            Inputs with { PublicAwareness = int.MaxValue },
            Inputs with { Notoriety = int.MaxValue }
        ];
        foreach (var inputs in invalid)
        {
            Assert.IsFalse(CharacterCareerReputationRules.TryProject(inputs, out var projected));
            Assert.IsNull(projected);
            Assert.IsFalse(CharacterCareerReputationRules.TryQuoteBurnStreetCred(inputs, out var quote));
            Assert.IsNull(quote);
        }
    }

    [TestMethod]
    public void Burn_counter_overflow_fails_even_when_effective_cred_can_afford_it()
    {
        var inputs = Inputs with
        { CareerKarma = int.MaxValue, StreetCredDivisorAdjustment = -9, BurntStreetCred = int.MaxValue, StreetCred = 2 };
        Assert.IsTrue(CharacterCareerReputationRules.TryProject(inputs, out var before));
        Assert.IsTrue(before!.CanBurnStreetCred);
        Assert.IsFalse(CharacterCareerReputationRules.TryQuoteBurnStreetCred(inputs, out var quote));
        Assert.IsNull(quote);
    }

    [TestMethod]
    public void Selected_manual_entries_use_legacy_bounds_not_limits_on_derived_totals()
    {
        Assert.IsTrue(CharacterCareerReputationRules.TryQuoteAdjustment(Inputs, new(97), out var maximum));
        Assert.AreEqual(108, maximum!.After.TotalStreetCred);
        Assert.IsTrue(CharacterCareerReputationRules.TryQuoteAdjustment(Inputs, new(-3), out var zero));
        Assert.AreEqual(0, zero!.After.Inputs.StreetCred);
        foreach (var delta in new[] { new CharacterCareerReputationAdjustment(98), new(-4), new(null, -5),
                     new(null, 97), new(null, null, -3), new(null, null, 99), new(int.MaxValue), new(int.MinValue) })
            Assert.IsFalse(CharacterCareerReputationRules.TryQuoteAdjustment(Inputs, delta, out _));
    }

    [TestMethod]
    public void A_valid_manual_entry_cannot_hide_overflow_in_its_effective_result()
    {
        var inputs = Inputs with { CareerKarma = 0, StreetCred = 0,
            StreetCredImprovement = int.MaxValue - 1, UseCalculatedPublicAwareness = false };
        Assert.IsTrue(CharacterCareerReputationRules.TryProject(inputs, out _));
        Assert.IsFalse(CharacterCareerReputationRules.TryQuoteAdjustment(inputs, new(2), out var quote));
        Assert.IsNull(quote);
    }

    [TestMethod]
    public void Unselected_imported_values_are_preserved_and_zero_changes_are_not_new_operations()
    {
        var imported = Inputs with { StreetCred = 150, Notoriety = -20 };
        Assert.IsTrue(CharacterCareerReputationRules.TryQuoteAdjustment(imported, new(null, null, 1), out var quote));
        Assert.AreEqual(imported with { PublicAwareness = 3 }, quote!.After.Inputs);
        Assert.IsFalse(CharacterCareerReputationRules.TryQuoteAdjustment(imported, new(0), out _));
        Assert.IsFalse(CharacterCareerReputationRules.TryQuoteAdjustment(Inputs, new(), out _));
        Assert.IsFalse(CharacterCareerReputationRules.TryQuoteAdjustment(Inputs, new(0, 0, 0), out _));
    }

    [TestMethod]
    public void Quotes_are_deterministic_and_reproject_to_the_same_outcomes()
    {
        for (int karma = 0; karma <= 200; karma += 10)
        for (int burnt = 0; burnt <= 6; burnt++)
        for (int awarded = 0; awarded <= 3; awarded++)
        {
            var inputs = Inputs with { CareerKarma = karma, BurntStreetCred = burnt, StreetCred = awarded };
            bool available = CharacterCareerReputationRules.TryQuoteBurnStreetCred(inputs, out var first);
            Assert.AreEqual(available, CharacterCareerReputationRules.TryQuoteBurnStreetCred(inputs, out var second));
            Assert.AreEqual(first, second);
            if (first is null) continue;
            Assert.IsTrue(CharacterCareerReputationRules.TryProject(first.After.Inputs, out var reprojected));
            Assert.AreEqual(first.After, reprojected);
            Assert.AreEqual(first.Before.TotalStreetCred - 2, first.After.TotalStreetCred);
            Assert.AreEqual(first.Before.TotalNotoriety - 1, first.After.TotalNotoriety);
        }
    }
}
