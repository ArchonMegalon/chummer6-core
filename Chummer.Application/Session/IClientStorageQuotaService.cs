using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public interface IClientStorageQuotaService
{
    Task<ClientStorageQuotaEstimate> GetEstimateAsync(CancellationToken ct = default);
}
