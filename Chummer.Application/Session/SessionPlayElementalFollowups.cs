using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

/// <summary>SR5 table effect calculations. Never writes a monitor, initiative, equipment or canonical source.</summary>
public static partial class SessionPlayElementalFollowups
{
    internal const int MaxCommandCharacters5393 = 8192; // Bounded typed form; note limits remain unchanged.
    public const string EventType = SessionPlayEventTypes.ElementalFollowup;
    private const string FireRules = "SR5:170-172/ordinary-fire/v1";
    private const string IceRules = "SG:104+SR5:293/ice-sheet/v1";
    private const string ColdRules = "SG:103-104+SR5:170/cold-danger/v1";

    public static IReadOnlyDictionary<string, RulesetCapabilityValue> Encode(SessionPlayElementalCommand command)
        => new Dictionary<string, RulesetCapabilityValue> {
            ["command"] = new(RulesetCapabilityValueKinds.String, StringValue: JsonSerializer.Serialize(CurrentCollisionCommand5420(command) with { RulesVersion = command.VehiclePolicy is null ? 1 : 2 }, Json)) };

    internal static SessionPlayElementalCommand Decode(SessionEventEnvelope e)
    {
        Require(e.EventType == EventType && e.Payload.Count == 1 && e.Payload.TryGetValue("command", out _), "elemental-followup-shape");
        var v = e.Payload["command"];
        Require(v is not null && v.Kind == RulesetCapabilityValueKinds.String && v.StringValue is { Length: > 0 } && v.StringValue.Length <= MaxCommandCharacters5393
            && v.IntegerValue is null && v.BooleanValue is null && v.NumberValue is null && v.DecimalValue is null
            && v.Items is null && v.Properties is null, "elemental-followup-shape");
        SessionPlayElementalCommand? command;
        try { command = JsonSerializer.Deserialize<SessionPlayElementalCommand>(v.StringValue!, Json); }
        catch (JsonException) { throw new SessionPlayException(SessionPlayFailures.Invalid("elemental-followup-shape")); }
        // The one canonical typed transport rejects unknown, duplicate and contradictory JSON fields.
        Require(command is not null && JsonSerializer.Serialize(command, Json) == v.StringValue, "elemental-followup-shape");
        return command!;
    }

    internal static void Seed(SessionPlayAuthority authority, SessionPlayCastOutcome cast, SessionEventEnvelope e,
        List<SessionPlayElementalEffect> effects)
    {
        if (cast.Indirect is not { } indirect || authority.CastingAuthority is not { } casting) return;
        var source = casting.Choices.Concat(casting.Indirect?.Choices ?? []).Concat(casting.Indirect?.Elemental?.Choices ?? [])
            .Single(c => c.CapabilityId == e.Payload["capability"].StringValue);
        if (indirect.IceSheet is { } sheet)
            Add("ice", sheet.AimReference, new { Area = sheet.AimReference }, 0, sheet.CrossingThreshold,
                (decimal)Math.PI * sheet.RadiusMeters * sheet.RadiusMeters, IceRules);
        foreach (var target in indirect.Targets)
            foreach (var item in target.Elemental?.Items ?? [])
            {
                string subject = $"Target {JsonSerializer.Serialize(target.Label)} · item {JsonSerializer.Serialize(item.Label)}";
                if (item.FireDamage is > 0)
                {
                    // Labels do not establish cross-cast identity. Require an explicit table link
                    // before the existing fire can be ticked as a new instance.
                    Add(item.Resolution == "already-burning-no-reset" ? "unlinked-fire" : "fire", subject, new { Target = target.Label, Item = item.Label }, item.FireDamage.Value, 0, 0, FireRules);
                }
                if (item.Resolution == "dangerously-destroyed") Add("danger", subject, new { Target = target.Label, Item = item.Label }, 0, 0, 0, ColdRules);
            }
        void Add(string kind, string subject, object identity, int dv, int threshold, decimal area, string rules)
        {
            // Display separators are not identity separators. Keep cast, kind, target and item structured.
            string id = Digest(new { CastEventId = e.EventId, Kind = kind, Identity = identity });
            var effect = new SessionPlayElementalEffect(id, e.EventId, Digest(e), source, rules, kind, subject,
                "", true, 1, false, dv, threshold, area, 0);
            effects.Add(effect with { Revision = Digest(effect) });
        }
    }

