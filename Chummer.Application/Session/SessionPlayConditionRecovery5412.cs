using System.Text.Json;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

/// <summary>Ephemeral replay state, seeded only by a frozen initial authority.
/// These keys are not tracker definitions and cannot be edited by tracker events.</summary>
public static class SessionPlayConditionRecovery5412
{
    private const string Overflow = "condition5412.overflow", Stabilized = "condition5412.stabilized", Carry = "condition5425.excess-stun";

    public static void Seed(SessionPlayAuthority authority, IDictionary<string, int> values)
    {
        if (authority.InitialCondition5412 is not { } x) return; // Old origins stay byte-compatible.
        SessionPlayInitialConditionSource5412.Validate(authority);
        values.Add(Overflow, Math.Max(0, checked(x.PhysicalFilled - x.PhysicalMaximum)));
        values.Add(Carry, x.CanonicalCondition?.ExcessStunRemainder ?? 0);
        values.Add(Stabilized, x.CanonicalCondition?.Stabilized is not { } stable ? -1 : stable ? 1 : 0);
    }

    public static SessionPlayConditionProjection5412? Project(SessionPlayAuthority authority, IDictionary<string, int> values)
    {
        if (authority.InitialCondition5412 is not { } x) return null;
        int overflow = values[Overflow], p = values["physical"], s = values["stun"];
        int limit = checked(x.Body + x.OverflowModifier);
        bool? stabilized = values[Stabilized] switch { -1 => null, 0 => false, 1 => true,
            _ => throw new SessionPlayException(SessionPlayFailures.Invalid("condition-stabilization-invalid")) };
        var state = SessionPlayElementalFollowups.ConditionState5412(x.PhysicalMaximum, x.StunMaximum,
            p, s, overflow, x.Body, limit, stabilized, values[Carry]);
        return new(x.Digest, state, Digest(new { x.Digest, State = state }));
    }

    public static SessionPlayCrashCondition CurrentCrash(SessionPlayAuthority authority, IDictionary<string, int> values)
    {
        var c = Project(authority, values)?.Current;
        Require(c is not null && c.Health is not null, "condition-stabilization-table-fact-required");
        // With no overflow/death the bool has no treatment meaning. No source fact is written.
        return new(c!.PhysicalMaximum, c.StunMaximum, c.Physical, c.Stun, c.Overflow, c.Body,
            c.OverflowLimit, c.Stabilized ?? false, c.Health!.Value) { ExcessStunRemainder = c.ExcessStunRemainder };
    }

    public static void Write(SessionPlayAuthority authority, IDictionary<string, int> values, SessionPlayCrashCondition after)
    {
        if (authority.InitialCondition5412 is null) return;
        Conflict(Project(authority, values)!.Current.Health != SessionPlayCrashHealth.Dead
            || after.Health == SessionPlayCrashHealth.Dead, "condition-dead-actor-cannot-recover");
        values[Carry] = after.ExcessStunRemainder;
        values["physical"] = after.Physical; values["stun"] = after.Stun;
        values[Overflow] = after.Overflow; values[Stabilized] = after.Stabilized ? 1 : 0;
    }

    // Rehydrate only a checked replay projection, never today's initial damage or an older crash receipt.
    internal static Dictionary<string, int> ReplayValues(SessionPlayAuthority authority, SessionOverlaySnapshot snapshot)
    {
        var values = snapshot.Trackers.ToDictionary(t => t.Definition.TrackerId, t => t.CurrentValue);
        if (authority.InitialCondition5412 is null) return values;
        var projection = snapshot.Play?.Condition5412;
        Conflict(projection is not null && projection.InitialDigest == authority.InitialCondition5412.Digest,
            "condition-replay-source-changed");
        var c = projection!.Current;
        values[Overflow] = c.Overflow; values[Stabilized] = c.Stabilized is null ? -1 : c.Stabilized.Value ? 1 : 0;
        values[Carry] = c.ExcessStunRemainder;
        Conflict(Project(authority, values) == projection, "condition-replay-state-changed");
        return values;
    }

