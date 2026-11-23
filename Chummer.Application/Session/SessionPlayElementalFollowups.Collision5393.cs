using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public static partial class SessionPlayElementalFollowups
{
    public static IReadOnlyList<string> RemainingCollision5393(SessionPlayCrashIncident x)
    {
        if (x.TransitionVersion == 0) return RemainingCollisionLegacy5399(x);
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
                    if (!ComposureResolved5401(p))
                    {
                        var health = PassengerCondition5401(p);
                        open.Add(health.Health == SessionPlayCrashHealth.Conscious
                            ? $"Required: {p.Input.Actor} Composure (4)."
                            : health.Health == SessionPlayCrashHealth.Dead
                                ? $"Required: acknowledge {p.Input.Actor}'s death; no Composure roll is possible."
                                : $"Required: {p.Input.Actor} cannot roll Composure while incapacitated. {CrashConditionSummary5401(health)}. Record stabilization/healing or each unattended interval, then resume Composure when conscious.");
                    }
                    if (p.DamageTransition is { } transition)
                        open.Add($"Retained condition: {p.Input.Actor}: {CrashConditionSummary5401(PassengerCondition5401(p))}; crash converted {transition.StunConvertedToPhysical} Stun pairs to Physical.");
                    if (p.Input.OwnedCharacterId is not null && p.Damage > 0 && !p.AppliedToOwnedRunner)
                        open.Add($"Required separate confirmation: apply {p.Damage} {p.Track} damage to owned runner {p.Input.Actor}.");
                    if (p.Penalty is > 0)
                    {
                        if (p.Input.IsOnboardDriver && x.MovementTiming is { Basis: SessionPlayCrashTimeBasis.DriverComposure } timing)
                            open.Add($"Retained consequence: {p.Input.Actor} −{p.Penalty} actions, {Math.Max(0, p.PenaltyTurns!.Value - timing.ElapsedCombatTurns)} of {p.PenaltyTurns} Combat Turns remain after reviewed elapsed time {timing.ElapsedCombatTurns}. Movement actions do not advance time.");
                        else open.Add($"Retained consequence: {p.Input.Actor} −{p.Penalty} actions for {p.PenaltyTurns} Combat Turns from turn {p.IncapacityRuling?.ElapsedCombatTurns ?? 0}; table advances duration.");
                    }
                }
            }
        }
        if (!x.MovementResolved) open.Add("Required: movement/control follow-up; uncontrolled occupants suffer −2 to actions. Do not assume a safe stop.");
        return open;
    }

    private static bool Onboard5393(SessionPlayVehicleControl control) => control is SessionPlayVehicleControl.Manual or SessionPlayVehicleControl.OnboardAr;
    private static bool CrashComplete5393(SessionPlayCrashIncident x) => x.TransitionVersion == 0 ? CrashCompleteLegacy5399(x) : x.MovementResolved && CrashDamageResolved5393(x);
    private static bool CrashDamageResolved5393(SessionPlayCrashIncident x) => CrashDamageRecorded5401(x) && x.Occupants.All(ComposureResolved5401);
    private static bool CrashDamageRecorded5401(SessionPlayCrashIncident x) => !x.Collided || x.VehicleResistanceRecorded && x.OccupantCount == x.Occupants.Count &&
        (!Onboard5393(x.OriginalTest.Control) || x.Occupants.Count(p => p.Input.IsOnboardDriver) == 1) &&
        x.Occupants.All(p => p.Input.OwnedCharacterId is null || p.Damage == 0 || p.AppliedToOwnedRunner);

    private static SessionPlayElementalEffect SeedIncident5393(SessionPlayElementalEffect before,
        SessionEventEnvelope e, SessionPlayIceVehicleInput input, SessionPlayVehicleTestOutcome outcome)
    {
        if (Decode(e).CollisionTransitionVersion == 0) return SeedIncidentLegacy5399(before, e, input, outcome);
        string id = Digest(new { Kind = "vehicle-incident", Parent = before.EffectId, CrashTest = e.EventId });
        var incident = new SessionPlayCrashIncident(id, e.EventId, input.Vehicle, input.Test!, outcome.Crashed, 1,
            false, null, null, null, null, [], 0, false, input.Test!.SpeedMetersPerTurn, "Unresolved heading") { TransitionVersion = 1 };
        var effect = before with { EffectId = id, Kind = "vehicle-incident", Subject = input.Vehicle,
            Rules = "SR5:201–203/crash-aftermath/v1", Revision = "", Incident = incident };
        return effect with { Revision = Digest(effect) };
    }

    private static (SessionPlayCrashIncident Incident, string Summary, int? Damage, bool Awaiting, SessionPlayCrashIncident? Child)
        EvaluateCollision5393(SessionPlayElementalEffect before, SessionPlayElementalCommand c,
            List<SessionPlayElementalRoll> rolls, SessionPlayAuthority? authority, IDictionary<string, int>? values, string eventId,
            IReadOnlyList<SessionPlayElementalOutcome> history, IEnumerable<SessionEventEnvelope> journal)
    {
        Require(c.CollisionTransitionVersion is 0 or 1, "elemental-collision-transition-version-invalid");
        if (c.CollisionTransitionVersion == 0)
        {
            Require(before.Incident?.TransitionVersion == 0, "elemental-collision-transition-cannot-downgrade");
            return EvaluateCollisionLegacy5399(before, c, rolls, authority, values, eventId);
        }
        Require(before.Kind == "vehicle-incident" && before.Incident is not null && c.RulesVersion is 1 or 2,
            "elemental-incident-required");
        var x = before.Incident! with { OwnedConditionWrite = null, TransitionVersion = 1 };
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
                Require(!Onboard5393(x.OriginalTest.Control) || input.OccupantCount > 0, "elemental-onboard-driver-must-be-counted");
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
                    Require(Onboard5393(x.OriginalTest.Control) && input.Actor == x.OriginalTest.Driver, "elemental-onboard-driver-binding-mismatch");
                    Conflict(x.Occupants.All(p => !p.Input.IsOnboardDriver), "elemental-onboard-driver-duplicate");
                }
                if (x.Occupants.Count + 1 == x.OccupantCount && Onboard5393(x.OriginalTest.Control))
                    Require(input.IsOnboardDriver || x.Occupants.Any(p => p.Input.IsOnboardDriver), "elemental-onboard-driver-missing");
                ValidatePassengerCondition5393(input.Condition);
                if (input.OwnedCharacterId is not null) BindPassengerCondition5401(input, authority!, values, history, journal);
                int pool = input.Body + Math.Max(0, input.Armor - 6);
                int? hits = Roll($"{input.Actor} crash resistance (Body + Armor, AP −6)", pool, null, input.Hits);
                damage = hits.HasValue ? Math.Max(0, x.OriginalTest.Body - hits.Value) : null;
                // Crash-specific SR5:201–202 compares vehicle Body with character Armor.
                string track = x.OriginalTest.Body < input.Armor ? "stun" : "physical";
                var transition = CrashDamageTransition5401(input, damage ?? 0, track);
                var result = new SessionPlayPassengerOutcome(input, pool, damage ?? 0, track, null, null, null, null, false)
                    { DamageTransition = transition, CurrentCondition = transition.After, ResistanceEventId = eventId };
                x = x with { Occupants = x.Occupants.Append(result).OrderBy(p => p.Input.Index).ToArray() };
                summary = $"{input.Actor}: crash DV {x.OriginalTest.Body}, AP −6; {damage} {track} damage. Before: {CrashConditionSummary5401(transition.Before)}. After: {CrashConditionSummary5401(transition.After)}. Converted {transition.StunConvertedToPhysical} Physical; unpaired excess Stun {transition.UnpairedExcessStun}. Composure or incapacity follow-up required. No actor monitor changed.";
                break;
            }
            case SessionPlayElementalAction.CrashComposure:
            {
                Require(c.Composure is not null, "elemental-composure-input-required");
                var input = c.Composure!;
                var p = x.Occupants.SingleOrDefault(p => p.Input.Index == input.Index);
                Require(p is not null, "elemental-passenger-resistance-required");
                Require(input.Recovery is null || input.Hits is null && input.IncapacityRuling is null, "elemental-recovery-cannot-supply-composure-hits");
                if (input.Recovery is { } recovery)
                {
                    var recovered = RecoverPassenger5401(p!, recovery, authority, values, history, eventId, journal);
                    x = x with { Occupants = x.Occupants.Select(o => o.Input.Index == input.Index ? recovered.Passenger : o).ToArray(),
                        OwnedConditionWrite = recovered.Write };
                    summary = recovered.Summary;
                    break;
                }
                var current = CurrentPassengerCondition5420(p!, authority, values, history, journal);
                Conflict(!ComposureResolved5401(p!) || current.Health == SessionPlayCrashHealth.Dead
                    && p!.ComposureState != SessionPlayCrashComposureState.NotApplicableDead, "elemental-composure-already-recorded");
                if (current.Health != SessionPlayCrashHealth.Conscious)
                {
                    Require(input.Hits is null && input.IncapacityRuling is null, "elemental-incapacitated-passenger-cannot-roll");
                    var state = current.Health == SessionPlayCrashHealth.Dead ? SessionPlayCrashComposureState.NotApplicableDead
                        : SessionPlayCrashComposureState.DeferredIncapacitated;
                    Conflict(p.ComposureState != state || p.CurrentCondition != current, "elemental-incapacity-already-recorded");
                    var deferred = p with { CurrentCondition = current, ComposureState = state };
                    x = x with { Occupants = x.Occupants.Select(o => o.Input.Index == input.Index ? deferred : o).ToArray() };
                    summary = $"{p.Input.Actor}: {CrashConditionSummary5401(current)}. No Composure test taken or passed. "
                        + (state == SessionPlayCrashComposureState.NotApplicableDead ? "Composure is inapplicable to the deceased; outstanding owned damage and movement remain required."
                            : "Composure deferred. Record actual treatment or deterioration; resume after recovery. Movement and other occupants remain required.");
                    break;
                }
                bool recoveredIncapacity = p.ComposureState == SessionPlayCrashComposureState.DeferredIncapacitated
                    || p.DamageTransition?.After.Health is SessionPlayCrashHealth.Unconscious or SessionPlayCrashHealth.Dying;
                if (recoveredIncapacity)
                {
                    Require(input.IncapacityRuling is not null, "elemental-recovered-composure-timing-ruling-required");
                    var ruling = input.IncapacityRuling!;
                    Require(!string.IsNullOrWhiteSpace(ruling.Adjudicator) && !string.IsNullOrWhiteSpace(ruling.Evidence), "elemental-composure-timing-evidence-required");
                    Label(ruling.Adjudicator); Label(ruling.Evidence);
                    Require(Enum.IsDefined(ruling.Timing) && ruling.ElapsedCombatTurns is >= 0 and <= 1000000,
                        "elemental-composure-timing-invalid");
                    if (ruling.Timing == SessionPlayCrashComposureTiming.IntervalAlreadyElapsed)
                    {
                        Require(input.Hits is null && ruling.ElapsedCombatTurns >= 4, "elemental-composure-interval-not-elapsed");
                        var adjudicated = p with { CurrentCondition = current, IncapacityRuling = ruling,
                            ComposureState = SessionPlayCrashComposureState.AdjudicatedElapsed };
                        x = x with { Occupants = x.Occupants.Select(o => o.Input.Index == input.Index ? adjudicated : o).ToArray() };
                        summary = $"{p.Input.Actor}: GM {ruling.Adjudicator} adjudicates the crash consequence interval elapsed during incapacity ({ruling.ElapsedCombatTurns} Combat Turns). No test or successful roll recorded. {ruling.Evidence}";
                        break;
                    }
                }
                else Require(input.IncapacityRuling is null, "elemental-composure-timing-not-applicable");
                int physical = current.Physical, stun = current.Stun;
                var condition = p.Input.Condition;
                var woundRules = p.Input.OwnedCharacterId is not null && authority?.Derived.Condition is { } sourcedCondition
                    ? sourcedCondition : new CharacterSheetConditionValues(condition.PhysicalMaximum, condition.StunMaximum, 3, 0, 0, 0, 0, 0, 0);
                int wounds = woundRules.WithDamage(physical, stun).WoundModifier;
                int pool = Math.Max(0, p.Input.Charisma + p.Input.Willpower + wounds);
                int? hits = Roll($"{p.Input.Actor} Composure (Charisma + Willpower, wounds included)", pool, 4, input.Hits);
                int penalty = Math.Max(0, 4 - (hits ?? 0));
                var updated = p with { ComposurePool = pool, ComposureHits = hits, Penalty = penalty, PenaltyTurns = penalty,
                    CurrentCondition = current, ComposureState = SessionPlayCrashComposureState.Rolled, IncapacityRuling = input.IncapacityRuling };
                x = x with { Occupants = x.Occupants.Select(o => o.Input.Index == input.Index ? updated : o).ToArray() };
                summary = $"{p.Input.Actor}: Composure {hits}/4; −{penalty} to actions for {penalty} Combat Turns. Retain this consequence at the table."
                    + (input.IncapacityRuling is { } timing ? $" GM {timing.Adjudicator} starts the deferred test's interval after turn {timing.ElapsedCombatTurns}: {timing.Evidence}" : "");
                break;
            }
            case SessionPlayElementalAction.UncontrolledMovement:
            {
                Require(c.Movement is not null, "elemental-movement-input-required");
                var input = c.Movement!; Label(input.Heading); Label(input.TableDecision); Rating(input.PilotRating);
                Conflict(!x.MovementResolved && input.Turn == x.MovementTurn + 1, "elemental-movement-turn-changed");
                Require(Enum.IsDefined(input.Decision) && input.SpeedMetersPerTurn is >= 0 and <= 1000000, "elemental-movement-invalid");
                // Null retains historical replay. New admission requires the version-one timing receipt.
                if (input.Timing is { } timing) ValidateCollisionTiming5411(x, timing, history);
                bool resolved = false;
                if (input.Decision == SessionPlayMovementDecision.RegainControl)
                {
                    Conflict(CrashDamageRecorded5401(x), "elemental-resolve-crash-before-driver-control");
                    Require(input.Test is not null, "elemental-control-test-required");
                    var test = input.Test!;
                    Require(test.SpeedMetersPerTurn == input.SpeedMetersPerTurn, "elemental-control-speed-mismatch");
                    Require(test.Driver == x.OriginalTest.Driver && test.Body == x.OriginalTest.Body && test.Drone == x.OriginalTest.Drone
                        && test.Control == x.OriginalTest.Control && test.SkillName == x.OriginalTest.SkillName && test.Skill == x.OriginalTest.Skill
                        && test.VehicleDamage == (x.VehicleDamageAfter ?? x.PriorVehicleDamage ?? x.OriginalTest.VehicleDamage), "elemental-control-source-mismatch");
                    Require(test.Aware, "elemental-unaware-pilot-cannot-roll");
                    if (c.RulesVersion == 2) test = ApplyVehiclePolicy5400(test, c, authority);
                    else Require(test.JumpedHotSimBonus == 0 && test.Control != SessionPlayVehicleControl.JumpedHot, "elemental-frozen-hot-sim-policy-unavailable");
                    var driver = x.Occupants.SingleOrDefault(p => p.Input.IsOnboardDriver);
                    if (driver is not null)
                    {
                        var driverCondition = CurrentPassengerCondition5420(driver, authority, values, history, journal);
                        Require(driverCondition.Health == SessionPlayCrashHealth.Conscious, "elemental-incapacitated-driver-cannot-control");
                        Conflict(ComposureResolved5401(driver), "elemental-driver-composure-required");
                        Require(test.Condition == new SessionPlayFollowupCondition(driverCondition.PhysicalMaximum,
                            driverCondition.StunMaximum, driverCondition.Physical, driverCondition.Stun), "elemental-driver-condition-changed");
                    }
                    int composureStart = driver?.IncapacityRuling?.ElapsedCombatTurns ?? 0;
                    int composure = input.Timing is { } clock
                        ? driver?.Penalty is { } currentPenalty && clock.ElapsedCombatTurns < driver.PenaltyTurns ? currentPenalty : 0
                        : driver?.Penalty is { } penalty && input.Turn > composureStart && input.Turn <= composureStart + driver.PenaltyTurns ? penalty : 0;
                    if (input.Timing is not null) Require(CollisionChassisOperable5411(x), "elemental-wrecked-chassis-cannot-drive");
                    int instability = Onboard5393(test.Control) ? 2 : 0;
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
                        if (input.Timing is not null)
                        {
                            Conflict(!x.Collided || x.VehicleResistanceRecorded, "elemental-resolve-chassis-before-autopilot");
                            Require(CollisionChassisOperable5411(x), "elemental-wrecked-chassis-cannot-drive");
                        }
                        resolved = true; summary = "Pilot takes the required driving Complex Action and follows traffic (SR5:202).";
                    }
                    else if (input.Decision == SessionPlayMovementDecision.Stopped)
                    { Require(input.SpeedMetersPerTurn == 0, "elemental-stopped-speed-required"); resolved = true; summary = "Table confirms vehicle has stopped at the recorded heading/location."; }
                    else if (input.Decision == SessionPlayMovementDecision.ContinueUncontrolled)
                    {
                        Require(!input.PilotEnabled || input.PilotRating == 0 || input.Timing is not null && !CollisionChassisOperable5411(x),
                            "elemental-enabled-autopilot-must-take-over");
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
                x = x with { MovementTurn = input.Turn, MovementResolved = resolved, SpeedMetersPerTurn = input.SpeedMetersPerTurn, Heading = input.Heading,
                    MovementTiming = input.Timing };
                if (input.Timing is { } reviewedTime)
                {
                    summary += $" GM {reviewedTime.Adjudicator}: {reviewedTime.ElapsedCombatTurns} completed Combat Turns since {(reviewedTime.Basis == SessionPlayCrashTimeBasis.DriverComposure ? "the driver’s retained Composure test" : reviewedTime.Basis == SessionPlayCrashTimeBasis.Collision ? "this collision" : "this uncontrolled incident")} for {x.Vehicle}, incident {x.CollisionNumber}; {reviewedTime.Evidence}.";
                    var driver = x.Occupants.SingleOrDefault(p => p.Input.IsOnboardDriver);
                    if (reviewedTime.Basis == SessionPlayCrashTimeBasis.DriverComposure && driver?.PenaltyTurns is { } duration)
                        summary += $" Driver Composure −{driver.Penalty}: {Math.Max(0, duration - reviewedTime.ElapsedCombatTurns)} Combat Turns remaining. Movement order does not advance time.";
                    if (!CollisionChassisOperable5411(x) && !resolved)
                        summary += " Chassis is inoperable; retain coasting/collision consequences until an actual stop is confirmed.";
                }
                summary += " " + input.TableDecision;
                break;

                void RecordCollision()
                {
                    if (x.Collided)
                    {
                        // A new collision may occur while a passenger awaits recovery. The parent
                        // stays active until its own Composure/recovery and owned writes are resolved.
                        Conflict(CrashDamageRecorded5401(x), "elemental-finish-current-collision-aftermath-first");
                        string childId = Digest(new { ParentIncident = x.IncidentId, CollisionEvent = eventId });
                        child = new(childId, x.OriginalCrashTestEventId, x.Vehicle, x.OriginalTest, true, checked(x.CollisionNumber + 1),
                            false, null, null, null, null, [], 0, false, input.SpeedMetersPerTurn, input.Heading)
                            { ParentIncidentId = x.IncidentId, PriorVehicleDamage = x.VehicleDamageAfter, TransitionVersion = 1 };
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
                var transition = PendingCrashTransition5420(p);
                if (input.ReviewRebase) transition = ValidateCrashRebase5420(p, input, authority!, values!, history, journal, x.IncidentId);
                else Require(input.Rebase is null, "elemental-unrequested-damage-rebase");
                var write = WriteOwnedCondition5401(p, transition.Before, transition.After, input.ExpectedPhysical, input.ExpectedStun,
                    input.ExpectedOverflow, input.ExplicitlyApply, authority, values, history, eventId, journal);
                x = x with { Occupants = x.Occupants.Select(o => o.Input.Index == input.Index
                    ? o with { AppliedToOwnedRunner = true, CurrentCondition = transition.After, AppliedRebase = input.Rebase } : o).ToArray(), OwnedConditionWrite = write };
                damage = p.Damage; summary = (input.Rebase is null ? "" : $"Reviewed replacement of the pending transition; original resistance, hits and damage retained. Original before: {CrashConditionSummary5401(PendingCrashTransition5420(p).Before)}. Original after: {CrashConditionSummary5401(PendingCrashTransition5420(p).After)}. ")
                    + $"Explicitly applied retained occupant {input.Index} damage: {p.Damage} {p.Track}. "
                    + $"Before: {CrashConditionSummary5401(transition.Before)}. After: {CrashConditionSummary5401(transition.After)}. "
                    + "Both bounded monitors and the typed overflow receipt are retained atomically in this owned local overlay. No external actor or vehicle changed.";
                break;
            }
            default: throw new SessionPlayException(SessionPlayFailures.Invalid("elemental-incident-action-invalid"));
        }
        return (x, summary, damage, awaiting, child);
    }

    private static void ValidatePassengerCondition5393(SessionPlayFollowupCondition c)
    {
        Require(c is not null, "elemental-passenger-condition-required");
        Rating(c.PhysicalMaximum, 1); Rating(c.StunMaximum, 1);
        Require(c.PhysicalDamage >= 0 && c.PhysicalDamage <= c.PhysicalMaximum && c.StunDamage >= 0 && c.StunDamage <= c.StunMaximum,
            "elemental-passenger-condition-invalid");
    }
}
