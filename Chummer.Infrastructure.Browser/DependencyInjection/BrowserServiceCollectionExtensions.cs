using Chummer.Application.Session;
using Chummer.Contracts.Session;
using Chummer.Infrastructure.Browser.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Chummer.Infrastructure.Browser.DependencyInjection;

public static class BrowserServiceCollectionExtensions
{
    public static IServiceCollection AddBrowserSessionOfflineStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IndexedDbBrowserStore>();
        services.AddScoped<IBrowseCacheStore, IndexedDbBrowseCacheStore>();
        services.AddScoped<ISessionRuntimeBundleCacheStore, IndexedDbSessionRuntimeBundleCacheStore>();
        services.AddScoped<ISessionLedgerStore, IndexedDbSessionLedgerStore>();
        services.AddScoped<ISessionReplicaStore, IndexedDbSessionReplicaStore>();
        services.AddScoped<IClientStorageQuotaService, BrowserClientStorageQuotaService>();
        services.AddScoped<ISessionOfflineCacheService, BrowserSessionOfflineCacheService>();

        return services;
    }
}
