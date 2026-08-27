using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterSr5HealingTrack
{
    Stun,
    Physical
}

public enum CharacterSr5HealingPrerequisite
{
    CareerCharacter,
    Sr5Ruleset,
    ExactAuthority,
    DamagePresent,
    PositiveDicePool
}

public enum CharacterSr5HealingBlocker
{
    None,
    NotCareerCharacter,
    UnsupportedRuleset,
    NoDamage,
    NoDicePool,
    GlitchRequiresResolution
}

public enum CharacterSr5HealingCancellationKind
{
    CancelReservation,
    InterruptStartedInterval
}

public enum CharacterSr5HealingOutcome
{
    Applied,
    NotApplied,
    Pending,
    Conflict
}

public sealed record CharacterSr5HealingPrerequisiteResult(
    CharacterSr5HealingPrerequisite Prerequisite,
    bool Satisfied,
    string Authority);

public sealed record CharacterSr5HealingSourceAnchor(
    string SourceId,
    string RuleId,
    string SourceDigest);

public sealed record CharacterSr5HealingDicePoolModifier(
    string ModifierId,
    int Value,
    string SourceDigest);

public sealed record CharacterSr5HealingAuthorityInput(
    CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision,
    long CalendarRevision,
    Guid ActivityId,
    bool Created,
    string RulesetId,
    CharacterSr5HealingTrack Track,
    int DamageBoxes,
    int Body,
    int Willpower,
    IReadOnlyList<CharacterSr5HealingDicePoolModifier> DicePoolModifiers,
    DateTimeOffset EarliestStartUtc,
    string RuntimeFingerprint,
    string SourceDigest,
    string ContentDigest,
    string CalendarDigest);

public sealed record CharacterSr5HealingQuote(
    CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision,
    long CalendarRevision,
    Guid ActivityId,
    CharacterSr5HealingTrack Track,
    int DamageBoxesBefore,
    int Body,
    int Willpower,
    int BaseDicePool,
    IReadOnlyList<CharacterSr5HealingDicePoolModifier> DicePoolModifiers,
    int DicePool,
    int NuyenCost,
    TimeSpan Interval,
    DateTimeOffset EarliestStartUtc,
    string RuntimeFingerprint,
    string SourceDigest,
    string ContentDigest,
    string CalendarDigest,
    CharacterSr5HealingSourceAnchor SourceAnchor,
    IReadOnlyList<CharacterSr5HealingPrerequisiteResult> Prerequisites,
    bool CanPlan,
    CharacterSr5HealingBlocker Blocker,
    string QuoteDigest);

public sealed record CharacterSr5HealingPlan(
    Guid ReservationId,
    Guid ActivityId,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedWorkspaceRevision,
    long ExpectedCalendarRevision,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset CompletesAtUtc,
    string ExpectedQuoteDigest,
    string PlanDigest);

public sealed record CharacterSr5HealingReservation(
    int Version,
    CharacterSr5HealingPlan Plan,
    DateTimeOffset ReservedAtUtc,
    string IdempotencyKey,
    string ReservationDigest);

public sealed record CharacterSr5HealingStartedInterval(
    int Version,
    CharacterSr5HealingReservation Reservation,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EligibleCompletionUtc,
    string StartDigest);

public sealed record CharacterSr5HealingRollReceipt(
    Guid RollId,
    Guid ActivityId,
    int DicePool,
    IReadOnlyList<int> Dice,
    int Hits,
    int Ones,
    bool Glitch,
    bool CriticalGlitch,
    DateTimeOffset CompletedAtUtc,
    string ExpectedStartDigest,
    string RollDigest);

public sealed record CharacterSr5HealingCompletionQuote(
    Guid ActivityId,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedWorkspaceRevision,
    long ExpectedCalendarRevision,
    CharacterSr5HealingTrack Track,
    int DamageBoxesBefore,
    int BoxesHealed,
    int DamageBoxesAfter,
    int NuyenCost,
    DateTimeOffset CompletedAtUtc,
    string ExpectedQuoteDigest,
    string ExpectedStartDigest,
    string RollDigest,
    bool CanComplete,
    CharacterSr5HealingBlocker Blocker,
    string CompletionQuoteDigest);

public sealed record CharacterSr5HealingCompletionCommand(
    Guid TransactionId,
    Guid ActivityId,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedWorkspaceRevision,
    long ExpectedCalendarRevision,
    int SavedDamageBoxes,
    string ExpectedQuoteDigest,
    string ExpectedStartDigest,
    string ExpectedCompletionQuoteDigest,
    string IdempotencyKey,
    bool ExplicitlyConfirmed,
    string CommandDigest);

public sealed record CharacterSr5HealingCompletionReceipt(
    Guid TransactionId,
    Guid ActivityId,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedWorkspaceRevision,
    long AppliedWorkspaceRevision,
    long ExpectedCalendarRevision,
    long AppliedCalendarRevision,
    CharacterSr5HealingTrack Track,
    int DamageBoxesBefore,
    int DamageBoxesAfter,
    int BoxesHealed,
    int NuyenCost,
    string QuoteDigest,
    string StartDigest,
    string CompletionQuoteDigest,
    string CommandDigest,
    string ReceiptDigest);

public sealed record CharacterSr5HealingCancellationQuote(
    CharacterSr5HealingCancellationKind Kind,
    Guid ActivityId,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedWorkspaceRevision,
    long ExpectedCalendarRevision,
    int DamageBoxesBefore,
    int DamageBoxesAfter,
    int RefundNuyen,
    int RetainedNuyen,
    TimeSpan Elapsed,
    DateTimeOffset RequestedAtUtc,
    string ExpectedQuoteDigest,
    string SubjectDigest,
    string CancellationQuoteDigest);

public sealed record CharacterSr5HealingCancellationCommand(
    Guid TransactionId,
    Guid ActivityId,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedWorkspaceRevision,
    long ExpectedCalendarRevision,
    int SavedDamageBoxes,
    string ExpectedCancellationQuoteDigest,
    string SubjectDigest,
    string IdempotencyKey,
    bool ExplicitlyConfirmed,
    string CommandDigest);

public sealed record CharacterSr5HealingCancellationReceipt(
    Guid TransactionId,
    Guid ActivityId,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedWorkspaceRevision,
    long AppliedWorkspaceRevision,
    long ExpectedCalendarRevision,
    long AppliedCalendarRevision,
    CharacterSr5HealingCancellationKind Kind,
    int DamageBoxes,
    int RefundNuyen,
    int RetainedNuyen,
    TimeSpan Elapsed,
    string CancellationQuoteDigest,
    string CommandDigest,
    string ReceiptDigest);

