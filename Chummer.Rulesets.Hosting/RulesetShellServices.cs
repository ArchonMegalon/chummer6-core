using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;

namespace Chummer.Rulesets.Hosting;

public sealed class RulesetPluginRegistry : IRulesetPluginRegistry
{
    private readonly IReadOnlyList<IRulesetPlugin> _all;
    private readonly IReadOnlyDictionary<string, IRulesetPlugin> _pluginsByRuleset;

    public RulesetPluginRegistry(IEnumerable<IRulesetPlugin>? plugins)
    {
        _all = plugins?.ToArray() ?? [];

        Dictionary<string, IRulesetPlugin> pluginsByRuleset = new(StringComparer.Ordinal);
        foreach (IRulesetPlugin plugin in _all)
        {
            pluginsByRuleset[plugin.Id.NormalizedValue] = plugin;
        }

        _pluginsByRuleset = pluginsByRuleset;
    }

    public IReadOnlyList<IRulesetPlugin> All => _all;

    public IRulesetPlugin? Resolve(string? rulesetId)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        return normalizedRulesetId is null
            ? null
            : _pluginsByRuleset.GetValueOrDefault(normalizedRulesetId);
    }
}

public sealed class DefaultRulesetSelectionPolicy : IRulesetSelectionPolicy
{
    private readonly IRulesetPluginRegistry _pluginRegistry;
    private readonly RulesetSelectionOptions _options;

    public DefaultRulesetSelectionPolicy(
        IRulesetPluginRegistry pluginRegistry,
        RulesetSelectionOptions? options = null)
    {
        _pluginRegistry = pluginRegistry;
        _options = options ?? new RulesetSelectionOptions();
    }

    public string GetDefaultRulesetId()
    {
        string defaultRulesetId = RulesetDefaults.NormalizeRequired(_options.DefaultRulesetId);
        if (_pluginRegistry.Resolve(defaultRulesetId) is not null)
        {
            return defaultRulesetId;
        }

        string availableRulesets = string.Join(
            ", ",
            _pluginRegistry.All
            .Select(plugin => plugin.Id.NormalizedValue)
            .Where(rulesetId => !string.IsNullOrWhiteSpace(rulesetId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(rulesetId => rulesetId, StringComparer.Ordinal));
        if (string.IsNullOrWhiteSpace(availableRulesets))
        {
            throw new InvalidOperationException(
                $"Configured default ruleset '{defaultRulesetId}' from {_options.Source} cannot be resolved because no ruleset plugins are registered.");
        }

        throw new InvalidOperationException(
            $"Configured default ruleset '{defaultRulesetId}' from {_options.Source} is not registered. Available rulesets: {availableRulesets}.");
    }
}

public sealed class RulesetShellCatalogResolverService : IRulesetShellCatalogResolver
{
    private readonly IRulesetPluginRegistry _pluginRegistry;
    private readonly IRulesetSelectionPolicy _rulesetSelectionPolicy;

    public RulesetShellCatalogResolverService(
        IRulesetPluginRegistry pluginRegistry,
        IRulesetSelectionPolicy? rulesetSelectionPolicy = null)
    {
        _pluginRegistry = pluginRegistry;
        _rulesetSelectionPolicy = rulesetSelectionPolicy ?? new DefaultRulesetSelectionPolicy(pluginRegistry);
    }

    public IReadOnlyList<AppCommandDefinition> ResolveCommands(string? rulesetId)
    {
        return ResolveRequiredPlugin(rulesetId).ShellDefinitions.GetCommands();
    }

    public IReadOnlyList<NavigationTabDefinition> ResolveNavigationTabs(string? rulesetId)
    {
        return ResolveRequiredPlugin(rulesetId).ShellDefinitions.GetNavigationTabs();
    }

    public IReadOnlyList<WorkflowDefinition> ResolveWorkflowDefinitions(string? rulesetId)
    {
        return ResolveRequiredPlugin(rulesetId).Catalogs.GetWorkflowDefinitions();
    }

    public IReadOnlyList<WorkflowSurfaceDefinition> ResolveWorkflowSurfaces(string? rulesetId)
    {
        return ResolveRequiredPlugin(rulesetId).Catalogs.GetWorkflowSurfaces();
    }

    public IReadOnlyList<WorkspaceSurfaceActionDefinition> ResolveWorkspaceActionsForTab(string? tabId, string? rulesetId)
    {
        return SelectTabActions(ResolveRequiredPlugin(rulesetId).Catalogs.GetWorkspaceActions(), tabId);
    }

    private IRulesetPlugin ResolveRequiredPlugin(string? requestedRulesetId)
    {
        string effectiveRulesetId = RulesetDefaults.NormalizeOptional(requestedRulesetId)
            ?? RulesetDefaults.NormalizeOptional(_rulesetSelectionPolicy.GetDefaultRulesetId())
            ?? throw new InvalidOperationException("No ruleset plugin is registered to provide shell metadata.");

        return _pluginRegistry.Resolve(effectiveRulesetId)
            ?? throw new InvalidOperationException(
                $"No ruleset plugin is registered for ruleset '{effectiveRulesetId}'.");
    }

    private static IReadOnlyList<WorkspaceSurfaceActionDefinition> SelectTabActions(
        IReadOnlyList<WorkspaceSurfaceActionDefinition> actions,
        string? tabId)
    {
        string effectiveTabId = string.IsNullOrWhiteSpace(tabId) ? "tab-info" : tabId;

        WorkspaceSurfaceActionDefinition[] tabActions = actions
            .Where(action => string.Equals(action.TabId, effectiveTabId, StringComparison.Ordinal))
            .ToArray();
        if (tabActions.Length > 0)
            return tabActions;

        return actions
            .Where(action => string.Equals(action.TabId, "tab-info", StringComparison.Ordinal))
            .ToArray();
    }

}
