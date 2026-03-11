#nullable enable annotations

using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Chummer.Blazor;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public class CharacterOverviewStateBridgeTests
{
    [TestMethod]
    public async Task InitializeAsync_delegates_to_presenter()
    {
        var presenter = new FakeCharacterOverviewPresenter();
        using var bridge = new CharacterOverviewStateBridge(presenter, _ => { });

        await bridge.InitializeAsync(CancellationToken.None);

        Assert.AreEqual(1, presenter.InitializeCalls);
    }

    [TestMethod]
    public async Task LoadAsync_delegates_to_presenter()
    {
        var presenter = new FakeCharacterOverviewPresenter();
        using var bridge = new CharacterOverviewStateBridge(presenter, _ => { });

        await bridge.LoadAsync(new CharacterWorkspaceId("ws-load"), CancellationToken.None);

        Assert.AreEqual("ws-load", presenter.LoadedWorkspaceId?.Value);
    }

    [TestMethod]
    public async Task SwitchWorkspaceAsync_delegates_to_presenter()
    {
        var presenter = new FakeCharacterOverviewPresenter();
        using var bridge = new CharacterOverviewStateBridge(presenter, _ => { });

        await bridge.SwitchWorkspaceAsync(new CharacterWorkspaceId("ws-switch"), CancellationToken.None);

        Assert.AreEqual("ws-switch", presenter.SwitchedWorkspaceId?.Value);
    }

    [TestMethod]
    public async Task CloseWorkspaceAsync_delegates_to_presenter()
    {
        var presenter = new FakeCharacterOverviewPresenter();
        using var bridge = new CharacterOverviewStateBridge(presenter, _ => { });

        await bridge.CloseWorkspaceAsync(new CharacterWorkspaceId("ws-close"), CancellationToken.None);

        Assert.AreEqual("ws-close", presenter.ClosedWorkspaceId?.Value);
    }

    [TestMethod]
    public async Task SelectTabAsync_delegates_to_presenter()
    {
        var presenter = new FakeCharacterOverviewPresenter();
        using var bridge = new CharacterOverviewStateBridge(presenter, _ => { });

        await bridge.SelectTabAsync("tab-info", CancellationToken.None);

        Assert.AreEqual("tab-info", presenter.SelectedTabId);
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_delegates_to_presenter()
    {
        var presenter = new FakeCharacterOverviewPresenter();
        using var bridge = new CharacterOverviewStateBridge(presenter, _ => { });

        await bridge.ExecuteCommandAsync("save_character", CancellationToken.None);

        Assert.AreEqual("save_character", presenter.ExecutedCommandId);
    }

    [TestMethod]
    public async Task HandleUiControlAsync_delegates_to_presenter()
    {
        var presenter = new FakeCharacterOverviewPresenter();
        using var bridge = new CharacterOverviewStateBridge(presenter, _ => { });

        await bridge.HandleUiControlAsync("create_entry", CancellationToken.None);

        Assert.AreEqual("create_entry", presenter.HandledUiControlId);
    }

    [TestMethod]
    public async Task ExecuteWorkspaceActionAsync_delegates_to_presenter()
    {
        var presenter = new FakeCharacterOverviewPresenter();
        using var bridge = new CharacterOverviewStateBridge(presenter, _ => { });
        WorkspaceSurfaceActionDefinition action = new(
            Id: "tab-info.summary",
            Label: "Summary",
            TabId: "tab-info",
            Kind: WorkspaceSurfaceActionKind.Summary,
            TargetId: "summary",
            RequiresOpenCharacter: true,
            EnabledByDefault: true,
            RulesetId: RulesetDefaults.Sr5);

        await bridge.ExecuteWorkspaceActionAsync(action, CancellationToken.None);

        Assert.AreEqual("tab-info.summary", presenter.ExecutedWorkspaceActionId);
    }

    [TestMethod]
    public async Task UpdateDialogFieldAsync_delegates_to_presenter()
    {
        var presenter = new FakeCharacterOverviewPresenter();
        using var bridge = new CharacterOverviewStateBridge(presenter, _ => { });

        await bridge.UpdateDialogFieldAsync("diceExpression", "10d6", CancellationToken.None);

        Assert.AreEqual("diceExpression", presenter.UpdatedDialogFieldId);
        Assert.AreEqual("10d6", presenter.UpdatedDialogFieldValue);
    }

    [TestMethod]
    public async Task ExecuteDialogActionAsync_delegates_to_presenter()
    {
        var presenter = new FakeCharacterOverviewPresenter();
        using var bridge = new CharacterOverviewStateBridge(presenter, _ => { });

        await bridge.ExecuteDialogActionAsync("close", CancellationToken.None);

        Assert.AreEqual("close", presenter.ExecutedDialogActionId);
    }

    [TestMethod]
    public async Task CloseDialogAsync_delegates_to_presenter()
    {
        var presenter = new FakeCharacterOverviewPresenter();
        using var bridge = new CharacterOverviewStateBridge(presenter, _ => { });

        await bridge.CloseDialogAsync(CancellationToken.None);

        Assert.AreEqual(1, presenter.CloseDialogCalls);
    }

    [TestMethod]
    public async Task ImportAsync_delegates_to_presenter()
    {
        var presenter = new FakeCharacterOverviewPresenter();
        using var bridge = new CharacterOverviewStateBridge(presenter, _ => { });

        await bridge.ImportAsync(Encoding.UTF8.GetBytes("<character />"), CancellationToken.None);

        Assert.AreEqual("<character />", presenter.ImportedContent);
        Assert.AreEqual(string.Empty, presenter.ImportedRulesetId);
    }

    [TestMethod]
    public void State_change_publishes_new_snapshot_to_callback()
    {
        var presenter = new FakeCharacterOverviewPresenter();
        CharacterOverviewState? observed = null;
        using var bridge = new CharacterOverviewStateBridge(presenter, state => observed = state);

        CharacterOverviewState updated = CharacterOverviewState.Empty with
        {
            WorkspaceId = new CharacterWorkspaceId("ws-state")
        };
        presenter.Publish(updated);

        Assert.IsNotNull(observed);
        Assert.AreEqual("ws-state", observed.WorkspaceId?.Value);
        Assert.AreEqual("ws-state", bridge.Current.WorkspaceId?.Value);
    }

    [TestMethod]
    public void Dispose_unsubscribes_from_presenter_events()
    {
        var presenter = new FakeCharacterOverviewPresenter();
        int callbackCount = 0;
        var bridge = new CharacterOverviewStateBridge(presenter, _ => callbackCount++);
        bridge.Dispose();

        presenter.Publish(CharacterOverviewState.Empty with { WorkspaceId = new CharacterWorkspaceId("ws-after-dispose") });

        Assert.AreEqual(0, callbackCount);
    }
}
