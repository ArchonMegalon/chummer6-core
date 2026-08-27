using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public enum CharacterSr5HealingWorkspaceOutcome
{
    Available,
    Reserved,
    Started,
    Applied,
    Replayed,
    NotFound,
    Conflict,
    IdempotencyConflict,
    Missing,
    Corrupt,
    Indeterminate,
    Unavailable
}

public sealed record CharacterSr5HealingWorkspaceReadRequest(
    CharacterWorkspaceId WorkspaceId,
    CharacterSr5HealingTrack Track,
    Guid ActivityId,
    DateTimeOffset EarliestStartUtc);

public sealed record CharacterSr5HealingWorkspaceReadResult(
    CharacterSr5HealingWorkspaceOutcome Outcome,
    long CurrentWorkspaceRevision = 0,
    long CurrentCalendarRevision = 0,
    CharacterSr5HealingAuthorityInput? Input = null,
    string? Error = null);

public sealed record CharacterSr5HealingWorkspaceReserveRequest(
    CharacterSr5HealingQuote Quote,
    CharacterSr5HealingPlan Plan,
    string ExpectedQuoteDigest,
    string ExpectedPlanDigest,
    DateTimeOffset ReservedAtUtc,
    bool ExplicitlyConfirmed);

public sealed record CharacterSr5HealingWorkspaceReserveResult(
    CharacterSr5HealingWorkspaceOutcome Outcome,
    long CurrentWorkspaceRevision = 0,
    long CurrentCalendarRevision = 0,
    CharacterSr5HealingReservation? Reservation = null,
    string? Error = null);

public sealed record CharacterSr5HealingWorkspaceStartRequest(
    CharacterSr5HealingQuote Quote,
    CharacterSr5HealingReservation Reservation,
    string ExpectedReservationDigest,
    DateTimeOffset StartedAtUtc,
    bool ExplicitlyConfirmed);

public sealed record CharacterSr5HealingWorkspaceStartResult(
    CharacterSr5HealingWorkspaceOutcome Outcome,
    long CurrentWorkspaceRevision = 0,
    long CurrentCalendarRevision = 0,
    CharacterSr5HealingStartedInterval? Started = null,
    string? Error = null);

public sealed record CharacterSr5HealingWorkspaceCompletionCommitRequest(
    CharacterSr5HealingQuote Quote,
    CharacterSr5HealingReservation Reservation,
    CharacterSr5HealingStartedInterval Started,
    CharacterSr5HealingRollReceipt Roll,
    CharacterSr5HealingCompletionQuote CompletionQuote,
    CharacterSr5HealingCompletionCommand Command);

public sealed record CharacterSr5HealingWorkspaceCancellationCommitRequest(
    CharacterSr5HealingQuote Quote,
    CharacterSr5HealingReservation? Reservation,
    CharacterSr5HealingStartedInterval? Started,
    CharacterSr5HealingCancellationQuote CancellationQuote,
    CharacterSr5HealingCancellationCommand Command);

public sealed record CharacterSr5HealingWorkspaceLookupResult(
    CharacterSr5HealingWorkspaceOutcome Outcome,
    long CurrentWorkspaceRevision = 0,
    long CurrentCalendarRevision = 0,
    string ExistingIdempotencyKey = "",
    string ExistingCommandDigest = "",
    CharacterSr5HealingActivityLedgerEntry? Entry = null,
    string? Error = null);

public sealed record CharacterSr5HealingWorkspaceCommitResult(
    CharacterSr5HealingWorkspaceOutcome Outcome,
    long CurrentWorkspaceRevision = 0,
    long CurrentCalendarRevision = 0,
    string ExistingIdempotencyKey = "",
    string ExistingCommandDigest = "",
    CharacterSr5HealingActivityLedgerEntry? Entry = null,
    string? Error = null);

/// <summary>
/// Exact saved-SR5 projection and persistence seam for one natural-healing interval.
/// Read/Reserve/Start never mutate. Completion and cancellation append the complete
/// activity record, update the character when applicable, advance the healing-calendar
/// revision and checkpoint through one workspace/auxiliary-state CAS. Lookup must be
/// usable after process restart and must run before stale-revision rejection.
/// </summary>
public interface ICharacterSr5DowntimeHealingWorkspace
{
    CharacterSr5HealingWorkspaceReadResult Read(
        CharacterSr5HealingWorkspaceReadRequest request);

    CharacterSr5HealingWorkspaceReserveResult Reserve(
        CharacterSr5HealingWorkspaceReserveRequest request);

    CharacterSr5HealingWorkspaceStartResult Start(
        CharacterSr5HealingWorkspaceStartRequest request);

    CharacterSr5HealingWorkspaceLookupResult Lookup(
        CharacterWorkspaceId workspaceId,
        Guid transactionId,
        string idempotencyKey,
        string commandDigest);

    CharacterSr5HealingWorkspaceCommitResult CommitCompletion(
        CharacterSr5HealingWorkspaceCompletionCommitRequest request);

    CharacterSr5HealingWorkspaceCommitResult CommitCancellation(
        CharacterSr5HealingWorkspaceCancellationCommitRequest request);
}
