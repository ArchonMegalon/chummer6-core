using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

internal static class SessionPlaySustainingEffects
{
    internal static bool IsMechanical(string type) => type is SessionPlayEventTypes.SustainingApply or SessionPlayEventTypes.SustainingEnd;
    internal static bool IsMechanical(SessionPlayOperation operation) => operation.Events.Any(e => IsMechanical(e.EventType));

    internal static SessionPlaySustainingAuthority Bind(SessionPlaySustainingAuthority authority, SessionPlayBinding binding, string custody, string? environmentDigest)
    {
        var bound = authority with { Digest = "",
            Choices = authority.Choices.Select(o => o with { CapabilityId = Digest(new { binding, custody, o.CapabilityId }),
                EnvironmentDigest = environmentDigest ?? o.SourceDigest }).ToArray(),
            Baseline = authority.Baseline.Select(o => {
                var instance = o with { InstanceId = "baseline:" + Digest(new { binding, custody, o.BaselineIdentity }), StateDigest = "" };
                return instance with { StateDigest = Digest(instance) };
            }).ToArray() };
        return bound with { Digest = Digest(bound) };
    }

    internal static SessionPlaySustainingProjection Project(SessionPlaySustainingAuthority authority,
        IReadOnlyList<SessionPlayMechanicalEffect> instances)
    {
        Conflict(authority.Digest == Digest(authority with { Digest = "" }), "sustaining-authority-invalid");
        // This is an internal adapter to the one existing penalty kernel, not a
        // second rules implementation or projected XML sent to source admission.
        var graph = new XElement("character", new XElement("psyche", authority.Psyche),
            new XElement("spells", instances.Select(e => e.SpellId).Distinct().Select(id =>
                new XElement("spell", new XElement("guid", id.ToString("D"))))),
            new XElement("improvements", authority.Allowances.Select(a => new XElement("improvement",
                new XElement("improvementttype", "PenaltyFreeSustain"), new XElement("val", a.Value), new XElement("rating", a.Rating)))),
            new XElement("sustainedobjects", instances.Where(e => e.Active).Select(e => new XElement("sustainedobject",
                new XElement("linkedobjecttype", "Spell"), new XElement("linkedobject", e.SpellId.ToString("D")),
                new XElement("force", e.Force), new XElement("self", e.SelfSustained)))));
        var penalty = CharacterCreationNativeMagic1430.EvaluateSustainingPenalty(graph, authority.PerObject);
        return new(authority.Digest, authority.Choices, instances.ToArray(), penalty.Count, penalty.Penalty);
    }

    internal static void Apply(SessionPlaySustainingAuthority? authority, List<SessionPlayMechanicalEffect>? instances,
        SessionEventEnvelope e, IEnumerable<SessionEventEnvelope> prefix)
    {
        Require(authority is not null && instances is not null, "sustaining-authority-unavailable");
        bool start = e.EventType == SessionPlayEventTypes.SustainingApply;
        string[] keys = start ? ["authority", "cursor", "capability", "instance", "force", "hits"] : ["authority", "cursor", "instance", "state"];
        Require(e.Payload.Count == keys.Length && keys.All(e.Payload.ContainsKey), "sustaining-payload-shape");
        Conflict(Text(e, "authority") == authority!.Digest && Text(e, "cursor") == Cursor(prefix), "sustaining-review-changed");
        string id = Text(e, "instance");
        if (start)
        {
            var request = new SessionPlaySustainingApply(Text(e, "authority"), Text(e, "cursor"), Text(e, "capability"),
                id, Integer(e, "force"), Integer(e, "hits"));
            var choice = authority.Choices.SingleOrDefault(o => o.CapabilityId == request.CapabilityId);
            Require(choice is not null && id.StartsWith("local:", StringComparison.Ordinal)
                && Guid.TryParseExact(id[6..], "N", out var guid) && guid != Guid.Empty
                && instances!.All(o => o.InstanceId != id), "sustaining-choice-or-instance-invalid");
            Require(request.Force >= choice!.MinimumForce && request.Force <= choice.MaximumForce
                && request.NetHits >= choice.MinimumNetHits && request.NetHits <= choice.MaximumNetHits
                && instances!.Count < 1024, "sustaining-registration-outside-bounds");
            var added = new SessionPlayMechanicalEffect(id, choice.SpellId, choice.DisplayName,
                request.Force, request.NetHits, true, true, null, "");
            instances!.Add(added with { StateDigest = Digest(added) });
        }
        else
        {
            var request = new SessionPlaySustainingEnd(Text(e, "authority"), Text(e, "cursor"), id, Text(e, "state"));
            int index = instances!.FindIndex(o => o.InstanceId == request.InstanceId);
            Conflict(index >= 0 && instances[index].Active && instances[index].SelfSustained
                && instances[index].StateDigest == request.ExpectedStateDigest, "sustaining-instance-changed");
            var ended = instances[index] with { Active = false, StateDigest = "" };
            instances[index] = ended with { StateDigest = Digest(ended) };
        }
    }

    internal static CharacterSheetDerivedSection Recalculate(CharacterSheetDerivedSection derived,
        SessionPlaySustainingAuthority authority, SessionPlaySustainingProjection state, int wound)
    {
        var basis = authority.Basis;
        var skills = basis.Skills.ToDictionary(s => s.Id, StringComparer.Ordinal);
        int adjustment = checked(wound + state.DicePoolPenalty);
        var other = derived.OtherPoolsAndModifiers!.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        // Existing Core Composure/Memory consumers are unclamped and ignore wounds.
        other["Composure"] = checked(basis.Composure + state.DicePoolPenalty);
        other["Memory"] = checked(basis.Memory + state.DicePoolPenalty);
        return derived with {
            DefensePool = basis.RawDefense is { } defense ? Math.Max(0, checked(defense + adjustment)) : null,
            Skills = derived.Skills.Select(s => !skills.TryGetValue(s.Guid, out var raw) ? s : s with {
                DicePool = s.DicePool is null ? null : raw.CanRoll ? Math.Max(0, checked(raw.RawPool + adjustment)) : 0,
                Specializations = s.Specializations.Select(p => p with { DicePool = Math.Max(0, checked(raw.RawPool + p.Bonus + adjustment)) }).ToArray()
            }).ToArray(),
            OtherPoolsAndModifiers = other,
            // Local instances have their own identities. Do not manufacture saved
            // XML occurrence identities or offer a canonical registration action.
            SustainedSpellPreparation = null
        };
    }
    private static string Text(SessionEventEnvelope e, string key)
    {
        var v = e.Payload[key];
        Require(v.Kind == RulesetCapabilityValueKinds.String && v.StringValue is not null && v.IntegerValue is null
            && v.BooleanValue is null && v.NumberValue is null && v.DecimalValue is null && v.Items is null && v.Properties is null,
            "sustaining-string-required");
        return v.StringValue!;
    }
    private static int Integer(SessionEventEnvelope e, string key)
    {
        var v = e.Payload[key];
        Require(v.Kind == RulesetCapabilityValueKinds.Integer && v.IntegerValue is not null && v.StringValue is null
            && v.BooleanValue is null && v.NumberValue is null && v.DecimalValue is null && v.Items is null && v.Properties is null,
            "sustaining-integer-required");
        return checked((int)v.IntegerValue!.Value);
    }
}
