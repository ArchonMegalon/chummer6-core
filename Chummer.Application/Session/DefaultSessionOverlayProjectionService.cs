using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public sealed class DefaultSessionOverlayProjectionService : ISessionOverlayProjectionService
{
    public SessionOverlayProjection Replay(
        string overlayId,
        string characterId,
        string runtimeFingerprint,
        IReadOnlyList<SessionEventEnvelope> events)
    {
        Dictionary<string, int> trackers = new(StringComparer.Ordinal);
        HashSet<string> effects = new(StringComparer.Ordinal);
        HashSet<string> pins = new(StringComparer.Ordinal);
        List<string> notes = [];
        List<RulesetCapabilityDiagnostic> diagnostics = [];

        SessionEventEnvelope[] orderedEvents = events
            .OrderBy(static candidate => candidate.Sequence)
            .ThenBy(static candidate => candidate.CreatedAtUtc)
            .ThenBy(static candidate => candidate.EventId, StringComparer.Ordinal)
            .ToArray();

        foreach (SessionEventEnvelope item in orderedEvents)
        {
            if (!SessionOverlayEventValidator.AllowsEvent(item, diagnostics))
            {
                continue;
            }

            switch (item.EventType)
            {
                case SessionOverlayEventKinds.TrackerIncrement:
                    ApplyTrackerDelta(item, trackers, +1, diagnostics);
                    break;
                case SessionOverlayEventKinds.TrackerDecrement:
                    ApplyTrackerDelta(item, trackers, -1, diagnostics);
                    break;
                case SessionOverlayEventKinds.EffectApplied:
                    ApplyEffect(item, effects, enabled: true, diagnostics);
                    break;
                case SessionOverlayEventKinds.EffectRemoved:
                    ApplyEffect(item, effects, enabled: false, diagnostics);
                    break;
                case SessionOverlayEventKinds.NoteAdded:
                    if (TryGetString(item.Payload, "note", out string? note) && note is not null)
                    {
                        notes.Add(note);
                    }
                    break;
                case SessionOverlayEventKinds.PinChanged:
                    ApplyPin(item, pins, diagnostics);
                    break;
            }
        }

        return new SessionOverlayProjection(
            OverlayId: overlayId,
            CharacterId: characterId,
            RuntimeFingerprint: runtimeFingerprint,
            AppliedEvents: orderedEvents,
            Trackers: trackers
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => new SessionOverlayTrackerState(pair.Key, pair.Value))
                .ToArray(),
            ActiveEffects: effects.OrderBy(static effect => effect, StringComparer.Ordinal).ToArray(),
            Notes: notes,
            PinnedActionIds: pins.OrderBy(static action => action, StringComparer.Ordinal).ToArray(),
            Diagnostics: diagnostics);
    }

    private static void ApplyTrackerDelta(
        SessionEventEnvelope item,
        IDictionary<string, int> trackers,
        int direction,
        ICollection<RulesetCapabilityDiagnostic> diagnostics)
    {
        if (!TryGetString(item.Payload, "trackerId", out string? trackerId) || trackerId is null)
        {
            diagnostics.Add(new RulesetCapabilityDiagnostic(
                "session.replay.tracker.missing-id",
                "session.replay.tracker.missing-id",
                MessageKey: "session.replay.tracker.missing-id"));
            return;
        }

        int amount = 1;
        if (item.Payload.TryGetValue("amount", out RulesetCapabilityValue? amountValue)
            && amountValue.IntegerValue is long integerAmount)
        {
            amount = (int)integerAmount;
        }

        int previous = trackers.TryGetValue(trackerId, out int current) ? current : 0;
        trackers[trackerId] = previous + (amount * direction);
    }

    private static void ApplyEffect(
        SessionEventEnvelope item,
        ISet<string> effects,
        bool enabled,
        ICollection<RulesetCapabilityDiagnostic> diagnostics)
    {
        if (!TryGetString(item.Payload, "effectId", out string? effectId) || effectId is null)
        {
            diagnostics.Add(new RulesetCapabilityDiagnostic(
                "session.replay.effect.missing-id",
                "session.replay.effect.missing-id",
                MessageKey: "session.replay.effect.missing-id"));
            return;
        }

        if (enabled)
        {
            effects.Add(effectId);
        }
        else
        {
            effects.Remove(effectId);
        }
    }

    private static void ApplyPin(
        SessionEventEnvelope item,
        ISet<string> pins,
        ICollection<RulesetCapabilityDiagnostic> diagnostics)
    {
        if (!TryGetString(item.Payload, "actionId", out string? actionId) || actionId is null)
        {
            diagnostics.Add(new RulesetCapabilityDiagnostic(
                "session.replay.pin.missing-id",
                "session.replay.pin.missing-id",
                MessageKey: "session.replay.pin.missing-id"));
            return;
        }

        bool isPinned = true;
        if (item.Payload.TryGetValue("isPinned", out RulesetCapabilityValue? pinValue)
            && pinValue.BooleanValue is bool value)
        {
            isPinned = value;
        }

        if (isPinned)
        {
            pins.Add(actionId);
        }
        else
        {
            pins.Remove(actionId);
        }
    }

    private static bool TryGetString(
        IReadOnlyDictionary<string, RulesetCapabilityValue> payload,
        string key,
        out string? value)
    {
        value = null;
        if (!payload.TryGetValue(key, out RulesetCapabilityValue? capabilityValue))
        {
            return false;
        }

        value = capabilityValue.StringValue;
        return !string.IsNullOrWhiteSpace(value);
    }
}
