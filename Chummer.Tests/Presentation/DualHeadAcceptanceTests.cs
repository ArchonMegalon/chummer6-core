#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Avalonia;
using Chummer.Blazor;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public class DualHeadAcceptanceTests
{
    private static readonly Uri BaseUri = ResolveBaseUri();
    private static readonly string? ApiKey = ResolveApiKey();
    private static readonly RulesetShellCatalogResolverService ShellCatalogResolver =
        CreateShellCatalogResolver();
    private static readonly Regex WorkspaceTokenRegex = new("(?<=Workspace:\\s)[A-Za-z0-9-]+", RegexOptions.Compiled);
    private static readonly Regex WorkspaceFileNameRegex = new("^[a-f0-9]{32}(?:-[a-f0-9]{4}){0,4}\\.(?:chum5|json)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WorkspaceFileTokenRegex = new("[a-f0-9]{32}(?:-[a-f0-9]{4}){0,4}\\.(?:chum5|json)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static RulesetShellCatalogResolverService CreateShellCatalogResolver()
    {
        RulesetPluginRegistry registry = new(
        [
            new Sr5RulesetPlugin(),
            new Sr6RulesetPlugin()
        ]);
        return new RulesetShellCatalogResolverService(registry, new DefaultRulesetSelectionPolicy(registry));
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_overview_flows_show_equivalent_state_after_import()
    {
        string xml = File.ReadAllText(FindTestFilePath("Barrett.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);

        CharacterOverviewState avaloniaState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);
            avaloniaState = adapter.State;
        }

        CharacterOverviewState blazorState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);
            blazorState = callbackState.WorkspaceId is null ? bridge.Current : callbackState;
        }

        Assert.IsNotNull(avaloniaState.WorkspaceId);
        Assert.IsNotNull(blazorState.WorkspaceId);
        Assert.AreEqual(avaloniaState.Profile?.Name, blazorState.Profile?.Name);
        Assert.AreEqual(avaloniaState.Profile?.Alias, blazorState.Profile?.Alias);
        Assert.AreEqual(avaloniaState.Progress?.Karma, blazorState.Progress?.Karma);
        Assert.AreEqual(avaloniaState.Skills?.Count, blazorState.Skills?.Count);
        Assert.AreEqual(avaloniaState.Rules?.GameEdition, blazorState.Rules?.GameEdition);
        Assert.AreEqual("Moa", avaloniaState.Profile?.Name);
        Assert.AreEqual("Barrett", avaloniaState.Profile?.Alias);
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_metadata_save_roundtrip_match()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);
        UpdateWorkspaceMetadata update = new("Updated Name", "Updated Alias", "Updated Notes");

        CharacterOverviewState avaloniaState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);
            await presenter.UpdateMetadataAsync(update, CancellationToken.None);
            await presenter.SaveAsync(CancellationToken.None);
            avaloniaState = adapter.State;
        }

        CharacterOverviewState blazorState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);
            await presenter.UpdateMetadataAsync(update, CancellationToken.None);
            await presenter.SaveAsync(CancellationToken.None);
            blazorState = callbackState.WorkspaceId is null ? bridge.Current : callbackState;
        }

        Assert.AreEqual("Updated Name", avaloniaState.Profile?.Name);
        Assert.AreEqual("Updated Alias", avaloniaState.Profile?.Alias);
        Assert.AreEqual("Updated Name", blazorState.Profile?.Name);
        Assert.AreEqual("Updated Alias", blazorState.Profile?.Alias);
        Assert.IsTrue(avaloniaState.HasSavedWorkspace);
        Assert.IsTrue(blazorState.HasSavedWorkspace);
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_tab_selection_loads_same_workspace_section()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);

        CharacterOverviewState avaloniaState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.InitializeAsync(CancellationToken.None);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);
            await adapter.SelectTabAsync("tab-skills", CancellationToken.None);
            avaloniaState = adapter.State;
        }

        CharacterOverviewState blazorState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            await bridge.InitializeAsync(CancellationToken.None);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);
            await bridge.SelectTabAsync("tab-skills", CancellationToken.None);
            blazorState = callbackState.WorkspaceId is null ? bridge.Current : callbackState;
        }

        Assert.AreEqual("tab-skills", avaloniaState.ActiveTabId);
        Assert.AreEqual("tab-skills", blazorState.ActiveTabId);
        Assert.AreEqual("skills", avaloniaState.ActiveSectionId);
        Assert.AreEqual("skills", blazorState.ActiveSectionId);
        Assert.AreEqual(avaloniaState.ActiveSectionJson, blazorState.ActiveSectionJson);
        Assert.HasCount(avaloniaState.ActiveSectionRows.Count, blazorState.ActiveSectionRows);
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_command_dispatch_save_character_matches()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);

        CharacterOverviewState avaloniaState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);
            await adapter.ExecuteCommandAsync("save_character", CancellationToken.None);
            avaloniaState = adapter.State;
        }

        CharacterOverviewState blazorState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);
            await bridge.ExecuteCommandAsync("save_character", CancellationToken.None);
            blazorState = callbackState.WorkspaceId is null ? bridge.Current : callbackState;
        }

        Assert.AreEqual("save_character", avaloniaState.LastCommandId);
        Assert.AreEqual("save_character", blazorState.LastCommandId);
        Assert.IsTrue(avaloniaState.HasSavedWorkspace);
        Assert.IsTrue(blazorState.HasSavedWorkspace);
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_command_dialog_dispatch_matches()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);

        CharacterOverviewState avaloniaState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);
            await adapter.ExecuteCommandAsync("global_settings", CancellationToken.None);
            avaloniaState = adapter.State;
        }

        CharacterOverviewState blazorState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);
            await bridge.ExecuteCommandAsync("global_settings", CancellationToken.None);
            blazorState = callbackState.WorkspaceId is null ? bridge.Current : callbackState;
        }

        Assert.AreEqual("global_settings", avaloniaState.LastCommandId);
        Assert.AreEqual("global_settings", blazorState.LastCommandId);
        Assert.IsNotNull(avaloniaState.ActiveDialog);
        Assert.IsNotNull(blazorState.ActiveDialog);
        Assert.AreEqual(avaloniaState.ActiveDialog?.Id, blazorState.ActiveDialog?.Id);
        Assert.AreEqual("Global Settings", avaloniaState.ActiveDialog?.Title);
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_dialog_field_updates_match()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);

        CharacterOverviewState avaloniaState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);
            await adapter.ExecuteCommandAsync("global_settings", CancellationToken.None);
            await adapter.UpdateDialogFieldAsync("globalUiScale", "125", CancellationToken.None);
            avaloniaState = adapter.State;
        }

        CharacterOverviewState blazorState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);
            await bridge.ExecuteCommandAsync("global_settings", CancellationToken.None);
            await bridge.UpdateDialogFieldAsync("globalUiScale", "125", CancellationToken.None);
            blazorState = callbackState.WorkspaceId is null ? bridge.Current : callbackState;
        }

        string? avaloniaUiScale = avaloniaState.ActiveDialog?.Fields.FirstOrDefault(field => string.Equals(field.Id, "globalUiScale", StringComparison.Ordinal)).Value;
        string? blazorUiScale = blazorState.ActiveDialog?.Fields.FirstOrDefault(field => string.Equals(field.Id, "globalUiScale", StringComparison.Ordinal)).Value;
        Assert.AreEqual("125", avaloniaUiScale);
        Assert.AreEqual("125", blazorUiScale);
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_global_settings_save_updates_shared_preferences()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);

        CharacterOverviewState avaloniaState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);
            await adapter.ExecuteCommandAsync("global_settings", CancellationToken.None);
            await adapter.UpdateDialogFieldAsync("globalUiScale", "120", CancellationToken.None);
            await adapter.UpdateDialogFieldAsync("globalTheme", "steel", CancellationToken.None);
            await adapter.ExecuteDialogActionAsync("save", CancellationToken.None);
            avaloniaState = adapter.State;
        }

        CharacterOverviewState blazorState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);
            await bridge.ExecuteCommandAsync("global_settings", CancellationToken.None);
            await bridge.UpdateDialogFieldAsync("globalUiScale", "120", CancellationToken.None);
            await bridge.UpdateDialogFieldAsync("globalTheme", "steel", CancellationToken.None);
            await bridge.ExecuteDialogActionAsync("save", CancellationToken.None);
            blazorState = callbackState.WorkspaceId is null ? bridge.Current : callbackState;
        }

        Assert.AreEqual(120, avaloniaState.Preferences.UiScalePercent);
        Assert.AreEqual(120, blazorState.Preferences.UiScalePercent);
        Assert.AreEqual("steel", avaloniaState.Preferences.Theme);
        Assert.AreEqual("steel", blazorState.Preferences.Theme);
        Assert.IsNull(avaloniaState.ActiveDialog);
        Assert.IsNull(blazorState.ActiveDialog);
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_dialog_workflow_keeps_shell_regions_in_parity()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);
        DefaultCommandAvailabilityEvaluator evaluator = new();

        ShellRegionSnapshot avaloniaBeforeDialog;
        ShellRegionSnapshot avaloniaDialogOpen;
        ShellRegionSnapshot avaloniaAfterDialogSave;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.InitializeAsync(CancellationToken.None);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);
            await adapter.SelectTabAsync("tab-info", CancellationToken.None);
            avaloniaBeforeDialog = BuildShellRegionSnapshot(adapter.State, evaluator);

            await adapter.ExecuteCommandAsync("global_settings", CancellationToken.None);
            avaloniaDialogOpen = BuildShellRegionSnapshot(adapter.State, evaluator);

            await adapter.UpdateDialogFieldAsync("globalTheme", "mint", CancellationToken.None);
            await adapter.UpdateDialogFieldAsync("globalUiScale", "130", CancellationToken.None);
            await adapter.ExecuteDialogActionAsync("save", CancellationToken.None);
            avaloniaAfterDialogSave = BuildShellRegionSnapshot(adapter.State, evaluator);
        }

        ShellRegionSnapshot blazorBeforeDialog;
        ShellRegionSnapshot blazorDialogOpen;
        ShellRegionSnapshot blazorAfterDialogSave;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            CharacterOverviewState Snapshot() => callbackState.WorkspaceId is null ? bridge.Current : callbackState;

            await bridge.InitializeAsync(CancellationToken.None);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);
            await bridge.SelectTabAsync("tab-info", CancellationToken.None);
            blazorBeforeDialog = BuildShellRegionSnapshot(Snapshot(), evaluator);

            await bridge.ExecuteCommandAsync("global_settings", CancellationToken.None);
            blazorDialogOpen = BuildShellRegionSnapshot(Snapshot(), evaluator);

            await bridge.UpdateDialogFieldAsync("globalTheme", "mint", CancellationToken.None);
            await bridge.UpdateDialogFieldAsync("globalUiScale", "130", CancellationToken.None);
            await bridge.ExecuteDialogActionAsync("save", CancellationToken.None);
            blazorAfterDialogSave = BuildShellRegionSnapshot(Snapshot(), evaluator);
        }

        AssertShellRegionsEqual(avaloniaBeforeDialog, blazorBeforeDialog, "before-dialog");
        AssertShellRegionsEqual(avaloniaDialogOpen, blazorDialogOpen, "dialog-open");
        AssertShellRegionsEqual(avaloniaAfterDialogSave, blazorAfterDialogSave, "after-dialog-save");

        Assert.IsGreaterThanOrEqualTo(1, avaloniaBeforeDialog.OpenWorkspaceCount);
        Assert.AreEqual(avaloniaBeforeDialog.OpenWorkspaceCount, avaloniaDialogOpen.OpenWorkspaceCount);
        Assert.AreEqual(avaloniaDialogOpen.OpenWorkspaceCount, avaloniaAfterDialogSave.OpenWorkspaceCount);
        Assert.IsGreaterThanOrEqualTo(1, blazorBeforeDialog.OpenWorkspaceCount);
        Assert.AreEqual(blazorBeforeDialog.OpenWorkspaceCount, blazorDialogOpen.OpenWorkspaceCount);
        Assert.AreEqual(blazorDialogOpen.OpenWorkspaceCount, blazorAfterDialogSave.OpenWorkspaceCount);

        Assert.AreEqual("dialog.global_settings", avaloniaDialogOpen.DialogId);
        Assert.AreEqual("dialog.global_settings", blazorDialogOpen.DialogId);
        Assert.AreEqual("Global Settings", avaloniaDialogOpen.DialogTitle);
        Assert.AreEqual("Global Settings", blazorDialogOpen.DialogTitle);
        Assert.IsNull(avaloniaAfterDialogSave.DialogId);
        Assert.IsNull(blazorAfterDialogSave.DialogId);
        Assert.AreEqual("mint", avaloniaAfterDialogSave.Theme);
        Assert.AreEqual("mint", blazorAfterDialogSave.Theme);
        Assert.AreEqual(130, avaloniaAfterDialogSave.UiScalePercent);
        Assert.AreEqual(130, blazorAfterDialogSave.UiScalePercent);
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_workspace_action_summary_matches()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);
        WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
            .First(item => string.Equals(item.Id, "tab-info.summary", StringComparison.Ordinal));

        CharacterOverviewState avaloniaState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.InitializeAsync(CancellationToken.None);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);
            await adapter.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
            avaloniaState = adapter.State;
        }

        CharacterOverviewState blazorState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            await bridge.InitializeAsync(CancellationToken.None);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);
            await bridge.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
            blazorState = callbackState.WorkspaceId is null ? bridge.Current : callbackState;
        }

        Assert.AreEqual("summary", avaloniaState.ActiveSectionId);
        Assert.AreEqual("summary", blazorState.ActiveSectionId);
        Assert.AreEqual("tab-info.summary", avaloniaState.ActiveActionId);
        Assert.AreEqual("tab-info.summary", blazorState.ActiveActionId);

        using JsonDocument avaloniaJson = JsonDocument.Parse(avaloniaState.ActiveSectionJson ?? "{}");
        using JsonDocument blazorJson = JsonDocument.Parse(blazorState.ActiveSectionJson ?? "{}");

        JsonElement avaloniaRoot = avaloniaJson.RootElement;
        JsonElement blazorRoot = blazorJson.RootElement;

        Assert.AreEqual(GetString(avaloniaRoot, "Name"), GetString(blazorRoot, "Name"));
        Assert.AreEqual(GetString(avaloniaRoot, "Alias"), GetString(blazorRoot, "Alias"));
        Assert.AreEqual(GetString(avaloniaRoot, "Metatype"), GetString(blazorRoot, "Metatype"));
        Assert.AreEqual(GetString(avaloniaRoot, "BuildMethod"), GetString(blazorRoot, "BuildMethod"));
        Assert.AreEqual(GetDecimal(avaloniaRoot, "Karma"), GetDecimal(blazorRoot, "Karma"));
        Assert.AreEqual(GetDecimal(avaloniaRoot, "Nuyen"), GetDecimal(blazorRoot, "Nuyen"));
        Assert.HasCount(avaloniaState.ActiveSectionRows.Count, blazorState.ActiveSectionRows);
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_info_family_workspace_actions_render_matching_sections()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);
        string[] actionIds =
        [
            "tab-info.profile",
            "tab-info.progress",
            "tab-info.rules",
            "tab-info.build",
            "tab-info.movement",
            "tab-info.awakening"
        ];

        var expectedSections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tab-info.profile"] = "profile",
            ["tab-info.progress"] = "progress",
            ["tab-info.rules"] = "rules",
            ["tab-info.build"] = "build",
            ["tab-info.movement"] = "movement",
            ["tab-info.awakening"] = "awakening"
        };

        var avaloniaSnapshots = new Dictionary<string, (string? ActionId, string? SectionId, string? Json, int RowCount)>(StringComparer.Ordinal);
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.InitializeAsync(CancellationToken.None);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);

            foreach (string actionId in actionIds)
            {
                WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
                    .First(item => string.Equals(item.Id, actionId, StringComparison.Ordinal));
                await adapter.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
                CharacterOverviewState state = adapter.State;
                avaloniaSnapshots[actionId] = (state.ActiveActionId, state.ActiveSectionId, state.ActiveSectionJson, state.ActiveSectionRows.Count);
            }
        }

        var blazorSnapshots = new Dictionary<string, (string? ActionId, string? SectionId, string? Json, int RowCount)>(StringComparer.Ordinal);
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            CharacterOverviewState Snapshot() => callbackState.WorkspaceId is null ? bridge.Current : callbackState;

            await bridge.InitializeAsync(CancellationToken.None);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);

            foreach (string actionId in actionIds)
            {
                WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
                    .First(item => string.Equals(item.Id, actionId, StringComparison.Ordinal));
                await bridge.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
                CharacterOverviewState state = Snapshot();
                blazorSnapshots[actionId] = (state.ActiveActionId, state.ActiveSectionId, state.ActiveSectionJson, state.ActiveSectionRows.Count);
            }
        }

        foreach (string actionId in actionIds)
        {
            Assert.IsTrue(avaloniaSnapshots.TryGetValue(actionId, out var avalonia), $"Missing Avalonia snapshot for action '{actionId}'.");
            Assert.IsTrue(blazorSnapshots.TryGetValue(actionId, out var blazor), $"Missing Blazor snapshot for action '{actionId}'.");

            Assert.AreEqual(actionId, avalonia.ActionId);
            Assert.AreEqual(actionId, blazor.ActionId);
            Assert.AreEqual(expectedSections[actionId], avalonia.SectionId);
            Assert.AreEqual(expectedSections[actionId], blazor.SectionId);
            Assert.AreEqual(avalonia.Json, blazor.Json);
            Assert.IsGreaterThan(0, avalonia.RowCount);
            Assert.IsGreaterThan(0, blazor.RowCount);
            Assert.AreEqual(avalonia.RowCount, blazor.RowCount);
        }
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_attributes_and_skills_workspace_actions_render_matching_sections()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);
        string[] actionIds =
        [
            "tab-attributes.attributes",
            "tab-attributes.attributedetails",
            "tab-skills.skills"
        ];

        var expectedSections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tab-attributes.attributes"] = "attributes",
            ["tab-attributes.attributedetails"] = "attributedetails",
            ["tab-skills.skills"] = "skills"
        };

        var avaloniaSnapshots = new Dictionary<string, (string? ActionId, string? SectionId, string? Json, int RowCount)>(StringComparer.Ordinal);
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.InitializeAsync(CancellationToken.None);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);

            foreach (string actionId in actionIds)
            {
                WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
                    .First(item => string.Equals(item.Id, actionId, StringComparison.Ordinal));
                await adapter.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
                CharacterOverviewState state = adapter.State;
                avaloniaSnapshots[actionId] = (state.ActiveActionId, state.ActiveSectionId, state.ActiveSectionJson, state.ActiveSectionRows.Count);
            }
        }

        var blazorSnapshots = new Dictionary<string, (string? ActionId, string? SectionId, string? Json, int RowCount)>(StringComparer.Ordinal);
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            CharacterOverviewState Snapshot() => callbackState.WorkspaceId is null ? bridge.Current : callbackState;

            await bridge.InitializeAsync(CancellationToken.None);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);

            foreach (string actionId in actionIds)
            {
                WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
                    .First(item => string.Equals(item.Id, actionId, StringComparison.Ordinal));
                await bridge.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
                CharacterOverviewState state = Snapshot();
                blazorSnapshots[actionId] = (state.ActiveActionId, state.ActiveSectionId, state.ActiveSectionJson, state.ActiveSectionRows.Count);
            }
        }

        foreach (string actionId in actionIds)
        {
            Assert.IsTrue(avaloniaSnapshots.TryGetValue(actionId, out var avalonia), $"Missing Avalonia snapshot for action '{actionId}'.");
            Assert.IsTrue(blazorSnapshots.TryGetValue(actionId, out var blazor), $"Missing Blazor snapshot for action '{actionId}'.");

            Assert.AreEqual(actionId, avalonia.ActionId);
            Assert.AreEqual(actionId, blazor.ActionId);
            Assert.AreEqual(expectedSections[actionId], avalonia.SectionId);
            Assert.AreEqual(expectedSections[actionId], blazor.SectionId);
            Assert.AreEqual(avalonia.Json, blazor.Json);
            Assert.AreEqual(avalonia.RowCount, blazor.RowCount);
        }
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_gear_family_workspace_actions_render_matching_sections()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);
        string[] actionIds =
        [
            "tab-gear.inventory",
            "tab-gear.gear",
            "tab-gear.weapons",
            "tab-gear.armors",
            "tab-gear.vehicles"
        ];

        var expectedSections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tab-gear.inventory"] = "inventory",
            ["tab-gear.gear"] = "gear",
            ["tab-gear.weapons"] = "weapons",
            ["tab-gear.armors"] = "armors",
            ["tab-gear.vehicles"] = "vehicles"
        };

        var avaloniaSnapshots = new Dictionary<string, (string? ActionId, string? SectionId, string? Json, int RowCount)>(StringComparer.Ordinal);
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.InitializeAsync(CancellationToken.None);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);

            foreach (string actionId in actionIds)
            {
                WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
                    .First(item => string.Equals(item.Id, actionId, StringComparison.Ordinal));
                await adapter.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
                CharacterOverviewState state = adapter.State;
                avaloniaSnapshots[actionId] = (state.ActiveActionId, state.ActiveSectionId, state.ActiveSectionJson, state.ActiveSectionRows.Count);
            }
        }

        var blazorSnapshots = new Dictionary<string, (string? ActionId, string? SectionId, string? Json, int RowCount)>(StringComparer.Ordinal);
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            CharacterOverviewState Snapshot() => callbackState.WorkspaceId is null ? bridge.Current : callbackState;

            await bridge.InitializeAsync(CancellationToken.None);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);

            foreach (string actionId in actionIds)
            {
                WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
                    .First(item => string.Equals(item.Id, actionId, StringComparison.Ordinal));
                await bridge.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
                CharacterOverviewState state = Snapshot();
                blazorSnapshots[actionId] = (state.ActiveActionId, state.ActiveSectionId, state.ActiveSectionJson, state.ActiveSectionRows.Count);
            }
        }

        foreach (string actionId in actionIds)
        {
            Assert.IsTrue(avaloniaSnapshots.TryGetValue(actionId, out var avalonia), $"Missing Avalonia snapshot for action '{actionId}'.");
            Assert.IsTrue(blazorSnapshots.TryGetValue(actionId, out var blazor), $"Missing Blazor snapshot for action '{actionId}'.");

            Assert.AreEqual(actionId, avalonia.ActionId);
            Assert.AreEqual(actionId, blazor.ActionId);
            Assert.AreEqual(expectedSections[actionId], avalonia.SectionId);
            Assert.AreEqual(expectedSections[actionId], blazor.SectionId);
            Assert.AreEqual(avalonia.Json, blazor.Json);
            Assert.AreEqual(avalonia.RowCount, blazor.RowCount);
        }
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_magic_family_workspace_actions_render_matching_sections()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);
        string[] actionIds =
        [
            "tab-magician.spirits",
            "tab-magician.metamagics",
            "tab-adept.powers",
            "tab-technomancer.complexforms",
            "tab-technomancer.aiprograms"
        ];

        var expectedSections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tab-magician.spirits"] = "spirits",
            ["tab-magician.metamagics"] = "metamagics",
            ["tab-adept.powers"] = "powers",
            ["tab-technomancer.complexforms"] = "complexforms",
            ["tab-technomancer.aiprograms"] = "aiprograms"
        };

        var avaloniaSnapshots = new Dictionary<string, (string? ActionId, string? SectionId, string? Json, int RowCount)>(StringComparer.Ordinal);
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.InitializeAsync(CancellationToken.None);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);

            foreach (string actionId in actionIds)
            {
                WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
                    .First(item => string.Equals(item.Id, actionId, StringComparison.Ordinal));
                await adapter.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
                CharacterOverviewState state = adapter.State;
                avaloniaSnapshots[actionId] = (state.ActiveActionId, state.ActiveSectionId, state.ActiveSectionJson, state.ActiveSectionRows.Count);
            }
        }

        var blazorSnapshots = new Dictionary<string, (string? ActionId, string? SectionId, string? Json, int RowCount)>(StringComparer.Ordinal);
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            CharacterOverviewState Snapshot() => callbackState.WorkspaceId is null ? bridge.Current : callbackState;

            await bridge.InitializeAsync(CancellationToken.None);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);

            foreach (string actionId in actionIds)
            {
                WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
                    .First(item => string.Equals(item.Id, actionId, StringComparison.Ordinal));
                await bridge.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
                CharacterOverviewState state = Snapshot();
                blazorSnapshots[actionId] = (state.ActiveActionId, state.ActiveSectionId, state.ActiveSectionJson, state.ActiveSectionRows.Count);
            }
        }

        foreach (string actionId in actionIds)
        {
            Assert.IsTrue(avaloniaSnapshots.TryGetValue(actionId, out var avalonia), $"Missing Avalonia snapshot for action '{actionId}'.");
            Assert.IsTrue(blazorSnapshots.TryGetValue(actionId, out var blazor), $"Missing Blazor snapshot for action '{actionId}'.");

            Assert.AreEqual(actionId, avalonia.ActionId);
            Assert.AreEqual(actionId, blazor.ActionId);
            Assert.AreEqual(expectedSections[actionId], avalonia.SectionId);
            Assert.AreEqual(expectedSections[actionId], blazor.SectionId);
            Assert.AreEqual(avalonia.Json, blazor.Json);
            Assert.AreEqual(avalonia.RowCount, blazor.RowCount);
        }
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_support_family_workspace_actions_render_matching_sections()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);
        string[] actionIds =
        [
            "tab-lifestyle.lifestyles",
            "tab-contacts.contacts",
            "tab-calendar.calendar",
            "tab-improvements.improvements"
        ];

        var expectedSections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tab-lifestyle.lifestyles"] = "lifestyles",
            ["tab-contacts.contacts"] = "contacts",
            ["tab-calendar.calendar"] = "calendar",
            ["tab-improvements.improvements"] = "improvements"
        };

        var avaloniaSnapshots = new Dictionary<string, (string? ActionId, string? SectionId, string? Json, int RowCount)>(StringComparer.Ordinal);
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.InitializeAsync(CancellationToken.None);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);

            foreach (string actionId in actionIds)
            {
                WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
                    .First(item => string.Equals(item.Id, actionId, StringComparison.Ordinal));
                await adapter.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
                CharacterOverviewState state = adapter.State;
                avaloniaSnapshots[actionId] = (state.ActiveActionId, state.ActiveSectionId, state.ActiveSectionJson, state.ActiveSectionRows.Count);
            }
        }

        var blazorSnapshots = new Dictionary<string, (string? ActionId, string? SectionId, string? Json, int RowCount)>(StringComparer.Ordinal);
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            CharacterOverviewState Snapshot() => callbackState.WorkspaceId is null ? bridge.Current : callbackState;

            await bridge.InitializeAsync(CancellationToken.None);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);

            foreach (string actionId in actionIds)
            {
                WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
                    .First(item => string.Equals(item.Id, actionId, StringComparison.Ordinal));
                await bridge.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
                CharacterOverviewState state = Snapshot();
                blazorSnapshots[actionId] = (state.ActiveActionId, state.ActiveSectionId, state.ActiveSectionJson, state.ActiveSectionRows.Count);
            }
        }

        foreach (string actionId in actionIds)
        {
            Assert.IsTrue(avaloniaSnapshots.TryGetValue(actionId, out var avalonia), $"Missing Avalonia snapshot for action '{actionId}'.");
            Assert.IsTrue(blazorSnapshots.TryGetValue(actionId, out var blazor), $"Missing Blazor snapshot for action '{actionId}'.");

            Assert.AreEqual(actionId, avalonia.ActionId);
            Assert.AreEqual(actionId, blazor.ActionId);
            Assert.AreEqual(expectedSections[actionId], avalonia.SectionId);
            Assert.AreEqual(expectedSections[actionId], blazor.SectionId);
            Assert.AreEqual(avalonia.Json, blazor.Json);
            Assert.AreEqual(avalonia.RowCount, blazor.RowCount);
        }
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_combat_and_cyberware_workspace_actions_render_matching_sections()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);
        string[] actionIds =
        [
            "tab-combat.weapons",
            "tab-combat.armors",
            "tab-combat.drugs",
            "tab-armor.armormods",
            "tab-cyberware.cyberwares"
        ];

        var expectedSections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tab-combat.weapons"] = "weapons",
            ["tab-combat.armors"] = "armors",
            ["tab-combat.drugs"] = "drugs",
            ["tab-armor.armormods"] = "armormods",
            ["tab-cyberware.cyberwares"] = "cyberwares"
        };

        var avaloniaSnapshots = new Dictionary<string, (string? ActionId, string? SectionId, string? Json, int RowCount)>(StringComparer.Ordinal);
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.InitializeAsync(CancellationToken.None);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);

            foreach (string actionId in actionIds)
            {
                WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
                    .First(item => string.Equals(item.Id, actionId, StringComparison.Ordinal));
                await adapter.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
                CharacterOverviewState state = adapter.State;
                avaloniaSnapshots[actionId] = (state.ActiveActionId, state.ActiveSectionId, state.ActiveSectionJson, state.ActiveSectionRows.Count);
            }
        }

        var blazorSnapshots = new Dictionary<string, (string? ActionId, string? SectionId, string? Json, int RowCount)>(StringComparer.Ordinal);
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            CharacterOverviewState Snapshot() => callbackState.WorkspaceId is null ? bridge.Current : callbackState;

            await bridge.InitializeAsync(CancellationToken.None);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);

            foreach (string actionId in actionIds)
            {
                WorkspaceSurfaceActionDefinition action = WorkspaceSurfaceActionCatalog.All
                    .First(item => string.Equals(item.Id, actionId, StringComparison.Ordinal));
                await bridge.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
                CharacterOverviewState state = Snapshot();
                blazorSnapshots[actionId] = (state.ActiveActionId, state.ActiveSectionId, state.ActiveSectionJson, state.ActiveSectionRows.Count);
            }
        }

        foreach (string actionId in actionIds)
        {
            Assert.IsTrue(avaloniaSnapshots.TryGetValue(actionId, out var avalonia), $"Missing Avalonia snapshot for action '{actionId}'.");
            Assert.IsTrue(blazorSnapshots.TryGetValue(actionId, out var blazor), $"Missing Blazor snapshot for action '{actionId}'.");

            Assert.AreEqual(actionId, avalonia.ActionId);
            Assert.AreEqual(actionId, blazor.ActionId);
            Assert.AreEqual(expectedSections[actionId], avalonia.SectionId);
            Assert.AreEqual(expectedSections[actionId], blazor.SectionId);
            Assert.AreEqual(avalonia.Json, blazor.Json);
            Assert.AreEqual(avalonia.RowCount, blazor.RowCount);
        }
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_all_workspace_section_actions_render_matching_sections()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);
        WorkspaceSurfaceActionDefinition[] actions = WorkspaceSurfaceActionCatalog.All
            .Where(action => action.Kind == WorkspaceSurfaceActionKind.Section)
            .ToArray();

        Dictionary<string, WorkspaceActionSnapshot> avaloniaSnapshots = await CaptureAvaloniaWorkspaceActionSnapshotsAsync(documentBytes, actions);
        Dictionary<string, WorkspaceActionSnapshot> blazorSnapshots = await CaptureBlazorWorkspaceActionSnapshotsAsync(documentBytes, actions);

        foreach (WorkspaceSurfaceActionDefinition action in actions)
        {
            Assert.IsTrue(avaloniaSnapshots.TryGetValue(action.Id, out WorkspaceActionSnapshot? avalonia), $"Missing Avalonia snapshot for action '{action.Id}'.");
            Assert.IsTrue(blazorSnapshots.TryGetValue(action.Id, out WorkspaceActionSnapshot? blazor), $"Missing Blazor snapshot for action '{action.Id}'.");

            Assert.AreEqual(action.TabId, avalonia.ActiveTabId, $"Unexpected Avalonia active tab for action '{action.Id}'.");
            Assert.AreEqual(action.TabId, blazor.ActiveTabId, $"Unexpected Blazor active tab for action '{action.Id}'.");
            Assert.AreEqual(action.Id, avalonia.ActionId);
            Assert.AreEqual(action.Id, blazor.ActionId);
            Assert.AreEqual(action.TargetId, avalonia.SectionId);
            Assert.AreEqual(action.TargetId, blazor.SectionId);
            Assert.AreEqual(avalonia.Json, blazor.Json, $"Section payload mismatch for action '{action.Id}'.");
            Assert.AreEqual(avalonia.RowCount, blazor.RowCount, $"Section row count mismatch for action '{action.Id}'.");
        }
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_dialog_and_import_commands_expose_matching_dialog_contracts()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);
        string[] commandIds = AppCommandCatalog.All
            .Where(command => OverviewCommandPolicy.IsImportHintCommand(command.Id) || OverviewCommandPolicy.IsDialogCommand(command.Id))
            .Select(command => command.Id)
            .ToArray();

        Dictionary<string, CommandDialogSnapshot> avaloniaSnapshots = await CaptureAvaloniaCommandDialogSnapshotsAsync(documentBytes, commandIds);
        Dictionary<string, CommandDialogSnapshot> blazorSnapshots = await CaptureBlazorCommandDialogSnapshotsAsync(documentBytes, commandIds);

        foreach (string commandId in commandIds)
        {
            Assert.IsTrue(avaloniaSnapshots.TryGetValue(commandId, out CommandDialogSnapshot? avalonia), $"Missing Avalonia dialog snapshot for command '{commandId}'.");
            Assert.IsTrue(blazorSnapshots.TryGetValue(commandId, out CommandDialogSnapshot? blazor), $"Missing Blazor dialog snapshot for command '{commandId}'.");
            AssertCommandDialogSnapshotEqual(avalonia, blazor, commandId);
        }
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_character_settings_save_updates_shared_state()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);

        CharacterOverviewState avaloniaState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.InitializeAsync(CancellationToken.None);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);
            await adapter.ExecuteCommandAsync("character_settings", CancellationToken.None);
            await adapter.UpdateDialogFieldAsync("characterPriority", "Priority", CancellationToken.None);
            await adapter.UpdateDialogFieldAsync("characterKarmaNuyen", "5", CancellationToken.None);
            await adapter.UpdateDialogFieldAsync("characterHouseRulesEnabled", "true", CancellationToken.None);
            await adapter.UpdateDialogFieldAsync("characterNotes", "Shared parity notes", CancellationToken.None);
            await adapter.ExecuteDialogActionAsync("save", CancellationToken.None);
            avaloniaState = adapter.State;
        }

        CharacterOverviewState blazorState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            await bridge.InitializeAsync(CancellationToken.None);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);
            await bridge.ExecuteCommandAsync("character_settings", CancellationToken.None);
            await bridge.UpdateDialogFieldAsync("characterPriority", "Priority", CancellationToken.None);
            await bridge.UpdateDialogFieldAsync("characterKarmaNuyen", "5", CancellationToken.None);
            await bridge.UpdateDialogFieldAsync("characterHouseRulesEnabled", "true", CancellationToken.None);
            await bridge.UpdateDialogFieldAsync("characterNotes", "Shared parity notes", CancellationToken.None);
            await bridge.ExecuteDialogActionAsync("save", CancellationToken.None);
            blazorState = ResolveBridgeState(callbackState, bridge);
        }

        Assert.AreEqual("Priority", avaloniaState.Build?.BuildMethod);
        Assert.AreEqual("Priority", blazorState.Build?.BuildMethod);
        Assert.AreEqual(5, avaloniaState.Preferences.KarmaNuyenRatio);
        Assert.AreEqual(5, blazorState.Preferences.KarmaNuyenRatio);
        Assert.IsTrue(avaloniaState.Preferences.HouseRulesEnabled);
        Assert.IsTrue(blazorState.Preferences.HouseRulesEnabled);
        Assert.AreEqual("Shared parity notes", avaloniaState.Preferences.CharacterNotes);
        Assert.AreEqual("Shared parity notes", blazorState.Preferences.CharacterNotes);
        Assert.IsNull(avaloniaState.ActiveDialog);
        Assert.IsNull(blazorState.ActiveDialog);
        Assert.AreEqual("Character settings updated.", avaloniaState.Notice);
        Assert.AreEqual("Character settings updated.", blazorState.Notice);
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_download_export_and_print_commands_prepare_matching_receipts()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);

        PendingDownloadSnapshot avaloniaSaveAs = await CaptureAvaloniaDownloadSnapshotAsync(documentBytes, "save_character_as");
        PendingDownloadSnapshot blazorSaveAs = await CaptureBlazorDownloadSnapshotAsync(documentBytes, "save_character_as");
        AssertPendingDownloadSnapshotEqual(avaloniaSaveAs, blazorSaveAs, "save_character_as");
        Assert.AreEqual(WorkspaceDocumentFormat.NativeXml, avaloniaSaveAs.Format);

        PendingExportSnapshot avaloniaDataExporter = await CaptureAvaloniaExportSnapshotAsync(documentBytes, "data_exporter", dialogActionId: "download");
        PendingExportSnapshot blazorDataExporter = await CaptureBlazorExportSnapshotAsync(documentBytes, "data_exporter", dialogActionId: "download");
        AssertPendingExportSnapshotEqual(avaloniaDataExporter, blazorDataExporter, "data_exporter.download");
        Assert.AreEqual(WorkspaceDocumentFormat.Json, avaloniaDataExporter.Format);

        PendingExportSnapshot avaloniaExportCharacter = await CaptureAvaloniaExportSnapshotAsync(documentBytes, "export_character", dialogActionId: "download");
        PendingExportSnapshot blazorExportCharacter = await CaptureBlazorExportSnapshotAsync(documentBytes, "export_character", dialogActionId: "download");
        AssertPendingExportSnapshotEqual(avaloniaExportCharacter, blazorExportCharacter, "export_character.download");
        Assert.AreEqual(WorkspaceDocumentFormat.Json, avaloniaExportCharacter.Format);

        PendingPrintSnapshot avaloniaPrint = await CaptureAvaloniaPrintSnapshotAsync(documentBytes, "print_character");
        PendingPrintSnapshot blazorPrint = await CaptureBlazorPrintSnapshotAsync(documentBytes, "print_character");
        AssertPendingPrintSnapshotEqual(avaloniaPrint, blazorPrint, "print_character");
        Assert.AreEqual("text/html", avaloniaPrint.MimeType);
    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_shell_surfaces_expose_identical_ids()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        byte[] documentBytes = Encoding.UTF8.GetBytes(xml);
        DefaultCommandAvailabilityEvaluator evaluator = new();

        CharacterOverviewState avaloniaState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);
            await adapter.InitializeAsync(CancellationToken.None);
            await adapter.ImportAsync(documentBytes, CancellationToken.None);
            await adapter.SelectTabAsync("tab-info", CancellationToken.None);
            avaloniaState = adapter.State;
        }

        CharacterOverviewState blazorState;
        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            await bridge.InitializeAsync(CancellationToken.None);
            await bridge.ImportAsync(documentBytes, CancellationToken.None);
            await bridge.SelectTabAsync("tab-info", CancellationToken.None);
            blazorState = callbackState.WorkspaceId is null ? bridge.Current : callbackState;
        }

        string[] avaloniaCommandIds = avaloniaState.Commands
            .Where(command => evaluator.IsCommandEnabled(command, avaloniaState))
            .Select(command => command.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        string[] blazorCommandIds = blazorState.Commands
            .Where(command => evaluator.IsCommandEnabled(command, blazorState))
            .Select(command => command.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEquivalent(avaloniaCommandIds, blazorCommandIds);

        string[] avaloniaTabIds = avaloniaState.NavigationTabs
            .Where(tab => evaluator.IsNavigationTabEnabled(tab, avaloniaState))
            .Select(tab => tab.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        string[] blazorTabIds = blazorState.NavigationTabs
            .Where(tab => evaluator.IsNavigationTabEnabled(tab, blazorState))
            .Select(tab => tab.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEquivalent(avaloniaTabIds, blazorTabIds);

        string[] avaloniaActionIds = ShellCatalogResolver.ResolveWorkspaceActionsForTab(
                avaloniaState.ActiveTabId,
                ResolveActiveRulesetId(avaloniaState))
            .Where(action => evaluator.IsWorkspaceActionEnabled(action, avaloniaState))
            .Select(action => action.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        string[] blazorActionIds = ShellCatalogResolver.ResolveWorkspaceActionsForTab(
                blazorState.ActiveTabId,
                ResolveActiveRulesetId(blazorState))
            .Where(action => evaluator.IsWorkspaceActionEnabled(action, blazorState))
            .Select(action => action.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEquivalent(avaloniaActionIds, blazorActionIds);

    }

    [TestMethod]
    public async Task Avalonia_and_Blazor_two_workspace_import_switch_save_flow_matches()
    {
        byte[] firstDocument = Encoding.UTF8.GetBytes(File.ReadAllText(FindTestFilePath("Apex Predator.chum5")));
        byte[] secondDocument = Encoding.UTF8.GetBytes(File.ReadAllText(FindTestFilePath("Barrett.chum5")));
        CharacterWorkspaceId avaloniaFirstWorkspace;
        CharacterWorkspaceId avaloniaSecondWorkspace;
        CharacterOverviewState avaloniaAfterSwitchToFirst;
        CharacterOverviewState avaloniaAfterSwitchToSecond;

        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            using var adapter = new CharacterOverviewViewModelAdapter(presenter);

            await adapter.InitializeAsync(CancellationToken.None);
            await adapter.ImportAsync(firstDocument, CancellationToken.None);
            avaloniaFirstWorkspace = adapter.State.WorkspaceId!.Value;
            await adapter.SelectTabAsync("tab-skills", CancellationToken.None);
            await presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata("Avalonia One", "AV1", "Notes 1"), CancellationToken.None);
            await presenter.SaveAsync(CancellationToken.None);

            await adapter.ImportAsync(secondDocument, CancellationToken.None);
            avaloniaSecondWorkspace = adapter.State.WorkspaceId!.Value;
            await adapter.SelectTabAsync("tab-info", CancellationToken.None);
            await presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata("Avalonia Two", "AV2", "Notes 2"), CancellationToken.None);
            await presenter.SaveAsync(CancellationToken.None);

            await adapter.SwitchWorkspaceAsync(avaloniaFirstWorkspace, CancellationToken.None);
            avaloniaAfterSwitchToFirst = adapter.State;

            await adapter.SwitchWorkspaceAsync(avaloniaSecondWorkspace, CancellationToken.None);
            avaloniaAfterSwitchToSecond = adapter.State;
        }

        CharacterWorkspaceId blazorFirstWorkspace;
        CharacterWorkspaceId blazorSecondWorkspace;
        CharacterOverviewState blazorAfterSwitchToFirst;
        CharacterOverviewState blazorAfterSwitchToSecond;

        using (HttpClient http = CreateClient())
        {
            var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
            CharacterOverviewState callbackState = CharacterOverviewState.Empty;
            using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
            CharacterOverviewState Snapshot() => callbackState.WorkspaceId is null ? bridge.Current : callbackState;

            await bridge.InitializeAsync(CancellationToken.None);
            await bridge.ImportAsync(firstDocument, CancellationToken.None);
            blazorFirstWorkspace = Snapshot().WorkspaceId!.Value;
            await bridge.SelectTabAsync("tab-skills", CancellationToken.None);
            await presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata("Blazor One", "BZ1", "Notes 1"), CancellationToken.None);
            await presenter.SaveAsync(CancellationToken.None);

            await bridge.ImportAsync(secondDocument, CancellationToken.None);
            blazorSecondWorkspace = Snapshot().WorkspaceId!.Value;
            await bridge.SelectTabAsync("tab-info", CancellationToken.None);
            await presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata("Blazor Two", "BZ2", "Notes 2"), CancellationToken.None);
            await presenter.SaveAsync(CancellationToken.None);

            await bridge.SwitchWorkspaceAsync(blazorFirstWorkspace, CancellationToken.None);
            blazorAfterSwitchToFirst = Snapshot();

            await bridge.SwitchWorkspaceAsync(blazorSecondWorkspace, CancellationToken.None);
            blazorAfterSwitchToSecond = Snapshot();
        }

        Assert.AreNotEqual(avaloniaFirstWorkspace.Value, avaloniaSecondWorkspace.Value);
        Assert.AreNotEqual(blazorFirstWorkspace.Value, blazorSecondWorkspace.Value);

        Assert.IsGreaterThanOrEqualTo(2, avaloniaAfterSwitchToFirst.Session.OpenWorkspaces.Count);
        Assert.IsGreaterThanOrEqualTo(2, blazorAfterSwitchToFirst.Session.OpenWorkspaces.Count);
        CollectionAssert.IsSubsetOf(
            new[] { avaloniaFirstWorkspace.Value, avaloniaSecondWorkspace.Value },
            avaloniaAfterSwitchToFirst.Session.OpenWorkspaces.Select(workspace => workspace.Id.Value).ToArray());
        CollectionAssert.IsSubsetOf(
            new[] { blazorFirstWorkspace.Value, blazorSecondWorkspace.Value },
            blazorAfterSwitchToFirst.Session.OpenWorkspaces.Select(workspace => workspace.Id.Value).ToArray());

        Assert.AreEqual(avaloniaFirstWorkspace.Value, avaloniaAfterSwitchToFirst.WorkspaceId?.Value);
        Assert.AreEqual(blazorFirstWorkspace.Value, blazorAfterSwitchToFirst.WorkspaceId?.Value);
        Assert.AreEqual("tab-skills", avaloniaAfterSwitchToFirst.ActiveTabId);
        Assert.AreEqual("tab-skills", blazorAfterSwitchToFirst.ActiveTabId);
        Assert.AreEqual("skills", avaloniaAfterSwitchToFirst.ActiveSectionId);
        Assert.AreEqual("skills", blazorAfterSwitchToFirst.ActiveSectionId);

        Assert.AreEqual(avaloniaSecondWorkspace.Value, avaloniaAfterSwitchToSecond.WorkspaceId?.Value);
        Assert.AreEqual(blazorSecondWorkspace.Value, blazorAfterSwitchToSecond.WorkspaceId?.Value);
        Assert.AreEqual("tab-info", avaloniaAfterSwitchToSecond.ActiveTabId);
        Assert.AreEqual("tab-info", blazorAfterSwitchToSecond.ActiveTabId);
        Assert.AreEqual("profile", avaloniaAfterSwitchToSecond.ActiveSectionId);
        Assert.AreEqual("profile", blazorAfterSwitchToSecond.ActiveSectionId);
        Assert.AreEqual("Avalonia Two", avaloniaAfterSwitchToSecond.Profile?.Name);
        Assert.AreEqual("Blazor Two", blazorAfterSwitchToSecond.Profile?.Name);
    }

    private static async Task<Dictionary<string, WorkspaceActionSnapshot>> CaptureAvaloniaWorkspaceActionSnapshotsAsync(
        byte[] documentBytes,
        IReadOnlyList<WorkspaceSurfaceActionDefinition> actions)
    {
        var snapshots = new Dictionary<string, WorkspaceActionSnapshot>(StringComparer.Ordinal);
        using HttpClient http = CreateClient();
        var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
        using var adapter = new CharacterOverviewViewModelAdapter(presenter);
        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.ImportAsync(documentBytes, CancellationToken.None);

        foreach (WorkspaceSurfaceActionDefinition action in actions)
        {
            await adapter.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
            CharacterOverviewState state = adapter.State;
            snapshots[action.Id] = new WorkspaceActionSnapshot(
                state.ActiveTabId,
                state.ActiveActionId,
                state.ActiveSectionId,
                state.ActiveSectionJson,
                state.ActiveSectionRows.Count);
        }

        return snapshots;
    }

    private static async Task<Dictionary<string, WorkspaceActionSnapshot>> CaptureBlazorWorkspaceActionSnapshotsAsync(
        byte[] documentBytes,
        IReadOnlyList<WorkspaceSurfaceActionDefinition> actions)
    {
        var snapshots = new Dictionary<string, WorkspaceActionSnapshot>(StringComparer.Ordinal);
        using HttpClient http = CreateClient();
        var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
        CharacterOverviewState callbackState = CharacterOverviewState.Empty;
        using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
        await bridge.InitializeAsync(CancellationToken.None);
        await bridge.ImportAsync(documentBytes, CancellationToken.None);

        foreach (WorkspaceSurfaceActionDefinition action in actions)
        {
            await bridge.ExecuteWorkspaceActionAsync(action, CancellationToken.None);
            CharacterOverviewState state = ResolveBridgeState(callbackState, bridge);
            snapshots[action.Id] = new WorkspaceActionSnapshot(
                state.ActiveTabId,
                state.ActiveActionId,
                state.ActiveSectionId,
                state.ActiveSectionJson,
                state.ActiveSectionRows.Count);
        }

        return snapshots;
    }

    private static async Task<Dictionary<string, CommandDialogSnapshot>> CaptureAvaloniaCommandDialogSnapshotsAsync(
        byte[] documentBytes,
        IReadOnlyList<string> commandIds)
    {
        var snapshots = new Dictionary<string, CommandDialogSnapshot>(StringComparer.Ordinal);
        using HttpClient http = CreateClient();
        var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
        using var adapter = new CharacterOverviewViewModelAdapter(presenter);
        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.ImportAsync(documentBytes, CancellationToken.None);

        foreach (string commandId in commandIds)
        {
            await adapter.ExecuteCommandAsync(commandId, CancellationToken.None);
            snapshots[commandId] = TakeCommandDialogSnapshot(commandId, adapter.State);
            await adapter.CloseDialogAsync(CancellationToken.None);
        }

        return snapshots;
    }

    private static async Task<Dictionary<string, CommandDialogSnapshot>> CaptureBlazorCommandDialogSnapshotsAsync(
        byte[] documentBytes,
        IReadOnlyList<string> commandIds)
    {
        var snapshots = new Dictionary<string, CommandDialogSnapshot>(StringComparer.Ordinal);
        using HttpClient http = CreateClient();
        var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
        CharacterOverviewState callbackState = CharacterOverviewState.Empty;
        using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
        await bridge.InitializeAsync(CancellationToken.None);
        await bridge.ImportAsync(documentBytes, CancellationToken.None);

        foreach (string commandId in commandIds)
        {
            await bridge.ExecuteCommandAsync(commandId, CancellationToken.None);
            snapshots[commandId] = TakeCommandDialogSnapshot(commandId, ResolveBridgeState(callbackState, bridge));
            await bridge.CloseDialogAsync(CancellationToken.None);
        }

        return snapshots;
    }

    private static async Task<PendingDownloadSnapshot> CaptureAvaloniaDownloadSnapshotAsync(
        byte[] documentBytes,
        string commandId,
        string? dialogActionId = null)
    {
        using HttpClient http = CreateClient();
        var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
        using var adapter = new CharacterOverviewViewModelAdapter(presenter);
        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.ImportAsync(documentBytes, CancellationToken.None);
        await adapter.ExecuteCommandAsync(commandId, CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(dialogActionId))
        {
            await adapter.ExecuteDialogActionAsync(dialogActionId, CancellationToken.None);
        }

        return TakePendingDownloadSnapshot(adapter.State);
    }

    private static async Task<PendingDownloadSnapshot> CaptureBlazorDownloadSnapshotAsync(
        byte[] documentBytes,
        string commandId,
        string? dialogActionId = null)
    {
        using HttpClient http = CreateClient();
        var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
        CharacterOverviewState callbackState = CharacterOverviewState.Empty;
        using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
        await bridge.InitializeAsync(CancellationToken.None);
        await bridge.ImportAsync(documentBytes, CancellationToken.None);
        await bridge.ExecuteCommandAsync(commandId, CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(dialogActionId))
        {
            await bridge.ExecuteDialogActionAsync(dialogActionId, CancellationToken.None);
        }

        return TakePendingDownloadSnapshot(ResolveBridgeState(callbackState, bridge));
    }

    private static async Task<PendingExportSnapshot> CaptureAvaloniaExportSnapshotAsync(
        byte[] documentBytes,
        string commandId,
        string? dialogActionId = null)
    {
        using HttpClient http = CreateClient();
        var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
        using var adapter = new CharacterOverviewViewModelAdapter(presenter);
        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.ImportAsync(documentBytes, CancellationToken.None);
        await adapter.ExecuteCommandAsync(commandId, CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(dialogActionId))
        {
            await adapter.ExecuteDialogActionAsync(dialogActionId, CancellationToken.None);
        }

        return TakePendingExportSnapshot(adapter.State);
    }

    private static async Task<PendingExportSnapshot> CaptureBlazorExportSnapshotAsync(
        byte[] documentBytes,
        string commandId,
        string? dialogActionId = null)
    {
        using HttpClient http = CreateClient();
        var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
        CharacterOverviewState callbackState = CharacterOverviewState.Empty;
        using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
        await bridge.InitializeAsync(CancellationToken.None);
        await bridge.ImportAsync(documentBytes, CancellationToken.None);
        await bridge.ExecuteCommandAsync(commandId, CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(dialogActionId))
        {
            await bridge.ExecuteDialogActionAsync(dialogActionId, CancellationToken.None);
        }

        return TakePendingExportSnapshot(ResolveBridgeState(callbackState, bridge));
    }

    private static async Task<PendingPrintSnapshot> CaptureAvaloniaPrintSnapshotAsync(
        byte[] documentBytes,
        string commandId)
    {
        using HttpClient http = CreateClient();
        var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
        using var adapter = new CharacterOverviewViewModelAdapter(presenter);
        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.ImportAsync(documentBytes, CancellationToken.None);
        await adapter.ExecuteCommandAsync(commandId, CancellationToken.None);
        return TakePendingPrintSnapshot(adapter.State);
    }

    private static async Task<PendingPrintSnapshot> CaptureBlazorPrintSnapshotAsync(
        byte[] documentBytes,
        string commandId)
    {
        using HttpClient http = CreateClient();
        var presenter = new CharacterOverviewPresenter(new HttpChummerClient(http));
        CharacterOverviewState callbackState = CharacterOverviewState.Empty;
        using var bridge = new CharacterOverviewStateBridge(presenter, state => callbackState = state);
        await bridge.InitializeAsync(CancellationToken.None);
        await bridge.ImportAsync(documentBytes, CancellationToken.None);
        await bridge.ExecuteCommandAsync(commandId, CancellationToken.None);
        return TakePendingPrintSnapshot(ResolveBridgeState(callbackState, bridge));
    }

    private static CharacterOverviewState ResolveBridgeState(
        CharacterOverviewState callbackState,
        CharacterOverviewStateBridge bridge)
    {
        return callbackState.WorkspaceId is null ? bridge.Current : callbackState;
    }

    private static CommandDialogSnapshot TakeCommandDialogSnapshot(string commandId, CharacterOverviewState state)
    {
        DesktopDialogState? dialog = state.ActiveDialog;
        DialogFieldSnapshot[] fields = dialog?.Fields
            .Select(field => new DialogFieldSnapshot(
                field.Id,
                NormalizeDialogFieldValue(field.Id, field.Value),
                NormalizeDialogFieldValue(field.Id, field.Placeholder),
                field.IsReadOnly,
                field.IsMultiline,
                field.InputType))
            .ToArray() ?? Array.Empty<DialogFieldSnapshot>();
        string[] actionIds = dialog?.Actions
            .Select(action => action.Id)
            .ToArray() ?? Array.Empty<string>();

        return new CommandDialogSnapshot(
            commandId,
            state.LastCommandId,
            dialog?.Id,
            dialog?.Title,
            dialog?.Message,
            fields,
            actionIds);
    }

    private static PendingDownloadSnapshot TakePendingDownloadSnapshot(CharacterOverviewState state)
    {
        return new PendingDownloadSnapshot(
            state.LastCommandId,
            state.PendingDownload?.Format,
            NormalizeDownloadFileName(state.PendingDownload?.FileName),
            state.PendingDownload?.DocumentLength,
            state.PendingDownload?.RulesetId,
            state.PendingDownload?.ContentBase64,
            NormalizeDownloadNotice(state.Notice));
    }

    private static PendingExportSnapshot TakePendingExportSnapshot(CharacterOverviewState state)
    {
        return new PendingExportSnapshot(
            state.LastCommandId,
            state.PendingExport?.Format,
            NormalizeDownloadFileName(state.PendingExport?.FileName),
            state.PendingExport?.DocumentLength,
            state.PendingExport?.RulesetId,
            state.PendingExport?.ContentBase64,
            NormalizeDownloadNotice(state.Notice));
    }

    private static PendingPrintSnapshot TakePendingPrintSnapshot(CharacterOverviewState state)
    {
        return new PendingPrintSnapshot(
            state.LastCommandId,
            NormalizeDownloadFileName(state.PendingPrint?.FileName),
            state.PendingPrint?.DocumentLength,
            state.PendingPrint?.RulesetId,
            state.PendingPrint?.ContentBase64,
            state.PendingPrint?.MimeType,
            state.PendingPrint?.Title,
            NormalizeDownloadNotice(state.Notice));
    }

    private static string NormalizeDialogFieldValue(string fieldId, string value)
    {
        if (string.Equals(fieldId, "workspace", StringComparison.Ordinal))
            return "<workspace>";

        if (string.Equals(fieldId, "dataExportPreview", StringComparison.Ordinal))
            return WorkspaceTokenRegex.Replace(value, "<workspace>");

        return value;
    }

    private static string? NormalizeDownloadFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return fileName;

        return WorkspaceFileNameRegex.IsMatch(fileName)
            ? Regex.Replace(fileName, "^[^.]+", "<workspace>")
            : fileName;
    }

    private static string? NormalizeDownloadNotice(string? notice)
    {
        if (string.IsNullOrWhiteSpace(notice))
            return notice;

        return WorkspaceFileTokenRegex.Replace(notice, match => NormalizeDownloadFileName(match.Value) ?? match.Value);
    }

    private static void AssertCommandDialogSnapshotEqual(
        CommandDialogSnapshot avalonia,
        CommandDialogSnapshot blazor,
        string commandId)
    {
        Assert.AreEqual(commandId, avalonia.CommandId);
        Assert.AreEqual(commandId, blazor.CommandId);
        Assert.AreEqual(avalonia.LastCommandId, blazor.LastCommandId, $"Last command mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.DialogId, blazor.DialogId, $"Dialog id mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.Title, blazor.Title, $"Dialog title mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.Message, blazor.Message, $"Dialog message mismatch for '{commandId}'.");
        CollectionAssert.AreEqual(avalonia.Fields, blazor.Fields, $"Dialog fields mismatch for '{commandId}'.");
        CollectionAssert.AreEqual(avalonia.ActionIds, blazor.ActionIds, $"Dialog actions mismatch for '{commandId}'.");
    }

    private static void AssertPendingDownloadSnapshotEqual(
        PendingDownloadSnapshot avalonia,
        PendingDownloadSnapshot blazor,
        string commandId)
    {
        Assert.AreEqual(commandId.Split('.')[0], avalonia.LastCommandId, $"Unexpected Avalonia last command for '{commandId}'.");
        Assert.AreEqual(commandId.Split('.')[0], blazor.LastCommandId, $"Unexpected Blazor last command for '{commandId}'.");
        Assert.IsNotNull(avalonia.Format, $"Missing Avalonia download receipt for '{commandId}'.");
        Assert.IsNotNull(blazor.Format, $"Missing Blazor download receipt for '{commandId}'.");
        Assert.AreEqual(avalonia.Format, blazor.Format, $"Download format mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.FileName, blazor.FileName, $"Download file name mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.DocumentLength, blazor.DocumentLength, $"Download length mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.RulesetId, blazor.RulesetId, $"Download ruleset mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.ContentBase64, blazor.ContentBase64, $"Download payload mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.Notice, blazor.Notice, $"Download notice mismatch for '{commandId}'.");
    }

    private static void AssertPendingExportSnapshotEqual(
        PendingExportSnapshot avalonia,
        PendingExportSnapshot blazor,
        string commandId)
    {
        Assert.AreEqual(commandId.Split('.')[0], avalonia.LastCommandId, $"Unexpected Avalonia last command for '{commandId}'.");
        Assert.AreEqual(commandId.Split('.')[0], blazor.LastCommandId, $"Unexpected Blazor last command for '{commandId}'.");
        Assert.IsNotNull(avalonia.Format, $"Missing Avalonia export receipt for '{commandId}'.");
        Assert.IsNotNull(blazor.Format, $"Missing Blazor export receipt for '{commandId}'.");
        Assert.AreEqual(avalonia.Format, blazor.Format, $"Export format mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.FileName, blazor.FileName, $"Export file name mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.DocumentLength, blazor.DocumentLength, $"Export length mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.RulesetId, blazor.RulesetId, $"Export ruleset mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.ContentBase64, blazor.ContentBase64, $"Export payload mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.Notice, blazor.Notice, $"Export notice mismatch for '{commandId}'.");
    }

    private static void AssertPendingPrintSnapshotEqual(
        PendingPrintSnapshot avalonia,
        PendingPrintSnapshot blazor,
        string commandId)
    {
        Assert.AreEqual(commandId, avalonia.LastCommandId, $"Unexpected Avalonia last command for '{commandId}'.");
        Assert.AreEqual(commandId, blazor.LastCommandId, $"Unexpected Blazor last command for '{commandId}'.");
        Assert.IsNotNull(avalonia.FileName, $"Missing Avalonia print receipt for '{commandId}'.");
        Assert.IsNotNull(blazor.FileName, $"Missing Blazor print receipt for '{commandId}'.");
        Assert.AreEqual(avalonia.FileName, blazor.FileName, $"Print file name mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.DocumentLength, blazor.DocumentLength, $"Print length mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.RulesetId, blazor.RulesetId, $"Print ruleset mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.ContentBase64, blazor.ContentBase64, $"Print payload mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.MimeType, blazor.MimeType, $"Print mime type mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.Title, blazor.Title, $"Print title mismatch for '{commandId}'.");
        Assert.AreEqual(avalonia.Notice, blazor.Notice, $"Print notice mismatch for '{commandId}'.");
    }

    private static ShellRegionSnapshot BuildShellRegionSnapshot(CharacterOverviewState state, DefaultCommandAvailabilityEvaluator evaluator)
    {
        string[] enabledCommandIds = state.Commands
            .Where(command => evaluator.IsCommandEnabled(command, state))
            .Select(command => command.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        string[] enabledTabIds = state.NavigationTabs
            .Where(tab => evaluator.IsNavigationTabEnabled(tab, state))
            .Select(tab => tab.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        string[] dialogFieldIds = state.ActiveDialog?.Fields
            .Select(field => field.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();

        string[] dialogActionIds = state.ActiveDialog?.Actions
            .Select(action => action.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();

        return new ShellRegionSnapshot(
            HasActiveWorkspace: state.WorkspaceId is not null,
            OpenWorkspaceCount: state.Session.OpenWorkspaces.Count,
            ActiveTabId: state.ActiveTabId,
            Theme: state.Preferences.Theme,
            UiScalePercent: state.Preferences.UiScalePercent,
            EnabledCommandIds: enabledCommandIds,
            EnabledTabIds: enabledTabIds,
            DialogId: state.ActiveDialog?.Id,
            DialogTitle: state.ActiveDialog?.Title,
            DialogFieldIds: dialogFieldIds,
            DialogActionIds: dialogActionIds);
    }

    private static void AssertShellRegionsEqual(ShellRegionSnapshot avalonia, ShellRegionSnapshot blazor, string phase)
    {
        Assert.AreEqual(avalonia.HasActiveWorkspace, blazor.HasActiveWorkspace, $"Active workspace presence mismatch at phase '{phase}'.");
        Assert.AreEqual(avalonia.ActiveTabId, blazor.ActiveTabId, $"Active tab mismatch at phase '{phase}'.");
        Assert.AreEqual(avalonia.DialogId, blazor.DialogId, $"Dialog id mismatch at phase '{phase}'.");
        Assert.AreEqual(avalonia.DialogTitle, blazor.DialogTitle, $"Dialog title mismatch at phase '{phase}'.");

        CollectionAssert.AreEquivalent(
            avalonia.EnabledCommandIds,
            blazor.EnabledCommandIds,
            $"Enabled command ids mismatch at phase '{phase}'.");
        CollectionAssert.AreEquivalent(
            avalonia.EnabledTabIds,
            blazor.EnabledTabIds,
            $"Enabled tab ids mismatch at phase '{phase}'.");
        CollectionAssert.AreEquivalent(
            avalonia.DialogFieldIds,
            blazor.DialogFieldIds,
            $"Dialog field ids mismatch at phase '{phase}'.");
        CollectionAssert.AreEquivalent(
            avalonia.DialogActionIds,
            blazor.DialogActionIds,
            $"Dialog action ids mismatch at phase '{phase}'.");
    }

    private sealed record ShellRegionSnapshot(
        bool HasActiveWorkspace,
        int OpenWorkspaceCount,
        string? ActiveTabId,
        string? Theme,
        int UiScalePercent,
        string[] EnabledCommandIds,
        string[] EnabledTabIds,
        string? DialogId,
        string? DialogTitle,
        string[] DialogFieldIds,
        string[] DialogActionIds);

    private sealed record WorkspaceActionSnapshot(
        string? ActiveTabId,
        string? ActionId,
        string? SectionId,
        string? Json,
        int RowCount);

    private sealed record DialogFieldSnapshot(
        string Id,
        string Value,
        string Placeholder,
        bool IsReadOnly,
        bool IsMultiline,
        string InputType);

    private sealed record CommandDialogSnapshot(
        string CommandId,
        string? LastCommandId,
        string? DialogId,
        string? Title,
        string? Message,
        DialogFieldSnapshot[] Fields,
        string[] ActionIds);

    private sealed record PendingDownloadSnapshot(
        string? LastCommandId,
        WorkspaceDocumentFormat? Format,
        string? FileName,
        int? DocumentLength,
        string? RulesetId,
        string? ContentBase64,
        string? Notice);

    private sealed record PendingExportSnapshot(
        string? LastCommandId,
        WorkspaceDocumentFormat? Format,
        string? FileName,
        int? DocumentLength,
        string? RulesetId,
        string? ContentBase64,
        string? Notice);

    private sealed record PendingPrintSnapshot(
        string? LastCommandId,
        string? FileName,
        int? DocumentLength,
        string? RulesetId,
        string? ContentBase64,
        string? MimeType,
        string? Title,
        string? Notice);

    private static string ResolveActiveRulesetId(CharacterOverviewState state)
    {
        CharacterWorkspaceId? activeWorkspaceId = state.Session.ActiveWorkspaceId ?? state.WorkspaceId;
        if (activeWorkspaceId is null)
        {
            return state.Commands
                .Select(command => RulesetDefaults.NormalizeOptional(command.RulesetId))
                .FirstOrDefault(rulesetId => rulesetId is not null)
                ?? state.NavigationTabs
                    .Select(tab => RulesetDefaults.NormalizeOptional(tab.RulesetId))
                    .FirstOrDefault(rulesetId => rulesetId is not null)
                ?? string.Empty;
        }

        OpenWorkspaceState? openWorkspace = state.Session.OpenWorkspaces
            .FirstOrDefault(workspace => string.Equals(workspace.Id.Value, activeWorkspaceId.Value.Value, StringComparison.Ordinal));
        return RulesetDefaults.NormalizeOptional(openWorkspace?.RulesetId)
            ?? state.Commands
                .Select(command => RulesetDefaults.NormalizeOptional(command.RulesetId))
                .FirstOrDefault(rulesetId => rulesetId is not null)
            ?? state.NavigationTabs
                .Select(tab => RulesetDefaults.NormalizeOptional(tab.RulesetId))
                .FirstOrDefault(rulesetId => rulesetId is not null)
            ?? string.Empty;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            BaseAddress = BaseUri,
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            client.DefaultRequestHeaders.Remove("X-Api-Key");
            client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        }

        return client;
    }

    private static Uri ResolveBaseUri()
    {
        string? raw = Environment.GetEnvironmentVariable("CHUMMER_API_BASE_URL");
        if (string.IsNullOrWhiteSpace(raw))
            raw = Environment.GetEnvironmentVariable("CHUMMER_WEB_BASE_URL");
        if (string.IsNullOrWhiteSpace(raw))
            raw = "http://chummer-api:8080";

        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri))
            throw new InvalidOperationException($"Invalid CHUMMER_API_BASE_URL/CHUMMER_WEB_BASE_URL: '{raw}'");

        return uri;
    }

    private static string? ResolveApiKey()
    {
        return Environment.GetEnvironmentVariable("CHUMMER_API_KEY");
    }

    private static string FindTestFilePath(string fileName)
    {
        string? root = Environment.GetEnvironmentVariable("CHUMMER_REPO_ROOT");
        string[] candidates =
        {
            Path.Combine(Directory.GetCurrentDirectory(), "Chummer.Tests", "TestFiles", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "TestFiles", fileName),
            Path.Combine(AppContext.BaseDirectory, "TestFiles", fileName),
            Path.Combine("/src", "Chummer.Tests", "TestFiles", fileName),
            string.IsNullOrWhiteSpace(root) ? string.Empty : Path.Combine(root, "Chummer.Tests", "TestFiles", fileName)
        };

        string? match = candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        if (match is null)
            throw new FileNotFoundException("Could not locate test file.", fileName);

        return match;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return FindProperty(element, propertyName).GetString();
    }

    private static decimal GetDecimal(JsonElement element, string propertyName)
    {
        return FindProperty(element, propertyName).GetDecimal();
    }

    private static JsonElement FindProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement direct))
            return direct;

        if (element.TryGetProperty(char.ToLowerInvariant(propertyName[0]) + propertyName[1..], out JsonElement camel))
            return camel;

        throw new KeyNotFoundException($"Missing property '{propertyName}' in JSON payload.");
    }
}
