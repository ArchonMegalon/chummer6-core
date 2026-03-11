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
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public class WorkspaceSectionRendererTests
{
    [TestMethod]
    public async Task RenderSectionAsync_projects_json_rows_and_selection()
    {
        WorkspaceSectionRenderer renderer = new();
        SectionRendererClientStub client = new();

        WorkspaceSectionRenderResult result = await renderer.RenderSectionAsync(
            client,
            new CharacterWorkspaceId("ws-section"),
            sectionId: "profile",
            tabId: "tab-info",
            actionId: "tab-info.profile",
            currentTabId: null,
            currentActionId: null,
            ct: CancellationToken.None);

        Assert.AreEqual("tab-info", result.ActiveTabId);
        Assert.AreEqual("tab-info.profile", result.ActiveActionId);
        Assert.AreEqual("profile", result.ActiveSectionId);
        StringAssert.Contains(result.ActiveSectionJson, "\"sectionId\": \"profile\"");
        Assert.IsGreaterThan(0, result.ActiveSectionRows.Count);
    }

    [TestMethod]
    public async Task RenderSummaryAsync_projects_summary_payload()
    {
        WorkspaceSectionRenderer renderer = new();
        SectionRendererClientStub client = new();
        WorkspaceSurfaceActionDefinition action = new(
            Id: "tab-info.summary",
            Label: "Summary",
            TabId: "tab-info",
            Kind: WorkspaceSurfaceActionKind.Summary,
            TargetId: "summary",
            RequiresOpenCharacter: true,
            EnabledByDefault: true,
            RulesetId: RulesetDefaults.Sr5);

        WorkspaceSectionRenderResult result = await renderer.RenderSummaryAsync(
            client,
            new CharacterWorkspaceId("ws-section"),
            action,
            CancellationToken.None);

        Assert.AreEqual("tab-info", result.ActiveTabId);
        Assert.AreEqual("tab-info.summary", result.ActiveActionId);
        Assert.AreEqual("summary", result.ActiveSectionId);
        StringAssert.Contains(result.ActiveSectionJson, "\"Name\": \"Summary Neo\"");
        Assert.IsGreaterThan(0, result.ActiveSectionRows.Count);
    }

    [TestMethod]
    public async Task RenderValidationAsync_projects_validation_payload()
    {
        WorkspaceSectionRenderer renderer = new();
        SectionRendererClientStub client = new();
        WorkspaceSurfaceActionDefinition action = new(
            Id: "tab-info.validate",
            Label: "Validate",
            TabId: "tab-info",
            Kind: WorkspaceSurfaceActionKind.Validate,
            TargetId: "validate",
            RequiresOpenCharacter: true,
            EnabledByDefault: true,
            RulesetId: RulesetDefaults.Sr5);

        WorkspaceSectionRenderResult result = await renderer.RenderValidationAsync(
            client,
            new CharacterWorkspaceId("ws-section"),
            action,
            CancellationToken.None);

        Assert.AreEqual("tab-info", result.ActiveTabId);
        Assert.AreEqual("tab-info.validate", result.ActiveActionId);
        Assert.AreEqual("validate", result.ActiveSectionId);
        StringAssert.Contains(result.ActiveSectionJson, "\"IsValid\": true");
        Assert.IsGreaterThan(0, result.ActiveSectionRows.Count);
    }

    private sealed class SectionRendererClientStub : IChummerClient
    {
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
                Name: "Summary Neo",
                Alias: "SUM",
                Metatype: "Human",
                BuildMethod: "Priority",
                CreatedVersion: "1.0",
                AppVersion: "1.0",
                Karma: 7m,
                Nuyen: 1000m,
                Created: true));
        }

        public Task<CharacterValidationResult> ValidateAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            return Task.FromResult(new CharacterValidationResult(
                IsValid: true,
                Issues: []));
        }

        public Task<CharacterProfileSection> GetProfileAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterProgressSection> GetProgressAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterSkillsSection> GetSkillsAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterRulesSection> GetRulesAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterBuildSection> GetBuildAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterMovementSection> GetMovementAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterAwakeningSection> GetAwakeningAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CommandResult<CharacterProfileSection>> UpdateMetadataAsync(CharacterWorkspaceId id, UpdateWorkspaceMetadata command, CancellationToken ct) => throw new NotImplementedException();

        public Task<CommandResult<WorkspaceSaveReceipt>> SaveAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CommandResult<WorkspaceDownloadReceipt>> DownloadAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CommandResult<WorkspaceExportReceipt>> ExportAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CommandResult<WorkspacePrintReceipt>> PrintAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();
    }
}
