#nullable enable annotations

using System;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Chummer.Blazor;
using Chummer.Blazor.Components.Layout;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Chummer.Rulesets.Sr5;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public sealed class DesktopShellStartupSyncTests
{
    [TestMethod]
    public void OnInitializedAsync_skips_sync_when_states_are_aligned()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        CharacterWorkspaceId workspaceId = new("ws-1");
        FakeCharacterOverviewPresenter presenter = new();
        presenter.Publish(CreateOverviewState(workspaceId));
        RecordingShellPresenter shellPresenter = new(CreateShellState(workspaceId));
        RegisterDesktopShellServices(context, presenter, shellPresenter);

        context.Render<DesktopShell>();

        Assert.AreEqual(1, shellPresenter.InitializeCalls);
        Assert.AreEqual(0, shellPresenter.SyncWorkspaceContextCalls);
    }

    [TestMethod]
    public void OnInitializedAsync_syncs_when_states_are_misaligned()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        CharacterWorkspaceId presenterWorkspaceId = new("ws-2");
        FakeCharacterOverviewPresenter presenter = new();
        presenter.Publish(CreateOverviewState(presenterWorkspaceId));
        RecordingShellPresenter shellPresenter = new(CreateShellState(new CharacterWorkspaceId("ws-1")));
        RegisterDesktopShellServices(context, presenter, shellPresenter);

        context.Render<DesktopShell>();

        Assert.AreEqual(1, shellPresenter.InitializeCalls);
        Assert.AreEqual(1, shellPresenter.SyncWorkspaceContextCalls);
        Assert.AreEqual("ws-2", shellPresenter.LastSyncedWorkspaceId?.Value);
    }

    private static void RegisterDesktopShellServices(
        BunitContext context,
        ICharacterOverviewPresenter presenter,
        IShellPresenter shellPresenter)
    {
        context.Services.AddSingleton(presenter);
        context.Services.AddSingleton(shellPresenter);
        context.Services.AddSingleton<ICommandAvailabilityEvaluator, DefaultCommandAvailabilityEvaluator>();
        context.Services.AddSingleton<IWorkbenchCoachApiClient>(FakeWorkbenchCoachApiClient.CreateDefault());
        context.Services.AddSingleton<IRulesetPlugin, Sr5RulesetPlugin>();
        context.Services.AddSingleton<IRulesetPluginRegistry, RulesetPluginRegistry>();
        context.Services.AddSingleton<IRulesetShellCatalogResolver, RulesetShellCatalogResolverService>();
        context.Services.AddSingleton<IShellSurfaceResolver, ShellSurfaceResolver>();
    }

    private static CharacterOverviewState CreateOverviewState(CharacterWorkspaceId workspaceId)
    {
        OpenWorkspaceState openWorkspace = new(
            Id: workspaceId,
            Name: "Runner",
            Alias: "RUN",
            LastOpenedUtc: DateTimeOffset.UtcNow,
            RulesetId: "sr5");
        WorkspaceSessionState session = new(
            ActiveWorkspaceId: workspaceId,
            OpenWorkspaces: [openWorkspace],
            RecentWorkspaceIds: [workspaceId]);

        return CharacterOverviewState.Empty with
        {
            Session = session,
            OpenWorkspaces = [openWorkspace],
            WorkspaceId = workspaceId
        };
    }

    private static ShellState CreateShellState(CharacterWorkspaceId workspaceId)
    {
        AppCommandDefinition menuRoot = new("file", "menu.file", "menu", false, true, RulesetDefaults.Sr5);
        NavigationTabDefinition infoTab = new("tab-info", "Info", "profile", "character", true, true, RulesetDefaults.Sr5);
        ShellWorkspaceState openWorkspace = new(
            Id: workspaceId,
            Name: "Runner",
            Alias: "RUN",
            LastOpenedUtc: DateTimeOffset.UtcNow,
            RulesetId: "sr5");

        return ShellState.Empty with
        {
            ActiveWorkspaceId = workspaceId,
            OpenWorkspaces = [openWorkspace],
            ActiveRulesetId = "sr5",
            Commands = [menuRoot],
            MenuRoots = [menuRoot],
            NavigationTabs = [infoTab],
            ActiveTabId = infoTab.Id
        };
    }

    private sealed class RecordingShellPresenter : IShellPresenter
    {
        public RecordingShellPresenter(ShellState state)
        {
            State = state;
        }

        public ShellState State { get; private set; }
        public int InitializeCalls { get; private set; }
        public int SyncWorkspaceContextCalls { get; private set; }
        public CharacterWorkspaceId? LastSyncedWorkspaceId { get; private set; }

        public event EventHandler? StateChanged;

        public Task InitializeAsync(CancellationToken ct)
        {
            InitializeCalls++;
            return Task.CompletedTask;
        }

        public Task ExecuteCommandAsync(string commandId, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task SelectTabAsync(string tabId, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ToggleMenuAsync(string menuId, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task SetPreferredRulesetAsync(string rulesetId, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task SyncWorkspaceContextAsync(CharacterWorkspaceId? activeWorkspaceId, CancellationToken ct)
        {
            SyncWorkspaceContextCalls++;
            LastSyncedWorkspaceId = activeWorkspaceId;
            State = State with { ActiveWorkspaceId = activeWorkspaceId };
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }
}
