#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Bunit;
using Chummer.Blazor.Components.Shell;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BunitContext = Bunit.BunitContext;

namespace Chummer.Tests.Presentation;

[TestClass]
public sealed class BlazorShellComponentTests
{
    [TestMethod]
    public void MenuBar_renders_open_menu_items_and_applies_enablement_state()
    {
        IReadOnlyList<AppCommandDefinition> menuRoots =
        [
            new AppCommandDefinition("file", "menu.file", "menu", false, true, RulesetDefaults.Sr5)
        ];
        IReadOnlyList<AppCommandDefinition> menuCommands =
        [
            new AppCommandDefinition("save_character", "command.save", "file", true, true, RulesetDefaults.Sr5),
            new AppCommandDefinition("close_character", "command.close", "file", true, true, RulesetDefaults.Sr5)
        ];

        using var context = new BunitContext();
        IRenderedComponent<MenuBar> cut = context.Render<MenuBar>(parameters => parameters
            .Add(component => component.MenuRoots, menuRoots)
            .Add(component => component.OpenMenuId, "file")
            .Add(component => component.IsBusy, false)
            .Add(component => component.MenuCommands, menuId =>
                string.Equals(menuId, "file", StringComparison.Ordinal)
                    ? menuCommands
                    : Array.Empty<AppCommandDefinition>())
            .Add(component => component.IsCommandEnabled,
                command => string.Equals(command.Id, "save_character", StringComparison.Ordinal)));

        Assert.HasCount(1, cut.FindAll(".menu-btn"));
        StringAssert.Contains(cut.Find(".menu-btn").ClassName, "active");

        IReadOnlyList<AngleSharp.Dom.IElement> menuButtons = cut.FindAll(".menu-item");
        Assert.HasCount(2, menuButtons);
        Assert.IsFalse(menuButtons[0].HasAttribute("disabled"));
        Assert.IsTrue(menuButtons[1].HasAttribute("disabled"));
    }

    [TestMethod]
    public void MenuBar_invokes_toggle_and_execute_callbacks()
    {
        string? toggledMenuId = null;
        string? executedCommandId = null;

        using var context = new BunitContext();
        IRenderedComponent<MenuBar> cut = context.Render<MenuBar>(parameters => parameters
            .Add(component => component.MenuRoots,
            [
                new AppCommandDefinition("file", "menu.file", "menu", false, true, RulesetDefaults.Sr5)
            ])
            .Add(component => component.OpenMenuId, "file")
            .Add(component => component.MenuCommands, menuId =>
                string.Equals(menuId, "file", StringComparison.Ordinal)
                    ? new[]
                    {
                        new AppCommandDefinition("save_character", "command.save", "file", true, true, RulesetDefaults.Sr5)
                    }
                    : Array.Empty<AppCommandDefinition>())
            .Add(component => component.IsCommandEnabled, _ => true)
            .Add(component => component.ToggleMenuRequested, (Action<string>)(menuId => toggledMenuId = menuId))
            .Add(component => component.ExecuteCommandRequested, (Action<string>)(commandId => executedCommandId = commandId)));

        cut.Find(".menu-btn").Click();
        cut.Find(".menu-item").Click();

        Assert.AreEqual("file", toggledMenuId);
        Assert.AreEqual("save_character", executedCommandId);
    }

    [TestMethod]
    public void ToolStrip_applies_selected_and_disabled_states()
    {
        string? executedCommandId = null;

        using var context = new BunitContext();
        IRenderedComponent<ToolStrip> cut = context.Render<ToolStrip>(parameters => parameters
            .Add(component => component.Commands,
            [
                new AppCommandDefinition("save_character", "command.save", "file", true, true, RulesetDefaults.Sr5),
                new AppCommandDefinition("print_character", "command.print", "file", true, true, RulesetDefaults.Sr5)
            ])
            .Add(component => component.LastCommandId, "print_character")
            .Add(component => component.IsBusy, false)
            .Add(component => component.IsCommandEnabled,
                command => string.Equals(command.Id, "print_character", StringComparison.Ordinal))
            .Add(component => component.ExecuteCommandRequested, (Action<string>)(commandId => executedCommandId = commandId)));

        IReadOnlyList<AngleSharp.Dom.IElement> toolButtons = cut.FindAll(".tool-btn");
        Assert.HasCount(2, toolButtons);
        Assert.IsTrue(toolButtons[0].HasAttribute("disabled"));
        Assert.IsFalse(toolButtons[1].HasAttribute("disabled"));
        StringAssert.Contains(toolButtons[1].ClassName, "selected");

        toolButtons[1].Click();
        Assert.AreEqual("print_character", executedCommandId);
    }

