using System.Text.Json;
using Chummer.Application.Session;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;

namespace Chummer.Infrastructure.Files;

/// <summary>An owner-bound read view over the same journal, not an independently writable replica cache.</summary>
public sealed class FileSessionReplicaStore(FileSessionLedgerStore store, OwnerScope owner, string characterId) : ISessionReplicaStore
{
    public Task<CachedClientPayload<SessionReplicaState>?> GetAsync(string overlayId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var value = store.Execute(owner, characterId, tx => {
            var origin = tx.ReadOrigin();
            if (origin is null || origin.OverlayId != overlayId) return null;
            var operations = tx.ReadOperations();
            var events = FileSessionLedgerStore.Events(operations);
            var updated = operations.LastOrDefault()?.Result?.CompletedAtUtc ?? origin.CreatedAtUtc;
            var clocks = events.GroupBy(e => e.DeviceId, StringComparer.Ordinal)
                .Select(g => new SessionReplicaClock(g.Key, g.Max(e => e.Sequence), updated))
                .OrderBy(c => c.ReplicaId, StringComparer.Ordinal).ToArray();
            var replica = new SessionReplicaState(overlayId, origin.Authority.Binding.Version,
                origin.Authority.Binding.Version.RuntimeFingerprint, "local:" + overlayId, clocks,
                [new("events", "ordered-event-ledger/v1", JsonSerializer.Serialize(events, SessionPlayEncoding.Json))],
                updated, operations.Count(o => o.Result is null));
            return new CachedClientPayload<SessionReplicaState>(SessionClientCacheAreas.Replica, overlayId,
                replica, DateTimeOffset.UtcNow, "local-file-journal");
        });
        return Task.FromResult(value);
    }
    public Task<CachedClientPayload<SessionReplicaState>> UpsertAsync(SessionReplicaState state, CancellationToken ct = default)
        => throw new NotSupportedException("Use SyncCharacterLedger; a raw CRDT replacement has no validated event authority.");
    public Task RemoveAsync(string overlayId, CancellationToken ct = default)
        => throw new NotSupportedException("An only-copy replica cannot be evicted through the cache contract.");
}
