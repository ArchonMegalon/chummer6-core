using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public sealed partial class DurableSessionPlayService
{
    public SessionApiResult<SessionPlayCastingReview> PreviewCharacterCasting(OwnerScope owner,
        string characterId, SessionPatchRequest request) => Guard(() =>
    {
        var intent = Canonical(request.OperationId, "apply", request.OverlayId, request.BaseCharacterVersion, request.Events, null);
        Require(intent.Events.Count == 1 && (intent.Events[0].EventType is SessionPlayEventTypes.SpellCast or SessionPlayEventTypes.IndirectSpellCast), "casting-requires-single-event");
        return store.Inspect(owner, characterId, reader =>
        {
            var origin = reader?.ReadOrigin() ?? throw new SessionPlayException(SessionPlayFailures.Conflict("journal-origin-missing"));
            var operations = reader!.ReadOperations();
            Conflict(operations.All(o => o.Result is not null && o.Intent.OperationId != intent.OperationId), "original-operation-needs-inspection");
            var current = ResolveCurrentWithContinuation(reader!, owner, characterId);
            ValidateAuthority(current, owner, characterId);
            Conflict(MatchesOrigin5082(origin.Authority, current) && intent.OverlayId == origin.OverlayId
                && intent.BaseCharacterVersion == CurrentPlayVersion5712(reader!, current), "canonical-base-or-runtime-changed");
            var history = History(operations);
            SessionPlayMagicUpgrade5311.ValidateCurrent(origin, current, history, CapabilityHistory7494(reader!));
            var before = Replay(reader!, origin, history, operations.LastOrDefault(o => o.Result?.Snapshot is not null)?.Intent.OperationId ?? "");
            CheckReplay(reader!, operations, before);
            ValidateCurrentEnvelope5712(reader!, origin, intent.Events[0]);
            Conflict(intent.Events[0].Sequence == history.Length + 1L && intent.Events[0].ParentEventId == history.LastOrDefault()?.EventId,
                "event-order-divergence");
            var reviewedRequest = new SessionPatchRequest(intent.OverlayId, intent.BaseCharacterVersion, intent.Events) { OperationId = intent.OperationId };
            if (SessionPlayCasting.IsSetup(intent.Events[0]))
                return new SessionPlayCastingReview(reviewedRequest, intent.PayloadDigest,
                    SessionPlayCasting.Setup(CastingAuthority7490(reader!, origin, current, history), before, intent.Events[0]), null);
            var after = Replay(reader!, origin, history.Concat(intent.Events).ToArray(), intent.OperationId);
            Require(after.Play!.Casting is not null, "casting-authority-unavailable");
            var outcome = after.Play.Casting!.History.Single(c => c.EventId == intent.Events[0].EventId);
            var option = before.Play!.Casting!.Choices.Single(c => c.Choice.SpellId == outcome.SpellId);
            return new SessionPlayCastingReview(reviewedRequest, intent.PayloadDigest,
                SessionPlayCasting.Describe(option, outcome.Force), outcome);
        });
    });
}

// The initiative 5110 Offer/ValidateNew/Replay convention, scoped to missing magic
// capabilities. No character rebase, mutable origin or second persistence mechanism.
internal static class SessionPlayMagicUpgrade5311
{
    private sealed record Added(SessionPlayCastingAuthority? Casting, SessionPlaySustainingAuthority? Sustaining);

    internal static SessionPlayAuthority OriginalCapabilities(SessionPlayAuthority origin, SessionPlayAuthority current)
        => Rebind(AtVehicleBoundary5418(origin, current) with { CastingAuthority = origin.CastingAuthority is null ? null : origin.CastingAuthority.Indirect is null
                ? WithoutIndirect(current.CastingAuthority) : origin.CastingAuthority.Indirect.Elemental is null
                    ? WithoutElemental(current.CastingAuthority) : current.CastingAuthority,
            SustainingAuthority = origin.SustainingAuthority is null ? null : current.SustainingAuthority,
            InitialCondition5412 = origin.InitialCondition5412 is null ? null : current.InitialCondition5412 });

