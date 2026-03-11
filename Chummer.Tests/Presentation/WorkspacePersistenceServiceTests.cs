#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Content;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public class WorkspacePersistenceServiceTests
{
    [TestMethod]
    public async Task UpdateMetadataAsync_updates_profile_and_preferences_on_success()
    {
        WorkspacePersistenceService service = new();
        PersistenceClientStub client = new()
        {
            MetadataResult = new CommandResult<CharacterProfileSection>(
                Success: true,
                Value: BuildProfile("Updated Name", "UPD"),
                Error: null)
        };

        WorkspaceMetadataUpdateResult result = await service.UpdateMetadataAsync(
            client,
            new CharacterWorkspaceId("ws-persist"),
            new UpdateWorkspaceMetadata("Updated Name", "UPD", "Desk Notes"),
            DesktopPreferenceState.Default,
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Profile);
        Assert.AreEqual("Updated Name", result.Profile!.Name);
        Assert.AreEqual("Desk Notes", result.Preferences.CharacterNotes);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public async Task UpdateMetadataAsync_returns_failure_when_client_fails()
    {
        WorkspacePersistenceService service = new();
        PersistenceClientStub client = new()
        {
            MetadataResult = new CommandResult<CharacterProfileSection>(
                Success: false,
                Value: null,
                Error: "Metadata failed.")
        };

        WorkspaceMetadataUpdateResult result = await service.UpdateMetadataAsync(
            client,
            new CharacterWorkspaceId("ws-persist"),
            new UpdateWorkspaceMetadata("Name", "Alias", "Notes"),
            DesktopPreferenceState.Default,
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Profile);
        Assert.AreEqual("Metadata failed.", result.Error);
    }

    [TestMethod]
    public async Task SaveAsync_returns_success_when_receipt_exists()
    {
        WorkspacePersistenceService service = new();
        PersistenceClientStub client = new()
        {
            SaveResult = new CommandResult<WorkspaceSaveReceipt>(
                Success: true,
                Value: new WorkspaceSaveReceipt(new CharacterWorkspaceId("ws-persist"), 42, "sr5"),
                Error: null)
        };

        WorkspaceSaveResult result = await service.SaveAsync(
            client,
            new CharacterWorkspaceId("ws-persist"),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public async Task Download_async_returns_receipt_when_client_succeeds()
    {
        WorkspacePersistenceService service = new();
        PersistenceClientStub client = new()
        {
            DownloadResult = new CommandResult<WorkspaceDownloadReceipt>(
                Success: true,
                Value: new WorkspaceDownloadReceipt(
                    Id: new CharacterWorkspaceId("ws-persist"),
                    Format: WorkspaceDocumentFormat.NativeXml,
                    ContentBase64: Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("<character />")),
                    FileName: "ws-persist.chum5",
                    DocumentLength: 12,
                    RulesetId: "sr5"),
                Error: null)
        };

        WorkspaceDownloadResult result = await service.DownloadAsync(
            client,
            new CharacterWorkspaceId("ws-persist"),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual("ws-persist.chum5", result.Receipt!.FileName);
        Assert.IsNull(result.Error);
    }

    private static CharacterProfileSection BuildProfile(string name, string alias)
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

    private sealed class PersistenceClientStub : IChummerClient
    {
        public CommandResult<CharacterProfileSection> MetadataResult { get; set; } = new(true, BuildProfile("Default", "DEF"), null);
        public CommandResult<WorkspaceSaveReceipt> SaveResult { get; set; } = new(true, new WorkspaceSaveReceipt(new CharacterWorkspaceId("ws"), 1, "sr5"), null);
        public CommandResult<WorkspaceDownloadReceipt> DownloadResult { get; set; } = new(
            true,
            new WorkspaceDownloadReceipt(
                new CharacterWorkspaceId("ws"),
                WorkspaceDocumentFormat.NativeXml,
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("<character />")),
                "ws.chum5",
                12,
                "sr5"),
            null);

        public Task<ShellPreferences> GetShellPreferencesAsync(CancellationToken ct) => throw new NotImplementedException();

        public Task SaveShellPreferencesAsync(ShellPreferences preferences, CancellationToken ct) => throw new NotImplementedException();

        public Task<ShellSessionState> GetShellSessionAsync(CancellationToken ct) => throw new NotImplementedException();

        public Task SaveShellSessionAsync(ShellSessionState session, CancellationToken ct) => throw new NotImplementedException();

        public Task<ShellBootstrapSnapshot> GetShellBootstrapAsync(string? rulesetId, CancellationToken ct) => throw new NotImplementedException();

        public Task<RuntimeInspectorProjection?> GetRuntimeInspectorProfileAsync(string profileId, string? rulesetId, CancellationToken ct) => throw new NotImplementedException();

        public Task<IReadOnlyList<AppCommandDefinition>> GetCommandsAsync(string? rulesetId, CancellationToken ct) => throw new NotImplementedException();

        public Task<IReadOnlyList<NavigationTabDefinition>> GetNavigationTabsAsync(string? rulesetId, CancellationToken ct) => throw new NotImplementedException();

        public Task<IReadOnlyList<WorkspaceListItem>> ListWorkspacesAsync(CancellationToken ct) => throw new NotImplementedException();

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

        public Task<CommandResult<CharacterProfileSection>> UpdateMetadataAsync(CharacterWorkspaceId id, UpdateWorkspaceMetadata command, CancellationToken ct)
            => Task.FromResult(MetadataResult);

        public Task<CommandResult<WorkspaceSaveReceipt>> SaveAsync(CharacterWorkspaceId id, CancellationToken ct)
            => Task.FromResult(SaveResult);

        public Task<CommandResult<WorkspaceDownloadReceipt>> DownloadAsync(CharacterWorkspaceId id, CancellationToken ct)
            => Task.FromResult(DownloadResult);

        public Task<CommandResult<WorkspaceExportReceipt>> ExportAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CommandResult<WorkspacePrintReceipt>> PrintAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();
    }
}
