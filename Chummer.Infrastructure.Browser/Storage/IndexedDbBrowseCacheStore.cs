using Chummer.Application.Session;
using Chummer.Contracts.Session;

namespace Chummer.Infrastructure.Browser.Storage;

public sealed class IndexedDbBrowseCacheStore : IBrowseCacheStore
{
    private const string StoreName = "browse-cache";

    private readonly IndexedDbBrowserStore _store;

    public IndexedDbBrowseCacheStore(IndexedDbBrowserStore store)
    {
        _store = store;
    }

    public Task<CachedClientPayload<T>?> GetAsync<T>(string cacheArea, string cacheKey, CancellationToken ct = default)
        => _store.GetAsync<T>(StoreName, cacheArea, cacheKey, ct);

    public Task<CachedClientPayload<T>> UpsertAsync<T>(string cacheArea, string cacheKey, T payload, CancellationToken ct = default)
        => _store.PutAsync(StoreName, cacheArea, cacheKey, payload, ct);

    public Task RemoveAsync(string cacheArea, string cacheKey, CancellationToken ct = default)
        => _store.DeleteAsync(StoreName, cacheArea, cacheKey, ct).AsTask();
}