    private static bool IsVehicleDiagnostic5418(SessionPlayDiagnostic diagnostic)
        => diagnostic.Code is "vehicle-policy-retained-environment-required" or "vehicle-policy-source-invalid";

    private static SessionPlayAuthority AtVehicleBoundary5418(SessionPlayAuthority boundary, SessionPlayAuthority value)
    {
        if (boundary.VehiclePolicy is not null) return value;
        // Magic, indirect and initiative adoption replay retain the original Diagnostics.
        // Only the one exact diagnostic newly emitted by 5400 may be absent there.
        // A boundary already carrying either code is current: all its diagnostics bind.
        var diagnostics = value.Diagnostics;
        if (value.VehiclePolicy is null && !boundary.Diagnostics.Any(IsVehicleDiagnostic5418))
        {
            var introduced = diagnostics.Where(IsVehicleDiagnostic5418).ToArray();
            if (introduced.Length == 1 && (introduced[0] == new SessionPlayDiagnostic(
                    "vehicle-policy-retained-environment-required", "vehicle-policy",
                    "Adopt and save this character's complete rule environment before vehicle tests. No ambient profile is substituted.")
                || introduced[0] == new SessionPlayDiagnostic("vehicle-policy-source-invalid", "vehicle-policy",
                    "Repair and re-adopt the selected profile's sessionplayvehiclepolicy (version, jumpedhotsimbonus, armorequality) "
                    + "or its Pilot skill source/default flags. No standard replacement was used.")))
                diagnostics = diagnostics.Where(d => d != introduced[0]).ToArray();
        }
        return value with { VehiclePolicy = null, Diagnostics = diagnostics };
    }

    internal static SessionPlayAuthority CurrentCapabilities(SessionPlayOrigin origin, SessionPlayAuthority current,
        IReadOnlyList<SessionEventEnvelope> history, SessionPlayCapabilityHistory7494? continuation = null)
    {
        if (continuation is not null)
        {
            var prior = continuation.At(origin, history);
            var source = continuation.SourceAt(current, history.Count);
            // Initiative Offer/ValidateNew needs available modes before explicit consent.
            return Rebind(prior with { Derived = prior.Derived with { InitiativeModes = source.Derived.InitiativeModes } });
        }
        return Rebind(AtIndirectBoundary(origin, history.Any(e => e.EventType == SessionPlayEventTypes.MagicCapabilitiesAdopt)
            ? current : OriginalCapabilities(origin.Authority, current), current, history));
    }

    internal static SessionPlayCastingAuthority? WithoutIndirect(SessionPlayCastingAuthority? value)
    {
        if (value?.Indirect is null) return value;
        var direct = value with { Digest = "", Indirect = null };
        return direct with { Digest = Digest(direct) };
    }
    internal static SessionPlayCastingAuthority? WithoutElemental(SessionPlayCastingAuthority? value)
    {
        if (value?.Indirect?.Elemental is null) return value;
        var prior = value with { Digest = "", Indirect = value.Indirect.Choices.Count == 0 ? null
            : value.Indirect with { Elemental = null } };
        return prior with { Digest = Digest(prior) };
    }
    private static SessionPlayAuthority AtIndirectBoundary(SessionPlayOrigin origin, SessionPlayAuthority value,
        SessionPlayAuthority current, IEnumerable<SessionEventEnvelope> prefix)
    {
        // Never let a later vehicle capability invalidate an old magic/initiative receipt.
        value = AtVehicleBoundary5418(origin.Authority, value);
        if (origin.Authority.InitialCondition5412 is null) value = value with { InitialCondition5412 = null };
        var adoptions = prefix.Where(e => e.EventType == SessionPlayEventTypes.IndirectCastingAdopt).ToArray();
        bool indirect = origin.Authority.CastingAuthority?.Indirect is not null || adoptions.Length > 0;
        bool elemental = origin.Authority.CastingAuthority?.Indirect?.Elemental is not null
            || adoptions.Any(e => SessionPlayIndirectUpgrade5338.ReadCapability(e).Elemental is not null);
        return value with { CastingAuthority = value.CastingAuthority is null ? null : !indirect
            ? WithoutIndirect(current.CastingAuthority) : elemental ? current.CastingAuthority : WithoutElemental(current.CastingAuthority) };
    }

