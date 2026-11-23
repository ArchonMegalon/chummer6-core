using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

// Frozen5399 collision replay. Only method names and independent policy5400 dispatch differ.
// Never use this path for new admission; never add current fields or summaries here.
public static partial class SessionPlayElementalFollowups
{
    public static IReadOnlyList<string> RemainingCollisionLegacy5399(SessionPlayCrashIncident x)
    {
        var open = new List<string>();
        if (x.Collided && !x.VehicleResistanceRecorded) open.Add("Required: collision damage to chassis and complete occupant count, including onboard driver.");
        if (x.Collided && x.OccupantCount is { } count)
        {
            for (int index = 1; index <= count; index++)
            {
                var p = x.Occupants.SingleOrDefault(p => p.Input.Index == index);
                if (p is null) open.Add($"Required: occupant {index}/{count} resistance and identity.");
                else
                {
                    if (p.ComposureHits is null) open.Add($"Required: {p.Input.Actor} Composure (4).");
                    if (p.Input.OwnedCharacterId is not null && p.Damage > 0 && !p.AppliedToOwnedRunner)
                        open.Add($"Required separate confirmation: apply {p.Damage} {p.Track} damage to owned runner {p.Input.Actor}.");
                    if (p.Penalty is > 0) open.Add($"Retained consequence: {p.Input.Actor} −{p.Penalty} actions for {p.PenaltyTurns} Combat Turns from crash; table advances duration.");
                }
            }
        }
        if (!x.MovementResolved) open.Add("Required: movement/control follow-up; uncontrolled occupants suffer −2 to actions. Do not assume a safe stop.");
        return open;
    }

    private static bool OnboardLegacy5399(SessionPlayVehicleControl control) => control is SessionPlayVehicleControl.Manual or SessionPlayVehicleControl.OnboardAr;
    private static bool CrashCompleteLegacy5399(SessionPlayCrashIncident x) => x.MovementResolved && CrashDamageResolvedLegacy5399(x);
    private static bool CrashDamageResolvedLegacy5399(SessionPlayCrashIncident x) => !x.Collided || x.VehicleResistanceRecorded && x.OccupantCount == x.Occupants.Count &&
        (!OnboardLegacy5399(x.OriginalTest.Control) || x.Occupants.Count(p => p.Input.IsOnboardDriver) == 1) &&
        x.Occupants.All(p => p.ComposureHits.HasValue && (p.Input.OwnedCharacterId is null || p.Damage == 0 || p.AppliedToOwnedRunner));

    private static SessionPlayElementalEffect SeedIncidentLegacy5399(SessionPlayElementalEffect before,
        SessionEventEnvelope e, SessionPlayIceVehicleInput input, SessionPlayVehicleTestOutcome outcome)
    {
        string id = Digest(new { Kind = "vehicle-incident", Parent = before.EffectId, CrashTest = e.EventId });
        var incident = new SessionPlayCrashIncident(id, e.EventId, input.Vehicle, input.Test!, outcome.Crashed, 1,
            false, null, null, null, null, [], 0, false, input.Test!.SpeedMetersPerTurn, "Unresolved heading");
        var effect = before with { EffectId = id, Kind = "vehicle-incident", Subject = input.Vehicle,
            Rules = "SR5:201–203/crash-aftermath/v1", Revision = "", Incident = incident };
        return effect with { Revision = Digest(effect) };
    }

