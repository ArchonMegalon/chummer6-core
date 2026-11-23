using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;
using Chummer.Contracts.Trackers;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

/// <summary>Typed deterministic replay against a captured source authority, never a zero-based scratchpad.</summary>
public static class SessionPlayReducer
{
    public static SessionOverlaySnapshot Replay(SessionPlayOrigin origin, IReadOnlyList<SessionEventEnvelope> events, string operationId)
        => Replay(origin, events, operationId, out _);

    internal static SessionOverlaySnapshot Replay(SessionPlayOrigin origin, IReadOnlyList<SessionEventEnvelope> events, string operationId,
        out SessionPlayElementalProjection elementalProjection)
        => ReplayWithNaturalRecovery(origin, events, operationId, [], out elementalProjection);

    internal static SessionOverlaySnapshot ReplayWithNaturalRecovery(SessionPlayOrigin origin,
        IReadOnlyList<SessionEventEnvelope> events, string operationId,
        IReadOnlyList<SessionNaturalRecoveryCheckpoint> checkpoints, out SessionPlayElementalProjection elementalProjection,
        Func<SessionEventEnvelope, bool>? admittedVersion5712 = null,
        IReadOnlyList<SessionAmmunitionCheckpoint5712>? ammunitionCheckpoints5712 = null,
        IReadOnlyList<SessionFocusCastingCheckpoint7490>? focusCheckpoints7490 = null)
    {
        var elementalEffects = new List<SessionPlayElementalEffect>();
        var elementalHistory = new List<SessionPlayElementalOutcome>();
        Require(events.Count <= SessionPlayLimits.MaxEvents, "event-capacity-reached");
        var authority = origin.Authority;
        var values = authority.Trackers.ToDictionary(t => t.TrackerId, t => t.DefaultValue, StringComparer.Ordinal);
        SessionPlayConditionRecovery5412.Seed(authority, values);
        var definitions = authority.Trackers.ToDictionary(t => t.TrackerId, StringComparer.Ordinal);
        var weapons = authority.Weapons.ToDictionary(w => w.Identity.WeaponId.ToString("D"), StringComparer.Ordinal);
        var ammunition = authority.Ammunition;
        if (ammunition is not null) SessionPlayAmmunitionReducer.Validate(ammunition, weapons);
        var mechanical = authority.SustainingAuthority?.Baseline.ToList();
        var casts = authority.CastingAuthority is null ? null : new List<SessionPlayCastOutcome>();
        var effects = new Dictionary<string, SessionEffectState>(StringComparer.Ordinal);
        var notes = new List<string>();
        int? initiative = null;
        int initiativeReference = 0;
        CharacterInitiativeMode? initiativeMode = null; // null preserves old physical JSON/digests
        CharacterInitiativeModeOption Mode(CharacterInitiativeMode mode)
        {
            var option = CharacterInitiativeModes.Find(authority.Derived, mode);
            Require(authority.StandardWoundRules && option is not null, "initiative-mode-unavailable");
            return option!;
        }
        int EffectiveInitiative() => CharacterInitiativeModes.WithWounds(initiativeMode ?? CharacterInitiativeMode.Physical,
            Mode(initiativeMode ?? CharacterInitiativeMode.Physical).Value!.Value, Wound(authority, values));
        CharacterInitiativeMode RequestedMode(SessionEventEnvelope e)
        {
            Require(CharacterInitiativeModes.TryParse(Text(e, "mode"), out var mode), "initiative-mode-invalid");
            return mode;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string? parent = null;
        long expected = 1;
        int checkpointIndex = 0;
        int ammunitionCheckpointIndex5712 = 0;
        void ApplyAmmunition5712(int boundary)
        {
            var admitted = ammunitionCheckpoints5712 ?? [];
            while (ammunitionCheckpointIndex5712 < admitted.Count && admitted[ammunitionCheckpointIndex5712].EventCount == boundary)
            {
                var checkpoint = admitted[ammunitionCheckpointIndex5712++];
                Conflict(SessionAmmunitionSettlementRules5712.SameQuantities(ammunition, checkpoint.Authority.Ammunition),
                    "ammunition-checkpoint-replay-divergence");
                // Rebase absolute custody and source-derived values at this exact historical boundary.
                // Old events remain replayed only against their original seed; later fire uses the saved source revision.
                ammunition = checkpoint.Authority.Ammunition;
                weapons = checkpoint.Authority.Weapons.ToDictionary(x => x.Identity.WeaponId.ToString("D"), StringComparer.Ordinal);
                SessionPlayAmmunitionReducer.Validate(ammunition!, weapons);
                authority = authority with { Derived = checkpoint.Authority.Derived };
            }
        }
        int focusCheckpointIndex7490 = 0;
        void ApplyFocus7490(int boundary)
        {
            var admitted = focusCheckpoints7490 ?? [];
            while (focusCheckpointIndex7490 < admitted.Count && admitted[focusCheckpointIndex7490].EventCount == boundary)
            {
                var checkpoint = admitted[focusCheckpointIndex7490++];
                // Focuses can also alter general skill pools (Power/Alchemy/Disenchanting).
                // Keep conditions, ammo, ongoing effects and historical cast results intact.
                // A later ammunition checkpoint at this same event boundary already contains
                // these skill effects, so never replace its newer source projection.
                var latestAmmunitionRevision = (ammunitionCheckpoints5712 ?? [])
                    .Take(ammunitionCheckpointIndex5712).Select(x => x.Authority.Binding.Version.VersionId)
                    .Select(x => long.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
                authority = SessionPlayCapabilityHistory7494.ApplyFocus(origin, authority, checkpoint,
                    events.Take(boundary).ToArray(), latestAmmunitionRevision);
            }
        }
        void ApplyRecovery(int boundary)
        {
            while (checkpointIndex < checkpoints.Count && checkpoints[checkpointIndex].EventCount == boundary)
            {
                var checkpoint = checkpoints[checkpointIndex++];
                SessionPlayConditionRecovery5412.ApplyNaturalRecovery(authority, values, checkpoint);
            }
        }
        foreach (var e in events)
        {
            ApplyRecovery(checked((int)e.Sequence - 1));
            ApplyAmmunition5712(checked((int)e.Sequence - 1));
            ApplyFocus7490(checked((int)e.Sequence - 1));
            if (admittedVersion5712 is null) ValidateEnvelope(origin, e);
            else
            {
                Conflict(admittedVersion5712(e), "event-base-runtime-edition-mismatch");
                ValidateEnvelope(origin, e with { BaseCharacterVersion = origin.Authority.Binding.Version });
            }
            Conflict(ids.Add(e.EventId) && e.Sequence == expected++ && e.ParentEventId == parent, "event-order-divergence");
            parent = e.EventId;
            SessionPlayElementalFollowups.GuardOwnedCrashAction5401(authority, values, elementalHistory, e, events.Take(checked((int)e.Sequence - 1)));
            int stunBefore5425 = values.TryGetValue("stun", out var stun5425) ? stun5425 : 0;
            switch (e.EventType)
            {
                case SessionPlayConditionCommand5412.EventType:
                    SessionPlayConditionRecovery5412.Apply(authority, values, e, events.Take(checked((int)e.Sequence - 1)));
                    break;
                case SessionPlayEventTypes.IndirectCastingAdopt:
                    authority = SessionPlayIndirectUpgrade5338.Replay(authority, e, events.Take(checked((int)e.Sequence - 1)));
                    break;
                case SessionPlayEventTypes.MagicCapabilitiesAdopt:
                {
                    bool addSustaining = authority.SustainingAuthority is null;
                    authority = SessionPlayMagicUpgrade5311.Replay(authority, e, events.Take(checked((int)e.Sequence - 1)));
                    if (addSustaining) mechanical = authority.SustainingAuthority?.Baseline.ToList();
                    if (casts is null && authority.CastingAuthority is not null) casts = [];
                    break;
                }
                case SessionPlayEventTypes.InitiativeCapabilitiesAdopt:
                    authority = SessionPlayInitiativeUpgrade5110.Replay(authority, e, events.Take(checked((int)e.Sequence - 1)));
                    break;
                case SessionEventTypes.TrackerIncrement:
                case SessionEventTypes.TrackerDecrement:
                case SessionEventTypes.ResourceSpend:
                case SessionEventTypes.ResourceRestore:
                {
                    Keys(e, "trackerId", "amount");
                    string target = Text(e, "trackerId");
                    Require(definitions.TryGetValue(target, out var definition), "tracker-authority-unavailable");
                    long amount = Integer(e, "amount");
                    Require(amount > 0 && amount <= int.MaxValue, "invalid-amount");
                    if (target == "edge")
                    {
                        Require(e.EventType is SessionEventTypes.ResourceSpend or SessionEventTypes.ResourceRestore,
                            "edge-requires-resource-action");
                        Require(amount == 1 && definition!.MaximumValue is not null, "edge-action-is-one-point");
                        Require(CharacterCareerEdgeUseRules.TryProject(true, definition.MaximumValue!.Value - values[target],
                            definition.MaximumValue.Value, out var edge), "edge-state-invalid");
                        var action = e.EventType == SessionEventTypes.ResourceSpend ? CharacterCareerEdgeUseAction.Spend : CharacterCareerEdgeUseAction.Regain;
                        Require(CharacterCareerEdgeUseRules.CanApply(edge!, action), "edge-action-illegal");
                        values[target] = definition.MaximumValue.Value - CharacterCareerEdgeUseRules.Apply(edge!, action);
                    }
                    else
                    {
                        Require(authority.StandardWoundRules && target is "physical" or "stun"
                            && e.EventType is SessionEventTypes.TrackerIncrement or SessionEventTypes.TrackerDecrement,
                            "damage-authority-unavailable");
                        long next = checked((long)values[target] + (e.EventType == SessionEventTypes.TrackerIncrement ? amount : -amount));
                        Require(next >= definition!.MinimumValue!.Value && next <= definition.MaximumValue!.Value, "condition-track-boundary");
                        values[target] = checked((int)next);
                    }
                    break;
                }
                case SessionPlayEventTypes.WeaponFire:
                {
                    Keys(e, "weaponId", "revision", "mode", "confirmPartial");
                    string target = Text(e, "weaponId");
                    Require(weapons.TryGetValue(target, out var weapon), "weapon-authority-unavailable");
                    string modeName = Text(e, "mode");
                    Require(Enum.TryParse<CharacterWeaponFireMode>(modeName, false, out var mode) && Enum.IsDefined(mode)
                        && mode.ToString() == modeName, "invalid-fire-mode");
                    Require(CharacterWeaponFireRules.TryValidateMutation(weapon, Text(e, "revision"), mode,
                        Boolean(e, "confirmPartial"), out var plan), "weapon-fire-illegal");
                    if (ammunition is not null)
                    {
                        ammunition = SessionPlayAmmunitionReducer.Fire(ammunition, weapon!, plan.RoundsConsumed, e.EventId);
                        SessionPlayAmmunitionReducer.Project(ammunition, weapons, e.EventId);
                        break;
                    }
                    weapons[target] = weapon! with { AmmoRemaining = plan.NewAmmoRemaining, AmmoGearQuantity = plan.NewAmmoGearQuantity,
                        Revision = Digest(new { weapon.Revision, e.EventId, plan.NewAmmoRemaining, plan.NewAmmoGearQuantity }) };
                    break;
                }
                case SessionPlayEventTypes.WeaponReload:
                case SessionPlayEventTypes.WeaponUnload:
                {
                    Keys(e, "weaponId", "gearId", "feed", "rounds", "kind", "revision", "ammoRevision");
                    Require(Guid.TryParse(Text(e, "weaponId"), out var weaponId) && weaponId != Guid.Empty
                        && Guid.TryParse(Text(e, "gearId"), out _), "ammunition-identity-invalid");
                    Require(Enum.TryParse<SessionPlayAmmoTransferKind>(Text(e, "kind"), false, out var kind)
                        && Enum.IsDefined(kind) && kind.ToString() == Text(e, "kind")
                        && (e.EventType == SessionPlayEventTypes.WeaponUnload) == (kind == SessionPlayAmmoTransferKind.Unload), "ammunition-action-invalid");
                    Require(ammunition is not null, "ammunition-authority-unavailable");
                    var transfer = new SessionPlayAmmoTransfer(kind, weaponId, Guid.Parse(Text(e, "gearId")), Text(e, "feed"),
                        checked((int)Integer(e, "rounds")), Text(e, "revision"), Text(e, "ammoRevision"));
                    ammunition = SessionPlayAmmunitionReducer.Transfer(ammunition!, weapons, transfer, e.EventId);
                    SessionPlayAmmunitionReducer.Project(ammunition, weapons, e.EventId);
                    break;
                }

                case SessionPlayEventTypes.InitiativeRoll:
                {
                    bool explicitMode = e.Payload.ContainsKey("mode");
                    if (explicitMode) Keys(e, "dice", "mode"); else Keys(e, "dice");
                    var requested = explicitMode ? RequestedMode(e) : CharacterInitiativeMode.Physical;
                    var source = Mode(requested);
                    initiativeMode = explicitMode ? requested : null;
                    var dice = Value(e, "dice");
                    Require(dice.Kind == RulesetCapabilityValueKinds.List && dice.Items is not null
                        && dice.StringValue is null && dice.IntegerValue is null && dice.BooleanValue is null
                        && dice.NumberValue is null && dice.DecimalValue is null && dice.Properties is null
                        && dice.Items.Count == source.Dice && dice.Items.Count <= 64, "initiative-dice-mismatch");
                    long sum = 0;
                    foreach (var die in dice.Items!)
                    {
                        long roll = Integer(die);
                        Require(roll >= 1 && roll <= 6, "invalid-die");
                        sum = checked(sum + roll);
                    }
                    initiativeReference = EffectiveInitiative();
                    initiative = checked((int)(initiativeReference + sum));
                    break;
                }
                case SessionPlayEventTypes.InitiativePass:
                    if (e.Payload.ContainsKey("mode"))
                    {
                        Keys(e, "mode");
                        Require(RequestedMode(e) == (initiativeMode ?? CharacterInitiativeMode.Physical), "initiative-pass-mode-mismatch");
                    }
                    else
                    {
                        Keys(e);
                        Require(initiativeMode is null, "initiative-pass-mode-required");
                    }
                    Require(initiative.HasValue, "initiative-pass-unavailable");
                    int currentScore = Math.Max(0, checked(initiative!.Value + EffectiveInitiative() - initiativeReference));
                    Require(currentScore > 0, "initiative-pass-unavailable");
                    // SR5 initiative pass decrement; this is not an arbitrary editable initiative value.
                    initiative = Math.Max(0, checked(currentScore - 10));
                    initiativeReference = EffectiveInitiative();
                    break;
                case SessionPlayEventTypes.ElementalFollowup:
                    SessionPlayElementalFollowups.Evaluate(elementalEffects, elementalHistory, e, events.Take(checked((int)e.Sequence - 1)), false, authority, values);
                    break;
                case SessionPlayEventTypes.IndirectSpellCast:
                case SessionPlayEventTypes.SpellCast:
                {
                    var cast = SessionPlayCasting.Apply(authority, mechanical, values, e, events.Take(checked((int)e.Sequence - 1)), Wound(authority, values));
                    casts!.Add(cast);
                    SessionPlayElementalFollowups.Seed(authority, cast, e, elementalEffects);
                    values[cast.DrainTrack] = cast.ConditionAfter;
                    break;
                }
                case SessionPlayEventTypes.SustainingApply:
                case SessionPlayEventTypes.SustainingEnd:
                    SessionPlaySustainingEffects.Apply(authority.SustainingAuthority, mechanical, e, events.Take(checked((int)e.Sequence - 1)));
                    break;
                case SessionEventTypes.EffectAdd:
                {
                    Keys(e, "effectId", "label");
                    string id = Text(e, "effectId"), label = Text(e, "label");
                    Identity(id, "invalid-effect-id");
                    Require(label.Length is > 0 and <= 256 && !effects.ContainsKey(id), "invalid-effect");
                    effects.Add(id, new(id, label, true, e.EventId));
                    break;
                }
                case SessionEventTypes.EffectRemove:
                    Keys(e, "effectId");
                    Require(effects.Remove(Text(e, "effectId")), "effect-not-active");
                    break;
                case SessionEventTypes.NoteAppend:
                    Keys(e, "text");
                    string note = Text(e, "text");
                    Require(!string.IsNullOrWhiteSpace(note) && note.Length <= SessionPlayLimits.MaxNoteLength, "invalid-note");
                    notes.Add(note);
                    break;
                default:
                    throw new SessionPlayException(SessionPlayFailures.Invalid("event-capability-unavailable"));
            }
            SessionPlayConditionRecovery5412.ObserveStunChange(authority, values, stunBefore5425);
        }
        ApplyRecovery(events.Count);
        ApplyAmmunition5712(events.Count);
        ApplyFocus7490(events.Count);
        Conflict(focusCheckpointIndex7490 == (focusCheckpoints7490?.Count ?? 0), "focus-checkpoint-order-divergence");
        Conflict(ammunitionCheckpointIndex5712 == (ammunitionCheckpoints5712?.Count ?? 0), "ammunition-checkpoint-order-divergence");
        Conflict(checkpointIndex == checkpoints.Count, "recovery-checkpoint-order-divergence");
        int adjustment = Wound(authority, values);
        var derived = authority.Derived;
        if (authority.StandardWoundRules)
        {
            derived = derived with {
                InitiativeModes = derived.InitiativeModes?.Select(m => m with { Value = m.Value is { } value
                    ? CharacterInitiativeModes.WithWounds(m.Mode, value, adjustment) : null }).ToArray(),
                Condition = derived.Condition?.WithDamage(values["physical"], values["stun"]),
                DefensePool = Adjust(derived.DefensePool, adjustment),
                Skills = derived.Skills.Select(s => s with { DicePool = Adjust(s.DicePool, adjustment),
                    Specializations = s.Specializations.Select(p => p with { DicePool = Math.Max(0, checked(p.DicePool + adjustment)) }).ToArray() }).ToArray(),
                PhysicalInitiative = derived.PhysicalInitiative is { } i ? i with {
                    Value = Math.Max(0, checked(i.Value + adjustment)), WoundModifier = adjustment } : null };
        }
        var mechanicalProjection = authority.SustainingAuthority is { } sustaining
            ? SessionPlaySustainingEffects.Project(sustaining, mechanical!) : null;
        if (mechanicalProjection is not null)
            derived = SessionPlaySustainingEffects.Recalculate(derived, authority.SustainingAuthority!, mechanicalProjection, adjustment);
        int? score = initiative is null ? null : Math.Max(0, checked(initiative.Value + EffectiveInitiative() - initiativeReference));
        // Event cursors retain their original wire identity; condition revision and the
        // canonical review bind healing. Existing typed events validate Cursor(prefix).
        string cursor = Cursor(events);
        elementalProjection = new(cursor, elementalEffects.ToArray(), elementalHistory.ToArray());
        return new(origin.OverlayId, authority.Binding.Version,
            authority.Trackers.Select(t => new TrackerSnapshot(t, values[t.TrackerId])).ToArray(),
            effects.Values.OrderBy(e => e.EffectId, StringComparer.Ordinal).ToArray(), [], notes,
            new(SessionSyncStatuses.LocalOnly, 0, null)) {
            Play = new(operationId, cursor, derived, score,
                weapons.Values.OrderBy(w => w.Identity.WeaponId).ToArray(), events.ToArray(), authority.Diagnostics) { Condition5412 = SessionPlayConditionRecovery5412.Project(authority, values), VehiclePolicy = authority.VehiclePolicy, ElementalEffects = elementalHistory.Count == 0 ? null : elementalProjection, Ammunition = ammunition, InitiativeMode = initiativeMode, MechanicalEffects = mechanicalProjection, Casting = authority.CastingAuthority is { } casting
                    ? SessionPlayCasting.Project(casting, casts!, adjustment, mechanicalProjection?.DicePoolPenalty ?? 0) : null } };
    }

    public static void ValidateEnvelope(SessionPlayOrigin origin, SessionEventEnvelope e)
    {
        Require(e is not null && e.Payload is not null && e.Payload.Count <= 8, "invalid-envelope");
        Identity(e.EventId, "event-id-required"); Identity(e.DeviceId, "device-id-required");
        Conflict(origin.Authority.SettledOperations5712?.All(x => !x.EventIds.Contains(e.EventId, StringComparer.Ordinal)) != false,
            "ammunition-event-already-settled");
        Conflict(e.OverlayId == origin.OverlayId && e.ActorId == origin.Authority.Binding.OwnerId,
            "event-owner-overlay-mismatch");
        Conflict(e.BaseCharacterVersion == origin.Authority.Binding.Version, "event-base-runtime-edition-mismatch");
        Require(e.Schema == SessionEventEnvelopeSchemas.SessionEventsVnext && e.Sequence > 0 && e.CreatedAtUtc != default,
            "invalid-event-version");
        Require(e.AppliedAtUtc is null && e.SyncCursor is null && e.ProviderId is null && e.PackId is null,
            "client-authority-metadata-forbidden");
    }
    private static int? Adjust(int? value, int delta) => value is null ? null : Math.Max(0, checked(value.Value + delta));
    private static int Wound(SessionPlayAuthority authority, IReadOnlyDictionary<string, int> values)
        => authority.StandardWoundRules
            ? authority.Derived.Condition is { } condition
                ? condition.WithDamage(values["physical"], values["stun"]).WoundModifier
                : checked(-(values["physical"] / 3) - values["stun"] / 3) // Retained v1 authorities have the explicit standard-wounds contract.
            : 0;
    private static void Keys(SessionEventEnvelope e, params string[] keys)
        => Require(e.Payload.Count == keys.Length && keys.All(e.Payload.ContainsKey), "event-payload-shape");
    private static RulesetCapabilityValue Value(SessionEventEnvelope e, string key)
    {
        Require(e.Payload.TryGetValue(key, out var value) && value is not null, "event-payload-missing");
        return value!;
    }
    private static string Text(SessionEventEnvelope e, string key)
    {
        var value = Value(e, key);
        Require(value.Kind == RulesetCapabilityValueKinds.String && value.StringValue is not null
            && value.BooleanValue is null && value.IntegerValue is null && value.NumberValue is null
            && value.DecimalValue is null && value.Items is null && value.Properties is null, "event-string-required");
        return value.StringValue!;
    }
    private static bool Boolean(SessionEventEnvelope e, string key)
    {
        var value = Value(e, key);
        Require(value.Kind == RulesetCapabilityValueKinds.Boolean && value.BooleanValue is not null
            && value.StringValue is null && value.IntegerValue is null && value.NumberValue is null
            && value.DecimalValue is null && value.Items is null && value.Properties is null, "event-boolean-required");
        return value.BooleanValue!.Value;
    }
    private static long Integer(SessionEventEnvelope e, string key) => Integer(Value(e, key));
    private static long Integer(RulesetCapabilityValue value)
    {
        Require(value.Kind == RulesetCapabilityValueKinds.Integer && value.IntegerValue is not null
            && value.StringValue is null && value.BooleanValue is null && value.NumberValue is null
            && value.DecimalValue is null && value.Items is null && value.Properties is null, "event-integer-required");
        return value.IntegerValue!.Value;
    }
}

internal static class SessionPlayAmmunitionReducer
{
    internal static void Validate(SessionPlayAmmunition state, IReadOnlyDictionary<string, CharacterWeaponFireState> weapons)
    {
        Require(state.Inventory.Count <= 256 && state.Weapons.Count <= 64 && !string.IsNullOrWhiteSpace(state.Revision)
            && state.Inventory.Select(a => a.GearId).Distinct().Count() == state.Inventory.Count
            && state.Weapons.Select(w => w.WeaponId).Distinct().Count() == state.Weapons.Count, "ammunition-authority-invalid");
        var inventory = state.Inventory.ToDictionary(a => a.GearId);
        foreach (var w in state.Weapons)
        {
            if (w.Configuration is { } configuration)
                Require(configuration.SourceId != Guid.Empty && !string.IsNullOrWhiteSpace(configuration.SourceDigest)
                    && configuration.Attachments.Count is > 0 and <= 32
                    && configuration.Attachments.All(a => a.InstanceId != Guid.Empty && a.SourceId != Guid.Empty
                        && a.ParentWeaponId == w.WeaponId && a.Rating >= 0 && !string.IsNullOrWhiteSpace(a.DisplayName))
                    && configuration.Attachments.Select(a => a.InstanceId).Distinct().Count() == configuration.Attachments.Count,
                    "ammunition-configuration-invalid");
            Require(w.WeaponId != Guid.Empty && w.AmmoSlot == 1 && w.Feeds.Count > 0 && w.Feeds.All(f => f.Capacity > 0)
                && w.Feeds.Select(f => f.Id).Distinct().Count() == w.Feeds.Count && w.LoadedRounds >= 0
                && w.LoadedRounds <= w.Feeds.Max(f => f.Capacity)
                && (w.Feed is null || w.Feeds.Any(f => f.Id == w.Feed && f.Capacity >= w.LoadedRounds))
                && (w.LoadedGearId != Guid.Empty || w.LoadedRounds == 0)
                && (w.LoadedGearId == Guid.Empty || inventory.ContainsKey(w.LoadedGearId) && w.CompatibleGearIds.Contains(w.LoadedGearId))
                && w.CompatibleGearIds.All(inventory.ContainsKey), "ammunition-weapon-invalid");
            Require(weapons.TryGetValue(w.WeaponId.ToString("D"), out var fire) && fire.Identity.AmmoSlot == w.AmmoSlot
                && fire.Identity.AmmoGearId == w.LoadedGearId && fire.AmmoRemaining == w.LoadedRounds
                && (w.LoadedGearId == Guid.Empty ? fire.AmmoGearQuantity is null : fire.AmmoGearQuantity == inventory[w.LoadedGearId].TotalRounds), "ammunition-fire-divergence");
        }
        foreach (var a in state.Inventory)
            Require(a.GearId != Guid.Empty && a.SourceId != Guid.Empty && a.TotalRounds >= 0 && a.AvailableRounds >= 0
                && (long)a.AvailableRounds + state.Weapons.Where(w => w.LoadedGearId == a.GearId).Sum(w => (long)w.LoadedRounds) == a.TotalRounds,
                "ammunition-conservation-invalid");
    }
    internal static SessionPlayAmmunition Transfer(SessionPlayAmmunition state,
        IReadOnlyDictionary<string, CharacterWeaponFireState> weapons, SessionPlayAmmoTransfer request, string eventId)
    {
        Validate(state, weapons);
        Conflict(request.AmmunitionRevision == state.Revision && weapons.TryGetValue(request.WeaponId.ToString("D"), out var fire)
            && request.WeaponRevision == fire.Revision, "ammunition-revision-changed");
        var weapon = state.Weapons.SingleOrDefault(w => w.WeaponId == request.WeaponId);
        Require(weapon is not null && request.Rounds > 0, "ammunition-transfer-invalid");
        var inventory = state.Inventory.ToDictionary(a => a.GearId);
        var current = weapon!;
        if (request.Kind == SessionPlayAmmoTransferKind.Unload)
        {
            Require(request.GearId == current.LoadedGearId && request.GearId != Guid.Empty
                && request.Feed == (current.Feed ?? "") && request.Rounds <= current.LoadedRounds, "ammunition-unload-invalid");
            var old = inventory[current.LoadedGearId];
            inventory[old.GearId] = old with { AvailableRounds = checked(old.AvailableRounds + request.Rounds) };
            current = current with { LoadedRounds = current.LoadedRounds - request.Rounds };
            if (current.LoadedRounds == 0) current = current with { LoadedGearId = Guid.Empty, Feed = null };
        }
        else
        {
            Require(request.Kind is SessionPlayAmmoTransferKind.Reload or SessionPlayAmmoTransferKind.Replace
                && current.CompatibleGearIds.Contains(request.GearId) && inventory.ContainsKey(request.GearId), "ammunition-incompatible");
            var feed = current.Feeds.SingleOrDefault(f => f.Id == request.Feed);
            Require(feed is not null, "ammunition-feed-unavailable");
            if (request.Kind == SessionPlayAmmoTransferKind.Replace)
            {
                Require(current.LoadedRounds > 0, "ammunition-replacement-empty");
                var old = inventory[current.LoadedGearId];
                inventory[old.GearId] = old with { AvailableRounds = checked(old.AvailableRounds + current.LoadedRounds) };
                current = current with { LoadedRounds = 0, LoadedGearId = Guid.Empty, Feed = null };
            }
            Require((current.LoadedRounds == 0 || current.LoadedGearId == request.GearId)
                && (current.LoadedRounds == 0 || current.Feed is null || current.Feed == request.Feed)
                && (long)current.LoadedRounds + request.Rounds <= feed!.Capacity, "ammunition-capacity-or-mixed-load");
            var selected = inventory[request.GearId];
            Require(request.Rounds <= selected.AvailableRounds, "ammunition-insufficient");
            inventory[selected.GearId] = selected with { AvailableRounds = selected.AvailableRounds - request.Rounds };
            current = current with { LoadedGearId = request.GearId, LoadedRounds = checked(current.LoadedRounds + request.Rounds), Feed = request.Feed };
        }
        return state with { Revision = Digest(new { state.Revision, eventId, request }),
            Inventory = state.Inventory.Select(a => inventory[a.GearId]).ToArray(),
            Weapons = state.Weapons.Select(w => w.WeaponId == current.WeaponId ? current : w).ToArray() };
    }
    internal static SessionPlayAmmunition Fire(SessionPlayAmmunition state, CharacterWeaponFireState weapon, int rounds, string eventId)
    {
        var clip = state.Weapons.SingleOrDefault(w => w.WeaponId == weapon.Identity.WeaponId);
        Require(clip is not null && clip.LoadedGearId != Guid.Empty && rounds > 0 && rounds <= clip.LoadedRounds, "ammunition-fire-unavailable");
        return state with { Revision = Digest(new { state.Revision, eventId, rounds }),
            Inventory = state.Inventory.Select(a => a.GearId == clip!.LoadedGearId ? a with { TotalRounds = checked(a.TotalRounds - rounds) } : a).ToArray(),
            Weapons = state.Weapons.Select(w => w.WeaponId == clip!.WeaponId ? w with { LoadedRounds = w.LoadedRounds - rounds } : w).ToArray() };
    }
    internal static void Project(SessionPlayAmmunition state, Dictionary<string, CharacterWeaponFireState> weapons, string eventId)
    {
        foreach (var clip in state.Weapons)
        {
            string key = clip.WeaponId.ToString("D"); var old = weapons[key];
            weapons[key] = old with { Identity = old.Identity with { AmmoGearId = clip.LoadedGearId }, AmmoRemaining = clip.LoadedRounds,
                AmmoGearQuantity = clip.LoadedGearId == Guid.Empty ? null : state.Inventory.Single(a => a.GearId == clip.LoadedGearId).TotalRounds,
                Revision = Digest(new { weaponRevision = old.Revision, ammunitionRevision = state.Revision, eventId }) };
        }
        Validate(state, weapons);
    }
}

internal sealed record SessionAmmunitionCheckpoint5712(int EventCount, SessionPlayAuthority Authority);
