using System.Text.Json;
using System.Xml;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public sealed partial class DurableSessionPlayService(ISessionPlayStore store, ISessionPlayAuthorityResolver authority)
{
    public SessionApiResult<SessionDashboardProjection> GetCharacterProjection(OwnerScope owner, string characterId)
        => Guard(() => store.Execute(owner, characterId, tx => {
            var origin = Origin(tx, owner, characterId, out var current);
            var operations = tx.ReadOperations();
            var pending = operations.LastOrDefault(o => o.Result is null);
            if (pending is not null) throw new SessionPlayException(SessionPlayFailures.Unavailable("pending-operation-requires-recovery")
                with { OperationId = pending.Intent.OperationId });
            var history = History(operations);
            var snapshot = Replay(tx, origin, history, operations.LastOrDefault(o => o.Result?.Snapshot is not null)?.Intent.OperationId ?? "", out var elemental);
            CheckReplay(tx, operations, snapshot);
            SessionPlayMagicUpgrade5311.ValidateCurrent(origin, current, history, CapabilityHistory7494(tx));
            snapshot = snapshot with { BaseCharacterVersion = CurrentPlayVersion5712(tx, current) };
            return Dashboard(origin, snapshot, CurrentPlayBinding5712(tx, origin.Authority)) with {
                ElementalEffects = elemental.Effects.Count == 0 ? null : elemental,
                InitiativeUpgrade = SessionPlayInitiativeUpgrade5110.Offer(origin,
                    SessionPlayMagicUpgrade5311.CurrentCapabilities(origin, current, history, CapabilityHistory7494(tx)), snapshot),
                MagicUpgrade = SessionPlayMagicUpgrade5311.Offer(origin, current, snapshot, CapabilityHistory7494(tx)),
                IndirectUpgrade = SessionPlayIndirectUpgrade5338.Offer(origin, current, snapshot, CapabilityHistory7494(tx)) };
        }));

    public SessionApiResult<SessionOverlaySnapshot> ApplyCharacterPatches(OwnerScope owner, string characterId, SessionPatchRequest? request)
        => Guard(() => {
            Require(request is not null, "request-required");
            var result = Execute(owner, characterId, Canonical(request!.OperationId, "apply", request.OverlayId,
                request.BaseCharacterVersion, request.Events, null));
            if (result.Failure is not null) throw new SessionPlayException(result.Failure);
            return result.Snapshot!;
        });

    public SessionApiResult<SessionSyncReceipt> SyncCharacterLedger(OwnerScope owner, string characterId, SessionSyncBatch? batch)
        => Guard(() => {
            Require(batch is not null, "request-required");
            var result = Execute(owner, characterId, Canonical(batch!.OperationId, "sync", batch.OverlayId,
                batch.BaseCharacterVersion, batch.Events, batch.ClientCursor));
            // Sync conflicts have a typed receipt as well as their durable failure. The receipt never claims server sync.
            if (result.Sync is not null) return result.Sync;
            if (result.Failure is not null) throw new SessionPlayException(result.Failure);
            throw new SessionPlayException(SessionPlayFailures.Unavailable("sync-result-unavailable"));
        });

    public SessionApiResult<SessionPlayOperationResult> RecoverCharacterOperation(OwnerScope owner, string characterId, string operationId)
        => Guard(() => {
            Identity(operationId, "operation-id-required");
            return store.Execute(owner, characterId, tx => {
                var operations = tx.ReadOperations();
                var original = operations.SingleOrDefault(o => o.Intent.OperationId == operationId);
                if (original is null) throw new SessionPlayException(SessionPlayFailures.Conflict("operation-not-prepared"));
                if (original.Result is not null) return original.Result;
                RefuseMechanicalReplay5262(original.Intent);
                var origin = tx.ReadOrigin() ?? throw new SessionPlayException(SessionPlayFailures.Conflict("journal-origin-missing"));
                return Finish(tx, owner, characterId, origin, operations, original);
            });
        });

    private SessionPlayOperationResult Execute(OwnerScope owner, string characterId, SessionPlayOperation intent)
        => store.Execute(owner, characterId, tx => {
            var operations = tx.ReadOperations();
            var original = operations.SingleOrDefault(o => o.Intent.OperationId == intent.OperationId);
            if (original is not null)
            {
                Conflict(original.Intent.PayloadDigest == intent.PayloadDigest, "operation-payload-mismatch");
                if (original.Result is not null) return original.Result;
                RefuseMechanicalReplay5262(original.Intent);
                return Finish(tx, owner, characterId, tx.ReadOrigin()!, operations, original);
            }
            var pending = operations.LastOrDefault(o => o.Result is null);
            if (pending is not null) throw new SessionPlayException(SessionPlayFailures.Unavailable("pending-operation-requires-recovery")
                with { OperationId = pending.Intent.OperationId });
            Require(!intent.Events.Any(e => e.EventType == SessionPlayEventTypes.ElementalFollowup) || intent.Events.Count == 1, "elemental-followup-requires-single-event");
            Require(!SessionPlaySustainingEffects.IsMechanical(intent) || intent.Events.Count == 1, "sustaining-requires-single-event");
            Require(!intent.Events.Any(e => (e.EventType is SessionPlayEventTypes.SpellCast or SessionPlayEventTypes.IndirectSpellCast)) || intent.Events.Count == 1,
                "casting-requires-single-event");
            Require(!intent.Events.Any(e => e.EventType == SessionPlayConditionCommand5412.EventType) || intent.Events.Count == 1,
                "condition-recovery-requires-single-event");
            Conflict(tx.ReadConditionTransfers().All(t => t.Intent.Review.OperationId != intent.OperationId), "operation-identity-already-used");
            var origin = Origin(tx, owner, characterId, out _);
            Conflict(intent.OverlayId == origin.OverlayId, "operation-overlay-mismatch");
            Conflict(origin.Authority.SettledOperations5712?.All(x => x.OperationId != intent.OperationId) != false,
                "ammunition-operation-already-settled");
            // This is the first durable dispatch boundary. Nothing has been applied before Prepare returns.
            var prepared = tx.Prepare(intent);
            return Finish(tx, owner, characterId, origin, operations, prepared);
        });

    private SessionPlayOrigin Origin(ISessionPlayTransaction tx, OwnerScope owner, string characterId, out SessionPlayAuthority current)
    {
        current = ResolveCurrentWithContinuation(tx, owner, characterId);
        ValidateAuthority(current, owner, characterId);
        var existing = tx.ReadOrigin();
        if (existing is not null)
        {
            Conflict(MatchesOrigin5082(existing.Authority, current), "canonical-base-or-runtime-changed");
            return existing;
        }
        var origin = new SessionPlayOrigin(OverlayId(current.Binding), current, DateTimeOffset.UtcNow);
        tx.CreateOrigin(origin);
        return origin;
    }

    // Old immutable origins retain exact projections and operation digests.
    // Compatibility removes only capabilities absent from that original origin;
    // every source/version/runtime and pre-existing authority byte must still match.
    // Adopted capabilities are separately checked against their original event.
    private static bool MatchesOrigin5082(SessionPlayAuthority origin, SessionPlayAuthority current)
    {
        current = SessionPlayMagicUpgrade5311.OriginalCapabilities(origin, current);
        if (origin.Binding == current.Binding) return true;
        if (origin.Derived.InitiativeModes is not null || current.Derived.InitiativeModes is null) return false;
        var legacy = current with { Derived = current.Derived with { InitiativeModes = null },
            Binding = current.Binding with { AuthorityDigest = string.Empty } };
        return origin.Binding == (current.Binding with { AuthorityDigest = Digest(legacy) });
    }

    private SessionPlayOperationResult Finish(ISessionPlayTransaction tx, OwnerScope owner, string characterId,
        SessionPlayOrigin origin, IReadOnlyList<SessionPlayStoredOperation> operations, SessionPlayStoredOperation operation)
    {
        var intent = operation.Intent;
        SessionPlayOperationResult result;
        try
        {
            var current = ResolveCurrentWithContinuation(tx, owner, characterId);
            ValidateAuthority(current, owner, characterId);
            Conflict(MatchesOrigin5082(origin.Authority, current), "canonical-base-or-runtime-changed");
            Conflict(intent.OverlayId == origin.OverlayId && intent.BaseCharacterVersion == CurrentPlayVersion5712(tx, current),
                "operation-base-runtime-edition-mismatch");
            Conflict(Canonical(intent.OperationId, intent.Kind, intent.OverlayId, intent.BaseCharacterVersion,
                intent.Events, intent.ClientCursor).PayloadDigest == intent.PayloadDigest, "journal-intent-payload-divergence");
            var history = History(operations);
            var before = Replay(tx, origin, history, operations.LastOrDefault(o => o.Result?.Snapshot is not null)?.Intent.OperationId ?? "");
            CheckReplay(tx, operations, before);
            SessionPlayMagicUpgrade5311.ValidateCurrent(origin, current, history, CapabilityHistory7494(tx));
            if (intent.ClientCursor is not null)
            {
                bool ancestor = intent.ClientCursor == Cursor([]) || intent.ClientCursor == before.Play!.ReplicaCursor
                    || operations.Any(o => o.Result?.Snapshot?.Play?.ReplicaCursor == intent.ClientCursor);
                Conflict(ancestor, "replica-cursor-divergence");
            }
            var merged = history.ToList();
            var byId = history.ToDictionary(e => e.EventId, StringComparer.Ordinal);
            int accepted = 0, replayed = 0;
            foreach (var e in intent.Events)
            {
                ValidateCurrentEnvelope5712(tx, origin, e);
                if (byId.TryGetValue(e.EventId, out var previous))
                {
                    Conflict(Digest(previous) == Digest(e), "event-identity-payload-mismatch");
                    replayed++;
                    continue;
                }
                Conflict(e.Sequence == merged.Count + 1L && e.ParentEventId == merged.LastOrDefault()?.EventId,
                    "event-order-divergence");
                Require(merged.Count < SessionPlayLimits.MaxEvents, "event-capacity-reached");
                if (e.EventType == SessionPlayEventTypes.InitiativeCapabilitiesAdopt)
                    SessionPlayInitiativeUpgrade5110.ValidateNew(origin,
                        SessionPlayMagicUpgrade5311.CurrentCapabilities(origin, current, history, CapabilityHistory7494(tx)), before, e, intent.Events.Count);
                if (e.EventType == SessionPlayEventTypes.IndirectCastingAdopt)
                    SessionPlayIndirectUpgrade5338.ValidateNew(origin, current, before, e, intent.Events.Count, CapabilityHistory7494(tx));
                if (e.EventType == SessionPlayEventTypes.MagicCapabilitiesAdopt)
                    SessionPlayMagicUpgrade5311.ValidateNew(origin, current, before, e, intent.Events.Count, CapabilityHistory7494(tx));
                if (e.EventType == SessionPlayEventTypes.ElementalFollowup)
                {
                    SessionPlayElementalFollowups.ValidateNewVehiclePolicy5400(e, current);
                    SessionPlayElementalFollowups.ValidateNewCollisionTiming5411(e);
                    SessionPlayElementalFollowups.ValidateNewCollisionTransitions5420(e);
                }
                // This guard needs the same admitted historical checkpoints as the final replay.
                // Replaying only the original origin rejects valid post-settlement/focus events.
                SessionPlayElementalFollowups.ValidateNewOwnedCollisionAction5420(origin, current, merged, e,
                    replay: () => {
                        var prefixState = Replay(tx, origin, merged, "", out var elemental);
                        return (prefixState, elemental);
                    });
                byId.Add(e.EventId, e); merged.Add(e); accepted++;
            }
            var snapshot = Replay(tx, origin, merged, intent.OperationId);
            // Recheck the current immutable character/runtime projection immediately before recording the result.
            var final = ResolveCurrentWithContinuation(tx, owner, characterId);
            ValidateAuthority(final, owner, characterId);
            Conflict(final.Binding == current.Binding, "canonical-source-changed-during-operation");
            SessionSyncReceipt? sync = intent.Kind == "sync" ? new(origin.OverlayId,
                new(CurrentPlayVersion5712(tx, current), accepted, replayed), [], []) {
                ReplicaCursor = snapshot.Play!.ReplicaCursor, Transport = "local-replica" } : null;
            result = new(intent.OperationId, intent.PayloadDigest, snapshot, sync, null, intent.PreparedAtUtc);
        }
        catch (SessionPlayException e) when (e.Failure.Kind != "unavailable")
        {
            result = Rejected(origin, intent, e.Failure);
        }
        catch (OverflowException)
        {
            result = Rejected(origin, intent, SessionPlayFailures.Invalid("arithmetic-boundary"));
        }
        // Refuse an oversized projection as a small durable rejected outcome; do not strand an intent
        // whose result could never fit the reserved record. The envelope margin covers schema/hash metadata.
        if (JsonSerializer.SerializeToUtf8Bytes(result, Json).Length > SessionPlayLimits.MaxRecordBytes - 512)
            result = Rejected(origin, intent, SessionPlayFailures.Invalid("projection-storage-capacity-reached"));
        // An I/O failure here leaves the original intent pending, or an already committed result. Recovery reads first.
        tx.Complete(operation, result);
        return result;
    }

    private static SessionPlayOperationResult Rejected(SessionPlayOrigin origin, SessionPlayOperation intent, SessionPlayFailure failure)
    {
        var attributed = failure with { OperationId = intent.OperationId };
        SessionSyncReceipt? sync = intent.Kind == "sync" ? new(origin.OverlayId,
            new(origin.Authority.Binding.Version, 0, 0, ManualResolutionRequired: true), [],
            [new(intent.Events.FirstOrDefault()?.EventId ?? "", failure.Code,
                "Local reconciliation was rejected; no events were applied.", true)]) { Transport = "local-replica" } : null;
        return new(intent.OperationId, intent.PayloadDigest, null, sync, attributed, intent.PreparedAtUtc);
    }

    private static SessionPlayOperation Canonical(string? operationId, string kind, string overlayId,
        CharacterVersionReference version, IReadOnlyList<SessionEventEnvelope> events, string? cursor)
    {
        Identity(operationId, "operation-id-required"); Identity(overlayId, "overlay-id-required");
        Require(kind is "apply" or "sync" && version is not null && events is not null
            && events.Count <= SessionPlayLimits.MaxBatchEvents, "invalid-operation-shape");
        Identity(version.CharacterId, "character-required"); Identity(version.VersionId, "version-required");
        Identity(version.RulesetId, "edition-required"); Identity(version.RuntimeFingerprint, "runtime-required");
        if (cursor is not null) Require(cursor.Length <= 80, "invalid-replica-cursor");
        // Bound before serializing/cloning untrusted trees. No nested object graphs or asset/source paths are admitted.
        foreach (var e in events)
        {
            Require(e is not null && e.Payload is not null && e.Payload.Count <= 8 && e.BaseCharacterVersion is not null, "invalid-envelope");
            Identity(e.EventId, "event-id-required"); Identity(e.DeviceId, "device-id-required");
            Identity(e.ActorId, "actor-id-required"); Identity(e.EventType, "event-type-required");
            Identity(e.OverlayId, "overlay-id-required");
            if (e.ParentEventId is not null) Identity(e.ParentEventId, "parent-id-invalid");
            Require(e.BaseCharacterVersion == version && e.ProviderId is null && e.PackId is null
                && e.SyncCursor is null && e.AppliedAtUtc is null && e.Schema == SessionEventEnvelopeSchemas.SessionEventsVnext,
                "invalid-event-binding-metadata");
            foreach (var pair in e.Payload)
            {
                Require(pair.Key.Length <= 64 && pair.Value is not null, "invalid-payload-field");
                if (e.EventType == SessionPlayEventTypes.IndirectSpellCast && pair.Key == "indirect")
                    BoundIndirectCastInput5374(pair.Value);
                else BoundValue(pair.Value, false, e.EventType == SessionPlayEventTypes.ElementalFollowup && pair.Key == "command"
                    ? SessionPlayElementalFollowups.MaxCommandCharacters5393 : SessionPlayLimits.MaxNoteLength);
            }
        }
        var normalized = events.Select(e => e with { Payload = e.Payload.OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) });
        if (kind == "sync") normalized = normalized.OrderBy(e => e.Sequence).ThenBy(e => e.EventId, StringComparer.Ordinal);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(normalized.ToArray(), Json);
        Require(bytes.Length <= SessionPlayLimits.MaxRequestBytes, "request-capacity-reached");
        var detached = JsonSerializer.Deserialize<SessionEventEnvelope[]>(bytes, Json)!;
        string digest = OperationDigest(kind, overlayId, version, detached, cursor);
        return new(operationId!, digest, kind, overlayId, version, detached, cursor, DateTimeOffset.UtcNow);
    }
    private static void BoundValue(RulesetCapabilityValue value, bool child, int maxStringLength = SessionPlayLimits.MaxNoteLength)
    {
        Require(value.Properties is null && (value.StringValue is null || value.StringValue.Length <= maxStringLength),
            "payload-capacity-reached");
        if (value.Items is null) return;
        Require(!child && value.Items.Count <= 64, "payload-depth-or-capacity");
        foreach (var item in value.Items) { Require(item is not null, "invalid-payload-item"); BoundValue(item, true); }
    }
    // Fixed wire positions only; choice-dependent counts, declarations, ranges and authority
    // remain reducer checks. Do not normalize these values: existing request digests use them.
    private static void BoundIndirectCastInput5374(RulesetCapabilityValue value)
    {
        // Setup uses a Boolean placeholder; Core still requires its value to be false.
        if (value.Kind == RulesetCapabilityValueKinds.Boolean) { BoundIndirectScalar5374(value); return; }
        var fields = BoundIndirectList5374(value, 4, 4);
        BoundIndirectScalar5374(fields[0]); BoundIndirectScalar5374(fields[1]);
        foreach (var die in BoundIndirectList5374(fields[2], 0, 4)) BoundIndirectScalar5374(die);
        foreach (var target in BoundIndirectList5374(fields[3], 0, 12))
        {
            var row = BoundIndirectList5374(target, 7, 8);
            for (int i = 0; i < 7; i++) BoundIndirectScalar5374(row[i]);
            if (row.Count == 7) continue;
            var elemental = BoundIndirectList5374(row[7], 5, 6);
            BoundIndirectScalar5374(elemental[0]); BoundIndirectScalar5374(elemental[1]);
            foreach (var item in BoundIndirectList5374(elemental[2], 0, 12))
                foreach (var field in BoundIndirectList5374(item, 5, 5)) BoundIndirectScalar5374(field);
            foreach (var field in BoundIndirectList5374(elemental[3], 0, 2)) BoundIndirectScalar5374(field);
            BoundIndirectScalar5374(elemental[4]);
            if (elemental.Count == 6)
                foreach (var item in BoundIndirectList5374(elemental[5], 0, 12))
                    foreach (var field in BoundIndirectList5374(item, 4, 4)) BoundIndirectScalar5374(field);
        }
    }

    private static IReadOnlyList<RulesetCapabilityValue> BoundIndirectList5374(RulesetCapabilityValue? value, int min, int max)
    {
        Require(value is not null, "invalid-payload-item");
        Require(value.Kind == RulesetCapabilityValueKinds.List && value.Items is not null
            && value.Items.Count >= min && value.Items.Count <= max && value.Properties is null
            && value.StringValue is null && value.IntegerValue is null && value.BooleanValue is null
            && value.NumberValue is null && value.DecimalValue is null, "indirect-payload-shape");
        return value.Items;
    }

    private static void BoundIndirectScalar5374(RulesetCapabilityValue? value)
    {
        Require(value is not null, "invalid-payload-item");
        BoundValue(value, true); // Retain the original leaf string/object/nesting bounds.
        Require(value.Kind switch
        {
            RulesetCapabilityValueKinds.String => value.StringValue is not null && value.IntegerValue is null
                && value.BooleanValue is null && value.NumberValue is null && value.DecimalValue is null,
            RulesetCapabilityValueKinds.Integer => value.IntegerValue is not null && value.StringValue is null
                && value.BooleanValue is null && value.NumberValue is null && value.DecimalValue is null,
            RulesetCapabilityValueKinds.Boolean => value.BooleanValue is not null && value.StringValue is null
                && value.IntegerValue is null && value.NumberValue is null && value.DecimalValue is null,
            _ => false
        }, "indirect-payload-shape");
    }

    private static void ValidateAuthority(SessionPlayAuthority current, OwnerScope owner, string characterId)
    {
        Conflict(current.Binding.OwnerId == owner.NormalizedValue && current.Binding.Version.CharacterId == characterId
            && current.Binding.Version.RulesetId == "sr5", "resolved-owner-character-edition-mismatch");
        Conflict(current.Binding.AuthorityDigest == Digest(current with {
            Binding = current.Binding with { AuthorityDigest = string.Empty } }), "authority-digest-invalid");
        Require(current.Trackers.Count <= 16 && current.Weapons.Count <= 64 && current.Trackers.All(t => t.SessionSafe
            && t.MinimumValue is not null && t.MaximumValue is not null && t.MinimumValue.Value <= t.DefaultValue
            && t.DefaultValue <= t.MaximumValue.Value), "invalid-tracker-authority");
    }
    private static SessionEventEnvelope[] History(IReadOnlyList<SessionPlayStoredOperation> operations)
        => operations.Where(o => o.Result is { Failure: null, Snapshot: not null }).SelectMany(o => o.Intent.Events)
            .GroupBy(e => e.EventId, StringComparer.Ordinal).Select(g => g.First()).OrderBy(e => e.Sequence).ToArray();
    private static void CheckReplay(ISessionPlayReader reader, IReadOnlyList<SessionPlayStoredOperation> operations, SessionOverlaySnapshot replay)
    {
        var last = operations.LastOrDefault(o => o.Result is { Failure: null, Snapshot: not null });
        if (last is null) return;
        // Compare the old result against exactly the checkpoints that existed when it was written.
        // The current projection may additionally contain a later completed healing checkpoint.
        var originalReplay = ReplaySettlement5712(reader, reader.ReadOrigin()!, History(operations),
            last.Intent.OperationId, last.Ordinal, out _);
        Conflict(Digest(last.Result!.Snapshot) == Digest(originalReplay), "durable-replay-divergence");
    }
    private static SessionDashboardProjection Dashboard(SessionPlayOrigin origin, SessionOverlaySnapshot snapshot, SessionPlayBinding? sourceBinding = null)
    {
        var binding = (sourceBinding ?? origin.Authority.Binding) with { Version = snapshot.BaseCharacterVersion };
        var bundle = new SessionRuntimeBundle("local-projection:" + binding.AuthorityDigest, binding.Version,
            "local-projection-only/v1", default, string.Empty, [], origin.Authority.Trackers, new Dictionary<string, string>()) { ProjectionOnly = true };
        return new(origin.OverlayId, binding.Version, binding.Version.RuntimeFingerprint, snapshot, bundle,
            [new("local-play", SessionDashboardSectionKinds.Trackers, "Local play", ["local-sync"])],
            [new("local-sync", SessionDashboardCardKinds.SyncBanner, "Stored on this device", "External sync unavailable")],
            [new("run", "Run overlay", snapshot.Trackers)], [], [],
            new("local-sync", SessionSyncBannerStates.Offline, "Durable local replica; no remote acknowledgement."));
    }
    private static SessionApiResult<T> Guard<T>(Func<T> action)
    {
        try { return SessionApiResult<T>.Implemented(action()); }
        catch (SessionPlayException e) { return SessionApiResult<T>.Failed(e.Failure); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return SessionApiResult<T>.Failed(SessionPlayFailures.Unavailable("journal-io-recover-original-operation")); }
        catch (Exception e) when (e is JsonException or XmlException or OverflowException or ArgumentException or InvalidOperationException)
        { return SessionApiResult<T>.Failed(SessionPlayFailures.Invalid("invalid-or-unavailable-session-source")); }
    }
}
