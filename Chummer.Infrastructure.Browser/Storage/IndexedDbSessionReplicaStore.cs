using Chummer.Application.Session;
using Chummer.Contracts.Session;

namespace Chummer.Infrastructure.Browser.Storage;

public sealed class IndexedDbSessionReplicaStore : ISessionReplicaStore
{
    private const string StoreName = "session-replicas";

    private readonly IndexedDbBrowserStore _store;

    public IndexedDbSessionReplicaStore(IndexedDbBrowserStore store)
    {
        _store = store;
    }

    public Task<CachedClientPayload<SessionReplicaState>?> GetAsync(string overlayId, CancellationToken ct = default)
        => _store.GetAsync<SessionReplicaState>(StoreName, SessionClientCacheAreas.Replica, overlayId, ct);

    public Task<CachedClientPayload<SessionReplicaState>> UpsertAsync(SessionReplicaState state, CancellationToken ct = default)
        => _store.PutAsync(StoreName, SessionClientCacheAreas.Replica, state.OverlayId, state, ct);

    public Task RemoveAsync(string overlayId, CancellationToken ct = default)
        => _store.DeleteAsync(StoreName, SessionClientCacheAreas.Replica, overlayId, ct).AsTask();
}
