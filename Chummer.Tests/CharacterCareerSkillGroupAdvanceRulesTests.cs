using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerSkillGroupAdvanceRulesTests
{
    private static readonly CharacterCareerSkillGroupIdentity Identity = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [TestMethod]
    public void Quote_and_plan_preserve_group_identity_cost_expense_and_undo()
    {
        CharacterCareerSkillGroupAdvanceQuote quote = Quote(Input());

        Assert.AreEqual(3, quote.Rating);
        Assert.AreEqual(20, quote.KarmaCost);
        Assert.IsTrue(quote.CanAdvance);
        Assert.AreEqual(CharacterCareerSkillGroupAdvanceBlocker.None, quote.Blocker);

        Guid expenseId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Assert.IsTrue(CharacterCareerSkillGroupAdvanceRules.TryPlanAdvance(
            quote,
            quote.RuleDigest,
            confirmed: true,
            expenseId,
            new DateTime(2081, 5, 12, 14, 30, 0),
            out CharacterCareerSkillGroupAdvancePlan plan));
        Assert.AreEqual(Identity, plan.Identity);
        Assert.AreEqual(2, plan.SavedGroupKarmaPoints);
        Assert.AreEqual(20, plan.SavedCharacterKarma);
        Assert.AreEqual(-20, plan.ExpenseAmount);
        Assert.AreEqual("Skill Group Stealth 3 -> 4", plan.ExpenseReason);
        Assert.AreEqual("ImproveSkillGroup", plan.KarmaUndoType);
        Assert.AreEqual("AddCyberware", plan.NuyenUndoType);
        Assert.AreEqual(Identity.SkillGroupId.ToString("D"), plan.UndoObjectId);
        Assert.AreEqual(0m, plan.UndoQuantity);
        Assert.AreEqual(string.Empty, plan.UndoExtra);
    }

    [TestMethod]
    public void New_group_uses_flat_new_group_cost()
    {
        CharacterCareerSkillGroupAdvanceInput input = Input() with
        {
            BasePoints = 0,
            KarmaPoints = 0,
            Members =
            [
                new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 0, true, "Physical Active"),
                new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 0, true, "Physical Active")
            ]
        };

        CharacterCareerSkillGroupAdvanceQuote quote = Quote(input);
        Assert.AreEqual(5, quote.KarmaCost);
    }

    [TestMethod]
    public void Enabled_member_minimum_and_group_category_modifiers_match_chummer_rounding()
    {
        CharacterCareerSkillGroupAdvanceInput input = Input() with
        {
            Members =
            [
                new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 2, true, "Physical Active"),
                new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 4, true, "Social Active"),
                new(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 0, false, "Technical Active")
            ],
            Modifiers =
            [
                new(
                    new string('4', 64),
                    CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier,
                    "Stealth",
                    3,
                    3,
                    50m),
                new(
                    new string('5', 64),
                    CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCategoryCost,
                    "Physical Active",
                    0,
                    0,
                    0.2m),
                new(
                    new string('6', 64),
                    CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCost,
                    "Stealth",
                    4,
                    0,
                    99m)
            ]
        };

        CharacterCareerSkillGroupAdvanceQuote quote = Quote(input);
        Assert.AreEqual(2, quote.Rating);
        Assert.AreEqual(8, quote.KarmaCost, "(3 * 5 * 50%) + 0.2 rounds away from zero.");
    }

    [TestMethod]
    public void Broken_disabled_maximum_insufficient_and_confirmation_fail_closed()
    {
        CharacterCareerSkillGroupAdvanceQuote broken = Quote(Input() with { Broken = true });
        Assert.IsFalse(broken.CanAdvance);
        Assert.AreEqual(CharacterCareerSkillGroupAdvanceBlocker.Broken, broken.Blocker);

        CharacterCareerSkillGroupAdvanceQuote disabled = Quote(Input() with { Disabled = true });
        Assert.IsFalse(disabled.CanAdvance);
        Assert.AreEqual(-1, disabled.KarmaCost);
        Assert.AreEqual(CharacterCareerSkillGroupAdvanceBlocker.Disabled, disabled.Blocker);

        CharacterCareerSkillGroupAdvanceQuote maximum = Quote(Input() with { RatingMaximum = 3 });
        Assert.IsFalse(maximum.CanAdvance);
        Assert.AreEqual(CharacterCareerSkillGroupAdvanceBlocker.AtMaximum, maximum.Blocker);

        CharacterCareerSkillGroupAdvanceQuote poor = Quote(Input() with { AvailableKarma = 19 });
        Assert.IsFalse(poor.CanAdvance);
        Assert.AreEqual(CharacterCareerSkillGroupAdvanceBlocker.InsufficientKarma, poor.Blocker);

        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryPlanAdvance(
            Quote(Input()),
            new string('0', 64),
            true,
            Guid.NewGuid(),
            new DateTime(2081, 5, 12),
            out _));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryPlanAdvance(
            Quote(Input()),
            Quote(Input()).RuleDigest,
            false,
            Guid.NewGuid(),
            new DateTime(2081, 5, 12),
            out _));
    }

    [TestMethod]
    public void Stable_guid_member_source_and_rule_changes_are_digest_bound()
    {
        CharacterCareerSkillGroupAdvanceInput input = Input();
        CharacterCareerSkillGroupAdvanceQuote original = Quote(input);
        Assert.AreNotEqual(
            original.SourceRevision,
            Quote(input with { RawSourceState = input.RawSourceState + " " }).SourceRevision);
        Assert.AreNotEqual(
            original.RuleDigest,
            Quote(input with { RawRuleState = input.RawRuleState + " " }).RuleDigest);
        Assert.AreNotEqual(
            original.RuleDigest,
            Quote(input with
            {
                Members = input.Members.Select(member => member with
                {
                    SkillCategory = member.SkillCategory + " Changed"
                }).ToArray()
            }).RuleDigest);
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(
            input with { Identity = new CharacterCareerSkillGroupIdentity(Guid.Empty) },
            out _));
    }

    private static CharacterCareerSkillGroupAdvanceQuote Quote(
        CharacterCareerSkillGroupAdvanceInput input)
    {
        Assert.IsTrue(CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(input, out var quote));
        Assert.IsTrue(CharacterCareerSkillGroupAdvanceRules.IsCoherent(quote));
        return quote;
    }

    private static CharacterCareerSkillGroupAdvanceInput Input()
        => new(
            Identity,
            Created: true,
            Name: "Stealth",
            BasePoints: 2,
            KarmaPoints: 1,
            RatingMaximum: 6,
            AvailableKarma: 40,
            Disabled: false,
            Broken: false,
            new CharacterCareerSkillGroupAdvanceSettings(
                KarmaNewSkillGroup: 5,
                KarmaImproveSkillGroup: 5),
            Members:
            [
                new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 3, true, "Physical Active"),
                new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 3, true, "Physical Active")
            ],
            Modifiers: [],
            RawSourceState: "<skills><skill>Stealth member source</skill></skills>",
            RawRuleState: "settings:v1");
}
