#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Application.Tools;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Desktop.Runtime;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Shell;
using Chummer.Rulesets.Sr5;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class RestartSafeWorkspacePersistenceTests
{
    [TestMethod]
    public async Task File_backed_runtime_restores_bootstrap_and_persistence_flows_after_restart()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            const string payload = "{\"runner\":\"Restart Safe\"}";
            string expectedDownloadPayload = $"download::{payload}";
            RuntimeHarness initial = CreateRuntime(stateDirectory);
            WorkspaceImportResult imported = await initial.Client.ImportAsync(
                new WorkspaceImportDocument(
                    payload,
                    RulesetId: "SR6",
                    Format: WorkspaceDocumentFormat.Json),
                CancellationToken.None);

            await initial.Client.SaveShellPreferencesAsync(
                new ShellPreferences("SR6"),
                CancellationToken.None);
            await initial.Client.SaveShellSessionAsync(
                new ShellSessionState(
                    ActiveWorkspaceId: imported.Id.Value,
                    ActiveTabId: "tab-rules",
                    ActiveTabsByWorkspace: new Dictionary<string, string>
                    {
                        [imported.Id.Value] = "tab-rules"
                    }),
                CancellationToken.None);

            RuntimeHarness restarted = CreateRuntime(stateDirectory);
            ShellBootstrapData bootstrap = await restarted.BootstrapProvider.GetAsync(CancellationToken.None);

            Assert.AreEqual("sr6", bootstrap.RulesetId);
            Assert.AreEqual("sr6", bootstrap.PreferredRulesetId);
            Assert.AreEqual("sr6", bootstrap.ActiveRulesetId);
            Assert.AreEqual(imported.Id.Value, bootstrap.ActiveWorkspaceId?.Value);
            Assert.AreEqual("tab-rules", bootstrap.ActiveTabId);
            Assert.IsNotNull(bootstrap.ActiveTabsByWorkspace);
            Assert.AreEqual("tab-rules", bootstrap.ActiveTabsByWorkspace![imported.Id.Value]);
            Assert.HasCount(1, bootstrap.Workspaces);
            Assert.AreEqual(imported.Id.Value, bootstrap.Workspaces[0].Id.Value);
            Assert.AreEqual("Restart Safe Runner", bootstrap.Workspaces[0].Summary.Name);
            Assert.AreEqual("sr6", bootstrap.Workspaces[0].RulesetId);

            CommandResult<WorkspaceSaveReceipt> save = await restarted.Client.SaveAsync(imported.Id, CancellationToken.None);
            Assert.IsTrue(save.Success);
            Assert.IsNotNull(save.Value);
            Assert.AreEqual(imported.Id, save.Value.Id);
            Assert.AreEqual(payload.Length, save.Value.DocumentLength);
            Assert.AreEqual("sr6", save.Value.RulesetId);

            CommandResult<WorkspaceDownloadReceipt> download = await restarted.Client.DownloadAsync(imported.Id, CancellationToken.None);
            Assert.IsTrue(download.Success);
            Assert.IsNotNull(download.Value);
            Assert.AreEqual("restart-safe.sr6pkg", download.Value.FileName);
            Assert.AreEqual(WorkspaceDocumentFormat.Json, download.Value.Format);
            Assert.AreEqual(expectedDownloadPayload.Length, download.Value.DocumentLength);
            Assert.AreEqual("sr6", download.Value.RulesetId);
            Assert.AreEqual(
                expectedDownloadPayload,
                Encoding.UTF8.GetString(Convert.FromBase64String(download.Value.ContentBase64)));

            CommandResult<WorkspaceExportReceipt> export = await restarted.Client.ExportAsync(imported.Id, CancellationToken.None);
            Assert.IsTrue(export.Success);
            Assert.IsNotNull(export.Value);
            Assert.AreEqual("Restart Safe Runner-export.json", export.Value.FileName);
            string exportPayload = Encoding.UTF8.GetString(Convert.FromBase64String(export.Value.ContentBase64));
            StringAssert.Contains(exportPayload, "\"Name\": \"Restart Safe Runner\"");
            StringAssert.Contains(exportPayload, "\"Reaction\"");
            StringAssert.Contains(exportPayload, "\"Fixer\"");

            CommandResult<WorkspacePrintReceipt> print = await restarted.Client.PrintAsync(imported.Id, CancellationToken.None);
            Assert.IsTrue(print.Success);
            Assert.IsNotNull(print.Value);
            Assert.AreEqual("Restart Safe Runner-print.html", print.Value.FileName);
            Assert.AreEqual("text/html", print.Value.MimeType);
            string printPayload = Encoding.UTF8.GetString(Convert.FromBase64String(print.Value.ContentBase64));
            StringAssert.Contains(printPayload, "Restart Safe Runner");
            StringAssert.Contains(printPayload, "<html");

            string persistedPath = Path.Combine(stateDirectory, "workspaces", $"{imported.Id.Value}.json");
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(persistedPath));
            JsonElement root = json.RootElement;
            Assert.AreEqual("Json", root.GetProperty("Format").GetString());
            Assert.IsTrue(root.TryGetProperty("Envelope", out JsonElement envelope));
            Assert.IsFalse(root.TryGetProperty("Content", out _));
            Assert.IsFalse(root.TryGetProperty("RulesetId", out _));
            Assert.AreEqual("sr6", envelope.GetProperty("RulesetId").GetString());
            Assert.AreEqual(7, envelope.GetProperty("SchemaVersion").GetInt32());
            Assert.AreEqual("sr6/restart-safe", envelope.GetProperty("PayloadKind").GetString());
            Assert.AreEqual(payload, envelope.GetProperty("Payload").GetString());
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private static RuntimeHarness CreateRuntime(string stateDirectory)
    {
        FileSettingsStore settingsStore = new(stateDirectory);
        IShellPreferencesService preferencesService = new ShellPreferencesService(new SettingsShellPreferencesStore(settingsStore));
        IShellSessionService sessionService = new ShellSessionService(new SettingsShellSessionStore(settingsStore));
        IWorkspaceService workspaceService = new WorkspaceService(
            new FileWorkspaceStore(stateDirectory),
            new RulesetWorkspaceCodecResolver([new RestartSafeWorkspaceCodec()]),
            new WorkspaceImportRulesetDetector());
        RulesetPluginRegistry pluginRegistry = new(
        [
            new Sr5RulesetPlugin(),
            new Sr6RulesetPlugin()
        ]);
        IRulesetSelectionPolicy rulesetSelectionPolicy = new DefaultRulesetSelectionPolicy(pluginRegistry);
        IRulesetShellCatalogResolver shellCatalogResolver = new RulesetShellCatalogResolverService(
            pluginRegistry,
            rulesetSelectionPolicy);
        InProcessChummerClient client = new(
            workspaceService,
            shellCatalogResolver,
            rulesetSelectionPolicy: rulesetSelectionPolicy,
            shellPreferencesService: preferencesService,
            shellSessionService: sessionService);
        return new RuntimeHarness(client, new ShellBootstrapDataProvider(client));
    }

    private static string CreateTempStateDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "chummer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record RuntimeHarness(
        InProcessChummerClient Client,
        ShellBootstrapDataProvider BootstrapProvider);

    private sealed class RestartSafeWorkspaceCodec : IRulesetWorkspaceCodec
    {
        public string RulesetId => "sr6";

        public int SchemaVersion => 7;

        public string PayloadKind => "sr6/restart-safe";

        public WorkspacePayloadEnvelope WrapImport(string rulesetId, WorkspaceImportDocument document)
        {
            return new WorkspacePayloadEnvelope(
                RulesetId: RulesetDefaults.NormalizeOptional(rulesetId) ?? string.Empty,
                SchemaVersion: SchemaVersion,
                PayloadKind: PayloadKind,
                Payload: document.Content);
        }

        public CharacterFileSummary ParseSummary(WorkspacePayloadEnvelope envelope)
        {
            return new CharacterFileSummary(
                Name: "Restart Safe Runner",
                Alias: "SAFE",
                Metatype: "Human",
                BuildMethod: "Priority",
                CreatedVersion: "1.0",
                AppVersion: "1.0",
                Karma: 12m,
                Nuyen: 3456m,
                Created: true);
        }

        public object ParseSection(string sectionId, WorkspacePayloadEnvelope envelope)
        {
            return sectionId switch
            {
                "profile" => new CharacterProfileSection(
                    Name: "Restart Safe Runner",
                    Alias: "SAFE",
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
                    CreatedVersion: "1.0",
                    AppVersion: "1.0",
                    BuildMethod: "Priority",
                    GameplayOption: string.Empty,
                    Created: true,
                    Adept: false,
                    Magician: false,
                    Technomancer: false,
                    AI: false,
                    MainMugshotIndex: 0,
                    MugshotCount: 0),
                "progress" => new CharacterProgressSection(12m, 3456m, 0m, 0, 0, 0, 0, 0, 0, 0, 0, 0, 6m, 0, 0, false, false, false),
                "attributes" => new CharacterAttributesSection(1, [new CharacterAttributeSummary("Reaction", 5, 7)]),
                "contacts" => new CharacterContactsSection(1, [new CharacterContactSummary("Fixer", "Broker", "Seattle", 4, 3)]),
                _ => throw new NotSupportedException()
            };
        }

        public CharacterValidationResult Validate(WorkspacePayloadEnvelope envelope)
        {
            return new CharacterValidationResult(IsValid: true, Issues: []);
        }

        public WorkspacePayloadEnvelope UpdateMetadata(WorkspacePayloadEnvelope envelope, UpdateWorkspaceMetadata command)
        {
            return envelope;
        }

        public WorkspaceDownloadReceipt BuildDownload(CharacterWorkspaceId id, WorkspacePayloadEnvelope envelope, WorkspaceDocumentFormat format)
        {
            string downloadPayload = $"download::{envelope.Payload}";
            return new WorkspaceDownloadReceipt(
                Id: id,
                Format: format,
                ContentBase64: Convert.ToBase64String(Encoding.UTF8.GetBytes(downloadPayload)),
                FileName: "restart-safe.sr6pkg",
                DocumentLength: downloadPayload.Length,
                RulesetId: envelope.RulesetId);
        }

        public DataExportBundle BuildExportBundle(WorkspacePayloadEnvelope envelope)
        {
            return new DataExportBundle(
                Summary: ParseSummary(envelope),
                Profile: ParseSection("profile", envelope) as CharacterProfileSection,
                Progress: ParseSection("progress", envelope) as CharacterProgressSection,
                Attributes: ParseSection("attributes", envelope) as CharacterAttributesSection,
                Skills: null,
                Inventory: null,
                Qualities: null,
                Contacts: ParseSection("contacts", envelope) as CharacterContactsSection);
        }
    }
}
