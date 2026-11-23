using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

/// <summary>One explicitly reviewed, append-only capability boundary. The origin is never rewritten.</summary>
internal static class SessionPlayInitiativeUpgrade5110
{
    public static SessionPlayInitiativeUpgradeOffer? Offer(SessionPlayOrigin origin,
        SessionPlayAuthority current, SessionOverlaySnapshot before)
    {
        if (origin.Authority.Derived.InitiativeModes is not null || before.Play!.Derived.InitiativeModes is not null
            || !current.StandardWoundRules || current.Derived.InitiativeModes is not { } modes
            || !modes.Any(m => m.IsAvailable && m.Mode != CharacterInitiativeMode.Physical)) return null;
        var physical = CharacterInitiativeModes.Find(current.Derived, CharacterInitiativeMode.Physical);
        if (physical is null || physical.Value != origin.Authority.Derived.PhysicalInitiative?.Value
            || physical.Dice != origin.Authority.Derived.PhysicalInitiative?.Dice) return null;
        var payload = new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)
        {
            ["authority"] = Text(current.Binding.AuthorityDigest),
            ["cursor"] = Text(before.Play.ReplicaCursor),
            ["modes"] = new(RulesetCapabilityValueKinds.List,
                Items: modes.Select(m => Text(JsonSerializer.Serialize(m, Json))).ToArray())
        };
        // Preserve existing transport bounds; never offer an unsubmitable review.
        if (modes.Count > 5 || payload["modes"].Items!.Any(m => m.StringValue!.Length > SessionPlayLimits.MaxNoteLength))
            return null;
        return new(modes, payload);
    }

    public static void ValidateCurrent(SessionPlayAuthority current, IReadOnlyList<SessionEventEnvelope> history)
    {
        var adoption = history.LastOrDefault(e => e.EventType == SessionPlayEventTypes.InitiativeCapabilitiesAdopt);
        if (adoption is not null)
            Conflict(adoption.Payload["authority"].StringValue == current.Binding.AuthorityDigest,
                "initiative-capability-authority-changed");
    }

    public static void ValidateNew(SessionPlayOrigin origin, SessionPlayAuthority current,
        SessionOverlaySnapshot before, SessionEventEnvelope e, int eventCount)
    {
        // An adoption is its own operation. No batch may hide dice or a second mutation behind this consent.
        Require(eventCount == 1, "initiative-adoption-requires-single-event");
        var offer = Offer(origin, current, before);
        Conflict(offer is not null, "initiative-adoption-unavailable");
        Conflict(Digest(e.Payload.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray())
            == Digest(offer!.Payload.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray()),
            "initiative-adoption-review-changed");
    }

    public static SessionPlayAuthority Replay(SessionPlayAuthority prior, SessionEventEnvelope e,
        IEnumerable<SessionEventEnvelope> prefix)
    {
        Require(prior.Derived.InitiativeModes is null && prior.StandardWoundRules
            && e.Payload.Count == 3 && e.Payload.ContainsKey("authority") && e.Payload.ContainsKey("cursor")
            && e.Payload.TryGetValue("modes", out var encoded) && encoded.Kind == RulesetCapabilityValueKinds.List,
            "initiative-adoption-invalid");
        Conflict(e.Payload["cursor"].StringValue == Cursor(prefix), "initiative-adoption-cursor-changed");
        var items = e.Payload["modes"].Items;
        Require(items is { Count: > 0 and <= 5 }, "initiative-adoption-modes-invalid");
        var modes = items!.Select(m => {
            Require(m.Kind == RulesetCapabilityValueKinds.String && m.StringValue is not null, "initiative-adoption-mode-invalid");
            return JsonSerializer.Deserialize<CharacterInitiativeModeOption>(m.StringValue!, Json)!;
        }).ToArray();
        Require(modes.All(m => m is not null && Enum.IsDefined(m.Mode) && m.Prerequisites is not null)
            && modes.Select(m => m.Mode).Distinct().Count() == modes.Length, "initiative-adoption-modes-invalid");
        var next = prior with { Derived = prior.Derived with { InitiativeModes = modes },
            Binding = prior.Binding with { AuthorityDigest = string.Empty } };
        // These are the exact source-authority bytes admitted at the durable boundary,
        // not a new resolver call that could change old outcomes during replay.
        Conflict(Digest(next) == e.Payload["authority"].StringValue, "initiative-adoption-authority-invalid");
        var physical = CharacterInitiativeModes.Find(next.Derived, CharacterInitiativeMode.Physical);
        Require(physical is not null && physical.Value == prior.Derived.PhysicalInitiative?.Value
            && physical.Dice == prior.Derived.PhysicalInitiative?.Dice, "initiative-adoption-physical-changed");
        return next with { Binding = next.Binding with { AuthorityDigest = e.Payload["authority"].StringValue! } };
    }

    private static RulesetCapabilityValue Text(string value) => new(RulesetCapabilityValueKinds.String, StringValue: value);
}
