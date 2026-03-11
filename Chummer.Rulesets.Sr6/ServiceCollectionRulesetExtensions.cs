using Chummer.Application.Workspaces;
using Chummer.Contracts.Rulesets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chummer.Rulesets.Sr6;

public static class ServiceCollectionRulesetExtensions
{
    public static IServiceCollection AddSr6Ruleset(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRulesetWorkspaceCodec, Sr6WorkspaceCodec>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRulesetPlugin, Chummer.Rulesets.Sr6.Sr6RulesetPlugin>());
        return services;
    }
}
