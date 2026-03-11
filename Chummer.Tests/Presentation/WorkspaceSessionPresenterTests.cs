using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public class WorkspaceSessionPresenterTests
{
    [TestMethod]
    public void Restore_sets_active_workspace_and_recent_order()
    {
        WorkspaceSessionPresenter presenter = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        WorkspaceListItem[] workspaces =
        [
            CreateWorkspace("ws-old", "Old", "O", now.AddMinutes(-20)),
            CreateWorkspace("ws-new", "New", "N", now.AddMinutes(-5))
        ];

        WorkspaceSessionState state = presenter.Restore(workspaces);

        Assert.AreEqual("ws-new", state.ActiveWorkspaceId?.Value);
        string[] expectedOrder = ["ws-new", "ws-old"];
        CollectionAssert.AreEqual(
            expectedOrder,
            state.OpenWorkspaces.Select(workspace => workspace.Id.Value).ToArray());
        CollectionAssert.AreEqual(
            expectedOrder,
            state.RecentWorkspaceIds.Select(id => id.Value).ToArray());
    }

    [TestMethod]
    public void Restore_prefers_explicit_active_workspace_when_available()
    {
        WorkspaceSessionPresenter presenter = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        WorkspaceListItem[] workspaces =
        [
            CreateWorkspace("ws-old", "Old", "O", now.AddMinutes(-20)),
            CreateWorkspace("ws-new", "New", "N", now.AddMinutes(-5))
        ];

        WorkspaceSessionState state = presenter.Restore(
            workspaces,
            new CharacterWorkspaceId("ws-old"));

        Assert.AreEqual("ws-old", state.ActiveWorkspaceId?.Value);
        string[] expectedRecent = ["ws-new", "ws-old"];
        CollectionAssert.AreEqual(
            expectedRecent,
            state.RecentWorkspaceIds.Select(id => id.Value).ToArray());
    }

    [TestMethod]
    public void Switch_updates_active_workspace_and_recent_order()
    {
        WorkspaceSessionPresenter presenter = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        presenter.Restore(
        [
            CreateWorkspace("ws-1", "One", "A", now.AddMinutes(-10)),
            CreateWorkspace("ws-2", "Two", "B", now.AddMinutes(-5))
        ]);

        WorkspaceSessionState switched = presenter.Switch(new CharacterWorkspaceId("ws-1"));

        Assert.AreEqual("ws-1", switched.ActiveWorkspaceId?.Value);
        string[] expectedRecent = ["ws-1", "ws-2"];
        CollectionAssert.AreEqual(
            expectedRecent,
            switched.RecentWorkspaceIds.Select(id => id.Value).ToArray());
    }

    [TestMethod]
    public void Open_activates_workspace_and_upserts_profile_label()
    {
        WorkspaceSessionPresenter presenter = new();
        CharacterWorkspaceId workspaceId = new("ws-open");
        CharacterProfileSection profile = CreateProfile("Opened Character", "OPEN");

        WorkspaceSessionState opened = presenter.Open(workspaceId, profile, rulesetId: "sr6");

        Assert.AreEqual("ws-open", opened.ActiveWorkspaceId?.Value);
        Assert.HasCount(1, opened.OpenWorkspaces);
        Assert.AreEqual("Opened Character", opened.OpenWorkspaces[0].Name);
        Assert.AreEqual("OPEN", opened.OpenWorkspaces[0].Alias);
        Assert.AreEqual("sr6", opened.OpenWorkspaces[0].RulesetId);
        string[] expectedRecent = ["ws-open"];
        CollectionAssert.AreEqual(
            expectedRecent,
            opened.RecentWorkspaceIds.Select(id => id.Value).ToArray());
    }

    [TestMethod]
    public void Close_active_workspace_selects_most_recent_remaining_workspace()
    {
        WorkspaceSessionPresenter presenter = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        presenter.Restore(
        [
            CreateWorkspace("ws-1", "One", "A", now.AddMinutes(-15)),
            CreateWorkspace("ws-2", "Two", "B", now.AddMinutes(-10)),
            CreateWorkspace("ws-3", "Three", "C", now.AddMinutes(-5))
        ]);

        presenter.Switch(new CharacterWorkspaceId("ws-1"));
        WorkspaceSessionState closed = presenter.Close(new CharacterWorkspaceId("ws-1"));

        Assert.AreEqual("ws-3", closed.ActiveWorkspaceId?.Value);
        string[] expectedOpenWorkspaces = ["ws-2", "ws-3"];
        CollectionAssert.AreEquivalent(
            expectedOpenWorkspaces,
            closed.OpenWorkspaces.Select(workspace => workspace.Id.Value).ToArray());
    }

    [TestMethod]
    public void CloseAll_clears_open_workspaces_and_active_workspace()
    {
        WorkspaceSessionPresenter presenter = new();
        presenter.Open(new CharacterWorkspaceId("ws-1"), CreateProfile("One", "A"), rulesetId: "sr5");
        presenter.Open(new CharacterWorkspaceId("ws-2"), CreateProfile("Two", "B"), rulesetId: "sr5");

        WorkspaceSessionState cleared = presenter.CloseAll();

        Assert.IsNull(cleared.ActiveWorkspaceId);
        Assert.IsEmpty(cleared.OpenWorkspaces);
        string[] expectedRecent = ["ws-2", "ws-1"];
        CollectionAssert.AreEqual(
            expectedRecent,
            cleared.RecentWorkspaceIds.Select(id => id.Value).ToArray());
    }

    [TestMethod]
    public void SetSavedStatus_updates_only_target_workspace()
    {
        WorkspaceSessionPresenter presenter = new();
        presenter.Open(new CharacterWorkspaceId("ws-1"), CreateProfile("One", "A"), rulesetId: "sr5");
        presenter.Open(new CharacterWorkspaceId("ws-2"), CreateProfile("Two", "B"), rulesetId: "sr5");

        WorkspaceSessionState updated = presenter.SetSavedStatus(new CharacterWorkspaceId("ws-1"), hasSavedWorkspace: true);

        OpenWorkspaceState ws1 = updated.OpenWorkspaces.First(workspace => string.Equals(workspace.Id.Value, "ws-1", StringComparison.Ordinal));
        OpenWorkspaceState ws2 = updated.OpenWorkspaces.First(workspace => string.Equals(workspace.Id.Value, "ws-2", StringComparison.Ordinal));
        Assert.IsTrue(ws1.HasSavedWorkspace);
        Assert.IsFalse(ws2.HasSavedWorkspace);
        string[] expectedRecent = ["ws-2", "ws-1"];
        CollectionAssert.AreEqual(
            expectedRecent,
            updated.RecentWorkspaceIds.Select(id => id.Value).ToArray());
    }

    private static WorkspaceListItem CreateWorkspace(
        string id,
        string name,
        string alias,
        DateTimeOffset lastUpdatedUtc)
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
            RulesetId: "sr5");
    }

    private static CharacterProfileSection CreateProfile(string name, string alias)
    {
        return new CharacterProfileSection(
            Name: name,
            Alias: alias,
            PlayerName: string.Empty,
            Metatype: "Human",
            Metavariant: string.Empty,
            Sex: string.Empty,
            Age: string.Empty,
            Height: string.Empty,
            Weight: string.Empty,
            Hair: string.Empty,
            Eyes: string.Empty,
            Skin: string.Empty,
            Concept: string.Empty,
            Description: string.Empty,
            Background: string.Empty,
            CreatedVersion: string.Empty,
            AppVersion: string.Empty,
            BuildMethod: "Priority",
            GameplayOption: string.Empty,
            Created: true,
            Adept: false,
            Magician: false,
            Technomancer: false,
            AI: false,
            MainMugshotIndex: 0,
            MugshotCount: 0);
    }
}
