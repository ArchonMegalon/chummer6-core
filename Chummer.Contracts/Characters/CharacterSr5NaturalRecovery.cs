using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum Sr5RecoveryAction { Reserve, Start, Roll, Complete, Cancel }
public enum Sr5RecoveryPhase { Reserved, Started, RollUnresolved, Rolled, Completed, Cancelled }
public enum Sr5RecoveryOutcome { Available, Applied, Recovered, NotFound, Conflict, Unavailable, Unknown }

// UTC offsets here are a canonical encoding of campaign time, never the phone clock.
public sealed record Sr5RecoveryRequest(CharacterWorkspaceId WorkspaceId, Guid OperationId,
    Guid ActivityId, Sr5RecoveryAction Action, CharacterSr5HealingTrack Track,
    DateTimeOffset StartsAtUtc, DateTimeOffset GameTimeUtc);

public sealed record Sr5RecoveryCommand(Sr5RecoveryRequest Request, string OwnerId,
    long WorkspaceRevision, string AuxiliaryDigest, string LedgerDigest,
    string QuoteDigest, string ActivityDigest, bool ExplicitlyConfirmed, string ReviewDigest = "")
{
    // Per-review owner/custody observation, not the activity's stable rule identity.
    // Omit null so existing serialized command/operation digests remain unchanged.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CurrentEnvironmentDigest { get; init; }
}

public sealed record Sr5RecoveryActivity(CharacterSr5HealingQuote Quote,
    CharacterSr5HealingReservation Reservation, Sr5RecoveryPhase Phase,
    string OtherAuxiliaryDigest, CharacterSr5HealingStartedInterval? Started = null,
    CharacterSr5HealingRollReceipt? Roll = null,
    CharacterSr5HealingCompletionQuote? Completion = null,
    CharacterSr5HealingCompletionReceipt? Completed = null,
    CharacterSr5HealingCancellationQuote? Cancellation = null,
    CharacterSr5HealingCancellationReceipt? Cancelled = null);

// An unresolved Roll entry is written and flushed BEFORE drawing dice. It is a
// permanent no-replay fence if the process dies before the receipt is published.
public sealed record Sr5RecoveryOperation(Sr5RecoveryCommand Command, string CommandDigest,
    Sr5RecoveryActivity Activity, long RecordedRevision, string PayloadDigest,
    string PreviousDigest, string Digest);

public sealed record Sr5RecoveryLedger(IReadOnlyList<Sr5RecoveryOperation> Operations);
public sealed record Sr5RecoveryTrack(CharacterSr5HealingTrack Track, int SavedDamage, int Capacity,
    CharacterSr5HealingQuote Quote);
public sealed record Sr5RecoveryCalendarWindow(Guid WeekId, DateTimeOffset Start, DateTimeOffset End);
public sealed record Sr5RecoveryScreen(CharacterWorkspaceId WorkspaceId, long WorkspaceRevision,
    string AuxiliaryDigest, string LedgerDigest, IReadOnlyList<Sr5RecoveryTrack> Tracks,
    IReadOnlyList<Sr5RecoveryCalendarWindow> Calendar, IReadOnlyList<Sr5RecoveryActivity> Activities,
    string? UnavailableReason = null);
public sealed record Sr5RecoveryReview(Sr5RecoveryCommand Command, Sr5RecoveryActivity Activity,
    CharacterSr5HealingCompletionQuote? Completion, CharacterSr5HealingCancellationQuote? Cancellation);
public sealed record Sr5RecoveryResult(Sr5RecoveryOutcome Outcome, Sr5RecoveryOperation? Operation = null,
    Sr5RecoveryReview? Review = null, string? Reason = null);
