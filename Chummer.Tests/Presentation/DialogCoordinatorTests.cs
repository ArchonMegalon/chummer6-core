#nullable enable annotations

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public class DialogCoordinatorTests
{
    [TestMethod]
    public async Task CoordinateAsync_save_global_settings_updates_preferences_and_closes_dialog()
    {
        DialogCoordinator coordinator = new();
        CharacterOverviewState published = CharacterOverviewState.Empty with
        {
            ActiveDialog = new DesktopDialogState(
                Id: "dialog.global_settings",
                Title: "Global Settings",
                Message: null,
                Fields:
                [
                    new DesktopDialogField("globalUiScale", "UI Scale", "125", "100"),
                    new DesktopDialogField("globalTheme", "Theme", "dark-steel", "chummer"),
                    new DesktopDialogField("globalLanguage", "Language", "de-de", "en-us"),
                    new DesktopDialogField("globalCompactMode", "Compact", "true", "false")
                ],
                Actions:
                [
                    new DesktopDialogAction("save", "Save", true)
                ])
        };

        DialogCoordinationContext context = new(
            State: published,
            Publish: state => published = state,
            ImportAsync: static (_, _) => Task.CompletedTask,
            UpdateMetadataAsync: static (_, _) => Task.CompletedTask,
            GetState: () => published);

        await coordinator.CoordinateAsync("save", context, CancellationToken.None);

        Assert.IsNull(published.ActiveDialog);
        Assert.AreEqual(125, published.Preferences.UiScalePercent);
        Assert.AreEqual("dark-steel", published.Preferences.Theme);
        Assert.AreEqual("de-de", published.Preferences.Language);
        Assert.IsTrue(published.Preferences.CompactMode);
    }

    [TestMethod]
    public async Task CoordinateAsync_apply_metadata_calls_update_delegate_and_closes_dialog_on_success()
    {
        DialogCoordinator coordinator = new();
        CharacterOverviewState published = CharacterOverviewState.Empty with
        {
            ActiveDialog = new DesktopDialogState(
                Id: "dialog.workspace.metadata",
                Title: "Metadata",
                Message: null,
                Fields:
                [
                    new DesktopDialogField("metadataName", "Name", "  Neo  ", string.Empty),
                    new DesktopDialogField("metadataAlias", "Alias", "  One  ", string.Empty),
                    new DesktopDialogField("metadataNotes", "Notes", "Runner", string.Empty, IsMultiline: true)
                ],
                Actions:
                [
                    new DesktopDialogAction("apply_metadata", "Apply", true)
                ])
        };

        UpdateWorkspaceMetadata? captured = null;
        DialogCoordinationContext context = new(
            State: published,
            Publish: state => published = state,
            ImportAsync: static (_, _) => Task.CompletedTask,
            UpdateMetadataAsync: (command, _) =>
            {
                captured = command;
                published = published with { Error = null };
                return Task.CompletedTask;
            },
            GetState: () => published);

        await coordinator.CoordinateAsync("apply_metadata", context, CancellationToken.None);

        Assert.IsNotNull(captured);
        Assert.AreEqual("Neo", captured!.Name);
        Assert.AreEqual("One", captured.Alias);
        Assert.AreEqual("Runner", captured.Notes);
        Assert.IsNull(published.ActiveDialog);
        Assert.AreEqual("Metadata updated.", published.Notice);
    }

    [TestMethod]
    public async Task CoordinateAsync_apply_metadata_keeps_dialog_open_when_update_sets_error()
    {
        DialogCoordinator coordinator = new();
        CharacterOverviewState published = CharacterOverviewState.Empty with
        {
            ActiveDialog = new DesktopDialogState(
                Id: "dialog.workspace.metadata",
                Title: "Metadata",
                Message: null,
                Fields:
                [
                    new DesktopDialogField("metadataName", "Name", "Neo", string.Empty)
                ],
                Actions:
                [
                    new DesktopDialogAction("apply_metadata", "Apply", true)
                ])
        };

        DialogCoordinationContext context = new(
            State: published,
            Publish: state => published = state,
            ImportAsync: static (_, _) => Task.CompletedTask,
            UpdateMetadataAsync: (_, _) =>
            {
                published = published with { Error = "boom" };
                return Task.CompletedTask;
            },
            GetState: () => published);

        await coordinator.CoordinateAsync("apply_metadata", context, CancellationToken.None);

        Assert.AreEqual("boom", published.Error);
        Assert.IsNotNull(published.ActiveDialog);
    }

    [TestMethod]
    public async Task CoordinateAsync_roll_adds_result_field_to_dice_dialog()
    {
        DialogCoordinator coordinator = new();
        CharacterOverviewState published = CharacterOverviewState.Empty with
        {
            ActiveDialog = new DesktopDialogState(
                Id: "dialog.dice_roller",
                Title: "Dice Roller",
                Message: null,
                Fields:
                [
                    new DesktopDialogField("diceExpression", "Expression", "3d6+2", "1d6")
                ],
                Actions:
                [
                    new DesktopDialogAction("roll", "Roll", true)
                ])
        };

        DialogCoordinationContext context = new(
            State: published,
            Publish: state => published = state,
            ImportAsync: static (_, _) => Task.CompletedTask,
            UpdateMetadataAsync: static (_, _) => Task.CompletedTask,
            GetState: () => published);

        await coordinator.CoordinateAsync("roll", context, CancellationToken.None);

        Assert.IsNotNull(published.ActiveDialog);
        Assert.IsNotNull(published.ActiveDialog!.Fields.FirstOrDefault(field => string.Equals(field.Id, "diceResult", StringComparison.Ordinal)));
        StringAssert.Contains(published.Notice ?? string.Empty, "3d6+2");
    }

    [TestMethod]
    public async Task CoordinateAsync_import_imports_workspace_and_closes_dialog_on_success()
    {
        DialogCoordinator coordinator = new();
        CharacterOverviewState published = CharacterOverviewState.Empty with
        {
            ActiveDialog = new DesktopDialogState(
                Id: "dialog.open_character",
                Title: "Open Character",
                Message: null,
                Fields:
                [
                    new DesktopDialogField("importRulesetId", "Ruleset", " SR6 ", "sr5"),
                    new DesktopDialogField("openCharacterXml", "Character XML", "<character><name>Runner</name></character>", "<character />", true)
                ],
                Actions:
                [
                    new DesktopDialogAction("import", "Import", true)
                ])
        };

        WorkspaceImportDocument? imported = null;
        DialogCoordinationContext context = new(
            State: published,
            Publish: state => published = state,
            ImportAsync: (document, _) =>
            {
                imported = document;
                published = published with
                {
                    Error = null,
                    WorkspaceId = new CharacterWorkspaceId("ws-imported")
                };
                return Task.CompletedTask;
            },
            UpdateMetadataAsync: static (_, _) => Task.CompletedTask,
            GetState: () => published);

        await coordinator.CoordinateAsync("import", context, CancellationToken.None);

        Assert.IsNotNull(imported);
        StringAssert.Contains(imported!.Content, "<character>");
        Assert.AreEqual("sr6", imported.RulesetId);
        Assert.IsNull(published.ActiveDialog);
        Assert.AreEqual("Character imported.", published.Notice);
        Assert.AreEqual("ws-imported", published.WorkspaceId?.Value);
    }

    [TestMethod]
    public async Task CoordinateAsync_apply_ruleset_calls_delegate_and_closes_dialog_on_success()
    {
        DialogCoordinator coordinator = new();
        CharacterOverviewState published = CharacterOverviewState.Empty with
        {
            ActiveDialog = new DesktopDialogState(
                Id: "dialog.switch_ruleset",
                Title: "Switch Ruleset",
                Message: null,
                Fields:
                [
                    new DesktopDialogField("preferredRulesetId", "Ruleset", " SR6 ", RulesetDefaults.Sr5)
                ],
                Actions:
                [
                    new DesktopDialogAction("apply_ruleset", "Apply", true)
                ])
        };

        string? preferredRulesetId = null;
        DialogCoordinationContext context = new(
            State: published,
            Publish: state => published = state,
            ImportAsync: static (_, _) => Task.CompletedTask,
            UpdateMetadataAsync: static (_, _) => Task.CompletedTask,
            GetState: () => published,
            SetPreferredRulesetAsync: (rulesetId, _) =>
            {
                preferredRulesetId = rulesetId;
                published = published with { Error = null };
                return Task.CompletedTask;
            });

        await coordinator.CoordinateAsync("apply_ruleset", context, CancellationToken.None);

        Assert.AreEqual("sr6", preferredRulesetId);
        Assert.IsNull(published.ActiveDialog);
        Assert.AreEqual("Preferred ruleset set to 'sr6'.", published.Notice);
    }

    [TestMethod]
    public async Task CoordinateAsync_apply_ruleset_rejects_blank_ruleset()
    {
        DialogCoordinator coordinator = new();
        CharacterOverviewState published = CharacterOverviewState.Empty with
        {
            ActiveDialog = new DesktopDialogState(
                Id: "dialog.switch_ruleset",
                Title: "Switch Ruleset",
                Message: null,
                Fields:
                [
                    new DesktopDialogField("preferredRulesetId", "Ruleset", "   ", string.Empty)
                ],
                Actions:
                [
                    new DesktopDialogAction("apply_ruleset", "Apply", true)
                ])
        };

        DialogCoordinationContext context = new(
            State: published,
            Publish: state => published = state,
            ImportAsync: static (_, _) => Task.CompletedTask,
            UpdateMetadataAsync: static (_, _) => Task.CompletedTask,
            GetState: () => published);

        await coordinator.CoordinateAsync("apply_ruleset", context, CancellationToken.None);

        Assert.AreEqual("Preferred ruleset is required.", published.Error);
        Assert.IsNotNull(published.ActiveDialog);
    }

    [TestMethod]
    public async Task CoordinateAsync_import_rejects_blank_ruleset()
    {
        DialogCoordinator coordinator = new();
        CharacterOverviewState published = CharacterOverviewState.Empty with
        {
            ActiveDialog = new DesktopDialogState(
                Id: "dialog.open_character",
                Title: "Open Character",
                Message: null,
                Fields:
                [
                    new DesktopDialogField("importRulesetId", "Ruleset", "   ", string.Empty),
                    new DesktopDialogField("openCharacterXml", "Character XML", "<character><name>Runner</name></character>", "<character />", true)
                ],
                Actions:
                [
                    new DesktopDialogAction("import", "Import", true)
                ])
        };

        DialogCoordinationContext context = new(
            State: published,
            Publish: state => published = state,
            ImportAsync: static (_, _) => Task.CompletedTask,
            UpdateMetadataAsync: static (_, _) => Task.CompletedTask,
            GetState: () => published);

        await coordinator.CoordinateAsync("import", context, CancellationToken.None);

        Assert.AreEqual("Ruleset is required.", published.Error);
        Assert.IsNotNull(published.ActiveDialog);
    }

    [TestMethod]
    public async Task CoordinateAsync_hero_lab_import_imports_workspace_and_sets_compat_notice()
    {
        DialogCoordinator coordinator = new();
        CharacterOverviewState published = CharacterOverviewState.Empty with
        {
            ActiveDialog = new DesktopDialogState(
                Id: "dialog.hero_lab_importer",
                Title: "Hero Lab Importer",
                Message: null,
                Fields:
                [
                    new DesktopDialogField("importRulesetId", "Ruleset", " sr6 ", string.Empty),
                    new DesktopDialogField("heroLabXml", "Hero Lab XML", "<character><name>Hero</name></character>", "<character />", true)
                ],
                Actions:
                [
                    new DesktopDialogAction("import", "Import", true)
                ])
        };

        WorkspaceImportDocument? imported = null;
        DialogCoordinationContext context = new(
            State: published,
            Publish: state => published = state,
            ImportAsync: (document, _) =>
            {
                imported = document;
                published = published with
                {
                    Error = null,
                    WorkspaceId = new CharacterWorkspaceId("ws-hero")
                };
                return Task.CompletedTask;
            },
            UpdateMetadataAsync: static (_, _) => Task.CompletedTask,
            GetState: () => published);

        await coordinator.CoordinateAsync("import", context, CancellationToken.None);

        Assert.IsNotNull(imported);
        StringAssert.Contains(imported!.Content, "<character>");
        Assert.AreEqual("sr6", imported.RulesetId);
        Assert.IsNull(published.ActiveDialog);
        Assert.AreEqual("Hero Lab XML imported.", published.Notice);
        Assert.AreEqual("ws-hero", published.WorkspaceId?.Value);
    }

    [TestMethod]
    public async Task CoordinateAsync_add_gear_adds_item_and_closes_dialog()
    {
        DialogCoordinator coordinator = new();
        CharacterOverviewState published = CharacterOverviewState.Empty with
        {
            ActiveDialog = new DesktopDialogState(
                Id: "dialog.ui.gear_add",
                Title: "Add Gear",
                Message: null,
                Fields:
                [
                    new DesktopDialogField("uiGearName", "Gear Name", "Ares Alpha", "Ares Predator")
                ],
                Actions:
                [
                    new DesktopDialogAction("add", "Add", true)
                ])
        };

        DialogCoordinationContext context = new(
            State: published,
            Publish: state => published = state,
            ImportAsync: static (_, _) => Task.CompletedTask,
            UpdateMetadataAsync: static (_, _) => Task.CompletedTask,
            GetState: () => published);

        await coordinator.CoordinateAsync("add", context, CancellationToken.None);

        Assert.IsNull(published.ActiveDialog);
        Assert.AreEqual("Gear 'Ares Alpha' added.", published.Notice);
    }

    [TestMethod]
    public async Task CoordinateAsync_apply_contact_edit_updates_notice_and_closes_dialog()
    {
        DialogCoordinator coordinator = new();
        CharacterOverviewState published = CharacterOverviewState.Empty with
        {
            ActiveDialog = new DesktopDialogState(
                Id: "dialog.ui.contact_edit",
                Title: "Edit Contact",
                Message: null,
                Fields:
                [
                    new DesktopDialogField("uiContactEditName", "Name", "Nines", "Selected Contact")
                ],
                Actions:
                [
                    new DesktopDialogAction("apply", "Apply", true)
                ])
        };

        DialogCoordinationContext context = new(
            State: published,
            Publish: state => published = state,
            ImportAsync: static (_, _) => Task.CompletedTask,
            UpdateMetadataAsync: static (_, _) => Task.CompletedTask,
            GetState: () => published);

        await coordinator.CoordinateAsync("apply", context, CancellationToken.None);

        Assert.IsNull(published.ActiveDialog);
        Assert.AreEqual("Contact renamed to 'Nines'.", published.Notice);
    }

    [TestMethod]
    public async Task CoordinateAsync_delete_skill_remove_closes_dialog_with_notice()
    {
        DialogCoordinator coordinator = new();
        CharacterOverviewState published = CharacterOverviewState.Empty with
        {
            ActiveDialog = new DesktopDialogState(
                Id: "dialog.ui.skill_remove",
                Title: "Remove Skill",
                Message: null,
                Fields: [],
                Actions:
                [
                    new DesktopDialogAction("delete", "Delete", true)
                ])
        };

        DialogCoordinationContext context = new(
            State: published,
            Publish: state => published = state,
            ImportAsync: static (_, _) => Task.CompletedTask,
            UpdateMetadataAsync: static (_, _) => Task.CompletedTask,
            GetState: () => published);

        await coordinator.CoordinateAsync("delete", context, CancellationToken.None);

        Assert.IsNull(published.ActiveDialog);
        Assert.AreEqual("Skill removed.", published.Notice);
    }
}
