using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerAttributeAdvanceRulesTests
{
    [TestMethod]
    public void Catalog_is_exact_and_rejects_foreign_or_mismatched_targets()
    {
        string[] expected =
        [
            "BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL",
            "EDG", "MAG", "MAGAdept", "RES"
        ];
        CollectionAssert.AreEqual(
            expected,
            CharacterCareerAttributeAdvanceRules.GetTargetCatalog()
                .Select(static identity => identity.Abbreviation)
                .ToArray());
        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryCreateIdentity("DEP", out _));
        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryCreateIdentity("mag", out _));
        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryCreateQuote(
            Input("BOD") with
            {
                Identity = new CharacterCareerAttributeIdentity("BOD", CharacterCareerAttributeKind.Edge)
            }, out _));
    }

    [TestMethod]
    public void Normal_attribute_quote_plan_and_receipt_bind_all_revisions()
    {
        CharacterCareerAttributeAdvanceQuote quote = Quote(Input("AGI") with
        {
            BasePoints = 2,
            KarmaPoints = 1,
            EffectiveValue = 3,
            NaturalMaximum = 6,
            AvailableKarma = 30
        });
        Assert.AreEqual(20, quote.KarmaCost);
        Assert.AreEqual(4, quote.TargetValue);
        Assert.AreEqual(TimeSpan.Zero, quote.ApplicationDuration);
        Assert.AreEqual(
            CharacterCareerAttributeTimeAuthority.ImmediateChummerPersistence,
            quote.TimeAuthority);
        Assert.IsTrue(quote.Prerequisites.All(static prerequisite => prerequisite.Satisfied));

        Guid expenseId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Assert.IsTrue(CharacterCareerAttributeAdvanceRules.TryPlanAdvance(
            quote,
            quote.LogicalRevision,
            quote.SourceRevision,
            quote.RuleDigest,
            confirmed: true,
            expenseId,
            new DateTime(2081, 5, 12, 14, 30, 0),
            out CharacterCareerAttributeAdvancePlan plan));
        Assert.AreEqual(2, plan.SavedAttributeKarmaPoints);
        Assert.AreEqual(10, plan.SavedCharacterKarma);
        Assert.AreEqual(-20, plan.ExpenseAmount);
        Assert.AreEqual("Attribute AGI 3 -> 4", plan.ExpenseReason);
        Assert.AreEqual("ImproveAttribute", plan.KarmaUndoType);
        Assert.AreEqual("AGI", plan.UndoObjectId);

        Guid transactionId = expenseId;
        Assert.IsTrue(CharacterCareerAttributeAdvanceRules.TryCreateReceipt(
            transactionId,
            quote,
            plan,
            observedAttributeKarma: 2,
            observedCharacterKarma: 10,
            observedBurnedEdgePoints: 0,
            expenseExistsExactlyOnce: true,
            out CharacterCareerAttributeAdvanceReceipt receipt));
        Assert.IsTrue(CharacterCareerAttributeAdvanceRules.IsCoherent(receipt));
        Assert.AreEqual(1, receipt.AttributeKarmaBefore);
        Assert.AreEqual(2, receipt.AttributeKarmaAfter);
    }

    [TestMethod]
    public void Edge_first_repairs_one_burn_without_incrementing_saved_karma()
    {
        CharacterCareerAttributeAdvanceQuote quote = Quote(Input("EDG") with
        {
            BasePoints = 3,
            KarmaPoints = 1,
            EffectiveValue = 2,
            NaturalMaximum = 6,
            BurnedEdgePoints = 2,
            AvailableKarma = 20
        });
        Assert.IsTrue(quote.RepairsBurnedEdge);
        Assert.AreEqual(15, quote.KarmaCost);

        Assert.IsTrue(CharacterCareerAttributeAdvanceRules.TryPlanAdvance(
            quote,
            quote.LogicalRevision,
            quote.SourceRevision,
            quote.RuleDigest,
            true,
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            new DateTime(2081, 5, 12),
            out CharacterCareerAttributeAdvancePlan plan));
        Assert.AreEqual(1, plan.SavedAttributeKarmaPoints);
        Assert.AreEqual(2, plan.BurnedEdgePointsBefore);
        Assert.AreEqual(1, plan.SavedBurnedEdgePoints);
        Assert.AreEqual(5, plan.SavedCharacterKarma);
    }

    [TestMethod]
    public void Magic_alias_and_resonance_require_their_exact_enable_authority()
    {
        foreach (string abbreviation in new[] { "MAG", "MAGAdept" })
        {
            CharacterCareerAttributeAdvanceQuote disabled = Quote(Input(abbreviation) with
            {
                MagicEnabled = false
            });
            Assert.AreEqual(
                CharacterCareerAttributeAdvanceBlocker.SpecialAttributeDisabled,
                disabled.Blocker);
            Assert.IsFalse(disabled.CanAdvance);

            CharacterCareerAttributeAdvanceQuote enabled = Quote(Input(abbreviation));
            Assert.IsTrue(enabled.CanAdvance);
            Assert.AreEqual(abbreviation, enabled.Identity.Abbreviation);
        }

        CharacterCareerAttributeAdvanceQuote secondMagicDisabled = Quote(Input("MAGAdept") with
        {
            MysticAdeptSecondMagicAttributeEnabled = false
        });
        Assert.AreEqual(
            CharacterCareerAttributeAdvanceBlocker.SpecialAttributeDisabled,
            secondMagicDisabled.Blocker);

        CharacterCareerAttributeAdvanceQuote resonance = Quote(Input("RES") with
        {
            ResonanceEnabled = false
        });
        Assert.AreEqual(
            CharacterCareerAttributeAdvanceBlocker.SpecialAttributeDisabled,
            resonance.Blocker);
    }

    [TestMethod]
    public void Alternate_metatype_formula_matches_normal_and_special_exception_authority()
    {
        CharacterCareerAttributeAdvanceQuote normal = Quote(Input("BOD") with
        {
            EffectiveValue = 3,
            MetatypeMinimum = 3,
            Settings = new CharacterCareerAttributeAdvanceSettings(5, true)
        });
        Assert.AreEqual(10, normal.KarmaCost, "(3 + 1) * 5 - (3 - 1) * 5");

        CharacterCareerAttributeAdvanceQuote magic = Quote(Input("MAG") with
        {
            EffectiveValue = 3,
            MetatypeMinimum = 3,
            Settings = new CharacterCareerAttributeAdvanceSettings(5, true)
        });
        Assert.AreEqual(20, magic.KarmaCost, "MAG is an exact Chummer5 exception.");

        CharacterCareerAttributeAdvanceQuote edge = Quote(Input("EDG") with
        {
            EffectiveValue = 3,
            MetatypeMinimum = 3,
            Settings = new CharacterCareerAttributeAdvanceSettings(5, true)
        });
        Assert.AreEqual(10, edge.KarmaCost, "EDG is deliberately not an exception in Chummer5A.");
    }

    [TestMethod]
    public void Zero_rating_modifiers_threshold_rounding_and_minimum_clamp_are_exact()
    {
        CharacterCareerAttributeAdvanceQuote zero = Quote(Input("LOG") with
        {
            BasePoints = 0,
            KarmaPoints = 0,
            EffectiveValue = 0,
            Settings = new CharacterCareerAttributeAdvanceSettings(5, false)
        });
        Assert.AreEqual(5, zero.KarmaCost);

        CharacterCareerAttributeAdvanceQuote modified = Quote(Input("LOG") with
        {
            EffectiveValue = 2,
            Modifiers =
            [
                Modifier('1', CharacterCareerAttributeKarmaModifierKind.AttributeKarmaCostMultiplier,
                    "LOG", minimum: 3, maximum: 3, value: 50m),
                Modifier('2', CharacterCareerAttributeKarmaModifierKind.AttributeKarmaCost,
                    string.Empty, minimum: 0, maximum: 0, value: 0.2m),
                Modifier('3', CharacterCareerAttributeKarmaModifierKind.AttributeKarmaCost,
                    "LOG", minimum: 4, maximum: 0, value: 99m)
            ]
        });
        Assert.AreEqual(8, modified.KarmaCost, "15 * .5 + .2 rounds away from zero.");

        CharacterCareerAttributeAdvanceQuote clamped = Quote(Input("WIL") with
        {
            EffectiveValue = 1,
            Settings = new CharacterCareerAttributeAdvanceSettings(5, false),
            Modifiers =
            [
                Modifier('4', CharacterCareerAttributeKarmaModifierKind.AttributeKarmaCost,
                    "WIL", 0, 0, -100m)
            ]
        });
        Assert.AreEqual(1, clamped.KarmaCost);
    }

    [TestMethod]
    public void Natural_max_insufficient_karma_creation_and_ruleset_blockers_are_explicit()
    {
        Assert.AreEqual(
            CharacterCareerAttributeAdvanceBlocker.AtNaturalMaximum,
            Quote(Input("REA") with { EffectiveValue = 6, NaturalMaximum = 6 }).Blocker);
        Assert.AreEqual(
            CharacterCareerAttributeAdvanceBlocker.InsufficientKarma,
            Quote(Input("REA") with { EffectiveValue = 3, AvailableKarma = 19 }).Blocker);
        Assert.AreEqual(
            CharacterCareerAttributeAdvanceBlocker.NotCareerCharacter,
            Quote(Input("REA") with { Created = false }).Blocker);
        Assert.AreEqual(
            CharacterCareerAttributeAdvanceBlocker.UnsupportedRuleset,
            Quote(Input("REA") with { RulesetId = "sr6" }).Blocker);
    }

    [TestMethod]
    public void Plan_rejects_unconfirmed_stale_source_rule_or_logical_digest()
    {
        CharacterCareerAttributeAdvanceQuote quote = Quote(Input("STR"));
        Guid expenseId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        DateTime date = new(2081, 5, 12);
        Assert.IsFalse(Plan(quote, false, quote.LogicalRevision, quote.SourceRevision, quote.RuleDigest));
        Assert.IsFalse(Plan(quote, true, Hex('0'), quote.SourceRevision, quote.RuleDigest));
        Assert.IsFalse(Plan(quote, true, quote.LogicalRevision, Hex('0'), quote.RuleDigest));
        Assert.IsFalse(Plan(quote, true, quote.LogicalRevision, quote.SourceRevision, Hex('0')));

        bool Plan(
            CharacterCareerAttributeAdvanceQuote candidate,
            bool confirmed,
            string logical,
            string source,
            string rules)
            => CharacterCareerAttributeAdvanceRules.TryPlanAdvance(
                candidate, logical, source, rules, confirmed, expenseId, date, out _);
    }

    [TestMethod]
    public void Source_settings_flags_maximum_modifiers_and_edge_burn_are_digest_bound()
    {
        CharacterCareerAttributeAdvanceInput input = Input("EDG") with { BurnedEdgePoints = 1 };
        CharacterCareerAttributeAdvanceQuote original = Quote(input);
        Assert.AreNotEqual(original.SourceRevision, Quote(input with
        {
            RawSourceState = input.RawSourceState + " "
        }).SourceRevision);
        Assert.AreNotEqual(original.RuleDigest, Quote(input with
        {
            Settings = input.Settings with { KarmaAttribute = 6 }
        }).RuleDigest);
        Assert.AreNotEqual(original.RuleDigest, Quote(input with
        {
            NaturalMaximum = 7
        }).RuleDigest);
        Assert.AreNotEqual(original.RuleDigest, Quote(input with
        {
            BurnedEdgePoints = 2
        }).RuleDigest);
        Assert.AreNotEqual(original.RuleDigest, Quote(input with
        {
            Modifiers =
            [
                Modifier('9', CharacterCareerAttributeKarmaModifierKind.AttributeKarmaCost,
                    "EDG", 0, 0, 1m)
            ]
        }).RuleDigest);
    }

    [TestMethod]
    public void Duplicate_or_foreign_modifier_authority_is_rejected()
    {
        CharacterCareerAttributeKarmaModifier modifier = Modifier(
            'a', CharacterCareerAttributeKarmaModifierKind.AttributeKarmaCost,
            "AGI", 0, 0, 1m);
        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryCreateQuote(
            Input("AGI") with { Modifiers = [modifier, modifier] }, out _));
        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryCreateQuote(
            Input("AGI") with
            {
                Modifiers =
                [
                    Modifier('b', CharacterCareerAttributeKarmaModifierKind.AttributeKarmaCost,
                        "BOD", 0, 0, 1m)
                ]
            }, out _));
    }

    [TestMethod]
    public void Receipt_is_exact_and_compensating_correction_requires_unchanged_post_state()
    {
        CharacterCareerAttributeAdvanceQuote quote = Quote(Input("CHA") with
        {
            KarmaPoints = 1,
            EffectiveValue = 3,
            AvailableKarma = 30
        });
        Assert.IsTrue(CharacterCareerAttributeAdvanceRules.TryPlanAdvance(
            quote, quote.LogicalRevision, quote.SourceRevision, quote.RuleDigest, true,
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            new DateTime(2081, 5, 12), out CharacterCareerAttributeAdvancePlan plan));
        Assert.IsTrue(CharacterCareerAttributeAdvanceRules.TryCreateReceipt(
            plan.ExpenseId,
            quote, plan, plan.SavedAttributeKarmaPoints, plan.SavedCharacterKarma,
            plan.SavedBurnedEdgePoints, true, out CharacterCareerAttributeAdvanceReceipt receipt));

        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryPlanCorrection(
            receipt,
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            "operator correction",
            observedAttributeKarma: receipt.AttributeKarmaAfter + 1,
            observedCharacterKarma: receipt.CharacterKarmaAfter,
            observedBurnedEdgePoints: receipt.BurnedEdgePointsAfter,
            expenseExistsExactlyOnce: true,
            correctionIdAlreadyExists: false,
            expectedReceiptDigest: receipt.ReceiptDigest,
            out _));
        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryPlanCorrection(
            receipt,
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            "operator correction",
            receipt.AttributeKarmaAfter,
            receipt.CharacterKarmaAfter,
            receipt.BurnedEdgePointsAfter,
            expenseExistsExactlyOnce: false,
            correctionIdAlreadyExists: false,
            expectedReceiptDigest: receipt.ReceiptDigest,
            out _));
        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryPlanCorrection(
            receipt,
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            "operator correction",
            receipt.AttributeKarmaAfter,
            receipt.CharacterKarmaAfter,
            receipt.BurnedEdgePointsAfter,
            expenseExistsExactlyOnce: true,
            correctionIdAlreadyExists: true,
            expectedReceiptDigest: receipt.ReceiptDigest,
            out _));
        Assert.IsTrue(CharacterCareerAttributeAdvanceRules.TryPlanCorrection(
            receipt,
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            "operator correction",
            receipt.AttributeKarmaAfter,
            receipt.CharacterKarmaAfter,
            receipt.BurnedEdgePointsAfter,
            true,
            correctionIdAlreadyExists: false,
            expectedReceiptDigest: receipt.ReceiptDigest,
            out CharacterCareerAttributeCorrectionPlan correction));
        Assert.IsTrue(CharacterCareerAttributeAdvanceRules.IsCoherent(correction));
        Assert.AreEqual(receipt.AttributeKarmaBefore, correction.SavedAttributeKarmaPoints);
        Assert.AreEqual(receipt.CharacterKarmaBefore, correction.SavedCharacterKarma);
        Assert.AreEqual(receipt.ExpenseId, correction.ExpenseIdToRemove);
        Assert.AreEqual(receipt.ReceiptDigest, correction.OriginalReceiptDigest);
    }

    [TestMethod]
    public void Receipt_rejects_replay_shape_and_foreign_observed_state()
    {
        CharacterCareerAttributeAdvanceQuote quote = Quote(Input("INT"));
        Assert.IsTrue(CharacterCareerAttributeAdvanceRules.TryPlanAdvance(
            quote, quote.LogicalRevision, quote.SourceRevision, quote.RuleDigest, true,
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            new DateTime(2081, 5, 12), out CharacterCareerAttributeAdvancePlan plan));
        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryCreateReceipt(
            Guid.Empty, quote, plan, plan.SavedAttributeKarmaPoints, plan.SavedCharacterKarma,
            plan.SavedBurnedEdgePoints, true, out _));
        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryCreateReceipt(
            plan.ExpenseId, quote, plan, plan.SavedAttributeKarmaPoints,
            plan.SavedCharacterKarma + 1, plan.SavedBurnedEdgePoints, true, out _));
        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryCreateReceipt(
            plan.ExpenseId, quote, plan, plan.SavedAttributeKarmaPoints,
            plan.SavedCharacterKarma, plan.SavedBurnedEdgePoints, false, out _));
        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryCreateReceipt(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), quote, plan,
            plan.SavedAttributeKarmaPoints, plan.SavedCharacterKarma,
            plan.SavedBurnedEdgePoints, true, out _));
    }

    [TestMethod]
    public void Receipt_rejects_a_structurally_coherent_but_forged_plan()
    {
        CharacterCareerAttributeAdvanceQuote quote = Quote(Input("BOD"));
        Assert.IsTrue(CharacterCareerAttributeAdvanceRules.TryPlanAdvance(
            quote, quote.LogicalRevision, quote.SourceRevision, quote.RuleDigest, true,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            new DateTime(2081, 5, 12), out CharacterCareerAttributeAdvancePlan plan));
        CharacterCareerAttributeAdvancePlan forged = plan with
        {
            SavedCharacterKarma = plan.SavedCharacterKarma + 1,
            ExpenseAmount = plan.ExpenseAmount + 1
        };
        Assert.IsTrue(CharacterCareerAttributeAdvanceRules.IsCoherent(forged));
        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryCreateReceipt(
            forged.ExpenseId, quote, forged, forged.SavedAttributeKarmaPoints,
            forged.SavedCharacterKarma, forged.SavedBurnedEdgePoints, true, out _));
    }

    [TestMethod]
    public void Undefined_modifier_null_payloads_and_zero_cost_setting_fail_or_match_source_exactly()
    {
        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryCreateQuote(
            Input("AGI") with
            {
                Modifiers =
                [
                    Modifier('c', (CharacterCareerAttributeKarmaModifierKind)999,
                        "AGI", 0, 0, 1m)
                ]
            }, out _));
        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryCreateQuote(
            Input("AGI") with
            {
                Identity = new CharacterCareerAttributeIdentity(null!, CharacterCareerAttributeKind.Normal)
            }, out _));
        Assert.IsFalse(CharacterCareerAttributeAdvanceRules.TryCreateQuote(
            Input("AGI") with { Modifiers = [null!] }, out _));

        CharacterCareerAttributeAdvanceQuote zeroCost = Quote(Input("AGI") with
        {
            Settings = new CharacterCareerAttributeAdvanceSettings(0, false)
        });
        Assert.AreEqual(0, zeroCost.KarmaCost,
            "Chummer5A deliberately clamps with Math.Min(1, KarmaAttribute), so a zero custom setting remains zero.");
    }

    [TestMethod]
    public void Multiple_modifiers_use_product_then_flat_and_round_away_from_zero()
    {
        CharacterCareerAttributeAdvanceQuote quote = Quote(Input("LOG") with
        {
            EffectiveValue = 2,
            Modifiers =
            [
                Modifier('d', CharacterCareerAttributeKarmaModifierKind.AttributeKarmaCostMultiplier,
                    "LOG", 0, 0, 50m),
                Modifier('e', CharacterCareerAttributeKarmaModifierKind.AttributeKarmaCostMultiplier,
                    "LOG", 0, 0, 50m),
                Modifier('f', CharacterCareerAttributeKarmaModifierKind.AttributeKarmaCost,
                    "LOG", 0, 0, -4.2m),
                Modifier('0', CharacterCareerAttributeKarmaModifierKind.AttributeKarmaCost,
                    "LOG", 0, 0, 1m)
            ]
        });
        Assert.AreEqual(1, quote.KarmaCost,
            "15 * .5 * .5 + (-4.2 + 1) = .55 rounds to 1, then minimum clamps to 1.");
    }

    private static CharacterCareerAttributeAdvanceQuote Quote(
        CharacterCareerAttributeAdvanceInput input)
    {
        Assert.IsTrue(CharacterCareerAttributeAdvanceRules.TryCreateQuote(input, out var quote));
        Assert.IsTrue(CharacterCareerAttributeAdvanceRules.IsCoherent(quote));
        return quote;
    }

    private static CharacterCareerAttributeAdvanceInput Input(string abbreviation)
    {
        Assert.IsTrue(CharacterCareerAttributeAdvanceRules.TryCreateIdentity(abbreviation, out var identity));
        return new CharacterCareerAttributeAdvanceInput(
            identity,
            Created: true,
            RulesetId: "sr5",
            DisplayName: abbreviation,
            BasePoints: 2,
            KarmaPoints: 1,
            EffectiveValue: 3,
            NaturalMaximum: 6,
            MetatypeMinimum: 1,
            AvailableKarma: 40,
            MagicEnabled: true,
            MysticAdept: true,
            MysticAdeptSecondMagicAttributeEnabled: true,
            ResonanceEnabled: true,
            BurnedEdgePoints: 0,
            Settings: new CharacterCareerAttributeAdvanceSettings(5, false),
            Modifiers: [],
            RawSourceState: $"<attribute><name>{abbreviation}</name></attribute>",
            RawRuleState: "settings:v1");
    }

    private static CharacterCareerAttributeKarmaModifier Modifier(
        char digestCharacter,
        CharacterCareerAttributeKarmaModifierKind kind,
        string target,
        int minimum,
        int maximum,
        decimal value)
        => new(Hex(digestCharacter), kind, target, minimum, maximum, value);

    private static string Hex(char character) => new(character, 64);
}
