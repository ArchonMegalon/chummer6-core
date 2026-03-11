#nullable enable annotations

using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Content;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public class CharacterOverviewPresenterTests
{
    private static readonly string[] LegacyUiControlIds =
    [
        "create_entry",
        "edit_entry",
        "delete_entry",
        "move_up",
        "move_down",
        "toggle_free_paid",
        "show_source",
        "open_notes",
        "gear_add",
        "gear_edit",
        "gear_delete",
        "gear_mount",
        "gear_source",
        "magic_add",
        "magic_delete",
        "magic_bind",
        "magic_source",
        "skill_add",
        "skill_specialize",
        "skill_remove",
        "skill_group",
        "combat_add_weapon",
        "combat_add_armor",
        "combat_reload",
        "combat_damage_track",
        "contact_add",
        "contact_edit",
        "contact_remove",
        "contact_connection"
    ];

    [TestMethod]
    public async Task InitializeAsync_loads_command_catalog()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);

        Assert.IsFalse(presenter.State.IsBusy);
        Assert.IsNull(presenter.State.Error);
        Assert.IsGreaterThan(0, presenter.State.Commands.Count);
        Assert.AreEqual("new_character", presenter.State.Commands[0].Id);
        Assert.IsGreaterThan(0, presenter.State.NavigationTabs.Count);
        Assert.AreEqual("tab-info", presenter.State.NavigationTabs[0].Id);
    }

    [TestMethod]
    public async Task InitializeAsync_restores_open_workspaces_from_service()
    {
        var client = new FakeChummerClient();
        client.SeedWorkspace("ws-legacy-1", "Legacy One", "L1", DateTimeOffset.UtcNow.AddMinutes(-10));
        client.SeedWorkspace("ws-legacy-2", "Legacy Two", "L2", DateTimeOffset.UtcNow.AddMinutes(-1));
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);

        Assert.HasCount(2, presenter.State.OpenWorkspaces);
        Assert.AreEqual("ws-legacy-2", presenter.State.OpenWorkspaces[0].Id.Value);
        Assert.AreEqual("ws-legacy-1", presenter.State.OpenWorkspaces[1].Id.Value);
        StringAssert.Contains(presenter.State.Notice ?? string.Empty, "Restored 2 workspace(s)");
    }

    [TestMethod]
    public async Task InitializeAsync_reuses_initialized_shell_state_without_refetching_bootstrap()
    {
        var client = new FakeChummerClient();
        var shellState = ShellState.Empty with
        {
            Commands = FakeChummerClient.Commands,
            NavigationTabs = FakeChummerClient.Tabs,
            OpenWorkspaces =
            [
                new ShellWorkspaceState(
                    Id: new CharacterWorkspaceId("ws-shell-1"),
                    Name: "Shell Workspace",
                    Alias: "SHELL",
                    LastOpenedUtc: DateTimeOffset.UtcNow,
                    RulesetId: "sr6")
            ],
            ActiveRulesetId = "sr6",
            ActiveWorkspaceId = new CharacterWorkspaceId("ws-shell-1")
        };

        var presenter = new CharacterOverviewPresenter(
            client,
            shellPresenter: new ShellPresenterStub(shellState));

        await presenter.InitializeAsync(CancellationToken.None);

        Assert.AreEqual(0, client.GetCommandsCalls);
        Assert.AreEqual(0, client.GetNavigationTabsCalls);
        Assert.AreEqual(0, client.ListWorkspacesCalls);
        Assert.HasCount(1, presenter.State.OpenWorkspaces);
        Assert.AreEqual("ws-shell-1", presenter.State.OpenWorkspaces[0].Id.Value);
        Assert.AreEqual("sr6", presenter.State.OpenWorkspaces[0].RulesetId);
    }

    [TestMethod]
    public async Task LoadAsync_populates_profile_progress_and_skills()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);

        Assert.IsFalse(presenter.State.IsBusy);
        Assert.IsNull(presenter.State.Error);
        Assert.IsNotNull(presenter.State.Profile);
        Assert.IsNotNull(presenter.State.Progress);
        Assert.IsNotNull(presenter.State.Skills);
        Assert.IsNotNull(presenter.State.Rules);
        Assert.IsNotNull(presenter.State.Build);
        Assert.IsNotNull(presenter.State.Movement);
        Assert.IsNotNull(presenter.State.Awakening);
        Assert.AreEqual("ws-1", presenter.State.WorkspaceId?.Value);
        Assert.AreEqual("BLUE", presenter.State.Profile.Alias);
    }

    [TestMethod]
    public async Task ImportAsync_loads_workspace_and_sections()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.ImportAsync(
            new WorkspaceImportDocument("<character><name>Imported</name></character>", RulesetDefaults.Sr5, WorkspaceDocumentFormat.NativeXml),
            CancellationToken.None);

        Assert.IsFalse(presenter.State.IsBusy);
        Assert.IsNull(presenter.State.Error);
        Assert.AreEqual("ws-1", presenter.State.WorkspaceId?.Value);
        Assert.IsNotNull(presenter.State.Profile);
        Assert.IsNotNull(presenter.State.Progress);
        Assert.IsNotNull(presenter.State.Skills);
        Assert.IsNotNull(presenter.State.Rules);
        Assert.IsNotNull(presenter.State.Build);
        Assert.IsNotNull(presenter.State.Movement);
        Assert.IsNotNull(presenter.State.Awakening);
    }

    [TestMethod]
    public async Task ImportAsync_resolves_ruleset_from_bootstrap_when_document_seed_is_blank()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.ImportAsync(
            new WorkspaceImportDocument("<character><name>Imported</name></character>", string.Empty, WorkspaceDocumentFormat.NativeXml),
            CancellationToken.None);

        Assert.IsNotNull(client.LastImportedDocument);
        Assert.AreEqual(RulesetDefaults.Sr5, client.LastImportedDocument!.RulesetId);
        Assert.AreEqual("ws-1", presenter.State.WorkspaceId?.Value);
    }

    [TestMethod]
    public async Task ImportAsync_resolves_ruleset_from_document_gameedition_when_document_seed_is_blank()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.ImportAsync(
            new WorkspaceImportDocument(
                "<character><gameedition>SR6</gameedition><name>Imported</name></character>",
                string.Empty,
                WorkspaceDocumentFormat.NativeXml),
            CancellationToken.None);

        Assert.IsNotNull(client.LastImportedDocument);
        Assert.AreEqual("sr6", client.LastImportedDocument!.RulesetId);
        Assert.AreEqual("ws-1", presenter.State.WorkspaceId?.Value);
    }

    [TestMethod]
    public async Task LoadAsync_tracks_open_workspaces_for_multi_document_shell_state()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.LoadAsync(new CharacterWorkspaceId("ws-2"), CancellationToken.None);

        Assert.AreEqual("ws-2", presenter.State.WorkspaceId?.Value);
        Assert.HasCount(2, presenter.State.OpenWorkspaces);
        string[] expectedWorkspaceIds = ["ws-1", "ws-2"];
        CollectionAssert.AreEquivalent(
            expectedWorkspaceIds,
            presenter.State.OpenWorkspaces.Select(workspace => workspace.Id.Value).ToArray());
    }

    [TestMethod]
    public async Task SwitchWorkspaceAsync_restores_workspace_specific_tab_and_section_context()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);
        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.SelectTabAsync("tab-info", CancellationToken.None);

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-2"), CancellationToken.None);
        await presenter.SelectTabAsync("tab-gear", CancellationToken.None);

        await presenter.SwitchWorkspaceAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        Assert.AreEqual("ws-1", presenter.State.WorkspaceId?.Value);
        Assert.AreEqual("tab-info", presenter.State.ActiveTabId);
        Assert.AreEqual("profile", presenter.State.ActiveSectionId);

        await presenter.SwitchWorkspaceAsync(new CharacterWorkspaceId("ws-2"), CancellationToken.None);
        Assert.AreEqual("ws-2", presenter.State.WorkspaceId?.Value);
        Assert.AreEqual("tab-gear", presenter.State.ActiveTabId);
        Assert.AreEqual("gear", presenter.State.ActiveSectionId);
    }

    [TestMethod]
    public async Task SwitchWorkspaceAsync_does_not_reload_when_target_workspace_is_already_active()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);
        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        int getProfileCalls = client.GetProfileCalls;

        await presenter.SwitchWorkspaceAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);

        Assert.AreEqual(getProfileCalls, client.GetProfileCalls);
        Assert.AreEqual("ws-1", presenter.State.WorkspaceId?.Value);
        Assert.AreEqual("Workspace 'ws-1' is already active.", presenter.State.Notice);
    }

    [TestMethod]
    public async Task CloseWorkspaceAsync_closes_active_workspace_and_switches_to_recent_workspace()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.LoadAsync(new CharacterWorkspaceId("ws-2"), CancellationToken.None);
        await presenter.CloseWorkspaceAsync(new CharacterWorkspaceId("ws-2"), CancellationToken.None);

        Assert.AreEqual("ws-1", presenter.State.WorkspaceId?.Value);
        Assert.AreEqual("ws-1", presenter.State.Session.ActiveWorkspaceId?.Value);
        Assert.HasCount(1, presenter.State.Session.OpenWorkspaces);
        Assert.AreEqual("ws-1", presenter.State.Session.OpenWorkspaces[0].Id.Value);
    }

    [TestMethod]
    public async Task CloseWorkspaceAsync_handles_remote_close_errors_and_keeps_local_shell_consistent()
    {
        var client = new FakeChummerClient
        {
            ThrowOnCloseWorkspace = true
        };
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.LoadAsync(new CharacterWorkspaceId("ws-2"), CancellationToken.None);
        await presenter.CloseWorkspaceAsync(new CharacterWorkspaceId("ws-2"), CancellationToken.None);

        Assert.AreEqual("ws-1", presenter.State.WorkspaceId?.Value);
        Assert.AreEqual("ws-1", presenter.State.Session.ActiveWorkspaceId?.Value);
        Assert.HasCount(1, presenter.State.OpenWorkspaces);
        StringAssert.Contains(presenter.State.Notice ?? string.Empty, "already closed");
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_close_window_switches_to_previous_workspace()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.LoadAsync(new CharacterWorkspaceId("ws-2"), CancellationToken.None);
        await presenter.ExecuteCommandAsync("close_window", CancellationToken.None);

        Assert.AreEqual("ws-1", presenter.State.WorkspaceId?.Value);
        Assert.HasCount(1, presenter.State.OpenWorkspaces);
        Assert.AreEqual("ws-1", presenter.State.OpenWorkspaces[0].Id.Value);
        Assert.IsTrue((presenter.State.Notice ?? string.Empty).Contains("Closed active workspace.", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task UpdateMetadataAsync_requires_loaded_workspace()
    {
        var presenter = new CharacterOverviewPresenter(new FakeChummerClient());

        await presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata("Name", "Alias", "Notes"), CancellationToken.None);

        Assert.AreEqual("No workspace loaded.", presenter.State.Error);
    }

    [TestMethod]
    public async Task UpdateMetadataAsync_updates_profile_when_client_succeeds()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata("Updated", "Alias", "Notes"), CancellationToken.None);

        Assert.IsNull(presenter.State.Error);
        Assert.AreEqual("Updated", presenter.State.Profile?.Name);
    }

    [TestMethod]
    public async Task SaveAsync_requires_loaded_workspace()
    {
        var presenter = new CharacterOverviewPresenter(new FakeChummerClient());

        await presenter.SaveAsync(CancellationToken.None);

        Assert.AreEqual("No workspace loaded.", presenter.State.Error);
    }

    [TestMethod]
    public async Task SaveAsync_marks_workspace_as_saved_after_workspace_load()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata("Updated", "Alias", "Notes"), CancellationToken.None);
        await presenter.SaveAsync(CancellationToken.None);

        Assert.IsNull(presenter.State.Error);
        Assert.IsTrue(presenter.State.HasSavedWorkspace);
    }

    [TestMethod]
    public async Task SaveAsync_syncs_shell_feedback_when_shell_presenter_is_supplied()
    {
        var client = new FakeChummerClient();
        var shellPresenter = new ShellPresenterStub(ShellState.Empty);
        var presenter = new CharacterOverviewPresenter(client, shellPresenter: shellPresenter);

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.SaveAsync(CancellationToken.None);

        Assert.IsNotNull(shellPresenter.LastOverviewFeedback);
        Assert.AreEqual("Workspace saved.", shellPresenter.LastOverviewFeedback.Notice);
        Assert.AreEqual("ws-1", shellPresenter.LastOverviewFeedback.OpenWorkspaces[0].Id.Value);
        Assert.IsTrue(shellPresenter.LastOverviewFeedback.OpenWorkspaces[0].HasSavedWorkspace);
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_save_character_marks_workspace_as_saved()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.ExecuteCommandAsync("save_character", CancellationToken.None);

        Assert.AreEqual("save_character", presenter.State.LastCommandId);
        Assert.IsTrue(presenter.State.HasSavedWorkspace);
        Assert.IsNull(presenter.State.Error);
    }

    [TestMethod]
    public async Task Save_character_as_command_prepares_download_without_marking_workspace_saved()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata("Updated", "Alias", "Notes"), CancellationToken.None);
        await presenter.ExecuteCommandAsync("save_character_as", CancellationToken.None);

        Assert.AreEqual("save_character_as", presenter.State.LastCommandId);
        Assert.AreEqual(1, client.DownloadCalls);
        Assert.IsFalse(presenter.State.HasSavedWorkspace);
        Assert.IsNull(presenter.State.Error);
        StringAssert.Contains(presenter.State.Notice ?? string.Empty, "Download prepared:");
        Assert.IsNotNull(presenter.State.PendingDownload);
        Assert.AreEqual(1L, presenter.State.PendingDownloadVersion);
        Assert.AreEqual("ws-1.chum5", presenter.State.PendingDownload?.FileName);
    }

    [TestMethod]
    public async Task SaveAsync_clears_pending_download_after_save_as()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.ExecuteCommandAsync("save_character_as", CancellationToken.None);

        Assert.IsNotNull(presenter.State.PendingDownload);

        await presenter.SaveAsync(CancellationToken.None);

        Assert.IsNull(presenter.State.PendingDownload);
        Assert.IsNull(presenter.State.Error);
    }

    [TestMethod]
    public async Task Export_character_dialog_download_prepares_json_bundle()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.ExecuteCommandAsync("export_character", CancellationToken.None);

        Assert.IsNotNull(presenter.State.ActiveDialog);

        await presenter.ExecuteDialogActionAsync("download", CancellationToken.None);

        Assert.AreEqual(1, client.ExportCalls);
        Assert.IsNull(presenter.State.ActiveDialog);
        Assert.IsNull(presenter.State.Error);
        Assert.IsNull(presenter.State.PendingDownload);
        Assert.IsNotNull(presenter.State.PendingExport);
        Assert.AreEqual(WorkspaceDocumentFormat.Json, presenter.State.PendingExport?.Format);
        StringAssert.EndsWith(presenter.State.PendingExport?.FileName ?? string.Empty, "-export.json");
        StringAssert.Contains(presenter.State.Notice ?? string.Empty, "Export prepared:");
        string payload = Encoding.UTF8.GetString(Convert.FromBase64String(presenter.State.PendingExport!.ContentBase64));
        StringAssert.Contains(payload, "\"Summary\"");
        StringAssert.Contains(payload, "\"Profile\"");
        StringAssert.Contains(payload, "\"Progress\"");
        StringAssert.Contains(payload, "\"Reaction\"");
        StringAssert.Contains(payload, "\"Fixer\"");
    }

    [TestMethod]
    public async Task Print_character_command_prepares_html_preview()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.ExecuteCommandAsync("print_character", CancellationToken.None);

        Assert.AreEqual(1, client.PrintCalls);
        Assert.IsNull(presenter.State.ActiveDialog);
        Assert.IsNull(presenter.State.Error);
        Assert.IsNull(presenter.State.PendingDownload);
        Assert.IsNull(presenter.State.PendingExport);
        Assert.IsNotNull(presenter.State.PendingPrint);
        StringAssert.EndsWith(presenter.State.PendingPrint?.FileName ?? string.Empty, "-print.html");
        Assert.AreEqual("text/html", presenter.State.PendingPrint?.MimeType);
        StringAssert.Contains(presenter.State.Notice ?? string.Empty, "Print preview prepared:");
        string payload = Encoding.UTF8.GetString(Convert.FromBase64String(presenter.State.PendingPrint!.ContentBase64));
        StringAssert.Contains(payload, "<html");
        StringAssert.Contains(payload, "Troy Simmons");
    }

    [TestMethod]
    public async Task Save_status_is_tracked_per_workspace_when_switching()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.SaveAsync(CancellationToken.None);
        await presenter.LoadAsync(new CharacterWorkspaceId("ws-2"), CancellationToken.None);

        OpenWorkspaceState ws1AfterSecondLoad = presenter.State.OpenWorkspaces
            .First(workspace => string.Equals(workspace.Id.Value, "ws-1", StringComparison.Ordinal));
        Assert.IsTrue(ws1AfterSecondLoad.HasSavedWorkspace);
        Assert.IsFalse(presenter.State.HasSavedWorkspace);

        await presenter.SwitchWorkspaceAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);

        Assert.AreEqual("ws-1", presenter.State.WorkspaceId?.Value);
        Assert.IsTrue(presenter.State.HasSavedWorkspace);
        OpenWorkspaceState active = presenter.State.OpenWorkspaces
            .First(workspace => string.Equals(workspace.Id.Value, "ws-1", StringComparison.Ordinal));
        Assert.IsTrue(active.HasSavedWorkspace);
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_unknown_command_sets_error()
    {
        var presenter = new CharacterOverviewPresenter(new FakeChummerClient());

        await presenter.ExecuteCommandAsync("nope", CancellationToken.None);

        Assert.AreEqual("nope", presenter.State.LastCommandId);
        StringAssert.Contains(presenter.State.Error ?? string.Empty, "not implemented");
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_global_settings_opens_dialog()
    {
        var presenter = new CharacterOverviewPresenter(new FakeChummerClient());

        await presenter.ExecuteCommandAsync("global_settings", CancellationToken.None);

        Assert.AreEqual("global_settings", presenter.State.LastCommandId);
        Assert.IsNotNull(presenter.State.ActiveDialog);
        Assert.AreEqual("dialog.global_settings", presenter.State.ActiveDialog?.Id);
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_switch_ruleset_opens_dialog()
    {
        var presenter = new CharacterOverviewPresenter(new FakeChummerClient());

        await presenter.ExecuteCommandAsync("switch_ruleset", CancellationToken.None);

        Assert.AreEqual("switch_ruleset", presenter.State.LastCommandId);
        Assert.IsNotNull(presenter.State.ActiveDialog);
        Assert.AreEqual("dialog.switch_ruleset", presenter.State.ActiveDialog?.Id);
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_open_character_opens_import_dialog()
    {
        var presenter = new CharacterOverviewPresenter(new FakeChummerClient());

        await presenter.ExecuteCommandAsync("open_character", CancellationToken.None);

        Assert.AreEqual("open_character", presenter.State.LastCommandId);
        Assert.IsNotNull(presenter.State.ActiveDialog);
        Assert.AreEqual("dialog.open_character", presenter.State.ActiveDialog?.Id);
    }

    [TestMethod]
    public async Task ExecuteDialogActionAsync_import_imports_workspace_from_open_character_dialog()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.ExecuteCommandAsync("open_character", CancellationToken.None);
        await presenter.UpdateDialogFieldAsync("importRulesetId", " SR6 ", CancellationToken.None);
        await presenter.UpdateDialogFieldAsync("openCharacterXml", "<character><name>Dialog Import</name></character>", CancellationToken.None);
        await presenter.ExecuteDialogActionAsync("import", CancellationToken.None);

        Assert.IsNotNull(client.LastImportedDocument);
        StringAssert.Contains(client.LastImportedDocument!.Content, "Dialog Import");
        Assert.AreEqual("sr6", client.LastImportedDocument.RulesetId);
        Assert.AreEqual("ws-1", presenter.State.WorkspaceId?.Value);
        Assert.IsNull(presenter.State.ActiveDialog);
        Assert.AreEqual("Character imported.", presenter.State.Notice);

        await presenter.ExecuteCommandAsync("open_character", CancellationToken.None);
        Assert.AreEqual("sr6", DesktopDialogFieldValueParser.GetValue(presenter.State.ActiveDialog!, "importRulesetId"));
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_open_character_prefills_import_ruleset_from_active_workspace()
    {
        var client = new FakeChummerClient();
        client.SeedWorkspace("ws-sr6", "Ruleset Six", "RS6", rulesetId: "sr6");
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);
        await presenter.LoadAsync(new CharacterWorkspaceId("ws-sr6"), CancellationToken.None);
        await presenter.ExecuteCommandAsync("open_character", CancellationToken.None);

        Assert.AreEqual("sr6", DesktopDialogFieldValueParser.GetValue(presenter.State.ActiveDialog!, "importRulesetId"));
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_open_character_prefills_import_ruleset_from_initialized_shell_contract_when_no_workspace_is_active()
    {
        var client = new FakeChummerClient();
        await client.SaveShellPreferencesAsync(new ShellPreferences("sr6"), CancellationToken.None);
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);
        await presenter.ExecuteCommandAsync("open_character", CancellationToken.None);

        Assert.AreEqual("sr6", DesktopDialogFieldValueParser.GetValue(presenter.State.ActiveDialog!, "importRulesetId"));
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_switch_ruleset_prefills_ruleset_from_active_workspace()
    {
        var client = new FakeChummerClient();
        client.SeedWorkspace("ws-sr6", "Ruleset Six", "RS6", rulesetId: "sr6");
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);
        await presenter.LoadAsync(new CharacterWorkspaceId("ws-sr6"), CancellationToken.None);
        await presenter.ExecuteCommandAsync("switch_ruleset", CancellationToken.None);

        Assert.AreEqual("sr6", DesktopDialogFieldValueParser.GetValue(presenter.State.ActiveDialog!, "preferredRulesetId"));
    }

    [TestMethod]
    public async Task ExecuteDialogActionAsync_apply_ruleset_calls_shell_presenter_and_closes_dialog()
    {
        var client = new FakeChummerClient();
        var shellPresenter = new ShellPresenterStub(ShellState.Empty with
        {
            Commands = AppCommandCatalog.All,
            NavigationTabs = NavigationTabCatalog.All
        });
        var presenter = new CharacterOverviewPresenter(
            client,
            shellPresenter: shellPresenter);

        await presenter.ExecuteCommandAsync("switch_ruleset", CancellationToken.None);
        await presenter.UpdateDialogFieldAsync("preferredRulesetId", " SR6 ", CancellationToken.None);
        await presenter.ExecuteDialogActionAsync("apply_ruleset", CancellationToken.None);

        Assert.AreEqual("sr6", shellPresenter.LastPreferredRulesetId);
        Assert.IsNull(presenter.State.ActiveDialog);
        Assert.AreEqual("Preferred ruleset set to 'sr6'.", presenter.State.Notice);
    }

    [TestMethod]
    public async Task HandleUiControlAsync_create_entry_opens_dialog()
    {
        var presenter = new CharacterOverviewPresenter(new FakeChummerClient());

        await presenter.HandleUiControlAsync("create_entry", CancellationToken.None);

        Assert.IsNotNull(presenter.State.ActiveDialog);
        Assert.AreEqual("dialog.ui.create_entry", presenter.State.ActiveDialog?.Id);
    }

    [TestMethod]
    public async Task HandleUiControlAsync_all_catalog_controls_are_non_generic()
    {
        var presenter = new CharacterOverviewPresenter(new FakeChummerClient());

        foreach (string controlId in LegacyUiControlIds)
        {
            await presenter.HandleUiControlAsync(controlId, CancellationToken.None);
            Assert.AreNotEqual("dialog.ui.generic", presenter.State.ActiveDialog?.Id, $"Control '{controlId}' fell back to generic dialog.");
        }
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_all_catalog_commands_are_handled()
    {
        AppCommandDefinition[] commands = AppCommandCatalog.All
            .Where(command => !string.Equals(command.Group, "menu", StringComparison.Ordinal))
            .ToArray();

        foreach (AppCommandDefinition command in commands)
        {
            var presenter = new CharacterOverviewPresenter(new FakeChummerClient());
            await presenter.InitializeAsync(CancellationToken.None);
            if (command.RequiresOpenCharacter)
            {
                await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
            }

            await presenter.ExecuteCommandAsync(command.Id, CancellationToken.None);

            string error = presenter.State.Error ?? string.Empty;
            Assert.IsFalse(
                error.Contains("not implemented", StringComparison.OrdinalIgnoreCase),
                $"Command '{command.Id}' fell through to not-implemented: {error}");
        }
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_dialog_commands_use_non_generic_dialog_templates()
    {
        string[] dialogCommands =
        [
            OverviewCommandPolicy.RuntimeInspectorCommandId,
            "new_window",
            "wiki",
            "discord",
            "revision_history",
            "dumpshock",
            "print_setup",
            "print_multiple",
            "dice_roller",
            "global_settings",
            "switch_ruleset",
            "character_settings",
            "translator",
            "xml_editor",
            "master_index",
            "character_roster",
            "data_exporter",
            "export_character",
            "report_bug",
            "about",
            "hero_lab_importer",
            "update"
        ];

        foreach (string commandId in dialogCommands)
        {
            var presenter = new CharacterOverviewPresenter(new FakeChummerClient());
            await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
            await presenter.ExecuteCommandAsync(commandId, CancellationToken.None);

            Assert.IsNotNull(presenter.State.ActiveDialog, $"Command '{commandId}' did not open a dialog.");
            Assert.AreNotEqual("dialog.generic", presenter.State.ActiveDialog?.Id, $"Command '{commandId}' fell back to generic dialog template.");
        }
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_runtime_inspector_uses_runtime_projection_dialog()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);
        await presenter.ExecuteCommandAsync(OverviewCommandPolicy.RuntimeInspectorCommandId, CancellationToken.None);

        Assert.IsNotNull(presenter.State.ActiveDialog);
        Assert.AreEqual("dialog.runtime_inspector", presenter.State.ActiveDialog?.Id);
        Assert.AreEqual("official.sr5.core", DesktopDialogFieldValueParser.GetValue(presenter.State.ActiveDialog!, "runtimeProfileId"));
        Assert.AreEqual("sha256:sr5-runtime-fingerprint", DesktopDialogFieldValueParser.GetValue(presenter.State.ActiveDialog!, "runtimeFingerprint"));
        StringAssert.Contains(DesktopDialogFieldValueParser.GetValue(presenter.State.ActiveDialog!, "runtimeRulePacks"), "official.sr5.core");
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_runtime_inspector_errors_when_no_active_runtime_exists()
    {
        var client = new FakeChummerClient
        {
            DisableActiveRuntime = true
        };
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);
        await presenter.ExecuteCommandAsync(OverviewCommandPolicy.RuntimeInspectorCommandId, CancellationToken.None);

        Assert.IsNull(presenter.State.ActiveDialog);
        Assert.AreEqual("No active runtime profile is available for inspection.", presenter.State.Error);
    }

    [TestMethod]
    public async Task ExecuteWorkspaceActionAsync_summary_sets_active_summary_payload()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);
        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
            .First(item => string.Equals(item.Id, "tab-info.summary", StringComparison.Ordinal));

        await presenter.ExecuteWorkspaceActionAsync(action, CancellationToken.None);

        Assert.AreEqual("summary", presenter.State.ActiveSectionId);
        Assert.AreEqual("tab-info.summary", presenter.State.ActiveActionId);
        StringAssert.Contains(presenter.State.ActiveSectionJson ?? string.Empty, "\"Name\": \"Troy Simmons\"");
        Assert.IsGreaterThan(0, presenter.State.ActiveSectionRows.Count);
    }

    [TestMethod]
    public async Task ExecuteWorkspaceActionAsync_metadata_applies_profile_updates_from_dialog()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);
        WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
            .First(item => string.Equals(item.Id, "tab-info.metadata", StringComparison.Ordinal));

        await presenter.InitializeAsync(CancellationToken.None);
        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
        await presenter.UpdateDialogFieldAsync("metadataName", "Dialog Updated", CancellationToken.None);
        await presenter.UpdateDialogFieldAsync("metadataAlias", "Dialog Alias", CancellationToken.None);
        await presenter.ExecuteDialogActionAsync("apply_metadata", CancellationToken.None);

        Assert.IsNull(presenter.State.ActiveDialog);
        Assert.AreEqual("Dialog Updated", presenter.State.Profile?.Name);
        Assert.AreEqual("Dialog Alias", presenter.State.Profile?.Alias);
    }

    [TestMethod]
    public async Task ExecuteWorkspaceActionAsync_metadata_blank_notes_are_treated_as_no_change()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);
        WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
            .First(item => string.Equals(item.Id, "tab-info.metadata", StringComparison.Ordinal));

        await presenter.InitializeAsync(CancellationToken.None);
        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata(null, null, "Existing Notes"), CancellationToken.None);
        await presenter.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
        await presenter.UpdateDialogFieldAsync("metadataNotes", string.Empty, CancellationToken.None);
        await presenter.ExecuteDialogActionAsync("apply_metadata", CancellationToken.None);

        Assert.IsNotNull(client.LastUpdateMetadata);
        Assert.IsNull(client.LastUpdateMetadata!.Notes);
        Assert.AreEqual("Existing Notes", presenter.State.Preferences.CharacterNotes);
    }

    [TestMethod]
    public async Task UpdateMetadataAsync_updates_preference_notes_when_notes_are_provided()
    {
        var presenter = new CharacterOverviewPresenter(new FakeChummerClient());

        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata(null, null, "Desk Notes"), CancellationToken.None);

        Assert.AreEqual("Desk Notes", presenter.State.Preferences.CharacterNotes);
    }

    [TestMethod]
    public async Task ExecuteDialogActionAsync_roll_updates_dice_dialog_result_field()
    {
        var presenter = new CharacterOverviewPresenter(new FakeChummerClient());

        await presenter.ExecuteCommandAsync("dice_roller", CancellationToken.None);
        await presenter.UpdateDialogFieldAsync("diceExpression", "3d6+2", CancellationToken.None);
        await presenter.ExecuteDialogActionAsync("roll", CancellationToken.None);

        Assert.IsNotNull(presenter.State.ActiveDialog);
        Assert.IsNotNull(presenter.State.ActiveDialog?.Fields.FirstOrDefault(field => string.Equals(field.Id, "diceResult", StringComparison.Ordinal)));
        StringAssert.Contains(presenter.State.Notice ?? string.Empty, "3d6+2");
    }

    [TestMethod]
    public async Task ExecuteDialogActionAsync_save_global_settings_updates_preferences()
    {
        var presenter = new CharacterOverviewPresenter(new FakeChummerClient());

        await presenter.ExecuteCommandAsync("global_settings", CancellationToken.None);
        await presenter.UpdateDialogFieldAsync("globalUiScale", "125", CancellationToken.None);
        await presenter.UpdateDialogFieldAsync("globalTheme", "dark-steel", CancellationToken.None);
        await presenter.UpdateDialogFieldAsync("globalLanguage", "de-de", CancellationToken.None);
        await presenter.UpdateDialogFieldAsync("globalCompactMode", "true", CancellationToken.None);
        await presenter.ExecuteDialogActionAsync("save", CancellationToken.None);

        Assert.AreEqual(125, presenter.State.Preferences.UiScalePercent);
        Assert.AreEqual("dark-steel", presenter.State.Preferences.Theme);
        Assert.AreEqual("de-de", presenter.State.Preferences.Language);
        Assert.IsTrue(presenter.State.Preferences.CompactMode);
        Assert.IsNull(presenter.State.ActiveDialog);
    }

    [TestMethod]
    public async Task SelectTabAsync_requires_loaded_workspace()
    {
        var presenter = new CharacterOverviewPresenter(new FakeChummerClient());

        await presenter.SelectTabAsync("tab-info", CancellationToken.None);

        Assert.AreEqual("No workspace loaded.", presenter.State.Error);
    }

    [TestMethod]
    public async Task SelectTabAsync_loads_active_section_preview_after_workspace_load()
    {
        var client = new FakeChummerClient();
        var presenter = new CharacterOverviewPresenter(client);

        await presenter.InitializeAsync(CancellationToken.None);
        await presenter.LoadAsync(new CharacterWorkspaceId("ws-1"), CancellationToken.None);
        await presenter.SelectTabAsync("tab-info", CancellationToken.None);

        Assert.AreEqual("tab-info", presenter.State.ActiveTabId);
        Assert.AreEqual("profile", presenter.State.ActiveSectionId);
        StringAssert.Contains(presenter.State.ActiveSectionJson ?? string.Empty, "\"sectionId\": \"profile\"");
        Assert.IsGreaterThan(0, presenter.State.ActiveSectionRows.Count);
    }

    private sealed class FakeChummerClient : IChummerClient
    {
        private string _name = "Troy Simmons";
        private string _alias = "BLUE";
        private readonly Dictionary<string, WorkspaceListItem> _workspaces = new(StringComparer.Ordinal);
        private readonly Dictionary<(string ProfileId, string RulesetId), RuntimeInspectorProjection> _runtimeInspectors = new();
        private int _clock;
        private ShellPreferences _preferences = new(RulesetDefaults.Sr5);
        private ShellSessionState _session = ShellSessionState.Default;
        public bool DisableActiveRuntime { get; set; }
        public bool ThrowOnCloseWorkspace { get; set; }
        public int DownloadCalls { get; private set; }
        public int GetCommandsCalls { get; private set; }
        public int GetNavigationTabsCalls { get; private set; }
        public int ListWorkspacesCalls { get; private set; }
        public int GetProfileCalls { get; private set; }
        public int ExportCalls { get; private set; }
        public int PrintCalls { get; private set; }
        public UpdateWorkspaceMetadata? LastUpdateMetadata { get; private set; }
        public WorkspaceImportDocument? LastImportedDocument { get; private set; }
        public static IReadOnlyList<AppCommandDefinition> Commands { get; } = CreateCommands(RulesetDefaults.Sr5);
        public static IReadOnlyList<NavigationTabDefinition> Tabs { get; } = CreateTabs(RulesetDefaults.Sr5);

        public FakeChummerClient()
        {
            SeedRuntimeInspector("official.sr5.core", RulesetDefaults.Sr5);
            SeedRuntimeInspector("official.sr6.core", RulesetDefaults.Sr6);
        }

        public Task<ShellPreferences> GetShellPreferencesAsync(CancellationToken ct)
        {
            return Task.FromResult(_preferences);
        }

        public Task SaveShellPreferencesAsync(ShellPreferences preferences, CancellationToken ct)
        {
            _preferences = new ShellPreferences(
                PreferredRulesetId: RulesetDefaults.NormalizeOptional(preferences.PreferredRulesetId) ?? string.Empty);
            return Task.CompletedTask;
        }

        public Task<ShellSessionState> GetShellSessionAsync(CancellationToken ct)
        {
            return Task.FromResult(_session);
        }

        public Task SaveShellSessionAsync(ShellSessionState session, CancellationToken ct)
        {
            _session = new ShellSessionState(
                ActiveWorkspaceId: NormalizeWorkspaceId(session.ActiveWorkspaceId),
                ActiveTabId: NormalizeTabId(session.ActiveTabId),
                ActiveTabsByWorkspace: NormalizeWorkspaceTabMap(session.ActiveTabsByWorkspace));
            return Task.CompletedTask;
        }

        public async Task<ShellBootstrapSnapshot> GetShellBootstrapAsync(string? rulesetId, CancellationToken ct)
        {
            IReadOnlyList<WorkspaceListItem> workspaces = await ListWorkspacesAsync(ct);
            CharacterWorkspaceId? activeWorkspaceId = ResolveActiveWorkspaceId(workspaces, _session.ActiveWorkspaceId);
            string preferredRulesetId = RulesetDefaults.NormalizeOptional(_preferences.PreferredRulesetId) ?? string.Empty;
            string activeRulesetId = activeWorkspaceId is null
                ? preferredRulesetId
                : RulesetDefaults.NormalizeOptional(
                    workspaces.First(workspace => string.Equals(workspace.Id.Value, activeWorkspaceId.Value.Value, StringComparison.Ordinal)).RulesetId) ?? string.Empty;
            string effectiveRulesetId = string.IsNullOrWhiteSpace(rulesetId)
                ? activeRulesetId
                : RulesetDefaults.NormalizeRequired(rulesetId);
            IReadOnlyList<AppCommandDefinition> commands = await GetCommandsAsync(effectiveRulesetId, ct);
            IReadOnlyList<NavigationTabDefinition> tabs = await GetNavigationTabsAsync(effectiveRulesetId, ct);
            return new ShellBootstrapSnapshot(
                RulesetId: effectiveRulesetId,
                Commands: commands,
                NavigationTabs: tabs,
                Workspaces: workspaces,
                PreferredRulesetId: preferredRulesetId,
                ActiveRulesetId: activeRulesetId,
                ActiveWorkspaceId: activeWorkspaceId,
                ActiveTabId: NormalizeTabId(_session.ActiveTabId),
                ActiveTabsByWorkspace: NormalizeWorkspaceTabMap(_session.ActiveTabsByWorkspace),
                ActiveRuntime: DisableActiveRuntime ? null : CreateActiveRuntime(effectiveRulesetId));
        }

        public Task<RuntimeInspectorProjection?> GetRuntimeInspectorProfileAsync(string profileId, string? rulesetId, CancellationToken ct)
        {
            string normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId)
                ?? RulesetDefaults.NormalizeRequired(_preferences.PreferredRulesetId);
            _runtimeInspectors.TryGetValue((profileId, normalizedRulesetId), out RuntimeInspectorProjection? projection);
            return Task.FromResult(projection);
        }

        public void SeedWorkspace(
            string workspaceId,
            string name,
            string alias,
            DateTimeOffset? lastUpdatedUtc = null,
            string? rulesetId = null)
        {
            string resolvedRulesetId = _workspaces.TryGetValue(workspaceId, out WorkspaceListItem? existingWorkspace)
                ? RulesetDefaults.NormalizeOptional(rulesetId ?? existingWorkspace.RulesetId) ?? string.Empty
                : RulesetDefaults.NormalizeOptional(rulesetId) ?? string.Empty;
            CharacterFileSummary summary = new(
                Name: name,
                Alias: alias,
                Metatype: "Human",
                BuildMethod: "Priority",
                CreatedVersion: "1.0",
                AppVersion: "1.0",
                Karma: 0m,
                Nuyen: 0m,
                Created: true);
            DateTimeOffset timestamp = lastUpdatedUtc ?? DateTimeOffset.UtcNow.AddMinutes(++_clock);
            _workspaces[workspaceId] = new WorkspaceListItem(
                new CharacterWorkspaceId(workspaceId),
                summary,
                timestamp,
                resolvedRulesetId);
        }

        public Task<WorkspaceImportResult> ImportAsync(WorkspaceImportDocument document, CancellationToken ct)
        {
            LastImportedDocument = document;
            SeedWorkspace("ws-1", "Imported", _alias);
            WorkspaceImportResult result = new(
                new CharacterWorkspaceId("ws-1"),
                new CharacterFileSummary(
                    Name: "Imported",
                    Alias: _alias,
                    Metatype: "Ork",
                    BuildMethod: "SumtoTen",
                    CreatedVersion: "1.0",
                    AppVersion: "1.0",
                    Karma: 0m,
                    Nuyen: 0m,
                    Created: true),
                RulesetDefaults.NormalizeOptional(document.RulesetId) ?? string.Empty);

            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<WorkspaceListItem>> ListWorkspacesAsync(CancellationToken ct)
        {
            ListWorkspacesCalls++;
            IReadOnlyList<WorkspaceListItem> workspaces = _workspaces.Values
                .OrderByDescending(workspace => workspace.LastUpdatedUtc)
                .ToArray();
            return Task.FromResult(workspaces);
        }

        public Task<bool> CloseWorkspaceAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            if (ThrowOnCloseWorkspace)
            {
                throw new InvalidOperationException("Simulated close failure.");
            }

            bool removed = _workspaces.Remove(id.Value);
            return Task.FromResult(removed);
        }

        public Task<IReadOnlyList<AppCommandDefinition>> GetCommandsAsync(string? rulesetId, CancellationToken ct)
        {
            GetCommandsCalls++;
            string effectiveRulesetId = RulesetDefaults.NormalizeOptional(rulesetId)
                ?? RulesetDefaults.NormalizeRequired(_preferences.PreferredRulesetId);
            return Task.FromResult(CreateCommands(effectiveRulesetId));
        }

        public Task<IReadOnlyList<NavigationTabDefinition>> GetNavigationTabsAsync(string? rulesetId, CancellationToken ct)
        {
            GetNavigationTabsCalls++;
            string effectiveRulesetId = RulesetDefaults.NormalizeOptional(rulesetId)
                ?? RulesetDefaults.NormalizeRequired(_preferences.PreferredRulesetId);
            return Task.FromResult(CreateTabs(effectiveRulesetId));
        }

        private void SeedRuntimeInspector(string profileId, string rulesetId)
        {
            _runtimeInspectors[(profileId, rulesetId)] = CreateRuntimeInspectorProjection(profileId, rulesetId);
        }

        private static ActiveRuntimeStatusProjection CreateActiveRuntime(string rulesetId)
        {
            string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);
            return new ActiveRuntimeStatusProjection(
                ProfileId: $"official.{normalizedRulesetId}.core",
                Title: normalizedRulesetId == RulesetDefaults.Sr6 ? "SR6 Core" : "SR5 Core",
                RulesetId: normalizedRulesetId,
                RuntimeFingerprint: $"sha256:{normalizedRulesetId}-runtime-fingerprint",
                InstallState: ArtifactInstallStates.Available,
                RulePackCount: 1,
                ProviderBindingCount: 1,
                WarningCount: 0);
        }

        private static RuntimeInspectorProjection CreateRuntimeInspectorProjection(string profileId, string rulesetId)
        {
            string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);
            return new RuntimeInspectorProjection(
                TargetKind: RuntimeInspectorTargetKinds.RuntimeLock,
                TargetId: profileId,
                RuntimeLock: new ResolvedRuntimeLock(
                    RulesetId: normalizedRulesetId,
                    ContentBundles:
                    [
                        new ContentBundleDescriptor(
                            BundleId: $"{normalizedRulesetId}.core.bundle",
                            RulesetId: normalizedRulesetId,
                            Version: "1.0.0",
                            Title: "Core Bundle",
                            Description: "Default bundle",
                            AssetPaths: ["data/core.xml"])
                    ],
                    RulePacks:
                    [
                        new ArtifactVersionReference($"official.{normalizedRulesetId}.core", "1.0.0")
                    ],
                    ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [RulePackCapabilityIds.DeriveStat] = $"official.{normalizedRulesetId}.core/derive.stat"
                    },
                    EngineApiVersion: "1.0.0",
                    RuntimeFingerprint: $"sha256:{normalizedRulesetId}-runtime-fingerprint"),
                Install: new ArtifactInstallState(
                    State: ArtifactInstallStates.Available,
                    RuntimeFingerprint: $"sha256:{normalizedRulesetId}-runtime-fingerprint"),
                ResolvedRulePacks:
                [
                    new RuntimeInspectorRulePackEntry(
                        new ArtifactVersionReference($"official.{normalizedRulesetId}.core", "1.0.0"),
                        normalizedRulesetId == RulesetDefaults.Sr6 ? "SR6 Core" : "SR5 Core",
                        ArtifactVisibilityModes.LocalOnly,
                        ArtifactTrustTiers.Official,
                        [RulePackCapabilityIds.DeriveStat])
                ],
                ProviderBindings:
                [
                    new RuntimeInspectorProviderBinding(
                        CapabilityId: RulePackCapabilityIds.DeriveStat,
                        ProviderId: $"official.{normalizedRulesetId}.core/derive.stat",
                        PackId: $"official.{normalizedRulesetId}.core")
                ],
                CompatibilityDiagnostics:
                [
                    new RuntimeLockCompatibilityDiagnostic(
                        State: RuntimeLockCompatibilityStates.Compatible,
                        Message: "Runtime lock resolves against the current RuleProfile and RulePack catalog.",
                        RequiredRulesetId: normalizedRulesetId,
                        RequiredRuntimeFingerprint: $"sha256:{normalizedRulesetId}-runtime-fingerprint")
                ],
                Warnings: [],
                MigrationPreview:
                [
                    new RuntimeMigrationPreviewItem(
                        Kind: RuntimeMigrationPreviewChangeKinds.RulePackAdded,
                        Summary: $"Profile applies RulePack 'official.{normalizedRulesetId}.core@1.0.0'.",
                        SubjectId: $"official.{normalizedRulesetId}.core",
                        AfterValue: "1.0.0")
                ],
                GeneratedAtUtc: DateTimeOffset.UtcNow);
        }

        public Task<JsonNode> GetSectionAsync(CharacterWorkspaceId id, string sectionId, CancellationToken ct)
        {
            JsonObject section = new()
            {
                ["workspaceId"] = id.Value,
                ["sectionId"] = sectionId
            };

            return Task.FromResult<JsonNode>(section);
        }

        public Task<CharacterFileSummary> GetSummaryAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            return Task.FromResult(new CharacterFileSummary(
                Name: _name,
                Alias: _alias,
                Metatype: "Ork",
                BuildMethod: "SumtoTen",
                CreatedVersion: "1.0",
                AppVersion: "1.0",
                Karma: 12m,
                Nuyen: 5000m,
                Created: true));
        }

        public Task<CharacterValidationResult> ValidateAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            return Task.FromResult(new CharacterValidationResult(
                IsValid: true,
                Issues: []));
        }

        public Task<CharacterProfileSection> GetProfileAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            GetProfileCalls++;
            SeedWorkspace(id.Value, _name, _alias);
            CharacterProfileSection profile = new(
                Name: _name,
                Alias: _alias,
                PlayerName: string.Empty,
                Metatype: "Ork",
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
                BuildMethod: "SumtoTen",
                GameplayOption: string.Empty,
                Created: true,
                Adept: false,
                Magician: false,
                Technomancer: false,
                AI: false,
                MainMugshotIndex: 0,
                MugshotCount: 0);

            return Task.FromResult(profile);
        }

        public Task<CharacterProgressSection> GetProgressAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            CharacterProgressSection progress = new(
                Karma: 12m,
                Nuyen: 5000m,
                StartingNuyen: 0m,
                StreetCred: 1,
                Notoriety: 0,
                PublicAwareness: 0,
                BurntStreetCred: 0,
                BuildKarma: 0,
                TotalAttributes: 0,
                TotalSpecial: 0,
                PhysicalCmFilled: 0,
                StunCmFilled: 0,
                TotalEssence: 6m,
                InitiateGrade: 0,
                SubmersionGrade: 0,
                MagEnabled: false,
                ResEnabled: false,
                DepEnabled: false);

            return Task.FromResult(progress);
        }

        public Task<CharacterSkillsSection> GetSkillsAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            CharacterSkillsSection skills = new(
                Count: 1,
                KnowledgeCount: 0,
                Skills:
                [
                    new CharacterSkillSummary(
                        Guid: "1",
                        Suid: string.Empty,
                        Category: "Combat",
                        IsKnowledge: false,
                        BaseValue: 6,
                        KarmaValue: 0,
                        Specializations: ["Semi-Automatics"])
                ]);

            return Task.FromResult(skills);
        }

        public Task<CharacterRulesSection> GetRulesAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            CharacterRulesSection rules = new(
                GameEdition: "SR5",
                Settings: "default.xml",
                GameplayOption: "Standard",
                GameplayOptionQualityLimit: 25,
                MaxNuyen: 10,
                MaxKarma: 25,
                ContactMultiplier: 3,
                BannedWareGrades: ["Betaware"]);

            return Task.FromResult(rules);
        }

        public Task<CharacterBuildSection> GetBuildAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            CharacterBuildSection build = new(
                BuildMethod: "SumtoTen",
                PriorityMetatype: "C,2",
                PriorityAttributes: "E,0",
                PrioritySpecial: "A,4",
                PrioritySkills: "B,3",
                PriorityResources: "D,1",
                PriorityTalent: "Mundane",
                SumToTen: 10,
                Special: 1,
                TotalSpecial: 4,
                TotalAttributes: 20,
                ContactPoints: 15,
                ContactPointsUsed: 8);

            return Task.FromResult(build);
        }

        public Task<CharacterMovementSection> GetMovementAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            CharacterMovementSection movement = new(
                Walk: "2/1/0",
                Run: "4/0/0",
                Sprint: "2/1/0",
                WalkAlt: "2/1/0",
                RunAlt: "4/0/0",
                SprintAlt: "2/1/0",
                PhysicalCmFilled: 0,
                StunCmFilled: 0);

            return Task.FromResult(movement);
        }

        public Task<CharacterAwakeningSection> GetAwakeningAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            CharacterAwakeningSection awakening = new(
                MagEnabled: false,
                ResEnabled: false,
                DepEnabled: false,
                Adept: false,
                Magician: false,
                Technomancer: false,
                AI: false,
                InitiateGrade: 0,
                SubmersionGrade: 0,
                Tradition: string.Empty,
                TraditionName: string.Empty,
                TraditionDrain: string.Empty,
                SpiritCombat: string.Empty,
                SpiritDetection: string.Empty,
                SpiritHealth: string.Empty,
                SpiritIllusion: string.Empty,
                SpiritManipulation: string.Empty,
                Stream: string.Empty,
                StreamDrain: string.Empty,
                CurrentCounterspellingDice: 0,
                SpellLimit: 0,
                CfpLimit: 0,
                AiNormalProgramLimit: 0,
                AiAdvancedProgramLimit: 0);

            return Task.FromResult(awakening);
        }

        public Task<CommandResult<CharacterProfileSection>> UpdateMetadataAsync(CharacterWorkspaceId id, UpdateWorkspaceMetadata command, CancellationToken ct)
        {
            LastUpdateMetadata = command;
            _name = command.Name ?? _name;
            _alias = command.Alias ?? _alias;
            SeedWorkspace(id.Value, _name, _alias);

            CharacterProfileSection updated = new(
                Name: _name,
                Alias: _alias,
                PlayerName: string.Empty,
                Metatype: "Ork",
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
                BuildMethod: "SumtoTen",
                GameplayOption: string.Empty,
                Created: true,
                Adept: false,
                Magician: false,
                Technomancer: false,
                AI: false,
                MainMugshotIndex: 0,
                MugshotCount: 0);

            return Task.FromResult(new CommandResult<CharacterProfileSection>(
                Success: true,
                Value: updated,
                Error: null));
        }

        public Task<CommandResult<WorkspaceSaveReceipt>> SaveAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            SeedWorkspace(id.Value, _name, _alias);
            return Task.FromResult(new CommandResult<WorkspaceSaveReceipt>(
                Success: true,
                Value: new WorkspaceSaveReceipt(
                    Id: id,
                    DocumentLength: 64,
                    RulesetId: RulesetDefaults.Sr5),
                Error: null));
        }

        public Task<CommandResult<WorkspaceDownloadReceipt>> DownloadAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            DownloadCalls++;
            SeedWorkspace(id.Value, _name, _alias);
            return Task.FromResult(new CommandResult<WorkspaceDownloadReceipt>(
                Success: true,
                Value: new WorkspaceDownloadReceipt(
                    Id: id,
                    Format: WorkspaceDocumentFormat.NativeXml,
                    ContentBase64: Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("<character><name>Download</name></character>")),
                    FileName: $"{id.Value}.chum5",
                    DocumentLength: 41,
                    RulesetId: RulesetDefaults.Sr5),
                Error: null));
        }

        public Task<CommandResult<WorkspaceExportReceipt>> ExportAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            ExportCalls++;
            SeedWorkspace(id.Value, _name, _alias);

            DataExportBundle bundle = new(
                Summary: new CharacterFileSummary(
                    Name: _name,
                    Alias: _alias,
                    Metatype: "Ork",
                    BuildMethod: "SumtoTen",
                    CreatedVersion: "1.0",
                    AppVersion: "1.0",
                    Karma: 12m,
                    Nuyen: 5000m,
                    Created: true),
                Profile: new CharacterProfileSection(
                    Name: _name,
                    Alias: _alias,
                    PlayerName: string.Empty,
                    Metatype: "Ork",
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
                    BuildMethod: "SumtoTen",
                    GameplayOption: string.Empty,
                    Created: true,
                    Adept: false,
                    Magician: false,
                    Technomancer: false,
                    AI: false,
                    MainMugshotIndex: 0,
                    MugshotCount: 0),
                Progress: new CharacterProgressSection(
                    Karma: 12m,
                    Nuyen: 5000m,
                    StartingNuyen: 0m,
                    StreetCred: 1,
                    Notoriety: 0,
                    PublicAwareness: 0,
                    BurntStreetCred: 0,
                    BuildKarma: 0,
                    TotalAttributes: 0,
                    TotalSpecial: 0,
                    PhysicalCmFilled: 0,
                    StunCmFilled: 0,
                    TotalEssence: 6m,
                    InitiateGrade: 0,
                    SubmersionGrade: 0,
                    MagEnabled: false,
                    ResEnabled: false,
                    DepEnabled: false),
                Attributes: new CharacterAttributesSection(
                    Count: 1,
                    Attributes:
                    [
                        new CharacterAttributeSummary("Reaction", 5, 7)
                    ]),
                Skills: new CharacterSkillsSection(
                    Count: 1,
                    KnowledgeCount: 0,
                    Skills:
                    [
                        new CharacterSkillSummary("1", string.Empty, "Combat", false, 6, 0, ["Semi-Automatics"])
                    ]),
                Inventory: new CharacterInventorySection(
                    GearCount: 1,
                    WeaponCount: 0,
                    ArmorCount: 0,
                    CyberwareCount: 0,
                    VehicleCount: 0,
                    GearNames: ["Medkit"],
                    WeaponNames: [],
                    ArmorNames: [],
                    CyberwareNames: [],
                    VehicleNames: []),
                Qualities: new CharacterQualitiesSection(
                    Count: 1,
                    Qualities:
                    [
                        new CharacterQualitySummary("First Impression", "Core", 11)
                    ]),
                Contacts: new CharacterContactsSection(
                    Count: 1,
                    Contacts:
                    [
                        new CharacterContactSummary("Fixer", "Broker", "Seattle", 4, 3)
                    ]));

            string json = JsonSerializer.Serialize(bundle);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(json);
            return Task.FromResult(new CommandResult<WorkspaceExportReceipt>(
                Success: true,
                Value: new WorkspaceExportReceipt(
                    Id: id,
                    Format: WorkspaceDocumentFormat.Json,
                    ContentBase64: Convert.ToBase64String(payloadBytes),
                    FileName: $"{id.Value}-export.json",
                    DocumentLength: payloadBytes.Length,
                    RulesetId: RulesetDefaults.Sr5),
                Error: null));
        }

        public Task<CommandResult<WorkspacePrintReceipt>> PrintAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            PrintCalls++;
            SeedWorkspace(id.Value, _name, _alias);

            string html = $"<!DOCTYPE html><html><head><title>{_name}</title></head><body><h1>{_name}</h1><p>{_alias}</p></body></html>";
            byte[] payloadBytes = Encoding.UTF8.GetBytes(html);
            return Task.FromResult(new CommandResult<WorkspacePrintReceipt>(
                Success: true,
                Value: new WorkspacePrintReceipt(
                    Id: id,
                    ContentBase64: Convert.ToBase64String(payloadBytes),
                    FileName: $"{id.Value}-print.html",
                    MimeType: "text/html",
                    DocumentLength: payloadBytes.Length,
                    Title: _name,
                    RulesetId: RulesetDefaults.Sr5),
                Error: null));
        }

        private static string? NormalizeWorkspaceId(string? workspaceId)
        {
            return string.IsNullOrWhiteSpace(workspaceId)
                ? null
                : workspaceId.Trim();
        }

        private static IReadOnlyList<AppCommandDefinition> CreateCommands(string rulesetId)
        {
            return
            [
                new("new_character", "command.new_character", "file", false, true, rulesetId),
                new("save_character", "command.save_character", "file", true, true, rulesetId)
            ];
        }

        private static IReadOnlyList<NavigationTabDefinition> CreateTabs(string rulesetId)
        {
            return
            [
                new("tab-info", "Info", "profile", "character", true, true, rulesetId),
                new("tab-gear", "Gear", "gear", "character", true, true, rulesetId)
            ];
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
            IEnumerable<WorkspaceListItem> workspaces,
            string? preferredWorkspaceId)
        {
            WorkspaceListItem[] workspaceList = workspaces as WorkspaceListItem[] ?? workspaces.ToArray();
            if (string.IsNullOrWhiteSpace(preferredWorkspaceId))
            {
                return null;
            }

            WorkspaceListItem? matchingWorkspace = workspaceList.FirstOrDefault(workspace =>
                string.Equals(workspace.Id.Value, preferredWorkspaceId, StringComparison.Ordinal));
            return matchingWorkspace?.Id;
        }
    }

    private sealed class ShellPresenterStub : IShellPresenter
    {
        public ShellPresenterStub(ShellState state)
        {
            State = state;
        }

        public ShellState State { get; private set; }
        public string? LastPreferredRulesetId { get; private set; }
        public ShellOverviewFeedback? LastOverviewFeedback { get; private set; }

        public event EventHandler? StateChanged
        {
            add { }
            remove { }
        }

        public Task InitializeAsync(CancellationToken ct)
        {
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
            LastPreferredRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);
            State = State with
            {
                PreferredRulesetId = LastPreferredRulesetId,
                ActiveRulesetId = LastPreferredRulesetId
            };
            return Task.CompletedTask;
        }

        public Task SyncWorkspaceContextAsync(CharacterWorkspaceId? activeWorkspaceId, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void SyncOverviewFeedback(ShellOverviewFeedback feedback)
        {
            LastOverviewFeedback = feedback;
        }
    }
}