    internal static void ApplyNaturalRecovery(SessionPlayAuthority authority, IDictionary<string, int> values,
        SessionNaturalRecoveryCheckpoint checkpoint)
    {
        Conflict(Project(authority, values)?.Current == checkpoint.Before, "recovery-checkpoint-preimage-divergence");
        var c = checkpoint.After;
        Conflict(checkpoint.Before.Health != SessionPlayCrashHealth.Dead && checkpoint.Before.Overflow == 0 && c.Overflow == 0
            && c.Physical <= checkpoint.Before.Physical && c.Stun <= checkpoint.Before.Stun, "recovery-checkpoint-invalid");
        values["physical"] = c.Physical; values["stun"] = c.Stun;
        values[Overflow] = c.Overflow; values[Carry] = c.ExcessStunRemainder;
        values[Stabilized] = c.Stabilized is null ? -1 : c.Stabilized.Value ? 1 : 0;
        Conflict(Project(authority, values)?.Current == c, "recovery-checkpoint-condition-divergence");
    }

    internal static void ObserveStunChange(SessionPlayAuthority authority, IDictionary<string, int> values, int before)
    {
        if (authority.InitialCondition5412 is not null && values["stun"] < before) values[Carry] = 0;
    }

    public static void Guard(SessionPlayAuthority authority, IDictionary<string, int> values, SessionEventEnvelope e)
    {
        if (Project(authority, values)?.Current is not { } current) return;
        if ((e.EventType is SessionEventTypes.TrackerIncrement or SessionEventTypes.TrackerDecrement)
            && e.Payload.TryGetValue("trackerId", out var target) && (target.StringValue is "physical" or "stun"))
            Require(current.Overflow == 0 && current.Health != SessionPlayCrashHealth.Dead,
                "condition-overflow-requires-typed-recovery");
        if (e.EventType is SessionPlayEventTypes.InitiativeRoll or SessionPlayEventTypes.InitiativePass
            or SessionPlayEventTypes.SpellCast or SessionPlayEventTypes.IndirectSpellCast or SessionPlayEventTypes.SustainingApply
            or SessionPlayEventTypes.WeaponFire or SessionPlayEventTypes.WeaponReload or SessionPlayEventTypes.WeaponUnload)
            Require(current.Health == SessionPlayCrashHealth.Conscious, "condition-incapacitated-owned-actor-cannot-act");
    }

    public static IReadOnlyDictionary<string, RulesetCapabilityValue> Encode(SessionPlayConditionCommand5412 command)
        => new Dictionary<string, RulesetCapabilityValue> { ["condition"] = new(RulesetCapabilityValueKinds.String,
            StringValue: JsonSerializer.Serialize(command, Json)) };

    public static SessionPlayConditionCommand5412 Decode(SessionEventEnvelope e)
    {
        Require(e.EventType == SessionPlayConditionCommand5412.EventType && e.Payload.Count == 1
            && e.Payload.ContainsKey("condition"), "condition-event-shape");
        var v = e.Payload["condition"];
        Require(v is not null && v.Kind == RulesetCapabilityValueKinds.String && v.StringValue is { Length: > 0 and <= 2048 }
            && v.IntegerValue is null && v.BooleanValue is null && v.NumberValue is null && v.DecimalValue is null
            && v.Items is null && v.Properties is null, "condition-event-shape");
        SessionPlayConditionCommand5412? c;
        try { c = JsonSerializer.Deserialize<SessionPlayConditionCommand5412>(v.StringValue!, Json); }
        catch (JsonException) { throw new SessionPlayException(SessionPlayFailures.Invalid("condition-event-shape")); }
        Require(c is not null && JsonSerializer.Serialize(c, Json) == v.StringValue, "condition-event-shape");
        return c!;
    }

