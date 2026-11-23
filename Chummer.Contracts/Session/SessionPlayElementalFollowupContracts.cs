using System.Text.Json.Serialization;

namespace Chummer.Contracts.Session;

public enum SessionPlayElementalAction { FireTick, FireNextTurn, Extinguish, Spread, IceCrossing, IceVehicle, IceMelt, DangerousBreakage, ExistingFire, FireConsumed, FireMaterialTick, FireBarrierTick, FireVehicleTick, CollisionDamage, PassengerResistance, CrashComposure, UncontrolledMovement, ApplyOwnedCrashDamage }
public enum SessionPlayIceTemperature { Room, BelowFreezing, CoolerAboveFreezing, Hotter }
public enum SessionPlayExtinguishMethod { Water, Smother, Other }
public enum SessionPlayDangerDecision { Injury, Explosion, OtherDanger, NoAdditionalConsequence }

// Declared table actors are labels, never canonical character IDs or mutation authority.
public sealed record SessionPlayExistingFireInput(bool DeclaredSeparate, string? OriginalEffectId, string? ExpectedOriginalRevision, string GmDecision);
public sealed record SessionPlayFireConsumedInput(int Turn, bool CompletelyDestroyed, string Adjudicator, string Decision);
public sealed record SessionPlayFireTickInput(int Turn, int Body, int Armor, int FireResistance, bool OrdinaryLivingTarget, int? Hits);
public sealed record SessionPlayFollowupCondition(int PhysicalMaximum, int StunMaximum, int PhysicalDamage, int StunDamage);
public sealed record SessionPlayExtinguishInput(int Turn, string Actor, int Agility, int Intuition,
    SessionPlayExtinguishMethod Method, string MethodDetail, bool OrdinaryNoGlitch, int? Hits)
{
    public SessionPlayFollowupCondition? Condition { get; init; }
}
// Environmental fire has no attack net hits. Its ignition threshold must be adjudicated explicitly.
public sealed record SessionPlayFireSpreadInput(int Turn, bool NoNearbyFlammables, string Subject, int Armor,
    int FireResistance, int GmThreshold, string GmDecision, int? Hits);
public sealed record SessionPlayIceCrossingInput(string Actor, int Agility, int Reaction, bool OrdinaryNoGlitch, int? Hits)
{
    public SessionPlayFollowupCondition? Condition { get; init; }
}
// Table inputs identify external actors; these are never canonical owner IDs.
public enum SessionPlayPilotSkill { GroundCraft, Aircraft, Aerospace, Watercraft, Walker, Maneuvering }
public enum SessionPlayVehicleControl { Manual, OnboardAr, RemoteAr, RemoteCold, RemoteHot, JumpedCold, JumpedHot, Autonomous }
public sealed record SessionPlayVehicleTestInput(string Driver, string SkillName, SessionPlayVehicleControl Control,
    int Skill, int ReactionOrPilot, int Handling, int Body, bool Drone, int VehicleDamage,
    int ControlRig, int DataProcessing, int DiceModifier, int BaseThreshold, int TerrainModifier,
    decimal SpeedMetersPerTurn, string Terrain, string ModifierReason, bool Aware, bool OrdinaryNoGlitch,
    bool CollisionOnFailure, string FailureDecision, int? Hits)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayFollowupCondition? Condition { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayPilotSkill? PilotSkill { get; init; }
    // SR5:230 and 266 give differing hot-sim language; the table must select its interpretation explicitly.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int JumpedHotSimBonus { get; init; }
}
public sealed record SessionPlayVehicleTestOutcome(int Pool, int Limit, int Threshold, int? CountedHits,
    bool TestPossible, bool Succeeded, bool Crashed);
public sealed record SessionPlayIceVehicleInput(string Vehicle, bool TouchesSheet, string GmDecision)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayVehicleTestInput? Test { get; init; }
}
public enum SessionPlayBurningMaterialKind { Object, BarrierMaterial, VehicleMaterial }
// The fire rule does not assign an arbitrary item a condition monitor or a time to total consumption.
// This action records the actual table resolution for one burning material and one turn, not zero damage.
public sealed record SessionPlayFireMaterialInput(int Turn, SessionPlayBurningMaterialKind Kind,
    string Material, string Extent, string Adjudicator, string DamageResolution, bool CompletelyConsumed);