/// <summary>
/// Deterministic SR5 natural-recovery authority for one damage-track interval. It deliberately
/// excludes First Aid, Medicine, magical healing, Edge and caller-supplied recovery rates. Core
/// derives the dice pool and interval, binds every lifecycle transition to the exact runner,
/// calendar and content authority, and never treats elapsed time as a successful heal.
/// </summary>
public static class CharacterSr5DowntimeHealingRules
{
    public const string RulesetId = "sr5";
    public const string SourceId = "sr5-core";
    public const string StunRuleId = "healing.natural.stun";
    public const string PhysicalRuleId = "healing.natural.physical";
    public const int MaximumDamageBoxes = 1_000;
    public const int MaximumAttribute = 1_000;
    public const int MaximumDicePool = 3_000;
    public const int MaximumWorkspaceIdLength = 200;
    public const int DigestLength = 64;
    public const int ReservationVersion = 1;
    public const int StartedVersion = 2;

    public static bool TryCreateQuote(
        CharacterSr5HealingAuthorityInput? input,
        out CharacterSr5HealingQuote quote)
    {
        quote = null!;
        if (!IsValidInput(input))
            return false;

        CharacterSr5HealingAuthorityInput value = input!;
        int baseDicePool;
        int dicePool;
        CharacterSr5HealingDicePoolModifier[] modifiers = value.DicePoolModifiers
            .OrderBy(static modifier => modifier.ModifierId, StringComparer.Ordinal)
            .ToArray();
        try
        {
            baseDicePool = checked(value.Track == CharacterSr5HealingTrack.Stun
                    ? value.Body + value.Willpower
                    : value.Body * 2);
            dicePool = checked(baseDicePool
                + modifiers.Sum(static modifier => modifier.Value));
        }
        catch (OverflowException)
        {
            return false;
        }

        bool exactAuthority = IsDigest(value.RuntimeFingerprint)
            && IsDigest(value.SourceDigest)
            && IsDigest(value.ContentDigest)
            && IsDigest(value.CalendarDigest);
        CharacterSr5HealingPrerequisiteResult[] prerequisites =
        [
            new(CharacterSr5HealingPrerequisite.CareerCharacter, value.Created,
                "character.created"),
            new(CharacterSr5HealingPrerequisite.Sr5Ruleset,
                string.Equals(value.RulesetId, RulesetId, StringComparison.Ordinal),
                "ruleset.sr5"),
            new(CharacterSr5HealingPrerequisite.ExactAuthority, exactAuthority,
                "runtime/source/content/calendar"),
            new(CharacterSr5HealingPrerequisite.DamagePresent, value.DamageBoxes > 0,
                value.Track == CharacterSr5HealingTrack.Stun
                    ? "damage.stun" : "damage.physical"),
            new(CharacterSr5HealingPrerequisite.PositiveDicePool, dicePool > 0,
                value.Track == CharacterSr5HealingTrack.Stun
                    ? "attribute.body+willpower" : "attribute.body*2")
        ];
        CharacterSr5HealingBlocker blocker = !value.Created
            ? CharacterSr5HealingBlocker.NotCareerCharacter
            : !string.Equals(value.RulesetId, RulesetId, StringComparison.Ordinal)
                ? CharacterSr5HealingBlocker.UnsupportedRuleset
                : value.DamageBoxes == 0
                    ? CharacterSr5HealingBlocker.NoDamage
                    : dicePool <= 0
                        ? CharacterSr5HealingBlocker.NoDicePool
                        : CharacterSr5HealingBlocker.None;
        TimeSpan interval = value.Track == CharacterSr5HealingTrack.Stun
            ? TimeSpan.FromHours(1)
            : TimeSpan.FromDays(1);
        CharacterSr5HealingSourceAnchor anchor = new(
            SourceId,
            value.Track == CharacterSr5HealingTrack.Stun ? StunRuleId : PhysicalRuleId,
            value.SourceDigest);
        var unsigned = new CharacterSr5HealingQuote(
            value.WorkspaceId,
            value.WorkspaceRevision,
            value.CalendarRevision,
            value.ActivityId,
            value.Track,
            value.DamageBoxes,
            value.Body,
            value.Willpower,
            baseDicePool,
            modifiers,
            Math.Max(0, dicePool),
            NuyenCost: 0,
            interval,
            value.EarliestStartUtc,
            value.RuntimeFingerprint,
            value.SourceDigest,
            value.ContentDigest,
            value.CalendarDigest,
            anchor,
            prerequisites,
            blocker == CharacterSr5HealingBlocker.None,
            blocker,
            QuoteDigest: string.Empty);
        quote = unsigned with { QuoteDigest = ComputeQuoteDigest(unsigned) };
        return IsCoherent(quote);
    }

