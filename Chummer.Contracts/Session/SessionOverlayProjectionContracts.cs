using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Session;

public static class SessionOverlayEventKinds
{
    public const string TrackerIncrement = SessionEventTypes.TrackerIncrement;
    public const string TrackerDecrement = SessionEventTypes.TrackerDecrement;
    public const string EffectApplied = SessionEventTypes.EffectAdd;
    public const string EffectRemoved = SessionEventTypes.EffectRemove;
    public const string AmmoSpent = SessionEventTypes.AmmoSpend;
    public const string AmmoReloaded = SessionEventTypes.AmmoReload;
    public const string NoteAdded = SessionEventTypes.NoteAppend;
    public const string PinChanged = SessionEventTypes.SelectionSet;
}

[Obsolete("Compatibility-only. Use SessionEventEnvelope.")]
public sealed record SessionOverlayEventDto(
    string EventId,
    long Sequence,
    string EventType,
    IReadOnlyDictionary<string, RulesetCapabilityValue> Payload,
    DateTimeOffset CreatedAtUtc,
    string? ParentEventId = null,
    string? ProviderId = null,
    string? PackId = null)
{
    public SessionEventEnvelope ToEnvelope(string overlayId, CharacterVersionReference baseCharacterVersion, string deviceId, string actorId)
        => new(
            EventId,
            overlayId,
            baseCharacterVersion,
            deviceId,
            actorId,
            Sequence,
            EventType,
            Payload,
            CreatedAtUtc,
            ParentEventId: ParentEventId,
            ProviderId: ProviderId,
            PackId: PackId);

    public static SessionOverlayEventDto FromEnvelope(SessionEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return new SessionOverlayEventDto(
            envelope.EventId,
            envelope.Sequence,
            envelope.EventType,
            envelope.Payload,
            envelope.CreatedAtUtc,
            envelope.ParentEventId,
            envelope.ProviderId,
            envelope.PackId);
    }
}

public sealed record SessionOverlayTrackerState(
    string TrackerId,
    int CurrentValue);

public sealed record SessionOverlayProjection(
    string OverlayId,
    string CharacterId,
    string RuntimeFingerprint,
    IReadOnlyList<SessionEventEnvelope> AppliedEvents,
    IReadOnlyList<SessionOverlayTrackerState> Trackers,
    IReadOnlyList<string> ActiveEffects,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> PinnedActionIds,
    IReadOnlyList<RulesetCapabilityDiagnostic> Diagnostics);