    internal static (IReadOnlyList<SessionPlayElementalRoll> Rolls, SessionPlayElementalOutcome? Outcome)
        Evaluate(List<SessionPlayElementalEffect> effects, List<SessionPlayElementalOutcome> history,
        SessionEventEnvelope e, IEnumerable<SessionEventEnvelope> prefix, bool preview, SessionPlayAuthority? authority = null, IDictionary<string, int>? values = null)
    {
        var c = Decode(e);
        Conflict(c.Cursor == Cursor(prefix), "elemental-followup-cursor-changed");
        int index = effects.FindIndex(x => x.EffectId == c.EffectId);
        Conflict(index >= 0 && effects[index].Revision == c.ExpectedRevision, "elemental-followup-state-changed");
        var before = effects[index]; var after = before;
        Require(before.Active, "elemental-effect-ended");
        Label(c.TableActionId);
        Conflict(!history.Any(h => h.Input.TableActionId == c.TableActionId), "elemental-table-action-already-recorded");
        Require(Enum.IsDefined(c.Action) && c.RulesVersion is 0 or 1 or 2, "elemental-action-invalid");
        Require(c.CollisionTransitionVersion is 0 or 1 && (c.CollisionTransitionVersion == 0 || UsesCollisionTransitions5420(c)),
            "elemental-collision-transition-version-invalid");
        Require(c.RulesVersion == 2 ? c.VehiclePolicy is not null && NeedsVehiclePolicy5400(c) : c.VehiclePolicy is null,
            "vehicle-policy-command-version-invalid");
        bool incidentAction = c.Action is SessionPlayElementalAction.CollisionDamage or SessionPlayElementalAction.PassengerResistance
            or SessionPlayElementalAction.CrashComposure or SessionPlayElementalAction.UncontrolledMovement or SessionPlayElementalAction.ApplyOwnedCrashDamage;
        Require(incidentAction || c.IncidentId is null, "elemental-followup-contradictory-input");
        int fields = (c.FireTick is null ? 0 : 1) + (c.NextTurn is null ? 0 : 1) + (c.Extinguish is null ? 0 : 1)
            + (c.Spread is null ? 0 : 1) + (c.Crossing is null ? 0 : 1) + (c.Vehicle is null ? 0 : 1)
            + (c.Barrier is null ? 0 : 1) + (c.BurningVehicle is null ? 0 : 1) + (c.Material is null ? 0 : 1) + (c.Melt is null ? 0 : 1) + (c.Danger is null ? 0 : 1) + (c.ExistingFire is null ? 0 : 1) + (c.Consumed is null ? 0 : 1) + (c.Collision is null ? 0 : 1) + (c.Passenger is null ? 0 : 1)
            + (c.Composure is null ? 0 : 1) + (c.Movement is null ? 0 : 1) + (c.OwnedCrashDamage is null ? 0 : 1);
        Require(fields == 1, "elemental-followup-contradictory-input");
        var rolls = new List<SessionPlayElementalRoll>(); var open = new List<string>();
        var spawned = new List<SessionPlayElementalEffect>();
        SessionPlayVehicleTestOutcome? vehicleTest = null;
        SessionPlayNonlivingFireOutcome? nonlivingFire = null;
        string summary = ""; int? damage = null; bool? prone = null, crash = null; bool awaitingHits = false;
        switch (c.Action)
        {
            case SessionPlayElementalAction.CollisionDamage:
            case SessionPlayElementalAction.PassengerResistance:
            case SessionPlayElementalAction.CrashComposure:
            case SessionPlayElementalAction.UncontrolledMovement:
            case SessionPlayElementalAction.ApplyOwnedCrashDamage:
            {
                var result = EvaluateCollision5393(before, c, rolls, authority, values, e.EventId, history, prefix);
                after = before with { Incident = result.Incident, Active = !CrashComplete5393(result.Incident) };
                summary = result.Summary; damage = result.Damage; awaitingHits = result.Awaiting;
                open.AddRange(RemainingCollision5393(result.Incident));
                if (result.Child is { } child)
                {
                    var nextIncident = before with { EffectId = child.IncidentId, Revision = "", Incident = child, Active = true };
                    spawned.Add(nextIncident with { Revision = Digest(nextIncident) });
                    open.Add($"Required next collision incident: {child.IncidentId}. Resolve its new damage, occupants and movement separately.");
                }
                break;
            }
            case SessionPlayElementalAction.ExistingFire:
            {
                Require(before.Kind == "unlinked-fire" && c.ExistingFire is not null, "elemental-action-kind-mismatch");
                var x = c.ExistingFire!; Label(x.GmDecision);
                if (x.DeclaredSeparate)
                {
                    Require(x.OriginalEffectId is null && x.ExpectedOriginalRevision is null, "elemental-followup-contradictory-input");
                    after = before with { Kind = "fire" };
                    summary = $"Table declared a separate previously untracked fire: DV {before.FireDamage}. Turn 1 is this tracking interval, not a new ignition.";
                }
                else
                {
                    var original = effects.SingleOrDefault(f => f.EffectId == x.OriginalEffectId);
                    Conflict(original is { Active: true, Kind: "fire" } && original.Revision == x.ExpectedOriginalRevision
                        && original.FireDamage == before.FireDamage, "elemental-original-fire-link-changed");
                    after = before with { Active = false };
                    summary = $"Linked to retained fire {original!.Subject} ({original.EffectId}). Its DV, turn and damage state were not reset.";
                }
                break;
            }
            case SessionPlayElementalAction.FireTick:
            {
                Require(before.Kind == "fire" && c.FireTick is not null, "elemental-action-kind-mismatch");
                var x = c.FireTick!; Turn(x.Turn);
                Conflict(!before.TurnDamageRecorded, "elemental-fire-turn-already-recorded");
                Require(x.OrdinaryLivingTarget, "elemental-special-fire-context-open");
                Rating(x.Body, 1);
                int pool = OngoingFirePool5381(x.Body, x.Armor, x.FireResistance);
                int? hits = Roll("Living target fire resistance", pool, null, x.Hits, "SR5:170-172; ongoing open flame AP -2");
                damage = hits.HasValue ? Math.Max(0, before.FireDamage - hits.Value) : null;
                after = before with { TurnDamageRecorded = true };
                summary = $"Turn {before.Turn}: {before.FireDamage}P fire, AP -2; {damage} boxes at the table. Next turn has not started.";
                open.Add("Calculated living-target damage only. Apply any owned-runner damage separately through the guarded monitor action. Item destruction requires table adjudication.");
                break;
            }
            case SessionPlayElementalAction.FireBarrierTick:
            {
                Require(before.Kind == "fire" && c.Barrier is not null, "elemental-action-kind-mismatch");
                var x = c.Barrier!; Turn(x.Turn);
                Conflict(!before.TurnDamageRecorded, "elemental-fire-turn-already-recorded");
                Rating(x.Structure, 1); Label(x.Material); Label(x.Extent); Label(x.TableDecision);
                Require(x.PriorDamage is >= 0 and <= 1000000, "elemental-barrier-damage-invalid");
                int pool = OngoingFirePool5381(x.Structure, x.Armor, x.FireResistance);
                int armor = pool - x.Structure;
                bool legacyStopped = c.RulesVersion == 0 && before.FireDamage <= armor;
                // SR5:197 damaging a barrier, not penetrating/attacking behind it.
                int? hits = legacyStopped ? 0 : Roll("Burning barrier Structure + modified Armor", pool, null, x.Hits, "SR5:171,197; ongoing flame AP -2; physical barrier damage");
                if (legacyStopped) Require(x.Hits is null, "elemental-armor-stopped-no-roll");
                damage = legacyStopped ? 0 : hits.HasValue ? Math.Max(0, before.FireDamage - hits.Value) : null;
                int total = checked(x.PriorDamage + (damage ?? 0));
                nonlivingFire = new("barrier", pool, armor, x.PriorDamage, total, null, total / x.Structure, legacyStopped);
                after = before with { TurnDamageRecorded = true };
                summary = $"Turn {before.Turn}: {damage} barrier damage; total {total}, hole-equivalent {total / x.Structure} m² in the declared standard barrier section.";
                open.Add("The table supplies material/extent and prior damage for the SR5:197 section. Resolve actual hole placement, remaining fuel and any behind-barrier targets separately. A hole does not automatically consume every burning part; use the material/destruction action when appropriate. No barrier record changed.");
                break;
            }
            case SessionPlayElementalAction.FireVehicleTick:
            {
                Require(before.Kind == "fire" && c.BurningVehicle is not null, "elemental-action-kind-mismatch");
                var x = c.BurningVehicle!; Turn(x.Turn);
                Conflict(!before.TurnDamageRecorded, "elemental-fire-turn-already-recorded");
                Rating(x.Body); Label(x.TableDecision);
                Require(Enum.IsDefined(x.ArmorEquality), "elemental-vehicle-armor-equality-decision-required");
                if (c.RulesVersion == 0) Require(x.ArmorEquality != SessionPlayVehicleArmorEquality.Unspecified, "elemental-vehicle-armor-equality-decision-required");
                else Require(x.ArmorEquality == SessionPlayVehicleArmorEquality.Unspecified, "elemental-per-action-rule-override-forbidden");
                Require(CharacterVehicleConditionMonitorCalculator.TryCalculatePhysicalMaximum(
                    x.Drone ? "Drones: Small" : "Cars", x.Body, [], out int maximum)
                    && x.PriorDamage >= 0 && x.PriorDamage <= maximum, "elemental-vehicle-condition-invalid");
                int pool = OngoingFirePool5381(x.Body, x.Armor, x.FireResistance); int armor = pool - x.Body;
                if (c.RulesVersion == 1) Require(before.FireDamage != armor, "elemental-frozen-vehicle-armor-policy-unavailable");
                var equality = c.RulesVersion == 2 ? Policy5400(c, authority).ArmorEquality : x.ArmorEquality;
                bool stopped = before.FireDamage < armor || before.FireDamage == armor && equality == SessionPlayVehicleArmorEquality.ArmorStopsDamage;
                int? hits;
                if (stopped) { Require(x.Hits is null, "elemental-armor-stopped-no-roll"); hits = 0; }
                else hits = Roll("Burning vehicle Body + modified Armor", pool, null, x.Hits, "SR5:171,199,205; ongoing flame AP -2; retained vehicle armor policy");
                damage = stopped ? 0 : hits.HasValue ? Math.Max(0, before.FireDamage - hits.Value) : null;
                int total = Math.Min(maximum, checked(x.PriorDamage + (damage ?? 0)));
                nonlivingFire = new("vehicle", pool, armor, x.PriorDamage, total, maximum, null, stopped);
                after = before with { TurnDamageRecorded = true };
                summary = $"Turn {before.Turn}: {damage} physical vehicle damage; declared standard monitor {total}/{maximum}. "
                    + (total == maximum ? "Vehicle monitor filled; this does not establish that all burning material is consumed." : "Burning material remains tracked separately.");
                open.Add("Table vehicle damage calculation only; no canonical chassis, passenger, inventory or monitor write. Standard vehicle/ordinary-drone monitors only. Complete material consumption still needs an explicit table decision.");
                break;
            }
            case SessionPlayElementalAction.FireMaterialTick:
            {
                Require(before.Kind == "fire" && c.Material is not null, "elemental-action-kind-mismatch");
                var x = c.Material!; Turn(x.Turn);
                Conflict(!before.TurnDamageRecorded, "elemental-fire-turn-already-recorded");
                Require(Enum.IsDefined(x.Kind), "elemental-material-kind-required");
                Label(x.Material); Label(x.Extent); Label(x.Adjudicator); Label(x.DamageResolution);
                after = before with { TurnDamageRecorded = true, Active = !x.CompletelyConsumed };
                summary = $"Turn {before.Turn}, fire DV {before.FireDamage}: {x.Adjudicator} resolved damage to {x.Material} ({x.Extent}): {x.DamageResolution}. "
                    + (x.CompletelyConsumed ? "Burning material completely consumed; this fire is closed." : "Burning material remains; the next turn may now start.");
                open.Add("Explicit table material resolution (SR5:171,197,199,205); no living Body, invented item monitor, zero-damage assumption, or canonical inventory/vehicle change. Any separately affected actor or chassis needs its own damage resolution.");
                break;
            }
            case SessionPlayElementalAction.FireConsumed:
            {
                Require(before.Kind == "fire" && c.Consumed is not null, "elemental-action-kind-mismatch");
                var x = c.Consumed!; Turn(x.Turn); Label(x.Adjudicator); Label(x.Decision);
                Require(x.CompletelyDestroyed, "elemental-destruction-decision-required");
                after = before with { Active = false };
                summary = $"{x.Adjudicator} recorded complete destruction of the burning subject. Further ticks for this subject are closed; other fires remain independent.";
                open.Add("Table destruction decision only; no inventory or character was modified.");
                break;
            }
            case SessionPlayElementalAction.FireNextTurn:
                Require(before.Kind == "fire" && c.NextTurn.HasValue, "elemental-action-kind-mismatch");
                Conflict(before.TurnDamageRecorded && c.NextTurn == before.Turn + 1, "elemental-fire-turn-order");
                Require(before.FireDamage < 1000 && before.Turn < 10000, "elemental-fire-boundary");
                after = before with { Turn = c.NextTurn!.Value, FireDamage = before.FireDamage + 1, TurnDamageRecorded = false };
                summary = $"Turn {after.Turn} started: fire DV {after.FireDamage}. No damage tick was applied.";
                break;
            case SessionPlayElementalAction.Extinguish:
            {
                Require(before.Kind == "fire" && c.Extinguish is not null, "elemental-action-kind-mismatch");
                var x = c.Extinguish!; Turn(x.Turn); Label(x.Actor); Rating(x.Agility, 1); Rating(x.Intuition, 1);
                Require(x.OrdinaryNoGlitch && Enum.IsDefined(x.Method), "elemental-special-extinguish-context-open");
                if (x.Method == SessionPlayExtinguishMethod.Other) Label(x.MethodDetail); else Require(x.MethodDetail == "", "elemental-followup-contradictory-input");
                int? hits = Roll("Extinguishing Agility + Intuition, with wounds", Math.Max(0, x.Agility + x.Intuition + Wounds(x.Condition)), before.FireDamage, x.Hits, "SR5:171-172");
                int remaining = hits.HasValue ? Math.Max(0, before.FireDamage - hits.Value) : before.FireDamage;
                after = before with { FireDamage = remaining, Active = remaining > 0 };
                summary = remaining == 0 ? "Fire extinguished." : $"Fire DV {before.FireDamage} → {remaining}; fire remains active.";
                break;
            }
            case SessionPlayElementalAction.Spread:
            {
                Require(before.Kind == "fire" && c.Spread is not null, "elemental-action-kind-mismatch");
                var x = c.Spread!; Turn(x.Turn); Label(x.GmDecision);
                if (x.NoNearbyFlammables)
                {
                    Require(x.Subject == "" && x.Armor == 0 && x.FireResistance == 0 && x.GmThreshold == 0 && x.Hits is null, "elemental-followup-contradictory-input");
                    summary = "Table checked the surroundings: no nearby flammable subject declared.";
                }
                else
                {
                    Label(x.Subject); Rating(x.Armor); Rating(x.FireResistance); Rating(x.GmThreshold, 1);
                    Conflict(!effects.Any(f => f.Kind == "fire" && f.Subject == x.Subject && f.Active), "elemental-spread-subject-already-burning");
                    int? hits = Roll("Nearby item ignition resistance", Math.Max(0, x.Armor + x.FireResistance - 2), x.GmThreshold, x.Hits,
                        "SR5:171; open flame AP -2; threshold explicitly adjudicated by table");
                    bool ignited = hits.HasValue && hits.Value < x.GmThreshold;
                    summary = ignited ? $"{x.Subject}: ignited, initial fire DV 3." : $"{x.Subject}: resisted this declared ignition exposure.";
                    if (ignited)
                    {
                        var child = before with { EffectId = Digest(new { CastEventId = before.CastEventId, ParentEffectId = before.EffectId, SpreadEventId = e.EventId, Kind = "fire", Subject = x.Subject }), Subject = x.Subject, Revision = "",
                            Turn = 1, FireDamage = 3, TurnDamageRecorded = false };
                        spawned.Add(child with { Revision = Digest(child) });
                    }
                }
                break;
            }
            case SessionPlayElementalAction.IceCrossing:
            {
                Require(before.Kind == "ice" && c.Crossing is not null, "elemental-action-kind-mismatch");
                var x = c.Crossing!; Label(x.Actor); Rating(x.Agility, 1); Rating(x.Reaction, 1);
                Require(x.OrdinaryNoGlitch, "elemental-special-crossing-context-open");
                int? hits = Roll("Crossing Agility + Reaction, with wounds", Math.Max(0, x.Agility + x.Reaction + Wounds(x.Condition)), before.CrossingThreshold, x.Hits, "SR5:169,293; SG:104");
                prone = hits.HasValue ? hits.Value < before.CrossingThreshold : null;
                summary = $"{x.Actor}: {(prone == true ? "falls prone" : "crosses without falling")}. Table result only.";
                break;
            }
            case SessionPlayElementalAction.IceVehicle:
            {
                Require(before.Kind == "ice" && c.Vehicle is not null, "elemental-action-kind-mismatch");
                var x = c.Vehicle!; Label(x.Vehicle); Label(x.GmDecision); crash = x.TouchesSheet;
                if (!x.TouchesSheet)
                {
                    Require(x.Test is null, "elemental-followup-contradictory-input");
                    summary = $"{x.Vehicle}: table declared no contact with the sheet.";
                    break;
                }
                Require(x.Test is not null, "elemental-vehicle-test-required");
                var test = x.Test!;
                if (c.RulesVersion == 1) Require(test.Control != SessionPlayVehicleControl.JumpedHot && test.JumpedHotSimBonus == 0,
                    "elemental-frozen-hot-sim-policy-unavailable");
                if (c.RulesVersion == 2) test = ApplyVehiclePolicy5400(test, c, authority);
                var basis = VehicleTestBasis5381(test, c.RulesVersion >= 1, c.VehiclePolicy);
                int? hits = null;
                if (test.Aware)
                {
                    hits = Roll("Immediate vehicle Crash Test", basis.Pool, basis.Threshold, test.Hits, "SR5:199–201,238,266,269,452; table difficulty/terrain; hits capped by Handling");
                    rolls[^1] = rolls[^1] with { Limit = basis.Limit };
                }
                else Require(test.Hits is null, "elemental-unaware-pilot-cannot-roll");
                bool success = hits.HasValue && Math.Min(hits.Value, basis.Limit) >= basis.Threshold;
                vehicleTest = new(basis.Pool, basis.Limit, basis.Threshold,
                    hits.HasValue ? Math.Min(hits.Value, basis.Limit) : null, test.Aware, success,
                    !success && test.CollisionOnFailure);
                summary = $"{x.Vehicle}: " + (!test.Aware ? "pilot unaware; no test possible. " : success ? "Crash Test succeeded. " : "Crash Test failed. ")
                    + (success ? "Control retained for this contact." : test.CollisionOnFailure ? "Collision recorded at the table. " + test.FailureDecision : "Uncontrolled; table records no collision yet. " + test.FailureDecision);
                if (c.RulesVersion >= 1 && !success && !awaitingHits)
                    spawned.Add(SeedIncident5393(before, e, x, vehicleTest));
                if (vehicleTest.Crashed)
                    open.Add("Collision damage and occupant Composure remain separate required resolutions (SR5:201–202); this record does not assert zero damage or apply any monitor change.");
                else if (!success) open.Add("Vehicle remains uncontrolled; the table must resolve subsequent movement, control and collision. This is not a safe-crossing result.");
                break;
            }
            case SessionPlayElementalAction.IceMelt:
            {
                Require(before.Kind == "ice" && c.Melt is not null, "elemental-action-kind-mismatch");
                var x = c.Melt!;
                Require(Enum.IsDefined(x.Temperature) && x.ElapsedMinutes > before.ElapsedMinutes && x.ElapsedMinutes <= 1000000, "elemental-ice-time-invalid");
                decimal rate;
                if (x.Temperature is SessionPlayIceTemperature.Room or SessionPlayIceTemperature.BelowFreezing)
                {
                    Require(x.TableRateSquareMetersPerMinute is null && x.GmDecision == "", "elemental-followup-contradictory-input");
                    rate = x.Temperature == SessionPlayIceTemperature.Room ? 1 : 0;
                }
                else
                {
                    Label(x.GmDecision);
                    Require(x.TableRateSquareMetersPerMinute is { } r && r > 0 && r <= 100000
                        && (x.Temperature == SessionPlayIceTemperature.Hotter ? r > 1 : r < 1), "elemental-ice-table-rate-required");
                    rate = x.TableRateSquareMetersPerMinute!.Value;
                }
                decimal area = Math.Max(0, before.RemainingSquareMeters - rate * (x.ElapsedMinutes - before.ElapsedMinutes));
                after = before with { ElapsedMinutes = x.ElapsedMinutes, RemainingSquareMeters = area, Active = area > 0 };
                summary = FormattableString.Invariant($"Ice remaining: {area:0.###} m² after {x.ElapsedMinutes} elapsed minutes; interval rate {rate} m²/min.");
                open.Add("The table locates the remaining ice; area reduction does not select a safe path or automatically move actors.");
                break;
            }
            case SessionPlayElementalAction.DangerousBreakage:
            {
                Require(before.Kind == "danger" && c.Danger is not null, "elemental-action-kind-mismatch");
                var x = c.Danger!;
                Require(Enum.IsDefined(x.Decision), "elemental-danger-decision-required");
                Label(x.Adjudicator); Label(x.AffectedActorsOrObjects); Label(x.Consequence);
                after = before with { Active = false };
                summary = $"Dangerous breakage adjudicated by {x.Adjudicator}: {x.Decision}; {x.Consequence}. Item remains destroyed.";
                open.Add("Recorded GM/table decision only. Any damage, inventory changes or secondary hazards still require their own explicit authorized actions.");
                break;
            }
        }
        if (c.RulesVersion == 2)
        {
            var policy = Policy5400(c, authority);
            string rule = $"Adopted vehicle policy v{policy.Version}: jumped hot-sim +{policy.JumpedHotSimBonus}; Armor equality {policy.ArmorEquality}.";
            for (int i = 0; i < rolls.Count; i++) rolls[i] = rolls[i] with { Rule = rolls[i].Rule + "; " + rule };
            summary += " " + rule;
        }
        if (awaitingHits) { Require(preview, "elemental-roll-results-required"); return (rolls, null); }
        // Every accepted action advances state, even a failed extinguish or a table-only decision.
        after = after with { Revision = Digest(new { before.Revision, e.EventId, Command = c }) };
        var outcome = new SessionPlayElementalOutcome(e.EventId, before.EffectId, before.CastEventId, before.Revision,
            after.Revision, c, summary, damage, prone, crash, open) { VehicleTest = vehicleTest, NonlivingFire = nonlivingFire, Incident = after.Incident };
        effects[index] = after; effects.AddRange(spawned); history.Add(outcome);
        return (rolls, outcome);

        void Turn(int turn) => Conflict(turn == before.Turn, "elemental-fire-turn-changed");
        int? Roll(string label, int pool, int? threshold, int? hits, string rule)
        {
            rolls.Add(new(label, pool, threshold, rule));
            if (hits is null) awaitingHits = true;
            else Require(hits >= 0 && hits <= pool, "elemental-hits-outside-core-pool");
            return hits;
        }
    }
    private static int OngoingFirePool5381(int resistanceAttribute, int armor, int fireResistance)
    {
        Rating(armor); Rating(fireResistance);
        return checked(resistanceAttribute + Math.Max(0, armor + fireResistance - 2));
    }
    private static int Wounds(SessionPlayFollowupCondition? condition)
    {
        Require(condition is not null, "elemental-acting-condition-required");
        Rating(condition.PhysicalMaximum, 1); Rating(condition.StunMaximum, 1);
        Require(condition.PhysicalDamage >= 0 && condition.PhysicalDamage < condition.PhysicalMaximum
            && condition.StunDamage >= 0 && condition.StunDamage < condition.StunMaximum,
            "elemental-acting-condition-incapacitated-or-invalid");
        // Standard table-declared actors only. Reuse the same condition kernel as the owned overlay.
        return new CharacterSheetConditionValues(condition.PhysicalMaximum, condition.StunMaximum, 3, 0, 0, 0, 0, 0, 0)
            .WithDamage(condition.PhysicalDamage, condition.StunDamage).WoundModifier;
    }
    private static void Rating(int value, int minimum = 0) => Require(value >= minimum && value <= 100, "elemental-rating-outside-bounds");
    private static void Label(string? value) => Require(value is { Length: > 0 and <= 160 } && value.Trim() == value
        && !value.Any(char.IsControl), "elemental-table-description-required");
}