public sealed record SessionPlayFireBarrierInput(int Turn, int Structure, int Armor, int FireResistance,
    int PriorDamage, string Material, string Extent, string TableDecision, int? Hits);
// SR5:199 says DV must exceed armor; p205 says less-than armor cannot damage. Keep equality explicit.
public enum SessionPlayVehicleArmorEquality { Unspecified, ArmorStopsDamage, RollResistance }
public sealed record SessionPlayFireVehicleInput(int Turn, int Body, int Armor, int FireResistance,
    bool Drone, int PriorDamage, SessionPlayVehicleArmorEquality ArmorEquality, string TableDecision, int? Hits);
public sealed record SessionPlayNonlivingFireOutcome(string Kind, int Pool, int EffectiveArmor,
    int PriorDamage, int DamageAfter, int? VehicleMaximum, int? BarrierHoleSquareMeters, bool ArmorStoppedDamage);

public sealed record SessionPlayIceMeltInput(decimal ElapsedMinutes, SessionPlayIceTemperature Temperature,
    decimal? TableRateSquareMetersPerMinute, string GmDecision);
public sealed record SessionPlayDangerInput(SessionPlayDangerDecision Decision, string AffectedActorsOrObjects,
    string Adjudicator, string Consequence);
public sealed record SessionPlayElementalCommand(string EffectId, string ExpectedRevision, string Cursor,
    string TableActionId, SessionPlayElementalAction Action)
{
    // Versions zero/one retain historical semantics. Version two pins vehicle policy per reviewed action.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayVehiclePolicy5400? VehiclePolicy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int RulesVersion { get; init; }
    // Zero is frozen pre-transition replay. One pins accumulated-pair transitions and owned health.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int CollisionTransitionVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? IncidentId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayCollisionInput? Collision { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayPassengerInput? Passenger { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayComposureInput? Composure { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayMovementInput? Movement { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayOwnedCrashDamageInput? OwnedCrashDamage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayFireMaterialInput? Material { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayFireBarrierInput? Barrier { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayFireVehicleInput? BurningVehicle { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayExistingFireInput? ExistingFire { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayFireConsumedInput? Consumed { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayFireTickInput? FireTick { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? NextTurn { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayExtinguishInput? Extinguish { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayFireSpreadInput? Spread { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayIceCrossingInput? Crossing { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayIceVehicleInput? Vehicle { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayIceMeltInput? Melt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayDangerInput? Danger { get; init; }
}
public sealed record SessionPlayElementalEffect(string EffectId, string CastEventId, string CastDigest,
    SessionPlayCastingChoice Source, string Rules, string Kind, string Subject, string Revision,
    bool Active, int Turn, bool TurnDamageRecorded, int FireDamage, int CrossingThreshold,
    decimal RemainingSquareMeters, decimal ElapsedMinutes)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayCrashIncident? Incident { get; init; }
}
public sealed record SessionPlayElementalRoll(string Label, int Pool, int? Threshold, string Rule)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Limit { get; init; }
}
public sealed record SessionPlayElementalOutcome(string EventId, string EffectId, string CastEventId,
    string BeforeRevision, string AfterRevision, SessionPlayElementalCommand Input, string Summary,
    int? DamageBoxes, bool? Prone, bool? ImmediateCrashTestRequired, IReadOnlyList<string> OpenItems)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayCrashIncident? Incident { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayVehicleTestOutcome? VehicleTest { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionPlayNonlivingFireOutcome? NonlivingFire { get; init; }
}
public sealed record SessionPlayElementalProjection(string Cursor, IReadOnlyList<SessionPlayElementalEffect> Effects,
    IReadOnlyList<SessionPlayElementalOutcome> History);
public sealed record SessionPlayElementalReview(SessionPatchRequest Request, string PayloadDigest,
    SessionPlayElementalEffect Before, SessionPlayElementalCommand Command, IReadOnlyList<SessionPlayElementalRoll> Rolls,
    SessionPlayElementalOutcome? Outcome);
