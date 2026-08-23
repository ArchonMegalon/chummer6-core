using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerActiveSkillAdvanceRulesTests
{
    private static readonly CharacterCareerActiveSkillIdentity Identity = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

    [TestMethod]
    public void Improve_quote_and_plan_preserve_identity_and_exact_expense_undo_semantics()
    {
        CharacterCareerActiveSkillAdvanceInput input = Input(rating: 3, karma: 1, availableKarma: 40);
        Assert.IsTrue(CharacterCareerActiveSkillAdvanceRules.TryCreateQuote(
            input,
            out CharacterCareerActiveSkillAdvanceQuote quote));
        Assert.AreEqual(8, quote.KarmaCost);
        Assert.IsTrue(quote.CanAdvance);
        Assert.AreEqual(CharacterCareerActiveSkillAdvanceBlocker.None, quote.Blocker);

        Guid expenseId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Assert.IsTrue(CharacterCareerActiveSkillAdvanceRules.TryPlanAdvance(
            quote,
            quote.RuleDigest,
            confirmed: true,
            expenseId,
            new DateTime(2081, 5, 12, 14, 30, 0),
            out CharacterCareerActiveSkillAdvancePlan plan));
        Assert.AreEqual(Identity, plan.Identity);
        Assert.AreEqual(2, plan.SavedSkillKarmaPoints);
        Assert.AreEqual(32, plan.SavedCharacterKarma);
        Assert.AreEqual(-8, plan.ExpenseAmount);
        Assert.AreEqual("Active Skill Sneaking 3 -> 4", plan.ExpenseReason);
        Assert.AreEqual("ImproveSkill", plan.KarmaUndoType);
        Assert.AreEqual("AddCyberware", plan.NuyenUndoType);
        Assert.AreEqual(Identity.SkillId.ToString("D"), plan.UndoObjectId);
        Assert.AreEqual(0m, plan.UndoQuantity);
        Assert.AreEqual(string.Empty, plan.UndoExtra);
    }

    [TestMethod]
    public void New_skill_uses_new_cost_and_add_skill_undo_type()
    {
        CharacterCareerActiveSkillAdvanceInput input = Input(rating: 0, karma: 0, availableKarma: 20) with
        {
            BasePoints = 0
        };
        Assert.IsTrue(CharacterCareerActiveSkillAdvanceRules.TryCreateQuote(
            input,
            out CharacterCareerActiveSkillAdvanceQuote quote));
        Assert.AreEqual(2, quote.KarmaCost);
        Assert.IsTrue(CharacterCareerActiveSkillAdvanceRules.TryPlanAdvance(
            quote,
            quote.RuleDigest,
            true,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            new DateTime(2081, 5, 12),
            out CharacterCareerActiveSkillAdvancePlan plan));
        Assert.AreEqual("AddSkill", plan.KarmaUndoType);
        Assert.AreEqual(1, plan.SavedSkillKarmaPoints);
    }

    [TestMethod]
    public void Modifiers_filter_by_target_and_rating_and_round_away_from_zero()
    {
        CharacterCareerActiveSkillAdvanceInput input = Input(rating: 2, karma: 1, availableKarma: 40) with
        {
            Modifiers =
            [
                new(
                    new string('4', 64),
                    CharacterCareerActiveSkillKarmaModifierKind.ActiveSkillCostMultiplier,
                    "Sneaking",
                    3,
                    3,
                    50m),
                new(
                    new string('5', 64),
                    CharacterCareerActiveSkillKarmaModifierKind.SkillCategoryCost,
                    string.Empty,
                    0,
                    0,
                    0.2m),
                new(
                    new string('6', 64),
                    CharacterCareerActiveSkillKarmaModifierKind.ActiveSkillCost,
                    "Sneaking",
                    4,
                    0,
                    99m)
            ]
        };

        Assert.IsTrue(CharacterCareerActiveSkillAdvanceRules.TryCreateQuote(
            input,
            out CharacterCareerActiveSkillAdvanceQuote quote));
        Assert.AreEqual(4, quote.KarmaCost, "(6 * 0.5 + 0.2) must ceil away from zero.");
    }

    [TestMethod]
    public void Skill_group_compensation_matches_enabled_peer_threshold_and_count()
    {
        CharacterCareerActiveSkillAdvanceInput input = Input(rating: 1, karma: 0, availableKarma: 40) with
        {
            OtherGroupMembers =
            [
                new(Guid.Parse("77777777-7777-7777-7777-777777777777"), 2, true),
                new(Guid.Parse("88888888-8888-8888-8888-888888888888"), 3, true),
                new(Guid.Parse("99999999-9999-9999-9999-999999999999"), 0, false)
            ]
        };

        Assert.IsTrue(CharacterCareerActiveSkillAdvanceRules.TryCreateQuote(
            input,
            out CharacterCareerActiveSkillAdvanceQuote quote));
        Assert.AreEqual(2, quote.KarmaCost);
    }

    [TestMethod]
    public void Maximum_insufficient_karma_and_confirmation_fail_closed()
    {
        Assert.IsTrue(CharacterCareerActiveSkillAdvanceRules.TryCreateQuote(
            Input(rating: 6, karma: 5, availableKarma: 40) with { RatingMaximum = 6 },
            out CharacterCareerActiveSkillAdvanceQuote maximum));
        Assert.IsFalse(maximum.CanAdvance);
        Assert.AreEqual(-1, maximum.KarmaCost);
        Assert.AreEqual(CharacterCareerActiveSkillAdvanceBlocker.AtMaximum, maximum.Blocker);

        Assert.IsTrue(CharacterCareerActiveSkillAdvanceRules.TryCreateQuote(
            Input(rating: 3, karma: 1, availableKarma: 7),
            out CharacterCareerActiveSkillAdvanceQuote poor));
        Assert.IsFalse(poor.CanAdvance);
        Assert.AreEqual(CharacterCareerActiveSkillAdvanceBlocker.InsufficientKarma, poor.Blocker);
        Assert.IsFalse(CharacterCareerActiveSkillAdvanceRules.TryPlanAdvance(
            poor,
            poor.RuleDigest,
            true,
            Guid.NewGuid(),
            new DateTime(2081, 5, 12),
            out _));

        CharacterCareerActiveSkillAdvanceQuote valid = Quote(Input(3, 1, 40));
        Assert.IsFalse(CharacterCareerActiveSkillAdvanceRules.TryPlanAdvance(
            valid, valid.RuleDigest, false, Guid.NewGuid(), new DateTime(2081, 5, 12), out _));
        Assert.IsFalse(CharacterCareerActiveSkillAdvanceRules.TryPlanAdvance(
            valid, new string('0', 64), true, Guid.NewGuid(), new DateTime(2081, 5, 12), out _));
    }

    [TestMethod]
    public void Stable_guid_source_and_rule_changes_are_digest_bound()
    {
        CharacterCareerActiveSkillAdvanceInput input = Input(3, 1, 40);
        CharacterCareerActiveSkillAdvanceQuote original = Quote(input);
        CharacterCareerActiveSkillAdvanceQuote sourceChanged = Quote(input with
        {
            RawSourceState = input.RawSourceState + " "
        });
        CharacterCareerActiveSkillAdvanceQuote rulesChanged = Quote(input with
        {
            RawRuleState = input.RawRuleState + " "
        });
        Assert.AreNotEqual(original.SourceRevision, sourceChanged.SourceRevision);
        Assert.AreNotEqual(original.RuleDigest, rulesChanged.RuleDigest);

        Assert.IsFalse(CharacterCareerActiveSkillAdvanceRules.TryCreateQuote(
            input with
            {
                Identity = new CharacterCareerActiveSkillIdentity(Guid.Empty, Identity.SourceSkillId)
            },
            out _));
        Assert.IsFalse(CharacterCareerActiveSkillAdvanceRules.TryCreateQuote(
            input with
            {
                Identity = new CharacterCareerActiveSkillIdentity(Identity.SkillId, Guid.Empty)
            },
            out _));
    }

    private static CharacterCareerActiveSkillAdvanceQuote Quote(
        CharacterCareerActiveSkillAdvanceInput input)
    {
        Assert.IsTrue(CharacterCareerActiveSkillAdvanceRules.TryCreateQuote(input, out var quote));
        Assert.IsTrue(CharacterCareerActiveSkillAdvanceRules.IsCoherent(quote));
        return quote;
    }

    private static CharacterCareerActiveSkillAdvanceInput Input(
        int rating,
        int karma,
        int availableKarma)
        => new(
            Identity,
            Created: true,
            Name: "Sneaking",
            SkillCategory: "Physical",
            DictionaryKey: "Sneaking",
            BasePoints: Math.Max(0, rating - karma),
            KarmaPoints: karma,
            TotalBaseRating: rating,
            RatingMaximum: 6,
            AvailableKarma: availableKarma,
            Settings: new CharacterCareerActiveSkillAdvanceSettings(
                KarmaNewActiveSkill: 2,
                KarmaImproveActiveSkill: 2,
                KarmaNewSkillGroup: 5,
                KarmaImproveSkillGroup: 5,
                CompensateSkillGroupKarmaDifference: true),
            OtherGroupMembers: [],
            Modifiers: [],
            RawSourceState: "<skill><guid>11111111-1111-1111-1111-111111111111</guid></skill>",
            RawRuleState: "settings:v1");
}
