using Chummer.Application.Session;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chummer.Infrastructure.DependencyInjection;

public static class SessionPlayServiceCollectionExtensions
{
    /// <summary>Controller inserts services.AddDurableSessionPlay(stateDirectory) after the selected session registrations.</summary>
    public static IServiceCollection AddDurableSessionPlay(this IServiceCollection services, string stateDirectory)
    {
        services.TryAddSingleton<FileSessionLedgerStore>(_ => new FileSessionLedgerStore(stateDirectory));
        services.TryAddSingleton<ISessionPlayStore>(p => p.GetRequiredService<FileSessionLedgerStore>());
        services.TryAddSingleton<ISessionPlayWorkspaceAuthoritySource, WorkspaceSessionPlaySource>();
        services.TryAddSingleton<ISessionPlayAuthorityResolver, WorkspaceSessionPlayAuthorityResolver>();
        services.TryAddSingleton<DurableSessionPlayService>();
        // The selected OwnerScopedSessionService registration is retained; its optional dependency is now supplied.
        // Unscoped cache interfaces are deliberately not registered as an owner-authorized mutable journal.
        return services;
    }
}
