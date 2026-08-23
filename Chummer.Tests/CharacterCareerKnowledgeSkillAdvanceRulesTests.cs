using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerKnowledgeSkillAdvanceRulesTests
{
    private static readonly CharacterCareerKnowledgeSkillIdentity CustomIdentity = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        null);

    [TestMethod]
    public void Improve_custom_skill_preserves_nullable_source_identity_and_exact_expense_undo()
    {
        CharacterCareerKnowledgeSkillAdvanceQuote quote = Quote(Input(rating: 3, karma: 1, availableKarma: 20));
        Assert.AreEqual(CustomIdentity, quote.Identity);
        Assert.AreEqual(4, quote.KarmaCost);
        Assert.IsTrue(quote.CanAdvance);
        Assert.AreEqual(CharacterCareerKnowledgeSkillAdvanceBlocker.None, quote.Blocker);

        Guid expenseId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        DateTime requestedDate = new(2081, 5, 12, 14, 30, 0, DateTimeKind.Local);
        Assert.IsTrue(CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
            quote,
            quote.RuleDigest,
            confirmed: true,
            expenseId,
            requestedDate,
            out CharacterCareerKnowledgeSkillAdvancePlan plan));
        Assert.AreEqual(CustomIdentity, plan.Identity);
        Assert.AreEqual(2, plan.SavedSkillKarmaPoints);
        Assert.AreEqual(16, plan.SavedCharacterKarma);
        Assert.AreEqual(-4, plan.ExpenseAmount);
        Assert.AreEqual("Knowledge Skill Lone Star Procedures 3 -> 4", plan.ExpenseReason);
        Assert.AreEqual(DateTimeKind.Unspecified, plan.ExpenseDateLocal.Kind);
        Assert.AreEqual(expenseId, plan.ExpenseId);
        Assert.AreEqual("ImproveSkill", plan.KarmaUndoType);
        Assert.AreEqual("AddCyberware", plan.NuyenUndoType);
        Assert.AreEqual(CustomIdentity.SkillId.ToString("D"), plan.UndoObjectId);
        Assert.AreEqual(0m, plan.UndoQuantity);
        Assert.AreEqual(string.Empty, plan.UndoExtra);
        Assert.AreEqual(quote.RuleDigest, plan.RuleDigest);
    }

    [TestMethod]
    public void New_source_backed_skill_uses_new_cost_and_add_skill_undo()
    {
        CharacterCareerKnowledgeSkillIdentity sourceBacked = new(
            CustomIdentity.SkillId,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        CharacterCareerKnowledgeSkillAdvanceQuote quote = Quote(Input(0, 0, 20) with
        {
            Identity = sourceBacked,
            BasePoints = 0,
            KarmaPoints = 0
        });
        Assert.AreEqual(2, quote.KarmaCost);

        Assert.IsTrue(CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
            quote,
            quote.RuleDigest,
            true,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            new DateTime(2081, 5, 12),
            out CharacterCareerKnowledgeSkillAdvancePlan plan));
        Assert.AreEqual("AddSkill", plan.KarmaUndoType);
        Assert.AreEqual(1, plan.SavedSkillKarmaPoints);
    }

    [TestMethod]
    public void Knowledge_and_category_modifiers_use_target_rating_and_chummer_rounding()
    {
        CharacterCareerKnowledgeSkillAdvanceInput input = Input(2, 1, 20) with
        {
            Modifiers =
            [
                Modifier('4', CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCostMultiplier,
                    "Lone Star Procedures", minimum: 3, maximum: 3, value: 50m),
                Modifier('5', CharacterCareerKnowledgeSkillKarmaModifierKind.SkillCategoryCost,
                    "Professional", minimum: 0, maximum: 0, value: 0.2m),
                Modifier('6', CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCostMinimum,
                    "Professional", minimum: 3, maximum: 3, value: 3.2m),
                Modifier('7', CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCost,
                    "Lone Star Procedures", minimum: 4, maximum: 0, value: 99m)
            ]
        };

        CharacterCareerKnowledgeSkillAdvanceQuote quote = Quote(input);
        Assert.AreEqual(4, quote.KarmaCost,
            "(3 * 0.5 + 0.2) rounds away from zero to 2, then the rounded minimum override wins at 4.");
    }

    [TestMethod]
    public void Default_minimum_matches_legacy_zero_and_positive_setting_behavior()
    {
        CharacterCareerKnowledgeSkillAdvanceQuote free = Quote(Input(0, 0, 0) with
        {
            Settings = new CharacterCareerKnowledgeSkillAdvanceSettings(0, 0)
        });
        Assert.AreEqual(0, free.KarmaCost);
        Assert.IsTrue(free.CanAdvance);

        CharacterCareerKnowledgeSkillAdvanceQuote discounted = Quote(Input(1, 1, 20) with
        {
            Settings = new CharacterCareerKnowledgeSkillAdvanceSettings(2, 1),
            Modifiers =
            [
                Modifier('8', CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCost,
                    "Lone Star Procedures", minimum: 0, maximum: 0, value: -99m)
            ]
        });
        Assert.AreEqual(1, discounted.KarmaCost);
    }

    [TestMethod]
    public void Upgrade_visibility_native_language_maximum_and_karma_block_fail_closed()
    {
        CharacterCareerKnowledgeSkillAdvanceQuote disallowed = Quote(Input(3, 1, 20) with
        {
            AllowUpgrade = false
        });
        Assert.IsFalse(disallowed.CanAdvance);
        Assert.AreEqual(CharacterCareerKnowledgeSkillAdvanceBlocker.UpgradeDisallowed, disallowed.Blocker);

        CharacterCareerKnowledgeSkillAdvanceQuote native = Quote(Input(0, 0, 20) with
        {
            AllowUpgrade = false,
            IsNativeLanguage = true,
            SkillType = "Language",
            SkillCategory = "Language"
        });
        Assert.IsFalse(native.CanAdvance);
        Assert.AreEqual(CharacterCareerKnowledgeSkillAdvanceBlocker.NativeLanguage, native.Blocker);

        CharacterCareerKnowledgeSkillAdvanceQuote maximum = Quote(Input(6, 5, 20) with
        {
            RatingMaximum = 6
        });
        Assert.AreEqual(-1, maximum.KarmaCost);
        Assert.AreEqual(CharacterCareerKnowledgeSkillAdvanceBlocker.AtMaximum, maximum.Blocker);

        CharacterCareerKnowledgeSkillAdvanceQuote poor = Quote(Input(3, 1, 3));
        Assert.AreEqual(CharacterCareerKnowledgeSkillAdvanceBlocker.InsufficientKarma, poor.Blocker);
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
            poor, poor.RuleDigest, true, Guid.NewGuid(), new DateTime(2081, 5, 12), out _));
    }

    [TestMethod]
    public void Custom_empty_type_and_category_remain_valid_chummer5_data()
    {
        CharacterCareerKnowledgeSkillAdvanceQuote quote = Quote(Input(1, 0, 20) with
        {
            Name = "zoology",
            DictionaryKey = "zoology",
            SkillType = string.Empty,
            SkillCategory = string.Empty
        });
        Assert.IsTrue(quote.CanAdvance);
        Assert.AreEqual(string.Empty, quote.SkillType);
        Assert.AreEqual(string.Empty, quote.SkillCategory);
    }

    [TestMethod]
    public void Typed_identity_kind_and_modifier_authority_reject_ambiguous_inputs()
    {
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryCreateQuote(
            Input(3, 1, 20) with
            {
                Identity = new CharacterCareerKnowledgeSkillIdentity(Guid.Empty, null)
            },
            out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryCreateQuote(
            Input(3, 1, 20) with
            {
                Modifiers =
                [
                    Modifier('b', CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCostMinimum,
                        "Professional", 0, 0, -1m)
                ]
            },
            out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryCreateQuote(
            Input(3, 1, 20) with
            {
                Identity = new CharacterCareerKnowledgeSkillIdentity(CustomIdentity.SkillId, Guid.Empty)
            },
            out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryCreateQuote(
            Input(3, 1, 20) with { IsKnowledgeSkill = false },
            out _));

        CharacterCareerKnowledgeSkillKarmaModifier duplicate = Modifier(
            '9',
            CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCost,
            "Lone Star Procedures",
            0,
            0,
            1m);
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryCreateQuote(
            Input(3, 1, 20) with { Modifiers = [duplicate, duplicate] },
            out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryCreateQuote(
            Input(3, 1, 20) with
            {
                Modifiers =
                [
                    Modifier('a', CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCost,
                        "Different Skill", 0, 0, 1m)
                ]
            },
            out _));
    }

    [TestMethod]
    public void Saved_source_rules_and_upgrade_policy_are_digest_bound()
    {
        CharacterCareerKnowledgeSkillAdvanceInput input = Input(3, 1, 20);
        CharacterCareerKnowledgeSkillAdvanceQuote original = Quote(input);
        CharacterCareerKnowledgeSkillAdvanceQuote sourceChanged = Quote(input with
        {
            RawSourceState = input.RawSourceState + " "
        });
        CharacterCareerKnowledgeSkillAdvanceQuote rulesChanged = Quote(input with
        {
            RawRuleState = input.RawRuleState + " "
        });
        CharacterCareerKnowledgeSkillAdvanceQuote policyChanged = Quote(input with
        {
            AllowUpgrade = false
        });
        Assert.AreNotEqual(original.SourceRevision, sourceChanged.SourceRevision);
        Assert.AreNotEqual(original.RuleDigest, rulesChanged.RuleDigest);
        Assert.AreNotEqual(original.RuleDigest, policyChanged.RuleDigest);
    }

    [TestMethod]
    public void Confirmation_revision_expense_identity_and_date_are_mandatory()
    {
        CharacterCareerKnowledgeSkillAdvanceQuote quote = Quote(Input(3, 1, 20));
        DateTime validDate = new(2081, 5, 12);
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
            quote, quote.RuleDigest, false, Guid.NewGuid(), validDate, out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
            quote, new string('0', 64), true, Guid.NewGuid(), validDate, out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
            quote, quote.RuleDigest, true, Guid.Empty, validDate, out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
            quote, quote.RuleDigest, true, Guid.NewGuid(), new DateTime(1752, 12, 31), out _));
    }

    private static CharacterCareerKnowledgeSkillAdvanceQuote Quote(
        CharacterCareerKnowledgeSkillAdvanceInput input)
    {
        Assert.IsTrue(CharacterCareerKnowledgeSkillAdvanceRules.TryCreateQuote(input, out var quote));
        Assert.IsTrue(CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(quote));
        return quote;
    }

    private static CharacterCareerKnowledgeSkillAdvanceInput Input(
        int rating,
        int karma,
        int availableKarma)
        => new(
            CustomIdentity,
            Created: true,
            IsKnowledgeSkill: true,
            AllowUpgrade: true,
            IsNativeLanguage: false,
            Name: "Lone Star Procedures",
            SkillType: "Professional",
            SkillCategory: "Professional",
            DictionaryKey: "Lone Star Procedures",
            BasePoints: Math.Max(0, rating - karma),
            KarmaPoints: karma,
            TotalBaseRating: rating,
            RatingMaximum: 6,
            AvailableKarma: availableKarma,
            Settings: new CharacterCareerKnowledgeSkillAdvanceSettings(
                KarmaNewKnowledgeSkill: 2,
                KarmaImproveKnowledgeSkill: 1),
            Modifiers: [],
            RawSourceState: "<skill><guid>11111111-1111-1111-1111-111111111111</guid><isknowledge>True</isknowledge></skill>",
            RawRuleState: "settings:v1");

    private static CharacterCareerKnowledgeSkillKarmaModifier Modifier(
        char identityCharacter,
        CharacterCareerKnowledgeSkillKarmaModifierKind kind,
        string target,
        int minimum,
        int maximum,
        decimal value)
        => new(new string(identityCharacter, 64), kind, target, minimum, maximum, value);
}
