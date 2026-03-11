using Chummer.Application.Workspaces;
using Chummer.Contracts.Rulesets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chummer.Rulesets.Sr4;

public static class ServiceCollectionRulesetExtensions
{
    public static IServiceCollection AddSr4Ruleset(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRulesetWorkspaceCodec, Sr4WorkspaceCodec>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRulesetPlugin, Chummer.Rulesets.Sr4.Sr4RulesetPlugin>());
        return services;
    }
}
