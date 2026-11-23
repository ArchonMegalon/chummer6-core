using Chummer.Application.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

internal static class SessionPlayCasting
{
    internal static SessionPlayCastingAuthority Bind(SessionPlayCastingAuthority authority, SessionPlayBinding binding, string custody)
    {
        var bound = authority with { Digest = "", Choices = authority.Choices.Select(o => o with {
            CapabilityId = Digest(new { binding, custody, o.CapabilityId }) }).ToArray(),
            Indirect = authority.Indirect is not { } extra ? null : extra with { Choices = extra.Choices.Select(o => o with {
                CapabilityId = Digest(new { binding, custody, o.CapabilityId }) }).ToArray(),
                Elemental = extra.Elemental is not { } elements ? null : elements with { Choices = elements.Choices.Select(o => o with {
                    CapabilityId = Digest(new { binding, custody, o.CapabilityId }) }).ToArray() } } };
        return bound with { Digest = Digest(bound) };
    }
    internal static SessionPlayCastingProjection Project(SessionPlayCastingAuthority authority,
        IReadOnlyList<SessionPlayCastOutcome> history, int wound, int sustaining)
    {
        Conflict(authority.Digest == Digest(authority with { Digest = "" }), "casting-authority-invalid");
        return new(authority.Digest, authority.Choices.Concat(authority.Indirect?.Choices ?? []).Concat(authority.Indirect?.Elemental?.Choices ?? []).Select(c => new SessionPlayCastingOption(c,
            Math.Max(0, checked(c.RawCastingPool + wound + sustaining)))).ToArray(), RemainingBlockers(authority), history.ToArray());
    }
    private static IReadOnlyList<string> RemainingBlockers(SessionPlayCastingAuthority authority)
    {
        if (authority.Indirect is null) return authority.Blockers;
        var replaced = authority.Indirect.ReplacedBlockers.Concat(authority.Indirect.Elemental?.ReplacedBlockers ?? []).GroupBy(b => b, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var result = new List<string>();
        foreach (string blocker in authority.Blockers)
        {
            if (replaced.TryGetValue(blocker, out int count) && count > 0) replaced[blocker] = count - 1;
            else result.Add(blocker);
        }
        return result;
    }
    internal static SessionPlayCastOutcome Apply(SessionPlayAuthority authority, IReadOnlyList<SessionPlayMechanicalEffect>? effects,
        IReadOnlyDictionary<string, int> values, SessionEventEnvelope e, IEnumerable<SessionEventEnvelope> prefix, int wound)
    {
        Require(authority.CastingAuthority is not null && authority.StandardWoundRules, "casting-authority-unavailable");
        var casting = authority.CastingAuthority!;
        int sustaining = authority.SustainingAuthority is { } sustained
            ? SessionPlaySustainingEffects.Project(sustained, effects!).DicePoolPenalty : 0;
        var projection = Project(casting, [], wound, sustaining);
        var (option, force) = ReadRequest(casting, projection, e, prefix);
        var choice = option.Choice;
        var rolls = e.Payload["rolls"];
        Require(rolls.Kind == RulesetCapabilityValueKinds.List && rolls.Items?.Count == 4
            && rolls.StringValue is null && rolls.IntegerValue is null && rolls.BooleanValue is null
            && rolls.NumberValue is null && rolls.DecimalValue is null && rolls.Properties is null, "casting-manual-roll-shape");
        // Explicit player declarations: target WIL/BOD pool, raw casting hits,
        // target resistance hits, caster drain-resistance hits. No random calls.
        int opposition = Integer(rolls.Items![0]), hits = Integer(rolls.Items[1]),
            opposed = Integer(rolls.Items[2]), resisted = Integer(rolls.Items[3]);
        Require((choice.IndirectKind is null ? opposition is >= 1 and <= 100 : opposition == 0 && opposed == 0) && hits >= 0 && hits <= option.CastingPool
            && opposed >= 0 && opposed <= opposition && resisted >= 0 && resisted <= choice.DrainResistancePool,
            "casting-manual-hits-outside-pool");
        CheckCondition(authority, values);
        int limited = CharacterDirectSpellRules5296.LimitedHits(hits, force);
        SessionPlayIndirectOutcome? indirect = choice.IndirectKind is null ? null
            : SessionPlayIndirect5338.Resolve(choice, force, limited, e.Payload["indirect"]);
        int drain = CharacterDirectSpellRules5296.DrainValue(force, choice.DrainAdjustment);
        int boxes = CharacterDirectSpellRules5296.UnresistedDrain(drain, resisted);
        string drainTrack = CharacterDirectSpellRules5296.PhysicalDrain(limited, choice.Magic) ? "physical" : "stun";
        int before = values[drainTrack], after = checked(before + boxes);
        Require(after <= authority.Trackers.Single(t => t.TrackerId == drainTrack).MaximumValue,
            "casting-drain-overflow-conversion-unavailable");
        int net = CharacterDirectSpellRules5296.NetHits(limited, opposed);
        if (indirect is not null)
        {
            var target = indirect.Kind == "indirect-single" ? indirect.Targets.Single() : null;
            opposition = target?.DefensePool ?? 0; opposed = target?.DefenseHits ?? 0;
            net = target?.NetHits ?? Math.Max(0, limited - 3);
        }
        return new(e.EventId, choice.SpellId, choice.DisplayName, force, option.CastingPool, hits, limited,
            opposition, opposed, net, choice.DamageType,
            drain, choice.DrainResistancePool, resisted, drainTrack, boxes, before, after,
            choice.Element == "cold" ? "player-entered hits; ordinary casting/defense/soak/drain; cold item outcomes separately declared; no generated dice; target outcome not applied"
                : "player-entered hits; ordinary action and no glitches declared; no generated dice; target outcome not applied") { Indirect = indirect };
    }
    internal static bool IsSetup(SessionEventEnvelope e)
        => e.Payload.TryGetValue("rolls", out var rolls) && rolls.Items?.Count == 0;

    internal static SessionPlayCastingSetup Setup(SessionPlayAuthority authority, SessionOverlaySnapshot snapshot, SessionEventEnvelope e)
    {
        Require(authority.CastingAuthority is not null && snapshot.Play?.Casting is not null, "casting-authority-unavailable");
        CheckCondition(authority, snapshot.Trackers.ToDictionary(t => t.Definition.TrackerId, t => t.CurrentValue));
        var (option, force) = ReadRequest(authority.CastingAuthority!, snapshot.Play!.Casting!, e, snapshot.Play.History);
        var rolls = e.Payload["rolls"];
        Require(rolls.Kind == RulesetCapabilityValueKinds.List && rolls.Items?.Count == 0
            && rolls.StringValue is null && rolls.IntegerValue is null && rolls.BooleanValue is null
            && rolls.NumberValue is null && rolls.DecimalValue is null && rolls.Properties is null, "casting-setup-shape");
        if (option.Choice.IndirectKind is not null)
            Require(e.Payload["indirect"] == new RulesetCapabilityValue(RulesetCapabilityValueKinds.Boolean, BooleanValue: false),
                "casting-setup-shape");
        return Describe(option, force);
    }
    internal static SessionPlayCastingSetup Describe(SessionPlayCastingOption option, int force)
        => new(option.Choice.CapabilityId, option.Choice.DisplayName, force, option.Choice.Magic, option.CastingPool,
            force, CharacterDirectSpellRules5296.DrainValue(force, option.Choice.DrainAdjustment),
            option.Choice.DrainResistancePool, option.Choice.DefenseAttribute) { IndirectKind = option.Choice.IndirectKind, Element = option.Choice.Element };

    private static (SessionPlayCastingOption Option, int Force) ReadRequest(SessionPlayCastingAuthority casting,
        SessionPlayCastingProjection projection, SessionEventEnvelope e, IEnumerable<SessionEventEnvelope> prefix)
    {
        string[] keys = ["authority", "cursor", "capability", "force", "rolls", "ordinary"];
        Require(keys.All(e.Payload.ContainsKey), "casting-payload-shape");
        Conflict(Text(e, "authority") == casting.Digest && Text(e, "cursor") == Cursor(prefix), "casting-review-changed");
        var option = projection.Choices.SingleOrDefault(c => c.Choice.CapabilityId == Text(e, "capability"));
        Require(option is not null, "casting-choice-invalid");
        var choice = option!.Choice;
        bool indirect = choice.IndirectKind is not null;
        Require(e.EventType == (indirect ? SessionPlayEventTypes.IndirectSpellCast : SessionPlayEventTypes.SpellCast)
            && e.Payload.Count == keys.Length + (indirect ? 1 : 0)
            && (!indirect || e.Payload.ContainsKey("indirect")), "casting-payload-shape");
        var ordinary = e.Payload["ordinary"];
        Require(ordinary.Kind == RulesetCapabilityValueKinds.Boolean && ordinary.BooleanValue == true
            && ordinary.StringValue is null && ordinary.IntegerValue is null && ordinary.NumberValue is null
            && ordinary.DecimalValue is null && ordinary.Items is null && ordinary.Properties is null,
            "casting-ordinary-no-glitch-declaration-required");
        int force = Integer(e.Payload["force"]);
        Require(force >= 1 && force <= 2 * choice.Magic && force <= 200, "casting-force-outside-magic-bound");
        Require(option.CastingPool is >= 1 and <= 200 && choice.DrainResistancePool is >= 0 and <= 200,
            "casting-pool-outside-supported-bound");
        return (option, force);
    }
    private static void CheckCondition(SessionPlayAuthority authority, IReadOnlyDictionary<string, int> values)
    {
        foreach (string track in new[] { "physical", "stun" })
        {
            var definition = authority.Trackers.SingleOrDefault(t => t.TrackerId == track);
            Require(definition?.MaximumValue is > 0 && values.TryGetValue(track, out int damage)
                && damage >= 0 && damage < definition.MaximumValue, "casting-condition-incapacitated-or-unavailable");
        }
    }
    private static string Text(SessionEventEnvelope e, string key)
    {
        var v = e.Payload[key];
        Require(v.Kind == RulesetCapabilityValueKinds.String && v.StringValue is not null && v.IntegerValue is null
            && v.BooleanValue is null && v.NumberValue is null && v.DecimalValue is null && v.Items is null && v.Properties is null,
            "casting-string-required");
        return v.StringValue!;
    }
    private static int Integer(RulesetCapabilityValue v)
    {
        Require(v.Kind == RulesetCapabilityValueKinds.Integer && v.IntegerValue is not null && v.StringValue is null
            && v.BooleanValue is null && v.NumberValue is null && v.DecimalValue is null && v.Items is null && v.Properties is null,
            "casting-integer-required");
        return checked((int)v.IntegerValue!.Value);
    }
}

// Publisher-authored SR5 pp. 173, 181-182, 283. Only ordinary non-elemental
// living-target resistance. No NPC authority, target monitor or generated rolls.
internal static class SessionPlayIndirect5338
{
    internal static SessionPlayIndirectOutcome Resolve(SessionPlayCastingChoice choice, int force, int hits, RulesetCapabilityValue value)
    {
        var fields = List(value, 4, 4);
        string aim = Label(fields[0]);
        Require(fields[1] == new RulesetCapabilityValue(RulesetCapabilityValueKinds.Boolean, BooleanValue: true),
            "indirect-ordinary-targets-required");
        bool area = choice.IndirectKind == "indirect-area";
        Require(area || choice.IndirectKind == "indirect-single", "indirect-kind-invalid");
        var scatter = List(fields[2], 0, 4).Select(Number).ToArray();
        bool scatters = area && hits < 3;
        Require(scatters ? scatter.Length == 4 && scatter.All(d => d is >= 1 and <= 6) : scatter.Length == 0,
            "indirect-scatter-required");
        int meters = scatters ? Math.Max(0, scatter[0] + scatter[1] - hits) : 0;
        int? direction = scatters ? scatter[2] + scatter[3] : null;
        int areaNet = Math.Max(0, hits - 3), areaDamage = force + areaNet;
        var targets = List(fields[3], area ? 0 : 1, area ? 12 : 1);
        var results = new List<SessionPlayIndirectTargetResult>();
        foreach (var target in targets)
        {
            var t = List(target, choice.Element is null ? 7 : 8, choice.Element is null ? 7 : 8);
            string label = Label(t[0]);
            Require(!results.Any(r => r.Label == label), "indirect-target-duplicate");
            int reaction = Number(t[1]), intuition = Number(t[2]), defenseHits = Number(t[3]),
                body = Number(t[4]), armor = Number(t[5]), soakHits = Number(t[6]);
            Require(body is >= 1 and <= 100 && armor is >= 0 and <= 100, "indirect-resistance-input-invalid");
            var elemental = choice.Element is null ? null : ReadElemental5361(choice.Element, t[7]);
            // SR5 Core p. 170: only an existing electrical penalty affects this defense, never soak.
            int defensePool = reaction + intuition - (!area && elemental?.ElectricalTurnsRemaining > 0 ? 1 : 0);
            Require(area ? reaction == 0 && intuition == 0 && defenseHits == 0
                : reaction is >= 1 and <= 50 && intuition is >= 1 and <= 50
                    && defenseHits >= 0 && defenseHits <= defensePool, "indirect-defense-input-invalid");
            int net = area ? areaNet : Math.Max(0, hits - defenseHits);
            int damage = area ? areaDamage : net > 0 ? force + net : 0;
            int adjustedArmor = Math.Max(0, armor + (elemental?.ProtectionRating ?? 0) - force), pool = body + adjustedArmor;
            Require(soakHits >= 0 && soakHits <= pool && (damage > 0 || soakHits == 0), "indirect-soak-hits-invalid");
            string type = choice.DamageType == "P" && damage > 0 && damage < adjustedArmor ? "S" : choice.DamageType;
            results.Add(new(label, area ? "area-hit" : hits == defenseHits ? "grazing-no-damage" : hits < defenseHits ? "miss" : "hit",
                defensePool, defenseHits, net, damage, adjustedArmor, pool,
                soakHits, type, Math.Max(0, damage - soakHits)) { Elemental = elemental is null ? null
                    : ResolveElemental5361(choice.Element!, elemental, force, net, area || hits >= defenseHits,
                        Math.Max(0, damage - soakHits), armor) });
        }
        return new(choice.IndirectKind!, aim, area ? areaDamage : results[0].DamageValue, -force,
            area ? force : 0, meters, direction, results.ToArray()) {
                IceSheet = choice.Element == "cold" && area ? new(hits, force, aim, [
                    new("final-ice-area", "table geometry", "Place Ice Sheet in the final scattered area, not the original aim point."),
                    new("crossing-actors", "actor labels + Agility + Reaction + raw hits", "For each character crossing the ice, compare hits with the displayed threshold; failure means prone. No actor is moved or marked prone here."),
                    new("vehicles", "vehicle identity + Crash Test inputs", "The table must resolve an immediate Crash Test for affected vehicles; this living-target cast does not resolve it."),
                    new("temperature-and-time", "ambient temperature + elapsed minutes + remaining square meters", "Track melting at the table: room temperature removes 1 square meter per minute; other temperatures need the GM's rate, and below freezing the sheet does not melt.")]) : null };

    }
    private static SessionPlayElementalTargetInput ReadElemental5361(string element, RulesetCapabilityValue value)
    {
        Require(element is "acid" or "fire" or "electricity" or "cold", "elemental-source-unavailable");
        var fields = List(value, element == "cold" ? 6 : 5, element == "cold" ? 6 : 5);
        int protection = Number(fields[0]);
        Require(protection <= 100 && (element != "acid" || protection == 0), "elemental-protection-invalid");
        Require(fields[1] == new RulesetCapabilityValue(RulesetCapabilityValueKinds.Boolean, BooleanValue: true),
            "elemental-equipment-declaration-required");
        var items = List(fields[2], 0, element == "fire" ? 12 : element == "acid" ? 1 : 0).Select(v => {
            var i = List(v, 5, 5);
            return new SessionPlayElementalItemInput(Label(i[0]), Number(i[1]), Number(i[2]), Number(i[3]), Number(i[4]));
        }).ToArray();
        Require(items.Select(i => i.Label).Distinct(StringComparer.Ordinal).Count() == items.Length
            && items.All(i => i.Armor <= 100 && i.FireResistance <= 100 && i.ExistingFireDamage <= 100), "elemental-item-invalid");
        var electric = List(fields[3], element == "electricity" ? 2 : 0, element == "electricity" ? 2 : 0);
        int? turns = electric.Count == 0 ? null : Number(electric[0]);
        int? initiative = null;
        if (electric.Count != 0)
        {
            var v = electric[1];
            Require(v.Kind == RulesetCapabilityValueKinds.Integer && v.IntegerValue is >= -200 and <= 200
                && v.StringValue is null && v.BooleanValue is null && v.NumberValue is null && v.DecimalValue is null
                && v.Items is null && v.Properties is null, "elemental-initiative-required");
            initiative = (int)v.IntegerValue!.Value;
        }
        Require(fields[4] == new RulesetCapabilityValue(RulesetCapabilityValueKinds.String, StringValue: element == "acid" ? "none" : "")
            || element == "acid" && fields[4] == new RulesetCapabilityValue(RulesetCapabilityValueKinds.String, StringValue: "light-smoke"),
            "elemental-acid-smoke-decision-required");
        IReadOnlyList<SessionPlayColdItemInput>? cold = null;
        if (element == "cold")
        {
            cold = List(fields[5], 0, 12).Select(v => {
                var i = List(v, 4, 4); string outcome = Label(i[3]);
                int armor = Number(i[1]), hits = Number(i[2]);
                // SR5 Core pp. 44-45, 170: more than half the dice must be ones; no-hit glitches are critical.
                // Ones cannot also be hits, and a zero-die pool cannot produce a glitch.
                Require(armor <= 100 && hits <= armor && (outcome is "ordinary" or "glitch" or "critical-glitch")
                    && (outcome != "glitch" || hits > 0 && hits <= armor - (armor / 2 + 1))
                    && (outcome != "critical-glitch" || armor > 0 && hits == 0), "elemental-cold-test-required");
                return new SessionPlayColdItemInput(Label(i[0]), armor, hits, outcome);
            }).ToArray();
            Require(cold.Select(i => i.Label).Distinct(StringComparer.Ordinal).Count() == cold.Count, "elemental-item-invalid");
        }
        return new(protection, true, items, turns, initiative, element == "acid" ? fields[4].StringValue : null) { ColdItems = cold };
    }