    public static bool TryCreatePlan(
        CharacterSr5HealingQuote? quote,
        Guid reservationId,
        DateTimeOffset startsAtUtc,
        out CharacterSr5HealingPlan plan)
    {
        plan = null!;
        if (!IsCoherent(quote)
            || !quote!.CanPlan
            || reservationId == Guid.Empty
            || reservationId == quote.ActivityId
            || !IsUtc(startsAtUtc)
            || startsAtUtc < quote.EarliestStartUtc)
        {
            return false;
        }
        DateTimeOffset completesAtUtc;
        try
        {
            completesAtUtc = startsAtUtc.Add(quote.Interval);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        var unsigned = new CharacterSr5HealingPlan(
            reservationId,
            quote.ActivityId,
            quote.WorkspaceId,
            quote.WorkspaceRevision,
            quote.CalendarRevision,
            startsAtUtc,
            completesAtUtc,
            quote.QuoteDigest,
            PlanDigest: string.Empty);
        plan = unsigned with { PlanDigest = ComputePlanDigest(unsigned) };
        return IsCoherent(plan, quote);
    }

    public static bool TryReserve(
        CharacterSr5HealingQuote? quote,
        CharacterSr5HealingPlan? plan,
        string? expectedQuoteDigest,
        string? expectedPlanDigest,
        long observedWorkspaceRevision,
        long observedCalendarRevision,
        DateTimeOffset reservedAtUtc,
        bool explicitlyConfirmed,
        out CharacterSr5HealingReservation reservation)
    {
        reservation = null!;
        if (!explicitlyConfirmed
            || !IsCoherent(quote)
            || !IsCoherent(plan, quote)
            || !FixedEquals(quote!.QuoteDigest, expectedQuoteDigest)
            || !FixedEquals(plan!.PlanDigest, expectedPlanDigest)
            || observedWorkspaceRevision != quote.WorkspaceRevision
            || observedCalendarRevision != quote.CalendarRevision
            || !IsUtc(reservedAtUtc)
            || reservedAtUtc > plan.StartsAtUtc)
        {
            return false;
        }
        string idempotencyKey = ComputeReservationIdempotencyKey(plan);
        var unsigned = new CharacterSr5HealingReservation(
            ReservationVersion,
            plan,
            reservedAtUtc,
            idempotencyKey,
            ReservationDigest: string.Empty);
        reservation = unsigned with
        {
            ReservationDigest = ComputeReservationDigest(unsigned)
        };
        return IsCoherent(reservation, quote);
    }

    public static bool TryStart(
        CharacterSr5HealingQuote? currentQuote,
        CharacterSr5HealingReservation? reservation,
        string? expectedReservationDigest,
        long observedWorkspaceRevision,
        long observedCalendarRevision,
        DateTimeOffset startedAtUtc,
        bool explicitlyConfirmed,
        out CharacterSr5HealingStartedInterval started)
    {
        started = null!;
        if (!explicitlyConfirmed
            || !IsCoherent(currentQuote)
            || !IsCoherent(reservation, currentQuote)
            || !FixedEquals(reservation!.ReservationDigest, expectedReservationDigest)
            || observedWorkspaceRevision != currentQuote!.WorkspaceRevision
            || observedCalendarRevision != currentQuote.CalendarRevision
            || !IsUtc(startedAtUtc)
            || startedAtUtc != reservation.Plan.StartsAtUtc)
        {
            return false;
        }
        var unsigned = new CharacterSr5HealingStartedInterval(
            StartedVersion,
            reservation,
            startedAtUtc,
            reservation.Plan.CompletesAtUtc,
            StartDigest: string.Empty);
        started = unsigned with { StartDigest = ComputeStartDigest(unsigned) };
        return IsCoherent(started, currentQuote);
    }

    public static bool TryCreateRollReceipt(
        CharacterSr5HealingStartedInterval? started,
        string? expectedStartDigest,
        Guid rollId,
        DateTimeOffset completedAtUtc,
        IReadOnlyList<int>? dice,
        out CharacterSr5HealingRollReceipt receipt)
    {
        receipt = null!;
        if (!IsCoherent(started)
            || !FixedEquals(started!.StartDigest, expectedStartDigest)
            || rollId == Guid.Empty
            || rollId == started.Reservation.Plan.ActivityId
            || !IsUtc(completedAtUtc)
            || completedAtUtc < started.EligibleCompletionUtc
            || dice is null
            || dice.Count <= 0
            || dice.Count > MaximumDicePool
            || dice.Any(static die => die is < 1 or > 6))
        {
            return false;
        }
        int dicePool = dice.Count;
        int hits = dice.Count(static die => die >= 5);
        int ones = dice.Count(static die => die == 1);
        bool glitch = ones * 2 > dicePool;
        int[] stableDice = dice.ToArray();
        var unsigned = new CharacterSr5HealingRollReceipt(
            rollId,
            started.Reservation.Plan.ActivityId,
            dicePool,
            stableDice,
            hits,
            ones,
            glitch,
            glitch && hits == 0,
            completedAtUtc,
            started.StartDigest,
            RollDigest: string.Empty);
        receipt = unsigned with { RollDigest = ComputeRollDigest(unsigned) };
        return IsCoherent(receipt, started);
    }

    public static bool TryCreateCompletionQuote(
        CharacterSr5HealingQuote? currentQuote,
        CharacterSr5HealingStartedInterval? started,
        CharacterSr5HealingRollReceipt? roll,
        out CharacterSr5HealingCompletionQuote completion)
    {
        completion = null!;
        if (!IsCoherent(currentQuote)
            || !IsCoherent(started, currentQuote)
            || !IsCoherent(roll, started)
            || roll!.DicePool != currentQuote!.DicePool)
        {
            return false;
        }
        int boxesHealed = Math.Min(currentQuote.DamageBoxesBefore, roll.Hits);
        CharacterSr5HealingBlocker blocker = roll.Glitch
            ? CharacterSr5HealingBlocker.GlitchRequiresResolution
            : CharacterSr5HealingBlocker.None;
        var unsigned = new CharacterSr5HealingCompletionQuote(
            currentQuote.ActivityId,
            currentQuote.WorkspaceId,
            currentQuote.WorkspaceRevision,
            currentQuote.CalendarRevision,
            currentQuote.Track,
            currentQuote.DamageBoxesBefore,
            boxesHealed,
            currentQuote.DamageBoxesBefore - boxesHealed,
            currentQuote.NuyenCost,
            roll.CompletedAtUtc,
            currentQuote.QuoteDigest,
            started!.StartDigest,
            roll.RollDigest,
            blocker == CharacterSr5HealingBlocker.None,
            blocker,
            CompletionQuoteDigest: string.Empty);
        completion = unsigned with
        {
            CompletionQuoteDigest = ComputeCompletionQuoteDigest(unsigned)
        };
        return IsCoherent(completion, currentQuote, started, roll);
    }

    public static bool TryCreateCompletionCommand(
        CharacterSr5HealingQuote? quote,
        CharacterSr5HealingStartedInterval? started,
        CharacterSr5HealingCompletionQuote? completion,
        Guid transactionId,
        string? expectedQuoteDigest,
        string? expectedStartDigest,
        string? expectedCompletionQuoteDigest,
        bool explicitlyConfirmed,
        out CharacterSr5HealingCompletionCommand command)
    {
        command = null!;
        if (!explicitlyConfirmed
            || transactionId == Guid.Empty
            || !IsCoherent(completion, quote, started)
            || !completion!.CanComplete
            || !FixedEquals(completion.ExpectedQuoteDigest, expectedQuoteDigest)
            || !FixedEquals(completion.ExpectedStartDigest, expectedStartDigest)
            || !FixedEquals(completion.CompletionQuoteDigest, expectedCompletionQuoteDigest))
        {
            return false;
        }
        string idempotencyKey = ComputeCompletionIdempotencyKey(
            transactionId,
            completion.ActivityId,
            completion.CompletionQuoteDigest);
        var unsigned = new CharacterSr5HealingCompletionCommand(
            transactionId,
            completion.ActivityId,
            completion.WorkspaceId,
            completion.ExpectedWorkspaceRevision,
            completion.ExpectedCalendarRevision,
            completion.DamageBoxesAfter,
            completion.ExpectedQuoteDigest,
            completion.ExpectedStartDigest,
            completion.CompletionQuoteDigest,
            idempotencyKey,
            explicitlyConfirmed,
            CommandDigest: string.Empty);
        command = unsigned with { CommandDigest = ComputeCompletionCommandDigest(unsigned) };
        return IsCoherent(command, completion);
    }

    public static bool TryCreateCompletionReceipt(
        CharacterSr5HealingCompletionQuote? completion,
        CharacterSr5HealingCompletionCommand? command,
        long observedWorkspaceRevision,
        long observedCalendarRevision,
        int observedDamageBoxes,
        out CharacterSr5HealingCompletionReceipt receipt)
    {
        receipt = null!;
        if (!IsCoherent(command, completion)
            || observedWorkspaceRevision != command!.ExpectedWorkspaceRevision + 1
            || observedCalendarRevision != command.ExpectedCalendarRevision + 1
            || observedDamageBoxes != command.SavedDamageBoxes)
        {
            return false;
        }
        var unsigned = new CharacterSr5HealingCompletionReceipt(
            command.TransactionId,
            command.ActivityId,
            command.WorkspaceId,
            command.ExpectedWorkspaceRevision,
            observedWorkspaceRevision,
            command.ExpectedCalendarRevision,
            observedCalendarRevision,
            completion!.Track,
            completion.DamageBoxesBefore,
            observedDamageBoxes,
            completion.BoxesHealed,
            completion.NuyenCost,
            completion.ExpectedQuoteDigest,
            completion.ExpectedStartDigest,
            completion.CompletionQuoteDigest,
            command.CommandDigest,
            ReceiptDigest: string.Empty);
        receipt = unsigned with { ReceiptDigest = ComputeCompletionReceiptDigest(unsigned) };
        return IsCoherent(receipt, completion, command);
    }

    public static CharacterSr5HealingOutcome ResolveCompletionOutcome(
        CharacterSr5HealingCompletionQuote? completion,
        CharacterSr5HealingCompletionCommand? command,
        long observedWorkspaceRevision,
        long observedCalendarRevision,
        int observedDamageBoxes,
        CharacterSr5HealingCompletionReceipt? receipt)
    {
        if (!IsCoherent(command, completion))
            return CharacterSr5HealingOutcome.Conflict;
        if (IsCoherent(receipt, completion, command))
            return receipt!.AppliedWorkspaceRevision == observedWorkspaceRevision
                && receipt.AppliedCalendarRevision == observedCalendarRevision
                && receipt.DamageBoxesAfter == observedDamageBoxes
                    ? CharacterSr5HealingOutcome.Applied
                    : CharacterSr5HealingOutcome.Conflict;
        if (receipt is not null)
            return CharacterSr5HealingOutcome.Conflict;
        if (observedWorkspaceRevision == command!.ExpectedWorkspaceRevision
            && observedCalendarRevision == command.ExpectedCalendarRevision
            && observedDamageBoxes == completion!.DamageBoxesBefore)
        {
            return CharacterSr5HealingOutcome.NotApplied;
        }
        if (observedWorkspaceRevision == command.ExpectedWorkspaceRevision + 1
            && observedCalendarRevision == command.ExpectedCalendarRevision + 1
            && observedDamageBoxes == command.SavedDamageBoxes)
        {
            return CharacterSr5HealingOutcome.Pending;
        }
        return CharacterSr5HealingOutcome.Conflict;
    }

    public static bool TryCreateCancellationQuote(
        CharacterSr5HealingQuote? quote,
        CharacterSr5HealingReservation? reservation,
        CharacterSr5HealingStartedInterval? started,
        DateTimeOffset requestedAtUtc,
        out CharacterSr5HealingCancellationQuote cancellation)
    {
        cancellation = null!;
        bool reservedSubject = reservation is not null && started is null
            && IsCoherent(reservation, quote);
        bool startedSubject = reservation is null && started is not null
            && IsCoherent(started, quote);
        if (!IsCoherent(quote)
            || (!reservedSubject && !startedSubject)
            || !IsUtc(requestedAtUtc))
        {
            return false;
        }
        CharacterSr5HealingCancellationKind kind = reservedSubject
            ? CharacterSr5HealingCancellationKind.CancelReservation
            : CharacterSr5HealingCancellationKind.InterruptStartedInterval;
        string subjectDigest = reservedSubject
            ? reservation!.ReservationDigest
            : started!.StartDigest;
        DateTimeOffset lowerBound = reservedSubject
            ? reservation!.ReservedAtUtc
            : started!.StartedAtUtc;
        if (requestedAtUtc < lowerBound)
            return false;
        TimeSpan elapsed = startedSubject
            ? requestedAtUtc - started!.StartedAtUtc
            : TimeSpan.Zero;
        var unsigned = new CharacterSr5HealingCancellationQuote(
            kind,
            quote!.ActivityId,
            quote.WorkspaceId,
            quote.WorkspaceRevision,
            quote.CalendarRevision,
            quote.DamageBoxesBefore,
            quote.DamageBoxesBefore,
            RefundNuyen: 0,
            RetainedNuyen: 0,
            elapsed,
            requestedAtUtc,
            quote.QuoteDigest,
            subjectDigest,
            CancellationQuoteDigest: string.Empty);
        cancellation = unsigned with
        {
            CancellationQuoteDigest = ComputeCancellationQuoteDigest(unsigned)
        };
        return IsCoherent(cancellation, quote, reservation, started);
    }

    public static bool TryCreateCancellationCommand(
        CharacterSr5HealingCancellationQuote? cancellation,
        Guid transactionId,
        string? expectedCancellationQuoteDigest,
        string? expectedSubjectDigest,
        bool explicitlyConfirmed,
        out CharacterSr5HealingCancellationCommand command)
    {
        command = null!;
        if (!explicitlyConfirmed
            || !IsCoherent(cancellation)
            || transactionId == Guid.Empty
            || !FixedEquals(
                cancellation!.CancellationQuoteDigest,
                expectedCancellationQuoteDigest)
            || !FixedEquals(cancellation.SubjectDigest, expectedSubjectDigest))
        {
            return false;
        }
        string idempotencyKey = ComputeCancellationIdempotencyKey(
            transactionId,
            cancellation.ActivityId,
            cancellation.CancellationQuoteDigest);
        var unsigned = new CharacterSr5HealingCancellationCommand(
            transactionId,
            cancellation.ActivityId,
            cancellation.WorkspaceId,
            cancellation.ExpectedWorkspaceRevision,
            cancellation.ExpectedCalendarRevision,
            cancellation.DamageBoxesAfter,
            cancellation.CancellationQuoteDigest,
            cancellation.SubjectDigest,
            idempotencyKey,
            explicitlyConfirmed,
            CommandDigest: string.Empty);
        command = unsigned with { CommandDigest = ComputeCancellationCommandDigest(unsigned) };
        return IsCoherent(command, cancellation);
    }

    public static bool TryCreateCancellationReceipt(
        CharacterSr5HealingCancellationQuote? cancellation,
        CharacterSr5HealingCancellationCommand? command,
        long observedWorkspaceRevision,
        long observedCalendarRevision,
        int observedDamageBoxes,
        out CharacterSr5HealingCancellationReceipt receipt)
    {
        receipt = null!;
        if (!IsCoherent(command, cancellation)
            || observedWorkspaceRevision != command!.ExpectedWorkspaceRevision + 1
            || observedCalendarRevision != command.ExpectedCalendarRevision + 1
            || observedDamageBoxes != command.SavedDamageBoxes)
        {
            return false;
        }
        var unsigned = new CharacterSr5HealingCancellationReceipt(
            command.TransactionId,
            command.ActivityId,
            command.WorkspaceId,
            command.ExpectedWorkspaceRevision,
            observedWorkspaceRevision,
            command.ExpectedCalendarRevision,
            observedCalendarRevision,
            cancellation!.Kind,
            observedDamageBoxes,
            cancellation.RefundNuyen,
            cancellation.RetainedNuyen,
            cancellation.Elapsed,
            cancellation.CancellationQuoteDigest,
            command.CommandDigest,
            ReceiptDigest: string.Empty);
        receipt = unsigned with { ReceiptDigest = ComputeCancellationReceiptDigest(unsigned) };
        return IsCoherent(receipt, cancellation, command);
    }

    public static CharacterSr5HealingOutcome ResolveCancellationOutcome(
        CharacterSr5HealingCancellationQuote? cancellation,
        CharacterSr5HealingCancellationCommand? command,
        long observedWorkspaceRevision,
        long observedCalendarRevision,
        int observedDamageBoxes,
        CharacterSr5HealingCancellationReceipt? receipt)
    {
        if (!IsCoherent(command, cancellation))
            return CharacterSr5HealingOutcome.Conflict;
        if (IsCoherent(receipt, cancellation, command))
            return receipt!.AppliedWorkspaceRevision == observedWorkspaceRevision
                && receipt.AppliedCalendarRevision == observedCalendarRevision
                && receipt.DamageBoxes == observedDamageBoxes
                    ? CharacterSr5HealingOutcome.Applied
                    : CharacterSr5HealingOutcome.Conflict;
        if (receipt is not null)
            return CharacterSr5HealingOutcome.Conflict;
        if (observedWorkspaceRevision == command!.ExpectedWorkspaceRevision
            && observedCalendarRevision == command.ExpectedCalendarRevision
            && observedDamageBoxes == cancellation!.DamageBoxesBefore)
        {
            return CharacterSr5HealingOutcome.NotApplied;
        }
        if (observedWorkspaceRevision == command.ExpectedWorkspaceRevision + 1
            && observedCalendarRevision == command.ExpectedCalendarRevision + 1
            && observedDamageBoxes == command.SavedDamageBoxes)
        {
            return CharacterSr5HealingOutcome.Pending;
        }
        return CharacterSr5HealingOutcome.Conflict;
    }

    public static bool IsCoherent(CharacterSr5HealingQuote? value)
        => value is not null
            && IsWorkspace(value.WorkspaceId)
            && IsRevision(value.WorkspaceRevision)
            && IsRevision(value.CalendarRevision)
            && value.ActivityId != Guid.Empty
            && Enum.IsDefined(value.Track)
            && value.DamageBoxesBefore is >= 0 and <= MaximumDamageBoxes
            && value.Body is > 0 and <= MaximumAttribute
            && value.Willpower is > 0 and <= MaximumAttribute
            && value.BaseDicePool == (value.Track == CharacterSr5HealingTrack.Stun
                ? value.Body + value.Willpower : value.Body * 2)
            && AreValidModifiers(value.DicePoolModifiers)
            && value.DicePool is >= 0 and <= MaximumDicePool
            && value.DicePool == Math.Max(
                0,
                value.BaseDicePool + value.DicePoolModifiers.Sum(static item => item.Value))
            && value.NuyenCost == 0
            && value.Interval == (value.Track == CharacterSr5HealingTrack.Stun
                ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1))
            && IsUtc(value.EarliestStartUtc)
            && IsDigest(value.RuntimeFingerprint)
            && IsDigest(value.SourceDigest)
            && IsDigest(value.ContentDigest)
            && IsDigest(value.CalendarDigest)
            && value.SourceAnchor == new CharacterSr5HealingSourceAnchor(
                SourceId,
                value.Track == CharacterSr5HealingTrack.Stun ? StunRuleId : PhysicalRuleId,
                value.SourceDigest)
            && value.Prerequisites is { Count: 5 }
            && value.Prerequisites.All(static item => item is not null)
            && value.Prerequisites.All(static item => item.Satisfied) == value.CanPlan
            && value.CanPlan == (value.Blocker == CharacterSr5HealingBlocker.None)
            && IsDigest(value.QuoteDigest)
            && FixedEquals(value.QuoteDigest, ComputeQuoteDigest(value));

