using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterAfterRunSettlementRulesTests
{
    private static readonly CharacterAfterRunSettlementIdentity Identity = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"));
    private static readonly Guid TransactionId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");

    [TestMethod]
    public void Quote_is_deterministic_reviewed_and_binds_all_authorities()
    {
        CharacterAfterRunSettlementQuote quote = Quote(Input());

        Assert.AreEqual(3, quote.HeatAfter);
        Assert.AreEqual(12, quote.StreetCredAfter);
        Assert.AreEqual(5, quote.NotorietyAfter);
        Assert.AreEqual(7, quote.PublicAwarenessAfter);
        Assert.AreEqual(11, quote.ContactKarmaCost);
        Assert.AreEqual(19, quote.KarmaAfter);
        Assert.AreEqual(2, quote.Contacts.Count);
        Assert.AreEqual(0, quote.Contacts[0].KarmaCost);
        Assert.AreEqual(11, quote.Contacts[1].KarmaCost);
        Assert.IsTrue(quote.CanSettle);
        Assert.AreEqual(CharacterAfterRunSettlementBlocker.None, quote.Blocker);
        Assert.IsTrue(quote.Prerequisites.All(static value => value.Satisfied));
        Assert.IsTrue(CharacterAfterRunSettlementRules.IsCanonicalDigest(
            quote.SourceDigest));
        Assert.IsTrue(CharacterAfterRunSettlementRules.IsCanonicalDigest(
            quote.CustomDataDigest));
        Assert.IsTrue(CharacterAfterRunSettlementRules.IsCanonicalDigest(
            quote.GmPolicyDigest));
        Assert.IsTrue(CharacterAfterRunSettlementRules.IsCanonicalDigest(
            quote.RuntimeDigest));
        Assert.IsTrue(CharacterAfterRunSettlementRules.IsCanonicalDigest(
            quote.LogicalDigest));
    }

    [TestMethod]
    public void Missing_rejected_or_wrong_actor_reviews_fail_closed()
    {
        AssertBlocker(
            Input() with { GmReview = null },
            CharacterAfterRunSettlementBlocker.GmReviewPending);
        AssertBlocker(
            Input() with
            {
                GmReview = Review(
                    CharacterAfterRunReviewRole.GameMaster,
                    "gm-17",
                    CharacterAfterRunReviewDecision.Rejected)
            },
            CharacterAfterRunSettlementBlocker.GmRejected);
        AssertBlocker(
            Input() with { OwnerReview = null },
            CharacterAfterRunSettlementBlocker.OwnerReviewPending);
        AssertBlocker(
            Input() with
            {
                OwnerReview = Review(
                    CharacterAfterRunReviewRole.CharacterOwner,
                    "owner-foreign",
                    CharacterAfterRunReviewDecision.Approved)
            },
            CharacterAfterRunSettlementBlocker.OwnerReviewPending);
        AssertBlocker(
            Input() with
            {
                OwnerReview = Review(
                    CharacterAfterRunReviewRole.CharacterOwner,
                    "owner-23",
                    CharacterAfterRunReviewDecision.Rejected)
            },
            CharacterAfterRunSettlementBlocker.OwnerRejected);
    }

    [TestMethod]
    public void Policy_bounds_and_karma_are_explicit_blockers()
    {
        AssertBlocker(
            Input() with { HeatDelta = 100 },
            CharacterAfterRunSettlementBlocker.HeatOutsidePolicy);
        AssertBlocker(
            Input() with { StreetCredDelta = 100 },
            CharacterAfterRunSettlementBlocker.ReputationOutsidePolicy);
        AssertBlocker(
            Input() with
            {
                Settings = Input().Settings with
                {
                    AllowRunRewardContacts = false
                }
            },
            CharacterAfterRunSettlementBlocker.ContactOutsidePolicy);
        AssertBlocker(
            Input() with { CurrentKarma = 10 },
            CharacterAfterRunSettlementBlocker.InsufficientKarma);
    }

    [TestMethod]
    public void Calculated_public_awareness_ignores_requested_delta_deterministically()
    {
        CharacterAfterRunSettlementInput input = Input() with
        {
            CurrentPublicAwareness = 99,
            PublicAwarenessDelta = -50,
            Settings = Input().Settings with
            {
                UseCalculatedPublicAwareness = true
            }
        };
        CharacterAfterRunSettlementQuote quote = Quote(input);

        Assert.AreEqual(
            quote.StreetCredAfter + quote.NotorietyAfter / 3,
            quote.PublicAwarenessAfter);
        Assert.AreEqual(-50, quote.RequestedPublicAwarenessDelta);
    }

    [TestMethod]
    public void Plan_requires_confirmation_and_all_five_quote_bindings()
    {
        CharacterAfterRunSettlementQuote quote = Quote(Input());

        Assert.IsFalse(TryPlan(quote, explicitlyConfirmed: false, out _));
        Assert.IsFalse(CharacterAfterRunSettlementRules.TryCreatePlan(
            quote,
            new string('0', 64),
            quote.CustomDataDigest,
            quote.GmPolicyDigest,
            quote.RuntimeDigest,
            quote.LogicalDigest,
            true,
            false,
            TransactionId,
            out _));
        Assert.IsFalse(CharacterAfterRunSettlementRules.TryCreatePlan(
            quote,
            quote.SourceDigest,
            quote.CustomDataDigest,
            quote.GmPolicyDigest,
            quote.RuntimeDigest,
            quote.LogicalDigest,
            true,
            true,
            TransactionId,
            out _));

        Assert.IsTrue(TryPlan(quote, explicitlyConfirmed: true,
            out CharacterAfterRunSettlementPlan plan));
        Assert.AreEqual(quote.HeatAfter, plan.TargetHeat);
        Assert.AreEqual(quote.KarmaAfter, plan.TargetKarma);
        Assert.AreEqual(TransactionId, plan.ExpenseId);
        Assert.AreEqual(-11, plan.ExpenseAmount);
        Assert.IsTrue(CharacterAfterRunSettlementRules.IsCoherent(plan));
    }

    [TestMethod]
    public void Receipt_requires_exact_atomic_post_state_and_unique_expense()
    {
        Completed completed = Complete();

        Assert.IsTrue(CharacterAfterRunSettlementRules.IsCoherent(
            completed.Receipt));
        Assert.AreEqual(2, completed.Receipt.AddedContacts.Count);
        Assert.AreEqual(19, completed.Receipt.KarmaAfter);
        Assert.AreEqual(completed.Quote.LogicalDigest,
            completed.Receipt.LogicalDigestBefore);
        Assert.IsTrue(CharacterAfterRunSettlementRules.IsCanonicalDigest(
            completed.Receipt.LogicalDigestAfter));

        Assert.IsFalse(CharacterAfterRunSettlementRules.TryCreateReceipt(
            TransactionId,
            completed.Quote,
            completed.Plan,
            completed.Observation with { MatchingTransactionCount = 2 },
            out _));
        Assert.IsFalse(CharacterAfterRunSettlementRules.TryCreateReceipt(
            TransactionId,
            completed.Quote,
            completed.Plan,
            completed.Observation with
            {
                Expense = completed.Observation.Expense with
                {
                    MatchingEntryCount = 2
                }
            },
            out _));
        Assert.IsFalse(CharacterAfterRunSettlementRules.TryCreateReceipt(
            TransactionId,
            completed.Quote,
            completed.Plan,
            completed.Observation with { Heat = completed.Observation.Heat + 1 },
            out _));
    }

    [TestMethod]
    public void Receipt_recovery_and_correction_are_digest_and_review_bound()
    {
        Completed completed = Complete();
        Assert.IsTrue(CharacterAfterRunSettlementRules.TryRecoverReceipt(
            completed.Receipt,
            TransactionId,
            completed.Observation,
            completed.Receipt.ReceiptDigest,
            out CharacterAfterRunSettlementReceipt recovered));
        Assert.AreSame(completed.Receipt, recovered);
        Assert.IsFalse(CharacterAfterRunSettlementRules.TryRecoverReceipt(
            completed.Receipt,
            TransactionId,
            completed.Observation,
            new string('0', 64),
            out _));

        Guid correctionId =
            Guid.Parse("55555555-5555-5555-5555-555555555555");
        CharacterAfterRunReview correctionGm = Review(
            CharacterAfterRunReviewRole.GameMaster,
            "gm-17",
            CharacterAfterRunReviewDecision.Approved,
            "Correct settlement");
        CharacterAfterRunReview correctionOwner = Review(
            CharacterAfterRunReviewRole.CharacterOwner,
            "owner-23",
            CharacterAfterRunReviewDecision.Approved,
            "Accept correction");
        Assert.IsTrue(CharacterAfterRunSettlementRules.TryPlanCorrection(
            completed.Receipt,
            completed.Observation,
            correctionId,
            "Undo wrong run allocation",
            correctionGm,
            "gm-17",
            correctionOwner,
            "owner-23",
            correctionIdAlreadyExists: false,
            originalTransactionAlreadyCorrected: false,
            completed.Receipt.ReceiptDigest,
            out CharacterAfterRunSettlementCorrectionPlan correction));
        Assert.AreEqual(completed.Receipt.HeatBefore, correction.RestoredHeat);
        Assert.AreEqual(completed.Receipt.KarmaBefore, correction.RestoredKarma);
        Assert.AreEqual(2, correction.ContactIdsToRemove.Count);
        Assert.IsTrue(CharacterAfterRunSettlementRules.IsCoherent(correction));

        Assert.IsFalse(CharacterAfterRunSettlementRules.TryPlanCorrection(
            completed.Receipt,
            completed.Observation,
            Guid.NewGuid(),
            "Undo",
            correctionGm,
            "wrong-gm",
            correctionOwner,
            "owner-23",
            false,
            false,
            completed.Receipt.ReceiptDigest,
            out _));
        Assert.IsFalse(CharacterAfterRunSettlementRules.TryPlanCorrection(
            completed.Receipt,
            completed.Observation with { Karma = completed.Observation.Karma + 1 },
            Guid.NewGuid(),
            "Undo",
            correctionGm,
            "gm-17",
            correctionOwner,
            "owner-23",
            false,
            false,
            completed.Receipt.ReceiptDigest,
            out _));
    }

    [TestMethod]
    public void Digests_bind_source_custom_gm_runtime_reviews_and_order()
    {
        CharacterAfterRunSettlementInput input = Input();
        CharacterAfterRunSettlementQuote original = Quote(input);
        CharacterAfterRunSettlementQuote reordered = Quote(input with
        {
            ContactProposals = input.ContactProposals.Reverse().ToArray()
        });
        Assert.AreEqual(original.LogicalDigest, reordered.LogicalDigest);

        Assert.AreNotEqual(original.SourceDigest,
            Quote(input with { RawSourceState = "source:v2" }).SourceDigest);
        Assert.AreNotEqual(original.CustomDataDigest,
            Quote(input with { RawCustomDataState = "custom:v2" })
                .CustomDataDigest);
        Assert.AreNotEqual(original.GmPolicyDigest,
            Quote(input with { RawGmPolicyState = "gm-policy:v2" })
                .GmPolicyDigest);
        Assert.AreNotEqual(original.RuntimeDigest,
            Quote(input with { RawRuntimeState = "runtime:v2" }).RuntimeDigest);
        Assert.AreNotEqual(original.OwnerReviewDigest,
            Quote(input with
            {
                OwnerReview = Input().OwnerReview! with { Reason = "different" }
            }).OwnerReviewDigest);
    }

    private static void AssertBlocker(
        CharacterAfterRunSettlementInput input,
        CharacterAfterRunSettlementBlocker expected)
    {
        CharacterAfterRunSettlementQuote quote = Quote(input);
        Assert.IsFalse(quote.CanSettle);
        Assert.AreEqual(expected, quote.Blocker);
    }

    private static bool TryPlan(
        CharacterAfterRunSettlementQuote quote,
        bool explicitlyConfirmed,
        out CharacterAfterRunSettlementPlan plan)
        => CharacterAfterRunSettlementRules.TryCreatePlan(
            quote,
            quote.SourceDigest,
            quote.CustomDataDigest,
            quote.GmPolicyDigest,
            quote.RuntimeDigest,
            quote.LogicalDigest,
            explicitlyConfirmed,
            transactionIdAlreadyExists: false,
            TransactionId,
            out plan);

    private static CharacterAfterRunSettlementQuote Quote(
        CharacterAfterRunSettlementInput input)
    {
        Assert.IsTrue(CharacterAfterRunSettlementRules.TryCreateQuote(
            input,
            out CharacterAfterRunSettlementQuote quote));
        Assert.IsTrue(CharacterAfterRunSettlementRules.IsCoherent(quote));
        return quote;
    }

    private static Completed Complete()
    {
        CharacterAfterRunSettlementQuote quote = Quote(Input());
        Assert.IsTrue(TryPlan(quote, true,
            out CharacterAfterRunSettlementPlan plan));
        CharacterAfterRunSettlementObservation observation = Observation(plan);
        Assert.IsTrue(CharacterAfterRunSettlementRules.TryCreateReceipt(
            TransactionId,
            quote,
            plan,
            observation,
            out CharacterAfterRunSettlementReceipt receipt));
        return new(quote, plan, observation, receipt);
    }

    internal static CharacterAfterRunSettlementObservation Observation(
        CharacterAfterRunSettlementPlan plan)
        => new(
            MatchingTransactionCount: 1,
            plan.TargetHeat,
            plan.TargetStreetCred,
            plan.TargetNotoriety,
            plan.TargetPublicAwareness,
            plan.TargetKarma,
            plan.ContactsToAdd,
            new CharacterAfterRunExpenseObservation(
                plan.ContactKarmaCost == 0 ? 0 : 1,
                plan.ExpenseId,
                plan.ExpenseAmount,
                plan.ExpenseReason,
                plan.ContactKarmaCost == 0 ? string.Empty : "Karma",
                Refund: false),
            plan.ExpectedSourceDigest,
            plan.ExpectedCustomDataDigest,
            plan.ExpectedGmPolicyDigest,
            plan.ExpectedRuntimeDigest);

    internal static CharacterAfterRunSettlementInput Input()
        => new(
            Identity,
            Created: true,
            RulesetId: CharacterAfterRunSettlementRules.RulesetId,
            TargetOwnedByCharacter: true,
            ProjectionIsExact: true,
            RunCompleted: true,
            ProposalAlreadySettled: false,
            ExpectedGmActorId: "gm-17",
            ExpectedOwnerActorId: "owner-23",
            CurrentHeat: 1,
            CurrentStreetCred: 10,
            CurrentNotoriety: 4,
            CurrentPublicAwareness: 6,
            CurrentKarma: 30,
            HeatDelta: 2,
            StreetCredDelta: 2,
            NotorietyDelta: 1,
            PublicAwarenessDelta: 1,
            new CharacterAfterRunSettlementSettings(
                MaximumHeat: 20,
                MaximumReputation: 100,
                MaximumConnection: 12,
                MaximumLoyalty: 6,
                KarmaPerContactPoint: 1,
                AllowRunRewardContacts: true,
                AllowKarmaPurchasedContacts: true,
                UseCalculatedPublicAwareness: false),
            ContactProposals:
            [
                new(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "Fixer Jane",
                    "Fixer",
                    "Seattle",
                    4,
                    3,
                    CharacterAfterRunContactProposalKind.RunReward),
                new(
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    "Doc Red",
                    "Street Doc",
                    "Tacoma",
                    6,
                    5,
                    CharacterAfterRunContactProposalKind.KarmaPurchase)
            ],
            GmReview: Review(
                CharacterAfterRunReviewRole.GameMaster,
                "gm-17",
                CharacterAfterRunReviewDecision.Approved,
                "Run settlement approved"),
            OwnerReview: Review(
                CharacterAfterRunReviewRole.CharacterOwner,
                "owner-23",
                CharacterAfterRunReviewDecision.Approved,
                "I accept the complete settlement"),
            RawSourceState: "source:v1",
            RawCustomDataState: "custom:v1",
            RawGmPolicyState: "gm-policy:v1",
            RawRuntimeState: "runtime:v1");

    private static CharacterAfterRunReview Review(
        CharacterAfterRunReviewRole role,
        string actor,
        CharacterAfterRunReviewDecision decision,
        string reason = "reviewed")
        => new(
            role == CharacterAfterRunReviewRole.GameMaster
                ? Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")
                : Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            role,
            actor,
            decision,
            reason);

    private sealed record Completed(
        CharacterAfterRunSettlementQuote Quote,
        CharacterAfterRunSettlementPlan Plan,
        CharacterAfterRunSettlementObservation Observation,
        CharacterAfterRunSettlementReceipt Receipt);
}