    // SR5 Core pp. 169-171, 283-284, 437-438. These are TABLE outcomes.
    // This reducer returns no tracker/equipment mutation for any target.
    private static SessionPlayElementalTargetResult ResolveElemental5361(string element, SessionPlayElementalTargetInput input,
        int force, int net, bool contact, int boxes, int armor)
    {
        var items = new List<SessionPlayElementalItemResult>();
        var follow = new List<string> { "Table action required: apply target damage, equipment changes and secondary effects with that actor's authority. This cast saves only caster drain." };
        SessionPlayElectricalResult? electrical = null;
        if (element == "fire")
        {
            Require(!contact || input.Items.Count > 0, "elemental-fire-subjects-required");
            foreach (var item in input.Items)
            {
                int pool = Math.Max(0, item.Armor + item.FireResistance - force);
                Require(item.IgnitionHits <= pool && (contact && item.ExistingFireDamage == 0 || item.IgnitionHits == 0),
                    "elemental-ignition-hits-invalid");
                bool burning = item.ExistingFireDamage > 0 || contact && item.IgnitionHits < net;
                int? fire = burning ? item.ExistingFireDamage > 0 ? item.ExistingFireDamage : 3 : null;
                items.Add(new(item.Label, item.ExistingFireDamage > 0 ? "already-burning-no-reset"
                    : !contact ? "not-contacted" : burning ? "ignited" : "not-ignited",
                    item.Armor, item.Armor, contact && item.ExistingFireDamage == 0 ? pool : null,
                    contact && item.ExistingFireDamage == 0 ? item.IgnitionHits : null,
                    contact && item.ExistingFireDamage == 0 ? net : null, fire, fire.HasValue ? fire.Value + 1 : null));
            }
            if (items.Any(i => i.FireDamage.HasValue))
                follow.Add("Burning remains a table effect: at the end of this Combat Turn resolve the displayed fire DV; the next-turn DV assumes no extinguishing. To fight it, supply the acting character's Agility, Intuition and extinguishing hits at the table. Resolve resistance, nearby spread and subsequent turns separately; nothing is automatically ticked or reset.");
            follow.Add("Ignition subjects must include the worn armor or exposed clothing/body. Test accessories separately only when the GM calls for it; do not add accessory Armor to the worn item's ignition pool. Environmental spread still needs the GM's affected-item declarations.");
        }
        else if (element == "acid")
        {
            Require(input.ProtectionRating == 0 && input.Items.All(i => i.FireResistance == 0 && i.IgnitionHits == 0 && i.ExistingFireDamage == 0
                && i.Armor > 0 && i.Armor <= armor) && (armor == 0 ? input.Items.Count == 0 : input.Items.Count == 1),
                "elemental-acid-armor-required");
            foreach (var item in input.Items)
                items.Add(new(item.Label, contact ? "corroded" : "not-contacted", item.Armor,
                    contact ? item.Armor - 1 : item.Armor, null, null, null, null, null));
            Require(contact || input.AcidSmoke == "none", "elemental-acid-smoke-decision-required");
            follow.Add("This instantaneous spell's acid dissipates; no recurring acid damage or repeated corrosion is scheduled. The declared worn-first armor loss persists at the table, including when damage was soaked. Chemical Protection is not an acid resistance bonus in the SR5 Core armor-modification entry.");
            if (contact && input.AcidSmoke == "light-smoke")
                follow.Add("The GM declared Light Smoke. Resolve visibility for subsequent actions with the affected actors' vision and scene inputs; this cast does not alter later pools.");
        }
        else if (element == "cold")
        {
            Require(input.ColdItems is not null && (armor == 0 || input.ColdItems.Count > 0), "elemental-cold-test-required");
            foreach (var item in input.ColdItems!)
            {
                Require(contact || item.Hits == 0 && item.RollOutcome == "ordinary", "elemental-cold-test-required");
                bool broken = contact && (item.Hits == 0 || item.RollOutcome != "ordinary");
                string result = !contact ? "not-contacted" : item.RollOutcome == "critical-glitch" ? "dangerously-destroyed"
                    : item.RollOutcome == "glitch" ? "irreparably-destroyed" : broken ? "brittle-repairable" : "intact";
                items.Add(new(item.Label, result, item.Armor, broken ? 0 : item.Armor,
                    contact ? item.Armor : null, contact ? item.Hits : null, contact ? 1 : null, null, null));
            }
            follow.Add("Cold item tests use each declared item's Armor, without spell AP or Body. Insulation protects the wearer's damage resistance. The table must apply broken/destroyed equipment and any dangerous critical-glitch consequence; supply the affected item's identity and the GM's consequence before using it again. Other frozen liquids/lubricants and nonstandard protections require their own item context.");
        }
        else
        {
            int turns = input.ElectricalTurnsRemaining!.Value;
            bool injured = boxes > 0, newEffect = injured && turns == 0;
            int duration = injured ? turns + 1 : turns;
            electrical = new(duration > 0 ? -1 : 0, duration,
                newEffect && input.InitiativeScore > 0 ? 5 : 0, newEffect && input.InitiativeScore <= 0 ? 5 : 0);
            follow.Add("Use the displayed electricity penalty on actions and defense, never damage resistance. Existing electricity penalties do not stack; a damaging hit extends their duration. The table must apply any displayed initiative loss now or at the next turn and handle action loss. No target initiative or timer is written.");
            follow.Add("This is a living-target result. Directly hit electronics/drones require their own physical damage and Matrix-damage review; vehicles and unusual conductivity require separate target context. Do not infer equipment immunity from the living target's soak.");
        }
        return new(element, input.ProtectionRating, items, electrical, contact ? input.AcidSmoke : null, follow);
    }