    internal static SessionPlayAuthority Rebind(SessionPlayAuthority value)
    {
        value = value with { Binding = value.Binding with { AuthorityDigest = "" } };
        return value with { Binding = value.Binding with { AuthorityDigest = Digest(value) } };
    }

    private static SessionPlayAuthority AtMagicAdoption(SessionPlayOrigin origin, SessionPlayAuthority current,
        IEnumerable<SessionEventEnvelope> prefix)
    {
        var at = AtIndirectBoundary(origin, current, current, prefix);
        if (origin.Authority.Derived.InitiativeModes is null
            && !prefix.Any(e => e.EventType == SessionPlayEventTypes.InitiativeCapabilitiesAdopt))
            at = at with { Derived = at.Derived with { InitiativeModes = null } };
        return Rebind(at);
    }

    internal static SessionPlayMagicUpgradeOffer? Offer(SessionPlayOrigin origin, SessionPlayAuthority current,
        SessionOverlaySnapshot before, SessionPlayCapabilityHistory7494? continuation = null)
    {
        if (!current.StandardWoundRules || before.Play is not { } play
            || play.History.Any(e => e.EventType == SessionPlayEventTypes.MagicCapabilitiesAdopt)) return null;
        var source = continuation?.SourceAt(current, play.History.Count) ?? current;
        var prior = continuation?.At(origin, play.History) ?? origin.Authority;
        var added = new Added(prior.CastingAuthority is null ? WithoutIndirect(source.CastingAuthority) : null,
            prior.SustainingAuthority is null ? source.SustainingAuthority : null);
        if (added.Casting is null && added.Sustaining is null) return null;
        var next = MagicAuthority7494(origin, current, play.History, continuation);
        string json = System.Text.Json.JsonSerializer.Serialize(added, Json);
        var chunks = Enumerable.Range(0, (json.Length + 1023) / 1024)
            .Select(i => Text(json.Substring(i * 1024, Math.Min(1024, json.Length - i * 1024)))).ToArray();
        var payload = new Dictionary<string, Chummer.Contracts.Rulesets.RulesetCapabilityValue>(StringComparer.Ordinal) {
            ["authority"] = Text(next.Binding.AuthorityDigest), ["cursor"] = Text(play.ReplicaCursor),
            ["capabilities"] = new(Chummer.Contracts.Rulesets.RulesetCapabilityValueKinds.List, Items: chunks) };
        // Leave room for the envelope and both existing journal/transport bounds.
        if (chunks.Length > 64 || System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload, Json).Length > Math.Min(SessionPlayLimits.MaxRequestBytes - 4096, 60000)) return null;
        return new(added.Casting, added.Sustaining, payload);
    }

    internal static void ValidateCurrent(SessionPlayOrigin origin, SessionPlayAuthority current,
        IReadOnlyList<SessionEventEnvelope> history, SessionPlayCapabilityHistory7494? continuation = null)
    {
        SessionPlayIndirectUpgrade5338.ValidateCurrent(origin, current, history, continuation);
        var magic = history.LastOrDefault(e => e.EventType == SessionPlayEventTypes.MagicCapabilitiesAdopt);
        if (magic is not null)
            Conflict(MagicAuthority7494(origin, current, history.TakeWhile(e => e.EventId != magic.EventId).ToArray(), continuation).Binding.AuthorityDigest
                == magic.Payload["authority"].StringValue, "magic-capability-authority-changed");
        var initiative = history.LastOrDefault(e => e.EventType == SessionPlayEventTypes.InitiativeCapabilitiesAdopt);
        // An older initiative receipt bound the capabilities present at THAT boundary.
        // Later magic consent may add fields, but never invalidate or rewrite that receipt.
        var atInitiative = initiative is null ? history : history.TakeWhile(e => e.EventId != initiative.EventId).ToArray();
        SessionPlayInitiativeUpgrade5110.ValidateCurrent(CurrentCapabilities(origin, current, atInitiative, continuation), history);
    }

    private static SessionPlayAuthority MagicAuthority7494(SessionPlayOrigin origin, SessionPlayAuthority current,
        IReadOnlyList<SessionEventEnvelope> prefix, SessionPlayCapabilityHistory7494? continuation)
    {
        if (continuation is null) return AtMagicAdoption(origin, current, prefix);
        var prior = continuation.At(origin, prefix);
        var source = continuation.SourceAt(current, prefix.Count);
        return Rebind(prior with { CastingAuthority = prior.CastingAuthority ?? WithoutIndirect(source.CastingAuthority),
            SustainingAuthority = prior.SustainingAuthority ?? source.SustainingAuthority });
    }

    internal static void ValidateNew(SessionPlayOrigin origin, SessionPlayAuthority current,
        SessionOverlaySnapshot before, SessionEventEnvelope e, int eventCount, SessionPlayCapabilityHistory7494? continuation = null)
    {
        Require(eventCount == 1, "magic-adoption-requires-single-event");
        var offer = Offer(origin, current, before, continuation);
        Conflict(offer is not null, "magic-adoption-unavailable");
        Conflict(Digest(e.Payload.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray())
            == Digest(offer!.Payload.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray()), "magic-adoption-review-changed");
    }

    internal static SessionPlayAuthority Replay(SessionPlayAuthority prior, SessionEventEnvelope e,
        IEnumerable<SessionEventEnvelope> prefix)
    {
        Require(prior.StandardWoundRules && e.Payload.Count == 3 && e.Payload.ContainsKey("authority")
            && e.Payload.ContainsKey("cursor") && e.Payload.TryGetValue("capabilities", out var encoded)
            && encoded.Kind == Chummer.Contracts.Rulesets.RulesetCapabilityValueKinds.List
            && encoded.Items is { Count: > 0 and <= 64 }, "magic-adoption-invalid");
        Conflict(e.Payload["cursor"].StringValue == Cursor(prefix), "magic-adoption-cursor-changed");
        var items = e.Payload["capabilities"].Items!;
        Require(items.All(i => i.Kind == Chummer.Contracts.Rulesets.RulesetCapabilityValueKinds.String
            && i.StringValue is { Length: > 0 and <= 1024 }), "magic-adoption-invalid");
        var added = System.Text.Json.JsonSerializer.Deserialize<Added>(string.Concat(items.Select(i => i.StringValue)), Json);
        Require(added is not null && (added.Casting is not null || added.Sustaining is not null)
            && (added.Casting is null || prior.CastingAuthority is null)
            && (added.Sustaining is null || prior.SustainingAuthority is null), "magic-adoption-invalid");
        var next = Rebind(prior with { CastingAuthority = added!.Casting ?? prior.CastingAuthority,
            SustainingAuthority = added.Sustaining ?? prior.SustainingAuthority });
        Conflict(next.Binding.AuthorityDigest == e.Payload["authority"].StringValue, "magic-adoption-authority-invalid");
        return next;
    }

    private static Chummer.Contracts.Rulesets.RulesetCapabilityValue Text(string value)
        => new(Chummer.Contracts.Rulesets.RulesetCapabilityValueKinds.String, StringValue: value);
}