    public static bool IsCoherent(
        CharacterSr5HealingPlan? value,
        CharacterSr5HealingQuote? quote)
        => value is not null
            && IsCoherent(quote)
            && value.ReservationId != Guid.Empty
            && value.ReservationId != value.ActivityId
            && value.ActivityId == quote!.ActivityId
            && value.WorkspaceId == quote.WorkspaceId
            && value.ExpectedWorkspaceRevision == quote.WorkspaceRevision
            && value.ExpectedCalendarRevision == quote.CalendarRevision
            && value.StartsAtUtc >= quote.EarliestStartUtc
            && value.CompletesAtUtc - value.StartsAtUtc == quote.Interval
            && FixedEquals(value.ExpectedQuoteDigest, quote.QuoteDigest)
            && IsDigest(value.PlanDigest)
            && FixedEquals(value.PlanDigest, ComputePlanDigest(value));

    public static bool IsCoherent(
        CharacterSr5HealingReservation? value,
        CharacterSr5HealingQuote? quote = null)
        => quote is null
            ? IsCoherentReservationShape(value)
            : value is not null
                && value.Version == ReservationVersion
                && IsCoherent(value.Plan, quote)
                && IsUtc(value.ReservedAtUtc)
                && value.ReservedAtUtc <= value.Plan.StartsAtUtc
                && IsDigest(value.IdempotencyKey)
                && FixedEquals(
                    value.IdempotencyKey,
                    ComputeReservationIdempotencyKey(value.Plan))
                && IsDigest(value.ReservationDigest)
                && FixedEquals(value.ReservationDigest, ComputeReservationDigest(value));

