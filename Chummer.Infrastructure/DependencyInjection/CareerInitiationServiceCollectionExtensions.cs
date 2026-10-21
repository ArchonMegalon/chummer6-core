using Chummer.Application.Characters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chummer.Infrastructure.DependencyInjection;

public static class CareerInitiationServiceCollectionExtensions
{
    /// <summary>Requires the host's existing IWorkspaceStore, IOwnerContextAccessor and source resolver.</summary>
    public static IServiceCollection AddCharacterCareerInitiation(this IServiceCollection services)
    {
        services.TryAddScoped<ICharacterCareerInitiationAuthorityResolver, CharacterCareerInitiationAuthorityResolver>();
        services.TryAddScoped<OwnerBoundCharacterCareerInitiationService>();
        return services;
    }
}
