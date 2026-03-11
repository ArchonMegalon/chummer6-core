#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Content;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public class ShellPresenterTests
{
    [TestMethod]
    public async Task InitializeAsync_loads_shell_contract_and_restores_workspaces_without_auto_selecting_active_workspace()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var client = new ShellClientStub
        {
            Workspaces =
            [
                CreateWorkspace("ws-old", "Old Character", "OLD", now.AddMinutes(-25)),
                CreateWorkspace("ws-new", "New Character", "NEW", now.AddMinutes(-5))
            ]
        };
        var presenter = new ShellPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);

        Assert.IsFalse(presenter.State.IsBusy);
        Assert.IsNull(presenter.State.Error);
        Assert.HasCount(2, presenter.State.OpenWorkspaces);
        Assert.IsNull(presenter.State.ActiveWorkspaceId);
        Assert.AreEqual("ws-new", presenter.State.OpenWorkspaces[0].Id.Value);
        Assert.AreEqual("file", presenter.State.MenuRoots[0].Id);
        Assert.AreEqual(RulesetDefaults.Sr5, presenter.State.ActiveRulesetId);
        Assert.AreEqual("tab-info", presenter.State.ActiveTabId);
        StringAssert.Contains(presenter.State.Notice ?? string.Empty, "Restored 2 workspace(s).");
    }

    [TestMethod]
    public async Task InitializeAsync_restores_persisted_active_workspace_from_bootstrap_session()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var client = new ShellClientStub
        {
            Workspaces =
            [
                CreateWorkspace("ws-older", "Older Character", "OLD", now.AddMinutes(-25)),
                CreateWorkspace("ws-newer", "Newer Character", "NEW", now.AddMinutes(-5))
            ],
            Preferences = new ShellPreferences(RulesetDefaults.Sr5),
            Session = new ShellSessionState("ws-older")
        };
        var presenter = new ShellPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);

        Assert.AreEqual("ws-older", presenter.State.ActiveWorkspaceId?.Value);
    }

    [TestMethod]
    public async Task InitializeAsync_uses_active_workspace_ruleset_for_shell_contract()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var client = new ShellClientStub
        {
            Workspaces =
            [
                CreateWorkspace("ws-sr5", "SR5 Character", "SR5", now.AddMinutes(-25), RulesetDefaults.Sr5),
                CreateWorkspace("ws-sr6", "SR6 Character", "SR6", now.AddMinutes(-5), "sr6")
            ],
            Session = new ShellSessionState("ws-sr6")
        };
        var presenter = new ShellPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);

        Assert.AreEqual("sr6", presenter.State.ActiveRulesetId);
        CollectionAssert.Contains(client.RequestedCommandRulesets, "sr6");
        CollectionAssert.Contains(client.RequestedNavigationRulesets, "sr6");
    }

    [TestMethod]
    public async Task InitializeAsync_requests_catalogs_only_for_active_ruleset()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var client = new ShellClientStub
        {
            Workspaces =
            [
                CreateWorkspace("ws-sr5", "SR5 Character", "SR5", now.AddMinutes(-25), RulesetDefaults.Sr5),
                CreateWorkspace("ws-sr6", "SR6 Character", "SR6", now.AddMinutes(-5), "sr6")
            ],
            Session = new ShellSessionState("ws-sr6")
        };
        var presenter = new ShellPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);

        string?[] expectedSr6Rulesets = ["sr6"];
        CollectionAssert.AreEqual(expectedSr6Rulesets, client.RequestedCommandRulesets);
        CollectionAssert.AreEqual(expectedSr6Rulesets, client.RequestedNavigationRulesets);
    }

    [TestMethod]
    public async Task SyncWorkspaceContextAsync_switches_ruleset_when_active_workspace_changes()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var client = new ShellClientStub
        {
            Workspaces =
            [
                CreateWorkspace("ws-sr5", "SR5 Character", "SR5", now.AddMinutes(-5), RulesetDefaults.Sr5),
                CreateWorkspace("ws-sr6", "SR6 Character", "SR6", now.AddMinutes(-25), "sr6")
            ]
        };
        var presenter = new ShellPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);

        client.Workspaces =
        [
            CreateWorkspace("ws-sr6", "SR6 Character", "SR6", now.AddMinutes(-1), "sr6"),
            CreateWorkspace("ws-sr5", "SR5 Character", "SR5", now.AddMinutes(-20), RulesetDefaults.Sr5)
        ];
        await presenter.SyncWorkspaceContextAsync(new CharacterWorkspaceId("ws-sr6"), CancellationToken.None);

        Assert.AreEqual("ws-sr6", presenter.State.ActiveWorkspaceId?.Value);
        Assert.AreEqual("sr6", presenter.State.ActiveRulesetId);
        CollectionAssert.Contains(client.RequestedCommandRulesets, "sr6");
        CollectionAssert.Contains(client.RequestedNavigationRulesets, "sr6");
    }

    [TestMethod]
    public async Task ToggleMenuAsync_toggles_open_and_closed_state()
    {
        var presenter = new ShellPresenter(new ShellClientStub());
        await presenter.InitializeAsync(CancellationToken.None);

        await presenter.ToggleMenuAsync("file", CancellationToken.None);
        Assert.AreEqual("file", presenter.State.OpenMenuId);

        await presenter.ToggleMenuAsync("file", CancellationToken.None);
        Assert.IsNull(presenter.State.OpenMenuId);
    }

    [TestMethod]
    public async Task SelectTabAsync_rejects_disabled_tabs()
    {
        var client = new ShellClientStub
        {
            NavigationTabs =
            [
                new NavigationTabDefinition("tab-enabled", "Enabled", "profile", "character", true, true, RulesetDefaults.Sr5),
                new NavigationTabDefinition("tab-disabled", "Disabled", "profile", "character", true, false, RulesetDefaults.Sr5)
            ]
        };
        var presenter = new ShellPresenter(client);
        await presenter.InitializeAsync(CancellationToken.None);

        await presenter.SelectTabAsync("tab-disabled", CancellationToken.None);

        Assert.AreEqual("Tab 'tab-disabled' is disabled.", presenter.State.Error);
        Assert.AreEqual("tab-enabled", presenter.State.ActiveTabId);
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_requires_workspace_for_workspace_scoped_commands()
    {
        var presenter = new ShellPresenter(new ShellClientStub());
        await presenter.InitializeAsync(CancellationToken.None);

        await presenter.ExecuteCommandAsync("save_character", CancellationToken.None);

        Assert.AreEqual("Command 'save_character' is disabled in the current shell state.", presenter.State.Error);
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_menu_command_updates_open_menu_and_last_command()
    {
        var presenter = new ShellPresenter(new ShellClientStub());
        await presenter.InitializeAsync(CancellationToken.None);

        await presenter.ExecuteCommandAsync("file", CancellationToken.None);

        Assert.AreEqual("file", presenter.State.OpenMenuId);
        Assert.AreEqual("file", presenter.State.LastCommandId);
        Assert.IsNull(presenter.State.Error);
    }

    [TestMethod]
    public async Task SetPreferredRulesetAsync_updates_active_ruleset_when_no_workspace_is_open()
    {
        var client = new ShellClientStub
        {
            Workspaces = Array.Empty<WorkspaceListItem>()
        };
        var presenter = new ShellPresenter(client);
        await presenter.InitializeAsync(CancellationToken.None);

        await presenter.SetPreferredRulesetAsync("sr6", CancellationToken.None);

        Assert.AreEqual("sr6", presenter.State.PreferredRulesetId);
        Assert.AreEqual("sr6", presenter.State.ActiveRulesetId);
        CollectionAssert.Contains(client.RequestedBootstrapRulesets, "sr6");
        CollectionAssert.Contains(client.RequestedCommandRulesets, "sr6");
        CollectionAssert.Contains(client.RequestedNavigationRulesets, "sr6");
        Assert.AreEqual("sr6", client.Preferences.PreferredRulesetId);
    }

    [TestMethod]
    public async Task SetPreferredRulesetAsync_does_not_override_active_workspace_ruleset()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var client = new ShellClientStub
        {
            Workspaces =
            [
                CreateWorkspace("ws-sr5", "SR5 Character", "SR5", now, RulesetDefaults.Sr5)
            ],
            Session = new ShellSessionState(ActiveWorkspaceId: "ws-sr5")
        };
        var presenter = new ShellPresenter(client);
        await presenter.InitializeAsync(CancellationToken.None);

        await presenter.SetPreferredRulesetAsync("sr6", CancellationToken.None);

        Assert.AreEqual("sr6", presenter.State.PreferredRulesetId);
        Assert.AreEqual("sr5", presenter.State.ActiveRulesetId);
        string[] expectedSr5BootstrapRulesets = ["sr5"];
        string?[] expectedSr5Rulesets = ["sr5"];
        CollectionAssert.AreEqual(expectedSr5BootstrapRulesets, client.RequestedBootstrapRulesets);
        CollectionAssert.AreEqual(expectedSr5Rulesets, client.RequestedCommandRulesets);
        CollectionAssert.AreEqual(expectedSr5Rulesets, client.RequestedNavigationRulesets);
        Assert.AreEqual("sr6", client.Preferences.PreferredRulesetId);
    }

    [TestMethod]
    public async Task InitializeAsync_uses_saved_preferred_ruleset_when_no_workspace_is_open()
    {
        var client = new ShellClientStub
        {
            Workspaces = Array.Empty<WorkspaceListItem>(),
            Preferences = new ShellPreferences("sr6")
        };
        var presenter = new ShellPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);

        Assert.AreEqual("sr6", presenter.State.PreferredRulesetId);
        Assert.AreEqual("sr6", presenter.State.ActiveRulesetId);
        string[] expectedSr6BootstrapRulesets = ["sr6"];
        CollectionAssert.AreEqual(expectedSr6BootstrapRulesets, client.RequestedBootstrapRulesets);
    }

    [TestMethod]
    public async Task InitializeAsync_restores_active_tab_from_bootstrap_session()
    {
        var client = new ShellClientStub
        {
            Workspaces = Array.Empty<WorkspaceListItem>(),
            NavigationTabs =
            [
                new NavigationTabDefinition("tab-info", "Info", "profile", "character", true, true, RulesetDefaults.Sr5),
                new NavigationTabDefinition("tab-rules", "Rules", "rules", "character", true, true, RulesetDefaults.Sr5)
            ],
            Session = new ShellSessionState(ActiveTabId: "tab-rules")
        };
        var presenter = new ShellPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);

        Assert.AreEqual("tab-rules", presenter.State.ActiveTabId);
    }

    [TestMethod]
    public async Task InitializeAsync_projects_workflow_metadata_from_bootstrap_snapshot()
    {
        var client = new ShellClientStub
        {
            WorkflowDefinitions =
            [
                new WorkflowDefinition(
                    WorkflowId: WorkflowDefinitionIds.CareerWorkbench,
                    Title: "Career Workbench",
                    SurfaceIds: ["career.main"],
                    RequiresOpenWorkspace: true)
            ],
            WorkflowSurfaces =
            [
                new WorkflowSurfaceDefinition(
                    SurfaceId: "career.main",
                    WorkflowId: WorkflowDefinitionIds.CareerWorkbench,
                    Kind: WorkflowSurfaceKinds.Workbench,
                    RegionId: ShellRegionIds.SectionPane,
                    LayoutToken: WorkflowLayoutTokens.CareerWorkbench,
                    ActionIds: ["career.refresh"])
            ]
        };
        var presenter = new ShellPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);

        Assert.IsNotNull(presenter.State.WorkflowDefinitions);
        Assert.IsNotNull(presenter.State.WorkflowSurfaces);
        Assert.HasCount(1, presenter.State.WorkflowDefinitions);
        Assert.HasCount(1, presenter.State.WorkflowSurfaces);
        Assert.AreEqual(WorkflowDefinitionIds.CareerWorkbench, presenter.State.WorkflowDefinitions[0].WorkflowId);
        Assert.AreEqual(WorkflowDefinitionIds.CareerWorkbench, presenter.State.WorkflowSurfaces[0].WorkflowId);
    }

    [TestMethod]
    public async Task InitializeAsync_projects_active_runtime_from_bootstrap_snapshot()
    {
        var client = new ShellClientStub();
        client.ActiveRuntimesByRuleset[RulesetDefaults.Sr5] = new ActiveRuntimeStatusProjection(
            ProfileId: "official.sr5.core",
            Title: "Official SR5 Core",
            RulesetId: RulesetDefaults.Sr5,
            RuntimeFingerprint: "sha256:sr5-runtime",
            InstallState: ArtifactInstallStates.Available,
            RulePackCount: 1,
            ProviderBindingCount: 2,
            WarningCount: 1);
        var presenter = new ShellPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);

        Assert.IsNotNull(presenter.State.ActiveRuntime);
        Assert.AreEqual("official.sr5.core", presenter.State.ActiveRuntime.ProfileId);
        Assert.AreEqual("sha256:sr5-runtime", presenter.State.ActiveRuntime.RuntimeFingerprint);
        Assert.AreEqual(1, presenter.State.ActiveRuntime.WarningCount);
    }

    [TestMethod]
    public async Task SetPreferredRulesetAsync_persists_preference_via_runtime_client()
    {
        var client = new ShellClientStub
        {
            Workspaces = Array.Empty<WorkspaceListItem>()
        };
        var presenter = new ShellPresenter(client);
        await presenter.InitializeAsync(CancellationToken.None);

        await presenter.SetPreferredRulesetAsync("sr6", CancellationToken.None);

        Assert.HasCount(1, client.SavedPreferences);
        Assert.AreEqual("sr6", client.SavedPreferences[0].PreferredRulesetId);
    }

    [TestMethod]
    public async Task SelectTabAsync_persists_active_tab_in_shell_session()
    {
        var client = new ShellClientStub();
        var presenter = new ShellPresenter(client);
        await presenter.InitializeAsync(CancellationToken.None);

        await presenter.SelectTabAsync("tab-info", CancellationToken.None);

        Assert.IsNotEmpty(client.SavedSessions);
        Assert.AreEqual("tab-info", client.SavedSessions[^1].ActiveTabId);
    }

    [TestMethod]
    public async Task SelectTabAsync_persists_workspace_tab_map_for_active_workspace()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var client = new ShellClientStub
        {
            Workspaces =
            [
                CreateWorkspace("ws-1", "Runner", "R1", now)
            ],
            Session = new ShellSessionState(ActiveWorkspaceId: "ws-1")
        };
        var presenter = new ShellPresenter(client);
        await presenter.InitializeAsync(CancellationToken.None);

        await presenter.SelectTabAsync("tab-rules", CancellationToken.None);

        Assert.IsNotEmpty(client.SavedSessions);
        Assert.IsNotNull(client.SavedSessions[^1].ActiveTabsByWorkspace);
        Assert.AreEqual("tab-rules", client.SavedSessions[^1].ActiveTabsByWorkspace!["ws-1"]);
    }

    [TestMethod]
    public async Task SyncWorkspaceContextAsync_restores_workspace_specific_tab_when_switching_workspaces()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var client = new ShellClientStub
        {
            Workspaces =
            [
                CreateWorkspace("ws-1", "One", "ONE", now.AddMinutes(-5)),
                CreateWorkspace("ws-2", "Two", "TWO", now.AddMinutes(-1))
            ],
            Session = new ShellSessionState(
                ActiveWorkspaceId: "ws-1",
                ActiveTabId: "tab-info",
                ActiveTabsByWorkspace: new Dictionary<string, string>
                {
                    ["ws-1"] = "tab-info",
                    ["ws-2"] = "tab-rules"
                })
        };
        var presenter = new ShellPresenter(client);
        await presenter.InitializeAsync(CancellationToken.None);

        await presenter.SyncWorkspaceContextAsync(new CharacterWorkspaceId("ws-2"), CancellationToken.None);

        Assert.AreEqual("tab-rules", presenter.State.ActiveTabId);
    }

    [TestMethod]
    public async Task SyncOverviewFeedback_updates_shell_feedback_and_saved_workspace_status()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var client = new ShellClientStub
        {
            Workspaces =
            [
                CreateWorkspace("ws-1", "Runner", "R1", now, hasSavedWorkspace: false)
            ],
            Session = new ShellSessionState(ActiveWorkspaceId: "ws-1")
        };
        var presenter = new ShellPresenter(client);
        await presenter.InitializeAsync(CancellationToken.None);

        presenter.SyncOverviewFeedback(new ShellOverviewFeedback(
            OpenWorkspaces:
            [
                new ShellWorkspaceState(
                    Id: new CharacterWorkspaceId("ws-1"),
                    Name: "Runner",
                    Alias: "R1",
                    LastOpenedUtc: now,
                    RulesetId: RulesetDefaults.Sr5,
                    HasSavedWorkspace: true)
            ],
            Notice: "Workspace saved.",
            Error: null,
            LastCommandId: "save_character"));

        Assert.IsTrue(presenter.State.OpenWorkspaces[0].HasSavedWorkspace);
        Assert.AreEqual("Workspace saved.", presenter.State.Notice);
        Assert.AreEqual("save_character", presenter.State.LastCommandId);
        Assert.AreEqual("ws-1", presenter.State.ActiveWorkspaceId?.Value);
    }

    private static WorkspaceListItem CreateWorkspace(
        string id,
        string name,
        string alias,
        DateTimeOffset lastUpdatedUtc,
        string rulesetId = RulesetDefaults.Sr5,
        bool hasSavedWorkspace = false)
    {
        return new WorkspaceListItem(
            Id: new CharacterWorkspaceId(id),
            Summary: new CharacterFileSummary(
                Name: name,
                Alias: alias,
                Metatype: "Human",
                BuildMethod: "Priority",
                CreatedVersion: "5",
                AppVersion: "5",
                Karma: 0m,
                Nuyen: 0m,
                Created: true),
            LastUpdatedUtc: lastUpdatedUtc,
            RulesetId: rulesetId,
            HasSavedWorkspace: hasSavedWorkspace);
    }

    private sealed class ShellClientStub : IChummerClient
    {
        public IReadOnlyList<AppCommandDefinition> Commands { get; set; } = AppCommandCatalog.All;

        public IReadOnlyList<NavigationTabDefinition> NavigationTabs { get; set; } = NavigationTabCatalog.All;

        public IReadOnlyList<WorkflowDefinition> WorkflowDefinitions { get; set; } = [];

        public IReadOnlyList<WorkflowSurfaceDefinition> WorkflowSurfaces { get; set; } = [];

        public Dictionary<string, ActiveRuntimeStatusProjection> ActiveRuntimesByRuleset { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<WorkspaceListItem> Workspaces { get; set; } = Array.Empty<WorkspaceListItem>();

        public ShellPreferences Preferences { get; set; } = new(RulesetDefaults.Sr5);

        public ShellSessionState Session { get; set; } = ShellSessionState.Default;

        public List<ShellPreferences> SavedPreferences { get; } = new();

        public List<ShellSessionState> SavedSessions { get; } = new();

        public List<string?> RequestedCommandRulesets { get; } = new();

        public List<string?> RequestedNavigationRulesets { get; } = new();

        public List<string?> RequestedBootstrapRulesets { get; } = new();

        public Task<IReadOnlyList<AppCommandDefinition>> GetCommandsAsync(string? rulesetId, CancellationToken ct)
        {
            RequestedCommandRulesets.Add(rulesetId);
            return Task.FromResult(Commands);
        }

        public Task<IReadOnlyList<NavigationTabDefinition>> GetNavigationTabsAsync(string? rulesetId, CancellationToken ct)
        {
            RequestedNavigationRulesets.Add(rulesetId);
            return Task.FromResult(NavigationTabs);
        }

        public Task<IReadOnlyList<WorkspaceListItem>> ListWorkspacesAsync(CancellationToken ct) => Task.FromResult(Workspaces);

        public Task<ShellPreferences> GetShellPreferencesAsync(CancellationToken ct)
            => Task.FromResult(Preferences);

        public Task SaveShellPreferencesAsync(ShellPreferences preferences, CancellationToken ct)
        {
            Preferences = new ShellPreferences(
                PreferredRulesetId: RulesetDefaults.NormalizeOptional(preferences.PreferredRulesetId) ?? string.Empty);
            SavedPreferences.Add(Preferences);
            return Task.CompletedTask;
        }

        public Task<ShellSessionState> GetShellSessionAsync(CancellationToken ct)
            => Task.FromResult(Session);

        public Task SaveShellSessionAsync(ShellSessionState session, CancellationToken ct)
        {
            Session = new ShellSessionState(
                ActiveWorkspaceId: NormalizeWorkspaceId(session.ActiveWorkspaceId),
                ActiveTabId: NormalizeTabId(session.ActiveTabId),
                ActiveTabsByWorkspace: NormalizeWorkspaceTabMap(session.ActiveTabsByWorkspace));
            SavedSessions.Add(Session);
            return Task.CompletedTask;
        }

        public async Task<ShellBootstrapSnapshot> GetShellBootstrapAsync(string? rulesetId, CancellationToken ct)
        {
            IReadOnlyList<WorkspaceListItem> workspaces = await ListWorkspacesAsync(ct);
            CharacterWorkspaceId? activeWorkspaceId = ResolveActiveWorkspaceId(workspaces, Session.ActiveWorkspaceId);
            string preferredRulesetId = RulesetDefaults.NormalizeOptional(Preferences.PreferredRulesetId) ?? string.Empty;
            string activeRulesetId = activeWorkspaceId is null
                ? preferredRulesetId
                : RulesetDefaults.NormalizeOptional(
                    workspaces.First(workspace => string.Equals(workspace.Id.Value, activeWorkspaceId.Value.Value, StringComparison.Ordinal)).RulesetId) ?? string.Empty;
            string effectiveRulesetId = string.IsNullOrWhiteSpace(rulesetId)
                ? activeRulesetId
                : RulesetDefaults.NormalizeRequired(rulesetId);
            RequestedBootstrapRulesets.Add(effectiveRulesetId);
            IReadOnlyList<AppCommandDefinition> commands = await GetCommandsAsync(effectiveRulesetId, ct);
            IReadOnlyList<NavigationTabDefinition> tabs = await GetNavigationTabsAsync(effectiveRulesetId, ct);
            ActiveRuntimeStatusProjection? activeRuntime = ActiveRuntimesByRuleset.GetValueOrDefault(effectiveRulesetId);
            return new ShellBootstrapSnapshot(
                RulesetId: effectiveRulesetId,
                Commands: commands,
                NavigationTabs: tabs,
                Workspaces: workspaces,
                PreferredRulesetId: preferredRulesetId,
                ActiveRulesetId: activeRulesetId,
                ActiveWorkspaceId: activeWorkspaceId,
                ActiveTabId: NormalizeTabId(Session.ActiveTabId),
                ActiveTabsByWorkspace: NormalizeWorkspaceTabMap(Session.ActiveTabsByWorkspace),
                WorkflowDefinitions: WorkflowDefinitions,
                WorkflowSurfaces: WorkflowSurfaces,
                ActiveRuntime: activeRuntime);
        }

        public Task<RuntimeInspectorProjection?> GetRuntimeInspectorProfileAsync(string profileId, string? rulesetId, CancellationToken ct)
        {
            return Task.FromResult<RuntimeInspectorProjection?>(null);
        }

        public Task<WorkspaceImportResult> ImportAsync(WorkspaceImportDocument document, CancellationToken ct) => throw new NotImplementedException();

        public Task<bool> CloseWorkspaceAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<JsonNode> GetSectionAsync(CharacterWorkspaceId id, string sectionId, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterFileSummary> GetSummaryAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterValidationResult> ValidateAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterProfileSection> GetProfileAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterProgressSection> GetProgressAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterSkillsSection> GetSkillsAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterRulesSection> GetRulesAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterBuildSection> GetBuildAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterMovementSection> GetMovementAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterAwakeningSection> GetAwakeningAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CommandResult<CharacterProfileSection>> UpdateMetadataAsync(CharacterWorkspaceId id, UpdateWorkspaceMetadata command, CancellationToken ct) => throw new NotImplementedException();

        public Task<CommandResult<WorkspaceSaveReceipt>> SaveAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CommandResult<WorkspaceDownloadReceipt>> DownloadAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CommandResult<WorkspaceExportReceipt>> ExportAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CommandResult<WorkspacePrintReceipt>> PrintAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        private static string? NormalizeWorkspaceId(string? workspaceId)
        {
            return string.IsNullOrWhiteSpace(workspaceId)
                ? null
                : workspaceId.Trim();
        }

        private static string? NormalizeTabId(string? tabId)
        {
            return string.IsNullOrWhiteSpace(tabId)
                ? null
                : tabId.Trim();
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
                string? normalizedWorkspaceId = NormalizeWorkspaceId(workspaceId);
                string? normalizedTabId = NormalizeTabId(tabId);
                if (normalizedWorkspaceId is null || normalizedTabId is null)
                {
                    continue;
                }

                normalized[normalizedWorkspaceId] = normalizedTabId;
            }

            return normalized.Count == 0
                ? null
                : normalized;
        }

        private static CharacterWorkspaceId? ResolveActiveWorkspaceId(
            IReadOnlyList<WorkspaceListItem> workspaces,
            string? preferredWorkspaceId)
        {
            if (string.IsNullOrWhiteSpace(preferredWorkspaceId))
                return null;

            WorkspaceListItem? matchingWorkspace = workspaces.FirstOrDefault(workspace =>
                string.Equals(workspace.Id.Value, preferredWorkspaceId, StringComparison.Ordinal));
            return matchingWorkspace?.Id;
        }
    }
}
