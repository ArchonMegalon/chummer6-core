using Chummer.Application.Session;
using Chummer.Contracts.Session;

namespace Chummer.Infrastructure.Browser.Storage;

public sealed class IndexedDbSessionLedgerStore : ISessionLedgerStore
{
    private const string StoreName = "session-ledgers";

    private readonly IndexedDbBrowserStore _store;

    public IndexedDbSessionLedgerStore(IndexedDbBrowserStore store)
    {
        _store = store;
    }

    public Task<CachedClientPayload<SessionLedger>?> GetAsync(string overlayId, CancellationToken ct = default)
        => _store.GetAsync<SessionLedger>(StoreName, SessionClientCacheAreas.Ledger, overlayId, ct);

    public Task<CachedClientPayload<SessionLedger>> UpsertAsync(SessionLedger ledger, CancellationToken ct = default)
        => _store.PutAsync(StoreName, SessionClientCacheAreas.Ledger, ledger.OverlayId, ledger, ct);

    public Task RemoveAsync(string overlayId, CancellationToken ct = default)
        => _store.DeleteAsync(StoreName, SessionClientCacheAreas.Ledger, overlayId, ct).AsTask();
}
