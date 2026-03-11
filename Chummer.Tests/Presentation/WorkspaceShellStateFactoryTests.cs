using System;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public class WorkspaceShellStateFactoryTests
{
    [TestMethod]
    public void CreateEmptyShellState_preserves_shell_contract_from_current_state()
    {
        var factory = new WorkspaceShellStateFactory();
        CharacterOverviewState current = CharacterOverviewState.Empty with
        {
            Commands = [new AppCommandDefinition("save_character", "Save", "file", true, true, RulesetDefaults.Sr5)],
            NavigationTabs = [new NavigationTabDefinition("tab-info", "Info", "profile", "character", true, true, RulesetDefaults.Sr5)],
            LastCommandId = "save_character",
            Preferences = new DesktopPreferenceState(
                UiScalePercent: 125,
                Theme: "legacy",
                Language: "en-us",
                CompactMode: true,
                CharacterPriority: "SumToTen",
                KarmaNuyenRatio: 2,
                HouseRulesEnabled: false,
                CharacterNotes: "notes")
        };

        WorkspaceSessionState session = new(
            ActiveWorkspaceId: new CharacterWorkspaceId("ws-a"),
            OpenWorkspaces:
            [
                new OpenWorkspaceState(
                    Id: new CharacterWorkspaceId("ws-a"),
                    Name: "A",
                    Alias: "AA",
                    LastOpenedUtc: DateTimeOffset.UtcNow,
                    RulesetId: RulesetDefaults.Sr5,
                    HasSavedWorkspace: true)
            ],
            RecentWorkspaceIds: [new CharacterWorkspaceId("ws-a")]);

        CharacterOverviewState next = factory.CreateEmptyShellState(current, session, "Workspace reset complete.");

        Assert.AreEqual("Workspace reset complete.", next.Notice);
        Assert.AreEqual("save_character", next.LastCommandId);
        Assert.HasCount(1, next.Commands);
        Assert.AreEqual("save_character", next.Commands[0].Id);
        Assert.HasCount(1, next.NavigationTabs);
        Assert.AreEqual("tab-info", next.NavigationTabs[0].Id);
        Assert.AreEqual(125, next.Preferences.UiScalePercent);
        Assert.AreEqual("legacy", next.Preferences.Theme);
        Assert.AreEqual("ws-a", next.Session.ActiveWorkspaceId?.Value);
        Assert.HasCount(1, next.OpenWorkspaces);
    }

    [TestMethod]
    public void CreateEmptyShellState_can_override_last_command_id()
    {
        var factory = new WorkspaceShellStateFactory();
        CharacterOverviewState current = CharacterOverviewState.Empty with { LastCommandId = "save_character" };
        WorkspaceSessionState session = WorkspaceSessionState.Empty;

        CharacterOverviewState next = factory.CreateEmptyShellState(
            current,
            session,
            "New character workspace initialized.",
            lastCommandId: "new_character");

        Assert.AreEqual("new_character", next.LastCommandId);
        Assert.AreEqual("New character workspace initialized.", next.Notice);
    }
}
