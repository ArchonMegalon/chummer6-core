using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerSkillGroupAdvanceRulesTests
{
    private static readonly CharacterCareerSkillGroupIdentity Identity = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [TestMethod]
    public void Quote_exposes_exact_cost_targets_prerequisites_time_and_cas()
    {
        CharacterCareerSkillGroupAdvanceQuote quote = Quote(Input());

        Assert.AreEqual(3, quote.GroupRating);
        Assert.AreEqual(3, quote.CostRating);
        Assert.AreEqual(4, quote.TargetGroupRating);
        Assert.AreEqual(4, quote.TargetCostRating);
        Assert.AreEqual(2, quote.EnabledMemberCount);
        Assert.AreEqual(20, quote.KarmaCost);
        Assert.AreEqual(TimeSpan.Zero, quote.ApplicationDuration);
        Assert.AreEqual(
            CharacterCareerSkillGroupTimeAuthority.ImmediateChummerPersistence,
            quote.TimeAuthority);
        Assert.IsTrue(quote.CanAdvance);
        Assert.AreEqual(CharacterCareerSkillGroupAdvanceBlocker.None, quote.Blocker);
        CollectionAssert.AreEqual(
            Enum.GetValues<CharacterCareerSkillGroupPrerequisite>(),
            quote.Prerequisites.Select(static value => value.Prerequisite).ToArray());
        Assert.IsTrue(quote.Prerequisites.All(static value => value.Satisfied));
        Assert.AreEqual($"skill-group.internal-id:{Identity.InternalId:D}",
            quote.Prerequisites.Single(value => value.Prerequisite
                == CharacterCareerSkillGroupPrerequisite.ExactTarget).Authority);
        Assert.AreEqual(64, quote.LogicalRevision.Length);
        Assert.AreEqual(64, quote.SourceRevision.Length);
        Assert.AreEqual(64, quote.RuleDigest.Length);
    }

    [TestMethod]
    public void Group_rating_and_enabled_member_cost_rating_remain_distinct()
    {
        CharacterCareerSkillGroupAdvanceQuote quote = Quote(Input() with
        {
            BasePoints = 2,
            KarmaPoints = 3,
            Members =
            [
                Member("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 2),
                Member("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", 2)
            ]
        });

        Assert.AreEqual(5, quote.GroupRating);
        Assert.AreEqual(2, quote.CostRating);
        Assert.AreEqual(15, quote.KarmaCost);
        CharacterCareerSkillGroupAdvancePlan plan = Plan(quote);
        Assert.AreEqual(6, plan.TargetGroupRating);
        Assert.AreEqual(3, plan.TargetCostRating);
        Assert.AreEqual("Skill Group Stealth 5 -> 6", plan.ExpenseReason);
    }

    [TestMethod]
    public void Empty_or_zero_enabled_group_uses_flat_new_group_cost()
    {
        CharacterCareerSkillGroupAdvanceQuote empty = Quote(Input() with
        {
            BasePoints = 0,
            KarmaPoints = 0,
            Members = []
        });
        CharacterCareerSkillGroupAdvanceQuote disabledMembers = Quote(Input() with
        {
            BasePoints = 0,
            KarmaPoints = 0,
            Members =
            [
                Member("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 8, false)
            ]
        });

        Assert.AreEqual(0, empty.CostRating);
        Assert.AreEqual(0, empty.TargetCostRating);
        Assert.AreEqual(5, empty.KarmaCost);
        Assert.AreEqual(0, disabledMembers.CostRating);
        Assert.AreEqual(5, disabledMembers.KarmaCost);
    }

    [TestMethod]
    public void Exact_group_and_all_member_category_modifiers_match_chummer_rounding()
    {
        CharacterCareerSkillGroupAdvanceInput input = Input() with
        {
            Members =
            [
                Member("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 2, true,
                    "Physical Active"),
                Member("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", 4, true,
                    "Social Active"),
                Member("cccccccc-cccc-cccc-cccc-cccccccccccc", 0, false,
                    "Technical Active")
            ],
            Modifiers =
            [
                Modifier('4',
                    CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier,
                    "Stealth", 3, 3, 50m),
                Modifier('5',
                    CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCategoryCost,
                    "Technical Active", 0, 0, 0.2m),
                Modifier('6',
                    CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCost,
                    "Stealth", 4, 0, 99m)
            ]
        };

        CharacterCareerSkillGroupAdvanceQuote quote = Quote(input);
        Assert.AreEqual(2, quote.CostRating);
        Assert.AreEqual(8, quote.KarmaCost,
            "(3 * 5 * 50%) + 0.2 rounds away from zero; disabled member categories still participate.");
    }

    [TestMethod]
    public void General_group_modifier_applies_but_blank_category_target_is_rejected()
    {
        CharacterCareerSkillGroupAdvanceQuote quote = Quote(Input() with
        {
            Modifiers =
            [
                Modifier('4',
                    CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCost,
                    string.Empty, 0, 0, -1.2m)
            ]
        });
        Assert.AreEqual(18, quote.KarmaCost);

        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(
            Input() with
            {
                Modifiers =
                [
                    Modifier('4',
                        CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCategoryCost,
                        string.Empty, 0, 0, -1m)
                ]
            },
            out _));
    }

    [TestMethod]
    public void Zero_cost_setting_stays_zero_after_floor_protection()
    {
        CharacterCareerSkillGroupAdvanceQuote quote = Quote(Input() with
        {
            Settings = new CharacterCareerSkillGroupAdvanceSettings(0, 0),
            Modifiers =
            [
                Modifier('4',
                    CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCost,
                    "Stealth", 0, 0, -5m)
            ]
        });

        Assert.AreEqual(0, quote.KarmaCost);
        Assert.IsTrue(quote.CanAdvance);
    }

    [TestMethod]
    public void Career_ruleset_and_skill_group_blockers_are_typed_and_ordered()
    {
        AssertBlocker(Input() with { Created = false },
            CharacterCareerSkillGroupAdvanceBlocker.NotCareerCharacter);
        AssertBlocker(Input() with { RulesetId = "sr6" },
            CharacterCareerSkillGroupAdvanceBlocker.UnsupportedRuleset);
        AssertBlocker(Input() with { TargetOwnedByCharacter = false },
            CharacterCareerSkillGroupAdvanceBlocker.ForeignTarget);
        AssertBlocker(Input() with { MemberProjectionIsExact = false },
            CharacterCareerSkillGroupAdvanceBlocker.InvalidMemberProjection);
        AssertBlocker(Input() with { Broken = true, Disabled = true },
            CharacterCareerSkillGroupAdvanceBlocker.Broken);
        AssertBlocker(Input() with { Disabled = true },
            CharacterCareerSkillGroupAdvanceBlocker.Disabled,
            expectedCost: -1);
        AssertBlocker(Input() with { RatingMaximum = 3 },
            CharacterCareerSkillGroupAdvanceBlocker.AtMaximum,
            expectedCost: -1);
        AssertBlocker(Input() with { AvailableKarma = 19 },
            CharacterCareerSkillGroupAdvanceBlocker.InsufficientKarma);
    }

    [TestMethod]
    public void Foreign_duplicate_null_and_undefined_projection_values_fail_closed()
    {
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(
            Input() with
            {
                Identity = new CharacterCareerSkillGroupIdentity(Guid.Empty)
            }, out _));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(
            Input() with
            {
                Members =
                [
                    Member("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 3),
                    Member("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 3)
                ]
            }, out _));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(
            Input() with
            {
                Members = new CharacterCareerSkillGroupMember[] { null! }
            }, out _));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(
            Input() with
            {
                Modifiers =
                [
                    Modifier('4',
                        (CharacterCareerSkillGroupKarmaModifierKind)999,
                        "Stealth", 0, 0, 1m)
                ]
            }, out _));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(
            Input() with
            {
                BasePoints = CharacterCareerSkillGroupAdvanceRules.MaximumRating,
                KarmaPoints = 0,
                Members =
                [
                    Member("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 1),
                    Member("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", 1)
                ],
                RatingMaximum = 6
            }, out _), "An executable +1 target must remain representable.");
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(
            Input() with
            {
                Modifiers =
                [
                    Modifier('4', CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier, "Stealth", 0, 0, 9_999_999m),
                    Modifier('5', CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier, "Stealth", 0, 0, 9_999_999m),
                    Modifier('6', CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier, "Stealth", 0, 0, 9_999_999m),
                    Modifier('7', CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier, "Stealth", 0, 0, 9_999_999m),
                    Modifier('8', CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier, "Stealth", 0, 0, 9_999_999m),
                    Modifier('9', CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier, "Stealth", 0, 0, 9_999_999m)
                ]
            }, out _), "Decimal multiplier overflow must fail closed.");
    }

    [TestMethod]
    public void Plan_requires_confirmation_full_triple_cas_and_unused_transaction_id()
    {
        CharacterCareerSkillGroupAdvanceQuote quote = Quote(Input());
        Guid id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        DateTime date = new(2081, 5, 12, 14, 30, 0);

        AssertPlanRejected(quote, new string('0', 64), quote.SourceRevision,
            quote.RuleDigest, true, false, id, date);
        AssertPlanRejected(quote, quote.LogicalRevision, new string('0', 64),
            quote.RuleDigest, true, false, id, date);
        AssertPlanRejected(quote, quote.LogicalRevision, quote.SourceRevision,
            new string('0', 64), true, false, id, date);
        AssertPlanRejected(quote, quote.LogicalRevision, quote.SourceRevision,
            quote.RuleDigest, false, false, id, date);
        AssertPlanRejected(quote, quote.LogicalRevision, quote.SourceRevision,
            quote.RuleDigest, true, true, id, date);
    }

    [TestMethod]
    public void Plan_contains_exact_chummer_mutation_expense_and_undo()
    {
        CharacterCareerSkillGroupAdvanceQuote quote = Quote(Input());
        CharacterCareerSkillGroupAdvancePlan plan = Plan(quote);

        Assert.AreEqual(Identity, plan.Identity);
        Assert.AreEqual(plan.ExpenseId, plan.TransactionId);
        Assert.AreEqual(2, plan.SavedGroupKarmaPoints);
        Assert.AreEqual(20, plan.SavedCharacterKarma);
        Assert.AreEqual(-20, plan.ExpenseAmount);
        Assert.AreEqual("Skill Group Stealth 3 -> 4", plan.ExpenseReason);
        Assert.AreEqual("ImproveSkillGroup", plan.KarmaUndoType);
        Assert.AreEqual("AddCyberware", plan.NuyenUndoType);
        Assert.AreEqual(Identity.InternalId.ToString("D"), plan.UndoObjectId);
        Assert.AreEqual(0m, plan.UndoQuantity);
        Assert.AreEqual(string.Empty, plan.UndoExtra);
        Assert.AreEqual(plan.ExpectedLogicalRevision, quote.LogicalRevision);
        Assert.AreEqual(plan.ExpectedSourceRevision, quote.SourceRevision);
        Assert.AreEqual(plan.ExpectedRuleDigest, quote.RuleDigest);
        Assert.IsTrue(CharacterCareerSkillGroupAdvanceRules.IsCoherent(plan));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.IsCoherent(
            plan with { TransactionId = Guid.NewGuid() }));
    }

    [TestMethod]
    public void Receipt_requires_exact_atomic_post_state_and_expense_observation()
    {
        CharacterCareerSkillGroupAdvanceQuote before = Quote(Input());
        CharacterCareerSkillGroupAdvancePlan plan = Plan(before);
        CharacterCareerSkillGroupAdvanceQuote after = PostQuote(Input(), plan);
        CharacterCareerSkillGroupExpenseObservation expense = Expense(plan);

        Assert.IsTrue(CharacterCareerSkillGroupAdvanceRules.TryCreateReceipt(
            plan.ExpenseId, before, plan, after, expense,
            out CharacterCareerSkillGroupAdvanceReceipt receipt));
        Assert.IsTrue(CharacterCareerSkillGroupAdvanceRules.IsCoherent(receipt));
        Assert.AreEqual(before.KarmaPoints, receipt.GroupKarmaBefore);
        Assert.AreEqual(after.KarmaPoints, receipt.GroupKarmaAfter);
        Assert.AreEqual(before.AvailableKarma, receipt.CharacterKarmaBefore);
        Assert.AreEqual(after.AvailableKarma, receipt.CharacterKarmaAfter);
        Assert.AreEqual(after.LogicalRevision, receipt.LogicalRevisionAfter);
        Assert.AreEqual(64, receipt.ExpenseAuthorityDigest.Length);
        Assert.AreEqual(64, receipt.ReceiptDigest.Length);
    }

    [TestMethod]
    public void Receipt_rejects_forged_plan_post_state_duplicate_or_wrong_expense()
    {
        CharacterCareerSkillGroupAdvanceInput input = Input();
        CharacterCareerSkillGroupAdvanceQuote before = Quote(input);
        CharacterCareerSkillGroupAdvancePlan plan = Plan(before);
        CharacterCareerSkillGroupAdvanceQuote after = PostQuote(input, plan);
        CharacterCareerSkillGroupExpenseObservation expense = Expense(plan);

        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryCreateReceipt(
            plan.ExpenseId, before,
            plan with { SavedGroupKarmaPoints = plan.SavedGroupKarmaPoints + 1 },
            after, expense, out _));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryCreateReceipt(
            plan.ExpenseId, before, plan,
            Quote(PostInput(input, plan) with { AvailableKarma = 21 }),
            expense, out _));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryCreateReceipt(
            plan.ExpenseId, before, plan, after,
            expense with { MatchingEntryCount = 0 }, out _));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryCreateReceipt(
            plan.ExpenseId, before, plan, after,
            expense with { MatchingEntryCount = 2 }, out _));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryCreateReceipt(
            plan.ExpenseId, before, plan, after,
            expense with { KarmaUndoType = "ImproveSkill" }, out _));
    }

    [TestMethod]
    public void Persisted_receipt_recovers_idempotently_after_restart()
    {
        (CharacterCareerSkillGroupAdvanceReceipt receipt,
            CharacterCareerSkillGroupAdvanceQuote after,
            CharacterCareerSkillGroupExpenseObservation expense) = Completed();

        Assert.IsTrue(CharacterCareerSkillGroupAdvanceRules.TryRecoverReceipt(
            receipt, receipt.TransactionId, after, expense, receipt.ReceiptDigest,
            out CharacterCareerSkillGroupAdvanceReceipt recovered));
        Assert.AreSame(receipt, recovered);
    }

    [TestMethod]
    public void Recovery_rejects_stale_foreign_tampered_or_non_unique_state()
    {
        (CharacterCareerSkillGroupAdvanceReceipt receipt,
            CharacterCareerSkillGroupAdvanceQuote after,
            CharacterCareerSkillGroupExpenseObservation expense) = Completed();

        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryRecoverReceipt(
            receipt, Guid.NewGuid(), after, expense, receipt.ReceiptDigest, out _));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryRecoverReceipt(
            receipt, receipt.TransactionId, after, expense, new string('0', 64), out _));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryRecoverReceipt(
            receipt, receipt.TransactionId,
            Quote(PostInput(Input(), Plan(Quote(Input()))) with
            {
                RawSourceState = "different-source"
            }), expense, receipt.ReceiptDigest, out _));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryRecoverReceipt(
            receipt, receipt.TransactionId,
            Quote(PostInput(Input(), Plan(Quote(Input()))) with
            {
                RawRuleState = "different-rule"
            }), expense, receipt.ReceiptDigest, out _));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryRecoverReceipt(
            receipt, receipt.TransactionId, after,
            expense with { MatchingEntryCount = 0 }, receipt.ReceiptDigest, out _));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryRecoverReceipt(
            receipt, receipt.TransactionId, after,
            expense with { MatchingEntryCount = 2 }, receipt.ReceiptDigest, out _));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryRecoverReceipt(
            receipt with { ExpenseAmount = receipt.ExpenseAmount + 1 },
            receipt.TransactionId, after, expense, receipt.ReceiptDigest, out _));
    }

    [TestMethod]
    public void Compensating_correction_restores_before_values_and_is_digest_bound()
    {
        (CharacterCareerSkillGroupAdvanceReceipt receipt,
            CharacterCareerSkillGroupAdvanceQuote after,
            CharacterCareerSkillGroupExpenseObservation expense) = Completed();
        Guid correctionId =
            Guid.Parse("33333333-3333-3333-3333-333333333333");

        Assert.IsTrue(CharacterCareerSkillGroupAdvanceRules.TryPlanCorrection(
            receipt, after, expense, correctionId, "Operator rollback",
            correctionIdAlreadyExists: false,
            originalTransactionAlreadyCorrected: false,
            receipt.ReceiptDigest,
            out CharacterCareerSkillGroupCorrectionPlan correction));
        Assert.IsTrue(CharacterCareerSkillGroupAdvanceRules.IsCoherent(correction));
        Assert.AreEqual(receipt.GroupKarmaBefore, correction.SavedGroupKarmaPoints);
        Assert.AreEqual(receipt.CharacterKarmaBefore, correction.SavedCharacterKarma);
        Assert.AreEqual(receipt.GroupRatingBefore, correction.RestoredGroupRating);
        Assert.AreEqual(receipt.CostRatingBefore, correction.RestoredCostRating);
        Assert.AreEqual(receipt.ExpenseId, correction.ExpenseIdToRemove);
        Assert.AreEqual(receipt.ReceiptDigest, correction.OriginalReceiptDigest);
        Assert.AreEqual(64, correction.CorrectionDigest.Length);
    }

    [TestMethod]
    public void Correction_rejects_replay_collision_stale_receipt_and_wrong_expense()
    {
        (CharacterCareerSkillGroupAdvanceReceipt receipt,
            CharacterCareerSkillGroupAdvanceQuote after,
            CharacterCareerSkillGroupExpenseObservation expense) = Completed();

        AssertCorrectionRejected(receipt, after, expense, Guid.NewGuid(),
            false, true, receipt.ReceiptDigest);
        AssertCorrectionRejected(receipt, after, expense, Guid.NewGuid(),
            true, false, receipt.ReceiptDigest);
        AssertCorrectionRejected(receipt, after, expense, receipt.TransactionId,
            false, false, receipt.ReceiptDigest);
        AssertCorrectionRejected(receipt, after, expense, Guid.NewGuid(),
            false, false, new string('0', 64));
        AssertCorrectionRejected(receipt, after,
            expense with { Reason = expense.Reason + " forged" }, Guid.NewGuid(),
            false, false, receipt.ReceiptDigest);
    }

    [TestMethod]
    public void Digests_are_order_stable_and_bind_source_rules_members_and_state()
    {
        CharacterCareerSkillGroupAdvanceInput input = Input() with
        {
            Modifiers =
            [
                Modifier('4', CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCost,
                    "Stealth", 0, 0, 1m),
                Modifier('5', CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier,
                    "Stealth", 0, 0, 100m)
            ]
        };
        CharacterCareerSkillGroupAdvanceQuote original = Quote(input);
        CharacterCareerSkillGroupAdvanceQuote reordered = Quote(input with
        {
            Members = input.Members.Reverse().ToArray(),
            Modifiers = input.Modifiers.Reverse().ToArray()
        });
        Assert.AreEqual(original.RuleDigest, reordered.RuleDigest);
        Assert.AreEqual(original.LogicalRevision, reordered.LogicalRevision);
        Assert.AreNotEqual(original.SourceRevision,
            Quote(input with { RawSourceState = input.RawSourceState + " " })
                .SourceRevision);
        Assert.AreNotEqual(original.RuleDigest,
            Quote(input with { RawRuleState = input.RawRuleState + " " }).RuleDigest);
        Assert.AreNotEqual(original.RuleDigest,
            Quote(input with
            {
                Members = input.Members.Select(member => member with
                {
                    TotalBaseRating = member.TotalBaseRating + 1
                }).ToArray()
            }).RuleDigest);
        Assert.AreNotEqual(original.LogicalRevision,
            Quote(input with { AvailableKarma = input.AvailableKarma + 1 })
                .LogicalRevision);
    }

    [TestMethod]
    public void Receipt_and_correction_tampering_breaks_coherence()
    {
        (CharacterCareerSkillGroupAdvanceReceipt receipt,
            CharacterCareerSkillGroupAdvanceQuote after,
            CharacterCareerSkillGroupExpenseObservation expense) = Completed();
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.IsCoherent(
            receipt with { ExpenseReason = receipt.ExpenseReason + " forged" }));

        Assert.IsTrue(CharacterCareerSkillGroupAdvanceRules.TryPlanCorrection(
            receipt, after, expense, Guid.NewGuid(), "Rollback", false, false,
            receipt.ReceiptDigest,
            out CharacterCareerSkillGroupCorrectionPlan correction));
        Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.IsCoherent(
            correction with { SavedCharacterKarma = correction.SavedCharacterKarma + 1 }));
    }

    private static void AssertBlocker(
        CharacterCareerSkillGroupAdvanceInput input,
        CharacterCareerSkillGroupAdvanceBlocker expected,
        int? expectedCost = null)
    {
        CharacterCareerSkillGroupAdvanceQuote quote = Quote(input);
        Assert.IsFalse(quote.CanAdvance);
        Assert.AreEqual(expected, quote.Blocker);
        if (expectedCost.HasValue)
        {
            Assert.AreEqual(expectedCost.Value, quote.KarmaCost);
        }
    }

    private static void AssertPlanRejected(
        CharacterCareerSkillGroupAdvanceQuote quote,
        string logical,
        string source,
        string rule,
        bool confirmed,
        bool exists,
        Guid id,
        DateTime date)
        => Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryPlanAdvance(
            quote, logical, source, rule, confirmed, exists, id, date, out _));

    private static void AssertCorrectionRejected(
        CharacterCareerSkillGroupAdvanceReceipt receipt,
        CharacterCareerSkillGroupAdvanceQuote after,
        CharacterCareerSkillGroupExpenseObservation expense,
        Guid correctionId,
        bool correctionExists,
        bool alreadyCorrected,
        string expectedReceiptDigest)
        => Assert.IsFalse(CharacterCareerSkillGroupAdvanceRules.TryPlanCorrection(
            receipt, after, expense, correctionId, "Rollback", correctionExists,
            alreadyCorrected, expectedReceiptDigest, out _));

    private static CharacterCareerSkillGroupAdvanceQuote Quote(
        CharacterCareerSkillGroupAdvanceInput input)
    {
        Assert.IsTrue(CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(
            input, out CharacterCareerSkillGroupAdvanceQuote quote));
        Assert.IsTrue(CharacterCareerSkillGroupAdvanceRules.IsCoherent(quote));
        return quote;
    }

    private static CharacterCareerSkillGroupAdvancePlan Plan(
        CharacterCareerSkillGroupAdvanceQuote quote)
    {
        Assert.IsTrue(CharacterCareerSkillGroupAdvanceRules.TryPlanAdvance(
            quote, quote.LogicalRevision, quote.SourceRevision, quote.RuleDigest,
            confirmed: true, transactionIdAlreadyExists: false,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            new DateTime(2081, 5, 12, 14, 30, 0),
            out CharacterCareerSkillGroupAdvancePlan plan));
        return plan;
    }

    private static CharacterCareerSkillGroupAdvanceQuote PostQuote(
        CharacterCareerSkillGroupAdvanceInput input,
        CharacterCareerSkillGroupAdvancePlan plan)
        => Quote(PostInput(input, plan));

    private static CharacterCareerSkillGroupAdvanceInput PostInput(
        CharacterCareerSkillGroupAdvanceInput input,
        CharacterCareerSkillGroupAdvancePlan plan)
        => input with
        {
            KarmaPoints = plan.SavedGroupKarmaPoints,
            AvailableKarma = plan.SavedCharacterKarma,
            Members = input.Members.Select(member => member.Enabled
                ? member with { TotalBaseRating = member.TotalBaseRating + 1 }
                : member).ToArray()
        };

    private static CharacterCareerSkillGroupExpenseObservation Expense(
        CharacterCareerSkillGroupAdvancePlan plan)
        => new(
            MatchingEntryCount: 1,
            plan.ExpenseId,
            plan.ExpenseDateLocal,
            plan.ExpenseAmount,
            plan.ExpenseReason,
            ExpenseType: "Karma",
            Refund: false,
            ForceCareerVisible: true,
            plan.KarmaUndoType,
            plan.NuyenUndoType,
            plan.UndoObjectId,
            plan.UndoQuantity,
            plan.UndoExtra);

    private static (CharacterCareerSkillGroupAdvanceReceipt Receipt,
        CharacterCareerSkillGroupAdvanceQuote After,
        CharacterCareerSkillGroupExpenseObservation Expense) Completed()
    {
        CharacterCareerSkillGroupAdvanceInput input = Input();
        CharacterCareerSkillGroupAdvanceQuote before = Quote(input);
        CharacterCareerSkillGroupAdvancePlan plan = Plan(before);
        CharacterCareerSkillGroupAdvanceQuote after = PostQuote(input, plan);
        CharacterCareerSkillGroupExpenseObservation expense = Expense(plan);
        Assert.IsTrue(CharacterCareerSkillGroupAdvanceRules.TryCreateReceipt(
            plan.ExpenseId, before, plan, after, expense,
            out CharacterCareerSkillGroupAdvanceReceipt receipt));
        return (receipt, after, expense);
    }

    private static CharacterCareerSkillGroupMember Member(
        string id,
        int rating,
        bool enabled = true,
        string category = "Physical Active")
        => new(Guid.Parse(id), rating, enabled, category);

    private static CharacterCareerSkillGroupKarmaModifier Modifier(
        char identity,
        CharacterCareerSkillGroupKarmaModifierKind kind,
        string target,
        int minimum,
        int maximum,
        decimal value)
        => new(new string(identity, 64), kind, target, minimum, maximum, value);

    private static CharacterCareerSkillGroupAdvanceInput Input()
        => new(
            Identity,
            Created: true,
            RulesetId: CharacterCareerSkillGroupAdvanceRules.RulesetId,
            TargetOwnedByCharacter: true,
            MemberProjectionIsExact: true,
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
                Member("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 3),
                Member("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", 3)
            ],
            Modifiers: [],
            RawSourceState: "<skills><skill>Stealth member source</skill></skills>",
            RawRuleState: "settings:v1");
}
