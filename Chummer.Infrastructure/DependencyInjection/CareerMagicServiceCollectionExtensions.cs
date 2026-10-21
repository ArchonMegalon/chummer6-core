using Chummer.Application.Characters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chummer.Infrastructure.DependencyInjection;

public static class CareerMagicServiceCollectionExtensions
{
    public static IServiceCollection AddCharacterCareerMagic(this IServiceCollection services)
    {
        // Remove a previously registered naked owner-managed authority. This local
        // lane cannot acquire campaign permission through host registration order.
        services.RemoveAll<ICharacterCareerPowerAuthorityResolver>();
        services.AddSingleton<ICharacterCareerPowerAuthorityResolver, CampaignDenyingCareerPowerAuthorityResolver>();
        services.TryAddSingleton<OwnerBoundCharacterCareerPowerService>();
        services.TryAddSingleton<ICharacterCareerMagicReader, OwnerBoundCharacterCareerMagicReader>();
        return services;
    }
}