// Independent append-only capability addition: prior direct authorities/receipts
// keep their exact bytes. Earlier magic/initiative boundaries are revalidated
// against only the capabilities actually available at those boundaries.
internal static class SessionPlayIndirectUpgrade5338
{
    private static SessionPlayAuthority Next(SessionPlayOrigin origin, SessionPlayAuthority current, IReadOnlyList<SessionEventEnvelope> prefix, bool elemental, SessionPlayCapabilityHistory7494? continuation = null)
    {
        var at = continuation?.At(origin, prefix) ?? SessionPlayMagicUpgrade5311.CurrentCapabilities(origin, current, prefix);
        var source = continuation?.SourceAt(current, prefix.Count) ?? current;
        Require(at.CastingAuthority is not null && source.CastingAuthority?.Indirect is not null, "indirect-capability-unavailable");
        var selected = elemental ? source.CastingAuthority : SessionPlayMagicUpgrade5311.WithoutElemental(source.CastingAuthority);
        var casting = at.CastingAuthority! with { Digest = "", Indirect = selected!.Indirect };
        at = at with { CastingAuthority = casting with { Digest = Digest(casting) } };
        if (origin.Authority.Derived.InitiativeModes is null && !prefix.Any(e => e.EventType == SessionPlayEventTypes.InitiativeCapabilitiesAdopt))
            at = at with { Derived = at.Derived with { InitiativeModes = null } };
        return SessionPlayMagicUpgrade5311.Rebind(at);
    }
    internal static SessionPlayIndirectUpgradeOffer? Offer(SessionPlayOrigin origin, SessionPlayAuthority current, SessionOverlaySnapshot before,
        SessionPlayCapabilityHistory7494? continuation = null)
    {
        if (!current.StandardWoundRules || before.Play?.Casting is null) return null;
        var source = continuation?.SourceAt(current, before.Play.History.Count) ?? current;
        if (source.CastingAuthority?.Indirect is not { } extra) return null;
        var prior = SessionPlayMagicUpgrade5311.CurrentCapabilities(origin, current, before.Play.History, continuation).CastingAuthority?.Indirect;
        if (prior is not null && (prior.Elemental is not null || extra.Elemental is null)) return null;
        var next = Next(origin, current, before.Play.History, extra.Elemental is not null, continuation);
        string json = System.Text.Json.JsonSerializer.Serialize(extra, Json);
        var chunks = Enumerable.Range(0, (json.Length + 1023) / 1024)
            .Select(i => new Chummer.Contracts.Rulesets.RulesetCapabilityValue(Chummer.Contracts.Rulesets.RulesetCapabilityValueKinds.String,
                StringValue: json.Substring(i * 1024, Math.Min(1024, json.Length - i * 1024)))).ToArray();
        var payload = new Dictionary<string, Chummer.Contracts.Rulesets.RulesetCapabilityValue> {
            ["authority"] = new(Chummer.Contracts.Rulesets.RulesetCapabilityValueKinds.String, StringValue: next.Binding.AuthorityDigest),
            ["cursor"] = new(Chummer.Contracts.Rulesets.RulesetCapabilityValueKinds.String, StringValue: before.Play.ReplicaCursor),
            ["capabilities"] = new(Chummer.Contracts.Rulesets.RulesetCapabilityValueKinds.List, Items: chunks) };
        if (chunks.Length > 64 || System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload, Json).Length > Math.Min(SessionPlayLimits.MaxRequestBytes - 4096, 60000)) return null;
        return new(extra, payload);
    }
    internal static void ValidateNew(SessionPlayOrigin origin, SessionPlayAuthority current, SessionOverlaySnapshot before, SessionEventEnvelope e, int count, SessionPlayCapabilityHistory7494? continuation = null)
    {
        Require(count == 1, "indirect-adoption-requires-single-event");
        var offer = Offer(origin, current, before, continuation);
        Conflict(offer is not null && Digest(e.Payload.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray())
            == Digest(offer.Payload.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray()), "indirect-adoption-review-changed");
    }
    internal static void ValidateCurrent(SessionPlayOrigin origin, SessionPlayAuthority current, IReadOnlyList<SessionEventEnvelope> history,
        SessionPlayCapabilityHistory7494? continuation = null)
    {
        foreach (var adopted in history.Where(e => e.EventType == SessionPlayEventTypes.IndirectCastingAdopt))
            Conflict(Next(origin, current, history.TakeWhile(e => e.EventId != adopted.EventId).ToArray(),
                ReadCapability(adopted).Elemental is not null, continuation).Binding.AuthorityDigest
                == adopted.Payload["authority"].StringValue, "indirect-capability-authority-changed");
    }
    internal static SessionPlayIndirectCapability ReadCapability(SessionEventEnvelope e)
    {
        Require(e.Payload.TryGetValue("capabilities", out var value) && value.Items is { Count: > 0 and <= 64 }
            && value.Items.All(i => i.Kind == Chummer.Contracts.Rulesets.RulesetCapabilityValueKinds.String
                && i.StringValue is { Length: > 0 and <= 1024 }), "indirect-adoption-invalid");
        var result = System.Text.Json.JsonSerializer.Deserialize<SessionPlayIndirectCapability>(string.Concat(value!.Items!.Select(i => i.StringValue)), Json);
        Require(result is not null, "indirect-adoption-invalid"); return result!;
    }
    internal static SessionPlayAuthority Replay(SessionPlayAuthority prior, SessionEventEnvelope e, IEnumerable<SessionEventEnvelope> prefix)
    {
        Require(prior.CastingAuthority is not null && e.Payload.Count == 3
            && e.Payload.ContainsKey("authority") && e.Payload.ContainsKey("cursor")
            && e.Payload.TryGetValue("capabilities", out var encoded)
            && encoded.Kind == Chummer.Contracts.Rulesets.RulesetCapabilityValueKinds.List
            && encoded.Items is { Count: > 0 and <= 64 }, "indirect-adoption-invalid");
        Conflict(e.Payload["cursor"].StringValue == Cursor(prefix), "indirect-adoption-cursor-changed");
        var items = e.Payload["capabilities"].Items!;
        Require(items.All(i => i.Kind == Chummer.Contracts.Rulesets.RulesetCapabilityValueKinds.String
            && i.StringValue is { Length: > 0 and <= 1024 }), "indirect-adoption-invalid");
        var extra = System.Text.Json.JsonSerializer.Deserialize<SessionPlayIndirectCapability>(string.Concat(items.Select(i => i.StringValue)), Json);
        Require(extra is not null && extra.Choices.All(c => c.Element is null && (c.IndirectKind is "indirect-single" or "indirect-area"))
            && (extra.Choices.Count > 0 || extra.Elemental?.Choices.Count > 0)
            && (extra.Elemental is null || extra.Elemental.Choices.Count > 0 && extra.Elemental.Choices.All(c =>
                (c.Element is "acid" or "fire" or "electricity" or "cold") && (c.IndirectKind is "indirect-single" or "indirect-area"))), "indirect-adoption-invalid");
        Require(prior.CastingAuthority!.Indirect is null || prior.CastingAuthority.Indirect.Elemental is null
            && extra!.Elemental is not null && Digest(prior.CastingAuthority.Indirect) == Digest(extra with { Elemental = null }),
            "indirect-adoption-cannot-replace-existing-capability");
        var casting = prior.CastingAuthority! with { Digest = "", Indirect = extra };
        var next = SessionPlayMagicUpgrade5311.Rebind(prior with { CastingAuthority = casting with { Digest = Digest(casting) } });
        Conflict(next.Binding.AuthorityDigest == e.Payload["authority"].StringValue, "indirect-adoption-authority-invalid");
        return next;
    }
}
