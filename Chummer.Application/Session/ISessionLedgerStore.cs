using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public interface ISessionLedgerStore
{
    Task<CachedClientPayload<SessionLedger>?> GetAsync(string overlayId, CancellationToken ct = default);

    Task<CachedClientPayload<SessionLedger>> UpsertAsync(SessionLedger ledger, CancellationToken ct = default);

    Task RemoveAsync(string overlayId, CancellationToken ct = default);
}
