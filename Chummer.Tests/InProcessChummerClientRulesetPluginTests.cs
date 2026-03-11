#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Application.Content;
using Chummer.Application.Owners;
using Chummer.Application.Tools;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Desktop.Runtime;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class InProcessChummerClientRulesetPluginTests
{
    [TestMethod]
    public async Task GetCommands_and_tabs_use_ruleset_plugin_definitions_when_registered()
    {
        var pluginCommands = new[]
        {
            new AppCommandDefinition(
                Id: "sr6_custom_command",
                LabelKey: "command.sr6_custom_command",
                Group: "tools",
                RequiresOpenCharacter: false,
                EnabledByDefault: true,
                RulesetId: "sr6")
        };
        var pluginTabs = new[]
        {
            new NavigationTabDefinition(
                Id: "tab-sr6-custom",
                Label: "SR6 Custom",
                SectionId: "profile",
                Group: "character",
                RequiresOpenCharacter: true,
                EnabledByDefault: true,
                RulesetId: "sr6")
        };

        var client = new InProcessChummerClient(
            new NoOpWorkspaceService(),
            new RulesetShellCatalogResolverService(
                new RulesetPluginRegistry([new StubRulesetPlugin("sr6", pluginCommands, pluginTabs)])));

        IReadOnlyList<AppCommandDefinition> commands = await client.GetCommandsAsync("SR6", CancellationToken.None);
        IReadOnlyList<NavigationTabDefinition> tabs = await client.GetNavigationTabsAsync("sr6", CancellationToken.None);

        Assert.HasCount(1, commands);
        Assert.AreEqual("sr6_custom_command", commands[0].Id);
        Assert.HasCount(1, tabs);
        Assert.AreEqual("tab-sr6-custom", tabs[0].Id);
    }

    [TestMethod]
    public async Task GetCommands_and_tabs_use_registered_runtime_ruleset_plugins()
    {
        var client = new InProcessChummerClient(
            new NoOpWorkspaceService(),
            CreateRuntimeShellCatalogResolver());

        IReadOnlyList<AppCommandDefinition> commands = await client.GetCommandsAsync("sr5", CancellationToken.None);
        IReadOnlyList<NavigationTabDefinition> tabs = await client.GetNavigationTabsAsync("sr5", CancellationToken.None);

        Assert.HasCount(new Sr5RulesetShellDefinitionProvider().GetCommands().Count, commands);
        Assert.HasCount(new Sr5RulesetShellDefinitionProvider().GetNavigationTabs().Count, tabs);
        Assert.IsTrue(commands.Any(command => string.Equals(command.Id, "file", StringComparison.Ordinal)));
        Assert.IsTrue(tabs.Any(tab => string.Equals(tab.Id, "tab-info", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task GetShellBootstrap_uses_saved_preferred_ruleset_when_no_workspaces_are_open()
    {
        var preferencesStore = new InMemoryShellPreferencesStore();
        preferencesStore.Save(new ShellPreferences("sr6"));
        var client = new InProcessChummerClient(
            new NoOpWorkspaceService(),
            CreateRuntimeShellCatalogResolver(),
            rulesetSelectionPolicy: CreateRuntimeRulesetSelectionPolicy(),
            shellPreferencesService: new ShellPreferencesService(preferencesStore));

        ShellBootstrapSnapshot snapshot = await client.GetShellBootstrapAsync(rulesetId: null, CancellationToken.None);

        Assert.AreEqual("sr6", snapshot.RulesetId);
    }

    [TestMethod]
    public async Task SaveShellPreferences_persists_preferred_ruleset()
    {
        var preferencesStore = new InMemoryShellPreferencesStore();
        var client = new InProcessChummerClient(
            new NoOpWorkspaceService(),
            CreateRuntimeShellCatalogResolver(),
            rulesetSelectionPolicy: CreateRuntimeRulesetSelectionPolicy(),
            shellPreferencesService: new ShellPreferencesService(preferencesStore));

        await client.SaveShellPreferencesAsync(new ShellPreferences("sr6"), CancellationToken.None);
        ShellPreferences restored = await client.GetShellPreferencesAsync(CancellationToken.None);

        Assert.AreEqual("sr6", restored.PreferredRulesetId);
    }

    [TestMethod]
    public async Task SaveShellSession_persists_active_workspace()
    {
        var sessionStore = new InMemoryShellSessionStore();
        var client = new InProcessChummerClient(
            new NoOpWorkspaceService(),
            CreateRuntimeShellCatalogResolver(),
            rulesetSelectionPolicy: CreateRuntimeRulesetSelectionPolicy(),
            shellSessionService: new ShellSessionService(sessionStore));

        await client.SaveShellSessionAsync(new ShellSessionState("ws-sr6"), CancellationToken.None);
        ShellSessionState restored = await client.GetShellSessionAsync(CancellationToken.None);

        Assert.AreEqual("ws-sr6", restored.ActiveWorkspaceId);
    }

    [TestMethod]
    public async Task SaveShellPreferences_routes_persistence_through_owner_context()
    {
        OwnerScope owner = new("alice@example.com");
        var preferencesStore = new InMemoryShellPreferencesStore();
        var client = new InProcessChummerClient(
            new NoOpWorkspaceService(),
            CreateRuntimeShellCatalogResolver(),
            rulesetSelectionPolicy: CreateRuntimeRulesetSelectionPolicy(),
            shellPreferencesService: new ShellPreferencesService(preferencesStore),
            ownerContextAccessor: new StubOwnerContextAccessor(owner));

        await client.SaveShellPreferencesAsync(new ShellPreferences("sr6"), CancellationToken.None);

        Assert.AreEqual(owner.NormalizedValue, preferencesStore.LastSavedOwner?.NormalizedValue);
        Assert.AreEqual("sr6", preferencesStore.Load(owner).PreferredRulesetId);
        Assert.AreEqual(RulesetDefaults.Sr5, preferencesStore.Load(OwnerScope.LocalSingleUser).PreferredRulesetId);
    }

    [TestMethod]
    public async Task GetShellBootstrap_routes_shell_state_and_workspace_listing_through_owner_context()
    {
        OwnerScope owner = new("alice@example.com");
        var workspaceService = new NoOpWorkspaceService
        {
            Workspaces =
            [
                CreateWorkspace("ws-sr6", DateTimeOffset.UtcNow, "sr6")
            ]
        };
        var preferencesStore = new InMemoryShellPreferencesStore();
        preferencesStore.Save(owner, new ShellPreferences("sr6"));
        var sessionStore = new InMemoryShellSessionStore();
        sessionStore.Save(owner, new ShellSessionState(ActiveTabId: "tab-rules"));
        var client = new InProcessChummerClient(
            workspaceService,
            CreateRuntimeShellCatalogResolver(),
            rulesetSelectionPolicy: CreateRuntimeRulesetSelectionPolicy(),
            shellPreferencesService: new ShellPreferencesService(preferencesStore),
            shellSessionService: new ShellSessionService(sessionStore),
            ownerContextAccessor: new StubOwnerContextAccessor(owner));

        ShellBootstrapSnapshot snapshot = await client.GetShellBootstrapAsync(rulesetId: null, CancellationToken.None);

        Assert.AreEqual(owner.NormalizedValue, preferencesStore.LastLoadedOwner?.NormalizedValue);
        Assert.AreEqual(owner.NormalizedValue, sessionStore.LastLoadedOwner?.NormalizedValue);
        Assert.AreEqual(owner.NormalizedValue, workspaceService.LastListOwner?.NormalizedValue);
        Assert.AreEqual("sr6", snapshot.RulesetId);
        Assert.AreEqual("tab-rules", snapshot.ActiveTabId);
        Assert.IsNotNull(snapshot.WorkflowDefinitions);
        Assert.IsNotNull(snapshot.WorkflowSurfaces);
        Assert.IsNotEmpty(snapshot.WorkflowDefinitions);
        Assert.IsNotEmpty(snapshot.WorkflowSurfaces);
    }

    [TestMethod]
    public async Task ImportAsync_routes_workspace_import_through_owner_context()
    {
        OwnerScope owner = new("alice@example.com");
        NoOpWorkspaceService workspaceService = new()
        {
            ImportResult = new WorkspaceImportResult(
                Id: new CharacterWorkspaceId("ws-owner"),
                Summary: new CharacterFileSummary(
                    Name: "Owner Runner",
                    Alias: "Owner Runner",
                    Metatype: "Human",
                    BuildMethod: "Priority",
                    CreatedVersion: "6",
                    AppVersion: "6",
                    Karma: 0m,
                    Nuyen: 0m,
                    Created: true),
                RulesetId: "sr6")
        };
        InProcessChummerClient client = new(
            workspaceService,
            CreateRuntimeShellCatalogResolver(),
            rulesetSelectionPolicy: CreateRuntimeRulesetSelectionPolicy(),
            ownerContextAccessor: new StubOwnerContextAccessor(owner));

        WorkspaceImportResult result = await client.ImportAsync(
            new WorkspaceImportDocument("<character />", "sr6", WorkspaceDocumentFormat.NativeXml),
            CancellationToken.None);

        Assert.AreEqual(owner.NormalizedValue, workspaceService.LastImportOwner?.NormalizedValue);
        Assert.AreEqual("sr6", result.RulesetId);
    }

    [TestMethod]
    public async Task GetShellBootstrap_restores_saved_active_workspace_when_present()
    {
        var workspaceService = new NoOpWorkspaceService
        {
            Workspaces =
            [
                CreateWorkspace("ws-sr5", DateTimeOffset.UtcNow.AddMinutes(-10), RulesetDefaults.Sr5),
                CreateWorkspace("ws-sr6", DateTimeOffset.UtcNow.AddMinutes(-5), "sr6")
            ]
        };
        var preferencesStore = new InMemoryShellPreferencesStore();
        preferencesStore.Save(new ShellPreferences(RulesetDefaults.Sr5));
        var sessionStore = new InMemoryShellSessionStore();
        sessionStore.Save(new ShellSessionState("ws-sr5"));
        var client = new InProcessChummerClient(
            workspaceService,
            CreateRuntimeShellCatalogResolver(),
            rulesetSelectionPolicy: CreateRuntimeRulesetSelectionPolicy(),
            shellPreferencesService: new ShellPreferencesService(preferencesStore),
            shellSessionService: new ShellSessionService(sessionStore));

        ShellBootstrapSnapshot snapshot = await client.GetShellBootstrapAsync(rulesetId: null, CancellationToken.None);

        Assert.AreEqual("ws-sr5", snapshot.ActiveWorkspaceId?.Value);
        Assert.AreEqual(RulesetDefaults.Sr5, snapshot.ActiveRulesetId);
        Assert.AreEqual(RulesetDefaults.Sr5, snapshot.RulesetId);
    }

    [TestMethod]
    public async Task GetShellBootstrap_does_not_infer_active_workspace_from_list_order_when_session_is_empty()
    {
        var workspaceService = new NoOpWorkspaceService
        {
            Workspaces =
            [
                CreateWorkspace("ws-sr5", DateTimeOffset.UtcNow.AddMinutes(-10), RulesetDefaults.Sr5),
                CreateWorkspace("ws-sr6", DateTimeOffset.UtcNow.AddMinutes(-5), "sr6")
            ]
        };
        var preferencesStore = new InMemoryShellPreferencesStore();
        preferencesStore.Save(new ShellPreferences(RulesetDefaults.Sr5));
        var client = new InProcessChummerClient(
            workspaceService,
            CreateRuntimeShellCatalogResolver(),
            rulesetSelectionPolicy: CreateRuntimeRulesetSelectionPolicy(),
            shellPreferencesService: new ShellPreferencesService(preferencesStore),
            shellSessionService: new ShellSessionService(new InMemoryShellSessionStore()));

        ShellBootstrapSnapshot snapshot = await client.GetShellBootstrapAsync(rulesetId: null, CancellationToken.None);

        Assert.IsNull(snapshot.ActiveWorkspaceId);
        Assert.AreEqual(RulesetDefaults.Sr5, snapshot.ActiveRulesetId);
        Assert.AreEqual(RulesetDefaults.Sr5, snapshot.RulesetId);
    }

    [TestMethod]
    public async Task GetShellBootstrap_restores_saved_active_tab()
    {
        var preferencesStore = new InMemoryShellPreferencesStore();
        preferencesStore.Save(new ShellPreferences(RulesetDefaults.Sr5));
        var sessionStore = new InMemoryShellSessionStore();
        sessionStore.Save(new ShellSessionState(ActiveTabId: "tab-rules"));
        var client = new InProcessChummerClient(
            new NoOpWorkspaceService(),
            CreateRuntimeShellCatalogResolver(),
            rulesetSelectionPolicy: CreateRuntimeRulesetSelectionPolicy(),
            shellPreferencesService: new ShellPreferencesService(preferencesStore),
            shellSessionService: new ShellSessionService(sessionStore));

        ShellBootstrapSnapshot snapshot = await client.GetShellBootstrapAsync(rulesetId: null, CancellationToken.None);

        Assert.AreEqual("tab-rules", snapshot.ActiveTabId);
    }

    [TestMethod]
    public async Task GetShellBootstrap_restores_saved_workspace_tab_map()
    {
        var preferencesStore = new InMemoryShellPreferencesStore();
        preferencesStore.Save(new ShellPreferences(RulesetDefaults.Sr5));
        var sessionStore = new InMemoryShellSessionStore();
        sessionStore.Save(new ShellSessionState(
            ActiveTabsByWorkspace: new Dictionary<string, string>
            {
                ["ws-a"] = "tab-info",
                ["ws-b"] = "tab-rules"
            }));
        var client = new InProcessChummerClient(
            new NoOpWorkspaceService(),
            CreateRuntimeShellCatalogResolver(),
            rulesetSelectionPolicy: CreateRuntimeRulesetSelectionPolicy(),
            shellPreferencesService: new ShellPreferencesService(preferencesStore),
            shellSessionService: new ShellSessionService(sessionStore));

        ShellBootstrapSnapshot snapshot = await client.GetShellBootstrapAsync(rulesetId: null, CancellationToken.None);

        Assert.IsNotNull(snapshot.ActiveTabsByWorkspace);
        Assert.AreEqual("tab-info", snapshot.ActiveTabsByWorkspace!["ws-a"]);
        Assert.AreEqual("tab-rules", snapshot.ActiveTabsByWorkspace["ws-b"]);
    }

    [TestMethod]
    public async Task GetShellBootstrap_includes_active_runtime_status_when_service_is_registered()
    {
        StubActiveRuntimeStatusService activeRuntimeStatusService = new(
            new ActiveRuntimeStatusProjection(
                ProfileId: "official.sr5.core",
                Title: "Official SR5 Core",
                RulesetId: RulesetDefaults.Sr5,
                RuntimeFingerprint: "sha256:sr5-runtime",
                InstallState: ArtifactInstallStates.Available,
                RulePackCount: 1,
                ProviderBindingCount: 2,
                WarningCount: 1));
        InProcessChummerClient client = new(
            new NoOpWorkspaceService(),
            CreateRuntimeShellCatalogResolver(),
            activeRuntimeStatusService: activeRuntimeStatusService,
            rulesetSelectionPolicy: CreateRuntimeRulesetSelectionPolicy());

        ShellBootstrapSnapshot snapshot = await client.GetShellBootstrapAsync(rulesetId: RulesetDefaults.Sr5, CancellationToken.None);

        Assert.IsNotNull(snapshot.ActiveRuntime);
        Assert.AreEqual("official.sr5.core", snapshot.ActiveRuntime.ProfileId);
        Assert.AreEqual("sha256:sr5-runtime", snapshot.ActiveRuntime.RuntimeFingerprint);
        Assert.AreEqual(OwnerScope.LocalSingleUser, activeRuntimeStatusService.LastOwner);
        Assert.AreEqual(RulesetDefaults.Sr5, activeRuntimeStatusService.LastRulesetId);
    }

    [TestMethod]
    public async Task ExportAsync_returns_workspace_bundle_from_workspace_service()
    {
        NoOpWorkspaceService workspaceService = new()
        {
            ExportResult = new CommandResult<WorkspaceExportReceipt>(
                Success: true,
                Value: new WorkspaceExportReceipt(
                    Id: new CharacterWorkspaceId("ws-export"),
                    Format: WorkspaceDocumentFormat.Json,
                    ContentBase64: Convert.ToBase64String(Encoding.UTF8.GetBytes("""
                        {
                          "Summary": {
                            "Name": "Runner"
                          },
                          "Attributes": {
                            "Attributes": [
                              {
                                "Name": "REA"
                              }
                            ]
                          }
                        }
                        """)),
                    FileName: "runner-export.json",
                    DocumentLength: 109,
                    RulesetId: "sr5"),
                Error: null)
        };
        InProcessChummerClient client = new(
            workspaceService,
            CreateRuntimeShellCatalogResolver());

        CommandResult<WorkspaceExportReceipt> export = await client.ExportAsync(new CharacterWorkspaceId("ws-export"), CancellationToken.None);

        Assert.IsTrue(export.Success);
        Assert.IsNotNull(export.Value);
        Assert.AreEqual("runner-export.json", export.Value.FileName);
        string payload = Encoding.UTF8.GetString(Convert.FromBase64String(export.Value.ContentBase64));
        StringAssert.Contains(payload, "\"Name\": \"Runner\"");
        StringAssert.Contains(payload, "\"REA\"");
    }

    private sealed class StubRulesetPlugin : IRulesetPlugin
    {
        public StubRulesetPlugin(
            string rulesetId,
            IReadOnlyList<AppCommandDefinition> commands,
            IReadOnlyList<NavigationTabDefinition> tabs)
        {
            Id = new RulesetId(rulesetId);
            DisplayName = $"Stub {rulesetId}";
            Serializer = new StubRulesetSerializer(Id);
            ShellDefinitions = new StubRulesetShellDefinitions(commands, tabs);
            Catalogs = new StubRulesetCatalogProvider();
            CapabilityDescriptors = new StubRulesetCapabilityDescriptorProvider();
            Capabilities = new StubRulesetCapabilityHost();
            Rules = new StubRulesetRuleHost();
            Scripts = new StubRulesetScriptHost();
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

    private static IRulesetPlugin[] CreateRuntimeRulesetPlugins()
    {
        return
        [
            new Sr5RulesetPlugin(),
            new Sr6RulesetPlugin()
        ];
    }

    private static RulesetShellCatalogResolverService CreateRuntimeShellCatalogResolver()
    {
        RulesetPluginRegistry registry = new(CreateRuntimeRulesetPlugins());
        return new RulesetShellCatalogResolverService(registry, new DefaultRulesetSelectionPolicy(registry));
    }

    private static DefaultRulesetSelectionPolicy CreateRuntimeRulesetSelectionPolicy()
    {
        return new DefaultRulesetSelectionPolicy(new RulesetPluginRegistry(CreateRuntimeRulesetPlugins()));
    }

    private sealed class StubRulesetSerializer : IRulesetSerializer
    {
        public StubRulesetSerializer(RulesetId rulesetId)
        {
            RulesetId = rulesetId;
        }

        public RulesetId RulesetId { get; }

        public int SchemaVersion => 1;

        public WorkspacePayloadEnvelope Wrap(string payloadKind, string payload)
        {
            return new WorkspacePayloadEnvelope(
                RulesetId: RulesetId.ToString(),
                SchemaVersion: SchemaVersion,
                PayloadKind: payloadKind,
                Payload: payload);
        }
    }

    private sealed class StubRulesetShellDefinitions : IRulesetShellDefinitionProvider
    {
        private readonly IReadOnlyList<AppCommandDefinition> _commands;
        private readonly IReadOnlyList<NavigationTabDefinition> _tabs;

        public StubRulesetShellDefinitions(
            IReadOnlyList<AppCommandDefinition> commands,
            IReadOnlyList<NavigationTabDefinition> tabs)
        {
            _commands = commands;
            _tabs = tabs;
        }

        public IReadOnlyList<AppCommandDefinition> GetCommands() => _commands;

        public IReadOnlyList<NavigationTabDefinition> GetNavigationTabs() => _tabs;
    }

    private sealed class StubRulesetCatalogProvider : IRulesetCatalogProvider
    {
        public IReadOnlyList<WorkspaceSurfaceActionDefinition> GetWorkspaceActions() => Array.Empty<WorkspaceSurfaceActionDefinition>();
    }

    private sealed class StubRulesetRuleHost : IRulesetRuleHost
    {
        public ValueTask<RulesetRuleEvaluationResult> EvaluateAsync(RulesetRuleEvaluationRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new RulesetRuleEvaluationResult(
                Success: true,
                Outputs: request.Inputs,
                Messages: Array.Empty<string>()));
        }
    }

    private sealed class StubRulesetCapabilityHost : IRulesetCapabilityHost
    {
        public ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(RulesetCapabilityInvocationRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new RulesetCapabilityInvocationResult(
                Success: true,
                Output: new RulesetCapabilityValue(
                    RulesetCapabilityValueKinds.Object,
                    Properties: request.Arguments.ToDictionary(
                        static argument => argument.Name,
                        static argument => argument.Value,
                        StringComparer.Ordinal)),
                Diagnostics: Array.Empty<RulesetCapabilityDiagnostic>()));
        }
    }

    private sealed class StubRulesetCapabilityDescriptorProvider : IRulesetCapabilityDescriptorProvider
    {
        public IReadOnlyList<RulesetCapabilityDescriptor> GetCapabilityDescriptors() => [];
    }

    private sealed class StubRulesetScriptHost : IRulesetScriptHost
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

    private sealed class InMemoryShellPreferencesStore : IShellPreferencesStore
    {
        private readonly Dictionary<string, ShellPreferences> _preferencesByOwner = new(StringComparer.Ordinal)
        {
            [OwnerScope.LocalSingleUser.NormalizedValue] = new(RulesetDefaults.Sr5)
        };

        public OwnerScope? LastLoadedOwner { get; private set; }

        public OwnerScope? LastSavedOwner { get; private set; }

        public ShellPreferences Load()
        {
            return Load(OwnerScope.LocalSingleUser);
        }

        public ShellPreferences Load(OwnerScope owner)
        {
            LastLoadedOwner = owner;
            return _preferencesByOwner.GetValueOrDefault(
                owner.NormalizedValue,
                ShellPreferences.Default);
        }

        public void Save(ShellPreferences preferences)
        {
            Save(OwnerScope.LocalSingleUser, preferences);
        }

        public void Save(OwnerScope owner, ShellPreferences preferences)
        {
            LastSavedOwner = owner;
            _preferencesByOwner[owner.NormalizedValue] = preferences;
        }
    }

    private sealed class InMemoryShellSessionStore : IShellSessionStore
    {
        private readonly Dictionary<string, ShellSessionState> _sessionsByOwner = new(StringComparer.Ordinal)
        {
            [OwnerScope.LocalSingleUser.NormalizedValue] = ShellSessionState.Default
        };

        public OwnerScope? LastLoadedOwner { get; private set; }

        public OwnerScope? LastSavedOwner { get; private set; }

        public ShellSessionState Load()
        {
            return Load(OwnerScope.LocalSingleUser);
        }

        public ShellSessionState Load(OwnerScope owner)
        {
            LastLoadedOwner = owner;
            return _sessionsByOwner.GetValueOrDefault(
                owner.NormalizedValue,
                ShellSessionState.Default);
        }

        public void Save(ShellSessionState session)
        {
            Save(OwnerScope.LocalSingleUser, session);
        }

        public void Save(OwnerScope owner, ShellSessionState session)
        {
            LastSavedOwner = owner;
            _sessionsByOwner[owner.NormalizedValue] = new ShellSessionState(
                ActiveWorkspaceId: session.ActiveWorkspaceId,
                ActiveTabId: session.ActiveTabId,
                ActiveTabsByWorkspace: NormalizeWorkspaceTabMap(session.ActiveTabsByWorkspace));
        }

        private static Dictionary<string, string>? NormalizeWorkspaceTabMap(IReadOnlyDictionary<string, string>? rawMap)
        {
            if (rawMap is null || rawMap.Count == 0)
            {
                return null;
            }

            Dictionary<string, string> normalized = new(StringComparer.Ordinal);
            foreach ((string workspaceId, string tabId) in rawMap)
            {
                if (!string.IsNullOrWhiteSpace(workspaceId) && !string.IsNullOrWhiteSpace(tabId))
                {
                    normalized[workspaceId.Trim()] = tabId.Trim();
                }
            }

            return normalized.Count == 0
                ? null
                : normalized;
        }
    }

    private sealed class NoOpWorkspaceService : IWorkspaceService
    {
        public WorkspaceImportResult ImportResult { get; init; } = new(
            Id: new CharacterWorkspaceId("ws-import"),
            Summary: new CharacterFileSummary(
                Name: "Runner",
                Alias: "Runner",
                Metatype: "Human",
                BuildMethod: "Priority",
                CreatedVersion: "5",
                AppVersion: "5",
                Karma: 0m,
                Nuyen: 0m,
                Created: true),
            RulesetId: RulesetDefaults.Sr5);

        public OwnerScope? LastImportOwner { get; private set; }

        public OwnerScope? LastListOwner { get; private set; }

        public WorkspaceImportResult Import(WorkspaceImportDocument document) => ImportResult;

        public WorkspaceImportResult Import(OwnerScope owner, WorkspaceImportDocument document)
        {
            LastImportOwner = owner;
            return Import(document);
        }

        public IReadOnlyList<WorkspaceListItem> Workspaces { get; init; } = Array.Empty<WorkspaceListItem>();

        public IReadOnlyList<WorkspaceListItem> List(int? maxCount = null)
        {
            if (maxCount is > 0)
            {
                return Workspaces.Take(maxCount.Value).ToArray();
            }

            return Workspaces;
        }

        public IReadOnlyList<WorkspaceListItem> List(OwnerScope owner, int? maxCount = null)
        {
            LastListOwner = owner;
            return List(maxCount);
        }

        public bool Close(CharacterWorkspaceId id) => throw new NotSupportedException();

        public bool Close(OwnerScope owner, CharacterWorkspaceId id) => Close(id);

        public object? GetSection(CharacterWorkspaceId id, string sectionId) => throw new NotSupportedException();

        public object? GetSection(OwnerScope owner, CharacterWorkspaceId id, string sectionId) => GetSection(id, sectionId);

        public CharacterFileSummary? GetSummary(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterFileSummary? GetSummary(OwnerScope owner, CharacterWorkspaceId id) => GetSummary(id);

        public CharacterValidationResult? Validate(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterValidationResult? Validate(OwnerScope owner, CharacterWorkspaceId id) => Validate(id);

        public CharacterProfileSection? GetProfile(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterProfileSection? GetProfile(OwnerScope owner, CharacterWorkspaceId id) => GetProfile(id);

        public CharacterProgressSection? GetProgress(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterProgressSection? GetProgress(OwnerScope owner, CharacterWorkspaceId id) => GetProgress(id);

        public CharacterSkillsSection? GetSkills(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterSkillsSection? GetSkills(OwnerScope owner, CharacterWorkspaceId id) => GetSkills(id);

        public CharacterRulesSection? GetRules(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterRulesSection? GetRules(OwnerScope owner, CharacterWorkspaceId id) => GetRules(id);

        public CharacterBuildSection? GetBuild(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterBuildSection? GetBuild(OwnerScope owner, CharacterWorkspaceId id) => GetBuild(id);

        public CharacterMovementSection? GetMovement(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterMovementSection? GetMovement(OwnerScope owner, CharacterWorkspaceId id) => GetMovement(id);

        public CharacterAwakeningSection? GetAwakening(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterAwakeningSection? GetAwakening(OwnerScope owner, CharacterWorkspaceId id) => GetAwakening(id);

        public CommandResult<CharacterProfileSection> UpdateMetadata(CharacterWorkspaceId id, UpdateWorkspaceMetadata command) => throw new NotSupportedException();

        public CommandResult<CharacterProfileSection> UpdateMetadata(OwnerScope owner, CharacterWorkspaceId id, UpdateWorkspaceMetadata command) => UpdateMetadata(id, command);

        public CommandResult<WorkspaceSaveReceipt> Save(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspaceSaveReceipt> Save(OwnerScope owner, CharacterWorkspaceId id) => Save(id);

        public CommandResult<WorkspaceDownloadReceipt> Download(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspaceDownloadReceipt> Download(OwnerScope owner, CharacterWorkspaceId id) => Download(id);

        public CommandResult<WorkspaceExportReceipt> ExportResult { get; init; } = new(false, null, "Export not configured.");

        public CommandResult<WorkspaceExportReceipt> Export(CharacterWorkspaceId id) => ExportResult;

        public CommandResult<WorkspaceExportReceipt> Export(OwnerScope owner, CharacterWorkspaceId id) => Export(id);

        public CommandResult<WorkspacePrintReceipt> Print(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspacePrintReceipt> Print(OwnerScope owner, CharacterWorkspaceId id) => Print(id);
    }

    private sealed class StubOwnerContextAccessor : IOwnerContextAccessor
    {
        public StubOwnerContextAccessor(OwnerScope current)
        {
            Current = current;
        }

        public OwnerScope Current { get; }
    }

    private sealed class StubActiveRuntimeStatusService : IActiveRuntimeStatusService
    {
        private readonly ActiveRuntimeStatusProjection? _projection;

        public StubActiveRuntimeStatusService(ActiveRuntimeStatusProjection? projection)
        {
            _projection = projection;
        }

        public OwnerScope LastOwner { get; private set; }

        public string? LastRulesetId { get; private set; }

        public ActiveRuntimeStatusProjection? GetActiveProfileStatus(OwnerScope owner, string? rulesetId = null)
        {
            LastOwner = owner;
            LastRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return _projection;
        }
    }

    private static WorkspaceListItem CreateWorkspace(
        string id,
        DateTimeOffset lastUpdatedUtc,
        string rulesetId)
    {
        return new WorkspaceListItem(
            Id: new CharacterWorkspaceId(id),
            Summary: new CharacterFileSummary(
                Name: id,
                Alias: id,
                Metatype: "Human",
                BuildMethod: "Priority",
                CreatedVersion: "5",
                AppVersion: "5",
                Karma: 0m,
                Nuyen: 0m,
                Created: true),
            LastUpdatedUtc: lastUpdatedUtc,
            RulesetId: rulesetId);
    }
}
