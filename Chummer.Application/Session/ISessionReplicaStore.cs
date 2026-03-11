using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public interface ISessionReplicaStore
{
    Task<CachedClientPayload<SessionReplicaState>?> GetAsync(string overlayId, CancellationToken ct = default);

    Task<CachedClientPayload<SessionReplicaState>> UpsertAsync(SessionReplicaState state, CancellationToken ct = default);

    Task RemoveAsync(string overlayId, CancellationToken ct = default);
}
