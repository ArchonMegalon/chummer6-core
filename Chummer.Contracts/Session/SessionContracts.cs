using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Trackers;

namespace Chummer.Contracts.Session;

public static class SessionEventEnvelopeSchemas
{
    public const string SessionEventsVnext = "session_events_vnext";
}

public static class SessionEventTypes
{
    public const string TrackerIncrement = "tracker.increment";
    public const string TrackerDecrement = "tracker.decrement";
    public const string ResourceSpend = "resource.spend";
    public const string ResourceRestore = "resource.restore";
    public const string AmmoSpend = "ammo.spent";
    public const string AmmoReload = "ammo.reloaded";
    public const string EffectAdd = "effect.applied";
    public const string EffectRemove = "effect.removed";
    public const string QuickActionPin = "quickaction.pin";
    public const string QuickActionUnpin = "quickaction.unpin";
    public const string NoteAppend = "note.added";
    public const string NoteReplace = "note.replace";
    public const string SelectionSet = "pin.changed";
}

public static class SessionSyncStatuses
{
    public const string LocalOnly = "local-only";
    public const string PendingSync = "pending-sync";
    public const string Synced = "synced";
    public const string Replayed = "replayed";
    public const string Conflict = "conflict";
}

public sealed record SessionEventEnvelope(
    string EventId,
    string OverlayId,
    CharacterVersionReference BaseCharacterVersion,
    string DeviceId,
    string ActorId,
    long Sequence,
    string EventType,
    IReadOnlyDictionary<string, RulesetCapabilityValue> Payload,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AppliedAtUtc = null,
    string? ParentEventId = null,
    string? SyncCursor = null,
    string? ProviderId = null,
    string? PackId = null,
    string Schema = SessionEventEnvelopeSchemas.SessionEventsVnext);

[Obsolete("Compatibility-only. Use SessionEventEnvelope.")]
public sealed record SessionEvent(
    string EventId,
    string OverlayId,
    CharacterVersionReference BaseCharacterVersion,
    string DeviceId,
    string ActorId,
    long Sequence,
    string EventType,
    string PayloadJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AppliedAtUtc = null,
    string? ParentEventId = null,
    string? SyncCursor = null)
{
    public SessionEventEnvelope ToEnvelope()
        => new(
            EventId,
            OverlayId,
            BaseCharacterVersion,
            DeviceId,
            ActorId,
            Sequence,
            EventType,
            ParsePayload(PayloadJson),
            CreatedAtUtc,
            AppliedAtUtc,
            ParentEventId,
            SyncCursor);

    public static SessionEvent FromEnvelope(SessionEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return new SessionEvent(
            envelope.EventId,
            envelope.OverlayId,
            envelope.BaseCharacterVersion,
            envelope.DeviceId,
            envelope.ActorId,
            envelope.Sequence,
            envelope.EventType,
            SerializePayload(envelope.Payload),
            envelope.CreatedAtUtc,
            envelope.AppliedAtUtc,
            envelope.ParentEventId,
            envelope.SyncCursor);
    }

    private static IReadOnlyDictionary<string, RulesetCapabilityValue> ParsePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal);
        }

        using JsonDocument document = JsonDocument.Parse(payloadJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal);
        }

        return document.RootElement.EnumerateObject().ToDictionary(
            static property => property.Name,
            static property => FromJsonElement(property.Value),
            StringComparer.Ordinal);
    }

    private static string SerializePayload(IReadOnlyDictionary<string, RulesetCapabilityValue> payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Dictionary<string, object?> rawPayload = payload.ToDictionary(
            static pair => pair.Key,
            static pair => RulesetCapabilityBridge.ToObject(pair.Value),
            StringComparer.Ordinal);

        return JsonSerializer.Serialize(rawPayload);
    }

    private static RulesetCapabilityValue FromJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => new RulesetCapabilityValue(RulesetCapabilityValueKinds.Null),
            JsonValueKind.String => new RulesetCapabilityValue(RulesetCapabilityValueKinds.String, StringValue: element.GetString()),
            JsonValueKind.True => new RulesetCapabilityValue(RulesetCapabilityValueKinds.Boolean, BooleanValue: true),
            JsonValueKind.False => new RulesetCapabilityValue(RulesetCapabilityValueKinds.Boolean, BooleanValue: false),
            JsonValueKind.Number when element.TryGetInt64(out long integerValue) => new RulesetCapabilityValue(
                RulesetCapabilityValueKinds.Integer,
                IntegerValue: integerValue),
            JsonValueKind.Number when element.TryGetDecimal(out decimal decimalValue) => new RulesetCapabilityValue(
                RulesetCapabilityValueKinds.Decimal,
                DecimalValue: decimalValue),
            JsonValueKind.Number => new RulesetCapabilityValue(
                RulesetCapabilityValueKinds.Number,
                NumberValue: element.GetDouble()),
            JsonValueKind.Array => new RulesetCapabilityValue(
                RulesetCapabilityValueKinds.List,
                Items: element.EnumerateArray().Select(FromJsonElement).ToArray()),
            JsonValueKind.Object => new RulesetCapabilityValue(
                RulesetCapabilityValueKinds.Object,
                Properties: element.EnumerateObject().ToDictionary(
                    static property => property.Name,
                    static property => FromJsonElement(property.Value),
                    StringComparer.Ordinal)),
            _ => new RulesetCapabilityValue(RulesetCapabilityValueKinds.String, StringValue: element.GetRawText())
        };
    }
}

public sealed record SessionLedger(
    string OverlayId,
    CharacterVersionReference BaseCharacterVersion,
    IReadOnlyList<SessionEventEnvelope> Events,
    string? BaselineSnapshotId = null,
    long NextSequence = 0);

public sealed record SessionEffectState(
    string EffectId,
    string Label,
    bool IsActive,
    string? SourceEventId = null);

public sealed record SessionQuickActionPin(
    string ActionId,
    string Label,
    string CapabilityId,
    bool IsPinned = true);

public sealed record SessionSyncState(
    string Status,
    int PendingEventCount,
    DateTimeOffset? LastSyncedAtUtc,
    bool WasReplayed = false,
    bool RuntimeFingerprintMismatch = false);

public sealed record SessionOverlaySnapshot(
    string OverlayId,
    CharacterVersionReference BaseCharacterVersion,
    IReadOnlyList<TrackerSnapshot> Trackers,
    IReadOnlyList<SessionEffectState> ActiveEffects,
    IReadOnlyList<SessionQuickActionPin> PinnedQuickActions,
    IReadOnlyList<string> Notes,
    SessionSyncState SyncState);

public sealed record SessionRuntimeBundle(
    string BundleId,
    CharacterVersionReference BaseCharacterVersion,
    string EngineApiVersion,
    DateTimeOffset SignedAtUtc,
    string Signature,
    IReadOnlyList<SessionQuickActionPin> QuickActions,
    IReadOnlyList<TrackerDefinition> Trackers,
    IReadOnlyDictionary<string, string> ReducerBindings);