    private static IReadOnlyList<RulesetCapabilityValue> List(RulesetCapabilityValue v, int min, int max)
    {
        Require(v.Kind == RulesetCapabilityValueKinds.List && v.Items is not null && v.Items.Count >= min && v.Items.Count <= max
            && v.StringValue is null && v.IntegerValue is null && v.BooleanValue is null && v.NumberValue is null
            && v.DecimalValue is null && v.Properties is null, "indirect-input-shape");
        return v.Items!;
    }
    private static int Number(RulesetCapabilityValue v)
    {
        Require(v.Kind == RulesetCapabilityValueKinds.Integer && v.IntegerValue is >= 0 and <= 200
            && v.StringValue is null && v.BooleanValue is null && v.NumberValue is null && v.DecimalValue is null
            && v.Items is null && v.Properties is null, "indirect-integer-required");
        return (int)v.IntegerValue!.Value;
    }
    private static string Label(RulesetCapabilityValue v)
    {
        Require(v.Kind == RulesetCapabilityValueKinds.String && v.StringValue is { Length: > 0 and <= 120 }
            && v.StringValue.Trim() == v.StringValue && !v.StringValue.Any(char.IsControl)
            && v.IntegerValue is null && v.BooleanValue is null && v.NumberValue is null && v.DecimalValue is null
            && v.Items is null && v.Properties is null, "indirect-label-required");
        return v.StringValue!;
    }
}