    public static bool IsCoherent(
        CharacterSr5HealingStartedInterval? value,
        CharacterSr5HealingQuote? quote = null)
        => value is not null
            && value.Version == StartedVersion
            && (quote is null
                ? IsCoherentReservationShape(value.Reservation)
                : IsCoherent(value.Reservation, quote))
            && value.StartedAtUtc == value.Reservation.Plan.StartsAtUtc
            && value.EligibleCompletionUtc == value.Reservation.Plan.CompletesAtUtc
            && IsDigest(value.StartDigest)
            && FixedEquals(value.StartDigest, ComputeStartDigest(value));

    public static bool IsCoherent(
        CharacterSr5HealingRollReceipt? value,
        CharacterSr5HealingStartedInterval? started)
        => value is not null
            && IsCoherent(started)
            && value.RollId != Guid.Empty
            && value.ActivityId == started!.Reservation.Plan.ActivityId
            && value.Dice is { Count: > 0 }
            && value.DicePool == value.Dice.Count
            && value.DicePool is > 0 and <= MaximumDicePool
            && value.Dice.All(static die => die is >= 1 and <= 6)
            && value.Hits == value.Dice.Count(static die => die >= 5)
            && value.Ones == value.Dice.Count(static die => die == 1)
            && value.Glitch == (value.Ones * 2 > value.DicePool)
            && value.CriticalGlitch == (value.Glitch && value.Hits == 0)
            && IsUtc(value.CompletedAtUtc)
            && value.CompletedAtUtc >= started.EligibleCompletionUtc
            && FixedEquals(value.ExpectedStartDigest, started.StartDigest)
            && IsDigest(value.RollDigest)
            && FixedEquals(value.RollDigest, ComputeRollDigest(value));

