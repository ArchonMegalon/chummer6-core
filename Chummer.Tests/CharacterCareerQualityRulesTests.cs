using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerQualityRulesTests
{
    private static readonly Guid SourceId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid NewInternalId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime ExpenseDate =
        new(2081, 5, 12, 14, 30, 0, DateTimeKind.Unspecified);

    [TestMethod]
    public void Positive_acquisition_uses_discount_career_double_and_exact_undo()
    {
        CharacterCareerQualityInput input = Input() with
        {
            Definition = Definition() with
            {
                BaseKarma = 5,
                CostDiscountDefined = true,
                CostDiscountProjectionIsExact = true,
                CostDiscountRequirementsMet = true,
                CostDiscountValue = -1
            }
        };
        CharacterCareerQualityQuote quote = Quote(input);

        Assert.AreEqual(8, quote.RuleKarmaCost);
        Assert.AreEqual(-8, quote.CharacterKarmaDelta);
        Assert.IsTrue(quote.CreatesExpense);
        Assert.IsFalse(quote.ExpenseRefund);
        Assert.AreEqual(0, quote.LevelBefore);
        Assert.AreEqual(1, quote.LevelAfter);
        Assert.AreEqual("AddQuality", quote.KarmaUndoType);
        Assert.AreEqual(NewInternalId.ToString("D"), quote.UndoObjectId);
        Assert.AreEqual(TimeSpan.Zero, quote.ApplicationDuration);
        Assert.IsTrue(quote.CanApply);
        Assert.AreEqual(18, quote.Prerequisites.Count);

        CharacterCareerQualityPlan plan = Plan(quote);
        Assert.AreEqual(42L, plan.TargetWorkspaceRevision);
        Assert.AreEqual(20L, plan.TargetSavedRevision);
        Assert.AreEqual(92, plan.SavedCharacterKarma);
        Assert.AreEqual(plan.TransactionId, plan.ExpenseId);
        Assert.IsFalse(plan.ForceCareerVisible,
            "Chummer5 ExpenseLogEntry.Create leaves forcecareervisible false.");
        Assert.AreEqual("AddCyberware", plan.NuyenUndoType,
            "ExpenseUndo.CreateKarma preserves the enum default.");
        Assert.AreEqual(1, plan.InstancesAfter.Count);
        Assert.AreEqual(CharacterCareerQualityOrigin.Selected,
            plan.InstancesAfter[0].Origin);
    }

    [TestMethod]
    public void Negative_acquisition_records_zero_expense_and_never_awards_karma()
    {
        CharacterCareerQualityQuote quote = Quote(Input() with
        {
            Definition = Definition(CharacterCareerQualityType.Negative) with
            {
                BaseKarma = -7
            },
            AvailableKarma = -20
        });

        Assert.AreEqual(-14, quote.RuleKarmaCost);
        Assert.AreEqual(0, quote.CharacterKarmaDelta);
        Assert.IsTrue(quote.CreatesExpense);
        Assert.IsFalse(quote.ExpenseRefund);
        Assert.IsTrue(quote.CanApply);
        CharacterCareerQualityPlan plan = Plan(quote);
        Assert.AreEqual(-20, plan.SavedCharacterKarma);
        Assert.AreEqual(0, plan.ExpenseAmount);
        Assert.AreEqual("Negative Quality Test Quality", plan.ExpenseReason);
    }

    [TestMethod]
    public void Automatic_and_gm_free_cases_preserve_distinct_expense_semantics()
    {
        CharacterCareerQualityQuote metagenic = Quote(Input() with
        {
            MetagenicLimit = 10,
            Definition = Definition() with
            {
                Metagenic = true,
                ContributeToBp = false
            }
        });
        Assert.AreEqual(0, metagenic.RuleKarmaCost);
        Assert.AreEqual(0, metagenic.CharacterKarmaDelta);
        Assert.IsFalse(metagenic.CreatesExpense);

        CharacterCareerQualityQuote gmFree = Quote(Input() with
        {
            GmFreeCostApproved = true
        });
        Assert.AreEqual(0, gmFree.RuleKarmaCost);
        Assert.AreEqual(0, gmFree.CharacterKarmaDelta);
        Assert.IsTrue(gmFree.CreatesExpense,
            "The career UI still creates the AddQuality expense when ContributeToBP remains true.");
    }

    [TestMethod]
    public void Mentor_spirit_way_free_cost_is_typed_and_never_inferred_from_name()
    {
        CharacterCareerQualityInput namedOnly = Input() with
        {
            HasMentorSpiritWay = true,
            Definition = Definition() with { Name = "Mentor Spirit" }
        };
        Assert.AreEqual(10, Quote(namedOnly).RuleKarmaCost,
            "A display label must never grant SR5 free-cost authority.");

        CharacterCareerQualityQuote typed = Quote(namedOnly with
        {
            Definition = namedOnly.Definition with
            {
                MentorSpiritWayFreeCostEligible = true
            }
        });
        Assert.AreEqual(0, typed.RuleKarmaCost);
        Assert.AreEqual(CharacterCareerQualityRules.AcquireUndoType,
            typed.KarmaUndoType);
#pragma warning disable MSTEST0032 // Intentional wire-literal contract assertions.
        Assert.AreEqual("AddQuality", CharacterCareerQualityRules.AcquireUndoType);
        Assert.AreEqual("RemoveQuality", CharacterCareerQualityRules.RemoveUndoType);
        Assert.AreEqual("Karma", CharacterCareerQualityRules.KarmaExpenseType);
        Assert.AreEqual("AddCyberware",
            CharacterCareerQualityRules.DefaultNuyenUndoType);
#pragma warning restore MSTEST0032
    }

    [TestMethod]
    public void Staged_purchase_may_enter_karma_debt_but_nonstaged_purchase_cannot()
    {
        CharacterCareerQualityInput insufficient = Input() with
        {
            AvailableKarma = 2
        };
        AssertBlocker(insufficient, CharacterCareerQualityBlocker.InsufficientKarma);

        CharacterCareerQualityQuote staged = Quote(insufficient with
        {
            Definition = insufficient.Definition with { StagedPurchase = true }
        });
        Assert.IsTrue(staged.CanApply);
        Assert.AreEqual(-8, Plan(staged).SavedCharacterKarma);
    }

    [TestMethod]
    public void Positive_removal_without_refund_has_no_ledger_entry()
    {
        CharacterCareerQualityInput input = RemovalInput(
            CharacterCareerQualityOperation.RemoveLevel,
            CharacterCareerQualityType.Positive, levels: 2);
        CharacterCareerQualityQuote quote = Quote(input);

        Assert.AreEqual(0, quote.RuleKarmaCost);
        Assert.AreEqual(0, quote.CharacterKarmaDelta);
        Assert.IsFalse(quote.CreatesExpense);
        CharacterCareerQualityPlan plan = Plan(quote);
        Assert.AreEqual(Guid.Empty, plan.ExpenseId);
        Assert.AreEqual(1, plan.InstancesAfter.Count);

        CharacterCareerQualityStateObservation post = PostState(plan, quote);
        CharacterCareerQualityExpenseObservation none = NoExpense();
        Assert.IsTrue(CharacterCareerQualityRules.TryCreateReceipt(
            plan.TransactionId, quote, plan, post, none, out var receipt));
        Assert.IsFalse(receipt.CreatesExpense);
        Assert.IsTrue(CharacterCareerQualityRules.TryRecoverReceipt(
            receipt, receipt.TransactionId, post, none, receipt.ReceiptDigest,
            out _));
    }

    [TestMethod]
    public void Positive_refund_uses_purchase_double_and_does_not_multiply_full_levels()
    {
        CharacterCareerQualityInput input = RemovalInput(
            CharacterCareerQualityOperation.RemoveAllLevels,
            CharacterCareerQualityType.Positive, levels: 3) with
        {
            Definition = Definition() with
            {
                RefundKarmaOnRemove = true,
                LevelLimit = 5
            }
        };
        CharacterCareerQualityQuote quote = Quote(input);

        Assert.AreEqual(-10, quote.RuleKarmaCost);
        Assert.AreEqual(10, quote.CharacterKarmaDelta,
            "fe4355 refunds one BP cost even when full deletion removes all levels.");
        Assert.IsTrue(quote.CreatesExpense);
        Assert.IsTrue(quote.ExpenseRefund);
        Assert.AreEqual(3, quote.AffectedInternalIds.Count);
        Assert.AreEqual(0, quote.LevelAfter);
    }

    [TestMethod]
    public void Negative_buyoff_uses_refund_double_and_full_level_multiplier()
    {
        CharacterCareerQualityInput input = RemovalInput(
            CharacterCareerQualityOperation.RemoveAllLevels,
            CharacterCareerQualityType.Negative, levels: 3) with
        {
            Definition = Definition(CharacterCareerQualityType.Negative) with
            {
                BaseKarma = -4,
                LevelLimit = 5
            }
        };
        CharacterCareerQualityQuote quote = Quote(input);
        Assert.AreEqual(24, quote.RuleKarmaCost);
        Assert.AreEqual(-24, quote.CharacterKarmaDelta);

        CharacterCareerQualityQuote notDoubled = Quote(input with
        {
            Settings = input.Settings with { DontDoubleQualityRefunds = true }
        });
        Assert.AreEqual(12, notDoubled.RuleKarmaCost);
    }

    [TestMethod]
    public void Duplicate_no_levels_numeric_limit_and_unlimited_are_exact()
    {
        CharacterCareerQualityInput oneExisting = Input() with
        {
            MatchingInstances = Instances(1)
        };
        AssertBlocker(oneExisting,
            CharacterCareerQualityBlocker.DuplicateOrLevelLimit);

        CharacterCareerQualityInput rated = oneExisting with
        {
            Definition = oneExisting.Definition with
            {
                NoLevels = false,
                LevelLimit = 2
            }
        };
        Assert.IsTrue(Quote(rated).CanApply);
        AssertBlocker(rated with { MatchingInstances = Instances(2) },
            CharacterCareerQualityBlocker.DuplicateOrLevelLimit);

        CharacterCareerQualityInput unlimited = rated with
        {
            Definition = rated.Definition with
            {
                LimitIsUnlimited = true,
                LevelLimit = 0
            },
            MatchingInstances = Instances(20)
        };
        Assert.IsTrue(Quote(unlimited).CanApply);
    }

    [TestMethod]
    public void Requirements_forbidden_cost_and_effect_projections_fail_closed()
    {
        CharacterCareerQualityInput input = Input();
        AssertBlocker(input with
        {
            Eligibility = input.Eligibility with { IsExact = false }
        }, CharacterCareerQualityBlocker.InvalidEligibilityProjection);
        AssertBlocker(input with
        {
            Eligibility = input.Eligibility with
            {
                RequiredAllQualitiesMet = false,
                MissingRequirementIds = ["quality:mentor"]
            }
        }, CharacterCareerQualityBlocker.MissingRequirement);
        AssertBlocker(input with
        {
            Eligibility = input.Eligibility with
            {
                ForbiddenQualitiesClear = false,
                ConflictingQualityInternalIds =
                    [Guid.Parse("99999999-9999-9999-9999-999999999999")]
            }
        }, CharacterCareerQualityBlocker.ForbiddenConflict);
        AssertBlocker(input with
        {
            Definition = input.Definition with
            {
                CostDiscountDefined = true,
                CostDiscountProjectionIsExact = false
            }
        }, CharacterCareerQualityBlocker.InvalidCostDiscountProjection);
        AssertBlocker(input with
        {
            Effects = input.Effects with { IsExact = false }
        }, CharacterCareerQualityBlocker.InvalidEffectProjection);
        AssertBlocker(input with
        {
            Effects = input.Effects with
            {
                UnsupportedFamilies = [CharacterCareerQualityEffectFamily.ChoiceSelection]
            }
        }, CharacterCareerQualityBlocker.UnsupportedEffectFamily);
    }

    [TestMethod]
    public void Career_source_gm_and_origin_restrictions_are_typed_and_ordered()
    {
        AssertBlocker(Input() with { Created = false },
            CharacterCareerQualityBlocker.NotCareerCharacter);
        AssertBlocker(Input() with { RulesetId = "sr6" },
            CharacterCareerQualityBlocker.UnsupportedRuleset);
        AssertBlocker(Input() with { DefinitionProjectionIsExact = false },
            CharacterCareerQualityBlocker.InvalidDefinitionProjection);
        AssertBlocker(Input() with { IdentityProjectionIsExact = false },
            CharacterCareerQualityBlocker.InvalidIdentityProjection);
        AssertBlocker(Input() with { ProposedInternalIdUnused = false },
            CharacterCareerQualityBlocker.ForeignOrCollidingTarget);
        AssertBlocker(Input() with
        {
            Definition = Definition() with { SourceEnabled = false }
        }, CharacterCareerQualityBlocker.SourceDisabled);
        AssertBlocker(Input() with
        {
            Definition = Definition() with { Implemented = false }
        }, CharacterCareerQualityBlocker.UnimplementedDefinition);
        AssertBlocker(Input() with
        {
            Definition = Definition() with { ChargenOnly = true }
        }, CharacterCareerQualityBlocker.CareerUnavailable);
        AssertBlocker(Input() with { GmAllows = false },
            CharacterCareerQualityBlocker.GmRestricted);

        CharacterCareerQualityInput metatype = RemovalInput(
            CharacterCareerQualityOperation.RemoveLevel,
            CharacterCareerQualityType.Positive, 1,
            CharacterCareerQualityOrigin.Metatype);
        AssertBlocker(metatype, CharacterCareerQualityBlocker.UnremovableOrigin);
        AssertBlocker(metatype with
        {
            MatchingInstances =
            [metatype.MatchingInstances[0] with
                { Origin = CharacterCareerQualityOrigin.Improvement }]
        }, CharacterCareerQualityBlocker.UnremovableOrigin);
    }

    [TestMethod]
    public void Full_removal_uses_the_exact_four_part_key_not_an_extra_origin_key()
    {
        CharacterCareerQualityInput input = RemovalInput(
            CharacterCareerQualityOperation.RemoveAllLevels,
            CharacterCareerQualityType.Negative, 2);
        input = input with
        {
            MatchingInstances =
            [
                input.MatchingInstances[0],
                input.MatchingInstances[1] with
                {
                    Origin = CharacterCareerQualityOrigin.BuiltIn
                }
            ]
        };

        CharacterCareerQualityQuote quote = Quote(input);
        Assert.IsTrue(quote.CanApply);
        Assert.AreEqual(2, quote.AffectedInternalIds.Count);
    }

    [TestMethod]
    public void Identity_source_duplicates_nulls_and_undefined_enums_are_rejected()
    {
        CharacterCareerQualityInput input = Input();
        Assert.IsFalse(CharacterCareerQualityRules.TryCreateQuote(input with
        {
            Identity = input.Identity with { SourceId = Guid.NewGuid() }
        }, out _));
        Assert.IsFalse(CharacterCareerQualityRules.TryCreateQuote(input with
        {
            MatchingInstances =
            [Instance(NewInternalId), Instance(NewInternalId)]
        }, out _));
        Assert.IsFalse(CharacterCareerQualityRules.TryCreateQuote(input with
        {
            MatchingInstances = new CharacterCareerQualityInstance[] { null! }
        }, out _));
        Assert.IsFalse(CharacterCareerQualityRules.TryCreateQuote(input with
        {
            Operation = (CharacterCareerQualityOperation)999
        }, out _));
        Assert.IsFalse(CharacterCareerQualityRules.TryCreateQuote(input with
        {
            Effects = input.Effects with
            {
                AppliedFamilies = [(CharacterCareerQualityEffectFamily)999]
            }
        }, out _));
    }

    [TestMethod]
    public void Quote_binds_authority_arithmetic_and_accepts_maximum_quality_name()
    {
        string maximumName = new('Q', CharacterCareerQualityRules.MaximumTextLength);
        CharacterCareerQualityQuote quote = Quote(Input() with
        {
            Definition = Definition() with { Name = maximumName }
        });

        Assert.IsTrue(quote.ExpenseReason.Length
            > CharacterCareerQualityRules.MaximumTextLength);
        Assert.IsTrue(quote.ExpenseReason.Length
            <= CharacterCareerQualityRules.MaximumReasonLength);
        Assert.IsTrue(CharacterCareerQualityRules.IsCoherent(Plan(quote)));

        Assert.IsFalse(CharacterCareerQualityRules.IsCoherent(quote with
        {
            Authority = quote.Authority with
            {
                Settings = quote.Authority.Settings with { KarmaQuality = 2 }
            }
        }));
        Assert.IsFalse(CharacterCareerQualityRules.IsCoherent(quote with
        {
            RuleKarmaCost = quote.RuleKarmaCost + 1,
            CharacterKarmaDelta = quote.CharacterKarmaDelta - 1
        }));
    }

    [TestMethod]
    public void Plan_requires_explicit_review_unused_id_and_every_cas_binding()
    {
        CharacterCareerQualityQuote quote = Quote(Input());
        Guid transaction = Guid.Parse("22222222-2222-2222-2222-222222222222");
        CharacterCareerQualityPlan validPlan = Plan(quote);

        Assert.IsFalse(CharacterCareerQualityRules.IsCoherent(validPlan with
        {
            ExpectedWorkspaceRevision = long.MaxValue,
            TargetWorkspaceRevision = long.MinValue
        }), "Unchecked long.MaxValue + 1 must not satisfy the CAS transition.");

        AssertPlanRejected(quote, confirmed: false, exists: false,
            quote.LogicalRevision, quote.SourceRevision, quote.RuleDigest,
            quote.Binding.RuntimeFingerprint, quote.Binding.ContentDigest,
            quote.Binding.WorkspaceRevision, quote.Binding.SavedRevision,
            transaction);
        AssertPlanRejected(quote, true, true, quote.LogicalRevision,
            quote.SourceRevision, quote.RuleDigest,
            quote.Binding.RuntimeFingerprint, quote.Binding.ContentDigest,
            quote.Binding.WorkspaceRevision, quote.Binding.SavedRevision,
            transaction);
        foreach (Action<PlanExpectations> mutate in new Action<PlanExpectations>[]
        {
            x => x.Logical = H('0'),
            x => x.Source = H('0'),
            x => x.Rule = H('0'),
            x => x.Runtime = H('0'),
            x => x.Content = H('0'),
            x => x.Workspace++,
            x => x.Saved++
        })
        {
            PlanExpectations values = new(quote);
            mutate(values);
            AssertPlanRejected(quote, true, false, values.Logical,
                values.Source, values.Rule, values.Runtime, values.Content,
                values.Workspace, values.Saved, transaction);
        }
    }

    [TestMethod]
    public void Receipt_requires_atomic_state_and_exact_single_expense()
    {
        CharacterCareerQualityQuote before = Quote(Input());
        CharacterCareerQualityPlan plan = Plan(before);
        CharacterCareerQualityStateObservation after = PostState(plan, before);
        CharacterCareerQualityExpenseObservation expense = Expense(plan);

        Assert.IsTrue(CharacterCareerQualityRules.TryCreateReceipt(
            plan.TransactionId, before, plan, after, expense,
            out CharacterCareerQualityReceipt receipt));
        Assert.IsTrue(CharacterCareerQualityRules.IsCoherent(receipt));
        Assert.AreEqual(64, receipt.ExpenseAuthorityDigest.Length);
        Assert.AreEqual(64, receipt.ReceiptDigest.Length);

        Assert.IsFalse(CharacterCareerQualityRules.TryCreateReceipt(
            plan.TransactionId, before, plan, after,
            expense with { MatchingEntryCount = 2 }, out _));
        Assert.IsFalse(CharacterCareerQualityRules.TryCreateReceipt(
            plan.TransactionId, before, plan,
            after with { AvailableKarma = after.AvailableKarma + 1 },
            expense, out _));
        Assert.IsFalse(CharacterCareerQualityRules.TryCreateReceipt(
            Guid.NewGuid(), before, plan, after, expense, out _));
    }

    [TestMethod]
    public void Recovery_and_correction_are_digest_bound_and_replay_safe()
    {
        CharacterCareerQualityQuote before = Quote(RemovalInput(
            CharacterCareerQualityOperation.RemoveAllLevels,
            CharacterCareerQualityType.Negative, 2));
        CharacterCareerQualityPlan plan = Plan(before);
        CharacterCareerQualityStateObservation after = PostState(plan, before);
        CharacterCareerQualityExpenseObservation expense = Expense(plan);
        Assert.IsTrue(CharacterCareerQualityRules.TryCreateReceipt(
            plan.TransactionId, before, plan, after, expense, out var receipt));

        Assert.IsFalse(CharacterCareerQualityRules.TryRecoverReceipt(
            receipt, receipt.TransactionId, after, expense, H('0'), out _));
        Assert.IsFalse(CharacterCareerQualityRules.TryRecoverReceipt(
            receipt, receipt.TransactionId, after,
            expense with { MatchingEntryCount = 2 }, receipt.ReceiptDigest,
            out _));

        Guid correctionId =
            Guid.Parse("33333333-3333-3333-3333-333333333333");
        Assert.IsTrue(CharacterCareerQualityRules.TryPlanCorrection(
            receipt, after, expense, correctionId, "operator correction",
            false, false, receipt.ReceiptDigest, out var correction));
        Assert.AreEqual(2, correction.RestoreInstances.Count);
        Assert.AreEqual(0, correction.RemoveInternalIds.Count);
        Assert.AreEqual(receipt.CharacterKarmaBefore,
            correction.SavedCharacterKarma);
        Assert.IsTrue(correction.RemoveExpense);
        Assert.IsTrue(CharacterCareerQualityRules.IsCoherent(correction));

        Assert.IsFalse(CharacterCareerQualityRules.IsCoherent(correction with
        {
            RestoreInstances = [],
            CorrectionDigest = H('0')
        }));

        Assert.IsFalse(CharacterCareerQualityRules.TryPlanCorrection(
            receipt, after, expense, correctionId, "operator correction",
            true, false, receipt.ReceiptDigest, out _));
        Assert.IsFalse(CharacterCareerQualityRules.TryPlanCorrection(
            receipt, after, expense, correctionId, "operator correction",
            false, true, receipt.ReceiptDigest, out _));
    }

    [TestMethod]
    public void Acquisition_correction_removes_the_added_internal_id()
    {
        CharacterCareerQualityQuote before = Quote(Input());
        CharacterCareerQualityPlan plan = Plan(before);
        CharacterCareerQualityStateObservation after = PostState(plan, before);
        CharacterCareerQualityExpenseObservation expense = Expense(plan);
        CharacterCareerQualityRules.TryCreateReceipt(
            plan.TransactionId, before, plan, after, expense, out var receipt);

        Assert.IsTrue(CharacterCareerQualityRules.TryPlanCorrection(
            receipt, after, expense, Guid.NewGuid(), "reverse add", false,
            false, receipt.ReceiptDigest, out var correction));
        Assert.AreEqual(0, correction.RestoreInstances.Count);
        CollectionAssert.AreEqual(new[] { NewInternalId },
            correction.RemoveInternalIds.ToArray());
    }

    private static CharacterCareerQualityInput Input()
        => new(
            CharacterCareerQualityOperation.AcquireLevel,
            new CharacterCareerQualityIdentity(NewInternalId, SourceId),
            true, "sr5", true, true, true, false, true, false, false, 0,
            100, string.Empty, string.Empty, Definition(),
            new CharacterCareerQualitySettings(1, false, false),
            new CharacterCareerQualityEligibilityProjection(
                true, true, true, true, true, true, [], [], H('e')),
            new CharacterCareerQualityEffectProjection(
                true, [], [], 0, H('f')),
            [], Binding(), "source-state", "rule-state");

    private static CharacterCareerQualityInput RemovalInput(
        CharacterCareerQualityOperation operation,
        CharacterCareerQualityType type,
        int levels,
        CharacterCareerQualityOrigin origin = CharacterCareerQualityOrigin.Selected)
    {
        CharacterCareerQualityInstance[] instances = Instances(levels, type, origin);
        return Input() with
        {
            Operation = operation,
            Identity = instances[0].Identity,
            ProposedInternalIdUnused = false,
            TargetOwnedByCharacter = true,
            Definition = Definition(type) with
            {
                LevelLimit = Math.Max(1, levels)
            },
            MatchingInstances = instances
        };
    }

    private static CharacterCareerQualityDefinition Definition(
        CharacterCareerQualityType type = CharacterCareerQualityType.Positive)
        => new(
            SourceId, "Test Quality", type,
            type == CharacterCareerQualityType.Positive ? 5 : -5,
            true, true, false, false, false, true, false, false, false,
            false, 0, false, true, false, true, false, 0);

    private static CharacterCareerQualityExecutionBinding Binding(
        long workspace = 41,
        long saved = 19)
        => new("owner-1", "workspace-1", workspace, saved, H('a'), H('b'));

    private static CharacterCareerQualityInstance[] Instances(
        int count,
        CharacterCareerQualityType type = CharacterCareerQualityType.Positive,
        CharacterCareerQualityOrigin origin = CharacterCareerQualityOrigin.Selected)
        => Enumerable.Range(1, count)
            .Select(index => Instance(Guid.Parse(
                $"{index:D8}-1111-1111-1111-{index:D12}"), type, origin))
            .ToArray();

    private static CharacterCareerQualityInstance Instance(
        Guid internalId,
        CharacterCareerQualityType type = CharacterCareerQualityType.Positive,
        CharacterCareerQualityOrigin origin = CharacterCareerQualityOrigin.Selected)
        => new(new CharacterCareerQualityIdentity(internalId, SourceId),
            string.Empty, string.Empty, type, origin);

    private static CharacterCareerQualityQuote Quote(
        CharacterCareerQualityInput input)
    {
        Assert.IsTrue(CharacterCareerQualityRules.TryCreateQuote(input,
            out CharacterCareerQualityQuote quote));
        Assert.IsTrue(CharacterCareerQualityRules.IsCoherent(quote));
        return quote;
    }

    private static CharacterCareerQualityPlan Plan(
        CharacterCareerQualityQuote quote)
    {
        Guid transaction =
            Guid.Parse("22222222-2222-2222-2222-222222222222");
        Assert.IsTrue(CharacterCareerQualityRules.TryPlan(
            quote, quote.LogicalRevision, quote.SourceRevision,
            quote.RuleDigest, quote.Binding.RuntimeFingerprint,
            quote.Binding.ContentDigest, quote.Binding.WorkspaceRevision,
            quote.Binding.SavedRevision, true, false, transaction,
            ExpenseDate, out CharacterCareerQualityPlan plan));
        Assert.IsTrue(CharacterCareerQualityRules.IsCoherent(plan));
        return plan;
    }

    private static CharacterCareerQualityStateObservation PostState(
        CharacterCareerQualityPlan plan,
        CharacterCareerQualityQuote before)
    {
        CharacterCareerQualityExecutionBinding binding = Binding(
            plan.TargetWorkspaceRevision, plan.TargetSavedRevision);
        Assert.IsTrue(CharacterCareerQualityRules.TryCreateStateObservation(
            plan.Identity, plan.Definition, plan.Extra, plan.SourceName,
            plan.InstancesAfter, plan.SavedCharacterKarma, binding,
            "source-state", before.RuleDigest,
            out CharacterCareerQualityStateObservation state));
        Assert.IsTrue(CharacterCareerQualityRules.IsCoherent(state));
        return state;
    }

    private static CharacterCareerQualityExpenseObservation Expense(
        CharacterCareerQualityPlan plan)
        => new(
            1, plan.ExpenseId, plan.ExpenseDateLocal, plan.ExpenseAmount,
            plan.ExpenseReason, plan.ExpenseType, plan.ExpenseRefund,
            plan.ForceCareerVisible, plan.KarmaUndoType, plan.NuyenUndoType,
            plan.UndoObjectId, plan.UndoQuantity, plan.UndoExtra);

    private static CharacterCareerQualityExpenseObservation NoExpense()
        => new(
            0, Guid.Empty, DateTime.MinValue, 0, string.Empty, string.Empty,
            false, false, string.Empty, string.Empty, string.Empty, 0m,
            string.Empty);

    private static void AssertBlocker(
        CharacterCareerQualityInput input,
        CharacterCareerQualityBlocker expected)
    {
        CharacterCareerQualityQuote quote = Quote(input);
        Assert.IsFalse(quote.CanApply);
        Assert.AreEqual(expected, quote.Blocker);
    }

    private static void AssertPlanRejected(
        CharacterCareerQualityQuote quote,
        bool confirmed,
        bool exists,
        string logical,
        string source,
        string rule,
        string runtime,
        string content,
        long workspace,
        long saved,
        Guid transaction)
        => Assert.IsFalse(CharacterCareerQualityRules.TryPlan(
            quote, logical, source, rule, runtime, content, workspace, saved,
            confirmed, exists, transaction, ExpenseDate, out _));

    private static string H(char value) => new(value, 64);

    private sealed class PlanExpectations
    {
        public PlanExpectations(CharacterCareerQualityQuote quote)
        {
            Logical = quote.LogicalRevision;
            Source = quote.SourceRevision;
            Rule = quote.RuleDigest;
            Runtime = quote.Binding.RuntimeFingerprint;
            Content = quote.Binding.ContentDigest;
            Workspace = quote.Binding.WorkspaceRevision;
            Saved = quote.Binding.SavedRevision;
        }

        public string Logical { get; set; }
        public string Source { get; set; }
        public string Rule { get; set; }
        public string Runtime { get; set; }
        public string Content { get; set; }
        public long Workspace { get; set; }
        public long Saved { get; set; }
    }
}
