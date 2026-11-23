using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public static partial class SessionPlayElementalFollowups
{
    // Version-one adopted interpretation: retain excess Stun until a complete pair exists.
    // The inspected publisher excerpt does not conclusively settle cross-hit timing.
    // SR5:100,169–170,201–202,209. Character.CMOverflow uses Body + improvement,
    // with an additional display box for death. Preserve actual overflow, not a clamp.
    public static SessionPlayCrashDamageTransition CrashDamageTransition5401(SessionPlayPassengerInput input, int damage, string track)
    {
        ValidatePassengerCondition5393(input.Condition); Rating(input.Body, 1);
        Require(damage >= 0 && damage <= 100 && track is "physical" or "stun", "elemental-crash-damage-invalid");
        Require(input.OverflowModifier is >= -100 and <= 100, "elemental-overflow-modifier-invalid");
        int limit = checked(input.Body + input.OverflowModifier);
        var before = CrashCondition5401(input.Condition, input.PhysicalOverflow, input.Body, limit, input.Stabilized, input.ExcessStunRemainder);
        int excess = track == "stun" ? Math.Max(0, checked(before.Stun + damage) - before.StunMaximum) : 0;
        int accumulated = checked(before.ExcessStunRemainder + excess);
        int converted = track == "stun" ? accumulated / 2 : 0;
        int remainder = track == "stun" ? accumulated % 2 : before.ExcessStunRemainder;
        int physicalTotal = checked(before.Physical + (track == "physical" ? damage : converted));
        int overflow = checked(before.Overflow + Math.Max(0, physicalTotal - before.PhysicalMaximum));
        var after = CrashCondition5401(new(before.PhysicalMaximum, before.StunMaximum,
            Math.Min(before.PhysicalMaximum, physicalTotal), track == "stun" ? Math.Min(before.StunMaximum, checked(before.Stun + damage)) : before.Stun),
            overflow, before.Body, before.OverflowLimit, before.Stabilized && overflow == before.Overflow, remainder);
        return new(before, after, damage, track, converted, remainder);
    }

    private static SessionPlayCrashCondition CrashCondition5401(SessionPlayFollowupCondition c, int overflow, int body, int limit, bool stabilized, int excessStunRemainder = 0)
    {
        ValidatePassengerCondition5393(c); Rating(body, 1);
        Require(excessStunRemainder is 0 or 1 && (excessStunRemainder == 0 || c.StunDamage == c.StunMaximum),
            "elemental-excess-stun-state-invalid");
        Require(limit >= 0 && overflow is >= 0 and <= 1000000 && (overflow == 0 || c.PhysicalDamage == c.PhysicalMaximum),
            "elemental-crash-overflow-state-invalid");
        Require(!stabilized || overflow > 0, "elemental-crash-stabilization-invalid");
        var health = overflow > limit ? SessionPlayCrashHealth.Dead
            : overflow > 0 && !stabilized ? SessionPlayCrashHealth.Dying
            : c.PhysicalDamage == c.PhysicalMaximum || c.StunDamage == c.StunMaximum ? SessionPlayCrashHealth.Unconscious
            : SessionPlayCrashHealth.Conscious;
        return new(c.PhysicalMaximum, c.StunMaximum, c.PhysicalDamage, c.StunDamage, overflow, body, limit, stabilized, health) { ExcessStunRemainder = excessStunRemainder };
    }

    public static SessionPlayCrashCondition PassengerCondition5401(SessionPlayPassengerOutcome p)
        => p.CurrentCondition ?? p.DamageTransition?.After ?? CrashDamageTransition5401(p.Input, p.Damage, p.Track).After;

    public static bool ComposureResolved5401(SessionPlayPassengerOutcome p)
        => p.ComposureHits.HasValue || p.ComposureState is SessionPlayCrashComposureState.NotApplicableDead or SessionPlayCrashComposureState.AdjudicatedElapsed;

    public static SessionPlayCrashOwnedWrite? LastOwnedCrashWrite5401(IEnumerable<SessionPlayElementalOutcome> history,
        string characterId, string owner) => history.Select(h => h.Incident?.OwnedConditionWrite)
        .LastOrDefault(w => w is not null && w.CharacterId == characterId && w.Owner == owner);

    private static SessionPlayCrashCondition OwnedCondition5401(SessionPlayAuthority authority, IDictionary<string, int> values,
        IReadOnlyList<SessionPlayElementalOutcome> history, IEnumerable<SessionEventEnvelope>? journal = null)
    {
        if (authority.InitialCondition5412 is not null) return SessionPlayConditionRecovery5412.CurrentCrash(authority, values);
        Require(authority.StandardWoundRules && values.ContainsKey("physical") && values.ContainsKey("stun"), "elemental-owned-damage-authority-required");
        int? physical = authority.Trackers.SingleOrDefault(t => t.TrackerId == "physical")?.MaximumValue;
        int? stun = authority.Trackers.SingleOrDefault(t => t.TrackerId == "stun")?.MaximumValue;
        int? body = authority.Derived.Attributes.SingleOrDefault(a => a.Name == "BOD")?.Total;
        Require(physical.HasValue && stun.HasValue && body is > 0, "elemental-owned-condition-source-required");
        var latest = LastOwnedCrashWrite5401(history, authority.Binding.Version.CharacterId, authority.Binding.OwnerId);
        int remainder = latest?.After.ExcessStunRemainder ?? 0;
        // A later in-track Stun heal clears the odd excess even if a subsequent hit fills
        // the track again. Replay the actual prefix, not just today's scalar or latest crash.
        if (remainder != 0 && journal is not null && journal.SkipWhile(e => e.EventId != latest!.EventId).Skip(1)
            .Any(e => e.EventType == SessionEventTypes.TrackerDecrement
                && e.Payload.TryGetValue("trackerId", out var t) && t.StringValue == "stun"
                && e.Payload.TryGetValue("amount", out var amount) && amount.IntegerValue > 0)) remainder = 0;
        if (values["stun"] < stun!.Value) remainder = 0;
        return CrashCondition5401(new(physical!.Value, stun!.Value, values["physical"], values["stun"]), latest?.After.Overflow ?? 0,
            body!.Value, checked(body.Value + (authority.Derived.Condition?.OverflowModifier ?? 0)), latest?.After.Stabilized ?? false, remainder);
    }

    // Native review consumes the current Core snapshot. External declarations never supply owned health.
    public static SessionPlayCrashCondition SnapshotOwnedCondition5425(SessionOverlaySnapshot snapshot, string owner)
    {
        if (snapshot.Play?.Condition5412?.Current is { } c)
        {
            Require(c.Health is not null, "condition-stabilization-table-fact-required");
            return new(c.PhysicalMaximum, c.StunMaximum, c.Physical, c.Stun, c.Overflow, c.Body,
                c.OverflowLimit, c.Stabilized ?? false, c.Health!.Value) { ExcessStunRemainder = c.ExcessStunRemainder };
        }
        var play = snapshot.Play;
        Require(play is not null, "elemental-owned-damage-authority-required");
        var physical = snapshot.Trackers.Single(t => t.Definition.TrackerId == "physical");
        var stun = snapshot.Trackers.Single(t => t.Definition.TrackerId == "stun");
        int? body = play!.Derived.Attributes.SingleOrDefault(a => a.Name == "BOD")?.Total;
        Require(body is > 0 && physical.Definition.MaximumValue.HasValue && stun.Definition.MaximumValue.HasValue,
            "elemental-owned-condition-source-required");
        var latest = LastOwnedCrashWrite5401(play.ElementalEffects?.History ?? [], snapshot.BaseCharacterVersion.CharacterId, owner);
        int carry = latest?.After.ExcessStunRemainder ?? 0;
        if (carry != 0 && play.History.SkipWhile(e => e.EventId != latest!.EventId).Skip(1)
            .Any(e => e.EventType == SessionEventTypes.TrackerDecrement
                && e.Payload.TryGetValue("trackerId", out var id) && id.StringValue == "stun"
                && e.Payload.TryGetValue("amount", out var amount) && amount.IntegerValue > 0)) carry = 0;
        if (stun.CurrentValue < stun.Definition.MaximumValue!.Value) carry = 0;
        return CrashCondition5401(new(physical.Definition.MaximumValue!.Value, stun.Definition.MaximumValue!.Value,
            physical.CurrentValue, stun.CurrentValue), latest?.After.Overflow ?? 0, body!.Value,
            checked(body.Value + (play.Derived.Condition?.OverflowModifier ?? 0)), latest?.After.Stabilized ?? false, carry);
    }

    private static void BindPassengerCondition5401(SessionPlayPassengerInput input, SessionPlayAuthority authority,
        IDictionary<string, int>? values, IReadOnlyList<SessionPlayElementalOutcome> history, IEnumerable<SessionEventEnvelope>? journal = null)
    {
        Require(values is not null, "elemental-owned-damage-authority-required");
        var current = OwnedCondition5401(authority, values!, history, journal);
        var declared = CrashDamageTransition5401(input, 0, "physical").Before;
        Conflict(current == declared, "elemental-owned-passenger-condition-changed");
    }

    private static SessionPlayCrashOwnedWrite WriteOwnedCondition5401(SessionPlayPassengerOutcome p, SessionPlayCrashCondition before,
        SessionPlayCrashCondition after, int expectedPhysical, int expectedStun, int expectedOverflow, bool explicitlyApply,
        SessionPlayAuthority? authority, IDictionary<string, int>? values, IReadOnlyList<SessionPlayElementalOutcome> history, string eventId, IEnumerable<SessionEventEnvelope>? journal = null)
    {
        Require(explicitlyApply && p.Input.OwnedCharacterId is not null && authority is not null && values is not null,
            "elemental-explicit-owned-damage-required");
        Require(p.Input.OwnedCharacterId == authority!.Binding.Version.CharacterId && p.Input.OwnedOwner == authority.Binding.OwnerId,
            "elemental-owned-damage-binding-mismatch");
        var current = OwnedCondition5401(authority, values!, history, journal);
        Conflict(current.Physical == expectedPhysical && current.Stun == expectedStun && current.Overflow == expectedOverflow,
            "elemental-owned-monitor-changed");
        Conflict(current == before, "elemental-owned-prior-condition-changed");
        SessionPlayConditionRecovery5412.Write(authority!, values!, after);
        values!["physical"] = after.Physical; values["stun"] = after.Stun;
        return new(p.Input.OwnedCharacterId!, p.Input.OwnedOwner!, before, after, eventId);
    }

    private static (SessionPlayPassengerOutcome Passenger, SessionPlayCrashOwnedWrite? Write, string Summary)
        RecoverPassenger5401(SessionPlayPassengerOutcome p, SessionPlayCrashRecoveryInput input,
            SessionPlayAuthority? authority, IDictionary<string, int>? values, IReadOnlyList<SessionPlayElementalOutcome> history, string eventId, IEnumerable<SessionEventEnvelope>? journal = null)
    {
        Require(!string.IsNullOrWhiteSpace(input.Adjudicator) && !string.IsNullOrWhiteSpace(input.Evidence), "elemental-recovery-evidence-required");
        Label(input.Adjudicator); Label(input.Evidence);
        Require(input.Condition is not null, "elemental-recovery-condition-required");
        Require(Enum.IsDefined(input.Kind), "elemental-crash-recovery-kind-invalid");
        Conflict(p.Input.OwnedCharacterId is null || p.Damage == 0 || p.AppliedToOwnedRunner, "elemental-owned-damage-must-be-applied-before-recovery");
        var before = PassengerCondition5401(p);
        // An owned actor's live overlay, not an older passenger snapshot, binds treatment.
        if (p.Input.OwnedCharacterId is not null)
        {
            Require(authority is not null && values is not null, "elemental-owned-damage-authority-required");
            before = OwnedCondition5401(authority!, values!, history, journal);
        }
        var after = RecoveryTransition5412(before, input);
        SessionPlayCrashOwnedWrite? write = null;
        if (p.Input.OwnedCharacterId is not null)
            write = WriteOwnedCondition5401(p, before, after, input.ExpectedPhysical, input.ExpectedStun, input.ExpectedOverflow,
                input.ExplicitlyApplyOwned, authority, values, history, eventId, journal);
        else Require(!input.ExplicitlyApplyOwned, "elemental-external-recovery-cannot-write-owned");
        var state = after.Health == SessionPlayCrashHealth.Conscious ? SessionPlayCrashComposureState.Pending
            : after.Health == SessionPlayCrashHealth.Dead ? SessionPlayCrashComposureState.NotApplicableDead
            : SessionPlayCrashComposureState.DeferredIncapacitated;
        // A previously rolled result remains the actual result; recovery never fabricates or repeats it.
        if (ComposureResolved5401(p)) state = p.ComposureHits.HasValue ? SessionPlayCrashComposureState.Rolled : p.ComposureState;
        return (p with { CurrentCondition = after, Recovery = new(input, before, after), ComposureState = state }, write,
            $"{p.Input.Actor}: recorded {input.Kind} adjudicated by {input.Adjudicator}; {CrashConditionSummary5401(after)}. No Composure roll was invented. {input.Evidence}");
    }

    public static string CrashConditionSummary5401(SessionPlayCrashCondition c)
        => $"Physical {c.Physical}/{c.PhysicalMaximum}, Stun {c.Stun}/{c.StunMaximum}, overflow {c.Overflow}/{c.OverflowLimit}; retained excess Stun {c.ExcessStunRemainder}/1; {c.Health}"
            + (c.Overflow > 0 && c.Health != SessionPlayCrashHealth.Dead ? c.Stabilized ? "; stabilized" : $"; stabilization required, one further box per {c.Body} unattended minutes" : "");

    // Called only for a current-rule unaware pilot. No roll is possible, but a full
    // condition monitor must not prevent the resulting crash incident from existing.
    private static int UnawarePilotWounds5401(SessionPlayFollowupCondition? condition)
    {
        Require(condition is not null, "elemental-acting-condition-required");
        ValidatePassengerCondition5393(condition!);
        return new CharacterSheetConditionValues(condition!.PhysicalMaximum, condition.StunMaximum, 3, 0, 0, 0, 0, 0, 0)
            .WithDamage(condition.PhysicalDamage, condition.StunDamage).WoundModifier;
    }

    // Replay begins current action guards only at an explicit admitted transition boundary.
    // Root also calls with currentAdmission:true after original-operation lookup for NEW actions.
    public static void GuardOwnedCrashAction5401(SessionPlayAuthority authority, IDictionary<string, int> values,
        IReadOnlyList<SessionPlayElementalOutcome> history, SessionEventEnvelope e,
        IEnumerable<SessionEventEnvelope>? journal = null, bool currentAdmission = false)
    {
        if (authority.InitialCondition5412 is not null)
        { SessionPlayConditionRecovery5412.Guard(authority, values, e); return; }
        if (!currentAdmission && !history.Any(h => h.Input.CollisionTransitionVersion == 1)) return;
        bool monitor = (e.EventType is SessionEventTypes.TrackerIncrement or SessionEventTypes.TrackerDecrement)
            && e.Payload.TryGetValue("trackerId", out var target) && (target.StringValue is "physical" or "stun");
        bool acts = e.EventType is SessionPlayEventTypes.InitiativeRoll or SessionPlayEventTypes.InitiativePass or SessionPlayEventTypes.SpellCast
            or SessionPlayEventTypes.IndirectSpellCast or SessionPlayEventTypes.WeaponFire or SessionPlayEventTypes.WeaponReload or SessionPlayEventTypes.WeaponUnload;
        if (!monitor && !acts) return;
        var current = OwnedCondition5401(authority, values, history, journal);
        if (monitor) Require(current.Overflow == 0 && current.Health != SessionPlayCrashHealth.Dead, "elemental-overflow-requires-typed-recovery");
        if (acts) Require(current.Health == SessionPlayCrashHealth.Conscious, "elemental-incapacitated-owned-actor-cannot-act");
    }

    // New-event admission only, after original identity/receipt lookup. Each event sees the
    // accepted prefix, including earlier condition changes in the same batch.
    internal static void ValidateNewOwnedCollisionAction5420(SessionPlayOrigin origin, SessionPlayAuthority current,
        IReadOnlyList<SessionEventEnvelope> prefix, SessionEventEnvelope e,
        IReadOnlyList<SessionNaturalRecoveryCheckpoint>? recovery = null,
        Func<(SessionOverlaySnapshot State, SessionPlayElementalProjection Elemental)>? replay = null)
    {
        if (e.EventType is not (SessionEventTypes.TrackerIncrement or SessionEventTypes.TrackerDecrement
            or SessionPlayEventTypes.InitiativeRoll or SessionPlayEventTypes.InitiativePass or SessionPlayEventTypes.SpellCast
            or SessionPlayEventTypes.IndirectSpellCast or SessionPlayEventTypes.WeaponFire or SessionPlayEventTypes.WeaponReload or SessionPlayEventTypes.WeaponUnload)) return;
        SessionOverlaySnapshot state;
        SessionPlayElementalProjection elemental;
        if (replay is null)
            state = SessionPlayReducer.ReplayWithNaturalRecovery(origin, prefix, "", recovery ?? [], out elemental);
        else
            (state, elemental) = replay();
        // Never upgrade an old origin with today's imported condition.
        var bound = current with { InitialCondition5412 = origin.Authority.InitialCondition5412 is null ? null : current.InitialCondition5412 };
        GuardOwnedCrashAction5401(bound, SessionPlayConditionRecovery5412.ReplayValues(bound, state),
            elemental.History, e, prefix, currentAdmission: true);
    }

    internal static bool UsesCollisionTransitions5420(SessionPlayElementalCommand c)
        => c.Action is SessionPlayElementalAction.IceVehicle or SessionPlayElementalAction.CollisionDamage
            or SessionPlayElementalAction.PassengerResistance or SessionPlayElementalAction.CrashComposure
            or SessionPlayElementalAction.UncontrolledMovement or SessionPlayElementalAction.ApplyOwnedCrashDamage;

    // Called by Encode for NEW commands, never by Decode/replay/completed-operation lookup.
    internal static SessionPlayElementalCommand CurrentCollisionCommand5420(SessionPlayElementalCommand c)
    {
        Require(c.CollisionTransitionVersion is 0 or 1 && (c.CollisionTransitionVersion == 0 || UsesCollisionTransitions5420(c)),
            "elemental-collision-transition-version-invalid");
        return UsesCollisionTransitions5420(c) ? c with { CollisionTransitionVersion = 1 } : c;
    }

    internal static void ValidateNewCollisionTransitions5420(SessionEventEnvelope e)
    {
        var c = Decode(e);
        Require(c.CollisionTransitionVersion == (UsesCollisionTransitions5420(c) ? 1 : 0),
            "elemental-current-collision-transition-required");
    }

    private static SessionPlayCrashDamageTransition PendingCrashTransition5420(SessionPlayPassengerOutcome p)
        => p.DamageTransition ?? CrashDamageTransition5401(p.Input, p.Damage, p.Track);

    private static SessionPlayCrashCondition CurrentPassengerCondition5420(SessionPlayPassengerOutcome p,
        SessionPlayAuthority? authority, IDictionary<string, int>? values, IReadOnlyList<SessionPlayElementalOutcome> history,
        IEnumerable<SessionEventEnvelope> journal)
    {
        // Unwritten, nonzero damage is a projection. Zero damage never needs a write receipt.
        if (p.Input.OwnedCharacterId is null || p.Damage != 0 && !p.AppliedToOwnedRunner) return PassengerCondition5401(p);
        Require(authority is not null && values is not null, "elemental-owned-damage-authority-required");
        Require(p.Input.OwnedCharacterId == authority!.Binding.Version.CharacterId && p.Input.OwnedOwner == authority.Binding.OwnerId,
            "elemental-owned-damage-binding-mismatch");
        return OwnedCondition5401(authority, values!, history, journal);
    }

    private static string ResistanceReceipt5420(SessionPlayPassengerOutcome p, IReadOnlyList<SessionPlayElementalOutcome> history, string incidentId)
    {
        // Old passengers have no extra identity field; locate the actual immutable resistance event.
        var receipt = history.SingleOrDefault(h => h.Input.Action == SessionPlayElementalAction.PassengerResistance
            && h.Input.IncidentId == incidentId && h.Input.Passenger == p.Input);
        if (p.ResistanceEventId is { } id)
        {
            receipt = history.SingleOrDefault(h => h.EventId == id && h.Input.Action == SessionPlayElementalAction.PassengerResistance);
            Require(receipt is not null && receipt.Input.Passenger == p.Input && receipt.Input.IncidentId == incidentId, "elemental-resistance-receipt-changed");
        }
        Require(receipt is not null, "elemental-resistance-receipt-unavailable");
        return receipt!.EventId;
    }

    private static SessionPlayCrashDamageTransition RebaseTransition5420(SessionPlayPassengerOutcome p, SessionPlayCrashCondition current)
        => CrashDamageTransition5401(p.Input with {
            Condition = new(current.PhysicalMaximum, current.StunMaximum, current.Physical, current.Stun),
            Body = current.Body, OverflowModifier = current.OverflowLimit - current.Body,
            PhysicalOverflow = current.Overflow, Stabilized = current.Stabilized, ExcessStunRemainder = current.ExcessStunRemainder
        }, p.Damage, p.Track);

    // Preview only. The ordinary preview's pending-operation fence must run BEFORE this helper.
    // The returned request, including the reviewed full condition, is what confirmation journals.
    internal static SessionEventEnvelope PrepareCollisionReview5420(SessionEventEnvelope e, SessionPlayAuthority authority,
        IDictionary<string, int> values, IReadOnlyList<SessionPlayElementalOutcome> history,
        IReadOnlyList<SessionPlayElementalEffect> effects, IEnumerable<SessionEventEnvelope> journal)
    {
        var c = Decode(e);
        if (c.OwnedCrashDamage is not { ReviewRebase: true } input) return e;
        Require(c.Action == SessionPlayElementalAction.ApplyOwnedCrashDamage && c.CollisionTransitionVersion == 1,
            "elemental-current-collision-transition-required");
        var incident = effects.SingleOrDefault(x => x.EffectId == c.EffectId)?.Incident;
        var p = incident?.Occupants.SingleOrDefault(o => o.Input.Index == input.Index);
        Require(p is not null && p.Input.OwnedCharacterId == authority.Binding.Version.CharacterId
            && p.Input.OwnedOwner == authority.Binding.OwnerId && input.CharacterId == p.Input.OwnedCharacterId
            && input.Owner == p.Input.OwnedOwner && input.ExplicitlyApply, "elemental-owned-damage-binding-mismatch");
        Conflict(!p!.AppliedToOwnedRunner && p.Damage > 0, "elemental-owned-damage-already-applied");
        var previous = PendingCrashTransition5420(p);
        var current = OwnedCondition5401(authority, values, history, journal);
        Conflict(current != previous.Before, "elemental-damage-rebase-not-needed");
        Conflict(current.Physical == input.ExpectedPhysical && current.Stun == input.ExpectedStun && current.Overflow == input.ExpectedOverflow,
            "elemental-owned-monitor-changed");
        var rebase = new SessionPlayCrashDamageRebase(ResistanceReceipt5420(p, history, incident!.IncidentId), Digest(previous), RebaseTransition5420(p, current));
        if (input.Rebase is not null) Conflict(input.Rebase == rebase, "elemental-damage-rebase-review-changed");
        return e with { Payload = Encode(c with { OwnedCrashDamage = input with { Rebase = rebase } }) };
    }

    private static SessionPlayCrashDamageTransition ValidateCrashRebase5420(SessionPlayPassengerOutcome p,
        SessionPlayOwnedCrashDamageInput input, SessionPlayAuthority authority, IDictionary<string, int> values,
        IReadOnlyList<SessionPlayElementalOutcome> history, IEnumerable<SessionEventEnvelope> journal, string incidentId)
    {
        Require(input.Rebase is not null, "elemental-damage-rebase-review-required");
        var review = input.Rebase!;
        Conflict(review.ResistanceEventId == ResistanceReceipt5420(p, history, incidentId)
            && review.PreviousTransitionDigest == Digest(PendingCrashTransition5420(p)), "elemental-resistance-receipt-changed");
        var current = OwnedCondition5401(authority, values, history, journal);
        Conflict(p.Damage > 0 && current != PendingCrashTransition5420(p).Before, "elemental-damage-rebase-not-needed");
        Conflict(current == review.Replacement.Before, "elemental-owned-prior-condition-changed");
        Conflict(review.Replacement == RebaseTransition5420(p, current), "elemental-damage-rebase-review-changed");
        return review.Replacement;
    }
}
