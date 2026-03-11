using Chummer.Contracts.Rulesets;
using Chummer.Application.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chummer.Rulesets.Sr5;

public static class ServiceCollectionRulesetExtensions
{
    public static IServiceCollection AddSr5Ruleset(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRulesetWorkspaceCodec, Sr5WorkspaceCodec>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRulesetPlugin, Chummer.Rulesets.Sr5.Sr5RulesetPlugin>());
        return services;
    }
}
