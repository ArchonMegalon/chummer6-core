using System.Text.Json.Serialization;

namespace Chummer.Contracts.Session;

// Incident identity is the failed Crash Test event, not an actor label. Occupant indexes are
// fixed seats/table roster entries for that incident, including the onboard driver.
public sealed record SessionPlayCollisionInput(int Armor, int OccupantCount, bool AllOccupantsIncludingDriver,
    string RosterDecision, int? Hits);
public sealed record SessionPlayPassengerInput(int Index, string Actor, int Body, int Armor,
    int Charisma, int Willpower, SessionPlayFollowupCondition Condition, bool OrdinaryNoGlitch,
    string? OwnedCharacterId, string? OwnedOwner, int? Hits)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool IsOnboardDriver { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int PhysicalOverflow { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int OverflowModifier { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Stabilized { get; init; }
    // Current transition version retains complete excess Stun pairs across applications.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int ExcessStunRemainder { get; init; }
}
public sealed record SessionPlayComposureInput(int Index, int? Hits)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayCrashRecoveryInput? Recovery { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayCrashComposureRuling? IncapacityRuling { get; init; }
}
public enum SessionPlayMovementDecision { RegainControl, AutopilotTakesOver, ContinueUncontrolled, Stopped, Collision }
public sealed record SessionPlayMovementInput(int Turn, SessionPlayMovementDecision Decision,
    string Heading, decimal SpeedMetersPerTurn, bool AcceleratorLocked, int PilotRating, bool PilotEnabled,
    string TableDecision, SessionPlayVehicleTestInput? Test)
{
    // Null only for historical movement. Versioned independently of source-bound vehicle policy.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayCrashMovementTiming? Timing { get; init; }
}
public enum SessionPlayCrashTimeBasis { UncontrolledIncident, Collision, DriverComposure }
// Completed Combat Turns from the named consequence, never the movement/action sequence.
// The GM supplies elapsed time; Core binds the anchor to retained incident/history.
public sealed record SessionPlayCrashMovementTiming(int Version, SessionPlayCrashTimeBasis Basis,
    string AnchorId, int ElapsedCombatTurns, string Adjudicator, string Evidence);
public sealed record SessionPlayOwnedCrashDamageInput(int Index, string CharacterId, string Owner,
    int ExpectedPhysical, int ExpectedStun, bool ExplicitlyApply)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int ExpectedOverflow { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool ReviewRebase { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayCrashDamageRebase? Rebase { get; init; }
}
public sealed record SessionPlayPassengerOutcome(SessionPlayPassengerInput Input, int ResistancePool,
    int Damage, string Track, int? ComposurePool, int? ComposureHits, int? Penalty, int? PenaltyTurns,
    bool AppliedToOwnedRunner)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayCrashDamageTransition? DamageTransition { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ResistanceEventId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayCrashDamageRebase? AppliedRebase { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayCrashCondition? CurrentCondition { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayCrashRecoveryOutcome? Recovery { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public SessionPlayCrashComposureState ComposureState { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayCrashComposureRuling? IncapacityRuling { get; init; }
}
public sealed record SessionPlayCrashIncident(string IncidentId, string OriginalCrashTestEventId,
    string Vehicle, SessionPlayVehicleTestInput OriginalTest, bool Collided, int CollisionNumber,
    bool VehicleResistanceRecorded, int? VehicleArmor, int? VehicleDamage, int? VehicleDamageAfter,
    int? OccupantCount, IReadOnlyList<SessionPlayPassengerOutcome> Occupants,
    int MovementTurn, bool MovementResolved, decimal SpeedMetersPerTurn, string Heading)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ParentIncidentId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int TransitionVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? PriorVehicleDamage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayCrashMovementTiming? MovementTiming { get; init; }
    // Present only on the event that performed an explicit owned write. The journal
    // history is the cross-incident overflow ledger; ordinary table actions never copy it.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayCrashOwnedWrite? OwnedConditionWrite { get; init; }
}

public enum SessionPlayCrashHealth { Conscious, Unconscious, Dying, Dead }
public enum SessionPlayCrashComposureState { Pending, DeferredIncapacitated, Rolled, NotApplicableDead, AdjudicatedElapsed }
public enum SessionPlayCrashRecoveryKind { Stabilization, Healing, UnattendedInterval }
public sealed record SessionPlayCrashCondition(int PhysicalMaximum, int StunMaximum, int Physical,
    int Stun, int Overflow, int Body, int OverflowLimit, bool Stabilized, SessionPlayCrashHealth Health)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int ExcessStunRemainder { get; init; }
}
public sealed record SessionPlayCrashDamageTransition(SessionPlayCrashCondition Before, SessionPlayCrashCondition After,
    int Damage, string Track, int StunConvertedToPhysical, int UnpairedExcessStun);
// A receipt of an already adjudicated table treatment, not a fabricated healing roll.
// Owned writes require a second explicit choice and the ordinary confirmation/journal path.
public sealed record SessionPlayCrashRecoveryInput(SessionPlayCrashRecoveryKind Kind,
    SessionPlayFollowupCondition Condition, int PhysicalOverflow, bool Stabilized,
    string Adjudicator, string Evidence, int ElapsedMinutes, bool ExplicitlyApplyOwned,
    int ExpectedPhysical, int ExpectedStun, int ExpectedOverflow)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int ExcessStunRemainder { get; init; }
}
public sealed record SessionPlayCrashRecoveryOutcome(SessionPlayCrashRecoveryInput Input,
    SessionPlayCrashCondition Before, SessionPlayCrashCondition After);
public sealed record SessionPlayCrashOwnedWrite(string CharacterId, string Owner,
    SessionPlayCrashCondition Before, SessionPlayCrashCondition After, string EventId);

// SR5 requires crash Composure but does not specify its timing during unconsciousness.
// Keep the GM's timing adjudication separate from actual dice and medical outcomes.
public enum SessionPlayCrashComposureTiming { TestOnRecovery, IntervalAlreadyElapsed }
public sealed record SessionPlayCrashComposureRuling(SessionPlayCrashComposureTiming Timing,
    int ElapsedCombatTurns, string Adjudicator, string Evidence);

// Keeps the original resistance receipt and damage immutable. The reviewed replacement is
// bound to the previous transition and the complete current owned condition, including carry.
public sealed record SessionPlayCrashDamageRebase(string ResistanceEventId, string PreviousTransitionDigest,
    SessionPlayCrashDamageTransition Replacement);
