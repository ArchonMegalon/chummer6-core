#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Rulesets.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class RulesetShellCatalogResolverTests
{
    [TestMethod]
    public void ResolveCommands_prefers_ruleset_plugin_when_available()
    {
        RulesetShellCatalogResolverService resolver = CreateResolver(
            new StubRulesetPlugin(
                rulesetId: "sr6",
                commands:
                [
                    new AppCommandDefinition(
                        Id: "sr6-only",
                        LabelKey: "command.sr6-only",
                        Group: "tools",
                        RequiresOpenCharacter: false,
                        EnabledByDefault: true,
                        RulesetId: "sr6")
                ],
                tabs: []));
        IReadOnlyList<AppCommandDefinition> commands = resolver.ResolveCommands("sr6");

        Assert.HasCount(1, commands);
        Assert.AreEqual("sr6-only", commands[0].Id);
    }

    [TestMethod]
    public void ResolveNavigationTabs_uses_default_ruleset_selection_when_ruleset_is_not_specified()
    {
        NavigationTabDefinition[] sr5Tabs =
        [
            new NavigationTabDefinition(
                Id: "tab-sr5-default",
                Label: "SR5 Default",
                SectionId: "profile",
                Group: "character",
                RequiresOpenCharacter: true,
                EnabledByDefault: true,
                RulesetId: "sr5")
        ];
        RulesetShellCatalogResolverService resolver = CreateResolver(
            new StubRulesetPlugin("sr5", commands: [], tabs: sr5Tabs),
            new StubRulesetPlugin("sr6", commands: [], tabs: []));
        IReadOnlyList<NavigationTabDefinition> tabs = resolver.ResolveNavigationTabs(null);

        Assert.HasCount(1, tabs);
        Assert.AreEqual("tab-sr5-default", tabs[0].Id);
    }

    [TestMethod]
    public void ResolveCommands_uses_last_matching_plugin_when_multiple_are_registered()
    {
        RulesetShellCatalogResolverService resolver = CreateResolver(
            new StubRulesetPlugin(
                "sr6",
                commands:
                [
                    new AppCommandDefinition("first", "command.first", "tools", false, true, "sr6")
                ],
                tabs: []),
            new StubRulesetPlugin(
                "sr6",
                commands:
                [
                    new AppCommandDefinition("second", "command.second", "tools", false, true, "sr6")
                ],
                tabs: []));
        IReadOnlyList<AppCommandDefinition> commands = resolver.ResolveCommands("sr6");

        Assert.HasCount(1, commands);
        Assert.AreEqual("second", commands[0].Id);
    }

    [TestMethod]
    public void ResolveWorkspaceActionsForTab_prefers_ruleset_plugin_catalogs()
    {
        RulesetShellCatalogResolverService resolver = CreateResolver(
            new StubRulesetPlugin(
                rulesetId: "sr6",
                commands: [],
                tabs: [],
                actions:
                [
                    new WorkspaceSurfaceActionDefinition(
                        Id: "tab-sr6.summary",
                        Label: "SR6 Summary",
                        TabId: "tab-sr6",
                        Kind: WorkspaceSurfaceActionKind.Summary,
                        TargetId: "summary",
                        RequiresOpenCharacter: true,
                        EnabledByDefault: true,
                        RulesetId: "sr6")
                ]));
        IReadOnlyList<WorkspaceSurfaceActionDefinition> actions = resolver.ResolveWorkspaceActionsForTab("tab-sr6", "sr6");

        Assert.HasCount(1, actions);
        Assert.AreEqual("tab-sr6.summary", actions[0].Id);
        Assert.AreEqual("sr6", actions[0].RulesetId);
    }

    [TestMethod]
    public void ResolveWorkflowSurfaces_prefers_ruleset_plugin_catalogs()
    {
        RulesetShellCatalogResolverService resolver = CreateResolver(
            new StubRulesetPlugin(
                rulesetId: "sr6",
                commands: [],
                tabs: [],
                workflowDefinitions:
                [
                    new WorkflowDefinition(
                        WorkflowId: WorkflowDefinitionIds.LibraryShell,
                        Title: "SR6 Library Shell",
                        SurfaceIds: ["sr6.shell.menu"],
                        RequiresOpenWorkspace: false)
                ],
                workflowSurfaces:
                [
                    new WorkflowSurfaceDefinition(
                        SurfaceId: "sr6.shell.menu",
                        WorkflowId: WorkflowDefinitionIds.LibraryShell,
                        Kind: WorkflowSurfaceKinds.ShellRegion,
                        RegionId: ShellRegionIds.MenuBar,
                        LayoutToken: WorkflowLayoutTokens.ShellFrame,
                        ActionIds: ["sr6-only"])
                ],
                actions: []));
        IReadOnlyList<WorkflowDefinition> workflows = resolver.ResolveWorkflowDefinitions("sr6");
        IReadOnlyList<WorkflowSurfaceDefinition> workflowSurfaces = resolver.ResolveWorkflowSurfaces("sr6");

        Assert.HasCount(1, workflows);
        Assert.AreEqual("SR6 Library Shell", workflows[0].Title);
        Assert.HasCount(1, workflowSurfaces);
        Assert.AreEqual("sr6.shell.menu", workflowSurfaces[0].SurfaceId);
    }

    [TestMethod]
    public void ResolveCommands_throws_when_no_ruleset_plugins_are_registered()
    {
        RulesetShellCatalogResolverService resolver = CreateResolver();

        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            resolver.ResolveCommands(null));

        StringAssert.Contains(ex.Message, "Configured default ruleset 'sr5'");
        StringAssert.Contains(ex.Message, "no ruleset plugins are registered");
    }

    [TestMethod]
    public void RulesetPluginRegistry_does_not_treat_blank_ruleset_as_sr5_plugin_request()
    {
        RulesetPluginRegistry registry = new([new StubRulesetPlugin("sr5", commands: [], tabs: [])]);

        Assert.IsNull(registry.Resolve(null));
        Assert.IsNull(registry.Resolve(" "));
        Assert.IsNotNull(registry.Resolve("sr5"));
    }

    [TestMethod]
    public void DefaultRulesetSelectionPolicy_defaults_to_sr5_without_following_registration_order()
    {
        DefaultRulesetSelectionPolicy policy = new(new RulesetPluginRegistry(
        [
            new StubRulesetPlugin("sr6", commands: [], tabs: []),
            new StubRulesetPlugin("sr5", commands: [], tabs: [])
        ]));

        Assert.AreEqual(RulesetDefaults.Sr5, policy.GetDefaultRulesetId());
    }

    [TestMethod]
    public void DefaultRulesetSelectionPolicy_returns_configured_ruleset_when_registered()
    {
        DefaultRulesetSelectionPolicy policy = new(
            new RulesetPluginRegistry(
            [
                new StubRulesetPlugin("sr6", commands: [], tabs: []),
                new StubRulesetPlugin("sr5", commands: [], tabs: [])
            ]),
            new RulesetSelectionOptions(RulesetDefaults.Sr6, "test"));

        Assert.AreEqual(RulesetDefaults.Sr6, policy.GetDefaultRulesetId());
    }

    [TestMethod]
    public void DefaultRulesetSelectionPolicy_throws_when_configured_ruleset_is_not_registered()
    {
        DefaultRulesetSelectionPolicy policy = new(
            new RulesetPluginRegistry(
            [
                new StubRulesetPlugin("sr6", commands: [], tabs: []),
                new StubRulesetPlugin("sr5", commands: [], tabs: [])
            ]),
            new RulesetSelectionOptions(RulesetDefaults.Sr4, "test"));

        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>(() => policy.GetDefaultRulesetId());

        StringAssert.Contains(ex.Message, "Configured default ruleset 'sr4'");
        StringAssert.Contains(ex.Message, "Available rulesets: sr5, sr6");
    }

    private static RulesetShellCatalogResolverService CreateResolver(params IRulesetPlugin[] plugins)
    {
        RulesetPluginRegistry registry = new(plugins);
        return new RulesetShellCatalogResolverService(registry, new DefaultRulesetSelectionPolicy(registry));
    }

    private sealed class StubRulesetPlugin : IRulesetPlugin
    {
        public StubRulesetPlugin(
            string rulesetId,
            IReadOnlyList<AppCommandDefinition> commands,
            IReadOnlyList<NavigationTabDefinition> tabs,
            IReadOnlyList<WorkflowDefinition>? workflowDefinitions = null,
            IReadOnlyList<WorkflowSurfaceDefinition>? workflowSurfaces = null,
            IReadOnlyList<WorkspaceSurfaceActionDefinition>? actions = null)
        {
            Id = new RulesetId(rulesetId);
            DisplayName = rulesetId;
            Serializer = new StubSerializer(Id);
            ShellDefinitions = new StubShellDefinitions(commands, tabs);
            Catalogs = new StubCatalogs(workflowDefinitions, workflowSurfaces, actions);
            CapabilityDescriptors = new StubCapabilityDescriptorProvider();
            Capabilities = new StubCapabilityHost();
            Rules = new StubRules();
            Scripts = new StubScripts();
        }

        public RulesetId Id { get; }

        public string DisplayName { get; }

        public IRulesetSerializer Serializer { get; }

        public IRulesetShellDefinitionProvider ShellDefinitions { get; }

        public IRulesetCatalogProvider Catalogs { get; }

        public IRulesetCapabilityDescriptorProvider CapabilityDescriptors { get; }

        public IRulesetCapabilityHost Capabilities { get; }

        public IRulesetRuleHost Rules { get; }

        public IRulesetScriptHost Scripts { get; }
    }

    private sealed class StubSerializer : IRulesetSerializer
    {
        public StubSerializer(RulesetId id)
        {
            RulesetId = id;
        }

        public RulesetId RulesetId { get; }

        public int SchemaVersion => 1;

        public WorkspacePayloadEnvelope Wrap(string payloadKind, string payload)
        {
            return new WorkspacePayloadEnvelope(RulesetId.ToString(), SchemaVersion, payloadKind, payload);
        }
    }

    private sealed class StubShellDefinitions : IRulesetShellDefinitionProvider
    {
        private readonly IReadOnlyList<AppCommandDefinition> _commands;
        private readonly IReadOnlyList<NavigationTabDefinition> _tabs;

        public StubShellDefinitions(
            IReadOnlyList<AppCommandDefinition> commands,
            IReadOnlyList<NavigationTabDefinition> tabs)
        {
            _commands = commands;
            _tabs = tabs;
        }

        public IReadOnlyList<AppCommandDefinition> GetCommands() => _commands;

        public IReadOnlyList<NavigationTabDefinition> GetNavigationTabs() => _tabs;
    }

        private sealed class StubCatalogs : IRulesetCatalogProvider
        {
            private readonly IReadOnlyList<WorkflowDefinition> _workflowDefinitions;
            private readonly IReadOnlyList<WorkflowSurfaceDefinition> _workflowSurfaces;
            private readonly IReadOnlyList<WorkspaceSurfaceActionDefinition> _actions;

        public StubCatalogs(
            IReadOnlyList<WorkflowDefinition>? workflowDefinitions = null,
            IReadOnlyList<WorkflowSurfaceDefinition>? workflowSurfaces = null,
            IReadOnlyList<WorkspaceSurfaceActionDefinition>? actions = null)
        {
            _workflowDefinitions = workflowDefinitions ?? Array.Empty<WorkflowDefinition>();
            _workflowSurfaces = workflowSurfaces ?? Array.Empty<WorkflowSurfaceDefinition>();
            _actions = actions ?? Array.Empty<WorkspaceSurfaceActionDefinition>();
        }

        public IReadOnlyList<WorkflowDefinition> GetWorkflowDefinitions() => _workflowDefinitions;

        public IReadOnlyList<WorkflowSurfaceDefinition> GetWorkflowSurfaces() => _workflowSurfaces;

        public IReadOnlyList<WorkspaceSurfaceActionDefinition> GetWorkspaceActions() => _actions;
    }

    private sealed class StubRules : IRulesetRuleHost
    {
        public ValueTask<RulesetRuleEvaluationResult> EvaluateAsync(RulesetRuleEvaluationRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new RulesetRuleEvaluationResult(true, request.Inputs, Array.Empty<string>()));
        }
    }

    private sealed class StubCapabilityHost : IRulesetCapabilityHost
    {
        public ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(RulesetCapabilityInvocationRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new RulesetCapabilityInvocationResult(
                true,
                new RulesetCapabilityValue(
                    RulesetCapabilityValueKinds.Object,
                    Properties: request.Arguments.ToDictionary(
                        static argument => argument.Name,
                        static argument => argument.Value,
                        StringComparer.Ordinal)),
                []));
        }
    }

    private sealed class StubCapabilityDescriptorProvider : IRulesetCapabilityDescriptorProvider
    {
        public IReadOnlyList<RulesetCapabilityDescriptor> GetCapabilityDescriptors() => [];
    }

    private sealed class StubScripts : IRulesetScriptHost
    {
        public ValueTask<RulesetScriptExecutionResult> ExecuteAsync(RulesetScriptExecutionRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new RulesetScriptExecutionResult(
                Success: true,
                Error: null,
                Outputs: new Dictionary<string, object?>()));
        }
    }
}