    [TestMethod]
    public void MdiStrip_shows_unsaved_marker_for_workspace_without_save_receipt()
    {
        CharacterWorkspaceId ws1 = new("ws-1");
        CharacterWorkspaceId ws2 = new("ws-2");
        OpenWorkspaceState dirtyWorkspace = new(ws1, "Ares Runner", "AR", DateTimeOffset.UtcNow, RulesetDefaults.Sr5, HasSavedWorkspace: false);
        OpenWorkspaceState savedWorkspace = new(ws2, "Neo Runner", "NR", DateTimeOffset.UtcNow.AddMinutes(-1), RulesetDefaults.Sr5, HasSavedWorkspace: true);

        using var context = new BunitContext();
        IRenderedComponent<MdiStrip> cut = context.Render<MdiStrip>(parameters => parameters
            .Add(component => component.OpenWorkspaces, [dirtyWorkspace, savedWorkspace])
            .Add(component => component.ActiveWorkspaceId, ws1)
            .Add(component => component.IsBusy, false));

        IReadOnlyList<AngleSharp.Dom.IElement> docs = cut.FindAll(".mdi-doc");
        Assert.HasCount(2, docs);
        StringAssert.Contains(docs[0].TextContent, "*");
        Assert.IsLessThan(0, docs[1].TextContent.IndexOf('*'));
    }

    [TestMethod]
    public void WorkspaceLeftPane_renders_shell_controls_and_invokes_callbacks()
    {
        CharacterWorkspaceId workspaceId = new("ws-1");
        OpenWorkspaceState openWorkspace = new(workspaceId, "Ares Runner", "AR", DateTimeOffset.UtcNow, RulesetDefaults.Sr5);
        CharacterOverviewState state = CharacterOverviewState.Empty with
        {
            Session = new WorkspaceSessionState(workspaceId, [openWorkspace], [workspaceId]),
            OpenWorkspaces = [openWorkspace],
            WorkspaceId = workspaceId,
            ActiveTabId = "tab-info",
            ActiveActionId = "summary",
            IsBusy = false
        };

        string? openedWorkspaceId = null;
        string? closedWorkspaceId = null;
        string? selectedTabId = null;
        WorkspaceSurfaceActionDefinition? executedAction = null;
        string? executedWorkflowSurfaceActionId = null;

        WorkspaceSurfaceActionDefinition summaryAction = new(
            Id: "summary",
            Label: "Refresh Summary",
            TabId: "tab-info",
            Kind: WorkspaceSurfaceActionKind.Summary,
            TargetId: "summary",
            RequiresOpenCharacter: true,
            EnabledByDefault: true,
            RulesetId: RulesetDefaults.Sr5);

        WorkflowSurfaceActionBinding summarySurface = new(
            SurfaceId: "surface.summary",
            WorkflowId: WorkflowDefinitionIds.CareerWorkbench,
            Label: "Refresh Summary",
            ActionId: "summary",
            RegionId: ShellRegionIds.SectionPane,
            LayoutToken: WorkflowLayoutTokens.CareerWorkbench);
        IReadOnlyList<OpenWorkspaceState> openWorkspaces = [openWorkspace];
        IReadOnlyList<NavigationTabDefinition> navigationTabs =
        [
            new NavigationTabDefinition("tab-info", "Info", "profile", "character", true, true, RulesetDefaults.Sr5),
            new NavigationTabDefinition("tab-skills", "Skills", "skills", "character", true, true, RulesetDefaults.Sr5)
        ];
        IReadOnlyList<WorkspaceSurfaceActionDefinition> workspaceActions = [summaryAction];
        IReadOnlyList<WorkflowSurfaceActionBinding> workflowSurfaceActions = [summarySurface];

        using var context = new BunitContext();
        IRenderedComponent<WorkspaceLeftPane> cut = context.Render<WorkspaceLeftPane>(parameters => parameters
            .Add(component => component.State, state)
            .Add(component => component.OpenWorkspaces, openWorkspaces)
            .Add(component => component.ActiveWorkspaceId, workspaceId)
            .Add(component => component.ActiveTabId, "tab-info")
            .Add(component => component.NavigationTabs, navigationTabs)
            .Add(component => component.ActiveWorkspaceActions, workspaceActions)
            .Add(component => component.ActiveWorkflowSurfaceActions, workflowSurfaceActions)
            .Add(component => component.IsNavigationTabEnabled,
                tab => string.Equals(tab.Id, "tab-info", StringComparison.Ordinal))
            .Add(component => component.OpenWorkspaceRequested, (Action<string>)(workspace => openedWorkspaceId = workspace))
            .Add(component => component.CloseWorkspaceRequested, (Action<string>)(workspace => closedWorkspaceId = workspace))
            .Add(component => component.SelectTabRequested, (Action<string>)(tabId => selectedTabId = tabId))
            .Add(component => component.ExecuteWorkspaceActionRequested,
                (Action<WorkspaceSurfaceActionDefinition>)(action => executedAction = action))
            .Add(component => component.ExecuteWorkflowSurfaceRequested, (Action<string>)(actionId => executedWorkflowSurfaceActionId = actionId)));

        AngleSharp.Dom.IElement enabledTab = cut.Find(".tabs #tab-info");
        AngleSharp.Dom.IElement disabledTab = cut.Find(".tabs #tab-skills");
        Assert.IsFalse(enabledTab.HasAttribute("disabled"));
        Assert.IsTrue(disabledTab.HasAttribute("disabled"));
        StringAssert.Contains(enabledTab.ClassName, "active");

        cut.Find(".navigator .command-button").Click();
        cut.Find(".navigator .mini-btn").Click();
        enabledTab.Click();
        cut.Find(".section-actions .action-button").Click();
        cut.Find("button[data-workflow-surface='surface.summary']").Click();

        Assert.AreEqual("ws-1", openedWorkspaceId);
        Assert.AreEqual("ws-1", closedWorkspaceId);
        Assert.AreEqual("tab-info", selectedTabId);
        Assert.AreEqual("summary", executedAction?.Id);
        Assert.AreEqual("summary", executedWorkflowSurfaceActionId);
    }