    public static bool IsCoherent(
        CharacterSr5HealingCompletionQuote? value,
        CharacterSr5HealingQuote? quote = null,
        CharacterSr5HealingStartedInterval? started = null,
        CharacterSr5HealingRollReceipt? roll = null)
        => value is not null
            && value.ActivityId != Guid.Empty
            && IsWorkspace(value.WorkspaceId)
            && IsRevision(value.ExpectedWorkspaceRevision)
            && IsRevision(value.ExpectedCalendarRevision)
            && Enum.IsDefined(value.Track)
            && value.DamageBoxesBefore is > 0 and <= MaximumDamageBoxes
            && value.BoxesHealed >= 0
            && value.BoxesHealed <= value.DamageBoxesBefore
            && value.DamageBoxesAfter == value.DamageBoxesBefore - value.BoxesHealed
            && value.NuyenCost == 0
            && IsUtc(value.CompletedAtUtc)
            && IsDigest(value.ExpectedQuoteDigest)
            && IsDigest(value.ExpectedStartDigest)
            && IsDigest(value.RollDigest)
            && value.CanComplete == (value.Blocker == CharacterSr5HealingBlocker.None)
            && value.Blocker is CharacterSr5HealingBlocker.None
                or CharacterSr5HealingBlocker.GlitchRequiresResolution
            && IsDigest(value.CompletionQuoteDigest)
            && FixedEquals(value.CompletionQuoteDigest, ComputeCompletionQuoteDigest(value))
            && (quote is null || (IsCoherent(quote)
                && value.ActivityId == quote.ActivityId
                && value.WorkspaceId == quote.WorkspaceId
                && value.ExpectedWorkspaceRevision == quote.WorkspaceRevision
                && value.ExpectedCalendarRevision == quote.CalendarRevision
                && value.Track == quote.Track
                && value.DamageBoxesBefore == quote.DamageBoxesBefore
                && FixedEquals(value.ExpectedQuoteDigest, quote.QuoteDigest)))
            && (started is null || (IsCoherent(started, quote)
                && FixedEquals(value.ExpectedStartDigest, started.StartDigest)))
            && (roll is null || (IsCoherent(roll, started)
                && FixedEquals(value.RollDigest, roll.RollDigest)
                && value.BoxesHealed == Math.Min(value.DamageBoxesBefore, roll.Hits)
                && value.Blocker == (roll.Glitch
                    ? CharacterSr5HealingBlocker.GlitchRequiresResolution
                    : CharacterSr5HealingBlocker.None)));

    public static bool IsCoherent(
        CharacterSr5HealingCompletionCommand? value,
        CharacterSr5HealingCompletionQuote? completion)
        => value is not null
            && IsCoherent(completion)
            && value.TransactionId != Guid.Empty
            && value.ActivityId == completion!.ActivityId
            && value.WorkspaceId == completion.WorkspaceId
            && value.ExpectedWorkspaceRevision == completion.ExpectedWorkspaceRevision
            && value.ExpectedCalendarRevision == completion.ExpectedCalendarRevision
            && value.SavedDamageBoxes == completion.DamageBoxesAfter
            && FixedEquals(value.ExpectedQuoteDigest, completion.ExpectedQuoteDigest)
            && FixedEquals(value.ExpectedStartDigest, completion.ExpectedStartDigest)
            && FixedEquals(
                value.ExpectedCompletionQuoteDigest,
                completion.CompletionQuoteDigest)
            && IsDigest(value.IdempotencyKey)
            && FixedEquals(
                value.IdempotencyKey,
                ComputeCompletionIdempotencyKey(
                    value.TransactionId,
                    value.ActivityId,
                    value.ExpectedCompletionQuoteDigest))
            && value.ExplicitlyConfirmed
            && IsDigest(value.CommandDigest)
            && FixedEquals(value.CommandDigest, ComputeCompletionCommandDigest(value));

    public static bool IsCoherent(
        CharacterSr5HealingCompletionReceipt? value,
        CharacterSr5HealingCompletionQuote? completion,
        CharacterSr5HealingCompletionCommand? command)
        => value is not null
            && IsCoherent(command, completion)
            && value.TransactionId == command!.TransactionId
            && value.ActivityId == command.ActivityId
            && value.WorkspaceId == command.WorkspaceId
            && value.ExpectedWorkspaceRevision == command.ExpectedWorkspaceRevision
            && value.AppliedWorkspaceRevision == value.ExpectedWorkspaceRevision + 1
            && value.ExpectedCalendarRevision == command.ExpectedCalendarRevision
            && value.AppliedCalendarRevision == value.ExpectedCalendarRevision + 1
            && value.Track == completion!.Track
            && value.DamageBoxesBefore == completion.DamageBoxesBefore
            && value.DamageBoxesAfter == completion.DamageBoxesAfter
            && value.BoxesHealed == completion.BoxesHealed
            && value.BoxesHealed == value.DamageBoxesBefore - value.DamageBoxesAfter
            && value.NuyenCost == completion.NuyenCost
            && FixedEquals(value.QuoteDigest, completion.ExpectedQuoteDigest)
            && FixedEquals(value.StartDigest, completion.ExpectedStartDigest)
            && FixedEquals(value.CompletionQuoteDigest, completion.CompletionQuoteDigest)
            && FixedEquals(value.CommandDigest, command.CommandDigest)
            && IsDigest(value.ReceiptDigest)
            && FixedEquals(value.ReceiptDigest, ComputeCompletionReceiptDigest(value));

    public static bool IsCoherent(
        CharacterSr5HealingCancellationQuote? value,
        CharacterSr5HealingQuote? quote = null,
        CharacterSr5HealingReservation? reservation = null,
        CharacterSr5HealingStartedInterval? started = null)
        => value is not null
            && Enum.IsDefined(value.Kind)
            && value.ActivityId != Guid.Empty
            && IsWorkspace(value.WorkspaceId)
            && IsRevision(value.ExpectedWorkspaceRevision)
            && IsRevision(value.ExpectedCalendarRevision)
            && value.DamageBoxesBefore is > 0 and <= MaximumDamageBoxes
            && value.DamageBoxesAfter == value.DamageBoxesBefore
            && value.RefundNuyen == 0
            && value.RetainedNuyen == 0
            && value.Elapsed >= TimeSpan.Zero
            && (value.Kind != CharacterSr5HealingCancellationKind.CancelReservation
                || value.Elapsed == TimeSpan.Zero)
            && IsUtc(value.RequestedAtUtc)
            && IsDigest(value.ExpectedQuoteDigest)
            && IsDigest(value.SubjectDigest)
            && IsDigest(value.CancellationQuoteDigest)
            && FixedEquals(value.CancellationQuoteDigest, ComputeCancellationQuoteDigest(value))
            && (quote is null || (IsCoherent(quote)
                && value.ActivityId == quote.ActivityId
                && value.WorkspaceId == quote.WorkspaceId
                && value.ExpectedWorkspaceRevision == quote.WorkspaceRevision
                && value.ExpectedCalendarRevision == quote.CalendarRevision
                && value.DamageBoxesBefore == quote.DamageBoxesBefore
                && FixedEquals(value.ExpectedQuoteDigest, quote.QuoteDigest)))
            && (reservation is null || (started is null
                && value.Kind == CharacterSr5HealingCancellationKind.CancelReservation
                && IsCoherent(reservation, quote)
                && FixedEquals(value.SubjectDigest, reservation.ReservationDigest)))
            && (started is null || (reservation is null
                && value.Kind == CharacterSr5HealingCancellationKind.InterruptStartedInterval
                && IsCoherent(started, quote)
                && FixedEquals(value.SubjectDigest, started.StartDigest)
                && value.Elapsed == value.RequestedAtUtc - started.StartedAtUtc));

