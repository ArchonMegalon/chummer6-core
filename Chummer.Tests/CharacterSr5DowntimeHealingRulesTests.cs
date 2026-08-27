using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterSr5DowntimeHealingRulesTests
{
    private static readonly DateTimeOffset Start =
        new(2081, 5, 12, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Quote_derives_sr5_track_pool_interval_and_source_anchor()
    {
        CharacterSr5HealingQuote stun = Quote(Input(CharacterSr5HealingTrack.Stun));
        Assert.AreEqual(7, stun.BaseDicePool);
        Assert.AreEqual(7, stun.DicePool);
        Assert.AreEqual(TimeSpan.FromHours(1), stun.Interval);
        Assert.AreEqual(CharacterSr5DowntimeHealingRules.StunRuleId, stun.SourceAnchor.RuleId);
        Assert.AreEqual(0, stun.NuyenCost);
        Assert.IsTrue(stun.CanPlan);

        CharacterSr5HealingQuote physical = Quote(Input(CharacterSr5HealingTrack.Physical));
        Assert.AreEqual(6, physical.BaseDicePool);
        Assert.AreEqual(6, physical.DicePool);
        Assert.AreEqual(TimeSpan.FromDays(1), physical.Interval);
        Assert.AreEqual(
            CharacterSr5DowntimeHealingRules.PhysicalRuleId,
            physical.SourceAnchor.RuleId);
        Assert.AreNotEqual(stun.QuoteDigest, physical.QuoteDigest);

        CharacterSr5HealingQuote sourceChanged = Quote(
            Input(CharacterSr5HealingTrack.Physical) with { SourceDigest = Hex('e') });
        Assert.AreNotEqual(physical.QuoteDigest, sourceChanged.QuoteDigest);
        Assert.AreEqual(sourceChanged.SourceDigest, sourceChanged.SourceAnchor.SourceDigest);
    }

    [TestMethod]
    public void Physical_interval_runs_quote_reserve_start_roll_complete_and_receipt()
    {
        CharacterSr5HealingQuote quote = Quote(Input(CharacterSr5HealingTrack.Physical));
        CharacterSr5HealingPlan plan = Plan(quote);
        Assert.IsFalse(CharacterSr5DowntimeHealingRules.TryReserve(
            quote,
            plan,
            quote.QuoteDigest,
            plan.PlanDigest,
            quote.WorkspaceRevision,
            quote.CalendarRevision,
            Start.AddMinutes(-5),
            explicitlyConfirmed: false,
            out _));
        CharacterSr5HealingReservation reservation = Reserve(quote, plan);
        Assert.IsFalse(CharacterSr5DowntimeHealingRules.TryStart(
            quote,
            reservation,
            reservation.ReservationDigest,
            quote.WorkspaceRevision,
            quote.CalendarRevision,
            reservation.Plan.StartsAtUtc,
            explicitlyConfirmed: false,
            out _));
        CharacterSr5HealingStartedInterval started = StartInterval(quote, reservation);
        Assert.AreEqual(Start.AddDays(1), started.EligibleCompletionUtc);

        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateRollReceipt(
            started,
            started.StartDigest,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            started.EligibleCompletionUtc,
            [5, 6, 2, 1, 3, 4],
            out CharacterSr5HealingRollReceipt roll));
        Assert.AreEqual(2, roll.Hits);
        Assert.IsFalse(roll.Glitch);

        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateCompletionQuote(
            quote,
            started,
            roll,
            out CharacterSr5HealingCompletionQuote completion));
        Assert.IsTrue(completion.CanComplete);
        Assert.AreEqual(5, completion.DamageBoxesBefore);
        Assert.AreEqual(2, completion.BoxesHealed);
        Assert.AreEqual(3, completion.DamageBoxesAfter);

        Guid transactionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        Assert.IsFalse(CharacterSr5DowntimeHealingRules.TryCreateCompletionCommand(
            quote,
            started,
            completion,
            transactionId,
            quote.QuoteDigest,
            started.StartDigest,
            completion.CompletionQuoteDigest,
            explicitlyConfirmed: false,
            out _));
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateCompletionCommand(
            quote,
            started,
            completion,
            transactionId,
            quote.QuoteDigest,
            started.StartDigest,
            completion.CompletionQuoteDigest,
            explicitlyConfirmed: true,
            out CharacterSr5HealingCompletionCommand command));
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateCompletionReceipt(
            completion,
            command,
            observedWorkspaceRevision: 42,
            observedCalendarRevision: 8,
            observedDamageBoxes: 3,
            out CharacterSr5HealingCompletionReceipt receipt));
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.IsCoherent(
            receipt,
            completion,
            command));
        Assert.AreEqual(
            CharacterSr5HealingOutcome.Applied,
            CharacterSr5DowntimeHealingRules.ResolveCompletionOutcome(
                completion, command, 42, 8, 3, receipt));
    }

    [TestMethod]
    public void Completion_recovery_distinguishes_not_applied_pending_and_conflict()
    {
        (CharacterSr5HealingCompletionQuote completion,
            CharacterSr5HealingCompletionCommand command) = CompletionCommand();

        Assert.AreEqual(
            CharacterSr5HealingOutcome.NotApplied,
            CharacterSr5DowntimeHealingRules.ResolveCompletionOutcome(
                completion, command, 41, 7, 5, receipt: null));
        Assert.AreEqual(
            CharacterSr5HealingOutcome.Pending,
            CharacterSr5DowntimeHealingRules.ResolveCompletionOutcome(
                completion, command, 42, 8, 3, receipt: null));
        Assert.AreEqual(
            CharacterSr5HealingOutcome.Conflict,
            CharacterSr5DowntimeHealingRules.ResolveCompletionOutcome(
                completion, command, 43, 9, 3, receipt: null));
        Assert.IsFalse(CharacterSr5DowntimeHealingRules.TryCreateCompletionReceipt(
            completion,
            command,
            observedWorkspaceRevision: 42,
            observedCalendarRevision: 8,
            observedDamageBoxes: 4,
            out _));
    }

    [TestMethod]
    public void Early_stale_wrong_pool_and_glitch_completion_fail_closed()
    {
        CharacterSr5HealingQuote quote = Quote(Input(CharacterSr5HealingTrack.Stun));
        CharacterSr5HealingPlan plan = Plan(quote);
        CharacterSr5HealingReservation reservation = Reserve(quote, plan);
        CharacterSr5HealingStartedInterval started = StartInterval(quote, reservation);

        Assert.IsFalse(CharacterSr5DowntimeHealingRules.TryCreateRollReceipt(
            started,
            started.StartDigest,
            Guid.NewGuid(),
            started.EligibleCompletionUtc.AddTicks(-1),
            [1, 2, 3, 4, 5, 6, 2],
            out _));
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateRollReceipt(
            started,
            started.StartDigest,
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            started.EligibleCompletionUtc,
            [1, 1, 1, 1, 5, 2, 3],
            out CharacterSr5HealingRollReceipt glitch));
        Assert.IsTrue(glitch.Glitch);
        Assert.IsFalse(glitch.CriticalGlitch);
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateCompletionQuote(
            quote,
            started,
            glitch,
            out CharacterSr5HealingCompletionQuote blocked));
        Assert.IsFalse(blocked.CanComplete);
        Assert.AreEqual(
            CharacterSr5HealingBlocker.GlitchRequiresResolution,
            blocked.Blocker);
        Assert.IsFalse(CharacterSr5DowntimeHealingRules.TryCreateCompletionCommand(
            quote,
            started,
            blocked,
            Guid.NewGuid(),
            quote.QuoteDigest,
            started.StartDigest,
            blocked.CompletionQuoteDigest,
            true,
            out _));
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateRollReceipt(
            started,
            started.StartDigest,
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            started.EligibleCompletionUtc,
            [5],
            out CharacterSr5HealingRollReceipt wrongPool));
        Assert.IsFalse(CharacterSr5DowntimeHealingRules.TryCreateCompletionQuote(
            quote,
            started,
            wrongPool,
            out _));

        CharacterSr5HealingQuote changed = Quote(
            Input(CharacterSr5HealingTrack.Stun) with { WorkspaceRevision = 42 });
        Assert.IsFalse(CharacterSr5DowntimeHealingRules.TryStart(
            changed,
            reservation,
            reservation.ReservationDigest,
            42,
            7,
            plan.StartsAtUtc,
            true,
            out _));
        Assert.IsFalse(CharacterSr5DowntimeHealingRules.TryReserve(
            quote,
            plan,
            quote.QuoteDigest,
            plan.PlanDigest,
            observedWorkspaceRevision: 42,
            observedCalendarRevision: 7,
            reservedAtUtc: Start.AddMinutes(-5),
            explicitlyConfirmed: true,
            out _));
    }

    [TestMethod]
    public void Cancel_and_interrupt_have_zero_refund_and_digest_bound_recovery()
    {
        CharacterSr5HealingQuote quote = Quote(Input(CharacterSr5HealingTrack.Stun));
        CharacterSr5HealingPlan plan = Plan(quote);
        CharacterSr5HealingReservation reservation = Reserve(quote, plan);

        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateCancellationQuote(
            quote,
            reservation,
            started: null,
            requestedAtUtc: Start.AddMinutes(-1),
            out CharacterSr5HealingCancellationQuote cancellation));
        Assert.AreEqual(
            CharacterSr5HealingCancellationKind.CancelReservation,
            cancellation.Kind);
        Assert.AreEqual(TimeSpan.Zero, cancellation.Elapsed);
        Assert.AreEqual(0, cancellation.RefundNuyen);
        Assert.AreEqual(0, cancellation.RetainedNuyen);
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateCancellationCommand(
            cancellation,
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            cancellation.CancellationQuoteDigest,
            cancellation.SubjectDigest,
            true,
            out CharacterSr5HealingCancellationCommand command));
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateCancellationReceipt(
            cancellation,
            command,
            42,
            8,
            observedDamageBoxes: 5,
            out CharacterSr5HealingCancellationReceipt receipt));
        Assert.AreEqual(
            CharacterSr5HealingOutcome.Applied,
            CharacterSr5DowntimeHealingRules.ResolveCancellationOutcome(
                cancellation, command, 42, 8, 5, receipt));
        Assert.AreEqual(
            CharacterSr5HealingOutcome.NotApplied,
            CharacterSr5DowntimeHealingRules.ResolveCancellationOutcome(
                cancellation, command, 41, 7, 5, receipt: null));
        Assert.AreEqual(
            CharacterSr5HealingOutcome.Pending,
            CharacterSr5DowntimeHealingRules.ResolveCancellationOutcome(
                cancellation, command, 42, 8, 5, receipt: null));
        Assert.AreEqual(
            CharacterSr5HealingOutcome.Conflict,
            CharacterSr5DowntimeHealingRules.ResolveCancellationOutcome(
                cancellation, command, 43, 8, 5, receipt: null));

        CharacterSr5HealingStartedInterval started = StartInterval(quote, reservation);
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateCancellationQuote(
            quote,
            reservation: null,
            started: started,
            requestedAtUtc: Start.AddMinutes(30),
            out CharacterSr5HealingCancellationQuote interruption));
        Assert.AreEqual(
            CharacterSr5HealingCancellationKind.InterruptStartedInterval,
            interruption.Kind);
        Assert.AreEqual(TimeSpan.FromMinutes(30), interruption.Elapsed);
        Assert.IsFalse(CharacterSr5DowntimeHealingRules.TryCreateCancellationCommand(
            interruption,
            Guid.NewGuid(),
            Hex('0'),
            interruption.SubjectDigest,
            true,
            out _));
    }

    [TestMethod]
    public void Blocked_quotes_are_explicit_and_invalid_authority_is_rejected()
    {
        CharacterSr5HealingQuote noDamage = Quote(
            Input(CharacterSr5HealingTrack.Stun) with { DamageBoxes = 0 });
        Assert.IsFalse(noDamage.CanPlan);
        Assert.AreEqual(CharacterSr5HealingBlocker.NoDamage, noDamage.Blocker);

        CharacterSr5HealingQuote wrongRules = Quote(
            Input(CharacterSr5HealingTrack.Stun) with { RulesetId = "sr6" });
        Assert.IsFalse(wrongRules.CanPlan);
        Assert.AreEqual(
            CharacterSr5HealingBlocker.UnsupportedRuleset,
            wrongRules.Blocker);

        CharacterSr5HealingQuote noPool = Quote(
            Input(CharacterSr5HealingTrack.Stun) with
            {
                DicePoolModifiers =
                [
                    new CharacterSr5HealingDicePoolModifier(
                        "wound-modifier",
                        -7,
                        Hex('f'))
                ]
            });
        Assert.IsFalse(noPool.CanPlan);
        Assert.AreEqual(CharacterSr5HealingBlocker.NoDicePool, noPool.Blocker);
        Assert.IsFalse(CharacterSr5DowntimeHealingRules.TryCreateQuote(
            Input(CharacterSr5HealingTrack.Stun) with { ContentDigest = "not-a-digest" },
            out _));
        Assert.IsFalse(CharacterSr5DowntimeHealingRules.TryCreateQuote(
            Input(CharacterSr5HealingTrack.Stun) with
            {
                DicePoolModifiers =
                [
                    new("z-last", 1, Hex('a')),
                    new("a-first", -1, Hex('b'))
                ]
            },
            out _));
    }

    private static (
        CharacterSr5HealingCompletionQuote Completion,
        CharacterSr5HealingCompletionCommand Command) CompletionCommand()
    {
        CharacterSr5HealingQuote quote = Quote(Input(CharacterSr5HealingTrack.Physical));
        CharacterSr5HealingStartedInterval started = StartInterval(quote, Reserve(quote, Plan(quote)));
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateRollReceipt(
            started,
            started.StartDigest,
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            started.EligibleCompletionUtc,
            [5, 6, 2, 1, 3, 4],
            out CharacterSr5HealingRollReceipt roll));
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateCompletionQuote(
            quote, started, roll, out CharacterSr5HealingCompletionQuote completion));
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateCompletionCommand(
            quote,
            started,
            completion,
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            quote.QuoteDigest,
            started.StartDigest,
            completion.CompletionQuoteDigest,
            true,
            out CharacterSr5HealingCompletionCommand command));
        return (completion, command);
    }

    private static CharacterSr5HealingAuthorityInput Input(CharacterSr5HealingTrack track)
        => new(
            new CharacterWorkspaceId("sr5-healing-tests"),
            WorkspaceRevision: 41,
            CalendarRevision: 7,
            ActivityId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Created: true,
            CharacterSr5DowntimeHealingRules.RulesetId,
            track,
            DamageBoxes: 5,
            Body: 3,
            Willpower: 4,
            DicePoolModifiers: Array.Empty<CharacterSr5HealingDicePoolModifier>(),
            EarliestStartUtc: Start,
            RuntimeFingerprint: Hex('a'),
            SourceDigest: Hex('b'),
            ContentDigest: Hex('c'),
            CalendarDigest: Hex('d'));

    private static CharacterSr5HealingQuote Quote(CharacterSr5HealingAuthorityInput input)
    {
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateQuote(input, out var quote));
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.IsCoherent(quote));
        return quote;
    }

    private static CharacterSr5HealingPlan Plan(CharacterSr5HealingQuote quote)
    {
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreatePlan(
            quote,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Start,
            out var plan));
        return plan;
    }

    private static CharacterSr5HealingReservation Reserve(
        CharacterSr5HealingQuote quote,
        CharacterSr5HealingPlan plan)
    {
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryReserve(
            quote,
            plan,
            quote.QuoteDigest,
            plan.PlanDigest,
            quote.WorkspaceRevision,
            quote.CalendarRevision,
            Start.AddMinutes(-5),
            explicitlyConfirmed: true,
            out var reservation));
        return reservation;
    }

    private static CharacterSr5HealingStartedInterval StartInterval(
        CharacterSr5HealingQuote quote,
        CharacterSr5HealingReservation reservation)
    {
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryStart(
            quote,
            reservation,
            reservation.ReservationDigest,
            quote.WorkspaceRevision,
            quote.CalendarRevision,
            reservation.Plan.StartsAtUtc,
            explicitlyConfirmed: true,
            out var started));
        return started;
    }

    private static string Hex(char value) => new(value, 64);
}
