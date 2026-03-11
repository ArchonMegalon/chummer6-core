using Chummer.Contracts.Presentation;

namespace Chummer.Contracts.Rulesets;

public interface IRulesetPluginRegistry
{
    IReadOnlyList<IRulesetPlugin> All { get; }

    IRulesetPlugin? Resolve(string? rulesetId);
}

public interface IRulesetSelectionPolicy
{
    string GetDefaultRulesetId();
}

public sealed record RulesetSelectionOptions(
    string DefaultRulesetId = RulesetDefaults.Sr5,
    string Source = "built-in:sr5");

public interface IRulesetShellCatalogResolver
{
    IReadOnlyList<AppCommandDefinition> ResolveCommands(string? rulesetId);

    IReadOnlyList<NavigationTabDefinition> ResolveNavigationTabs(string? rulesetId);

    IReadOnlyList<WorkflowDefinition> ResolveWorkflowDefinitions(string? rulesetId);

    IReadOnlyList<WorkflowSurfaceDefinition> ResolveWorkflowSurfaces(string? rulesetId);

    IReadOnlyList<WorkspaceSurfaceActionDefinition> ResolveWorkspaceActionsForTab(string? tabId, string? rulesetId);
}