    public static bool IsCoherent(
        CharacterSr5HealingCancellationCommand? value,
        CharacterSr5HealingCancellationQuote? cancellation)
        => value is not null
            && IsCoherent(cancellation)
            && value.TransactionId != Guid.Empty
            && value.ActivityId == cancellation!.ActivityId
            && value.WorkspaceId == cancellation.WorkspaceId
            && value.ExpectedWorkspaceRevision == cancellation.ExpectedWorkspaceRevision
            && value.ExpectedCalendarRevision == cancellation.ExpectedCalendarRevision
            && value.SavedDamageBoxes == cancellation.DamageBoxesAfter
            && FixedEquals(
                value.ExpectedCancellationQuoteDigest,
                cancellation.CancellationQuoteDigest)
            && FixedEquals(value.SubjectDigest, cancellation.SubjectDigest)
            && IsDigest(value.IdempotencyKey)
            && FixedEquals(
                value.IdempotencyKey,
                ComputeCancellationIdempotencyKey(
                    value.TransactionId,
                    value.ActivityId,
                    value.ExpectedCancellationQuoteDigest))
            && value.ExplicitlyConfirmed
            && IsDigest(value.CommandDigest)
            && FixedEquals(value.CommandDigest, ComputeCancellationCommandDigest(value));

    public static bool IsCoherent(
        CharacterSr5HealingCancellationReceipt? value,
        CharacterSr5HealingCancellationQuote? cancellation,
        CharacterSr5HealingCancellationCommand? command)
        => value is not null
            && IsCoherent(command, cancellation)
            && value.TransactionId == command!.TransactionId
            && value.ActivityId == command.ActivityId
            && value.WorkspaceId == command.WorkspaceId
            && value.ExpectedWorkspaceRevision == command.ExpectedWorkspaceRevision
            && value.AppliedWorkspaceRevision == value.ExpectedWorkspaceRevision + 1
            && value.ExpectedCalendarRevision == command.ExpectedCalendarRevision
            && value.AppliedCalendarRevision == value.ExpectedCalendarRevision + 1
            && value.Kind == cancellation!.Kind
            && value.DamageBoxes == cancellation.DamageBoxesAfter
            && value.RefundNuyen == cancellation.RefundNuyen
            && value.RetainedNuyen == cancellation.RetainedNuyen
            && value.Elapsed == cancellation.Elapsed
            && FixedEquals(
                value.CancellationQuoteDigest,
                cancellation.CancellationQuoteDigest)
            && FixedEquals(value.CommandDigest, command.CommandDigest)
            && IsDigest(value.ReceiptDigest)
            && FixedEquals(value.ReceiptDigest, ComputeCancellationReceiptDigest(value));

    private static bool IsValidInput(CharacterSr5HealingAuthorityInput? value)
        => value is not null
            && IsWorkspace(value.WorkspaceId)
            && IsRevision(value.WorkspaceRevision)
            && IsRevision(value.CalendarRevision)
            && value.ActivityId != Guid.Empty
            && value.RulesetId is { Length: > 0 and <= 64 }
            && Enum.IsDefined(value.Track)
            && value.DamageBoxes is >= 0 and <= MaximumDamageBoxes
            && value.Body is > 0 and <= MaximumAttribute
            && value.Willpower is > 0 and <= MaximumAttribute
            && AreValidModifiers(value.DicePoolModifiers)
            && IsUtc(value.EarliestStartUtc)
            && IsDigest(value.RuntimeFingerprint)
            && IsDigest(value.SourceDigest)
            && IsDigest(value.ContentDigest)
            && IsDigest(value.CalendarDigest);

    private static bool IsCoherentReservationShape(CharacterSr5HealingReservation? value)
        => value is not null
            && value.Version == ReservationVersion
            && value.Plan is not null
            && value.Plan.ReservationId != Guid.Empty
            && value.Plan.ActivityId != Guid.Empty
            && IsWorkspace(value.Plan.WorkspaceId)
            && IsRevision(value.Plan.ExpectedWorkspaceRevision)
            && IsRevision(value.Plan.ExpectedCalendarRevision)
            && IsUtc(value.Plan.StartsAtUtc)
            && IsUtc(value.Plan.CompletesAtUtc)
            && value.Plan.CompletesAtUtc > value.Plan.StartsAtUtc
            && value.Plan.CompletesAtUtc - value.Plan.StartsAtUtc
                is var interval
            && (interval == TimeSpan.FromHours(1) || interval == TimeSpan.FromDays(1))
            && IsDigest(value.Plan.ExpectedQuoteDigest)
            && IsDigest(value.Plan.PlanDigest)
            && FixedEquals(value.Plan.PlanDigest, ComputePlanDigest(value.Plan))
            && IsUtc(value.ReservedAtUtc)
            && value.ReservedAtUtc <= value.Plan.StartsAtUtc
            && IsDigest(value.IdempotencyKey)
            && FixedEquals(
                value.IdempotencyKey,
                ComputeReservationIdempotencyKey(value.Plan))
            && IsDigest(value.ReservationDigest)
            && FixedEquals(value.ReservationDigest, ComputeReservationDigest(value));

    private static string ComputeQuoteDigest(CharacterSr5HealingQuote value)
        => Hash(Canonical(
            "chummer.sr5-healing.quote/v1", value.WorkspaceId.Value,
            value.WorkspaceRevision, value.CalendarRevision, value.ActivityId, value.Track,
            value.DamageBoxesBefore, value.Body, value.Willpower, value.BaseDicePool,
            string.Join('|', value.DicePoolModifiers.Select(static item =>
                $"{item.ModifierId}:{item.Value}:{item.SourceDigest}")),
            value.DicePool, value.NuyenCost, value.Interval,
            value.EarliestStartUtc, value.RuntimeFingerprint, value.SourceDigest,
            value.ContentDigest, value.CalendarDigest, value.SourceAnchor.SourceId,
            value.SourceAnchor.RuleId, value.SourceAnchor.SourceDigest,
            string.Join('|', value.Prerequisites.Select(static item =>
                $"{item.Prerequisite}:{item.Satisfied}:{item.Authority}")),
            value.CanPlan, value.Blocker));

    private static string ComputePlanDigest(CharacterSr5HealingPlan value)
        => Hash(Canonical(
            "chummer.sr5-healing.plan/v1", value.ReservationId, value.ActivityId,
            value.WorkspaceId.Value, value.ExpectedWorkspaceRevision,
            value.ExpectedCalendarRevision, value.StartsAtUtc, value.CompletesAtUtc,
            value.ExpectedQuoteDigest));

    private static string ComputeReservationDigest(CharacterSr5HealingReservation value)
        => Hash(Canonical(
            "chummer.sr5-healing.reservation/v1", value.Version, value.Plan.PlanDigest,
            value.ReservedAtUtc, value.IdempotencyKey));

