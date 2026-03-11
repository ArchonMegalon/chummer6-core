using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public interface ISessionRuntimeBundleCacheStore
{
    Task<CachedClientPayload<SessionRuntimeBundleIssueReceipt>?> GetAsync(string characterId, CancellationToken ct = default);

    Task<CachedClientPayload<SessionRuntimeBundleIssueReceipt>> UpsertAsync(
        string characterId,
        SessionRuntimeBundleIssueReceipt receipt,
        CancellationToken ct = default);

    Task RemoveAsync(string characterId, CancellationToken ct = default);
}
