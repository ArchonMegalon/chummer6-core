using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public interface IBrowseCacheStore
{
    Task<CachedClientPayload<T>?> GetAsync<T>(string cacheArea, string cacheKey, CancellationToken ct = default);

    Task<CachedClientPayload<T>> UpsertAsync<T>(string cacheArea, string cacheKey, T payload, CancellationToken ct = default);

    Task RemoveAsync(string cacheArea, string cacheKey, CancellationToken ct = default);
}
