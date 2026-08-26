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
            quote.CharacterRevision,
            quote.LogicalRevision,
            quote.SourceRevision,
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
        Assert.AreEqual(plan.ExpectedCharacterRevision, quote.CharacterRevision);
        Assert.AreEqual(plan.ExpectedLogicalRevision, quote.LogicalRevision);
        Assert.AreEqual(plan.ExpectedSourceRevision, quote.SourceRevision);
        Assert.AreEqual(plan.ExpectedRuleDigest, quote.RuleDigest);
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
            quote.CharacterRevision,
            quote.LogicalRevision,
            quote.SourceRevision,
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
            poor, poor.CharacterRevision, poor.LogicalRevision, poor.SourceRevision,
            poor.RuleDigest, true, Guid.NewGuid(), new DateTime(2081, 5, 12), out _));
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
        CharacterCareerKnowledgeSkillAdvanceQuote foreignKind = Quote(
            Input(3, 1, 20) with { IsKnowledgeSkill = false });
        Assert.AreEqual(
            CharacterCareerKnowledgeSkillAdvanceBlocker.NotKnowledgeSkill,
            foreignKind.Blocker);

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
        CharacterCareerKnowledgeSkillAdvanceQuote characterChanged = Quote(input with
        {
            RawCharacterState = input.RawCharacterState + " "
        });
        Assert.AreNotEqual(original.CharacterRevision, characterChanged.CharacterRevision);
        Assert.AreNotEqual(original.RuleDigest, rulesChanged.RuleDigest);
        Assert.AreNotEqual(original.RuleDigest, policyChanged.RuleDigest);
    }

    [TestMethod]
    public void Confirmation_revision_expense_identity_and_date_are_mandatory()
    {
        CharacterCareerKnowledgeSkillAdvanceQuote quote = Quote(Input(3, 1, 20));
        DateTime validDate = new(2081, 5, 12);
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
            quote, quote.CharacterRevision, quote.LogicalRevision, quote.SourceRevision,
            quote.RuleDigest, false, Guid.NewGuid(), validDate, out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
            quote, new string('0', 64), quote.LogicalRevision, quote.SourceRevision,
            quote.RuleDigest, true, Guid.NewGuid(), validDate, out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
            quote, quote.CharacterRevision, new string('0', 64), quote.SourceRevision,
            quote.RuleDigest, true, Guid.NewGuid(), validDate, out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
            quote, quote.CharacterRevision, quote.LogicalRevision, new string('0', 64),
            quote.RuleDigest, true, Guid.NewGuid(), validDate, out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
            quote, quote.CharacterRevision, quote.LogicalRevision, quote.SourceRevision,
            new string('0', 64), true, Guid.NewGuid(), validDate, out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
            quote, quote.CharacterRevision, quote.LogicalRevision, quote.SourceRevision,
            quote.RuleDigest, true, Guid.Empty, validDate, out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
            quote, quote.CharacterRevision, quote.LogicalRevision, quote.SourceRevision,
            quote.RuleDigest, true, Guid.NewGuid(), new DateTime(1752, 12, 31), out _));
    }

    [TestMethod]
    public void Prerequisites_keep_sr5_knowledge_language_truth_explicit_and_digest_bound()
    {
        CharacterCareerKnowledgeSkillAdvanceQuote language = Quote(Input(2, 1, 20) with
        {
            SkillType = "Language",
            SkillCategory = "Language"
        });
        CollectionAssert.AreEqual(
            Enum.GetValues<CharacterCareerKnowledgeSkillAdvancePrerequisite>().ToList(),
            language.Prerequisites.Select(static value => value.Prerequisite).ToList());
        Assert.IsTrue(language.Prerequisites.Single(value => value.Prerequisite
            == CharacterCareerKnowledgeSkillAdvancePrerequisite.NotNativeLanguage).Satisfied);
        Assert.AreEqual(TimeSpan.Zero, language.ApplicationDuration);
        Assert.AreEqual(
            CharacterCareerKnowledgeSkillTimeAuthority.ImmediateChummerPersistence,
            language.TimeAuthority);

        CharacterCareerKnowledgeSkillAdvanceQuote native = Quote(Input(0, 0, 20) with
        {
            SkillType = "Language",
            SkillCategory = "Language",
            IsNativeLanguage = true
        });
        Assert.AreEqual(CharacterCareerKnowledgeSkillAdvanceBlocker.NativeLanguage, native.Blocker);
        Assert.IsFalse(native.Prerequisites.Single(value => value.Prerequisite
            == CharacterCareerKnowledgeSkillAdvancePrerequisite.NotNativeLanguage).Satisfied);

        CharacterCareerKnowledgeSkillAdvanceQuote wrongRuleset = Quote(Input(2, 1, 20) with
        {
            RulesetId = "sr6"
        });
        Assert.AreEqual(
            CharacterCareerKnowledgeSkillAdvanceBlocker.UnsupportedRuleset,
            wrongRuleset.Blocker);
    }

    [TestMethod]
    public void Receipt_is_atomic_idempotency_authority_and_rejects_forged_observed_state()
    {
        CharacterCareerKnowledgeSkillAdvanceQuote quote = Quote(Input(3, 1, 20));
        Guid transactionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        Assert.IsTrue(CharacterCareerKnowledgeSkillAdvanceRules.TryPlanAdvance(
            quote, quote.CharacterRevision, quote.LogicalRevision, quote.SourceRevision,
            quote.RuleDigest, true, transactionId, new DateTime(2081, 5, 12),
            out CharacterCareerKnowledgeSkillAdvancePlan plan));
        Assert.IsTrue(CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(plan));
        Assert.IsTrue(CharacterCareerKnowledgeSkillAdvanceRules.TryCreateReceipt(
            transactionId, quote, plan, plan.SavedSkillKarmaPoints,
            plan.SavedCharacterKarma, expenseExistsExactlyOnce: true,
            out CharacterCareerKnowledgeSkillAdvanceReceipt receipt));
        Assert.IsTrue(CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(receipt));
        Assert.AreEqual(transactionId, receipt.TransactionId);
        Assert.AreEqual(transactionId, receipt.ExpenseId);
        Assert.AreEqual(64, receipt.ReceiptDigest.Length);

        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryCreateReceipt(
            Guid.NewGuid(), quote, plan, plan.SavedSkillKarmaPoints,
            plan.SavedCharacterKarma, true, out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryCreateReceipt(
            transactionId, quote, plan, plan.SavedSkillKarmaPoints + 1,
            plan.SavedCharacterKarma, true, out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.TryCreateReceipt(
            transactionId, quote, plan, plan.SavedSkillKarmaPoints,
            plan.SavedCharacterKarma, false, out _));
        Assert.IsFalse(CharacterCareerKnowledgeSkillAdvanceRules.IsCoherent(
            receipt with { ReceiptDigest = new string('0', 64) }));
    }

    [TestMethod]
    public void Career_active_specialization_quotes_exact_cost_group_effect_and_expense_undo()
    {
        CharacterCareerSkillSpecializationInput input = SpecializationInput(
            CharacterCareerSkillKind.Active,
            availableKarma: 20) with
        {
            ExistingSpecializationCount = 2,
            Modifiers =
            [
                SpecializationModifier(
                    'c',
                    CharacterCareerSkillSpecializationModifierKind.SkillCategorySpecializationKarmaCostMultiplier,
                    "Combat Active",
                    minimum: 4,
                    value: 50m),
                SpecializationModifier(
                    'd',
                    CharacterCareerSkillSpecializationModifierKind.SkillCategorySpecializationKarmaCost,
                    "Combat Active",
                    minimum: 4,
                    value: 0.2m)
            ]
        };

        Assert.IsTrue(CharacterCareerSkillSpecializationRules.TryCreateQuote(input, out var quote));
        Assert.IsTrue(CharacterCareerSkillSpecializationRules.IsCoherent(quote));
        Assert.AreEqual(4, quote.KarmaCost,
            "Chummer rounds 7 * 0.5 + 0.2 away from zero after composing both modifier families.");
        Assert.IsTrue(quote.CanAdd);
        Assert.IsTrue(quote.WillBreakSkillGroup);

        Guid specializationId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        Guid expenseId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        Assert.IsTrue(CharacterCareerSkillSpecializationRules.TryPlanAdd(
            quote,
            quote.CharacterRevision,
            quote.SourceRevision,
            quote.RuleDigest,
            quote.LogicalRevision,
            confirmed: true,
            specializationId,
            expenseId,
            new DateTime(2081, 6, 1, 9, 30, 0, DateTimeKind.Local),
            out CharacterCareerSkillSpecializationPlan plan));
        Assert.AreEqual(specializationId, plan.SpecializationId);
        Assert.AreEqual("Semi-Automatics", plan.SpecializationName);
        Assert.IsFalse(plan.SavedFree);
        Assert.IsFalse(plan.SavedExpertise);
        Assert.AreEqual(16, plan.SavedCharacterKarma);
        Assert.AreEqual(-4, plan.ExpenseAmount);
        Assert.AreEqual("Learned Specialization Pistols (Semi-Automatics)", plan.ExpenseReason);
        Assert.AreEqual(DateTimeKind.Unspecified, plan.ExpenseDateLocal.Kind);
        Assert.AreEqual(expenseId, plan.ExpenseId);
        Assert.AreEqual("AddSpecialization", plan.KarmaUndoType);
        Assert.AreEqual("AddCyberware", plan.NuyenUndoType);
        Assert.AreEqual(specializationId.ToString("D"), plan.UndoObjectId);
        Assert.AreEqual(0m, plan.UndoQuantity);
        Assert.AreEqual(string.Empty, plan.UndoExtra);
        Assert.IsTrue(plan.WillBreakSkillGroup);
    }

    [TestMethod]
    public void Career_custom_knowledge_specialization_preserves_nullable_source_and_has_no_count_cap()
    {
        CharacterCareerSkillSpecializationInput input = SpecializationInput(
            CharacterCareerSkillKind.Knowledge,
            availableKarma: 10) with
        {
            Identity = new CharacterCareerSkillIdentity(
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                null,
                CharacterCareerSkillKind.Knowledge),
            SkillName = "Lone Star Procedures",
            SkillCategory = "Professional",
            DictionaryKey = "Lone Star Procedures",
            SkillGroup = string.Empty,
            EnabledSkillGroupMemberCount = 0,
            ExistingSpecializationCount = int.MaxValue,
            AvailableOptions = [],
            Selection = new CharacterCareerSkillSpecializationSelection(
                "Evidence handling",
                CharacterCareerSkillSpecializationOptionKind.Custom,
                null),
            RawSourceState = "<skill><guid>66666666-6666-6666-6666-666666666666</guid><name>Lone Star Procedures</name></skill>"
        };

        Assert.IsTrue(CharacterCareerSkillSpecializationRules.TryCreateQuote(input, out var quote));
        Assert.IsTrue(quote.CanAdd);
        Assert.AreEqual(5, quote.KarmaCost);
        Assert.AreEqual(input.Identity, quote.Identity);
        Assert.IsFalse(quote.WillBreakSkillGroup);
        Assert.IsFalse(CharacterCareerSkillSpecializationRules.TryCreateQuote(
            input with { ExistingSpecializationCount = -1 },
            out _));
    }

    [TestMethod]
    public void Career_specialization_eligibility_matches_chummer_can_have_specs_restrictions()
    {
        CharacterCareerSkillSpecializationInput active = SpecializationInput(
            CharacterCareerSkillKind.Active,
            availableKarma: 20);
        AssertSpecializationBlocker(
            active with { Enabled = false },
            CharacterCareerSkillSpecializationBlocker.SkillDisabled);
        AssertSpecializationBlocker(
            active with { IsExoticSkill = true },
            CharacterCareerSkillSpecializationBlocker.ExoticSkill);
        AssertSpecializationBlocker(
            active with { KarmaUnlocked = false },
            CharacterCareerSkillSpecializationBlocker.KarmaLocked);
        AssertSpecializationBlocker(
            active with { TotalBaseRating = 0 },
            CharacterCareerSkillSpecializationBlocker.RatingRequired);
        AssertSpecializationBlocker(
            active with { SkillSpecializationsBlocked = true },
            CharacterCareerSkillSpecializationBlocker.SkillSpecializationsBlocked);
        AssertSpecializationBlocker(
            active with { SkillCategorySpecializationsBlocked = true },
            CharacterCareerSkillSpecializationBlocker.SkillCategorySpecializationsBlocked);
        AssertSpecializationBlocker(
            active with { AvailableKarma = 6 },
            CharacterCareerSkillSpecializationBlocker.InsufficientKarma);

        CharacterCareerSkillSpecializationInput knowledge = SpecializationInput(
            CharacterCareerSkillKind.Knowledge,
            availableKarma: 20) with
        {
            Identity = new CharacterCareerSkillIdentity(
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                null,
                CharacterCareerSkillKind.Knowledge),
            SkillName = "English",
            SkillCategory = "Language",
            DictionaryKey = "English",
            SkillGroup = string.Empty,
            EnabledSkillGroupMemberCount = 0,
            AvailableOptions = [],
            Selection = new CharacterCareerSkillSpecializationSelection(
                "Legalese",
                CharacterCareerSkillSpecializationOptionKind.Custom,
                null)
        };
        AssertSpecializationBlocker(
            knowledge with { IsNativeLanguage = true, AllowUpgrade = false },
            CharacterCareerSkillSpecializationBlocker.NativeLanguage);
        AssertSpecializationBlocker(
            knowledge with { AllowUpgrade = false },
            CharacterCareerSkillSpecializationBlocker.UpgradeDisallowed);
    }

    [TestMethod]
    public void Career_specialization_selection_and_typed_identity_fail_closed()
    {
        CharacterCareerSkillSpecializationInput active = SpecializationInput(
            CharacterCareerSkillKind.Active,
            availableKarma: 20);
        Assert.IsTrue(CharacterCareerSkillSpecializationRules.TryCreateQuote(active, out var coherent));
        Assert.IsFalse(CharacterCareerSkillSpecializationRules.IsCoherent(
            coherent with
            {
                Selection = coherent.Selection with
                {
                    Kind = CharacterCareerSkillSpecializationOptionKind.Custom
                }
            }));
        Assert.IsFalse(CharacterCareerSkillSpecializationRules.TryCreateQuote(
            active with
            {
                Identity = active.Identity with { SourceSkillId = null }
            },
            out _));
        Assert.IsFalse(CharacterCareerSkillSpecializationRules.TryCreateQuote(
            active with
            {
                Selection = active.Selection with { OptionIdentity = new string('0', 64) }
            },
            out _));
        Assert.IsFalse(CharacterCareerSkillSpecializationRules.TryCreateQuote(
            active with
            {
                Selection = active.Selection with
                {
                    Kind = CharacterCareerSkillSpecializationOptionKind.Custom
                }
            },
            out _));
        Assert.IsFalse(CharacterCareerSkillSpecializationRules.TryCreateQuote(
            active with
            {
                Identity = active.Identity with { Kind = (CharacterCareerSkillKind)99 }
            },
            out _));
        Assert.IsFalse(CharacterCareerSkillSpecializationRules.TryCreateQuote(
            active with
            {
                Modifiers =
                [
                    SpecializationModifier(
                        'f',
                        (CharacterCareerSkillSpecializationModifierKind)99,
                        "Combat Active",
                        minimum: 0,
                        value: 1m)
                ]
            },
            out _));
        Assert.IsFalse(CharacterCareerSkillSpecializationRules.TryCreateQuote(
            active with
            {
                Settings = active.Settings with
                {
                    KarmaActiveSpecialization =
                        CharacterCareerSkillSpecializationRules.MaximumSettingCost + 1
                }
            },
            out _));
        Assert.IsFalse(CharacterCareerSkillSpecializationRules.TryCreateQuote(
            active with
            {
                AvailableOptions =
                [
                    active.AvailableOptions[0] with
                    {
                        Kind = (CharacterCareerSkillSpecializationOptionKind)99
                    }
                ],
                Selection = active.Selection with
                {
                    Kind = (CharacterCareerSkillSpecializationOptionKind)99
                }
            },
            out _));
        Assert.IsFalse(CharacterCareerSkillSpecializationRules.TryCreateQuote(
            active with
            {
                AvailableOptions =
                [
                    active.AvailableOptions[0] with
                    {
                        Kind = CharacterCareerSkillSpecializationOptionKind.CombatWeapon,
                        OptionIdentity = new string('e', 64)
                    }
                ],
                SkillCategory = "Technical Active",
                Selection = new CharacterCareerSkillSpecializationSelection(
                    "Semi-Automatics",
                    CharacterCareerSkillSpecializationOptionKind.CombatWeapon,
                    new string('e', 64))
            },
            out _));
    }

    [TestMethod]
    public void Career_specialization_plan_rejects_every_stale_cas_dimension()
    {
        CharacterCareerSkillSpecializationInput input = SpecializationInput(
            CharacterCareerSkillKind.Active,
            availableKarma: 20);
        Assert.IsTrue(CharacterCareerSkillSpecializationRules.TryCreateQuote(input, out var quote));
        Guid specializationId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        Guid expenseId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        DateTime date = new(2081, 6, 1);

        AssertPlanRejected(new string('0', 64), quote.SourceRevision, quote.RuleDigest, quote.LogicalRevision);
        AssertPlanRejected(quote.CharacterRevision, new string('0', 64), quote.RuleDigest, quote.LogicalRevision);
        AssertPlanRejected(quote.CharacterRevision, quote.SourceRevision, new string('0', 64), quote.LogicalRevision);
        AssertPlanRejected(quote.CharacterRevision, quote.SourceRevision, quote.RuleDigest, new string('0', 64));
        Assert.IsFalse(CharacterCareerSkillSpecializationRules.TryPlanAdd(
            quote,
            quote.CharacterRevision,
            quote.SourceRevision,
            quote.RuleDigest,
            quote.LogicalRevision,
            confirmed: false,
            specializationId,
            expenseId,
            date,
            out _));
        Assert.IsFalse(CharacterCareerSkillSpecializationRules.TryPlanAdd(
            quote,
            quote.CharacterRevision,
            quote.SourceRevision,
            quote.RuleDigest,
            quote.LogicalRevision,
            confirmed: true,
            Guid.Empty,
            expenseId,
            date,
            out _));

        void AssertPlanRejected(string character, string source, string rules, string logical)
            => Assert.IsFalse(CharacterCareerSkillSpecializationRules.TryPlanAdd(
                quote,
                character,
                source,
                rules,
                logical,
                confirmed: true,
                specializationId,
                expenseId,
                date,
                out _));
    }

    [TestMethod]
    public void Career_specialization_character_source_and_rule_changes_invalidate_quote()
    {
        CharacterCareerSkillSpecializationInput input = SpecializationInput(
            CharacterCareerSkillKind.Active,
            availableKarma: 20);
        Assert.IsTrue(CharacterCareerSkillSpecializationRules.TryCreateQuote(input, out var original));
        Assert.IsTrue(CharacterCareerSkillSpecializationRules.TryCreateQuote(
            input with { RawCharacterState = input.RawCharacterState + " " }, out var characterChanged));
        Assert.IsTrue(CharacterCareerSkillSpecializationRules.TryCreateQuote(
            input with { RawSourceState = input.RawSourceState + " " }, out var sourceChanged));
        Assert.IsTrue(CharacterCareerSkillSpecializationRules.TryCreateQuote(
            input with { RawRuleState = input.RawRuleState + " " }, out var rulesChanged));
        Assert.AreNotEqual(original.CharacterRevision, characterChanged.CharacterRevision);
        Assert.AreNotEqual(original.SourceRevision, sourceChanged.SourceRevision);
        Assert.AreNotEqual(original.RuleDigest, rulesChanged.RuleDigest);
        Assert.AreNotEqual(original.LogicalRevision, characterChanged.LogicalRevision);
        Assert.AreNotEqual(original.LogicalRevision, sourceChanged.LogicalRevision);
        Assert.AreNotEqual(original.LogicalRevision, rulesChanged.LogicalRevision);
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
            RulesetId: CharacterCareerKnowledgeSkillAdvanceRules.RulesetId,
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
            RawCharacterState: "<character><created>True</created><karma>20</karma></character>",
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

    private static CharacterCareerSkillSpecializationInput SpecializationInput(
        CharacterCareerSkillKind kind,
        int availableKarma)
    {
        Guid skillId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        Guid sourceSkillId = Guid.Parse("adf31a50-b228-4e09-a09c-46ab9f5e59a1");
        CharacterCareerSkillSpecializationOption option = new(
            new string('a', 64),
            "Semi-Automatics",
            CharacterCareerSkillSpecializationOptionKind.SourceCatalog,
            $"skills.xml#skill:{sourceSkillId:D}/spec:2");
        return new CharacterCareerSkillSpecializationInput(
            new CharacterCareerSkillIdentity(skillId, sourceSkillId, kind),
            Created: true,
            Enabled: true,
            IsExoticSkill: false,
            KarmaUnlocked: true,
            AllowUpgrade: true,
            IsNativeLanguage: false,
            SkillName: "Pistols",
            SkillCategory: "Combat Active",
            DictionaryKey: "Pistols",
            SkillGroup: "Firearms",
            TotalBaseRating: 4,
            ExistingSpecializationCount: 0,
            AvailableKarma: availableKarma,
            EnabledSkillGroupMemberCount: 3,
            SkillSpecializationsBlocked: false,
            SkillCategorySpecializationsBlocked: false,
            Settings: new CharacterCareerSkillSpecializationSettings(
                KarmaActiveSpecialization: 7,
                KarmaKnowledgeSpecialization: 5,
                SpecializationsBreakSkillGroups: true),
            Modifiers: [],
            AvailableOptions: [option],
            Selection: new CharacterCareerSkillSpecializationSelection(
                option.Name,
                option.Kind,
                option.OptionIdentity),
            RawCharacterState: "<skill><guid>aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb</guid><rating>4</rating></skill>",
            RawSourceState: "<skill><id>adf31a50-b228-4e09-a09c-46ab9f5e59a1</id><name>Pistols</name></skill>",
            RawRuleState: "settings:career-specialization:v1");
    }

    private static CharacterCareerSkillSpecializationModifier SpecializationModifier(
        char identityCharacter,
        CharacterCareerSkillSpecializationModifierKind kind,
        string target,
        int minimum,
        decimal value)
        => new(new string(identityCharacter, 64), kind, target, minimum, value);

    private static void AssertSpecializationBlocker(
        CharacterCareerSkillSpecializationInput input,
        CharacterCareerSkillSpecializationBlocker blocker)
    {
        Assert.IsTrue(CharacterCareerSkillSpecializationRules.TryCreateQuote(input, out var quote));
        Assert.IsFalse(quote.CanAdd);
        Assert.AreEqual(blocker, quote.Blocker);
    }
}