    private static string ComputeReservationIdempotencyKey(CharacterSr5HealingPlan value)
        => Hash(Canonical(
            "chummer.sr5-healing.reserve-idempotency/v1",
            value.ReservationId,
            value.PlanDigest));

    private static string ComputeCompletionIdempotencyKey(
        Guid transactionId,
        Guid activityId,
        string completionQuoteDigest)
        => Hash(Canonical(
            "chummer.sr5-healing.complete-idempotency/v1",
            transactionId,
            activityId,
            completionQuoteDigest));

    private static string ComputeCancellationIdempotencyKey(
        Guid transactionId,
        Guid activityId,
        string cancellationQuoteDigest)
        => Hash(Canonical(
            "chummer.sr5-healing.cancel-idempotency/v1",
            transactionId,
            activityId,
            cancellationQuoteDigest));

    private static string ComputeStartDigest(CharacterSr5HealingStartedInterval value)
        => Hash(Canonical(
            "chummer.sr5-healing.start/v1", value.Version,
            value.Reservation.ReservationDigest, value.StartedAtUtc,
            value.EligibleCompletionUtc));

    private static string ComputeRollDigest(CharacterSr5HealingRollReceipt value)
        => Hash(Canonical(
            "chummer.sr5-healing.roll/v1", value.RollId, value.ActivityId,
            value.DicePool, string.Join(',', value.Dice), value.Hits, value.Ones,
            value.Glitch, value.CriticalGlitch, value.CompletedAtUtc,
            value.ExpectedStartDigest));

    private static string ComputeCompletionQuoteDigest(CharacterSr5HealingCompletionQuote value)
        => Hash(Canonical(
            "chummer.sr5-healing.completion-quote/v1", value.ActivityId,
            value.WorkspaceId.Value, value.ExpectedWorkspaceRevision,
            value.ExpectedCalendarRevision, value.Track, value.DamageBoxesBefore,
            value.BoxesHealed, value.DamageBoxesAfter, value.NuyenCost,
            value.CompletedAtUtc, value.ExpectedQuoteDigest, value.ExpectedStartDigest,
            value.RollDigest, value.CanComplete, value.Blocker));

    private static string ComputeCompletionCommandDigest(CharacterSr5HealingCompletionCommand value)
        => Hash(Canonical(
            "chummer.sr5-healing.completion-command/v1", value.TransactionId,
            value.ActivityId, value.WorkspaceId.Value, value.ExpectedWorkspaceRevision,
            value.ExpectedCalendarRevision, value.SavedDamageBoxes,
            value.ExpectedQuoteDigest, value.ExpectedStartDigest,
            value.ExpectedCompletionQuoteDigest, value.IdempotencyKey,
            value.ExplicitlyConfirmed));

    private static string ComputeCompletionReceiptDigest(CharacterSr5HealingCompletionReceipt value)
        => Hash(Canonical(
            "chummer.sr5-healing.completion-receipt/v1", value.TransactionId,
            value.ActivityId, value.WorkspaceId.Value, value.ExpectedWorkspaceRevision,
            value.AppliedWorkspaceRevision, value.ExpectedCalendarRevision,
            value.AppliedCalendarRevision, value.Track, value.DamageBoxesBefore,
            value.DamageBoxesAfter, value.BoxesHealed, value.NuyenCost,
            value.QuoteDigest, value.StartDigest, value.CompletionQuoteDigest,
            value.CommandDigest));

    private static string ComputeCancellationQuoteDigest(CharacterSr5HealingCancellationQuote value)
        => Hash(Canonical(
            "chummer.sr5-healing.cancellation-quote/v1", value.Kind, value.ActivityId,
            value.WorkspaceId.Value, value.ExpectedWorkspaceRevision,
            value.ExpectedCalendarRevision, value.DamageBoxesBefore,
            value.DamageBoxesAfter, value.RefundNuyen, value.RetainedNuyen,
            value.Elapsed, value.RequestedAtUtc, value.ExpectedQuoteDigest,
            value.SubjectDigest));

    private static string ComputeCancellationCommandDigest(CharacterSr5HealingCancellationCommand value)
        => Hash(Canonical(
            "chummer.sr5-healing.cancellation-command/v1", value.TransactionId,
            value.ActivityId, value.WorkspaceId.Value, value.ExpectedWorkspaceRevision,
            value.ExpectedCalendarRevision, value.SavedDamageBoxes,
            value.ExpectedCancellationQuoteDigest, value.SubjectDigest,
            value.IdempotencyKey, value.ExplicitlyConfirmed));

    private static string ComputeCancellationReceiptDigest(CharacterSr5HealingCancellationReceipt value)
        => Hash(Canonical(
            "chummer.sr5-healing.cancellation-receipt/v1", value.TransactionId,
            value.ActivityId, value.WorkspaceId.Value, value.ExpectedWorkspaceRevision,
            value.AppliedWorkspaceRevision, value.ExpectedCalendarRevision,
            value.AppliedCalendarRevision, value.Kind, value.DamageBoxes,
            value.RefundNuyen, value.RetainedNuyen, value.Elapsed,
            value.CancellationQuoteDigest, value.CommandDigest));

    private static bool IsWorkspace(CharacterWorkspaceId value)
        => !string.IsNullOrWhiteSpace(value.Value)
            && value.Value.Length <= MaximumWorkspaceIdLength;

    private static bool AreValidModifiers(
        IReadOnlyList<CharacterSr5HealingDicePoolModifier>? modifiers)
        => modifiers is not null
            && modifiers.Count <= 1_000
            && modifiers.All(static modifier => modifier is not null
                && modifier.ModifierId is { Length: > 0 and <= 200 }
                && modifier.Value is >= -MaximumDicePool and <= MaximumDicePool
                && IsDigest(modifier.SourceDigest))
            && modifiers.Select(static modifier => modifier.ModifierId)
                .Distinct(StringComparer.Ordinal).Count() == modifiers.Count
            && modifiers.SequenceEqual(
                modifiers.OrderBy(static modifier => modifier.ModifierId, StringComparer.Ordinal));

    private static bool IsRevision(long value) => value is > 0 and < long.MaxValue;

    private static bool IsUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;

    private static bool IsDigest(string? value)
        => value is { Length: DigestLength }
            && value.All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool FixedEquals(string? left, string? right)
    {
        if (!IsDigest(left) || !IsDigest(right))
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left!),
            Encoding.ASCII.GetBytes(right!));
    }

    private static string Hash(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Canonical(params object?[] values)
    {
        var builder = new StringBuilder();
        foreach (object? value in values)
        {
            string text = value switch
            {
                null => string.Empty,
                DateTimeOffset instant => instant.ToUniversalTime()
                    .ToString("O", CultureInfo.InvariantCulture),
                TimeSpan duration => duration.Ticks.ToString(CultureInfo.InvariantCulture),
                IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
            builder.Append(text.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(text).Append(';');
        }
        return builder.ToString();
    }
}