    public static void Apply(SessionPlayAuthority authority, IDictionary<string, int> values,
        SessionEventEnvelope e, IEnumerable<SessionEventEnvelope> prefix)
    {
        var c = Decode(e);
        var before = Project(authority, values);
        Require(before is not null, "condition-new-origin-required");
        Conflict(c.InitialDigest == before!.InitialDigest && c.ExpectedRevision == before.Revision
            && c.Cursor == Cursor(prefix), "condition-review-changed");
        Conflict(!prefix.Where(x => x.EventType == SessionPlayConditionCommand5412.EventType)
            .Any(x => Decode(x).TableActionId == c.TableActionId), "condition-table-action-already-recorded");
        var after = Preview(before, c);
        values["physical"] = after.Physical; values["stun"] = after.Stun;
        values[Carry] = after.ExcessStunRemainder;
        values[Overflow] = after.Overflow; values[Stabilized] = after.Stabilized is null ? -1 : after.Stabilized.Value ? 1 : 0;
    }

    // Pure bounded Core review, shared by the native confirmation and admitted replay.
    // Binding/current-source/cursor/event-order authority is checked separately by Apply.
    public static SessionPlayConditionState5412 Preview(SessionPlayConditionProjection5412 before, SessionPlayConditionCommand5412 c)
    {
        Conflict(c.InitialDigest == before.InitialDigest && c.ExpectedRevision == before.Revision, "condition-review-changed");
        Require(JsonSerializer.Serialize(c, Json).Length <= 2048, "condition-receipt-too-large");
        Require(c.ExplicitlyApplyOwned && !string.IsNullOrWhiteSpace(c.TableActionId)
            && !string.IsNullOrWhiteSpace(c.Adjudicator) && !string.IsNullOrWhiteSpace(c.Evidence)
            && c.TableActionId.Length <= 128 && c.Adjudicator.Length <= 128 && c.Evidence.Length <= 512,
            "condition-explicit-table-receipt-required");
        Require(c.ObservedStabilized.HasValue != (c.Treatment is not null), "condition-choose-fact-or-treatment");
        Conflict(before.Current.Health != SessionPlayCrashHealth.Dead, "condition-dead-actor-cannot-recover");
        if (c.ObservedStabilized is { } fact)
        {
            Require(before.Current.Overflow > 0 && before.Current.Stabilized is null, "condition-fact-already-known-or-inapplicable");
            var observed = before.Current;
            return SessionPlayElementalFollowups.ConditionState5412(observed.PhysicalMaximum, observed.StunMaximum,
                observed.Physical, observed.Stun, observed.Overflow, observed.Body, observed.OverflowLimit, fact, observed.ExcessStunRemainder);
        }
        var input = c.Treatment!;
        var v = before.Current;
        Require(v.Health is not null, "condition-stabilization-table-fact-required");
        var prior = new SessionPlayCrashCondition(v.PhysicalMaximum, v.StunMaximum, v.Physical, v.Stun,
            v.Overflow, v.Body, v.OverflowLimit, v.Stabilized ?? false, v.Health!.Value) { ExcessStunRemainder = v.ExcessStunRemainder };
        Require(input.ExplicitlyApplyOwned && input.Adjudicator == c.Adjudicator && input.Evidence == c.Evidence,
            "condition-treatment-confirmation-required");
        Conflict(input.ExpectedPhysical == prior.Physical && input.ExpectedStun == prior.Stun
            && input.ExpectedOverflow == prior.Overflow, "condition-treatment-prior-changed");
        var after = SessionPlayElementalFollowups.RecoveryTransition5412(prior, input);
        return new(after.PhysicalMaximum, after.StunMaximum, after.Physical, after.Stun, after.Overflow,
            after.Body, after.OverflowLimit, after.Stabilized, after.Health) { ExcessStunRemainder = after.ExcessStunRemainder };
    }
}