    [TestMethod]
    public void SectionPane_switches_between_placeholder_and_section_payload()
    {
        using var context = new BunitContext();
        IRenderedComponent<SectionPane> emptyCut = context.Render<SectionPane>(parameters => parameters
            .Add(component => component.State, CharacterOverviewState.Empty));

        StringAssert.Contains(emptyCut.Markup, "Select a tab to render a workspace section");

        CharacterOverviewState sectionState = CharacterOverviewState.Empty with
        {
            ActiveSectionId = "skills",
            ActiveSectionJson = "{\"skills\":1}",
            ActiveSectionRows = [new SectionRowState("skills[0].name", "Pistols")]
        };

        IRenderedComponent<SectionPane> sectionCut = context.Render<SectionPane>(parameters => parameters
            .Add(component => component.State, sectionState));

        Assert.HasCount(1, sectionCut.FindAll(".section-table tbody tr"));
        StringAssert.Contains(sectionCut.Markup, "skills[0].name");
        StringAssert.Contains(sectionCut.Markup, "{\"skills\":1}");
    }

    [TestMethod]
    public void DialogHost_renders_dialog_and_emits_events()
    {
        DesktopDialogState dialog = new(
            Id: "save-dialog",
            Title: "Save Character",
            Message: "Confirm save.",
            Fields:
            [
                new DesktopDialogField("name", "Name", "Old Name", "enter name"),
                new DesktopDialogField("houseRules", "House Rules", "false", string.Empty, false, false, "checkbox"),
                new DesktopDialogField("notes", "Notes", "Old", "enter notes", true, false, "text"),
                new DesktopDialogField("token", "Token", "abc", "readonly token", false, true, "text")
            ],
            Actions:
            [
                new DesktopDialogAction("cancel", "Cancel"),
                new DesktopDialogAction("save", "Save", true)
            ]);

        List<DialogFieldInputChange> inputChanges = [];
        List<DialogFieldCheckboxChange> checkboxChanges = [];
        string? executedActionId = null;
        int closeCount = 0;

        using var context = new BunitContext();
        IRenderedComponent<DialogHost> cut = context.Render<DialogHost>(parameters => parameters
            .Add(component => component.Dialog, dialog)
            .Add(component => component.CloseRequested, (Action)(() => closeCount++))
            .Add(component => component.ExecuteDialogActionRequested, (Action<string>)(actionId => executedActionId = actionId))
            .Add(component => component.FieldInputRequested, (Action<DialogFieldInputChange>)(change => inputChanges.Add(change)))
            .Add(component => component.FieldCheckboxRequested,
                (Action<DialogFieldCheckboxChange>)(change => checkboxChanges.Add(change))));

        Assert.AreEqual("Save Character", cut.Find("#dialogTitle").TextContent.Trim());
        Assert.IsTrue(cut.Find("input[placeholder='readonly token']").HasAttribute("readonly"));

        cut.Find("input[placeholder='enter name']").Input("Neo");
        cut.Find("textarea[placeholder='enter notes']").Input("Updated notes");
        cut.Find("input[type='checkbox']").Change(true);
        cut.Find("#dialogFooter .action-btn.primary").Click();
        cut.Find("#dialogClose").Click();

        string[] expectedInputFieldIds = ["name", "notes"];
        CollectionAssert.AreEquivalent(
            expectedInputFieldIds,
            inputChanges.Select(change => change.FieldId).ToArray());
        Assert.AreEqual("houseRules", checkboxChanges[0].FieldId);
        Assert.IsTrue(checkboxChanges[0].Value);
        Assert.AreEqual("save", executedActionId);
        Assert.AreEqual(1, closeCount);
    }

    [TestMethod]
    public void DialogHost_renders_nothing_without_dialog_state()
    {
        using var context = new BunitContext();
        IRenderedComponent<DialogHost> cut = context.Render<DialogHost>(parameters => parameters
            .Add(component => component.Dialog, (DesktopDialogState?)null));

        Assert.AreEqual(string.Empty, cut.Markup.Trim());
    }
}
