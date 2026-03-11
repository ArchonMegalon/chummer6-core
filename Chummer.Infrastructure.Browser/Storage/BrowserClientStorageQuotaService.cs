using Chummer.Application.Session;
using Chummer.Contracts.Session;

namespace Chummer.Infrastructure.Browser.Storage;

public sealed class BrowserClientStorageQuotaService : IClientStorageQuotaService
{
    private readonly IndexedDbBrowserStore _store;

    public BrowserClientStorageQuotaService(IndexedDbBrowserStore store)
    {
        _store = store;
    }

    public Task<ClientStorageQuotaEstimate> GetEstimateAsync(CancellationToken ct = default)
        => _store.GetQuotaEstimateAsync(ct);
}
