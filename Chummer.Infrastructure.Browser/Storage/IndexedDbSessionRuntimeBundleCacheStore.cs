using Chummer.Application.Session;
using Chummer.Contracts.Session;

namespace Chummer.Infrastructure.Browser.Storage;

public sealed class IndexedDbSessionRuntimeBundleCacheStore : ISessionRuntimeBundleCacheStore
{
    private const string StoreName = "runtime-bundles";

    private readonly IndexedDbBrowserStore _store;

    public IndexedDbSessionRuntimeBundleCacheStore(IndexedDbBrowserStore store)
    {
        _store = store;
    }

    public Task<CachedClientPayload<SessionRuntimeBundleIssueReceipt>?> GetAsync(string characterId, CancellationToken ct = default)
        => _store.GetAsync<SessionRuntimeBundleIssueReceipt>(StoreName, SessionClientCacheAreas.RuntimeBundle, characterId, ct);

    public Task<CachedClientPayload<SessionRuntimeBundleIssueReceipt>> UpsertAsync(
        string characterId,
        SessionRuntimeBundleIssueReceipt receipt,
        CancellationToken ct = default)
        => _store.PutAsync(StoreName, SessionClientCacheAreas.RuntimeBundle, characterId, receipt, ct);

    public Task RemoveAsync(string characterId, CancellationToken ct = default)
        => _store.DeleteAsync(StoreName, SessionClientCacheAreas.RuntimeBundle, characterId, ct).AsTask();
}
