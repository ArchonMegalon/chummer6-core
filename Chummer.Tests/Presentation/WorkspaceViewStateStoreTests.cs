#nullable enable annotations

using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public class WorkspaceViewStateStoreTests
{
    [TestMethod]
    public void Capture_and_restore_round_trips_workspace_view_state()
    {
        var store = new WorkspaceViewStateStore();
        var workspaceId = new CharacterWorkspaceId("ws-a");
        var state = CharacterOverviewState.Empty with
        {
            ActiveTabId = "tab-skills",
            ActiveActionId = "tab-skills.skills",
            ActiveSectionId = "skills",
            ActiveSectionJson = "{\"sectionId\":\"skills\"}",
            ActiveSectionRows =
            [
                new SectionRowState("skills[0].name", "Pistols"),
                new SectionRowState("skills[1].name", "Sneaking")
            ],
            HasSavedWorkspace = true
        };

        store.Capture(workspaceId, state);
        WorkspaceViewState? restored = store.Restore(workspaceId);

        Assert.IsNotNull(restored);
        Assert.AreEqual("tab-skills", restored.ActiveTabId);
        Assert.AreEqual("tab-skills.skills", restored.ActiveActionId);
        Assert.AreEqual("skills", restored.ActiveSectionId);
        Assert.AreEqual("{\"sectionId\":\"skills\"}", restored.ActiveSectionJson);
        Assert.HasCount(2, restored.ActiveSectionRows);
        Assert.AreEqual("skills[0].name", restored.ActiveSectionRows[0].Path);
        Assert.AreEqual("skills[1].name", restored.ActiveSectionRows[1].Path);
        Assert.IsTrue(restored.HasSavedWorkspace);
    }

    [TestMethod]
    public void Remove_clears_workspace_view_state_for_single_workspace()
    {
        var store = new WorkspaceViewStateStore();
        var workspaceId = new CharacterWorkspaceId("ws-a");
        store.Capture(workspaceId, CharacterOverviewState.Empty with { ActiveTabId = "tab-info" });

        store.Remove(workspaceId);

        Assert.IsNull(store.Restore(workspaceId));
    }

    [TestMethod]
    public void Clear_removes_workspace_view_state_for_all_workspaces()
    {
        var store = new WorkspaceViewStateStore();
        store.Capture(new CharacterWorkspaceId("ws-a"), CharacterOverviewState.Empty with { ActiveTabId = "tab-info" });
        store.Capture(new CharacterWorkspaceId("ws-b"), CharacterOverviewState.Empty with { ActiveTabId = "tab-skills" });

        store.Clear();

        Assert.IsNull(store.Restore(new CharacterWorkspaceId("ws-a")));
        Assert.IsNull(store.Restore(new CharacterWorkspaceId("ws-b")));
    }
}