    private static (SessionPlayCrashIncident Incident, string Summary, int? Damage, bool Awaiting, SessionPlayCrashIncident? Child)
        EvaluateCollisionLegacy5399(SessionPlayElementalEffect before, SessionPlayElementalCommand c,
            List<SessionPlayElementalRoll> rolls, SessionPlayAuthority? authority, IDictionary<string, int>? values, string eventId)
    {
        Require(before.Kind == "vehicle-incident" && before.Incident is not null && c.RulesVersion is 1 or 2,
            "elemental-incident-required");
        var x = before.Incident!;
        Require(c.IncidentId == x.IncidentId, "elemental-incident-binding-mismatch");
        int? damage = null; bool awaiting = false; string summary = ""; SessionPlayCrashIncident? child = null;
        int? Roll(string label, int pool, int? threshold, int? hits)
        {
            rolls.Add(new(label, pool, threshold, "SR5:201–203; ordinary crash aftermath"));
            if (hits is null) awaiting = true;
            else Require(hits >= 0 && hits <= pool, "elemental-hits-outside-core-pool");
            return hits;
        }
        switch (c.Action)
        {
            case SessionPlayElementalAction.CollisionDamage:
            {
                Require(c.Collision is not null && x.Collided, "elemental-collision-required");
                Conflict(!x.VehicleResistanceRecorded, "elemental-collision-damage-already-recorded");
                var input = c.Collision!; Rating(input.Armor); Label(input.RosterDecision);
                Require(input.OccupantCount is >= 0 and <= 32 && input.AllOccupantsIncludingDriver,
                    "elemental-complete-occupant-count-required");
                Require(!OnboardLegacy5399(x.OriginalTest.Control) || input.OccupantCount > 0, "elemental-onboard-driver-must-be-counted");
                int pool = x.OriginalTest.Body + Math.Max(0, input.Armor - 6);
                int? hits = Roll("Chassis crash resistance (Body + Armor, AP −6)", pool, null, input.Hits);
                damage = hits.HasValue ? Math.Max(0, x.OriginalTest.Body - hits.Value) : null;
                Require(CharacterVehicleConditionMonitorCalculator.TryCalculatePhysicalMaximum(
                    x.OriginalTest.Drone ? "Drones: Small" : "Cars", x.OriginalTest.Body, [], out int maximum), "elemental-vehicle-condition-invalid");
                int total = Math.Min(maximum, checked((x.PriorVehicleDamage ?? x.OriginalTest.VehicleDamage) + (damage ?? 0)));
                x = x with { VehicleResistanceRecorded = true, VehicleArmor = input.Armor, VehicleDamage = damage,
                    VehicleDamageAfter = total, OccupantCount = input.OccupantCount };
                summary = $"{x.Vehicle}: crash DV {x.OriginalTest.Body}P, AP −6; {damage} damage, declared chassis {total}/{maximum}. {input.OccupantCount} occupants require individual resistance and Composure. No vehicle record changed.";
                break;
            }
            case SessionPlayElementalAction.PassengerResistance:
            {
                Require(c.Passenger is not null && x.VehicleResistanceRecorded && x.OccupantCount.HasValue, "elemental-collision-roster-required");
                var input = c.Passenger!; Label(input.Actor); Rating(input.Body, 1); Rating(input.Armor);
                Rating(input.Charisma, 1); Rating(input.Willpower, 1);
                Require(input.OrdinaryNoGlitch, "elemental-special-passenger-context-open");
                Require(input.Index >= 1 && input.Index <= x.OccupantCount, "elemental-occupant-index-invalid");
                Conflict(x.Occupants.All(p => p.Input.Index != input.Index), "elemental-passenger-already-recorded");
                Require((input.OwnedCharacterId is null) == (input.OwnedOwner is null), "elemental-owned-passenger-binding-required");
                if (input.OwnedCharacterId is not null)
                {
                    Require(authority is not null && input.OwnedCharacterId == authority.Binding.Version.CharacterId
                        && input.OwnedOwner == authority.Binding.OwnerId, "elemental-owned-passenger-binding-mismatch");
                    Conflict(x.Occupants.All(p => p.Input.OwnedCharacterId is null), "elemental-owned-passenger-duplicate");
                }
                Require(input.Condition is not null, "elemental-passenger-condition-required");
                if (input.IsOnboardDriver)
                {
                    Require(OnboardLegacy5399(x.OriginalTest.Control) && input.Actor == x.OriginalTest.Driver, "elemental-onboard-driver-binding-mismatch");
                    Conflict(x.Occupants.All(p => !p.Input.IsOnboardDriver), "elemental-onboard-driver-duplicate");
                }
                if (x.Occupants.Count + 1 == x.OccupantCount && OnboardLegacy5399(x.OriginalTest.Control))
                    Require(input.IsOnboardDriver || x.Occupants.Any(p => p.Input.IsOnboardDriver), "elemental-onboard-driver-missing");
                ValidatePassengerConditionLegacy5399(input.Condition);
                int pool = input.Body + Math.Max(0, input.Armor - 6);
                int? hits = Roll($"{input.Actor} crash resistance (Body + Armor, AP −6)", pool, null, input.Hits);
                damage = hits.HasValue ? Math.Max(0, x.OriginalTest.Body - hits.Value) : null;
                // Crash-specific SR5:201–202 compares vehicle Body with character Armor.
                string track = x.OriginalTest.Body < input.Armor ? "stun" : "physical";
                var result = new SessionPlayPassengerOutcome(input, pool, damage ?? 0, track, null, null, null, null, false);
                x = x with { Occupants = x.Occupants.Append(result).OrderBy(p => p.Input.Index).ToArray() };
                summary = $"{input.Actor}: crash DV {x.OriginalTest.Body}, AP −6; {damage} {track} damage. Composure still required. No actor monitor changed.";
                break;
            }
            case SessionPlayElementalAction.CrashComposure:
            {
                Require(c.Composure is not null, "elemental-composure-input-required");
                var input = c.Composure!;
                var p = x.Occupants.SingleOrDefault(p => p.Input.Index == input.Index);
                Require(p is not null, "elemental-passenger-resistance-required");
                Conflict(p!.ComposureHits is null, "elemental-composure-already-recorded");
                var condition = p.Input.Condition;
                int physical = condition.PhysicalDamage + (p.Track == "physical" ? p.Damage : 0);
                int stun = condition.StunDamage + (p.Track == "stun" ? p.Damage : 0);
                Require(physical < condition.PhysicalMaximum && stun < condition.StunMaximum,
                    "elemental-incapacitated-composure-adjudication-required");
                int wounds = new CharacterSheetConditionValues(condition.PhysicalMaximum, condition.StunMaximum, 3, 0, 0, 0, 0, 0, 0)
                    .WithDamage(Math.Min(physical, condition.PhysicalMaximum), Math.Min(stun, condition.StunMaximum)).WoundModifier;
                int pool = Math.Max(0, p.Input.Charisma + p.Input.Willpower + wounds);
                int? hits = Roll($"{p.Input.Actor} Composure (Charisma + Willpower, wounds included)", pool, 4, input.Hits);
                int penalty = Math.Max(0, 4 - (hits ?? 0));
                var updated = p with { ComposurePool = pool, ComposureHits = hits, Penalty = penalty, PenaltyTurns = penalty };
                x = x with { Occupants = x.Occupants.Select(o => o.Input.Index == input.Index ? updated : o).ToArray() };
                summary = $"{p.Input.Actor}: Composure {hits}/4; −{penalty} to actions for {penalty} Combat Turns. Retain this consequence at the table.";
                break;
            }
            case SessionPlayElementalAction.UncontrolledMovement:
            {
                Require(c.Movement is not null, "elemental-movement-input-required");
                var input = c.Movement!; Label(input.Heading); Label(input.TableDecision); Rating(input.PilotRating);
                Conflict(!x.MovementResolved && input.Turn == x.MovementTurn + 1, "elemental-movement-turn-changed");
                Require(Enum.IsDefined(input.Decision) && input.SpeedMetersPerTurn is >= 0 and <= 1000000, "elemental-movement-invalid");
                bool resolved = false;
                if (input.Decision == SessionPlayMovementDecision.RegainControl)
                {
                    Conflict(CrashDamageResolvedLegacy5399(x), "elemental-resolve-crash-before-driver-control");
                    Require(input.Test is not null, "elemental-control-test-required");
                    var test = input.Test!;
                    Require(test.SpeedMetersPerTurn == input.SpeedMetersPerTurn, "elemental-control-speed-mismatch");
                    Require(test.Driver == x.OriginalTest.Driver && test.Body == x.OriginalTest.Body && test.Drone == x.OriginalTest.Drone
                        && test.Control == x.OriginalTest.Control && test.SkillName == x.OriginalTest.SkillName && test.Skill == x.OriginalTest.Skill
                        && test.VehicleDamage == (x.VehicleDamageAfter ?? x.PriorVehicleDamage ?? x.OriginalTest.VehicleDamage), "elemental-control-source-mismatch");
                    Require(test.Aware, "elemental-unaware-pilot-cannot-roll");
                    if (c.RulesVersion == 1) Require(test.JumpedHotSimBonus == 0 && test.Control != SessionPlayVehicleControl.JumpedHot, "elemental-frozen-hot-sim-policy-unavailable");
                    var driver = x.Occupants.SingleOrDefault(p => p.Input.IsOnboardDriver);
                    int composure = driver?.Penalty is { } penalty && input.Turn <= driver.PenaltyTurns ? penalty : 0;
                    int instability = OnboardLegacy5399(test.Control) ? 2 : 0;
                    if (c.RulesVersion == 2) test = ApplyVehiclePolicy5400(test, c, authority);
                    var basis = VehicleTestBasis5381(test with { DiceModifier = checked(test.DiceModifier - instability - composure) }, true, c.VehiclePolicy);
                    var hits = Roll("Regain vehicle control", basis.Pool, basis.Threshold, test.Hits);
                    rolls[^1] = rolls[^1] with { Limit = basis.Limit };
                    resolved = hits.HasValue && Math.Min(hits.Value, basis.Limit) >= basis.Threshold;
                    summary = resolved ? "Control regained; spend the required driving Complex Action each Combat Turn." : "Control test failed: vehicle remains uncontrolled; −2 actions for occupants. Resolve the next movement interval.";
                    if (hits.HasValue && !resolved && test.CollisionOnFailure)
                    {
                        RecordCollision();
                        resolved = child is not null;
                        summary = "Failed control test on a collision course. New collision damage, occupants and Composure are required. " + test.FailureDecision;
                    }
                }
                else
                {
                    Require(input.Test is null, "elemental-followup-contradictory-input");
                    if (input.Decision == SessionPlayMovementDecision.AutopilotTakesOver)
                    {
                        Require(input.PilotEnabled && input.PilotRating > 0, "elemental-autopilot-required");
                        resolved = true; summary = "Pilot takes the required driving Complex Action and follows traffic (SR5:202).";
                    }
                    else if (input.Decision == SessionPlayMovementDecision.Stopped)
                    { Require(input.SpeedMetersPerTurn == 0, "elemental-stopped-speed-required"); resolved = true; summary = "Table confirms vehicle has stopped at the recorded heading/location."; }
                    else if (input.Decision == SessionPlayMovementDecision.ContinueUncontrolled)
                    {
                        Require(!input.PilotEnabled || input.PilotRating == 0, "elemental-enabled-autopilot-must-take-over");
                        Require(x.MovementTurn == 0 || input.Heading == x.Heading, "elemental-uncontrolled-cannot-steer");
                        Require(input.SpeedMetersPerTurn <= x.SpeedMetersPerTurn && (!input.AcceleratorLocked || input.SpeedMetersPerTurn == x.SpeedMetersPerTurn), "elemental-uncontrolled-speed-invalid");
                        summary = "Uncontrolled: last heading; no defense or actions except slowing, or maintaining speed with locked accelerator. Occupants have −2 actions. Next interval remains required.";
                    }
                    else
                    {
                        RecordCollision(); resolved = child is not null;
                        summary = "Collision now occurred on the retained uncontrolled trajectory; chassis/occupant damage and Composure are required.";
                    }
                }
                x = x with { MovementTurn = input.Turn, MovementResolved = resolved, SpeedMetersPerTurn = input.SpeedMetersPerTurn, Heading = input.Heading };
                summary += " " + input.TableDecision;
                break;

                void RecordCollision()
                {
                    if (x.Collided)
                    {
                        Conflict(CrashDamageResolvedLegacy5399(x), "elemental-finish-current-collision-aftermath-first");
                        string childId = Digest(new { ParentIncident = x.IncidentId, CollisionEvent = eventId });
                        child = new(childId, x.OriginalCrashTestEventId, x.Vehicle, x.OriginalTest, true, checked(x.CollisionNumber + 1),
                            false, null, null, null, null, [], 0, false, input.SpeedMetersPerTurn, input.Heading)
                            { ParentIncidentId = x.IncidentId, PriorVehicleDamage = x.VehicleDamageAfter };
                    }
                    else x = x with { Collided = true };
                }
            }
            case SessionPlayElementalAction.ApplyOwnedCrashDamage:
            {
                Require(c.OwnedCrashDamage is not null && authority is not null && values is not null, "elemental-owned-damage-authority-required");
                var input = c.OwnedCrashDamage!;
                var p = x.Occupants.SingleOrDefault(p => p.Input.Index == input.Index);
                Require(p is not null && p.Input.OwnedCharacterId is not null && input.ExplicitlyApply, "elemental-explicit-owned-damage-required");
                Conflict(!p!.AppliedToOwnedRunner, "elemental-owned-damage-already-applied");
                Require(input.CharacterId == authority!.Binding.Version.CharacterId && input.CharacterId == p.Input.OwnedCharacterId
                    && input.Owner == authority.Binding.OwnerId && input.Owner == p.Input.OwnedOwner && authority.StandardWoundRules,
                    "elemental-owned-damage-binding-mismatch");
                Conflict(values!.TryGetValue("physical", out int physical) && values.TryGetValue("stun", out int stun)
                    && physical == input.ExpectedPhysical && stun == input.ExpectedStun, "elemental-owned-monitor-changed");
                var tracker = authority.Trackers.SingleOrDefault(t => t.TrackerId == p.Track);
                Require(tracker?.MaximumValue is { } maximum && values[p.Track] + p.Damage <= maximum,
                    "elemental-owned-overflow-transition-required");
                values[p.Track] = checked(values[p.Track] + p.Damage);
                x = x with { Occupants = x.Occupants.Select(o => o.Input.Index == input.Index ? o with { AppliedToOwnedRunner = true } : o).ToArray() };
                damage = p.Damage; summary = $"Explicitly applied retained occupant {input.Index} damage: {p.Damage} {p.Track} to this owned runner's local monitor. No external actor or vehicle changed.";
                break;
            }
            default: throw new SessionPlayException(SessionPlayFailures.Invalid("elemental-incident-action-invalid"));
        }
        return (x, summary, damage, awaiting, child);
    }

    private static void ValidatePassengerConditionLegacy5399(SessionPlayFollowupCondition c)
    {
        Rating(c.PhysicalMaximum, 1); Rating(c.StunMaximum, 1);
        Require(c.PhysicalDamage >= 0 && c.PhysicalDamage <= c.PhysicalMaximum && c.StunDamage >= 0 && c.StunDamage <= c.StunMaximum,
            "elemental-passenger-condition-invalid");
    }
}
