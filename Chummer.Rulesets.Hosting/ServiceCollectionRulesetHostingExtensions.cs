using Chummer.Application.Workspaces;
using Chummer.Contracts.Rulesets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chummer.Rulesets.Hosting;

public static class ServiceCollectionRulesetHostingExtensions
{
    private const string DefaultRulesetEnvironmentVariable = "CHUMMER_DEFAULT_RULESET";

    public static IServiceCollection AddRulesetInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IRulesetPluginRegistry, RulesetPluginRegistry>();
        services.TryAddSingleton(_ => CreateRulesetSelectionOptions());
        services.TryAddSingleton<IRulesetSelectionPolicy, DefaultRulesetSelectionPolicy>();
        services.TryAddSingleton<IRulesetShellCatalogResolver, RulesetShellCatalogResolverService>();
        services.TryAddSingleton<IRulesetWorkspaceCodecResolver, RulesetWorkspaceCodecResolver>();
        return services;
    }

    private static RulesetSelectionOptions CreateRulesetSelectionOptions()
    {
        string? configuredRulesetId = Environment.GetEnvironmentVariable(DefaultRulesetEnvironmentVariable);
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(configuredRulesetId);
        return normalizedRulesetId is null
            ? new RulesetSelectionOptions()
            : new RulesetSelectionOptions(normalizedRulesetId, $"environment:{DefaultRulesetEnvironmentVariable}");
    }
}
