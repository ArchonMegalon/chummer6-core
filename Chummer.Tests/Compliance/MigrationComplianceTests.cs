#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chummer.Contracts.Presentation;
using Chummer.Presentation.Overview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Compliance;

[TestClass]
public class MigrationComplianceTests
{
    private static readonly Regex SectionMethodRegex = new(@"\bCharacter[A-Za-z0-9_]+\s+Parse([A-Za-z0-9_]+)\(string xml\)", RegexOptions.Compiled);
    private static readonly Regex SectionEndpointRegex = new(@"/api/characters/sections/([a-z0-9]+)", RegexOptions.Compiled);
    private static readonly Regex SectionMapCallRegex = new(@"MapSection\(app,\s*""([a-z0-9]+)""", RegexOptions.Compiled);
    private static readonly string[] SummaryValidateMetadataTargets = ["summary", "validate", "metadata"];

    private static readonly HashSet<string> RequiredDesktopCommands = AppCommandCatalog.All
        .Select(command => command.Id)
        .ToHashSet(StringComparer.Ordinal);

    [TestMethod]
    [TestCategory("LegacyShellRegression")]
    public void Section_parsers_are_exposed_as_api_endpoints_and_ui_actions()
    {
        string interfacePath = FindPath("Chummer.Infrastructure", "Xml", "ICharacterSectionService.cs");
        string endpointDirectory = FindDirectory("Chummer.Api", "Endpoints");
        HashSet<string> parityOracleActions = LoadParityOracleIds("workspaceActions");

        string interfaceText = File.ReadAllText(interfacePath);
        string endpointText = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(endpointDirectory, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));

        HashSet<string> expectedSections = SectionMethodRegex.Matches(interfaceText)
            .Select(match => ToSectionName(match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> endpointSections = SectionEndpointRegex.Matches(endpointText)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        endpointSections.UnionWith(SectionMapCallRegex.Matches(endpointText)
            .Select(match => match.Groups[1].Value));

        CollectionAssert.AreEquivalent(expectedSections.OrderBy(x => x).ToList(), endpointSections.OrderBy(x => x).ToList(),
            "API endpoint set must match ICharacterSectionService parser set.");

        List<string> missingInUi = expectedSections.Where(section => !parityOracleActions.Contains(section)).OrderBy(x => x).ToList();
        Assert.IsEmpty(missingInUi, "Missing UI actions for sections: " + string.Join(", ", missingInUi));
    }

    [TestMethod]
    public void Desktop_shell_commands_exist_and_have_handlers()
    {
        string dialogFactoryPath = FindPath("Chummer.Presentation", "Overview", "DesktopDialogFactory.cs");
        string dialogFactoryText = File.ReadAllText(dialogFactoryPath);
        string presenterTestsPath = FindPath("Chummer.Tests", "Presentation", "CharacterOverviewPresenterTests.cs");
        string presenterTestsText = File.ReadAllText(presenterTestsPath);

        foreach (string command in RequiredDesktopCommands)
        {
            Assert.IsTrue(OverviewCommandPolicy.IsKnownSharedCommand(command), $"Missing shared command classification for '{command}'.");
            if (OverviewCommandPolicy.IsDialogCommand(command))
            {
                if (string.Equals(command, OverviewCommandPolicy.RuntimeInspectorCommandId, StringComparison.Ordinal))
                {
                    StringAssert.Contains(dialogFactoryText, "CreateRuntimeInspectorDialog", "Missing runtime inspector dialog template.");
                }
                else
                {
                    StringAssert.Contains(dialogFactoryText, $"\"{command}\" =>", $"Missing dialog template for '{command}'.");
                }
            }
        }

        StringAssert.Contains(presenterTestsText, "ExecuteCommandAsync_all_catalog_commands_are_handled");
        StringAssert.Contains(presenterTestsText, "ExecuteCommandAsync_dialog_commands_use_non_generic_dialog_templates");
    }

    [TestMethod]
    public void Dual_head_adapter_projects_reference_shared_presentation_layer()
    {
        string blazorProjectPath = FindPath("Chummer.Blazor", "Chummer.Blazor.csproj");
        string avaloniaProjectPath = FindPath("Chummer.Avalonia", "Chummer.Avalonia.csproj");
        string blazorProjectText = File.ReadAllText(blazorProjectPath);
        string avaloniaProjectText = File.ReadAllText(avaloniaProjectPath);
        string blazorProgramPath = FindPath("Chummer.Blazor", "Program.cs");
        string blazorProgramText = File.ReadAllText(blazorProgramPath);
        string avaloniaProgramPath = FindPath("Chummer.Avalonia", "Program.cs");
        string avaloniaProgramText = File.ReadAllText(avaloniaProgramPath);
        string avaloniaAppCodePath = FindPath("Chummer.Avalonia", "App.axaml.cs");
        string avaloniaAppCodeText = File.ReadAllText(avaloniaAppCodePath);
        string avaloniaMainWindowCodePath = FindPath("Chummer.Avalonia", "MainWindow.axaml.cs");
        string avaloniaMainWindowCodeText = File.ReadAllText(avaloniaMainWindowCodePath);
        string avaloniaDialogsCodePath = FindPath("Chummer.Avalonia", "MainWindow.Dialogs.cs");
        string avaloniaDialogsCodeText = File.ReadAllText(avaloniaDialogsCodePath);
        string avaloniaActionExecutionCoordinatorPath = FindPath("Chummer.Avalonia", "MainWindow.ActionExecutionCoordinator.cs");
        string avaloniaActionExecutionCoordinatorText = File.ReadAllText(avaloniaActionExecutionCoordinatorPath);
        string avaloniaUiActionFeedbackPath = FindPath("Chummer.Avalonia", "MainWindow.UiActionFeedback.cs");
        string avaloniaUiActionFeedbackText = File.ReadAllText(avaloniaUiActionFeedbackPath);
        string avaloniaPostRefreshCoordinatorPath = FindPath("Chummer.Avalonia", "MainWindow.PostRefreshCoordinators.cs");
        string avaloniaPostRefreshCoordinatorText = File.ReadAllText(avaloniaPostRefreshCoordinatorPath);

        StringAssert.Contains(blazorProjectText, @"..\Chummer.Presentation\Chummer.Presentation.csproj");
        StringAssert.Contains(blazorProjectText, @"..\Chummer.Contracts\Chummer.Contracts.csproj");
        StringAssert.Contains(avaloniaProjectText, @"..\Chummer.Presentation\Chummer.Presentation.csproj");
        StringAssert.Contains(avaloniaProjectText, @"..\Chummer.Contracts\Chummer.Contracts.csproj");
        StringAssert.Contains(avaloniaProjectText, "Avalonia.Desktop");
        StringAssert.Contains(avaloniaProjectText, "Avalonia.Themes.Fluent");

        Assert.IsTrue(File.Exists(FindPath("Chummer.Blazor", "CharacterOverviewStateBridge.cs")));
        Assert.IsTrue(File.Exists(FindPath("Chummer.Avalonia", "CharacterOverviewViewModelAdapter.cs")));
        Assert.IsTrue(File.Exists(FindPath("Chummer.Blazor", "Components", "App.razor")));
        Assert.IsTrue(File.Exists(FindPath("Chummer.Blazor", "Components", "Pages", "Home.razor")));
        Assert.IsTrue(File.Exists(FindPath("Chummer.Avalonia", "App.axaml")));
        Assert.IsTrue(File.Exists(FindPath("Chummer.Avalonia", "MainWindow.axaml")));
        Assert.IsTrue(File.Exists(FindPath("Chummer.Avalonia", "MainWindow.axaml.cs")));
        Assert.IsTrue(File.Exists(FindPath("Chummer.Avalonia", "DesktopDialogWindow.axaml")));
        Assert.IsTrue(File.Exists(FindPath("Chummer.Avalonia", "DesktopDialogWindow.axaml.cs")));
        StringAssert.Contains(blazorProgramText, "AddRazorComponents()");
        StringAssert.Contains(blazorProgramText, "MapRazorComponents<App>()");
        StringAssert.Contains(avaloniaProgramText, "BuildAvaloniaApp()");
        StringAssert.Contains(avaloniaProgramText, "UsePlatformDetect()");
        StringAssert.Contains(avaloniaAppCodeText, "ConfigureServices(");
        StringAssert.Contains(avaloniaAppCodeText, "GetRequiredService<MainWindow>()");
        StringAssert.Contains(avaloniaAppCodeText, "AddChummerLocalRuntimeClient");
        StringAssert.Contains(avaloniaAppCodeText, "ICharacterOverviewPresenter");
        StringAssert.Contains(avaloniaMainWindowCodeText, "public MainWindow(");
        StringAssert.Contains(avaloniaDialogsCodeText, "private async Task RunUiActionAsync");
        StringAssert.Contains(avaloniaDialogsCodeText, "_actionExecutionCoordinator.RunAsync(operation, operationName, CancellationToken.None);");
        StringAssert.Contains(avaloniaActionExecutionCoordinatorText, "_onFailure(operationName, ex);");
        StringAssert.Contains(avaloniaUiActionFeedbackText, "private void ApplyUiActionFailure(string operationName, Exception ex)");
        StringAssert.Contains(avaloniaUiActionFeedbackText, "MainWindowShellFrameProjector.Project(");
        StringAssert.Contains(avaloniaPostRefreshCoordinatorText, "DesktopDialogWindow dialogWindow = new(adapter);");
    }

    [TestMethod]
    public void Workspace_routes_include_section_projection_endpoint()
    {
        string workspaceEndpointsPath = FindPath("Chummer.Api", "Endpoints", "WorkspaceEndpoints.cs");
        string workspaceEndpointsText = File.ReadAllText(workspaceEndpointsPath);
        string clientContractPath = FindPath("Chummer.Presentation", "IChummerClient.cs");
        string clientContractText = File.ReadAllText(clientContractPath);

        StringAssert.Contains(workspaceEndpointsText, "/api/workspaces/{id}/sections/{sectionId}");
        StringAssert.Contains(workspaceEndpointsText, "workspaceService.GetSection(owner, workspaceId, sectionId)");
        StringAssert.Contains(workspaceEndpointsText, "/api/workspaces/{id}/summary");
        StringAssert.Contains(workspaceEndpointsText, "workspaceService.GetSummary(owner, workspaceId)");
        StringAssert.Contains(workspaceEndpointsText, "/api/workspaces/{id}/validate");
        StringAssert.Contains(workspaceEndpointsText, "workspaceService.Validate(owner, workspaceId)");
        StringAssert.Contains(workspaceEndpointsText, "/api/workspaces/{id}/export");
        StringAssert.Contains(workspaceEndpointsText, "workspaceService.Export(owner, workspaceId)");
        StringAssert.Contains(workspaceEndpointsText, "/api/workspaces/{id}/print");
        StringAssert.Contains(workspaceEndpointsText, "workspaceService.Print(owner, workspaceId)");
        StringAssert.Contains(clientContractText, "Task<CommandResult<WorkspaceExportReceipt>> ExportAsync(CharacterWorkspaceId id, CancellationToken ct);");
        StringAssert.Contains(clientContractText, "Task<CommandResult<WorkspacePrintReceipt>> PrintAsync(CharacterWorkspaceId id, CancellationToken ct);");
    }

    [TestMethod]
    public void Session_routes_define_explicit_mobile_boundary_with_owner_backed_profile_and_bundle_seams()
    {
        string apiProgramPath = FindPath("Chummer.Api", "Program.cs");
        string apiProgramText = File.ReadAllText(apiProgramPath);
        string sessionEndpointsPath = FindPath("Chummer.Api", "Endpoints", "SessionEndpoints.cs");
        string sessionEndpointsText = File.ReadAllText(sessionEndpointsPath);
        string sessionApiContractsPath = FindPath("Chummer.Contracts", "Session", "SessionApiContracts.cs");
        string sessionApiContractsText = File.ReadAllText(sessionApiContractsPath);
        string sessionServiceContractPath = FindPath("Chummer.Application", "Session", "ISessionService.cs");
        string sessionServiceContractText = File.ReadAllText(sessionServiceContractPath);
        string ownerScopedSessionServicePath = FindPath("Chummer.Application", "Session", "OwnerScopedSessionService.cs");
        string ownerScopedSessionServiceText = File.ReadAllText(ownerScopedSessionServicePath);
        string notImplementedSessionServicePath = FindPath("Chummer.Application", "Session", "NotImplementedSessionService.cs");
        string notImplementedSessionServiceText = File.ReadAllText(notImplementedSessionServicePath);
        string sessionProfileSelectionStoreContractPath = FindPath("Chummer.Application", "Session", "ISessionProfileSelectionStore.cs");
        string sessionProfileSelectionStoreContractText = File.ReadAllText(sessionProfileSelectionStoreContractPath);
        string sessionRuntimeBundleStoreContractPath = FindPath("Chummer.Application", "Session", "ISessionRuntimeBundleStore.cs");
        string sessionRuntimeBundleStoreContractText = File.ReadAllText(sessionRuntimeBundleStoreContractPath);
        string sessionRuntimeStateContractsPath = FindPath("Chummer.Contracts", "Session", "SessionRuntimeStateContracts.cs");
        string sessionRuntimeStateContractsText = File.ReadAllText(sessionRuntimeStateContractsPath);
        string sessionRuntimeProjectionContractsPath = FindPath("Chummer.Contracts", "Session", "SessionRuntimeProjectionContracts.cs");
        string sessionRuntimeProjectionContractsText = File.ReadAllText(sessionRuntimeProjectionContractsPath);
        string sessionClientContractPath = FindPath("Chummer.Presentation", "ISessionClient.cs");
        string sessionClientContractText = File.ReadAllText(sessionClientContractPath);
        string workbenchClientContractPath = FindPath("Chummer.Presentation", "IChummerClient.cs");
        string workbenchClientContractText = File.ReadAllText(workbenchClientContractPath);
        string httpSessionClientPath = FindPath("Chummer.Presentation", "HttpSessionClient.cs");
        string httpSessionClientText = File.ReadAllText(httpSessionClientPath);
        string inProcessSessionClientPath = FindPath("Chummer.Desktop.Runtime", "InProcessSessionClient.cs");
        string inProcessSessionClientText = File.ReadAllText(inProcessSessionClientPath);
        string blazorProgramPath = FindPath("Chummer.Blazor", "Program.cs");
        string blazorProgramText = File.ReadAllText(blazorProgramPath);
        string desktopRuntimeExtensionsPath = FindPath("Chummer.Desktop.Runtime", "ServiceCollectionDesktopRuntimeExtensions.cs");
        string desktopRuntimeExtensionsText = File.ReadAllText(desktopRuntimeExtensionsPath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(apiProgramText, "app.MapSessionEndpoints();");
        StringAssert.Contains(sessionEndpointsText, "/api/session/characters");
        StringAssert.Contains(sessionEndpointsText, "/api/session/characters/{characterId}");
        StringAssert.Contains(sessionEndpointsText, "/api/session/characters/{characterId}/patches");
        StringAssert.Contains(sessionEndpointsText, "/api/session/characters/{characterId}/sync");
        StringAssert.Contains(sessionEndpointsText, "/api/session/profiles");
        StringAssert.Contains(sessionEndpointsText, "/api/session/characters/{characterId}/runtime-state");
        StringAssert.Contains(sessionEndpointsText, "/api/session/characters/{characterId}/runtime-bundle");
        StringAssert.Contains(sessionEndpointsText, "/api/session/characters/{characterId}/runtime-bundle/refresh");
        StringAssert.Contains(sessionEndpointsText, "/api/session/characters/{characterId}/profile");
        StringAssert.Contains(sessionEndpointsText, "/api/session/rulepacks");
        StringAssert.Contains(sessionEndpointsText, "/api/session/pins");
        StringAssert.Contains(sessionEndpointsText, "StatusCodes.Status501NotImplemented");
        StringAssert.Contains(sessionApiContractsText, "public static class SessionApiOperations");
        StringAssert.Contains(sessionApiContractsText, "public sealed record SessionCharacterCatalog");
        StringAssert.Contains(sessionApiContractsText, "public sealed record SessionProfileCatalog");
        StringAssert.Contains(sessionApiContractsText, "public sealed record SessionProfileSelectionRequest");
        StringAssert.Contains(sessionApiContractsText, "public sealed record SessionProfileSelectionReceipt");
        StringAssert.Contains(sessionApiContractsText, "public sealed record SessionApiResult<T>");
        StringAssert.Contains(sessionApiContractsText, "public sealed record SessionNotImplementedReceipt");
        StringAssert.Contains(sessionRuntimeProjectionContractsText, "public static class SessionRuntimeSelectionStates");
        StringAssert.Contains(sessionRuntimeProjectionContractsText, "public static class SessionRuntimeBundleFreshnessStates");
        StringAssert.Contains(sessionRuntimeProjectionContractsText, "public sealed record SessionRuntimeStatusProjection");
        StringAssert.Contains(sessionServiceContractText, "public interface ISessionService");
        StringAssert.Contains(sessionServiceContractText, "SessionApiResult<SessionCharacterCatalog> ListCharacters");
        StringAssert.Contains(sessionServiceContractText, "SessionApiResult<SessionSyncReceipt> SyncCharacterLedger");
        StringAssert.Contains(sessionServiceContractText, "SessionApiResult<SessionProfileCatalog> ListProfiles");
        StringAssert.Contains(sessionServiceContractText, "SessionApiResult<SessionRuntimeStatusProjection> GetRuntimeState");
        StringAssert.Contains(sessionServiceContractText, "SessionApiResult<SessionRuntimeBundleIssueReceipt> GetRuntimeBundle");
        StringAssert.Contains(sessionServiceContractText, "SessionApiResult<SessionRuntimeBundleRefreshReceipt> RefreshRuntimeBundle");
        StringAssert.Contains(sessionServiceContractText, "SessionApiResult<SessionProfileSelectionReceipt> SelectProfile");
        StringAssert.Contains(ownerScopedSessionServiceText, "public sealed class OwnerScopedSessionService : ISessionService");
        StringAssert.Contains(ownerScopedSessionServiceText, "SessionApiResult<SessionProfileCatalog>.Implemented");
        StringAssert.Contains(ownerScopedSessionServiceText, "SessionRuntimeBundleFreshnessStates.Current");
        StringAssert.Contains(ownerScopedSessionServiceText, "SessionRuntimeBundleIssueOutcomes.Blocked");
        StringAssert.Contains(ownerScopedSessionServiceText, "SessionRuntimeBundleDeliveryModes.Cached");
        StringAssert.Contains(ownerScopedSessionServiceText, "_profileSelectionStore.Upsert");
        StringAssert.Contains(notImplementedSessionServiceText, "public sealed class NotImplementedSessionService : ISessionService");
        StringAssert.Contains(notImplementedSessionServiceText, "session_not_implemented");
        StringAssert.Contains(sessionProfileSelectionStoreContractText, "public interface ISessionProfileSelectionStore");
        StringAssert.Contains(sessionProfileSelectionStoreContractText, "SessionProfileBinding Upsert");
        StringAssert.Contains(sessionRuntimeBundleStoreContractText, "public interface ISessionRuntimeBundleStore");
        StringAssert.Contains(sessionRuntimeBundleStoreContractText, "SessionRuntimeBundleRecord Upsert");
        StringAssert.Contains(sessionRuntimeStateContractsText, "public sealed record SessionProfileBinding");
        StringAssert.Contains(sessionRuntimeStateContractsText, "public sealed record SessionRuntimeBundleRecord");
        StringAssert.Contains(sessionClientContractText, "public interface ISessionClient");
        StringAssert.Contains(sessionClientContractText, "ListCharactersAsync");
        StringAssert.Contains(sessionClientContractText, "GetCharacterProjectionAsync");
        StringAssert.Contains(sessionClientContractText, "SyncCharacterLedgerAsync");
        StringAssert.Contains(sessionClientContractText, "ListProfilesAsync");
        StringAssert.Contains(sessionClientContractText, "GetRuntimeStateAsync");
        StringAssert.Contains(sessionClientContractText, "GetRuntimeBundleAsync");
        StringAssert.Contains(sessionClientContractText, "RefreshRuntimeBundleAsync");
        StringAssert.Contains(sessionClientContractText, "SelectProfileAsync");
        StringAssert.Contains(sessionEndpointsText, "ISessionService sessionService");
        StringAssert.Contains(sessionEndpointsText, "sessionService.ListCharacters(ownerContextAccessor.Current)");
        StringAssert.Contains(sessionEndpointsText, "sessionService.SyncCharacterLedger(ownerContextAccessor.Current, characterId, batch)");
        StringAssert.Contains(sessionEndpointsText, "sessionService.ListProfiles(ownerContextAccessor.Current)");
        StringAssert.Contains(sessionEndpointsText, "sessionService.GetRuntimeState(ownerContextAccessor.Current, characterId)");
        StringAssert.Contains(sessionEndpointsText, "sessionService.GetRuntimeBundle(ownerContextAccessor.Current, characterId)");
        StringAssert.Contains(sessionEndpointsText, "sessionService.RefreshRuntimeBundle(ownerContextAccessor.Current, characterId)");
        StringAssert.Contains(sessionEndpointsText, "sessionService.SelectProfile(ownerContextAccessor.Current, characterId, request)");
        StringAssert.Contains(sessionEndpointsText, "ToResult(");
        StringAssert.Contains(httpSessionClientText, "/api/session/characters");
        StringAssert.Contains(httpSessionClientText, "/api/session/profiles");
        StringAssert.Contains(httpSessionClientText, "/api/session/characters/{Uri.EscapeDataString(characterId)}/runtime-state");
        StringAssert.Contains(httpSessionClientText, "/api/session/characters/{Uri.EscapeDataString(characterId)}/runtime-bundle");
        StringAssert.Contains(httpSessionClientText, "/api/session/characters/{Uri.EscapeDataString(characterId)}/runtime-bundle/refresh");
        StringAssert.Contains(httpSessionClientText, "/api/session/characters/{Uri.EscapeDataString(characterId)}/profile");
        StringAssert.Contains(httpSessionClientText, "SessionApiResult<T>.FromNotImplemented");
        StringAssert.Contains(inProcessSessionClientText, "ISessionService");
        StringAssert.Contains(inProcessSessionClientText, "_sessionService.SyncCharacterLedger");
        StringAssert.Contains(inProcessSessionClientText, "_sessionService.ListProfiles");
        StringAssert.Contains(inProcessSessionClientText, "_sessionService.GetRuntimeState");
        StringAssert.Contains(inProcessSessionClientText, "_sessionService.GetRuntimeBundle");
        StringAssert.Contains(inProcessSessionClientText, "_sessionService.RefreshRuntimeBundle");
        StringAssert.Contains(inProcessSessionClientText, "_sessionService.SelectProfile");
        StringAssert.Contains(blazorProgramText, "AddHttpClient<ISessionClient, HttpSessionClient>");
        StringAssert.Contains(desktopRuntimeExtensionsText, "RemoveAll<ISessionClient>()");
        StringAssert.Contains(desktopRuntimeExtensionsText, "TryAddSingleton<ISessionClient, HttpSessionClient>()");
        StringAssert.Contains(desktopRuntimeExtensionsText, "TryAddSingleton<ISessionClient, InProcessSessionClient>()");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<ISessionProfileSelectionStore>(_ => new FileSessionProfileSelectionStore(stateDirectory))");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<ISessionRuntimeBundleStore>(_ => new FileSessionRuntimeBundleStore(stateDirectory))");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<ISessionService, OwnerScopedSessionService>()");
        Assert.IsFalse(
            workbenchClientContractText.Contains("SessionDashboardProjection", StringComparison.Ordinal),
            "IChummerClient should stay workbench-scoped; session/mobile operations belong on ISessionClient.");
        Assert.IsFalse(
            workbenchClientContractText.Contains("SessionSyncReceipt", StringComparison.Ordinal),
            "IChummerClient should not absorb dedicated session sync contracts.");
        StringAssert.Contains(sessionApiContractsText, "SyncCharacterLedger = \"sync-character-ledger\"");
        StringAssert.Contains(sessionApiContractsText, "ListProfiles = \"list-profiles\"");
        StringAssert.Contains(sessionApiContractsText, "GetRuntimeState = \"get-runtime-state\"");
        StringAssert.Contains(sessionApiContractsText, "GetRuntimeBundle = \"get-runtime-bundle\"");
        StringAssert.Contains(sessionApiContractsText, "RefreshRuntimeBundle = \"refresh-runtime-bundle\"");
        StringAssert.Contains(sessionApiContractsText, "SelectProfile = \"select-profile\"");
        StringAssert.Contains(readmeText, "/api/session/*");
        StringAssert.Contains(readmeText, "owner-backed session profile catalog/selection");
        StringAssert.Contains(readmeText, "session runtime-state route");
        StringAssert.Contains(readmeText, "runtime-bundle issuance");
        StringAssert.Contains(readmeText, "runtime-bundle refresh");
        StringAssert.Contains(readmeText, "same-origin browser fetches through `Chummer.Portal`");
        StringAssert.Contains(readmeText, "CHUMMER_SESSION_API_BASE_URL");
        StringAssert.Contains(readmeText, "character projection, ledger sync, patch mutation, and pin mutation paths remain explicit `session_not_implemented` receipts");
        StringAssert.Contains(readmeText, "runtime-bundle routes");
        StringAssert.Contains(readmeText, "dedicated `ISessionClient` seam");
    }

    [TestMethod]
    public void Rulepack_registry_surface_is_separate_from_overlay_catalog_surface()
    {
        string apiProgramPath = FindPath("Chummer.Api", "Program.cs");
        string apiProgramText = File.ReadAllText(apiProgramPath);
        string infoEndpointsPath = FindPath("Chummer.Api", "Endpoints", "InfoEndpoints.cs");
        string infoEndpointsText = File.ReadAllText(infoEndpointsPath);
        string rulePackRegistryEndpointsPath = FindPath("Chummer.Api", "Endpoints", "RulePackRegistryEndpoints.cs");
        string rulePackRegistryEndpointsText = File.ReadAllText(rulePackRegistryEndpointsPath);
        string rulePackRegistryServiceContractPath = FindPath("Chummer.Application", "Content", "IRulePackRegistryService.cs");
        string rulePackRegistryServiceContractText = File.ReadAllText(rulePackRegistryServiceContractPath);
        string rulePackInstallServiceContractPath = FindPath("Chummer.Application", "Content", "IRulePackInstallService.cs");
        string rulePackInstallServiceContractText = File.ReadAllText(rulePackInstallServiceContractPath);
        string rulePackManifestStoreContractPath = FindPath("Chummer.Application", "Content", "IRulePackManifestStore.cs");
        string rulePackManifestStoreContractText = File.ReadAllText(rulePackManifestStoreContractPath);
        string rulePackInstallStateStoreContractPath = FindPath("Chummer.Application", "Content", "IRulePackInstallStateStore.cs");
        string rulePackInstallStateStoreContractText = File.ReadAllText(rulePackInstallStateStoreContractPath);
        string rulePackPublicationStoreContractPath = FindPath("Chummer.Application", "Content", "IRulePackPublicationStore.cs");
        string rulePackPublicationStoreContractText = File.ReadAllText(rulePackPublicationStoreContractPath);
        string overlayRulePackRegistryServicePath = FindPath("Chummer.Application", "Content", "OverlayRulePackRegistryService.cs");
        string overlayRulePackRegistryServiceText = File.ReadAllText(overlayRulePackRegistryServicePath);
        string rulePackInstallServicePath = FindPath("Chummer.Application", "Content", "DefaultRulePackInstallService.cs");
        string rulePackInstallServiceText = File.ReadAllText(rulePackInstallServicePath);
        string fileRulePackManifestStorePath = FindPath("Chummer.Infrastructure", "Files", "FileRulePackManifestStore.cs");
        string fileRulePackManifestStoreText = File.ReadAllText(fileRulePackManifestStorePath);
        string fileRulePackInstallStateStorePath = FindPath("Chummer.Infrastructure", "Files", "FileRulePackInstallStateStore.cs");
        string fileRulePackInstallStateStoreText = File.ReadAllText(fileRulePackInstallStateStorePath);
        string fileRulePackPublicationStorePath = FindPath("Chummer.Infrastructure", "Files", "FileRulePackPublicationStore.cs");
        string fileRulePackPublicationStoreText = File.ReadAllText(fileRulePackPublicationStorePath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string overlayExtensionsPath = FindPath("Chummer.Application", "Content", "ContentOverlayRulePackCatalogExtensions.cs");
        string overlayExtensionsText = File.ReadAllText(overlayExtensionsPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(apiProgramText, "app.MapRulePackRegistryEndpoints();");
        StringAssert.Contains(infoEndpointsText, "/api/content/overlays");
        StringAssert.Contains(infoEndpointsText, "/api/rulepacks");
        StringAssert.Contains(infoEndpointsText, "/api/rulepacks/{packId}/install-preview");
        StringAssert.Contains(infoEndpointsText, "/api/rulepacks/{packId}/install");
        StringAssert.Contains(rulePackRegistryEndpointsText, "/api/rulepacks");
        StringAssert.Contains(rulePackRegistryEndpointsText, "/api/rulepacks/{packId}/install-preview");
        StringAssert.Contains(rulePackRegistryEndpointsText, "/api/rulepacks/{packId}/install");
        StringAssert.Contains(rulePackRegistryEndpointsText, "IRulePackRegistryService");
        StringAssert.Contains(rulePackRegistryEndpointsText, "IRulePackInstallService");
        StringAssert.Contains(rulePackRegistryEndpointsText, "rulePackRegistryService.List");
        StringAssert.Contains(rulePackRegistryEndpointsText, "rulepack_not_found");
        StringAssert.Contains(rulePackRegistryServiceContractText, "public interface IRulePackRegistryService");
        StringAssert.Contains(rulePackRegistryServiceContractText, "IReadOnlyList<RulePackRegistryEntry> List");
        StringAssert.Contains(rulePackInstallServiceContractText, "public interface IRulePackInstallService");
        StringAssert.Contains(rulePackInstallServiceContractText, "RulePackInstallReceipt? Apply");
        StringAssert.Contains(rulePackManifestStoreContractText, "public interface IRulePackManifestStore");
        StringAssert.Contains(rulePackManifestStoreContractText, "RulePackManifestRecord Upsert");
        StringAssert.Contains(rulePackInstallStateStoreContractText, "public interface IRulePackInstallStateStore");
        StringAssert.Contains(rulePackInstallStateStoreContractText, "RulePackInstallRecord Upsert");
        StringAssert.Contains(rulePackPublicationStoreContractText, "public interface IRulePackPublicationStore");
        StringAssert.Contains(rulePackPublicationStoreContractText, "RulePackPublicationRecord Upsert");
        StringAssert.Contains(overlayRulePackRegistryServiceText, "public sealed class OverlayRulePackRegistryService : IRulePackRegistryService");
        StringAssert.Contains(overlayRulePackRegistryServiceText, "ToRulePackManifest");
        StringAssert.Contains(overlayRulePackRegistryServiceText, "IRulePackManifestStore");
        StringAssert.Contains(overlayRulePackRegistryServiceText, "IRulePackInstallStateStore");
        StringAssert.Contains(overlayRulePackRegistryServiceText, "IRulePackPublicationStore");
        StringAssert.Contains(rulePackInstallServiceText, "public sealed class DefaultRulePackInstallService : IRulePackInstallService");
        StringAssert.Contains(rulePackInstallServiceText, "IRulePackInstallStateStore");
        StringAssert.Contains(rulePackInstallServiceText, "IRulePackInstallHistoryStore");
        StringAssert.Contains(fileRulePackManifestStoreText, "public sealed class FileRulePackManifestStore : IRulePackManifestStore");
        StringAssert.Contains(fileRulePackManifestStoreText, "OwnerScopedStatePath.ResolveOwnerDirectory");
        StringAssert.Contains(fileRulePackInstallStateStoreText, "public sealed class FileRulePackInstallStateStore : IRulePackInstallStateStore");
        StringAssert.Contains(fileRulePackInstallStateStoreText, "OwnerScopedStatePath.ResolveOwnerDirectory");
        StringAssert.Contains(fileRulePackPublicationStoreText, "public sealed class FileRulePackPublicationStore : IRulePackPublicationStore");
        StringAssert.Contains(fileRulePackPublicationStoreText, "OwnerScopedStatePath.ResolveOwnerDirectory");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRulePackRegistryService, OverlayRulePackRegistryService>()");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRulePackInstallService, DefaultRulePackInstallService>()");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRulePackManifestStore>(_ => new FileRulePackManifestStore(stateDirectory))");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRulePackInstallStateStore>(_ => new FileRulePackInstallStateStore(stateDirectory))");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRulePackPublicationStore>(_ => new FileRulePackPublicationStore(stateDirectory))");
        StringAssert.Contains(overlayExtensionsText, "public static RulePackCatalog ToRulePackCatalog");
        StringAssert.Contains(readmeText, "/api/rulepacks/{packId}/install-preview");
        StringAssert.Contains(readmeText, "/api/rulepacks/{packId}/install");
    }

    [TestMethod]
    public void Buildkit_registry_surface_is_separate_from_rulepack_and_profile_surfaces()
    {
        string apiProgramPath = FindPath("Chummer.Api", "Program.cs");
        string apiProgramText = File.ReadAllText(apiProgramPath);
        string infoEndpointsPath = FindPath("Chummer.Api", "Endpoints", "InfoEndpoints.cs");
        string infoEndpointsText = File.ReadAllText(infoEndpointsPath);
        string buildKitRegistryEndpointsPath = FindPath("Chummer.Api", "Endpoints", "BuildKitRegistryEndpoints.cs");
        string buildKitRegistryEndpointsText = File.ReadAllText(buildKitRegistryEndpointsPath);
        string buildKitRegistryServiceContractPath = FindPath("Chummer.Application", "Content", "IBuildKitRegistryService.cs");
        string buildKitRegistryServiceContractText = File.ReadAllText(buildKitRegistryServiceContractPath);
        string defaultBuildKitRegistryServicePath = FindPath("Chummer.Application", "Content", "DefaultBuildKitRegistryService.cs");
        string defaultBuildKitRegistryServiceText = File.ReadAllText(defaultBuildKitRegistryServicePath);
        string hubCatalogContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubCatalogContracts.cs");
        string hubCatalogContractsText = File.ReadAllText(hubCatalogContractsPath);
        string hubCatalogServicePath = FindPath("Chummer.Application", "Hub", "DefaultHubCatalogService.cs");
        string hubCatalogServiceText = File.ReadAllText(hubCatalogServicePath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(apiProgramText, "app.MapBuildKitRegistryEndpoints();");
        StringAssert.Contains(apiProgramText, "AllowsPublicApiKeyBypass(context)");
        StringAssert.Contains(infoEndpointsText, "/api/buildkits");
        StringAssert.Contains(buildKitRegistryEndpointsText, "/api/buildkits");
        StringAssert.Contains(buildKitRegistryEndpointsText, "AllowPublicApiKeyBypass()");
        StringAssert.Contains(buildKitRegistryEndpointsText, "IBuildKitRegistryService");
        StringAssert.Contains(buildKitRegistryEndpointsText, "buildKitRegistryService.List");
        StringAssert.Contains(buildKitRegistryEndpointsText, "buildkit_not_found");
        StringAssert.Contains(buildKitRegistryServiceContractText, "public interface IBuildKitRegistryService");
        StringAssert.Contains(buildKitRegistryServiceContractText, "IReadOnlyList<BuildKitRegistryEntry> List");
        StringAssert.Contains(defaultBuildKitRegistryServiceText, "public sealed class DefaultBuildKitRegistryService : IBuildKitRegistryService");
        StringAssert.Contains(hubCatalogContractsText, "public const string BuildKit = \"buildkit\";");
        StringAssert.Contains(hubCatalogServiceText, "_buildKitRegistryService");
        StringAssert.Contains(hubCatalogServiceText, "HubCatalogItemKinds.BuildKit");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IBuildKitRegistryService, DefaultBuildKitRegistryService>()");
        StringAssert.Contains(readmeText, "/api/buildkits/*");
    }

    [TestMethod]
    public void Npc_vault_registry_surface_is_integrated_into_hub_catalog_seam()
    {
        string npcVaultRegistryContractsPath = FindPath("Chummer.Contracts", "Content", "NpcVaultRegistryContracts.cs");
        string npcVaultRegistryContractsText = File.ReadAllText(npcVaultRegistryContractsPath);
        string npcVaultRegistryServiceContractPath = FindPath("Chummer.Application", "Content", "INpcVaultRegistryService.cs");
        string npcVaultRegistryServiceContractText = File.ReadAllText(npcVaultRegistryServiceContractPath);
        string defaultNpcVaultRegistryServicePath = FindPath("Chummer.Application", "Content", "DefaultNpcVaultRegistryService.cs");
        string defaultNpcVaultRegistryServiceText = File.ReadAllText(defaultNpcVaultRegistryServicePath);
        string hubCatalogContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubCatalogContracts.cs");
        string hubCatalogContractsText = File.ReadAllText(hubCatalogContractsPath);
        string hubProjectDetailContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubProjectDetailContracts.cs");
        string hubProjectDetailContractsText = File.ReadAllText(hubProjectDetailContractsPath);
        string hubCatalogServicePath = FindPath("Chummer.Application", "Hub", "DefaultHubCatalogService.cs");
        string hubCatalogServiceText = File.ReadAllText(hubCatalogServicePath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(npcVaultRegistryContractsText, "public sealed record NpcEntryManifest");
        StringAssert.Contains(npcVaultRegistryContractsText, "public sealed record NpcPackManifest");
        StringAssert.Contains(npcVaultRegistryContractsText, "public sealed record EncounterPackManifest");
        StringAssert.Contains(npcVaultRegistryContractsText, "public sealed record NpcEntryRegistryEntry");
        StringAssert.Contains(npcVaultRegistryServiceContractText, "public interface INpcVaultRegistryService");
        StringAssert.Contains(npcVaultRegistryServiceContractText, "IReadOnlyList<NpcEntryRegistryEntry> ListEntries");
        StringAssert.Contains(defaultNpcVaultRegistryServiceText, "public sealed class DefaultNpcVaultRegistryService : INpcVaultRegistryService");
        StringAssert.Contains(hubCatalogContractsText, "public const string NpcEntry = \"npc-entry\";");
        StringAssert.Contains(hubCatalogContractsText, "public const string NpcPack = \"npc-pack\";");
        StringAssert.Contains(hubCatalogContractsText, "public const string EncounterPack = \"encounter-pack\";");
        StringAssert.Contains(hubProjectDetailContractsText, "public const string IncludesNpcEntry = \"includes-npc-entry\";");
        StringAssert.Contains(hubProjectDetailContractsText, "public const string CloneToLibrary = \"clone-to-library\";");
        StringAssert.Contains(hubCatalogServiceText, "INpcVaultRegistryService");
        StringAssert.Contains(hubCatalogServiceText, "HubCatalogItemKinds.NpcEntry");
        StringAssert.Contains(hubCatalogServiceText, "HubCatalogItemKinds.NpcPack");
        StringAssert.Contains(hubCatalogServiceText, "HubCatalogItemKinds.EncounterPack");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<INpcVaultRegistryService, DefaultNpcVaultRegistryService>()");
        StringAssert.Contains(readmeText, "NPC entries/packs/encounters");
    }

    [TestMethod]
    public void Ruleprofile_registry_surface_is_separate_from_rulepack_registry_surface()
    {
        string apiProgramPath = FindPath("Chummer.Api", "Program.cs");
        string apiProgramText = File.ReadAllText(apiProgramPath);
        string infoEndpointsPath = FindPath("Chummer.Api", "Endpoints", "InfoEndpoints.cs");
        string infoEndpointsText = File.ReadAllText(infoEndpointsPath);
        string ruleProfileRegistryEndpointsPath = FindPath("Chummer.Api", "Endpoints", "RuleProfileRegistryEndpoints.cs");
        string ruleProfileRegistryEndpointsText = File.ReadAllText(ruleProfileRegistryEndpointsPath);
        string ruleProfileRegistryServiceContractPath = FindPath("Chummer.Application", "Content", "IRuleProfileRegistryService.cs");
        string ruleProfileRegistryServiceContractText = File.ReadAllText(ruleProfileRegistryServiceContractPath);
        string ruleProfileManifestStoreContractPath = FindPath("Chummer.Application", "Content", "IRuleProfileManifestStore.cs");
        string ruleProfileManifestStoreContractText = File.ReadAllText(ruleProfileManifestStoreContractPath);
        string ruleProfileInstallStateStoreContractPath = FindPath("Chummer.Application", "Content", "IRuleProfileInstallStateStore.cs");
        string ruleProfileInstallStateStoreContractText = File.ReadAllText(ruleProfileInstallStateStoreContractPath);
        string ruleProfilePublicationStoreContractPath = FindPath("Chummer.Application", "Content", "IRuleProfilePublicationStore.cs");
        string ruleProfilePublicationStoreContractText = File.ReadAllText(ruleProfilePublicationStoreContractPath);
        string runtimeFingerprintServiceContractPath = FindPath("Chummer.Application", "Content", "IRuntimeFingerprintService.cs");
        string runtimeFingerprintServiceContractText = File.ReadAllText(runtimeFingerprintServiceContractPath);
        string runtimeFingerprintServicePath = FindPath("Chummer.Application", "Content", "DefaultRuntimeFingerprintService.cs");
        string runtimeFingerprintServiceText = File.ReadAllText(runtimeFingerprintServicePath);
        string defaultRuleProfileRegistryServicePath = FindPath("Chummer.Application", "Content", "DefaultRuleProfileRegistryService.cs");
        string defaultRuleProfileRegistryServiceText = File.ReadAllText(defaultRuleProfileRegistryServicePath);
        string fileRuleProfileManifestStorePath = FindPath("Chummer.Infrastructure", "Files", "FileRuleProfileManifestStore.cs");
        string fileRuleProfileManifestStoreText = File.ReadAllText(fileRuleProfileManifestStorePath);
        string fileRuleProfileInstallStateStorePath = FindPath("Chummer.Infrastructure", "Files", "FileRuleProfileInstallStateStore.cs");
        string fileRuleProfileInstallStateStoreText = File.ReadAllText(fileRuleProfileInstallStateStorePath);
        string fileRuleProfilePublicationStorePath = FindPath("Chummer.Infrastructure", "Files", "FileRuleProfilePublicationStore.cs");
        string fileRuleProfilePublicationStoreText = File.ReadAllText(fileRuleProfilePublicationStorePath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(apiProgramText, "app.MapRuleProfileRegistryEndpoints();");
        StringAssert.Contains(apiProgramText, "AllowsPublicApiKeyBypass(context)");
        StringAssert.Contains(infoEndpointsText, "/api/profiles");
        StringAssert.Contains(ruleProfileRegistryEndpointsText, "/api/profiles");
        StringAssert.Contains(ruleProfileRegistryEndpointsText, "AllowPublicApiKeyBypass()");
        StringAssert.Contains(ruleProfileRegistryEndpointsText, "IRuleProfileRegistryService");
        StringAssert.Contains(ruleProfileRegistryEndpointsText, "ruleProfileRegistryService.List");
        StringAssert.Contains(ruleProfileRegistryEndpointsText, "ruleprofile_not_found");
        StringAssert.Contains(ruleProfileRegistryServiceContractText, "public interface IRuleProfileRegistryService");
        StringAssert.Contains(ruleProfileRegistryServiceContractText, "IReadOnlyList<RuleProfileRegistryEntry> List");
        StringAssert.Contains(ruleProfileManifestStoreContractText, "public interface IRuleProfileManifestStore");
        StringAssert.Contains(ruleProfileManifestStoreContractText, "RuleProfileManifestRecord Upsert");
        StringAssert.Contains(ruleProfileInstallStateStoreContractText, "public interface IRuleProfileInstallStateStore");
        StringAssert.Contains(ruleProfileInstallStateStoreContractText, "RuleProfileInstallRecord Upsert");
        StringAssert.Contains(ruleProfilePublicationStoreContractText, "public interface IRuleProfilePublicationStore");
        StringAssert.Contains(ruleProfilePublicationStoreContractText, "RuleProfilePublicationRecord Upsert");
        StringAssert.Contains(runtimeFingerprintServiceContractText, "public interface IRuntimeFingerprintService");
        StringAssert.Contains(runtimeFingerprintServiceText, "public sealed class DefaultRuntimeFingerprintService : IRuntimeFingerprintService");
        StringAssert.Contains(runtimeFingerprintServiceText, "ComputeResolvedRuntimeFingerprint");
        StringAssert.Contains(runtimeFingerprintServiceText, "RulesetTypedCapabilityCatalog");
        StringAssert.Contains(runtimeFingerprintServiceText, "fingerprintSource.Append(\"abi=\")");
        StringAssert.Contains(defaultRuleProfileRegistryServiceText, "public sealed class DefaultRuleProfileRegistryService : IRuleProfileRegistryService");
        StringAssert.Contains(defaultRuleProfileRegistryServiceText, "IRulePackRegistryService");
        StringAssert.Contains(defaultRuleProfileRegistryServiceText, "IRuleProfileManifestStore");
        StringAssert.Contains(defaultRuleProfileRegistryServiceText, "IRuleProfileInstallStateStore");
        StringAssert.Contains(defaultRuleProfileRegistryServiceText, "IRuleProfilePublicationStore");
        StringAssert.Contains(defaultRuleProfileRegistryServiceText, "IRuntimeFingerprintService");
        StringAssert.Contains(defaultRuleProfileRegistryServiceText, "capabilityAbiVersions");
        StringAssert.Contains(defaultRuleProfileRegistryServiceText, "ComputeResolvedRuntimeFingerprint(");
        Assert.IsFalse(defaultRuleProfileRegistryServiceText.Contains("ComputeRuntimeFingerprint(", StringComparison.Ordinal));
        StringAssert.Contains(defaultRuleProfileRegistryServiceText, "official.");
        StringAssert.Contains(defaultRuleProfileRegistryServiceText, "current-overlays");
        StringAssert.Contains(fileRuleProfileManifestStoreText, "public sealed class FileRuleProfileManifestStore : IRuleProfileManifestStore");
        StringAssert.Contains(fileRuleProfileManifestStoreText, "OwnerScopedStatePath.ResolveOwnerDirectory");
        StringAssert.Contains(fileRuleProfileInstallStateStoreText, "public sealed class FileRuleProfileInstallStateStore : IRuleProfileInstallStateStore");
        StringAssert.Contains(fileRuleProfileInstallStateStoreText, "OwnerScopedStatePath.ResolveOwnerDirectory");
        StringAssert.Contains(fileRuleProfilePublicationStoreText, "public sealed class FileRuleProfilePublicationStore : IRuleProfilePublicationStore");
        StringAssert.Contains(fileRuleProfilePublicationStoreText, "OwnerScopedStatePath.ResolveOwnerDirectory");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRuntimeFingerprintService, DefaultRuntimeFingerprintService>()");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRuleProfileManifestStore>(_ => new FileRuleProfileManifestStore(stateDirectory))");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRuleProfileInstallStateStore>(_ => new FileRuleProfileInstallStateStore(stateDirectory))");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRuleProfilePublicationStore>(_ => new FileRuleProfilePublicationStore(stateDirectory))");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRuleProfileRegistryService, DefaultRuleProfileRegistryService>()");
        StringAssert.Contains(readmeText, "/api/profiles/*");
    }

    [TestMethod]
    public void Ruleprofile_apply_boundary_executes_through_owner_backed_profile_and_runtime_lock_install_seams()
    {
        string ruleProfileRegistryEndpointsPath = FindPath("Chummer.Api", "Endpoints", "RuleProfileRegistryEndpoints.cs");
        string ruleProfileRegistryEndpointsText = File.ReadAllText(ruleProfileRegistryEndpointsPath);
        string ruleProfileApplicationServiceContractPath = FindPath("Chummer.Application", "Content", "IRuleProfileApplicationService.cs");
        string ruleProfileApplicationServiceContractText = File.ReadAllText(ruleProfileApplicationServiceContractPath);
        string defaultRuleProfileApplicationServicePath = FindPath("Chummer.Application", "Content", "DefaultRuleProfileApplicationService.cs");
        string defaultRuleProfileApplicationServiceText = File.ReadAllText(defaultRuleProfileApplicationServicePath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(ruleProfileRegistryEndpointsText, "/api/profiles/{profileId}/preview");
        StringAssert.Contains(ruleProfileRegistryEndpointsText, "/api/profiles/{profileId}/apply");
        StringAssert.Contains(ruleProfileRegistryEndpointsText, "IRuleProfileApplicationService");
        StringAssert.Contains(ruleProfileApplicationServiceContractText, "public interface IRuleProfileApplicationService");
        StringAssert.Contains(ruleProfileApplicationServiceContractText, "RuleProfilePreviewReceipt? Preview");
        StringAssert.Contains(ruleProfileApplicationServiceContractText, "RuleProfileApplyReceipt? Apply");
        StringAssert.Contains(defaultRuleProfileApplicationServiceText, "IRuntimeLockInstallService");
        StringAssert.Contains(defaultRuleProfileApplicationServiceText, "IRuleProfileInstallStateStore");
        StringAssert.Contains(defaultRuleProfileApplicationServiceText, "IRuleProfileInstallHistoryStore");
        StringAssert.Contains(defaultRuleProfileApplicationServiceText, "RuleProfileApplyOutcomes.Applied");
        StringAssert.Contains(defaultRuleProfileApplicationServiceText, "_runtimeLockInstallService.Apply");
        StringAssert.Contains(defaultRuleProfileApplicationServiceText, "_installStateStore.Upsert");
        StringAssert.Contains(defaultRuleProfileApplicationServiceText, "_installHistoryStore.Append");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRuleProfileApplicationService, DefaultRuleProfileApplicationService>()");
        StringAssert.Contains(readmeText, "nested runtime-lock installation receipts");
    }

    [TestMethod]
    public void Runtime_inspector_surface_is_exposed_through_runtime_api_seam()
    {
        string apiProgramPath = FindPath("Chummer.Api", "Program.cs");
        string apiProgramText = File.ReadAllText(apiProgramPath);
        string infoEndpointsPath = FindPath("Chummer.Api", "Endpoints", "InfoEndpoints.cs");
        string infoEndpointsText = File.ReadAllText(infoEndpointsPath);
        string runtimeInspectorEndpointsPath = FindPath("Chummer.Api", "Endpoints", "RuntimeInspectorEndpoints.cs");
        string runtimeInspectorEndpointsText = File.ReadAllText(runtimeInspectorEndpointsPath);
        string runtimeInspectorServiceContractPath = FindPath("Chummer.Application", "Content", "IRuntimeInspectorService.cs");
        string runtimeInspectorServiceContractText = File.ReadAllText(runtimeInspectorServiceContractPath);
        string defaultRuntimeInspectorServicePath = FindPath("Chummer.Application", "Content", "DefaultRuntimeInspectorService.cs");
        string defaultRuntimeInspectorServiceText = File.ReadAllText(defaultRuntimeInspectorServicePath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(apiProgramText, "app.MapRuntimeInspectorEndpoints();");
        StringAssert.Contains(apiProgramText, "AllowsPublicApiKeyBypass(context)");
        StringAssert.Contains(infoEndpointsText, "/api/runtime/profiles/{profileId}");
        StringAssert.Contains(runtimeInspectorEndpointsText, "/api/runtime/profiles/{profileId}");
        StringAssert.Contains(runtimeInspectorEndpointsText, "AllowPublicApiKeyBypass()");
        StringAssert.Contains(runtimeInspectorEndpointsText, "IRuntimeInspectorService");
        StringAssert.Contains(runtimeInspectorEndpointsText, "runtime_target_not_found");
        StringAssert.Contains(runtimeInspectorServiceContractText, "public interface IRuntimeInspectorService");
        StringAssert.Contains(runtimeInspectorServiceContractText, "RuntimeInspectorProjection? GetProfileProjection");
        StringAssert.Contains(defaultRuntimeInspectorServiceText, "public sealed class DefaultRuntimeInspectorService : IRuntimeInspectorService");
        StringAssert.Contains(defaultRuntimeInspectorServiceText, "IRuleProfileRegistryService");
        StringAssert.Contains(defaultRuntimeInspectorServiceText, "IRulePackRegistryService");
        StringAssert.Contains(defaultRuntimeInspectorServiceText, "profile.Install");
        StringAssert.Contains(defaultRuntimeInspectorServiceText, "MessageKey:");
        StringAssert.Contains(defaultRuntimeInspectorServiceText, "MessageParameters:");
        StringAssert.Contains(defaultRuntimeInspectorServiceText, "SummaryKey:");
        StringAssert.Contains(defaultRuntimeInspectorServiceText, "SummaryParameters:");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRuntimeInspectorService, DefaultRuntimeInspectorService>()");
        StringAssert.Contains(readmeText, "/api/runtime/profiles/{profileId}");
    }

    [TestMethod]
    public void Runtime_lock_registry_surface_is_exposed_through_runtime_api_seam()
    {
        string apiProgramPath = FindPath("Chummer.Api", "Program.cs");
        string apiProgramText = File.ReadAllText(apiProgramPath);
        string infoEndpointsPath = FindPath("Chummer.Api", "Endpoints", "InfoEndpoints.cs");
        string infoEndpointsText = File.ReadAllText(infoEndpointsPath);
        string runtimeLockRegistryEndpointsPath = FindPath("Chummer.Api", "Endpoints", "RuntimeLockRegistryEndpoints.cs");
        string runtimeLockRegistryEndpointsText = File.ReadAllText(runtimeLockRegistryEndpointsPath);
        string runtimeLockRegistryServiceContractPath = FindPath("Chummer.Application", "Content", "IRuntimeLockRegistryService.cs");
        string runtimeLockRegistryServiceContractText = File.ReadAllText(runtimeLockRegistryServiceContractPath);
        string runtimeLockInstallServiceContractPath = FindPath("Chummer.Application", "Content", "IRuntimeLockInstallService.cs");
        string runtimeLockInstallServiceContractText = File.ReadAllText(runtimeLockInstallServiceContractPath);
        string runtimeLockStoreContractPath = FindPath("Chummer.Application", "Content", "IRuntimeLockStore.cs");
        string runtimeLockStoreContractText = File.ReadAllText(runtimeLockStoreContractPath);
        string runtimeLockInstallServicePath = FindPath("Chummer.Application", "Content", "DefaultRuntimeLockInstallService.cs");
        string runtimeLockInstallServiceText = File.ReadAllText(runtimeLockInstallServicePath);
        string runtimeLockRegistryServicePath = FindPath("Chummer.Application", "Content", "OwnerScopedRuntimeLockRegistryService.cs");
        string runtimeLockRegistryServiceText = File.ReadAllText(runtimeLockRegistryServicePath);
        string fileRuntimeLockStorePath = FindPath("Chummer.Infrastructure", "Files", "FileRuntimeLockStore.cs");
        string fileRuntimeLockStoreText = File.ReadAllText(fileRuntimeLockStorePath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(apiProgramText, "app.MapRuntimeLockRegistryEndpoints();");
        StringAssert.Contains(apiProgramText, "AllowsPublicApiKeyBypass(context)");
        StringAssert.Contains(infoEndpointsText, "/api/runtime/locks");
        StringAssert.Contains(infoEndpointsText, "/api/runtime/locks/{lockId}");
        StringAssert.Contains(infoEndpointsText, "/api/runtime/locks/{lockId}/install-preview");
        StringAssert.Contains(infoEndpointsText, "/api/runtime/locks/{lockId}/install");
        StringAssert.Contains(runtimeLockRegistryEndpointsText, "/api/runtime/locks");
        StringAssert.Contains(runtimeLockRegistryEndpointsText, "/api/runtime/locks/{lockId}");
        StringAssert.Contains(runtimeLockRegistryEndpointsText, "/api/runtime/locks/{lockId}/install-preview");
        StringAssert.Contains(runtimeLockRegistryEndpointsText, "/api/runtime/locks/{lockId}/install");
        StringAssert.Contains(runtimeLockRegistryEndpointsText, "AllowPublicApiKeyBypass()");
        StringAssert.Contains(runtimeLockRegistryEndpointsText, "IRuntimeLockRegistryService");
        StringAssert.Contains(runtimeLockRegistryEndpointsText, "IRuntimeLockInstallService");
        StringAssert.Contains(runtimeLockRegistryEndpointsText, "runtime_lock_not_found");
        StringAssert.Contains(runtimeLockRegistryEndpointsText, "invalid_runtime_lock");
        StringAssert.Contains(runtimeLockRegistryServiceContractText, "public interface IRuntimeLockRegistryService");
        StringAssert.Contains(runtimeLockRegistryServiceContractText, "RuntimeLockRegistryPage List");
        StringAssert.Contains(runtimeLockRegistryServiceContractText, "RuntimeLockRegistryEntry Upsert");
        StringAssert.Contains(runtimeLockInstallServiceContractText, "public interface IRuntimeLockInstallService");
        StringAssert.Contains(runtimeLockInstallServiceContractText, "RuntimeLockInstallReceipt? Apply");
        StringAssert.Contains(runtimeLockStoreContractText, "public interface IRuntimeLockStore");
        StringAssert.Contains(runtimeLockStoreContractText, "RuntimeLockRegistryEntry Upsert");
        StringAssert.Contains(runtimeLockRegistryServiceText, "public sealed class OwnerScopedRuntimeLockRegistryService : IRuntimeLockRegistryService");
        StringAssert.Contains(runtimeLockRegistryServiceText, "public RuntimeLockRegistryEntry Upsert(OwnerScope owner, string lockId, RuntimeLockSaveRequest request)");
        StringAssert.Contains(runtimeLockRegistryServiceText, "IRuntimeLockStore");
        StringAssert.Contains(runtimeLockRegistryServiceText, "RuntimeLockCatalogKinds.Published");
        StringAssert.Contains(runtimeLockRegistryServiceText, "RuntimeLockCatalogKinds.Derived");
        StringAssert.Contains(runtimeLockRegistryServiceText, "profile.Install");
        StringAssert.Contains(runtimeLockInstallServiceText, "public sealed class DefaultRuntimeLockInstallService : IRuntimeLockInstallService");
        StringAssert.Contains(runtimeLockInstallServiceText, "IRuntimeLockInstallHistoryStore");
        Assert.IsFalse(runtimeLockInstallServiceText.Contains("IRuntimeLockStore", StringComparison.Ordinal));
        StringAssert.Contains(fileRuntimeLockStoreText, "public sealed class FileRuntimeLockStore : IRuntimeLockStore");
        StringAssert.Contains(fileRuntimeLockStoreText, "ArtifactInstallStates.Available");
        StringAssert.Contains(fileRuntimeLockStoreText, "OwnerScopedStatePath.ResolveOwnerDirectory");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRuntimeLockInstallService, DefaultRuntimeLockInstallService>()");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRuntimeLockStore>(_ => new FileRuntimeLockStore(stateDirectory))");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRuntimeLockRegistryService, OwnerScopedRuntimeLockRegistryService>()");
        StringAssert.Contains(readmeText, "/api/runtime/locks/*");
        StringAssert.Contains(readmeText, "/api/runtime/locks/{lockId}");
        StringAssert.Contains(readmeText, "/api/runtime/locks/{lockId}/install-preview");
        StringAssert.Contains(readmeText, "/api/runtime/locks/{lockId}/install");
    }

    [TestMethod]
    public void Hub_catalog_surface_is_exposed_through_hub_search_api_seam()
    {
        string apiProgramPath = FindPath("Chummer.Api", "Program.cs");
        string apiProgramText = File.ReadAllText(apiProgramPath);
        string infoEndpointsPath = FindPath("Chummer.Api", "Endpoints", "InfoEndpoints.cs");
        string infoEndpointsText = File.ReadAllText(infoEndpointsPath);
        string hubCatalogEndpointsPath = FindPath("Chummer.Api", "Endpoints", "HubCatalogEndpoints.cs");
        string hubCatalogEndpointsText = File.ReadAllText(hubCatalogEndpointsPath);
        string hubCatalogServiceContractPath = FindPath("Chummer.Application", "Hub", "IHubCatalogService.cs");
        string hubCatalogServiceContractText = File.ReadAllText(hubCatalogServiceContractPath);
        string hubCatalogServicePath = FindPath("Chummer.Application", "Hub", "DefaultHubCatalogService.cs");
        string hubCatalogServiceText = File.ReadAllText(hubCatalogServicePath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(apiProgramText, "app.MapHubCatalogEndpoints();");
        StringAssert.Contains(apiProgramText, "AllowsPublicApiKeyBypass(context)");
        StringAssert.Contains(infoEndpointsText, "/api/hub/search");
        StringAssert.Contains(hubCatalogEndpointsText, "/api/hub/search");
        StringAssert.Contains(hubCatalogEndpointsText, "AllowPublicApiKeyBypass()");
        StringAssert.Contains(hubCatalogEndpointsText, "IHubCatalogService");
        StringAssert.Contains(hubCatalogServiceContractText, "public interface IHubCatalogService");
        StringAssert.Contains(hubCatalogServiceContractText, "HubCatalogResultPage Search");
        StringAssert.Contains(hubCatalogServiceText, "IRulePackRegistryService");
        StringAssert.Contains(hubCatalogServiceText, "IRuleProfileRegistryService");
        StringAssert.Contains(hubCatalogServiceText, "IRuntimeLockRegistryService");
        StringAssert.Contains(hubCatalogServiceText, "InstallState: entry.Install.State");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IHubCatalogService, DefaultHubCatalogService>()");
        StringAssert.Contains(readmeText, "/api/hub/search");
    }

    [TestMethod]
    public void Hub_project_detail_surface_is_exposed_through_hub_api_seam()
    {
        string apiProgramPath = FindPath("Chummer.Api", "Program.cs");
        string apiProgramText = File.ReadAllText(apiProgramPath);
        string infoEndpointsPath = FindPath("Chummer.Api", "Endpoints", "InfoEndpoints.cs");
        string infoEndpointsText = File.ReadAllText(infoEndpointsPath);
        string hubCatalogEndpointsPath = FindPath("Chummer.Api", "Endpoints", "HubCatalogEndpoints.cs");
        string hubCatalogEndpointsText = File.ReadAllText(hubCatalogEndpointsPath);
        string hubCatalogServiceContractPath = FindPath("Chummer.Application", "Hub", "IHubCatalogService.cs");
        string hubCatalogServiceContractText = File.ReadAllText(hubCatalogServiceContractPath);
        string hubCatalogContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubCatalogContracts.cs");
        string hubCatalogContractsText = File.ReadAllText(hubCatalogContractsPath);
        string hubProjectDetailContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubProjectDetailContracts.cs");
        string hubProjectDetailContractsText = File.ReadAllText(hubProjectDetailContractsPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(apiProgramText, "AllowsPublicApiKeyBypass(context)");
        StringAssert.Contains(infoEndpointsText, "/api/hub/projects/{kind}/{itemId}");
        StringAssert.Contains(hubCatalogEndpointsText, "/api/hub/projects/{kind}/{itemId}");
        StringAssert.Contains(hubCatalogEndpointsText, "AllowPublicApiKeyBypass()");
        StringAssert.Contains(hubCatalogEndpointsText, "ValidateProjectKind(kind)");
        StringAssert.Contains(hubCatalogEndpointsText, "hub_project_kind_invalid");
        StringAssert.Contains(hubCatalogEndpointsText, "allowedKinds = HubCatalogItemKinds.All");
        StringAssert.Contains(hubCatalogEndpointsText, "hub_project_not_found");
        StringAssert.Contains(hubCatalogServiceContractText, "HubProjectDetailProjection? GetProjectDetail");
        StringAssert.Contains(hubCatalogContractsText, "public static class HubCatalogItemKinds");
        StringAssert.Contains(hubCatalogContractsText, "public static IReadOnlyList<string> All");
        StringAssert.Contains(hubCatalogContractsText, "public static bool IsDefined");
        StringAssert.Contains(hubCatalogContractsText, "public static string NormalizeRequired");
        StringAssert.Contains(hubCatalogContractsText, "public static string? NormalizeOptional");
        StringAssert.Contains(hubProjectDetailContractsText, "public sealed record HubProjectDetailProjection");
        StringAssert.Contains(readmeText, "/api/hub/projects/*");
    }

    [TestMethod]
    public void Owner_backed_install_history_seams_are_registered_for_hub_runtime_surfaces()
    {
        string artifactContractsPath = FindPath("Chummer.Contracts", "Content", "ArtifactContracts.cs");
        string artifactContractsText = File.ReadAllText(artifactContractsPath);
        string rulePackRegistryContractsPath = FindPath("Chummer.Contracts", "Content", "RulePackRegistryContracts.cs");
        string rulePackRegistryContractsText = File.ReadAllText(rulePackRegistryContractsPath);
        string ruleProfileRegistryContractsPath = FindPath("Chummer.Contracts", "Content", "RuleProfileRegistryContracts.cs");
        string ruleProfileRegistryContractsText = File.ReadAllText(ruleProfileRegistryContractsPath);
        string runtimeLockRegistryContractsPath = FindPath("Chummer.Contracts", "Content", "RuntimeLockRegistryContracts.cs");
        string runtimeLockRegistryContractsText = File.ReadAllText(runtimeLockRegistryContractsPath);
        string rulePackInstallHistoryStoreContractPath = FindPath("Chummer.Application", "Content", "IRulePackInstallHistoryStore.cs");
        string rulePackInstallHistoryStoreContractText = File.ReadAllText(rulePackInstallHistoryStoreContractPath);
        string ruleProfileInstallHistoryStoreContractPath = FindPath("Chummer.Application", "Content", "IRuleProfileInstallHistoryStore.cs");
        string ruleProfileInstallHistoryStoreContractText = File.ReadAllText(ruleProfileInstallHistoryStoreContractPath);
        string runtimeLockInstallHistoryStoreContractPath = FindPath("Chummer.Application", "Content", "IRuntimeLockInstallHistoryStore.cs");
        string runtimeLockInstallHistoryStoreContractText = File.ReadAllText(runtimeLockInstallHistoryStoreContractPath);
        string fileRulePackInstallHistoryStorePath = FindPath("Chummer.Infrastructure", "Files", "FileRulePackInstallHistoryStore.cs");
        string fileRulePackInstallHistoryStoreText = File.ReadAllText(fileRulePackInstallHistoryStorePath);
        string fileRuleProfileInstallHistoryStorePath = FindPath("Chummer.Infrastructure", "Files", "FileRuleProfileInstallHistoryStore.cs");
        string fileRuleProfileInstallHistoryStoreText = File.ReadAllText(fileRuleProfileInstallHistoryStorePath);
        string fileRuntimeLockInstallHistoryStorePath = FindPath("Chummer.Infrastructure", "Files", "FileRuntimeLockInstallHistoryStore.cs");
        string fileRuntimeLockInstallHistoryStoreText = File.ReadAllText(fileRuntimeLockInstallHistoryStorePath);
        string hubCatalogServicePath = FindPath("Chummer.Application", "Hub", "DefaultHubCatalogService.cs");
        string hubCatalogServiceText = File.ReadAllText(hubCatalogServicePath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(artifactContractsText, "public static class ArtifactInstallHistoryOperations");
        StringAssert.Contains(artifactContractsText, "public sealed record ArtifactInstallHistoryEntry");
        StringAssert.Contains(rulePackRegistryContractsText, "public sealed record RulePackInstallHistoryRecord");
        StringAssert.Contains(ruleProfileRegistryContractsText, "public sealed record RuleProfileInstallHistoryRecord");
        StringAssert.Contains(runtimeLockRegistryContractsText, "public sealed record RuntimeLockInstallHistoryRecord");
        StringAssert.Contains(rulePackInstallHistoryStoreContractText, "public interface IRulePackInstallHistoryStore");
        StringAssert.Contains(rulePackInstallHistoryStoreContractText, "RulePackInstallHistoryRecord Append");
        StringAssert.Contains(ruleProfileInstallHistoryStoreContractText, "public interface IRuleProfileInstallHistoryStore");
        StringAssert.Contains(ruleProfileInstallHistoryStoreContractText, "RuleProfileInstallHistoryRecord Append");
        StringAssert.Contains(runtimeLockInstallHistoryStoreContractText, "public interface IRuntimeLockInstallHistoryStore");
        StringAssert.Contains(runtimeLockInstallHistoryStoreContractText, "RuntimeLockInstallHistoryRecord Append");
        StringAssert.Contains(fileRulePackInstallHistoryStoreText, "public sealed class FileRulePackInstallHistoryStore : IRulePackInstallHistoryStore");
        StringAssert.Contains(fileRuleProfileInstallHistoryStoreText, "public sealed class FileRuleProfileInstallHistoryStore : IRuleProfileInstallHistoryStore");
        StringAssert.Contains(fileRuntimeLockInstallHistoryStoreText, "public sealed class FileRuntimeLockInstallHistoryStore : IRuntimeLockInstallHistoryStore");
        StringAssert.Contains(hubCatalogServiceText, "IRulePackInstallHistoryStore");
        StringAssert.Contains(hubCatalogServiceText, "IRuleProfileInstallHistoryStore");
        StringAssert.Contains(hubCatalogServiceText, "IRuntimeLockInstallHistoryStore");
        StringAssert.Contains(hubCatalogServiceText, "install-history-count");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRulePackInstallHistoryStore>(_ => new FileRulePackInstallHistoryStore(stateDirectory))");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRuleProfileInstallHistoryStore>(_ => new FileRuleProfileInstallHistoryStore(stateDirectory))");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IRuntimeLockInstallHistoryStore>(_ => new FileRuntimeLockInstallHistoryStore(stateDirectory))");
        StringAssert.Contains(readmeText, "install history");
    }

    [TestMethod]
    public void Hub_project_install_preview_surface_is_exposed_through_hub_api_seam()
    {
        string infoEndpointsPath = FindPath("Chummer.Api", "Endpoints", "InfoEndpoints.cs");
        string infoEndpointsText = File.ReadAllText(infoEndpointsPath);
        string hubCatalogEndpointsPath = FindPath("Chummer.Api", "Endpoints", "HubCatalogEndpoints.cs");
        string hubCatalogEndpointsText = File.ReadAllText(hubCatalogEndpointsPath);
        string hubInstallPreviewServiceContractPath = FindPath("Chummer.Application", "Hub", "IHubInstallPreviewService.cs");
        string hubInstallPreviewServiceContractText = File.ReadAllText(hubInstallPreviewServiceContractPath);
        string hubInstallPreviewServicePath = FindPath("Chummer.Application", "Hub", "DefaultHubInstallPreviewService.cs");
        string hubInstallPreviewServiceText = File.ReadAllText(hubInstallPreviewServicePath);
        string hubInstallPreviewContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubProjectInstallPreviewContracts.cs");
        string hubInstallPreviewContractsText = File.ReadAllText(hubInstallPreviewContractsPath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(infoEndpointsText, "/api/hub/projects/{kind}/{itemId}/install-preview");
        StringAssert.Contains(hubCatalogEndpointsText, "/api/hub/projects/{kind}/{itemId}/install-preview");
        StringAssert.Contains(hubCatalogEndpointsText, "IHubInstallPreviewService");
        StringAssert.Contains(hubCatalogEndpointsText, "hub_project_kind_invalid");
        StringAssert.Contains(hubCatalogEndpointsText, "hub_project_not_found");
        StringAssert.Contains(hubInstallPreviewServiceContractText, "public interface IHubInstallPreviewService");
        StringAssert.Contains(hubInstallPreviewServiceContractText, "HubProjectInstallPreviewReceipt? Preview");
        StringAssert.Contains(hubInstallPreviewServiceText, "public sealed class DefaultHubInstallPreviewService : IHubInstallPreviewService");
        StringAssert.Contains(hubInstallPreviewServiceText, "HubCatalogItemKinds.NormalizeRequired(kind)");
        StringAssert.Contains(hubInstallPreviewServiceText, "HubProjectInstallPreviewStates.Ready");
        StringAssert.Contains(hubInstallPreviewServiceText, "HubProjectInstallPreviewStates.Deferred");
        StringAssert.Contains(hubInstallPreviewServiceText, "entry.Install.State");
        StringAssert.Contains(hubInstallPreviewServiceText, "HubProjectInstallPreviewDiagnosticKinds.InstallState");
        StringAssert.Contains(hubInstallPreviewContractsText, "public sealed record HubProjectInstallPreviewReceipt");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IHubInstallPreviewService, DefaultHubInstallPreviewService>()");
        StringAssert.Contains(readmeText, "/api/hub/projects/*/install-preview");
    }

    [TestMethod]
    public void Hub_project_compatibility_surface_is_exposed_through_hub_api_seam()
    {
        string infoEndpointsPath = FindPath("Chummer.Api", "Endpoints", "InfoEndpoints.cs");
        string infoEndpointsText = File.ReadAllText(infoEndpointsPath);
        string hubCatalogEndpointsPath = FindPath("Chummer.Api", "Endpoints", "HubCatalogEndpoints.cs");
        string hubCatalogEndpointsText = File.ReadAllText(hubCatalogEndpointsPath);
        string hubCompatibilityServiceContractPath = FindPath("Chummer.Application", "Hub", "IHubProjectCompatibilityService.cs");
        string hubCompatibilityServiceContractText = File.ReadAllText(hubCompatibilityServiceContractPath);
        string hubCompatibilityServicePath = FindPath("Chummer.Application", "Hub", "DefaultHubProjectCompatibilityService.cs");
        string hubCompatibilityServiceText = File.ReadAllText(hubCompatibilityServicePath);
        string hubCompatibilityContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubProjectCompatibilityContracts.cs");
        string hubCompatibilityContractsText = File.ReadAllText(hubCompatibilityContractsPath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(infoEndpointsText, "/api/hub/projects/{kind}/{itemId}/compatibility");
        StringAssert.Contains(hubCatalogEndpointsText, "/api/hub/projects/{kind}/{itemId}/compatibility");
        StringAssert.Contains(hubCatalogEndpointsText, "IHubProjectCompatibilityService");
        StringAssert.Contains(hubCatalogEndpointsText, "hub_project_kind_invalid");
        StringAssert.Contains(hubCatalogEndpointsText, "hub_project_not_found");
        StringAssert.Contains(hubCompatibilityServiceContractText, "public interface IHubProjectCompatibilityService");
        StringAssert.Contains(hubCompatibilityServiceContractText, "HubProjectCompatibilityMatrix? GetMatrix");
        StringAssert.Contains(hubCompatibilityServiceText, "public sealed class DefaultHubProjectCompatibilityService : IHubProjectCompatibilityService");
        StringAssert.Contains(hubCompatibilityServiceText, "HubCatalogItemKinds.NormalizeRequired(kind)");
        StringAssert.Contains(hubCompatibilityServiceText, "HubProjectCompatibilityStates.Compatible");
        StringAssert.Contains(hubCompatibilityContractsText, "public sealed record HubProjectCompatibilityMatrix");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IHubProjectCompatibilityService, DefaultHubProjectCompatibilityService>()");
        StringAssert.Contains(readmeText, "/api/hub/projects/*/compatibility");
    }

    [TestMethod]
    public void Hub_publication_and_moderation_surfaces_are_exposed_through_protected_hub_api_seams()
    {
        string infoEndpointsPath = FindPath("Chummer.Api", "Endpoints", "InfoEndpoints.cs");
        string infoEndpointsText = File.ReadAllText(infoEndpointsPath);
        string hubPublicationEndpointsPath = FindPath("Chummer.Api", "Endpoints", "HubPublicationEndpoints.cs");
        string hubPublicationEndpointsText = File.ReadAllText(hubPublicationEndpointsPath);
        string hubDraftStoreContractPath = FindPath("Chummer.Application", "Hub", "IHubDraftStore.cs");
        string hubDraftStoreContractText = File.ReadAllText(hubDraftStoreContractPath);
        string hubModerationCaseStoreContractPath = FindPath("Chummer.Application", "Hub", "IHubModerationCaseStore.cs");
        string hubModerationCaseStoreContractText = File.ReadAllText(hubModerationCaseStoreContractPath);
        string hubPublicationServiceContractPath = FindPath("Chummer.Application", "Hub", "IHubPublicationService.cs");
        string hubPublicationServiceContractText = File.ReadAllText(hubPublicationServiceContractPath);
        string hubPublicationServicePath = FindPath("Chummer.Application", "Hub", "DefaultHubPublicationService.cs");
        string hubPublicationServiceText = File.ReadAllText(hubPublicationServicePath);
        string hubModerationServiceContractPath = FindPath("Chummer.Application", "Hub", "IHubModerationService.cs");
        string hubModerationServiceContractText = File.ReadAllText(hubModerationServiceContractPath);
        string hubModerationServicePath = FindPath("Chummer.Application", "Hub", "DefaultHubModerationService.cs");
        string hubModerationServiceText = File.ReadAllText(hubModerationServicePath);
        string hubPublicationContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubPublicationContracts.cs");
        string hubPublicationContractsText = File.ReadAllText(hubPublicationContractsPath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(infoEndpointsText, "/api/hub/publish/drafts");
        StringAssert.Contains(infoEndpointsText, "/api/hub/publish/drafts/{draftId}");
        StringAssert.Contains(infoEndpointsText, "/api/hub/publish/drafts/{draftId}/archive");
        StringAssert.Contains(infoEndpointsText, "/api/hub/publish/{kind}/{itemId}/submit");
        StringAssert.Contains(infoEndpointsText, "/api/hub/moderation/queue");
        StringAssert.Contains(infoEndpointsText, "/api/hub/moderation/queue/{caseId}/approve");
        StringAssert.Contains(infoEndpointsText, "/api/hub/moderation/queue/{caseId}/reject");
        StringAssert.Contains(hubPublicationEndpointsText, "public static class HubPublicationEndpoints");
        StringAssert.Contains(hubPublicationEndpointsText, "MapGet(\"/api/hub/publish/drafts\"");
        StringAssert.Contains(hubPublicationEndpointsText, "MapGet(\"/api/hub/publish/drafts/{draftId}\"");
        StringAssert.Contains(hubPublicationEndpointsText, "MapPut(\"/api/hub/publish/drafts/{draftId}\"");
        StringAssert.Contains(hubPublicationEndpointsText, "MapPost(\"/api/hub/publish/drafts/{draftId}/archive\"");
        StringAssert.Contains(hubPublicationEndpointsText, "MapDelete(\"/api/hub/publish/drafts/{draftId}\"");
        StringAssert.Contains(hubPublicationEndpointsText, "MapPost(\"/api/hub/moderation/queue/{caseId}/approve\"");
        StringAssert.Contains(hubPublicationEndpointsText, "MapPost(\"/api/hub/moderation/queue/{caseId}/reject\"");
        StringAssert.Contains(hubPublicationEndpointsText, "IHubPublicationService");
        StringAssert.Contains(hubPublicationEndpointsText, "IHubModerationService");
        StringAssert.Contains(hubPublicationEndpointsText, "hub_project_kind_invalid");
        StringAssert.Contains(hubPublicationEndpointsText, "ValidateProjectKindOptional");
        StringAssert.Contains(hubPublicationEndpointsText, "ValidateProjectKindRequired");
        StringAssert.Contains(hubPublicationEndpointsText, "allowedKinds = HubCatalogItemKinds.All");
        StringAssert.Contains(hubDraftStoreContractText, "public interface IHubDraftStore");
        StringAssert.Contains(hubDraftStoreContractText, "HubDraftRecord? Get(OwnerScope owner, string draftId)");
        StringAssert.Contains(hubDraftStoreContractText, "HubDraftRecord Upsert");
        StringAssert.Contains(hubDraftStoreContractText, "bool Delete(OwnerScope owner, string draftId)");
        StringAssert.Contains(hubModerationCaseStoreContractText, "public interface IHubModerationCaseStore");
        StringAssert.Contains(hubModerationCaseStoreContractText, "HubModerationCaseRecord? GetByCaseId");
        StringAssert.Contains(hubModerationCaseStoreContractText, "HubModerationCaseRecord? GetByDraftId");
        StringAssert.Contains(hubModerationCaseStoreContractText, "HubModerationCaseRecord Upsert");
        StringAssert.Contains(hubModerationCaseStoreContractText, "bool DeleteByDraftId(OwnerScope owner, string draftId)");
        StringAssert.Contains(hubPublicationServiceContractText, "public interface IHubPublicationService");
        StringAssert.Contains(hubPublicationServiceContractText, "HubPublicationResult<HubPublishDraftList> ListDrafts");
        StringAssert.Contains(hubPublicationServiceContractText, "HubPublicationResult<HubDraftDetailProjection?> GetDraft");
        StringAssert.Contains(hubPublicationServiceContractText, "HubPublicationResult<HubPublishDraftReceipt> CreateDraft");
        StringAssert.Contains(hubPublicationServiceContractText, "HubPublicationResult<HubPublishDraftReceipt?> UpdateDraft");
        StringAssert.Contains(hubPublicationServiceContractText, "HubPublicationResult<HubPublishDraftReceipt?> ArchiveDraft");
        StringAssert.Contains(hubPublicationServiceContractText, "HubPublicationResult<bool> DeleteDraft");
        StringAssert.Contains(hubPublicationServiceContractText, "HubPublicationResult<HubProjectSubmissionReceipt> SubmitForReview");
        StringAssert.Contains(hubPublicationServiceText, "public sealed class DefaultHubPublicationService : IHubPublicationService");
        StringAssert.Contains(hubPublicationServiceText, "HubDraftDetailProjection");
        StringAssert.Contains(hubPublicationServiceText, "NormalizeOptionalText(request.Description)");
        StringAssert.Contains(hubPublicationServiceText, "HubCatalogItemKinds.NormalizeOptional(kind)");
        StringAssert.Contains(hubPublicationServiceText, "HubCatalogItemKinds.NormalizeRequired(request.ProjectKind");
        StringAssert.Contains(hubPublicationServiceText, "HubPublicationStates.Archived");
        StringAssert.Contains(hubPublicationServiceText, "_moderationCaseStore.DeleteByDraftId");
        StringAssert.Contains(hubPublicationServiceText, "HubPublicationStates.Submitted");
        StringAssert.Contains(hubModerationServiceContractText, "public interface IHubModerationService");
        StringAssert.Contains(hubModerationServiceContractText, "HubPublicationResult<HubModerationQueue> ListQueue");
        StringAssert.Contains(hubModerationServiceContractText, "HubPublicationResult<HubModerationDecisionReceipt?> Approve");
        StringAssert.Contains(hubModerationServiceContractText, "HubPublicationResult<HubModerationDecisionReceipt?> Reject");
        StringAssert.Contains(hubModerationServiceText, "public sealed class DefaultHubModerationService : IHubModerationService");
        StringAssert.Contains(hubModerationServiceText, "HubModerationStates.Approved");
        StringAssert.Contains(hubModerationServiceText, "HubModerationStates.Rejected");
        StringAssert.Contains(hubModerationServiceText, "_moderationCaseStore.GetByCaseId");
        StringAssert.Contains(hubPublicationContractsText, "public static class HubPublicationOperations");
        StringAssert.Contains(hubPublicationContractsText, "public sealed record HubUpdateDraftRequest");
        StringAssert.Contains(hubPublicationContractsText, "public const string ArchiveDraft = \"archive-draft\"");
        StringAssert.Contains(hubPublicationContractsText, "public const string DeleteDraft = \"delete-draft\"");
        StringAssert.Contains(hubPublicationContractsText, "public const string ApproveModerationCase = \"approve-moderation-case\"");
        StringAssert.Contains(hubPublicationContractsText, "public const string RejectModerationCase = \"reject-moderation-case\"");
        StringAssert.Contains(hubPublicationContractsText, "public const string Archived = \"archived\"");
        StringAssert.Contains(hubPublicationContractsText, "public sealed record HubPublishDraftList");
        StringAssert.Contains(hubPublicationContractsText, "public sealed record HubDraftDetailProjection");
        StringAssert.Contains(hubPublicationContractsText, "public sealed record HubPublishDraftRequest");
        StringAssert.Contains(hubPublicationContractsText, "public sealed record HubProjectSubmissionReceipt");
        StringAssert.Contains(hubPublicationContractsText, "public sealed record HubModerationQueue");
        StringAssert.Contains(hubPublicationContractsText, "public sealed record HubModerationDecisionRequest");
        StringAssert.Contains(hubPublicationContractsText, "public sealed record HubModerationDecisionReceipt");
        StringAssert.Contains(hubPublicationContractsText, "public sealed record HubDraftRecord");
        StringAssert.Contains(hubPublicationContractsText, "public sealed record HubModerationCaseRecord");
        StringAssert.Contains(hubPublicationContractsText, "string? PublisherId");
        StringAssert.Contains(hubPublicationServiceText, "IHubPublisherStore");
        StringAssert.Contains(hubPublicationServiceText, "ResolvePublisherId");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IHubDraftStore>(_ => new FileHubDraftStore(stateDirectory))");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IHubModerationCaseStore>(_ => new FileHubModerationCaseStore(stateDirectory))");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IHubPublicationService, DefaultHubPublicationService>()");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IHubModerationService, DefaultHubModerationService>()");
        StringAssert.Contains(readmeText, "/api/hub/publish/*");
        StringAssert.Contains(readmeText, "/api/hub/moderation/*");
        StringAssert.Contains(readmeText, "stable owner-backed publisher profiles");
    }

    [TestMethod]
    public void Hub_publisher_surface_is_exposed_through_protected_hub_api_seam()
    {
        string infoEndpointsPath = FindPath("Chummer.Api", "Endpoints", "InfoEndpoints.cs");
        string infoEndpointsText = File.ReadAllText(infoEndpointsPath);
        string hubPublisherEndpointsPath = FindPath("Chummer.Api", "Endpoints", "HubPublisherEndpoints.cs");
        string hubPublisherEndpointsText = File.ReadAllText(hubPublisherEndpointsPath);
        string hubPublisherStoreContractPath = FindPath("Chummer.Application", "Hub", "IHubPublisherStore.cs");
        string hubPublisherStoreContractText = File.ReadAllText(hubPublisherStoreContractPath);
        string hubPublisherServiceContractPath = FindPath("Chummer.Application", "Hub", "IHubPublisherService.cs");
        string hubPublisherServiceContractText = File.ReadAllText(hubPublisherServiceContractPath);
        string hubPublisherServicePath = FindPath("Chummer.Application", "Hub", "DefaultHubPublisherService.cs");
        string hubPublisherServiceText = File.ReadAllText(hubPublisherServicePath);
        string hubPublisherContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubPublisherContracts.cs");
        string hubPublisherContractsText = File.ReadAllText(hubPublisherContractsPath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(infoEndpointsText, "/api/hub/publishers");
        StringAssert.Contains(infoEndpointsText, "/api/hub/publishers/{publisherId}");
        StringAssert.Contains(hubPublisherEndpointsText, "public static class HubPublisherEndpoints");
        StringAssert.Contains(hubPublisherEndpointsText, "MapGet(\"/api/hub/publishers\"");
        StringAssert.Contains(hubPublisherEndpointsText, "MapGet(\"/api/hub/publishers/{publisherId}\"");
        StringAssert.Contains(hubPublisherEndpointsText, "MapPut(\"/api/hub/publishers/{publisherId}\"");
        StringAssert.Contains(hubPublisherEndpointsText, "IHubPublisherService");
        StringAssert.Contains(hubPublisherStoreContractText, "public interface IHubPublisherStore");
        StringAssert.Contains(hubPublisherStoreContractText, "HubPublisherRecord? Get(OwnerScope owner, string publisherId)");
        StringAssert.Contains(hubPublisherStoreContractText, "HubPublisherRecord Upsert");
        StringAssert.Contains(hubPublisherServiceContractText, "public interface IHubPublisherService");
        StringAssert.Contains(hubPublisherServiceContractText, "HubPublicationResult<HubPublisherCatalog> ListPublishers");
        StringAssert.Contains(hubPublisherServiceContractText, "HubPublicationResult<HubPublisherProfile?> GetPublisher");
        StringAssert.Contains(hubPublisherServiceContractText, "HubPublicationResult<HubPublisherProfile> UpsertPublisher");
        StringAssert.Contains(hubPublisherServiceText, "public sealed class DefaultHubPublisherService : IHubPublisherService");
        StringAssert.Contains(hubPublisherServiceText, "HubPublisherVerificationStates.Unverified");
        StringAssert.Contains(hubPublisherContractsText, "public static class HubPublisherVerificationStates");
        StringAssert.Contains(hubPublisherContractsText, "public sealed record HubUpdatePublisherRequest");
        StringAssert.Contains(hubPublisherContractsText, "public sealed record HubPublisherProfile");
        StringAssert.Contains(hubPublisherContractsText, "public sealed record HubPublisherCatalog");
        StringAssert.Contains(hubPublisherContractsText, "public sealed record HubPublisherSummary");
        StringAssert.Contains(hubPublisherContractsText, "public sealed record HubPublisherRecord");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IHubPublisherStore>(_ => new FileHubPublisherStore(stateDirectory))");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IHubPublisherService, DefaultHubPublisherService>()");
        StringAssert.Contains(readmeText, "/api/hub/publishers/*");
    }

    [TestMethod]
    public void Hub_review_surface_is_exposed_through_protected_hub_api_seam()
    {
        string infoEndpointsPath = FindPath("Chummer.Api", "Endpoints", "InfoEndpoints.cs");
        string infoEndpointsText = File.ReadAllText(infoEndpointsPath);
        string hubReviewEndpointsPath = FindPath("Chummer.Api", "Endpoints", "HubReviewEndpoints.cs");
        string hubReviewEndpointsText = File.ReadAllText(hubReviewEndpointsPath);
        string hubReviewStoreContractPath = FindPath("Chummer.Application", "Hub", "IHubReviewStore.cs");
        string hubReviewStoreContractText = File.ReadAllText(hubReviewStoreContractPath);
        string hubReviewServiceContractPath = FindPath("Chummer.Application", "Hub", "IHubReviewService.cs");
        string hubReviewServiceContractText = File.ReadAllText(hubReviewServiceContractPath);
        string hubReviewServicePath = FindPath("Chummer.Application", "Hub", "DefaultHubReviewService.cs");
        string hubReviewServiceText = File.ReadAllText(hubReviewServicePath);
        string hubReviewContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubReviewContracts.cs");
        string hubReviewContractsText = File.ReadAllText(hubReviewContractsPath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(infoEndpointsText, "/api/hub/reviews");
        StringAssert.Contains(infoEndpointsText, "/api/hub/reviews/{kind}/{itemId}");
        StringAssert.Contains(hubReviewEndpointsText, "public static class HubReviewEndpoints");
        StringAssert.Contains(hubReviewEndpointsText, "MapGet(\"/api/hub/reviews\"");
        StringAssert.Contains(hubReviewEndpointsText, "MapPut(\"/api/hub/reviews/{kind}/{itemId}\"");
        StringAssert.Contains(hubReviewEndpointsText, "IHubReviewService");
        StringAssert.Contains(hubReviewEndpointsText, "hub_project_kind_invalid");
        StringAssert.Contains(hubReviewEndpointsText, "ValidateProjectKindOptional");
        StringAssert.Contains(hubReviewEndpointsText, "ValidateProjectKindRequired");
        StringAssert.Contains(hubReviewEndpointsText, "allowedKinds = HubCatalogItemKinds.All");
        StringAssert.Contains(hubReviewStoreContractText, "public interface IHubReviewStore");
        StringAssert.Contains(hubReviewStoreContractText, "IReadOnlyList<HubReviewRecord> List");
        StringAssert.Contains(hubReviewStoreContractText, "IReadOnlyList<HubReviewRecord> ListAll");
        StringAssert.Contains(hubReviewStoreContractText, "HubReviewRecord? Get(OwnerScope owner, string kind, string itemId, string rulesetId)");
        StringAssert.Contains(hubReviewStoreContractText, "HubReviewRecord Upsert");
        StringAssert.Contains(hubReviewServiceContractText, "public interface IHubReviewService");
        StringAssert.Contains(hubReviewServiceContractText, "HubPublicationResult<HubReviewCatalog> ListReviews");
        StringAssert.Contains(hubReviewServiceContractText, "HubPublicationResult<HubReviewAggregateSummary> GetAggregateSummary");
        StringAssert.Contains(hubReviewServiceContractText, "HubPublicationResult<HubReviewReceipt> UpsertReview");
        StringAssert.Contains(hubReviewServiceText, "public sealed class DefaultHubReviewService : IHubReviewService");
        StringAssert.Contains(hubReviewServiceText, "HubCatalogItemKinds.NormalizeOptional(kind)");
        StringAssert.Contains(hubReviewServiceText, "HubCatalogItemKinds.NormalizeRequired(kind");
        StringAssert.Contains(hubReviewServiceText, "HubRecommendationStates.Recommended");
        StringAssert.Contains(hubReviewContractsText, "public static class HubRecommendationStates");
        StringAssert.Contains(hubReviewContractsText, "public sealed record HubUpsertReviewRequest");
        StringAssert.Contains(hubReviewContractsText, "public sealed record HubReviewReceipt");
        StringAssert.Contains(hubReviewContractsText, "public sealed record HubReviewCatalog");
        StringAssert.Contains(hubReviewContractsText, "public sealed record HubReviewAggregateSummary");
        StringAssert.Contains(hubReviewContractsText, "public sealed record HubReviewRecord");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IHubReviewStore>(_ => new FileHubReviewStore(stateDirectory))");
        StringAssert.Contains(serviceRegistrationText, "AddSingleton<IHubReviewService, DefaultHubReviewService>()");
        StringAssert.Contains(readmeText, "/api/hub/reviews/*");
    }

    [TestMethod]
    public void Hub_project_kind_validation_flows_through_shared_registry()
    {
        string hubCatalogContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubCatalogContracts.cs");
        string hubCatalogContractsText = File.ReadAllText(hubCatalogContractsPath);
        string hubCatalogEndpointsPath = FindPath("Chummer.Api", "Endpoints", "HubCatalogEndpoints.cs");
        string hubCatalogEndpointsText = File.ReadAllText(hubCatalogEndpointsPath);
        string hubPublicationEndpointsPath = FindPath("Chummer.Api", "Endpoints", "HubPublicationEndpoints.cs");
        string hubPublicationEndpointsText = File.ReadAllText(hubPublicationEndpointsPath);
        string hubReviewEndpointsPath = FindPath("Chummer.Api", "Endpoints", "HubReviewEndpoints.cs");
        string hubReviewEndpointsText = File.ReadAllText(hubReviewEndpointsPath);
        string hubCatalogServicePath = FindPath("Chummer.Application", "Hub", "DefaultHubCatalogService.cs");
        string hubCatalogServiceText = File.ReadAllText(hubCatalogServicePath);
        string hubInstallPreviewServicePath = FindPath("Chummer.Application", "Hub", "DefaultHubInstallPreviewService.cs");
        string hubInstallPreviewServiceText = File.ReadAllText(hubInstallPreviewServicePath);
        string hubCompatibilityServicePath = FindPath("Chummer.Application", "Hub", "DefaultHubProjectCompatibilityService.cs");
        string hubCompatibilityServiceText = File.ReadAllText(hubCompatibilityServicePath);
        string hubPublicationServicePath = FindPath("Chummer.Application", "Hub", "DefaultHubPublicationService.cs");
        string hubPublicationServiceText = File.ReadAllText(hubPublicationServicePath);
        string hubReviewServicePath = FindPath("Chummer.Application", "Hub", "DefaultHubReviewService.cs");
        string hubReviewServiceText = File.ReadAllText(hubReviewServicePath);

        StringAssert.Contains(hubCatalogContractsText, "public static IReadOnlyList<string> All");
        StringAssert.Contains(hubCatalogContractsText, "public static bool IsDefined");
        StringAssert.Contains(hubCatalogContractsText, "public static string NormalizeRequired");
        StringAssert.Contains(hubCatalogContractsText, "public static string? NormalizeOptional");
        StringAssert.Contains(hubCatalogEndpointsText, "ValidateProjectKind(kind)");
        StringAssert.Contains(hubPublicationEndpointsText, "ValidateProjectKindOptional");
        StringAssert.Contains(hubPublicationEndpointsText, "ValidateProjectKindRequired");
        StringAssert.Contains(hubReviewEndpointsText, "ValidateProjectKindOptional");
        StringAssert.Contains(hubReviewEndpointsText, "ValidateProjectKindRequired");
        StringAssert.Contains(hubCatalogServiceText, "HubCatalogItemKinds.NormalizeRequired(kind)");
        StringAssert.Contains(hubInstallPreviewServiceText, "HubCatalogItemKinds.NormalizeRequired(kind)");
        StringAssert.Contains(hubCompatibilityServiceText, "HubCatalogItemKinds.NormalizeRequired(kind)");
        StringAssert.Contains(hubPublicationServiceText, "HubCatalogItemKinds.NormalizeOptional(kind)");
        StringAssert.Contains(hubPublicationServiceText, "HubCatalogItemKinds.NormalizeRequired(request.ProjectKind");
        StringAssert.Contains(hubReviewServiceText, "HubCatalogItemKinds.NormalizeOptional(kind)");
        StringAssert.Contains(hubReviewServiceText, "HubCatalogItemKinds.NormalizeRequired(kind");
        Assert.IsFalse(hubCatalogServiceText.Contains("kind.Trim() switch", StringComparison.Ordinal));
        Assert.IsFalse(hubInstallPreviewServiceText.Contains("kind.Trim() switch", StringComparison.Ordinal));
        Assert.IsFalse(hubCompatibilityServiceText.Contains("kind.Trim() switch", StringComparison.Ordinal));
        Assert.IsFalse(hubPublicationServiceText.Contains("NormalizeKindRequired(", StringComparison.Ordinal));
        Assert.IsFalse(hubPublicationServiceText.Contains("NormalizeKindOptional(", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Hosted_ai_and_hub_contract_families_live_under_run_contract_project()
    {
        string coreContractsProjectPath = FindPath("Chummer.Contracts", "Chummer.Contracts.csproj");
        string coreContractsProjectText = File.ReadAllText(coreContractsProjectPath);
        string aiGatewayContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiGatewayContracts.cs");
        string hubCatalogContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubCatalogContracts.cs");
        string coreAiDirectory = FindDirectory("Chummer.Run.Contracts", "AI");
        string coreHubDirectory = FindDirectory("Chummer.Run.Contracts", "Hub");

        StringAssert.Contains(coreContractsProjectText, "<AssemblyName>Chummer.Engine.Contracts</AssemblyName>");
        Assert.IsTrue(Directory.EnumerateFiles(coreAiDirectory, "*.cs", SearchOption.TopDirectoryOnly).Any(), "Run contracts should include hosted AI contract files.");
        Assert.IsTrue(Directory.EnumerateFiles(coreHubDirectory, "*.cs", SearchOption.TopDirectoryOnly).Any(), "Run contracts should include hosted Hub contract files.");
        StringAssert.Contains(File.ReadAllText(aiGatewayContractsPath), "namespace Chummer.Contracts.AI;");
        StringAssert.Contains(File.ReadAllText(hubCatalogContractsPath), "namespace Chummer.Contracts.Hub;");
    }

    [TestMethod]
    public void Removed_ruleset_normalize_helper_is_not_referenced_anywhere()
    {
        string repoRoot = Path.GetDirectoryName(FindPath("Chummer.sln"))
            ?? throw new InvalidOperationException("Could not resolve repository root from solution path.");
        List<string> offenders = Directory.EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.EndsWith($"{Path.DirectorySeparatorChar}MigrationComplianceTests.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("RulesetDefaults.Normalize(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.IsEmpty(offenders, "Removed ruleset normalize helper is still referenced in: " + string.Join(", ", offenders));
    }

    [TestMethod]
    public void Project_paths_use_matching_primary_namespaces_for_application_hosting_and_infrastructure()
    {
        AssertProjectNamespacesMatch("Chummer.Application", "Chummer.Application");
        AssertProjectNamespacesMatch("Chummer.Rulesets.Hosting", "Chummer.Rulesets.Hosting");
        AssertProjectNamespacesMatch("Chummer.Infrastructure", "Chummer.Infrastructure");
    }

    [TestMethod]
    public void Public_api_bypass_is_driven_by_endpoint_metadata_instead_of_path_prefixes()
    {
        string apiProgramPath = FindPath("Chummer.Api", "Program.cs");
        string apiProgramText = File.ReadAllText(apiProgramPath);
        string publicApiMetadataPath = FindPath("Chummer.Api", "Endpoints", "PublicApiEndpointMetadata.cs");
        string publicApiMetadataText = File.ReadAllText(publicApiMetadataPath);
        string infoEndpointsPath = FindPath("Chummer.Api", "Endpoints", "InfoEndpoints.cs");
        string infoEndpointsText = File.ReadAllText(infoEndpointsPath);
        string hubEndpointsPath = FindPath("Chummer.Api", "Endpoints", "HubCatalogEndpoints.cs");
        string hubEndpointsText = File.ReadAllText(hubEndpointsPath);
        string rulePackEndpointsPath = FindPath("Chummer.Api", "Endpoints", "RulePackRegistryEndpoints.cs");
        string rulePackEndpointsText = File.ReadAllText(rulePackEndpointsPath);
        string ruleProfileEndpointsPath = FindPath("Chummer.Api", "Endpoints", "RuleProfileRegistryEndpoints.cs");
        string ruleProfileEndpointsText = File.ReadAllText(ruleProfileEndpointsPath);
        string runtimeInspectorEndpointsPath = FindPath("Chummer.Api", "Endpoints", "RuntimeInspectorEndpoints.cs");
        string runtimeInspectorEndpointsText = File.ReadAllText(runtimeInspectorEndpointsPath);
        string runtimeLockEndpointsPath = FindPath("Chummer.Api", "Endpoints", "RuntimeLockRegistryEndpoints.cs");
        string runtimeLockEndpointsText = File.ReadAllText(runtimeLockEndpointsPath);
        string hubPublisherEndpointsPath = FindPath("Chummer.Api", "Endpoints", "HubPublisherEndpoints.cs");
        string hubPublisherEndpointsText = File.ReadAllText(hubPublisherEndpointsPath);
        string hubReviewEndpointsPath = FindPath("Chummer.Api", "Endpoints", "HubReviewEndpoints.cs");
        string hubReviewEndpointsText = File.ReadAllText(hubReviewEndpointsPath);
        string hubPublicationEndpointsPath = FindPath("Chummer.Api", "Endpoints", "HubPublicationEndpoints.cs");
        string hubPublicationEndpointsText = File.ReadAllText(hubPublicationEndpointsPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(apiProgramText, "app.UseRouting();");
        StringAssert.Contains(apiProgramText, "AllowsPublicApiKeyBypass(context)");
        StringAssert.Contains(apiProgramText, "PublicApiEndpointMetadata");
        Assert.IsFalse(apiProgramText.Contains("IsPublicApiPath(", StringComparison.Ordinal));
        StringAssert.Contains(publicApiMetadataText, "internal sealed class PublicApiEndpointMetadata");
        StringAssert.Contains(publicApiMetadataText, "AllowPublicApiKeyBypass");
        StringAssert.Contains(infoEndpointsText, "AllowPublicApiKeyBypass()");
        StringAssert.Contains(hubEndpointsText, "AllowPublicApiKeyBypass()");
        Assert.AreEqual(0, Regex.Count(hubPublisherEndpointsText, "AllowPublicApiKeyBypass\\(\\)"), "Hub publisher routes must stay protected until public publisher projections exist.");
        Assert.AreEqual(0, Regex.Count(hubReviewEndpointsText, "AllowPublicApiKeyBypass\\(\\)"), "Hub review routes must stay protected until public review aggregation exists.");
        Assert.AreEqual(0, Regex.Count(hubPublicationEndpointsText, "AllowPublicApiKeyBypass\\(\\)"), "Hub publication and moderation routes must stay protected.");
        Assert.AreEqual(2, Regex.Count(rulePackEndpointsText, "AllowPublicApiKeyBypass\\(\\)"), "RulePack registry should expose only list/detail as public metadata endpoints.");
        Assert.AreEqual(3, Regex.Count(ruleProfileEndpointsText, "AllowPublicApiKeyBypass\\(\\)"), "RuleProfile registry should expose list/detail/preview as public metadata endpoints.");
        Assert.AreEqual(1, Regex.Count(runtimeInspectorEndpointsText, "AllowPublicApiKeyBypass\\(\\)"), "Runtime inspector should expose only the profile projection as public metadata.");
        Assert.AreEqual(2, Regex.Count(runtimeLockEndpointsText, "AllowPublicApiKeyBypass\\(\\)"), "Runtime lock registry should expose only list/detail as public metadata endpoints.");
        StringAssert.Contains(rulePackEndpointsText, "\"/api/rulepacks/{packId}/install-preview\"");
        StringAssert.Contains(rulePackEndpointsText, "\"/api/rulepacks/{packId}/install\"");
        StringAssert.Contains(ruleProfileEndpointsText, "\"/api/profiles/{profileId}/preview\"");
        StringAssert.Contains(ruleProfileEndpointsText, "\"/api/profiles/{profileId}/apply\"");
        StringAssert.Contains(runtimeLockEndpointsText, "\"/api/runtime/locks/{lockId}/install-preview\"");
        StringAssert.Contains(runtimeLockEndpointsText, "\"/api/runtime/locks/{lockId}/install\"");
        StringAssert.Contains(hubPublisherEndpointsText, "\"/api/hub/publishers\"");
        StringAssert.Contains(hubPublisherEndpointsText, "\"/api/hub/publishers/{publisherId}\"");
        StringAssert.Contains(hubReviewEndpointsText, "\"/api/hub/reviews\"");
        StringAssert.Contains(hubReviewEndpointsText, "\"/api/hub/reviews/{kind}/{itemId}\"");
        StringAssert.Contains(hubPublicationEndpointsText, "\"/api/hub/publish/drafts\"");
        StringAssert.Contains(hubPublicationEndpointsText, "\"/api/hub/publish/{kind}/{itemId}/submit\"");
        StringAssert.Contains(hubPublicationEndpointsText, "\"/api/hub/moderation/queue\"");
        StringAssert.Contains(hubPublicationEndpointsText, "\"/api/hub/moderation/queue/{caseId}/approve\"");
        StringAssert.Contains(hubPublicationEndpointsText, "\"/api/hub/moderation/queue/{caseId}/reject\"");
        StringAssert.Contains(readmeText, "explicit endpoint metadata");
        StringAssert.Contains(readmeText, "protected and are not exposed through prefix-based allowlists");
    }

    [TestMethod]
    public void Solution_includes_headless_and_dual_head_projects()
    {
        string solutionPath = FindPath("Chummer.sln");
        string solutionText = File.ReadAllText(solutionPath);

        string[] requiredProjects =
        {
            @"Chummer.Api\Chummer.Api.csproj",
            @"Chummer.Application\Chummer.Application.csproj",
            @"Chummer.Contracts\Chummer.Contracts.csproj",
            @"Chummer.Desktop.Runtime\Chummer.Desktop.Runtime.csproj",
            @"Chummer.Hub.Web\Chummer.Hub.Web.csproj",
            @"Chummer.Infrastructure.Browser\Chummer.Infrastructure.Browser.csproj",
            @"Chummer.Session.Web\Chummer.Session.Web.csproj",
            @"Chummer.Coach.Web\Chummer.Coach.Web.csproj",
            @"Chummer.Infrastructure\Chummer.Infrastructure.csproj",
            @"Chummer.Presentation\Chummer.Presentation.csproj",
            @"Chummer.Portal\Chummer.Portal.csproj",
            @"Chummer.Avalonia\Chummer.Avalonia.csproj",
            @"Chummer.Avalonia.Browser\Chummer.Avalonia.Browser.csproj",
            @"Chummer.Blazor\Chummer.Blazor.csproj",
            @"Chummer.Blazor.Desktop\Chummer.Blazor.Desktop.csproj",
            @"Chummer.Rulesets.Hosting\Chummer.Rulesets.Hosting.csproj",
            @"Chummer.Rulesets.Sr4\Chummer.Rulesets.Sr4.csproj",
            @"Chummer.Rulesets.Sr5\Chummer.Rulesets.Sr5.csproj",
            @"Chummer.Rulesets.Sr6\Chummer.Rulesets.Sr6.csproj"
        };

        foreach (string requiredProject in requiredProjects)
        {
            StringAssert.Contains(solutionText, requiredProject, "Missing solution entry: " + requiredProject);
        }
    }

    [TestMethod]
    public void Docker_compose_exposes_blazor_head_with_api_dependency()
    {
        string projectPath = FindPath("Chummer.Blazor", "Chummer.Blazor.csproj");
        string projectText = File.ReadAllText(projectPath);
        string programPath = FindPath("Chummer.Blazor", "Program.cs");
        string programText = File.ReadAllText(programPath);
        string hubProjectPath = FindPath("Chummer.Hub.Web", "Chummer.Hub.Web.csproj");
        string hubProjectText = File.ReadAllText(hubProjectPath);
        string hubProgramPath = FindPath("Chummer.Hub.Web", "Program.cs");
        string hubProgramText = File.ReadAllText(hubProgramPath);
        string sessionProjectPath = FindPath("Chummer.Session.Web", "Chummer.Session.Web.csproj");
        string sessionProjectText = File.ReadAllText(sessionProjectPath);
        string sessionProgramPath = FindPath("Chummer.Session.Web", "Program.cs");
        string sessionProgramText = File.ReadAllText(sessionProgramPath);
        string sessionDockerfilePath = FindPath("Chummer.Session.Web", "Dockerfile");
        string sessionDockerfileText = File.ReadAllText(sessionDockerfilePath);
        string coachProjectPath = FindPath("Chummer.Coach.Web", "Chummer.Coach.Web.csproj");
        string coachProjectText = File.ReadAllText(coachProjectPath);
        string coachProgramPath = FindPath("Chummer.Coach.Web", "Program.cs");
        string coachProgramText = File.ReadAllText(coachProgramPath);
        string coachDockerfilePath = FindPath("Chummer.Coach.Web", "Dockerfile");
        string coachDockerfileText = File.ReadAllText(coachDockerfilePath);
        string browserInfrastructureProjectPath = FindPath("Chummer.Infrastructure.Browser", "Chummer.Infrastructure.Browser.csproj");
        string browserInfrastructureProjectText = File.ReadAllText(browserInfrastructureProjectPath);
        string composePath = TryFindPath("docker-compose.yml") ?? "/src/docker-compose.yml";
        Assert.IsTrue(File.Exists(composePath), $"Could not locate docker compose file at '{composePath}'.");
        string composeText = File.ReadAllText(composePath);
        string migrationLoopPath = FindPath("scripts", "migration-loop.sh");
        string migrationLoopText = File.ReadAllText(migrationLoopPath);
        string apiIntegrationTestsPath = FindPath("Chummer.Tests", "ApiIntegrationTests.cs");
        string apiIntegrationTestsText = File.ReadAllText(apiIntegrationTestsPath);
        string dualHeadTestsPath = FindPath("Chummer.Tests", "Presentation", "DualHeadAcceptanceTests.cs");
        string dualHeadTestsText = File.ReadAllText(dualHeadTestsPath);

        StringAssert.Contains(projectText, "<Project Sdk=\"Microsoft.NET.Sdk.Web\">");
        StringAssert.Contains(programText, "AddRazorComponents()");
        StringAssert.Contains(programText, "CHUMMER_API_BASE_URL");
        StringAssert.Contains(programText, "http://chummer-api:8080");
        StringAssert.Contains(hubProjectText, "<Project Sdk=\"Microsoft.NET.Sdk.Web\">");
        StringAssert.Contains(hubProgramText, "AddRazorComponents()");
        StringAssert.Contains(hubProgramText, "CHUMMER_HUB_PATH_BASE");
        StringAssert.Contains(hubProgramText, "head = \"hub-web\"");
        StringAssert.Contains(sessionProjectText, "<Project Sdk=\"Microsoft.NET.Sdk.Web\">");
        StringAssert.Contains(sessionProjectText, @"..\Chummer.Infrastructure.Browser\Chummer.Infrastructure.Browser.csproj");
        StringAssert.Contains(sessionProgramText, "AddRazorComponents()");
        StringAssert.Contains(sessionProgramText, "AddBrowserSessionOfflineStorage(builder.Services)");
        StringAssert.Contains(sessionProgramText, "CHUMMER_SESSION_PATH_BASE");
        StringAssert.Contains(sessionProgramText, "head = \"session-web\"");
        StringAssert.Contains(sessionDockerfileText, "COPY Chummer.Infrastructure.Browser/Chummer.Infrastructure.Browser.csproj Chummer.Infrastructure.Browser/");
        StringAssert.Contains(sessionDockerfileText, "COPY Chummer.Infrastructure.Browser/ Chummer.Infrastructure.Browser/");
        StringAssert.Contains(coachProjectText, "<Project Sdk=\"Microsoft.NET.Sdk.Web\">");
        StringAssert.Contains(coachProgramText, "AddRazorComponents()");
        StringAssert.Contains(coachProgramText, "CHUMMER_COACH_PATH_BASE");
        StringAssert.Contains(coachProgramText, "head = \"coach-web\"");
        StringAssert.Contains(coachDockerfileText, "COPY Chummer.Coach.Web/Chummer.Coach.Web.csproj Chummer.Coach.Web/");
        StringAssert.Contains(coachDockerfileText, "COPY Chummer.Coach.Web/ Chummer.Coach.Web/");
        StringAssert.Contains(browserInfrastructureProjectText, @"..\Chummer.Application\Chummer.Application.csproj");
        StringAssert.Contains(browserInfrastructureProjectText, "Microsoft.JSInterop");
        StringAssert.Contains(composeText, "chummer-hub-web:");
        StringAssert.Contains(composeText, "chummer-hub-web-portal:");
        StringAssert.Contains(composeText, "chummer-session-web:");
        StringAssert.Contains(composeText, "chummer-session-web-portal:");
        StringAssert.Contains(composeText, "chummer-coach-web:");
        StringAssert.Contains(composeText, "chummer-coach-web-portal:");
        StringAssert.Contains(composeText, "CHUMMER_HUB_PATH_BASE");
        StringAssert.Contains(composeText, "CHUMMER_PORTAL_HUB_PROXY_URL");
        StringAssert.Contains(composeText, "CHUMMER_SESSION_PATH_BASE");
        StringAssert.Contains(composeText, "CHUMMER_PORTAL_SESSION_PROXY_URL");
        StringAssert.Contains(composeText, "CHUMMER_COACH_PATH_BASE");
        StringAssert.Contains(composeText, "CHUMMER_PORTAL_COACH_PROXY_URL");
        StringAssert.Contains(composeText, "CHUMMER_PORTAL_AI_PROXY_URL");
        StringAssert.Contains(composeText, "CHUMMER_RUN_URL");
        StringAssert.Contains(composeText, "CHUMMER_AI_AIMAGICX_PRIMARY_API_KEY");
        StringAssert.Contains(composeText, "CHUMMER_AI_AIMAGICX_FALLBACK_API_KEY");
        StringAssert.Contains(composeText, "CHUMMER_AI_1MINAI_PRIMARY_API_KEY");
        StringAssert.Contains(composeText, "CHUMMER_AI_1MINAI_FALLBACK_API_KEY");
        StringAssert.Contains(composeText, "CHUMMER_AI_ENABLE_REMOTE_EXECUTION");
        StringAssert.Contains(composeText, "CHUMMER_AI_AIMAGICX_BASE_URL");
        StringAssert.Contains(composeText, "CHUMMER_AI_AIMAGICX_MODEL");
        StringAssert.Contains(composeText, "CHUMMER_AI_1MINAI_BASE_URL");
        StringAssert.Contains(composeText, "CHUMMER_AI_1MINAI_MODEL");
        StringAssert.Contains(apiIntegrationTestsText, "http://chummer-api:8080");
        StringAssert.Contains(dualHeadTestsText, "http://chummer-api:8080");
        StringAssert.Contains(migrationLoopText, "docker compose up -d --build --remove-orphans chummer-api chummer-blazor");
        Assert.IsFalse(
            migrationLoopText.Contains("--profile ui up", StringComparison.Ordinal),
            "Migration loop must not require the ui profile to start chummer-blazor.");
    }

    [TestMethod]
    public void Session_web_head_uses_browser_fetch_for_owner_backed_session_routes()
    {
        string sessionProgramPath = FindPath("Chummer.Session.Web", "Program.cs");
        string sessionProgramText = File.ReadAllText(sessionProgramPath);
        string sessionAppPath = FindPath("Chummer.Session.Web", "Components", "App.razor");
        string sessionAppText = File.ReadAllText(sessionAppPath);
        string sessionPagePath = FindPath("Chummer.Session.Web", "Components", "Pages", "Home.razor");
        string sessionPageText = File.ReadAllText(sessionPagePath);
        string sessionPageCodePath = FindPath("Chummer.Session.Web", "Components", "Pages", "Home.razor.cs");
        string sessionPageCodeText = File.ReadAllText(sessionPageCodePath);
        string sessionClientPath = FindPath("Chummer.Session.Web", "BrowserSessionApiClient.cs");
        string sessionClientText = File.ReadAllText(sessionClientPath);
        string sessionCoachClientPath = FindPath("Chummer.Session.Web", "BrowserSessionCoachApiClient.cs");
        string sessionCoachClientText = File.ReadAllText(sessionCoachClientPath);
        string sessionScriptPath = FindPath("Chummer.Session.Web", "wwwroot", "session-api.js");
        string sessionScriptText = File.ReadAllText(sessionScriptPath);
        string storageScriptPath = FindPath("Chummer.Session.Web", "wwwroot", "session-storage.js");
        string storageScriptText = File.ReadAllText(storageScriptPath);
        string sessionClientStorageContractsPath = FindPath("Chummer.Contracts", "Session", "SessionClientStorageContracts.cs");
        string sessionClientStorageContractsText = File.ReadAllText(sessionClientStorageContractsPath);
        string sessionReplicaContractsPath = FindPath("Chummer.Contracts", "Session", "SessionReplicaContracts.cs");
        string sessionReplicaContractsText = File.ReadAllText(sessionReplicaContractsPath);
        string browseCacheStoreContractPath = FindPath("Chummer.Application", "Session", "IBrowseCacheStore.cs");
        string browseCacheStoreContractText = File.ReadAllText(browseCacheStoreContractPath);
        string clientStorageQuotaServicePath = FindPath("Chummer.Application", "Session", "IClientStorageQuotaService.cs");
        string clientStorageQuotaServiceText = File.ReadAllText(clientStorageQuotaServicePath);
        string sessionRuntimeBundleCacheStorePath = FindPath("Chummer.Application", "Session", "ISessionRuntimeBundleCacheStore.cs");
        string sessionRuntimeBundleCacheStoreText = File.ReadAllText(sessionRuntimeBundleCacheStorePath);
        string browserOfflineCacheServicePath = FindPath("Chummer.Infrastructure.Browser", "Storage", "BrowserSessionOfflineCacheService.cs");
        string browserOfflineCacheServiceText = File.ReadAllText(browserOfflineCacheServicePath);
        string coachLaunchContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiCoachLaunchContracts.cs");
        string coachLaunchContractsText = File.ReadAllText(coachLaunchContractsPath);
        string sessionComponentTestsPath = FindPath("Chummer.Tests", "Presentation", "SessionWebComponentTests.cs");
        string sessionComponentTestsText = File.ReadAllText(sessionComponentTestsPath);

        StringAssert.Contains(sessionProgramText, "AddScoped<BrowserSessionApiClient>()");
        StringAssert.Contains(sessionProgramText, "AddScoped<BrowserSessionCoachApiClient>()");
        StringAssert.Contains(sessionAppText, "session-api.js");
        StringAssert.Contains(sessionAppText, "session-storage.js");
        StringAssert.Contains(sessionPageText, "Session Runtime Console");
        StringAssert.Contains(sessionPageText, "Coach Sidecar");
        StringAssert.Contains(sessionPageText, "Grounded Guidance");
        StringAssert.Contains(sessionPageText, "Recent Coach Guidance");
        StringAssert.Contains(sessionPageText, "data-testid=\"open-session-coach\"");
        StringAssert.Contains(sessionPageText, "data-testid=\"open-session-coach-thread\"");
        StringAssert.Contains(sessionPageText, "data-testid=\"session-coach-audit-flavor\"");
        StringAssert.Contains(sessionPageText, "data-testid=\"session-coach-audit-budget\"");
        StringAssert.Contains(sessionPageText, "data-testid=\"session-coach-audit-structured\"");
        StringAssert.Contains(sessionPageText, "Saved Characters");
        StringAssert.Contains(sessionPageText, "Apply To Character");
        StringAssert.Contains(sessionPageText, "Issue Runtime Bundle");
        StringAssert.Contains(sessionPageText, "RulePack Inventory");
        StringAssert.Contains(sessionPageText, "Offline Cache");
        StringAssert.Contains(sessionPageText, "Local Overlay Actions");
        StringAssert.Contains(sessionPageText, "Queue Tracker +1");
        StringAssert.Contains(sessionPageText, "Queue Note");
        StringAssert.Contains(sessionPageText, "Queue Pin");
        StringAssert.Contains(sessionPageText, "Recent Local Events");
        StringAssert.Contains(sessionPageText, "Overlay Key");
        StringAssert.Contains(sessionPageText, "Ledger Cache");
        StringAssert.Contains(sessionPageText, "Replica Cache");
        StringAssert.Contains(sessionClientText, "IJSRuntime");
        StringAssert.Contains(sessionClientText, "ListCharactersAsync");
        StringAssert.Contains(sessionClientText, "/api/session/characters");
        StringAssert.Contains(sessionClientText, "/api/session/profiles");
        StringAssert.Contains(sessionClientText, "/api/session/characters/{Uri.EscapeDataString(characterId)}/runtime-state");
        StringAssert.Contains(sessionClientText, "/api/session/characters/{Uri.EscapeDataString(characterId)}/runtime-bundle");
        StringAssert.Contains(sessionClientText, "/api/session/characters/{Uri.EscapeDataString(characterId)}/runtime-bundle/refresh");
        StringAssert.Contains(sessionClientText, "/api/session/characters/{Uri.EscapeDataString(characterId)}/profile");
        StringAssert.Contains(sessionClientText, "/api/session/rulepacks");
        StringAssert.Contains(sessionCoachClientText, "IJSRuntime");
        StringAssert.Contains(sessionCoachClientText, "/api/ai/status");
        StringAssert.Contains(sessionCoachClientText, "/api/ai/provider-health");
        StringAssert.Contains(sessionCoachClientText, "(\"routeType\", routeType)");
        StringAssert.Contains(sessionCoachClientText, "/api/ai/conversation-audits");
        StringAssert.Contains(sessionCoachClientText, "chummerSessionApi.send");
        StringAssert.Contains(sessionPageCodeText, "BuildCoachLaunchUri");
        StringAssert.Contains(sessionPageCodeText, "AiCoachLaunchQuery.BuildRelativeUri");
        StringAssert.Contains(sessionScriptText, "credentials: \"same-origin\"");
        StringAssert.Contains(sessionScriptText, "fetch(path, options)");
        StringAssert.Contains(storageScriptText, "indexedDB.open");
        StringAssert.Contains(storageScriptText, "navigator.storage");
        StringAssert.Contains(storageScriptText, "session-ledgers");
        StringAssert.Contains(storageScriptText, "session-replicas");
        StringAssert.Contains(sessionClientStorageContractsText, "public interface ISessionOfflineCacheService");
        StringAssert.Contains(sessionClientStorageContractsText, "public sealed record ClientStorageQuotaEstimate");
        StringAssert.Contains(sessionClientStorageContractsText, "public sealed record CachedClientPayload<T>");
        StringAssert.Contains(sessionClientStorageContractsText, "GetLedgerAsync");
        StringAssert.Contains(sessionClientStorageContractsText, "CacheReplicaStateAsync");
        StringAssert.Contains(sessionReplicaContractsText, "public sealed record SessionReplicaState");
        StringAssert.Contains(browseCacheStoreContractText, "public interface IBrowseCacheStore");
        StringAssert.Contains(clientStorageQuotaServiceText, "public interface IClientStorageQuotaService");
        StringAssert.Contains(sessionRuntimeBundleCacheStoreText, "public interface ISessionRuntimeBundleCacheStore");
        StringAssert.Contains(browserOfflineCacheServiceText, "public sealed class BrowserSessionOfflineCacheService");
        StringAssert.Contains(browserOfflineCacheServiceText, "CacheRuntimeBundleAsync");
        StringAssert.Contains(browserOfflineCacheServiceText, "CacheLedgerAsync");
        StringAssert.Contains(browserOfflineCacheServiceText, "CacheReplicaStateAsync");
        StringAssert.Contains(browserOfflineCacheServiceText, "GetStorageQuotaAsync");
        StringAssert.Contains(coachLaunchContractsText, "public sealed record AiCoachLaunchContext");
        StringAssert.Contains(coachLaunchContractsText, "public static class AiCoachLaunchQuery");
        StringAssert.Contains(coachLaunchContractsText, "ConversationId");
        StringAssert.Contains(coachLaunchContractsText, "ConversationIdKey");
        StringAssert.Contains(sessionComponentTestsText, "public sealed class SessionWebComponentTests");
        StringAssert.Contains(sessionComponentTestsText, "Home_renders_live_session_catalog_runtime_state_and_rulepack_inventory");
        StringAssert.Contains(sessionComponentTestsText, "Coach Sidecar");
        StringAssert.Contains(sessionComponentTestsText, "open-session-coach");
        StringAssert.Contains(sessionComponentTestsText, "Home_queues_local_overlay_mutations_into_browser_cache_before_sync_routes_exist");
        StringAssert.Contains(sessionComponentTestsText, "Home_falls_back_to_indexeddb_cache_when_live_session_requests_fail");
        Assert.IsFalse(
            sessionClientText.Contains("HttpClient", StringComparison.Ordinal),
            "Session web head should use browser-side same-origin fetch instead of server-side HttpClient calls.");
        Assert.IsFalse(
            sessionCoachClientText.Contains("HttpClient", StringComparison.Ordinal),
            "Session Coach sidecar should use browser-side same-origin fetch instead of server-side HttpClient calls.");
    }

    [TestMethod]
    public void Hub_web_head_uses_browser_fetch_for_live_hub_catalog_routes()
    {
        string hubProgramPath = FindPath("Chummer.Hub.Web", "Program.cs");
        string hubProgramText = File.ReadAllText(hubProgramPath);
        string hubAppPath = FindPath("Chummer.Hub.Web", "Components", "App.razor");
        string hubAppText = File.ReadAllText(hubAppPath);
        string hubPagePath = FindPath("Chummer.Hub.Web", "Components", "Pages", "Home.razor");
        string hubPageText = File.ReadAllText(hubPagePath);
        string hubPageCodePath = FindPath("Chummer.Hub.Web", "Components", "Pages", "Home.razor.cs");
        string hubPageCodeText = File.ReadAllText(hubPageCodePath);
        string hubClientPath = FindPath("Chummer.Hub.Web", "BrowserHubApiClient.cs");
        string hubClientText = File.ReadAllText(hubClientPath);
        string hubCoachClientPath = FindPath("Chummer.Hub.Web", "BrowserHubCoachApiClient.cs");
        string hubCoachClientText = File.ReadAllText(hubCoachClientPath);
        string hubScriptPath = FindPath("Chummer.Hub.Web", "wwwroot", "hub-api.js");
        string hubScriptText = File.ReadAllText(hubScriptPath);
        string hubComponentTestsPath = FindPath("Chummer.Tests", "Presentation", "HubWebComponentTests.cs");
        string hubComponentTestsText = File.ReadAllText(hubComponentTestsPath);

        StringAssert.Contains(hubProgramText, "AddScoped<BrowserHubApiClient>()");
        StringAssert.Contains(hubProgramText, "AddScoped<BrowserHubCoachApiClient>()");
        StringAssert.Contains(hubAppText, "hub-api.js");
        StringAssert.Contains(hubPageText, "Discover Projects");
        StringAssert.Contains(hubPageText, "Coach Sidecar");
        StringAssert.Contains(hubPageText, "Curation Signals");
        StringAssert.Contains(hubPageText, "Recent Coach Guidance");
        StringAssert.Contains(hubPageText, "data-hub-action=\"open-coach\"");
        StringAssert.Contains(hubPageText, "data-testid=\"hub-coach-provider-transport\"");
        StringAssert.Contains(hubPageText, "data-testid=\"hub-open-coach-thread\"");
        StringAssert.Contains(hubPageText, "data-testid=\"hub-coach-audit-flavor\"");
        StringAssert.Contains(hubPageText, "data-testid=\"hub-coach-audit-budget\"");
        StringAssert.Contains(hubPageText, "data-testid=\"hub-coach-audit-structured\"");
        StringAssert.Contains(hubPageText, "Inspect Project");
        StringAssert.Contains(hubPageText, "Preview Install");
        StringAssert.Contains(hubPageText, "My Drafts");
        StringAssert.Contains(hubPageText, "Create Draft");
        StringAssert.Contains(hubPageText, "Submit Draft");
        StringAssert.Contains(hubPageText, "Archive Draft");
        StringAssert.Contains(hubPageText, "Delete Draft");
        StringAssert.Contains(hubPageText, "Review Queue");
        StringAssert.Contains(hubPageText, "Approve");
        StringAssert.Contains(hubPageText, "Reject");
        StringAssert.Contains(hubClientText, "IJSRuntime");
        StringAssert.Contains(hubClientText, "/api/hub/search");
        StringAssert.Contains(hubClientText, "/api/hub/projects/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(itemId)}");
        StringAssert.Contains(hubClientText, "/api/hub/projects/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(itemId)}/compatibility");
        StringAssert.Contains(hubClientText, "/api/hub/projects/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(itemId)}/install-preview");
        StringAssert.Contains(hubClientText, "/api/hub/publish/drafts");
        StringAssert.Contains(hubClientText, "/api/hub/publish/drafts/{Uri.EscapeDataString(draftId)}");
        StringAssert.Contains(hubClientText, "/api/hub/publish/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(itemId)}/submit");
        StringAssert.Contains(hubClientText, "/api/hub/publish/drafts/{Uri.EscapeDataString(draftId)}/archive");
        StringAssert.Contains(hubClientText, "/api/hub/moderation/queue");
        StringAssert.Contains(hubClientText, "/api/hub/moderation/queue/{Uri.EscapeDataString(caseId)}/approve");
        StringAssert.Contains(hubClientText, "/api/hub/moderation/queue/{Uri.EscapeDataString(caseId)}/reject");
        StringAssert.Contains(hubCoachClientText, "IJSRuntime");
        StringAssert.Contains(hubCoachClientText, "/api/ai/status");
        StringAssert.Contains(hubCoachClientText, "/api/ai/provider-health");
        StringAssert.Contains(hubCoachClientText, "(\"routeType\", routeType)");
        StringAssert.Contains(hubCoachClientText, "/api/ai/conversation-audits");
        StringAssert.Contains(hubCoachClientText, "chummerHubApi.send");
        StringAssert.Contains(hubPageCodeText, "BuildCoachLaunchUri");
        StringAssert.Contains(hubPageCodeText, "AiCoachLaunchQuery.BuildRelativeUri");
        StringAssert.Contains(hubScriptText, "credentials: \"same-origin\"");
        StringAssert.Contains(hubScriptText, "fetch(path, options)");
        StringAssert.Contains(hubComponentTestsText, "public sealed class HubWebComponentTests");
        StringAssert.Contains(hubComponentTestsText, "Home_renders_live_hub_catalog_detail_compatibility_and_install_preview");
        StringAssert.Contains(hubComponentTestsText, "Coach Sidecar");
        StringAssert.Contains(hubComponentTestsText, "open-coach");
        StringAssert.Contains(hubComponentTestsText, "Home_surfaces_hub_search_errors_when_catalog_request_fails");
        StringAssert.Contains(hubComponentTestsText, "Home_loads_owner_backed_hub_drafts_and_draft_detail");
        StringAssert.Contains(hubComponentTestsText, "Home_creates_updates_and_submits_hub_drafts_through_publication_routes");
        StringAssert.Contains(hubComponentTestsText, "Home_archives_deletes_and_lists_moderation_queue_items_through_publication_routes");
        StringAssert.Contains(hubComponentTestsText, "Home_approves_and_rejects_hub_moderation_queue_items_through_publication_routes");
        Assert.IsFalse(
            hubClientText.Contains("HttpClient", StringComparison.Ordinal),
            "Hub web head should use browser-side same-origin fetch instead of server-side HttpClient calls.");
        Assert.IsFalse(
            hubCoachClientText.Contains("HttpClient", StringComparison.Ordinal),
            "Hub Coach sidecar should use browser-side same-origin fetch instead of server-side HttpClient calls.");
    }

    [TestMethod]
    public void Coach_web_head_uses_browser_fetch_for_live_ai_gateway_routes()
    {
        string coachProgramPath = FindPath("Chummer.Coach.Web", "Program.cs");
        string coachProgramText = File.ReadAllText(coachProgramPath);
        string coachAppPath = FindPath("Chummer.Coach.Web", "Components", "App.razor");
        string coachAppText = File.ReadAllText(coachAppPath);
        string coachPagePath = FindPath("Chummer.Coach.Web", "Components", "Pages", "Home.razor");
        string coachPageText = File.ReadAllText(coachPagePath);
        string coachPageCodePath = FindPath("Chummer.Coach.Web", "Components", "Pages", "Home.razor.cs");
        string coachPageCodeText = File.ReadAllText(coachPageCodePath);
        string coachClientPath = FindPath("Chummer.Coach.Web", "BrowserCoachApiClient.cs");
        string coachClientText = File.ReadAllText(coachClientPath);
        string coachScriptPath = FindPath("Chummer.Coach.Web", "wwwroot", "coach-api.js");
        string coachScriptText = File.ReadAllText(coachScriptPath);
        string coachComponentTestsPath = FindPath("Chummer.Tests", "Presentation", "CoachWebComponentTests.cs");
        string coachComponentTestsText = File.ReadAllText(coachComponentTestsPath);

        StringAssert.Contains(coachProgramText, "AddScoped<BrowserCoachApiClient>()");
        StringAssert.Contains(coachAppText, "coach-api.js");
        StringAssert.Contains(coachPageText, "Chummer Coach");
        StringAssert.Contains(coachPageText, "Refresh Coach");
        StringAssert.Contains(coachPageText, "<dt>Transport</dt>");
        StringAssert.Contains(coachPageText, "<dt>Base URL</dt>");
        StringAssert.Contains(coachPageText, "<dt>Model</dt>");
        StringAssert.Contains(coachPageCodeText, "DescribeTransportMetadata");
        StringAssert.Contains(coachPageText, "Coach Runtime Status");
        StringAssert.Contains(coachPageText, "Routing Inventory");
        StringAssert.Contains(coachPageText, "Last failure:");
        StringAssert.Contains(coachPageText, "Cache:");
        StringAssert.Contains(coachPageText, "Grounding coverage:");
        StringAssert.Contains(coachPageText, "Present signals:");
        StringAssert.Contains(coachPageText, "Missing signals:");
        StringAssert.Contains(coachPageText, "Retrieved corpora:");
        StringAssert.Contains(coachPageText, "Route decision:");
        StringAssert.Contains(coachPageText, "Coach Flow Rules");
        StringAssert.Contains(coachPageText, "Allowed Tooling");
        StringAssert.Contains(coachPageText, "Decker-Contact Prompts");
        StringAssert.Contains(coachPageText, "Coach Turn Preview");
        StringAssert.Contains(coachPageText, "Preview Coach Turn");
        StringAssert.Contains(coachPageText, "Ask Coach");
        StringAssert.Contains(coachPageText, "Conversation Replay");
        StringAssert.Contains(coachPageText, "Workspace Id");
        StringAssert.Contains(coachPageText, "Last route:");
        StringAssert.Contains(coachPageText, "Last coverage:");
        StringAssert.Contains(coachPageText, "Action Preview");
        StringAssert.Contains(coachPageText, "data-action-draft-preview");
        StringAssert.Contains(coachPageText, "data-suggested-action-preview");
        StringAssert.Contains(coachPageText, "Runtime Summary");
        StringAssert.Contains(coachPageText, "data-action-draft-runtime");
        StringAssert.Contains(coachPageText, "data-suggested-action-runtime");
        StringAssert.Contains(coachPageText, "data-action-draft-build");
        StringAssert.Contains(coachPageText, "data-suggested-action-build");
        StringAssert.Contains(coachPageText, "data-turn-flavor");
        StringAssert.Contains(coachPageText, "data-turn-budget");
        StringAssert.Contains(coachPageText, "data-turn-structured-answer");
        StringAssert.Contains(coachPageText, "Build Idea Cards");
        StringAssert.Contains(coachPageText, "data-coach-route=\"route-select\"");
        StringAssert.Contains(coachPageCodeText, "AiCoachLaunchQuery.Parse");
        StringAssert.Contains(coachPageCodeText, "launchContext.ConversationId");
        StringAssert.Contains(coachClientText, "IJSRuntime");
        StringAssert.Contains(coachClientText, "/api/ai/status");
        StringAssert.Contains(coachClientText, "/api/ai/provider-health");
        StringAssert.Contains(coachClientText, "/api/ai/prompts");
        StringAssert.Contains(coachClientText, "/api/ai/build-ideas");
        StringAssert.Contains(coachClientText, "/api/ai/coach");
        StringAssert.Contains(coachClientText, "/api/ai/chat");
        StringAssert.Contains(coachClientText, "/api/ai/build-lab/query");
        StringAssert.Contains(coachClientText, "/api/ai/docs/query");
        StringAssert.Contains(coachClientText, "/api/ai/session/recap");
        StringAssert.Contains(coachClientText, "/api/ai/conversations");
        StringAssert.Contains(coachClientText, "/api/ai/conversation-audits");
        StringAssert.Contains(coachClientText, "(\"workspaceId\", workspaceId)");
        StringAssert.Contains(coachClientText, "/api/ai/conversations/{Uri.EscapeDataString(conversationId)}");
        StringAssert.Contains(coachClientText, "/api/ai/runtime/{Uri.EscapeDataString(runtimeFingerprint)}/summary");
        StringAssert.Contains(coachClientText, "/api/ai/preview/{Uri.EscapeDataString(routeType)}");
        StringAssert.Contains(coachClientText, "/api/ai/preview/karma-spend");
        StringAssert.Contains(coachClientText, "/api/ai/preview/nuyen-spend");
        StringAssert.Contains(coachClientText, "/api/ai/apply-preview");
        StringAssert.Contains(coachScriptText, "credentials: \"same-origin\"");
        StringAssert.Contains(coachScriptText, "fetch(path, options)");
        StringAssert.Contains(coachComponentTestsText, "public sealed class CoachWebComponentTests");
        StringAssert.Contains(coachComponentTestsText, "Home_renders_live_gateway_prompt_and_build_idea_data");
        StringAssert.Contains(coachComponentTestsText, "Home_bootstraps_launch_context_from_query_string");
        StringAssert.Contains(coachComponentTestsText, "recent timeout");
        StringAssert.Contains(coachComponentTestsText, "cache miss");
        StringAssert.Contains(coachComponentTestsText, "Home_previews_grounded_turns_and_replays_owner_scoped_conversations");
        StringAssert.Contains(coachComponentTestsText, "Home_submits_grounded_turns_and_refreshes_owner_scoped_history");
        StringAssert.Contains(coachComponentTestsText, "Home_uses_selected_route_for_refresh_preview_and_send_requests");
        StringAssert.Contains(coachComponentTestsText, "Home_surfaces_gateway_errors_when_status_request_fails");
        Assert.IsFalse(
            coachClientText.Contains("HttpClient", StringComparison.Ordinal),
            "Coach web head should use browser-side same-origin fetch instead of server-side HttpClient calls.");
    }

    [TestMethod]
    public void Session_and_avalonia_coach_sidecars_surface_transport_readiness_detail()
    {
        string sessionPagePath = FindPath("Chummer.Session.Web", "Components", "Pages", "Home.razor");
        string sessionPageText = File.ReadAllText(sessionPagePath);
        string sessionPageCodePath = FindPath("Chummer.Session.Web", "Components", "Pages", "Home.razor.cs");
        string sessionPageCodeText = File.ReadAllText(sessionPageCodePath);
        string avaloniaProjectorPath = FindPath("Chummer.Avalonia", "MainWindowCoachSidecarProjector.cs");
        string avaloniaProjectorText = File.ReadAllText(avaloniaProjectorPath);
        string avaloniaControlPath = FindPath("Chummer.Avalonia", "Controls", "CoachSidecarControl.axaml.cs");
        string avaloniaControlText = File.ReadAllText(avaloniaControlPath);

        StringAssert.Contains(sessionPageText, "data-testid=\"session-coach-provider-transport\"");
        StringAssert.Contains(sessionPageCodeText, "DescribeCoachProviderTransport");
        StringAssert.Contains(sessionPageCodeText, "DescribeCoachProviderKeys");
        StringAssert.Contains(sessionPageCodeText, "DescribeCoachProviderBinding");
        StringAssert.Contains(avaloniaProjectorText, "DescribeTransport");
        StringAssert.Contains(avaloniaProjectorText, "DescribeCredentialCounts");
        StringAssert.Contains(avaloniaProjectorText, "DescribeBinding");
        StringAssert.Contains(avaloniaControlText, "TransportSummary");
        StringAssert.Contains(avaloniaControlText, "CredentialSummary");
        StringAssert.Contains(avaloniaControlText, "BindingSummary");
    }

    [TestMethod]
    public void Default_state_persistence_is_file_backed_and_configurable()
    {
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string workspaceStoreContractPath = FindPath("Chummer.Application", "Workspaces", "IWorkspaceStore.cs");
        string workspaceStoreContractText = File.ReadAllText(workspaceStoreContractPath);
        string workspaceServiceContractPath = FindPath("Chummer.Application", "Workspaces", "IWorkspaceService.cs");
        string workspaceServiceContractText = File.ReadAllText(workspaceServiceContractPath);
        string rosterStoreContractPath = FindPath("Chummer.Application", "Tools", "IRosterStore.cs");
        string rosterStoreContractText = File.ReadAllText(rosterStoreContractPath);
        string settingsStoreContractPath = FindPath("Chummer.Application", "Tools", "ISettingsStore.cs");
        string settingsStoreContractText = File.ReadAllText(settingsStoreContractPath);
        string fileWorkspaceStorePath = FindPath("Chummer.Infrastructure", "Workspaces", "FileWorkspaceStore.cs");
        string fileWorkspaceStoreText = File.ReadAllText(fileWorkspaceStorePath);
        string fileRosterStorePath = FindPath("Chummer.Infrastructure", "Files", "FileRosterStore.cs");
        string fileRosterStoreText = File.ReadAllText(fileRosterStorePath);
        string fileSettingsStorePath = FindPath("Chummer.Infrastructure", "Files", "FileSettingsStore.cs");
        string fileSettingsStoreText = File.ReadAllText(fileSettingsStorePath);
        string ownerScopedStatePath = FindPath("Chummer.Infrastructure", "Files", "OwnerScopedStatePath.cs");
        string ownerScopedStateText = File.ReadAllText(ownerScopedStatePath);

        StringAssert.Contains(serviceRegistrationText, "CHUMMER_STATE_PATH");
        StringAssert.Contains(serviceRegistrationText, "new FileSettingsStore(stateDirectory)");
        StringAssert.Contains(serviceRegistrationText, "new FileRosterStore(stateDirectory)");
        StringAssert.Contains(serviceRegistrationText, "new FileWorkspaceStore(stateDirectory)");
        StringAssert.Contains(serviceRegistrationText, "CHUMMER_WORKSPACE_STORE_PATH");
        StringAssert.Contains(serviceRegistrationText, "CHUMMER_AMENDS_PATH");
        StringAssert.Contains(serviceRegistrationText, "IContentOverlayCatalogService");
        Assert.IsFalse(serviceRegistrationText.Contains("new InMemoryWorkspaceStore()", StringComparison.Ordinal));
        StringAssert.Contains(workspaceStoreContractText, "Create(OwnerScope owner, WorkspaceDocument document)");
        StringAssert.Contains(workspaceStoreContractText, "List(OwnerScope owner)");
        StringAssert.Contains(workspaceStoreContractText, "TryGet(OwnerScope owner, CharacterWorkspaceId id, out WorkspaceDocument document)");
        StringAssert.Contains(workspaceStoreContractText, "Save(OwnerScope owner, CharacterWorkspaceId id, WorkspaceDocument document)");
        StringAssert.Contains(workspaceStoreContractText, "Delete(OwnerScope owner, CharacterWorkspaceId id)");
        StringAssert.Contains(workspaceServiceContractText, "Import(OwnerScope owner, WorkspaceImportDocument document)");
        StringAssert.Contains(workspaceServiceContractText, "List(OwnerScope owner, int? maxCount = null)");
        StringAssert.Contains(rosterStoreContractText, "Load(OwnerScope owner)");
        StringAssert.Contains(rosterStoreContractText, "Upsert(OwnerScope owner, RosterEntry entry)");
        StringAssert.Contains(settingsStoreContractText, "JsonObject Load(OwnerScope owner, string scope)");
        StringAssert.Contains(settingsStoreContractText, "void Save(OwnerScope owner, string scope, JsonObject settings)");
        StringAssert.Contains(fileWorkspaceStoreText, "OwnerScopedStatePath.ResolveOwnerDirectory");
        StringAssert.Contains(fileWorkspaceStoreText, "OwnerScope.LocalSingleUser");
        StringAssert.Contains(fileRosterStoreText, "OwnerScopedStatePath.ResolveOwnerDirectory");
        StringAssert.Contains(fileRosterStoreText, "OwnerScope.LocalSingleUser");
        StringAssert.Contains(fileSettingsStoreText, "OwnerScopedStatePath.ResolveOwnerDirectory");
        StringAssert.Contains(fileSettingsStoreText, "OwnerScope.LocalSingleUser");
        StringAssert.Contains(ownerScopedStateText, "OwnerScope owner");
        StringAssert.Contains(ownerScopedStateText, "Path.Combine(");
    }

    [TestMethod]
    public void Api_startup_enforces_content_bundle_validation_contract()
    {
        string apiProgramPath = FindPath("Chummer.Api", "Program.cs");
        string apiProgramText = File.ReadAllText(apiProgramPath);
        string serviceRegistrationPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string serviceRegistrationText = File.ReadAllText(serviceRegistrationPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(apiProgramText, "requireContentBundle: true");
        StringAssert.Contains(serviceRegistrationText, "CHUMMER_REQUIRE_CONTENT_BUNDLE");
        StringAssert.Contains(serviceRegistrationText, "ValidateContentBundle");
        StringAssert.Contains(serviceRegistrationText, "lifemodules.xml");
        StringAssert.Contains(serviceRegistrationText, "language XML files");
        string overlayServicePath = FindPath("Chummer.Infrastructure", "Files", "FileSystemContentOverlayCatalogService.cs");
        string overlayServiceText = File.ReadAllText(overlayServicePath);
        StringAssert.Contains(overlayServiceText, "ValidateManifestChecksums");
        StringAssert.Contains(overlayServiceText, "manifest.Checksums");
        StringAssert.Contains(readmeText, "\"checksums\"");
        StringAssert.Contains(readmeText, "CHUMMER_REQUIRE_CONTENT_BUNDLE");
    }

    [TestMethod]
    public void Content_artifact_taxonomy_distinguishes_rulepacks_from_buildkits()
    {
        string artifactContractsPath = FindPath("Chummer.Contracts", "Content", "ArtifactContracts.cs");
        string artifactContractsText = File.ReadAllText(artifactContractsPath);
        string declarativeRuleContractsPath = FindPath("Chummer.Contracts", "Content", "DeclarativeRuleContracts.cs");
        string declarativeRuleContractsText = File.ReadAllText(declarativeRuleContractsPath);
        string overlayContractsPath = FindPath("Chummer.Application", "Content", "IContentOverlayCatalogService.cs");
        string overlayContractsText = File.ReadAllText(overlayContractsPath);
        string overlayBridgePath = FindPath("Chummer.Application", "Content", "ContentOverlayRulePackCatalogExtensions.cs");
        string overlayBridgeText = File.ReadAllText(overlayBridgePath);

        StringAssert.Contains(artifactContractsText, "public sealed record ContentBundleDescriptor");
        StringAssert.Contains(artifactContractsText, "public sealed record RulePackManifest");
        StringAssert.Contains(artifactContractsText, "public sealed record BuildKitManifest");
        StringAssert.Contains(artifactContractsText, "public sealed record ResolvedRuntimeLock");
        StringAssert.Contains(artifactContractsText, "public static class RulePackAssetModes");
        StringAssert.Contains(artifactContractsText, "public static class RulePackCapabilityIds");
        StringAssert.Contains(artifactContractsText, "public static class RulePackExecutionEnvironments");
        StringAssert.Contains(artifactContractsText, "public static class RulePackExecutionPolicyModes");
        StringAssert.Contains(artifactContractsText, "public sealed record RulePackCapabilityDescriptor");
        StringAssert.Contains(artifactContractsText, "public sealed record RulePackExecutionPolicyHint");
        StringAssert.Contains(artifactContractsText, "public static class BuildKitPromptKinds");
        StringAssert.Contains(artifactContractsText, "public static class BuildKitActionKinds");
        StringAssert.Contains(artifactContractsText, "public sealed record BuildKitRuntimeRequirement");
        StringAssert.Contains(artifactContractsText, "public sealed record BuildKitPromptDescriptor");
        StringAssert.Contains(artifactContractsText, "public sealed record BuildKitActionDescriptor");
        StringAssert.Contains(artifactContractsText, "public static class ArtifactVisibilityModes");
        Assert.IsFalse(artifactContractsText.Contains("public sealed record Pack(", StringComparison.Ordinal));
        StringAssert.Contains(declarativeRuleContractsText, "public static class DeclarativeRuleOverrideModes");
        StringAssert.Contains(declarativeRuleContractsText, "public static class DeclarativeRuleTargetKinds");
        StringAssert.Contains(declarativeRuleContractsText, "public static class DeclarativeRuleValueKinds");
        StringAssert.Contains(declarativeRuleContractsText, "public static class DeclarativeRuleConditionOperators");
        StringAssert.Contains(declarativeRuleContractsText, "public sealed record DeclarativeRuleTarget");
        StringAssert.Contains(declarativeRuleContractsText, "public sealed record DeclarativeRuleCondition");
        StringAssert.Contains(declarativeRuleContractsText, "public sealed record DeclarativeRuleValue");
        StringAssert.Contains(declarativeRuleContractsText, "public sealed record DeclarativeRuleOverride");
        StringAssert.Contains(declarativeRuleContractsText, "public sealed record DeclarativeRuleOverrideSet");

        StringAssert.Contains(overlayContractsText, "public sealed record ContentOverlayPack");
        StringAssert.Contains(overlayBridgeText, "public static RulePackCatalog ToRulePackCatalog(");
        StringAssert.Contains(overlayBridgeText, "public static RulePackManifest ToRulePackManifest(");
        StringAssert.Contains(overlayBridgeText, "ArtifactVisibilityModes.LocalOnly");
        StringAssert.Contains(overlayBridgeText, "ArtifactTrustTiers.LocalOnly");
        StringAssert.Contains(overlayBridgeText, "RulePackAssetKinds.Xml");
        StringAssert.Contains(overlayBridgeText, "RulePackAssetKinds.Localization");
        StringAssert.Contains(overlayBridgeText, "RulePackCapabilityIds.ContentCatalog");
        StringAssert.Contains(overlayBridgeText, "RulePackExecutionEnvironments.DesktopLocal");
        StringAssert.Contains(overlayBridgeText, "RulePackExecutionPolicyModes.Deny");
        StringAssert.Contains(artifactContractsText, "DeclarativeRuleOverrideModes.SetConstant");
        StringAssert.Contains(artifactContractsText, "DeclarativeRuleOverrideModes.ModifyCap");
        Assert.IsFalse(declarativeRuleContractsText.Contains("BuildKitActionDescriptor", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Workflow_surface_contracts_lock_in_shared_shell_region_and_workbench_vocabulary()
    {
        string workflowSurfaceContractsPath = FindCorePresentationContractsPath("WorkflowSurfaceContracts.cs");
        string workflowSurfaceContractsText = File.ReadAllText(workflowSurfaceContractsPath);

        StringAssert.Contains(workflowSurfaceContractsText, "public static class ShellRegionIds");
        StringAssert.Contains(workflowSurfaceContractsText, "public static class WorkflowDefinitionIds");
        StringAssert.Contains(workflowSurfaceContractsText, "public static class WorkflowSurfaceKinds");
        StringAssert.Contains(workflowSurfaceContractsText, "public static class WorkflowLayoutTokens");
        StringAssert.Contains(workflowSurfaceContractsText, "public sealed record WorkflowDefinition");
        StringAssert.Contains(workflowSurfaceContractsText, "public sealed record WorkflowSurfaceDefinition");
        StringAssert.Contains(workflowSurfaceContractsText, "LibraryShell");
        StringAssert.Contains(workflowSurfaceContractsText, "CreateWorkbench");
        StringAssert.Contains(workflowSurfaceContractsText, "CareerWorkbench");
        StringAssert.Contains(workflowSurfaceContractsText, "ExpenseLedger");
        StringAssert.Contains(workflowSurfaceContractsText, "HistoryTimeline");
        StringAssert.Contains(workflowSurfaceContractsText, "DiceTool");
        StringAssert.Contains(workflowSurfaceContractsText, "ExportTool");
        StringAssert.Contains(workflowSurfaceContractsText, "PackManager");
        StringAssert.Contains(workflowSurfaceContractsText, "SessionDashboard");
        StringAssert.Contains(workflowSurfaceContractsText, "MenuBar");
        StringAssert.Contains(workflowSurfaceContractsText, "SectionPane");
        StringAssert.Contains(workflowSurfaceContractsText, "DialogHost");
        Assert.IsFalse(workflowSurfaceContractsText.Contains("DesktopUiControlDefinition", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Shadow_regression_contracts_lock_in_corpus_diff_waiver_and_explain_vocabulary()
    {
        string shadowRegressionContractsPath = FindPath("Chummer.Contracts", "Diagnostics", "ShadowRegressionContracts.cs");
        string shadowRegressionContractsText = File.ReadAllText(shadowRegressionContractsPath);

        StringAssert.Contains(shadowRegressionContractsText, "public static class ShadowRegressionFixtureKinds");
        StringAssert.Contains(shadowRegressionContractsText, "public static class ShadowRegressionMetricKinds");
        StringAssert.Contains(shadowRegressionContractsText, "public static class ShadowRegressionDiffKinds");
        StringAssert.Contains(shadowRegressionContractsText, "public static class ShadowRegressionSeverityLevels");
        StringAssert.Contains(shadowRegressionContractsText, "public sealed record ShadowRegressionFixtureDescriptor");
        StringAssert.Contains(shadowRegressionContractsText, "public sealed record ShadowRegressionCorpusDescriptor");
        StringAssert.Contains(shadowRegressionContractsText, "public sealed record ShadowRegressionMetricBaseline");
        StringAssert.Contains(shadowRegressionContractsText, "public sealed record ShadowRegressionExplainReference");
        StringAssert.Contains(shadowRegressionContractsText, "public sealed record ShadowRegressionDiff");
        StringAssert.Contains(shadowRegressionContractsText, "public sealed record ShadowRegressionWaiver");
        StringAssert.Contains(shadowRegressionContractsText, "public sealed record ShadowRegressionRunReceipt");
        StringAssert.Contains(shadowRegressionContractsText, "DerivedStats");
        StringAssert.Contains(shadowRegressionContractsText, "Validation");
        StringAssert.Contains(shadowRegressionContractsText, "SessionProjection");
        StringAssert.Contains(shadowRegressionContractsText, "LegacyOracle = false");
        StringAssert.Contains(shadowRegressionContractsText, "ShadowRegressionExplainReference? Explain = null");
    }

    [TestMethod]
    public void Rulepack_compiler_contracts_lock_in_resolution_diagnostic_and_compile_receipt_vocabulary()
    {
        string rulePackCompilerContractsPath = FindPath("Chummer.Contracts", "Content", "RulePackCompilerContracts.cs");
        string rulePackCompilerContractsText = File.ReadAllText(rulePackCompilerContractsPath);

        StringAssert.Contains(rulePackCompilerContractsText, "public static class RulePackResolutionDiagnosticKinds");
        StringAssert.Contains(rulePackCompilerContractsText, "public static class RulePackResolutionSeverityLevels");
        StringAssert.Contains(rulePackCompilerContractsText, "public static class RulePackCompileStatuses");
        StringAssert.Contains(rulePackCompilerContractsText, "public sealed record RulePackCompilerRequest");
        StringAssert.Contains(rulePackCompilerContractsText, "public sealed record RulePackResolutionDiagnostic");
        StringAssert.Contains(rulePackCompilerContractsText, "public sealed record RulePackResolutionResult");
        StringAssert.Contains(rulePackCompilerContractsText, "public sealed record RulePackCompileReceipt");
        StringAssert.Contains(rulePackCompilerContractsText, "string? MessageKey = null");
        StringAssert.Contains(rulePackCompilerContractsText, "IReadOnlyList<RulesetExplainParameter>? MessageParameters = null");
        StringAssert.Contains(rulePackCompilerContractsText, "MissingDependency");
        StringAssert.Contains(rulePackCompilerContractsText, "TrustTierViolation");
        StringAssert.Contains(rulePackCompilerContractsText, "CapabilityBlocked");
        StringAssert.Contains(rulePackCompilerContractsText, "CompiledWithReview");
        StringAssert.Contains(rulePackCompilerContractsText, "ResolvedRuntimeLock? RuntimeLock");
        StringAssert.Contains(rulePackCompilerContractsText, "DateTimeOffset CompiledAtUtc");
    }

    [TestMethod]
    public void Session_merge_contracts_lock_in_family_policy_and_rebind_vocabulary()
    {
        string sessionMergeContractsPath = FindPath("Chummer.Contracts", "Session", "SessionMergeContracts.cs");
        string sessionMergeContractsText = File.ReadAllText(sessionMergeContractsPath);

        StringAssert.Contains(sessionMergeContractsText, "public static class SessionMergeFamilies");
        StringAssert.Contains(sessionMergeContractsText, "public static class SessionMergePolicyModes");
        StringAssert.Contains(sessionMergeContractsText, "public static class SessionRebindOutcomes");
        StringAssert.Contains(sessionMergeContractsText, "public sealed record SessionMergePolicy");
        StringAssert.Contains(sessionMergeContractsText, "public sealed record SessionRebindDiagnostic");
        StringAssert.Contains(sessionMergeContractsText, "public sealed record SessionRebindReceipt");
        StringAssert.Contains(sessionMergeContractsText, "Tracker");
        StringAssert.Contains(sessionMergeContractsText, "QuickAction");
        StringAssert.Contains(sessionMergeContractsText, "ConflictMarker");
        StringAssert.Contains(sessionMergeContractsText, "ReboundToNewRuntime");
        StringAssert.Contains(sessionMergeContractsText, "ManualResolutionRequired");
        StringAssert.Contains(sessionMergeContractsText, "bool RuntimeFingerprintChanged = false");
        StringAssert.Contains(sessionMergeContractsText, "bool BaseCharacterChanged = false");
    }

    [TestMethod]
    public void Session_lifecycle_contracts_lock_in_snapshot_compaction_and_bundle_refresh_vocabulary()
    {
        string sessionLifecycleContractsPath = FindPath("Chummer.Contracts", "Session", "SessionLifecycleContracts.cs");
        string sessionLifecycleContractsText = File.ReadAllText(sessionLifecycleContractsPath);

        StringAssert.Contains(sessionLifecycleContractsText, "public static class SessionCompactionModes");
        StringAssert.Contains(sessionLifecycleContractsText, "public static class SessionRuntimeBundleRefreshOutcomes");
        StringAssert.Contains(sessionLifecycleContractsText, "public sealed record SessionSnapshotBaseline");
        StringAssert.Contains(sessionLifecycleContractsText, "public sealed record SessionCompactionReceipt");
        StringAssert.Contains(sessionLifecycleContractsText, "public sealed record SessionRuntimeBundleRefreshReceipt");
        StringAssert.Contains(sessionLifecycleContractsText, "IncrementalSnapshot");
        StringAssert.Contains(sessionLifecycleContractsText, "FullRebuild");
        StringAssert.Contains(sessionLifecycleContractsText, "Refreshed");
        StringAssert.Contains(sessionLifecycleContractsText, "Blocked");
        StringAssert.Contains(sessionLifecycleContractsText, "bool PendingEventsRetained = true");
        StringAssert.Contains(sessionLifecycleContractsText, "bool SignatureChanged = false");
    }

    [TestMethod]
    public void Buildkit_application_contracts_lock_in_prompt_validation_and_apply_receipt_vocabulary()
    {
        string buildKitApplicationContractsPath = FindPath("Chummer.Contracts", "Content", "BuildKitApplicationContracts.cs");
        string buildKitApplicationContractsText = File.ReadAllText(buildKitApplicationContractsPath);

        StringAssert.Contains(buildKitApplicationContractsText, "public static class BuildKitValidationIssueKinds");
        StringAssert.Contains(buildKitApplicationContractsText, "public static class BuildKitAppliedActionOutcomes");
        StringAssert.Contains(buildKitApplicationContractsText, "public static class BuildKitApplicationStatuses");
        StringAssert.Contains(buildKitApplicationContractsText, "public sealed record BuildKitPromptResolution");
        StringAssert.Contains(buildKitApplicationContractsText, "public sealed record BuildKitValidationIssue");
        StringAssert.Contains(buildKitApplicationContractsText, "public sealed record BuildKitAppliedAction");
        StringAssert.Contains(buildKitApplicationContractsText, "public sealed record BuildKitValidationReceipt");
        StringAssert.Contains(buildKitApplicationContractsText, "public sealed record BuildKitApplicationReceipt");
        StringAssert.Contains(buildKitApplicationContractsText, "string? MessageKey = null");
        StringAssert.Contains(buildKitApplicationContractsText, "IReadOnlyList<RulesetExplainParameter>? MessageParameters = null");
        StringAssert.Contains(buildKitApplicationContractsText, "RuntimeFingerprintMismatch");
        StringAssert.Contains(buildKitApplicationContractsText, "PromptRequired");
        StringAssert.Contains(buildKitApplicationContractsText, "PartiallyApplied");
        StringAssert.Contains(buildKitApplicationContractsText, "CharacterVersionReference? ResultingCharacterVersion = null");
    }

    [TestMethod]
    public void Rulepack_registry_contracts_lock_in_publication_review_share_and_fork_vocabulary()
    {
        string rulePackRegistryContractsPath = FindPath("Chummer.Contracts", "Content", "RulePackRegistryContracts.cs");
        string rulePackRegistryContractsText = File.ReadAllText(rulePackRegistryContractsPath);

        StringAssert.Contains(rulePackRegistryContractsText, "public static class RulePackPublicationStatuses");
        StringAssert.Contains(rulePackRegistryContractsText, "public static class RulePackReviewStates");
        StringAssert.Contains(rulePackRegistryContractsText, "public static class RulePackShareSubjectKinds");
        StringAssert.Contains(rulePackRegistryContractsText, "public static class RulePackShareAccessLevels");
        StringAssert.Contains(rulePackRegistryContractsText, "public static class RegistryEntrySourceKinds");
        StringAssert.Contains(rulePackRegistryContractsText, "public sealed record RulePackForkLineage");
        StringAssert.Contains(rulePackRegistryContractsText, "public sealed record RulePackShareGrant");
        StringAssert.Contains(rulePackRegistryContractsText, "public sealed record RulePackReviewDecision");
        StringAssert.Contains(rulePackRegistryContractsText, "public sealed record RulePackPublicationMetadata");
        StringAssert.Contains(rulePackRegistryContractsText, "public sealed record RulePackRegistryEntry");
        StringAssert.Contains(rulePackRegistryContractsText, "public sealed record RulePackPublicationReceipt");
        StringAssert.Contains(rulePackRegistryContractsText, "PendingReview");
        StringAssert.Contains(rulePackRegistryContractsText, "PublicCatalog");
        StringAssert.Contains(rulePackRegistryContractsText, "Campaign");
        StringAssert.Contains(rulePackRegistryContractsText, "Fork");
        StringAssert.Contains(rulePackRegistryContractsText, "OverlayCatalogBridge");
        StringAssert.Contains(rulePackRegistryContractsText, "PersistedManifest");
        StringAssert.Contains(rulePackRegistryContractsText, "DateTimeOffset? PublishedAtUtc = null");
        StringAssert.Contains(rulePackRegistryContractsText, "string? PublisherId = null");
        StringAssert.Contains(rulePackRegistryContractsText, "public sealed record RulePackInstallPreviewItem");
        StringAssert.Contains(rulePackRegistryContractsText, "string? SummaryKey = null");
        StringAssert.Contains(rulePackRegistryContractsText, "IReadOnlyList<RulesetExplainParameter>? SummaryParameters = null");
    }

    [TestMethod]
    public void Ruleprofile_registry_contracts_lock_in_curated_install_target_and_runtime_preview_vocabulary()
    {
        string ruleProfileRegistryContractsPath = FindPath("Chummer.Contracts", "Content", "RuleProfileRegistryContracts.cs");
        string ruleProfileRegistryContractsText = File.ReadAllText(ruleProfileRegistryContractsPath);

        StringAssert.Contains(ruleProfileRegistryContractsText, "public static class RuleProfileAudienceKinds");
        StringAssert.Contains(ruleProfileRegistryContractsText, "public static class RuleProfileCatalogKinds");
        StringAssert.Contains(ruleProfileRegistryContractsText, "public static class RuleProfilePublicationStatuses");
        StringAssert.Contains(ruleProfileRegistryContractsText, "public static class RuleProfileUpdateChannels");
        StringAssert.Contains(ruleProfileRegistryContractsText, "public sealed record RuleProfilePackSelection");
        StringAssert.Contains(ruleProfileRegistryContractsText, "public sealed record RuleProfileDefaultToggle");
        StringAssert.Contains(ruleProfileRegistryContractsText, "public sealed record RuleProfileManifest");
        StringAssert.Contains(ruleProfileRegistryContractsText, "public sealed record RuleProfilePublicationMetadata");
        StringAssert.Contains(ruleProfileRegistryContractsText, "public sealed record RuleProfileRegistryEntry");
        StringAssert.Contains(ruleProfileRegistryContractsText, "ResolvedRuntimeLock RuntimeLock");
        StringAssert.Contains(ruleProfileRegistryContractsText, "RulePackReviewDecision Review");
        StringAssert.Contains(ruleProfileRegistryContractsText, "string? PublisherId = null");
    }

    [TestMethod]
    public void Ruleprofile_application_contracts_lock_in_target_preview_and_apply_vocabulary()
    {
        string ruleProfileApplicationContractsPath = FindPath("Chummer.Contracts", "Content", "RuleProfileApplicationContracts.cs");
        string ruleProfileApplicationContractsText = File.ReadAllText(ruleProfileApplicationContractsPath);

        StringAssert.Contains(ruleProfileApplicationContractsText, "public static class RuleProfileApplyTargetKinds");
        StringAssert.Contains(ruleProfileApplicationContractsText, "public static class RuleProfileApplyOutcomes");
        StringAssert.Contains(ruleProfileApplicationContractsText, "public static class RuleProfilePreviewChangeKinds");
        StringAssert.Contains(ruleProfileApplicationContractsText, "public sealed record RuleProfileApplyTarget");
        StringAssert.Contains(ruleProfileApplicationContractsText, "public sealed record RuleProfilePreviewItem");
        StringAssert.Contains(ruleProfileApplicationContractsText, "public sealed record RuleProfilePreviewReceipt");
        StringAssert.Contains(ruleProfileApplicationContractsText, "public sealed record RuleProfileApplyReceipt");
        StringAssert.Contains(ruleProfileApplicationContractsText, "RuntimeLockInstallReceipt? InstallReceipt = null");
        StringAssert.Contains(ruleProfileApplicationContractsText, "string? DeferredReason = null");
        StringAssert.Contains(ruleProfileApplicationContractsText, "string? SummaryKey = null");
        StringAssert.Contains(ruleProfileApplicationContractsText, "IReadOnlyList<RulesetExplainParameter>? SummaryParameters = null");
    }

    [TestMethod]
    public void Hub_catalog_contracts_lock_in_item_kind_facet_sort_and_result_vocabulary()
    {
        string hubCatalogContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubCatalogContracts.cs");
        string hubCatalogContractsText = File.ReadAllText(hubCatalogContractsPath);

        StringAssert.Contains(hubCatalogContractsText, "public static class HubCatalogItemKinds");
        StringAssert.Contains(hubCatalogContractsText, "public static class HubCatalogFacetIds");
        StringAssert.Contains(hubCatalogContractsText, "public static class HubCatalogSortIds");
        StringAssert.Contains(hubCatalogContractsText, "public sealed record HubCatalogItem");
        StringAssert.Contains(hubCatalogContractsText, "public sealed record HubCatalogResultPage");
        StringAssert.Contains(hubCatalogContractsText, "HubReviewSummary? OwnerReview");
        StringAssert.Contains(hubCatalogContractsText, "HubReviewAggregateSummary? AggregateReview");
        StringAssert.Contains(hubCatalogContractsText, "HubPublisherSummary? Publisher = null");
        StringAssert.Contains(hubCatalogContractsText, "BrowseQuery Query");
        StringAssert.Contains(hubCatalogContractsText, "IReadOnlyList<FacetDefinition> Facets");
        StringAssert.Contains(hubCatalogContractsText, "IReadOnlyList<SortDefinition> Sorts");
    }

    [TestMethod]
    public void Hub_project_detail_contracts_lock_in_fact_dependency_and_action_vocabulary()
    {
        string hubProjectDetailContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubProjectDetailContracts.cs");
        string hubProjectDetailContractsText = File.ReadAllText(hubProjectDetailContractsPath);

        StringAssert.Contains(hubProjectDetailContractsText, "public static class HubProjectDependencyKinds");
        StringAssert.Contains(hubProjectDetailContractsText, "public static class HubProjectActionKinds");
        StringAssert.Contains(hubProjectDetailContractsText, "public sealed record HubProjectDetailFact");
        StringAssert.Contains(hubProjectDetailContractsText, "public sealed record HubProjectDependency");
        StringAssert.Contains(hubProjectDetailContractsText, "public sealed record HubProjectAction");
        StringAssert.Contains(hubProjectDetailContractsText, "public sealed record HubProjectCapabilityDescriptorProjection");
        StringAssert.Contains(hubProjectDetailContractsText, "public sealed record HubProjectDetailProjection");
        StringAssert.Contains(hubProjectDetailContractsText, "HubCatalogItem Summary");
        StringAssert.Contains(hubProjectDetailContractsText, "HubReviewSummary? OwnerReview");
        StringAssert.Contains(hubProjectDetailContractsText, "HubReviewAggregateSummary? AggregateReview");
        StringAssert.Contains(hubProjectDetailContractsText, "HubPublisherSummary? Publisher = null");
        StringAssert.Contains(hubProjectDetailContractsText, "string? RuntimeFingerprint");
        StringAssert.Contains(hubProjectDetailContractsText, "IReadOnlyList<HubProjectAction> Actions");
        StringAssert.Contains(hubProjectDetailContractsText, "IReadOnlyList<HubProjectCapabilityDescriptorProjection>? Capabilities = null");
    }

    [TestMethod]
    public void Hub_project_install_preview_contracts_lock_in_state_change_and_diagnostic_vocabulary()
    {
        string hubInstallPreviewContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubProjectInstallPreviewContracts.cs");
        string hubInstallPreviewContractsText = File.ReadAllText(hubInstallPreviewContractsPath);

        StringAssert.Contains(hubInstallPreviewContractsText, "public static class HubProjectInstallPreviewStates");
        StringAssert.Contains(hubInstallPreviewContractsText, "public static class HubProjectInstallPreviewChangeKinds");
        StringAssert.Contains(hubInstallPreviewContractsText, "public static class HubProjectInstallPreviewDiagnosticKinds");
        StringAssert.Contains(hubInstallPreviewContractsText, "public static class HubProjectInstallPreviewDiagnosticSeverityLevels");
        StringAssert.Contains(hubInstallPreviewContractsText, "public sealed record HubProjectInstallPreviewChange");
        StringAssert.Contains(hubInstallPreviewContractsText, "public sealed record HubProjectInstallPreviewDiagnostic");
        StringAssert.Contains(hubInstallPreviewContractsText, "public sealed record HubProjectInstallPreviewReceipt");
        StringAssert.Contains(hubInstallPreviewContractsText, "RuleProfileApplyTarget Target");
        StringAssert.Contains(hubInstallPreviewContractsText, "string? RuntimeFingerprint");
        StringAssert.Contains(hubInstallPreviewContractsText, "string? DeferredReason = null");
    }

    [TestMethod]
    public void Hub_project_compatibility_contracts_lock_in_row_state_and_matrix_vocabulary()
    {
        string hubCompatibilityContractsPath = FindPath("Chummer.Run.Contracts", "Hub", "HubProjectCompatibilityContracts.cs");
        string hubCompatibilityContractsText = File.ReadAllText(hubCompatibilityContractsPath);

        StringAssert.Contains(hubCompatibilityContractsText, "public static class HubProjectCompatibilityRowKinds");
        StringAssert.Contains(hubCompatibilityContractsText, "public static class HubProjectCompatibilityStates");
        StringAssert.Contains(hubCompatibilityContractsText, "public sealed record HubProjectCompatibilityRow");
        StringAssert.Contains(hubCompatibilityContractsText, "public sealed record HubProjectCompatibilityMatrix");
        StringAssert.Contains(hubCompatibilityContractsText, "IReadOnlyList<HubProjectCompatibilityRow> Rows");
        StringAssert.Contains(hubCompatibilityContractsText, "DateTimeOffset GeneratedAtUtc");
        StringAssert.Contains(hubCompatibilityContractsText, "IReadOnlyList<HubProjectCapabilityDescriptorProjection>? Capabilities = null");
        StringAssert.Contains(hubCompatibilityContractsText, "Capabilities = \"capabilities\"");
        StringAssert.Contains(hubCompatibilityContractsText, "InstallState");
        StringAssert.Contains(hubCompatibilityContractsText, "SessionRuntime");
        StringAssert.Contains(hubCompatibilityContractsText, "HostedPublic");
    }

    [TestMethod]
    public void Linked_asset_library_contracts_lock_in_registry_share_and_transfer_vocabulary()
    {
        string linkedAssetLibraryContractsPath = FindPath("Chummer.Contracts", "Assets", "LinkedAssetLibraryContracts.cs");
        string linkedAssetLibraryContractsText = File.ReadAllText(linkedAssetLibraryContractsPath);

        StringAssert.Contains(linkedAssetLibraryContractsText, "public static class LinkedAssetShareSubjectKinds");
        StringAssert.Contains(linkedAssetLibraryContractsText, "public static class LinkedAssetShareAccessLevels");
        StringAssert.Contains(linkedAssetLibraryContractsText, "public static class LinkedAssetTransferFormats");
        StringAssert.Contains(linkedAssetLibraryContractsText, "public sealed record LinkedAssetShareGrant");
        StringAssert.Contains(linkedAssetLibraryContractsText, "public sealed record LinkedAssetLibraryEntry");
        StringAssert.Contains(linkedAssetLibraryContractsText, "public sealed record LinkedAssetImportReceipt");
        StringAssert.Contains(linkedAssetLibraryContractsText, "public sealed record LinkedAssetExportReceipt");
        StringAssert.Contains(linkedAssetLibraryContractsText, "PublicCatalog");
        StringAssert.Contains(linkedAssetLibraryContractsText, "Manage");
        StringAssert.Contains(linkedAssetLibraryContractsText, "Bundle");
        StringAssert.Contains(linkedAssetLibraryContractsText, "DateTimeOffset UpdatedAtUtc");
        StringAssert.Contains(linkedAssetLibraryContractsText, "string FileName");
    }

    [TestMethod]
    public void Session_projection_contracts_lock_in_dashboard_card_banner_and_explain_vocabulary()
    {
        string sessionProjectionContractsPath = FindPath("Chummer.Contracts", "Session", "SessionProjectionContracts.cs");
        string sessionProjectionContractsText = File.ReadAllText(sessionProjectionContractsPath);

        StringAssert.Contains(sessionProjectionContractsText, "public static class SessionDashboardSectionKinds");
        StringAssert.Contains(sessionProjectionContractsText, "public static class SessionDashboardCardKinds");
        StringAssert.Contains(sessionProjectionContractsText, "public static class SessionSyncBannerStates");
        StringAssert.Contains(sessionProjectionContractsText, "public static class SessionExplainEntryKinds");
        StringAssert.Contains(sessionProjectionContractsText, "public sealed record SessionDashboardSection");
        StringAssert.Contains(sessionProjectionContractsText, "public sealed record SessionDashboardCard");
        StringAssert.Contains(sessionProjectionContractsText, "public sealed record SessionTrackerGroup");
        StringAssert.Contains(sessionProjectionContractsText, "public sealed record SessionQuickActionDescriptor");
        StringAssert.Contains(sessionProjectionContractsText, "public sealed record SessionQuickActionGroup");
        StringAssert.Contains(sessionProjectionContractsText, "public sealed record SessionSyncBanner");
        StringAssert.Contains(sessionProjectionContractsText, "public sealed record SessionExplainEntry");
        StringAssert.Contains(sessionProjectionContractsText, "public sealed record SessionDashboardProjection");
        StringAssert.Contains(sessionProjectionContractsText, "tracker-group");
        StringAssert.Contains(sessionProjectionContractsText, "pending-sync");
        StringAssert.Contains(sessionProjectionContractsText, "quick-action-availability");
        StringAssert.Contains(sessionProjectionContractsText, "SessionRuntimeBundle RuntimeBundle");
        Assert.IsFalse(sessionProjectionContractsText.Contains("Avalonia", StringComparison.Ordinal));
        Assert.IsFalse(sessionProjectionContractsText.Contains("Blazor", StringComparison.Ordinal));
        Assert.IsFalse(sessionProjectionContractsText.Contains("LuaSource", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Portal_identity_contracts_lock_in_account_session_owner_and_binding_vocabulary()
    {
        string portalIdentityContractsPath = FindPath("Chummer.Contracts", "Owners", "PortalIdentityContracts.cs");
        string portalIdentityContractsText = File.ReadAllText(portalIdentityContractsPath);

        StringAssert.Contains(portalIdentityContractsText, "public static class PortalIdentityProviderKinds");
        StringAssert.Contains(portalIdentityContractsText, "public static class PortalAccountStatuses");
        StringAssert.Contains(portalIdentityContractsText, "public static class PortalSessionModes");
        StringAssert.Contains(portalIdentityContractsText, "public static class PortalOwnerKinds");
        StringAssert.Contains(portalIdentityContractsText, "public sealed record PortalIdentityBinding");
        StringAssert.Contains(portalIdentityContractsText, "public sealed record PortalAccountProfile");
        StringAssert.Contains(portalIdentityContractsText, "public sealed record PortalSessionDescriptor");
        StringAssert.Contains(portalIdentityContractsText, "public sealed record PortalOwnerDescriptor");
        StringAssert.Contains(portalIdentityContractsText, "public sealed record PortalAuthenticationReceipt");
        StringAssert.Contains(portalIdentityContractsText, "pending-confirmation");
        StringAssert.Contains(portalIdentityContractsText, "interactive-web");
        StringAssert.Contains(portalIdentityContractsText, "portal-bridge");
        StringAssert.Contains(portalIdentityContractsText, "OwnerScope Owner");
        StringAssert.Contains(portalIdentityContractsText, "OwnerScope Scope");
        Assert.IsFalse(portalIdentityContractsText.Contains("HttpContext", StringComparison.Ordinal));
        Assert.IsFalse(portalIdentityContractsText.Contains("ClaimsPrincipal", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Owner_repository_contracts_lock_in_scope_filter_page_and_receipt_vocabulary()
    {
        string ownerRepositoryContractsPath = FindPath("Chummer.Contracts", "Owners", "OwnerRepositoryContracts.cs");
        string ownerRepositoryContractsText = File.ReadAllText(ownerRepositoryContractsPath);

        StringAssert.Contains(ownerRepositoryContractsText, "public static class OwnerRepositoryAssetKinds");
        StringAssert.Contains(ownerRepositoryContractsText, "public static class OwnerRepositoryScopeModes");
        StringAssert.Contains(ownerRepositoryContractsText, "public static class OwnerRepositorySortModes");
        StringAssert.Contains(ownerRepositoryContractsText, "public sealed record OwnerRepositoryQuery");
        StringAssert.Contains(ownerRepositoryContractsText, "public sealed record OwnerRepositoryEntry");
        StringAssert.Contains(ownerRepositoryContractsText, "public sealed record OwnerRepositoryPage");
        StringAssert.Contains(ownerRepositoryContractsText, "public sealed record OwnerRepositoryQueryReceipt");
        StringAssert.Contains(ownerRepositoryContractsText, "shared-with-me");
        StringAssert.Contains(ownerRepositoryContractsText, "public-catalog");
        StringAssert.Contains(ownerRepositoryContractsText, "updated-desc");
        StringAssert.Contains(ownerRepositoryContractsText, "OwnerScope Owner");
        StringAssert.Contains(ownerRepositoryContractsText, "bool CanShare = false");
        Assert.IsFalse(ownerRepositoryContractsText.Contains("HttpContext", StringComparison.Ordinal));
        Assert.IsFalse(ownerRepositoryContractsText.Contains("ClaimsPrincipal", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Owner_repository_mutation_contracts_lock_in_share_fork_archive_and_delete_vocabulary()
    {
        string ownerRepositoryMutationContractsPath = FindPath("Chummer.Contracts", "Owners", "OwnerRepositoryMutationContracts.cs");
        string ownerRepositoryMutationContractsText = File.ReadAllText(ownerRepositoryMutationContractsPath);

        StringAssert.Contains(ownerRepositoryMutationContractsText, "public static class OwnerRepositoryMutationKinds");
        StringAssert.Contains(ownerRepositoryMutationContractsText, "public static class OwnerRepositoryMutationStatuses");
        StringAssert.Contains(ownerRepositoryMutationContractsText, "public static class OwnerRepositoryArchiveModes");
        StringAssert.Contains(ownerRepositoryMutationContractsText, "public static class OwnerRepositoryShareAccessLevels");
        StringAssert.Contains(ownerRepositoryMutationContractsText, "public sealed record OwnerRepositoryShareGrant");
        StringAssert.Contains(ownerRepositoryMutationContractsText, "public sealed record OwnerRepositoryMutationReceipt");
        StringAssert.Contains(ownerRepositoryMutationContractsText, "public sealed record OwnerRepositoryShareReceipt");
        StringAssert.Contains(ownerRepositoryMutationContractsText, "public sealed record OwnerRepositoryForkReceipt");
        StringAssert.Contains(ownerRepositoryMutationContractsText, "public sealed record OwnerRepositoryArchiveReceipt");
        StringAssert.Contains(ownerRepositoryMutationContractsText, "retain-history");
        StringAssert.Contains(ownerRepositoryMutationContractsText, "soft-delete");
        StringAssert.Contains(ownerRepositoryMutationContractsText, "RequiresReindex = false");
        StringAssert.Contains(ownerRepositoryMutationContractsText, "OwnerScope Actor");
        Assert.IsFalse(ownerRepositoryMutationContractsText.Contains("HttpContext", StringComparison.Ordinal));
        Assert.IsFalse(ownerRepositoryMutationContractsText.Contains("ClaimsPrincipal", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Runtime_lock_install_contracts_lock_in_target_pin_and_rebind_vocabulary()
    {
        string runtimeLockInstallContractsPath = FindPath("Chummer.Contracts", "Content", "RuntimeLockInstallContracts.cs");
        string runtimeLockInstallContractsText = File.ReadAllText(runtimeLockInstallContractsPath);

        StringAssert.Contains(runtimeLockInstallContractsText, "public static class RuntimeLockTargetKinds");
        StringAssert.Contains(runtimeLockInstallContractsText, "public static class RuntimeLockPinModes");
        StringAssert.Contains(runtimeLockInstallContractsText, "public static class RuntimeLockInstallOutcomes");
        StringAssert.Contains(runtimeLockInstallContractsText, "public static class RuntimeLockRebindReasons");
        StringAssert.Contains(runtimeLockInstallContractsText, "public sealed record RuntimeLockReference");
        StringAssert.Contains(runtimeLockInstallContractsText, "public sealed record RuntimeLockPin");
        StringAssert.Contains(runtimeLockInstallContractsText, "public sealed record RuntimeLockRebindNotice");
        StringAssert.Contains(runtimeLockInstallContractsText, "public sealed record RuntimeLockInstallReceipt");
        StringAssert.Contains(runtimeLockInstallContractsText, "character-version");
        StringAssert.Contains(runtimeLockInstallContractsText, "session-ledger");
        StringAssert.Contains(runtimeLockInstallContractsText, "rulepack-selection-changed");
        StringAssert.Contains(runtimeLockInstallContractsText, "ResolvedRuntimeLock RuntimeLock");
        StringAssert.Contains(runtimeLockInstallContractsText, "bool RequiresSessionReplay = false");
        Assert.IsFalse(runtimeLockInstallContractsText.Contains("HttpContext", StringComparison.Ordinal));
        Assert.IsFalse(runtimeLockInstallContractsText.Contains("ClaimsPrincipal", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Runtime_lock_registry_contracts_lock_in_catalog_compatibility_and_candidate_vocabulary()
    {
        string runtimeLockRegistryContractsPath = FindPath("Chummer.Contracts", "Content", "RuntimeLockRegistryContracts.cs");
        string runtimeLockRegistryContractsText = File.ReadAllText(runtimeLockRegistryContractsPath);

        StringAssert.Contains(runtimeLockRegistryContractsText, "public static class RuntimeLockCatalogKinds");
        StringAssert.Contains(runtimeLockRegistryContractsText, "public static class RuntimeLockCompatibilityStates");
        StringAssert.Contains(runtimeLockRegistryContractsText, "public sealed record RuntimeLockRegistryEntry");
        StringAssert.Contains(runtimeLockRegistryContractsText, "public sealed record RuntimeLockCompatibilityDiagnostic");
        StringAssert.Contains(runtimeLockRegistryContractsText, "public sealed record RuntimeLockInstallCandidate");
        StringAssert.Contains(runtimeLockRegistryContractsText, "public sealed record RuntimeLockRegistryPage");
        StringAssert.Contains(runtimeLockRegistryContractsText, "published");
        StringAssert.Contains(runtimeLockRegistryContractsText, "rebind-required");
        StringAssert.Contains(runtimeLockRegistryContractsText, "engine-api-mismatch");
        StringAssert.Contains(runtimeLockRegistryContractsText, "ResolvedRuntimeLock RuntimeLock");
        StringAssert.Contains(runtimeLockRegistryContractsText, "OwnerScope Owner");
        StringAssert.Contains(runtimeLockRegistryContractsText, "ArtifactInstallState Install");
        StringAssert.Contains(runtimeLockRegistryContractsText, "string? MessageKey = null");
        StringAssert.Contains(runtimeLockRegistryContractsText, "IReadOnlyList<RulesetExplainParameter>? MessageParameters = null");
        StringAssert.Contains(runtimeLockRegistryContractsText, "string? SummaryKey = null");
        StringAssert.Contains(runtimeLockRegistryContractsText, "IReadOnlyList<RulesetExplainParameter>? SummaryParameters = null");
        Assert.IsFalse(runtimeLockRegistryContractsText.Contains("HttpContext", StringComparison.Ordinal));
        Assert.IsFalse(runtimeLockRegistryContractsText.Contains("ClaimsPrincipal", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Session_runtime_bundle_issue_contracts_lock_in_issue_rotation_and_trust_vocabulary()
    {
        string sessionRuntimeBundleIssueContractsPath = FindPath("Chummer.Contracts", "Session", "SessionRuntimeBundleIssueContracts.cs");
        string sessionRuntimeBundleIssueContractsText = File.ReadAllText(sessionRuntimeBundleIssueContractsPath);

        StringAssert.Contains(sessionRuntimeBundleIssueContractsText, "public static class SessionRuntimeBundleIssueOutcomes");
        StringAssert.Contains(sessionRuntimeBundleIssueContractsText, "public static class SessionRuntimeBundleDeliveryModes");
        StringAssert.Contains(sessionRuntimeBundleIssueContractsText, "public static class SessionRuntimeBundleTrustStates");
        StringAssert.Contains(sessionRuntimeBundleIssueContractsText, "public static class SessionRuntimeBundleRotationReasons");
        StringAssert.Contains(sessionRuntimeBundleIssueContractsText, "public sealed record SessionRuntimeBundleSignatureEnvelope");
        StringAssert.Contains(sessionRuntimeBundleIssueContractsText, "public sealed record SessionRuntimeBundleTrustDiagnostic");
        StringAssert.Contains(sessionRuntimeBundleIssueContractsText, "public sealed record SessionRuntimeBundleIssueReceipt");
        StringAssert.Contains(sessionRuntimeBundleIssueContractsText, "public sealed record SessionRuntimeBundleRotationNotice");
        StringAssert.Contains(sessionRuntimeBundleIssueContractsText, "expiring-soon");
        StringAssert.Contains(sessionRuntimeBundleIssueContractsText, "runtime-fingerprint-changed");
        StringAssert.Contains(sessionRuntimeBundleIssueContractsText, "SessionRuntimeBundle Bundle");
        Assert.IsFalse(sessionRuntimeBundleIssueContractsText.Contains("LuaSource", StringComparison.Ordinal));
        Assert.IsFalse(sessionRuntimeBundleIssueContractsText.Contains("HttpContext", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Browse_query_contracts_lock_in_search_facet_sort_preset_and_disable_reason_vocabulary()
    {
        string browseQueryContractsPath = FindPresentationContractsPath("BrowseQueryContracts.cs");
        string browseQueryContractsText = File.ReadAllText(browseQueryContractsPath);

        StringAssert.Contains(browseQueryContractsText, "public static class BrowseFacetKinds");
        StringAssert.Contains(browseQueryContractsText, "public static class BrowseSortDirections");
        StringAssert.Contains(browseQueryContractsText, "public static class BrowseValueKinds");
        StringAssert.Contains(browseQueryContractsText, "public sealed record BrowseQuery");
        StringAssert.Contains(browseQueryContractsText, "public sealed record FacetOptionDefinition");
        StringAssert.Contains(browseQueryContractsText, "public sealed record FacetDefinition");
        StringAssert.Contains(browseQueryContractsText, "public sealed record SortDefinition");
        StringAssert.Contains(browseQueryContractsText, "public sealed record ViewPreset");
        StringAssert.Contains(browseQueryContractsText, "public sealed record DisableReason");
        StringAssert.Contains(browseQueryContractsText, "public sealed record BrowseColumnDefinition");
        StringAssert.Contains(browseQueryContractsText, "public sealed record BrowseResultItem");
        StringAssert.Contains(browseQueryContractsText, "public sealed record BrowseResultPage");
        StringAssert.Contains(browseQueryContractsText, "public sealed record SelectionResult");
        StringAssert.Contains(browseQueryContractsText, "single-select");
        StringAssert.Contains(browseQueryContractsText, "multi-select");
        StringAssert.Contains(browseQueryContractsText, "availability");
        StringAssert.Contains(browseQueryContractsText, "string? DisableReasonId = null");
        Assert.IsFalse(browseQueryContractsText.Contains("Avalonia", StringComparison.Ordinal));
        Assert.IsFalse(browseQueryContractsText.Contains("Blazor", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Journal_contracts_lock_in_structured_notes_ledger_and_timeline_vocabulary()
    {
        string journalContractsPath = FindPath("Chummer.Contracts", "Journal", "JournalContracts.cs");
        string journalContractsText = File.ReadAllText(journalContractsPath);

        StringAssert.Contains(journalContractsText, "public static class JournalScopeKinds");
        StringAssert.Contains(journalContractsText, "public static class NoteBlockKinds");
        StringAssert.Contains(journalContractsText, "public static class LedgerEntryKinds");
        StringAssert.Contains(journalContractsText, "public static class TimelineEventKinds");
        StringAssert.Contains(journalContractsText, "public sealed record NoteBlock");
        StringAssert.Contains(journalContractsText, "public sealed record NoteDocument");
        StringAssert.Contains(journalContractsText, "public sealed record LedgerEntry");
        StringAssert.Contains(journalContractsText, "public sealed record TimelineEvent");
        StringAssert.Contains(journalContractsText, "public sealed record JournalProjection");
        StringAssert.Contains(journalContractsText, "training");
        StringAssert.Contains(journalContractsText, "OwnerScope Owner");
        StringAssert.Contains(journalContractsText, "string? LedgerEntryId = null");
        Assert.IsFalse(journalContractsText.Contains("Avalonia", StringComparison.Ordinal));
        Assert.IsFalse(journalContractsText.Contains("Blazor", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Runtime_inspector_contracts_lock_in_projection_warning_and_migration_preview_vocabulary()
    {
        string runtimeInspectorContractsPath = FindPath("Chummer.Contracts", "Content", "RuntimeInspectorContracts.cs");
        string runtimeInspectorContractsText = File.ReadAllText(runtimeInspectorContractsPath);

        StringAssert.Contains(runtimeInspectorContractsText, "public static class RuntimeInspectorTargetKinds");
        StringAssert.Contains(runtimeInspectorContractsText, "public static class RuntimeInspectorWarningKinds");
        StringAssert.Contains(runtimeInspectorContractsText, "public static class RuntimeInspectorWarningSeverityLevels");
        StringAssert.Contains(runtimeInspectorContractsText, "public static class RuntimeMigrationPreviewChangeKinds");
        StringAssert.Contains(runtimeInspectorContractsText, "public sealed record RuntimeInspectorRulePackEntry");
        StringAssert.Contains(runtimeInspectorContractsText, "public sealed record RuntimeInspectorProviderBinding");
        StringAssert.Contains(runtimeInspectorContractsText, "public sealed record RuntimeInspectorCapabilityDescriptorProjection");
        StringAssert.Contains(runtimeInspectorContractsText, "public sealed record RuntimeInspectorWarning");
        StringAssert.Contains(runtimeInspectorContractsText, "public sealed record RuntimeMigrationPreviewItem");
        StringAssert.Contains(runtimeInspectorContractsText, "public sealed record RuntimeInspectorProjection");
        StringAssert.Contains(runtimeInspectorContractsText, "runtime-lock");
        StringAssert.Contains(runtimeInspectorContractsText, "provider-rebound");
        StringAssert.Contains(runtimeInspectorContractsText, "ResolvedRuntimeLock RuntimeLock");
        StringAssert.Contains(runtimeInspectorContractsText, "ArtifactInstallState Install");
        StringAssert.Contains(runtimeInspectorContractsText, "IReadOnlyList<RuntimeLockCompatibilityDiagnostic> CompatibilityDiagnostics");
        StringAssert.Contains(runtimeInspectorContractsText, "IReadOnlyList<RuntimeInspectorCapabilityDescriptorProjection>? CapabilityDescriptors = null");
        StringAssert.Contains(runtimeInspectorContractsText, "string SourceKind = RegistryEntrySourceKinds.PersistedManifest");
        StringAssert.Contains(runtimeInspectorContractsText, "string ProfileSourceKind = RegistryEntrySourceKinds.PersistedManifest");
        StringAssert.Contains(runtimeInspectorContractsText, "string? MessageKey = null");
        StringAssert.Contains(runtimeInspectorContractsText, "IReadOnlyList<RulesetExplainParameter>? MessageParameters = null");
        StringAssert.Contains(runtimeInspectorContractsText, "string? SummaryKey = null");
        StringAssert.Contains(runtimeInspectorContractsText, "IReadOnlyList<RulesetExplainParameter>? SummaryParameters = null");
        Assert.IsFalse(runtimeInspectorContractsText.Contains("Avalonia", StringComparison.Ordinal));
        Assert.IsFalse(runtimeInspectorContractsText.Contains("Blazor", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Runtime_lock_diff_contracts_lock_in_before_after_change_vocabulary()
    {
        string runtimeLockDiffContractsPath = FindPath("Chummer.Contracts", "Content", "RuntimeLockDiffContracts.cs");
        string runtimeLockDiffContractsText = File.ReadAllText(runtimeLockDiffContractsPath);
        string runtimeLockDiffServiceContractPath = FindPath("Chummer.Application", "Content", "IRuntimeLockDiffService.cs");
        string runtimeLockDiffServiceContractText = File.ReadAllText(runtimeLockDiffServiceContractPath);
        string runtimeLockDiffServicePath = FindPath("Chummer.Application", "Content", "DefaultRuntimeLockDiffService.cs");
        string runtimeLockDiffServiceText = File.ReadAllText(runtimeLockDiffServicePath);

        StringAssert.Contains(runtimeLockDiffContractsText, "public static class RuntimeLockDiffChangeKinds");
        StringAssert.Contains(runtimeLockDiffContractsText, "public sealed record RuntimeLockDiffChange");
        StringAssert.Contains(runtimeLockDiffContractsText, "public sealed record RuntimeLockDiffProjection");
        StringAssert.Contains(runtimeLockDiffContractsText, "provider-binding-changed");
        StringAssert.Contains(runtimeLockDiffServiceContractText, "public interface IRuntimeLockDiffService");
        StringAssert.Contains(runtimeLockDiffServiceContractText, "RuntimeLockDiffProjection Diff");
        StringAssert.Contains(runtimeLockDiffServiceText, "public sealed class DefaultRuntimeLockDiffService");
        StringAssert.Contains(runtimeLockDiffServiceText, "runtime.diff.rulepack.added");
        StringAssert.Contains(runtimeLockDiffServiceText, "runtime.diff.provider-binding.changed");
    }

    [TestMethod]
    public void Rulepack_workbench_contracts_lock_in_library_inspector_graph_validation_and_override_vocabulary()
    {
        string rulePackWorkbenchContractsPath = FindPresentationContractsPath("RulePackWorkbenchContracts.cs");
        string rulePackWorkbenchContractsText = File.ReadAllText(rulePackWorkbenchContractsPath);

        StringAssert.Contains(rulePackWorkbenchContractsText, "public static class RulePackWorkbenchSurfaceIds");
        StringAssert.Contains(rulePackWorkbenchContractsText, "public static class RulePackInstallStates");
        StringAssert.Contains(rulePackWorkbenchContractsText, "public static class RulePackDependencyEdgeKinds");
        StringAssert.Contains(rulePackWorkbenchContractsText, "public static class RulePackValidationIssueKinds");
        StringAssert.Contains(rulePackWorkbenchContractsText, "public sealed record RulePackWorkbenchListItem");
        StringAssert.Contains(rulePackWorkbenchContractsText, "public sealed record RulePackDependencyNode");
        StringAssert.Contains(rulePackWorkbenchContractsText, "public sealed record RulePackDependencyEdge");
        StringAssert.Contains(rulePackWorkbenchContractsText, "public sealed record RulePackValidationIssue");
        StringAssert.Contains(rulePackWorkbenchContractsText, "public sealed record DeclarativeOverrideDraft");
        StringAssert.Contains(rulePackWorkbenchContractsText, "public sealed record RulePackLibraryProjection");
        StringAssert.Contains(rulePackWorkbenchContractsText, "public sealed record RulePackInspectorProjection");
        StringAssert.Contains(rulePackWorkbenchContractsText, "public sealed record RulePackDependencyGraphProjection");
        StringAssert.Contains(rulePackWorkbenchContractsText, "public sealed record RulePackValidationPanelProjection");
        StringAssert.Contains(rulePackWorkbenchContractsText, "public sealed record DeclarativeOverrideEditorProjection");
        StringAssert.Contains(rulePackWorkbenchContractsText, "dependency-graph-view");
        StringAssert.Contains(rulePackWorkbenchContractsText, "review-required");
        StringAssert.Contains(rulePackWorkbenchContractsText, "declarative-override");
        Assert.IsFalse(rulePackWorkbenchContractsText.Contains("Avalonia", StringComparison.Ordinal));
        Assert.IsFalse(rulePackWorkbenchContractsText.Contains("Blazor", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Campaign_and_gm_board_contracts_lock_in_party_roster_tracker_board_and_round_marker_vocabulary()
    {
        string campaignProjectionContractsPath = FindPath("Chummer.Contracts", "Campaign", "CampaignProjectionContracts.cs");
        string campaignProjectionContractsText = File.ReadAllText(campaignProjectionContractsPath);

        StringAssert.Contains(campaignProjectionContractsText, "public static class CampaignParticipantRoles");
        StringAssert.Contains(campaignProjectionContractsText, "public static class CombatRoundMarkerStates");
        StringAssert.Contains(campaignProjectionContractsText, "public sealed record CampaignDescriptor");
        StringAssert.Contains(campaignProjectionContractsText, "public sealed record PartyRosterEntry");
        StringAssert.Contains(campaignProjectionContractsText, "public sealed record InitiativeOrderEntry");
        StringAssert.Contains(campaignProjectionContractsText, "public sealed record ParticipantSessionTile");
        StringAssert.Contains(campaignProjectionContractsText, "public sealed record GmTrackerBoardTile");
        StringAssert.Contains(campaignProjectionContractsText, "public sealed record CombatRoundMarker");
        StringAssert.Contains(campaignProjectionContractsText, "public sealed record GmBoardProjection");
        StringAssert.Contains(campaignProjectionContractsText, "game-master");
        StringAssert.Contains(campaignProjectionContractsText, "string Visibility");
        StringAssert.Contains(campaignProjectionContractsText, "SessionSyncBanner? SyncBanner = null");
        StringAssert.Contains(campaignProjectionContractsText, "IReadOnlyList<NoteDocument> Notes");
        Assert.IsFalse(campaignProjectionContractsText.Contains("Avalonia", StringComparison.Ordinal));
        Assert.IsFalse(campaignProjectionContractsText.Contains("Blazor", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Buildkit_workbench_contracts_lock_in_library_inspector_prompt_and_apply_preview_vocabulary()
    {
        string buildKitWorkbenchContractsPath = FindPresentationContractsPath("BuildKitWorkbenchContracts.cs");
        string buildKitWorkbenchContractsText = File.ReadAllText(buildKitWorkbenchContractsPath);

        StringAssert.Contains(buildKitWorkbenchContractsText, "public static class BuildKitWorkbenchSurfaceIds");
        StringAssert.Contains(buildKitWorkbenchContractsText, "public static class BuildKitAvailabilityStates");
        StringAssert.Contains(buildKitWorkbenchContractsText, "public static class BuildKitPreviewChangeKinds");
        StringAssert.Contains(buildKitWorkbenchContractsText, "public sealed record BuildKitLibraryItem");
        StringAssert.Contains(buildKitWorkbenchContractsText, "public sealed record BuildKitPromptPreview");
        StringAssert.Contains(buildKitWorkbenchContractsText, "public sealed record BuildKitPreviewChange");
        StringAssert.Contains(buildKitWorkbenchContractsText, "public sealed record BuildKitLibraryProjection");
        StringAssert.Contains(buildKitWorkbenchContractsText, "public sealed record BuildKitInspectorProjection");
        StringAssert.Contains(buildKitWorkbenchContractsText, "public sealed record BuildKitApplyPreviewProjection");
        StringAssert.Contains(buildKitWorkbenchContractsText, "buildkit-library");
        StringAssert.Contains(buildKitWorkbenchContractsText, "requires-runtime-change");
        StringAssert.Contains(buildKitWorkbenchContractsText, "career-update-queued");
        StringAssert.Contains(buildKitWorkbenchContractsText, "BuildKitValidationReceipt Validation");
        Assert.IsFalse(buildKitWorkbenchContractsText.Contains("Avalonia", StringComparison.Ordinal));
        Assert.IsFalse(buildKitWorkbenchContractsText.Contains("Blazor", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Journal_panel_contracts_lock_in_notes_ledger_and_timeline_panel_vocabulary()
    {
        string journalPanelContractsPath = FindPresentationContractsPath("JournalPanelContracts.cs");
        string journalPanelContractsText = File.ReadAllText(journalPanelContractsPath);

        StringAssert.Contains(journalPanelContractsText, "public static class JournalPanelSurfaceIds");
        StringAssert.Contains(journalPanelContractsText, "public static class JournalPanelSectionKinds");
        StringAssert.Contains(journalPanelContractsText, "public sealed record NoteListItem");
        StringAssert.Contains(journalPanelContractsText, "public sealed record LedgerEntryView");
        StringAssert.Contains(journalPanelContractsText, "public sealed record TimelineEventView");
        StringAssert.Contains(journalPanelContractsText, "public sealed record JournalPanelSection");
        StringAssert.Contains(journalPanelContractsText, "public sealed record JournalPanelProjection");
        StringAssert.Contains(journalPanelContractsText, "notes-panel");
        StringAssert.Contains(journalPanelContractsText, "campaign-journal-panel");
        StringAssert.Contains(journalPanelContractsText, "string? LedgerEntryId = null");
        Assert.IsFalse(journalPanelContractsText.Contains("Avalonia", StringComparison.Ordinal));
        Assert.IsFalse(journalPanelContractsText.Contains("Blazor", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Browse_workspace_contracts_lock_in_workspace_and_selection_dialog_vocabulary()
    {
        string browseWorkspaceContractsPath = FindPresentationContractsPath("BrowseWorkspaceContracts.cs");
        string browseWorkspaceContractsText = File.ReadAllText(browseWorkspaceContractsPath);

        StringAssert.Contains(browseWorkspaceContractsText, "public static class BrowseWorkspaceSurfaceIds");
        StringAssert.Contains(browseWorkspaceContractsText, "public static class BrowseWorkspaceSectionKinds");
        StringAssert.Contains(browseWorkspaceContractsText, "public static class SelectionDialogModes");
        StringAssert.Contains(browseWorkspaceContractsText, "public sealed record BrowseWorkspaceSection");
        StringAssert.Contains(browseWorkspaceContractsText, "public sealed record BrowseItemDetail");
        StringAssert.Contains(browseWorkspaceContractsText, "public sealed record SelectionSummaryItem");
        StringAssert.Contains(browseWorkspaceContractsText, "public sealed record BrowseWorkspaceProjection");
        StringAssert.Contains(browseWorkspaceContractsText, "public sealed record SelectionDialogProjection");
        StringAssert.Contains(browseWorkspaceContractsText, "browse-workspace");
        StringAssert.Contains(browseWorkspaceContractsText, "selection-summary");
        StringAssert.Contains(browseWorkspaceContractsText, "BrowseResultPage Results");
        StringAssert.Contains(browseWorkspaceContractsText, "BrowseWorkspaceProjection Workspace");
        Assert.IsFalse(browseWorkspaceContractsText.Contains("Avalonia", StringComparison.Ordinal));
        Assert.IsFalse(browseWorkspaceContractsText.Contains("Blazor", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Session_contracts_lock_in_ledger_snapshot_and_runtime_bundle_vocabulary()
    {
        string characterVersionContractsPath = FindPath("Chummer.Contracts", "Characters", "CharacterVersionContracts.cs");
        string characterVersionContractsText = File.ReadAllText(characterVersionContractsPath);
        string trackerContractsPath = FindPath("Chummer.Contracts", "Trackers", "TrackerContracts.cs");
        string trackerContractsText = File.ReadAllText(trackerContractsPath);
        string sessionContractsPath = FindPath("Chummer.Contracts", "Session", "SessionContracts.cs");
        string sessionContractsText = File.ReadAllText(sessionContractsPath);

        StringAssert.Contains(characterVersionContractsText, "public sealed record CharacterVersionReference");
        StringAssert.Contains(characterVersionContractsText, "public sealed record CharacterVersion");
        StringAssert.Contains(characterVersionContractsText, "ResolvedRuntimeLock RuntimeLock");
        StringAssert.Contains(characterVersionContractsText, "WorkspacePayloadEnvelope PayloadEnvelope");
        StringAssert.Contains(trackerContractsText, "public static class TrackerCategories");
        StringAssert.Contains(trackerContractsText, "public sealed record TrackerThresholdDefinition");
        StringAssert.Contains(trackerContractsText, "public sealed record TrackerDefinition");
        StringAssert.Contains(trackerContractsText, "public sealed record TrackerSnapshot");
        StringAssert.Contains(sessionContractsText, "public static class SessionEventEnvelopeSchemas");
        StringAssert.Contains(sessionContractsText, "public sealed record SessionEventEnvelope");
        StringAssert.Contains(sessionContractsText, "public static class SessionEventTypes");
        StringAssert.Contains(sessionContractsText, "public static class SessionSyncStatuses");
        StringAssert.Contains(sessionContractsText, "public sealed record SessionEvent");
        StringAssert.Contains(sessionContractsText, "public sealed record SessionLedger");
        StringAssert.Contains(sessionContractsText, "public sealed record SessionOverlaySnapshot");
        StringAssert.Contains(sessionContractsText, "public sealed record SessionRuntimeBundle");
        StringAssert.Contains(sessionContractsText, "session_events_vnext");
        StringAssert.Contains(sessionContractsText, "tracker.increment");
        StringAssert.Contains(sessionContractsText, "tracker.decrement");
        StringAssert.Contains(sessionContractsText, "effect.applied");
        StringAssert.Contains(sessionContractsText, "effect.removed");
        StringAssert.Contains(sessionContractsText, "ammo.spent");
        StringAssert.Contains(sessionContractsText, "ammo.reloaded");
        StringAssert.Contains(sessionContractsText, "note.added");
        StringAssert.Contains(sessionContractsText, "pin.changed");
        StringAssert.Contains(sessionContractsText, "CharacterVersionReference BaseCharacterVersion");
        StringAssert.Contains(sessionContractsText, "IReadOnlyDictionary<string, RulesetCapabilityValue> Payload");
        StringAssert.Contains(sessionContractsText, "IReadOnlyList<TrackerSnapshot> Trackers");
        StringAssert.Contains(sessionContractsText, "IReadOnlyList<TrackerDefinition> Trackers");
        StringAssert.Contains(sessionContractsText, "SignedAtUtc");
        StringAssert.Contains(sessionContractsText, "[Obsolete(\"Compatibility-only. Use SessionEventEnvelope.\")]");
        Assert.IsFalse(sessionContractsText.Contains("public sealed record SessionTrackerDefinition", StringComparison.Ordinal));
        Assert.IsFalse(sessionContractsText.Contains("public sealed record SessionOverlay(", StringComparison.Ordinal));
        Assert.IsFalse(sessionContractsText.Contains("LuaSource", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Linked_asset_contracts_lock_in_contact_library_vocabulary()
    {
        string linkedAssetContractsPath = FindPath("Chummer.Contracts", "Assets", "LinkedAssetContracts.cs");
        string linkedAssetContractsText = File.ReadAllText(linkedAssetContractsPath);
        string sectionContractsPath = FindPath("Chummer.Contracts", "Characters", "CharacterSectionModels.cs");
        string sectionContractsText = File.ReadAllText(sectionContractsPath);

        StringAssert.Contains(linkedAssetContractsText, "public static class LinkedAssetVisibilityModes");
        StringAssert.Contains(linkedAssetContractsText, "public sealed record LinkedAssetReference");
        StringAssert.Contains(linkedAssetContractsText, "public sealed record ContactAsset");
        StringAssert.Contains(linkedAssetContractsText, "public sealed record ContactLinkOverride");
        StringAssert.Contains(linkedAssetContractsText, "public sealed record CharacterContactLink");
        StringAssert.Contains(sectionContractsText, "public sealed record CharacterContactSummary");
        StringAssert.Contains(sectionContractsText, "public sealed record CharacterContactsSection");
        Assert.IsFalse(linkedAssetContractsText.Contains("public sealed record ContactSection", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Design_token_contracts_lock_in_shared_theme_scale_and_touch_vocabulary()
    {
        string designTokenContractsPath = FindPresentationContractsPath("DesignTokenContracts.cs");
        string designTokenContractsText = File.ReadAllText(designTokenContractsPath);

        StringAssert.Contains(designTokenContractsText, "public static class ThemeModes");
        StringAssert.Contains(designTokenContractsText, "public static class TypographyScales");
        StringAssert.Contains(designTokenContractsText, "public static class DensityModes");
        StringAssert.Contains(designTokenContractsText, "public static class ContrastModes");
        StringAssert.Contains(designTokenContractsText, "public static class TouchTargetModes");
        StringAssert.Contains(designTokenContractsText, "public sealed record DesignTokenSet");
        Assert.IsFalse(designTokenContractsText.Contains("Avalonia", StringComparison.Ordinal));
        Assert.IsFalse(designTokenContractsText.Contains("Blazor", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Session_sync_contracts_lock_in_batch_receipt_and_conflict_vocabulary()
    {
        string sessionSyncContractsPath = FindPath("Chummer.Contracts", "Session", "SessionSyncContracts.cs");
        string sessionSyncContractsText = File.ReadAllText(sessionSyncContractsPath);

        StringAssert.Contains(sessionSyncContractsText, "public static class SessionConflictKinds");
        StringAssert.Contains(sessionSyncContractsText, "public sealed record SessionPendingEventState");
        StringAssert.Contains(sessionSyncContractsText, "public sealed record SessionSyncBatch");
        StringAssert.Contains(sessionSyncContractsText, "public sealed record SessionReplayReceipt");
        StringAssert.Contains(sessionSyncContractsText, "public sealed record SessionConflictDiagnostic");
        StringAssert.Contains(sessionSyncContractsText, "public sealed record SessionSyncReceipt");
        StringAssert.Contains(sessionSyncContractsText, "CharacterVersionReference BaseCharacterVersion");
        StringAssert.Contains(sessionSyncContractsText, "IReadOnlyList<SessionEventEnvelope> Events");
        Assert.IsFalse(sessionSyncContractsText.Contains("last-write-wins", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Ruleset_explain_contracts_lock_in_trace_and_gas_vocabulary()
    {
        string explainContractsPath = FindPath("Chummer.Contracts", "Rulesets", "RulesetExplainContracts.cs");
        string explainContractsText = File.ReadAllText(explainContractsPath);
        string rulesetContractsPath = FindPath("Chummer.Contracts", "Rulesets", "RulesetContracts.cs");
        string rulesetContractsText = File.ReadAllText(rulesetContractsPath);
        string rulesetCapabilityContractsPath = FindPath("Chummer.Contracts", "Rulesets", "RulesetCapabilityContracts.cs");
        string rulesetCapabilityContractsText = File.ReadAllText(rulesetCapabilityContractsPath);

        StringAssert.Contains(explainContractsText, "public sealed record RulesetGasBudget");
        StringAssert.Contains(explainContractsText, "public sealed record RulesetExecutionOptions");
        StringAssert.Contains(explainContractsText, "public sealed record RulesetGasUsage");
        StringAssert.Contains(explainContractsText, "public sealed record RulesetExplainParameter");
        StringAssert.Contains(explainContractsText, "public sealed record RulesetTraceStep");
        StringAssert.Contains(explainContractsText, "public sealed record RulesetProviderTrace");
        StringAssert.Contains(explainContractsText, "public sealed record RulesetExplainTrace");
        StringAssert.Contains(explainContractsText, "ProviderInstructionLimit");
        StringAssert.Contains(explainContractsText, "WallClockLimit");
        StringAssert.Contains(rulesetContractsText, "RulesetExecutionOptions? Options = null");
        StringAssert.Contains(rulesetContractsText, "RulesetExplainTrace? Explain = null");
        StringAssert.Contains(rulesetCapabilityContractsText, "public sealed record RulesetCapabilityDiagnostic");
        StringAssert.Contains(rulesetCapabilityContractsText, "string? MessageKey = null");
        StringAssert.Contains(rulesetCapabilityContractsText, "IReadOnlyList<RulesetExplainParameter>? MessageParameters = null");
        StringAssert.Contains(rulesetCapabilityContractsText, "string? TitleKey = null");
        StringAssert.Contains(rulesetCapabilityContractsText, "IReadOnlyList<RulesetExplainParameter>? TitleParameters = null");
    }

    [TestMethod]
    public void Explain_trace_contracts_lock_in_localization_safe_provenance_and_evidence_vocabulary()
    {
        string explainTracePath = FindPath("Chummer.Contracts", "ExplainTraceDto.cs");
        string explainTraceText = File.ReadAllText(explainTracePath);
        string traceStepPath = FindPath("Chummer.Contracts", "TraceStepDto.cs");
        string traceStepText = File.ReadAllText(traceStepPath);
        string evidencePath = FindPath("Chummer.Contracts", "ExplainEvidencePointerDto.cs");
        string evidenceText = File.ReadAllText(evidencePath);
        string provenancePath = FindPath("Chummer.Contracts", "ExplainProvenanceDto.cs");
        string provenanceText = File.ReadAllText(provenancePath);

        StringAssert.Contains(explainTraceText, "RulesetCapabilityValue? FinalValue");
        StringAssert.Contains(explainTraceText, "IReadOnlyList<RulesetExplainParameter> SummaryParameters");
        StringAssert.Contains(explainTraceText, "ExplainProvenanceDto? Provenance = null");
        StringAssert.Contains(explainTraceText, "IReadOnlyList<ExplainEvidencePointerDto>? Evidence = null");
        StringAssert.Contains(traceStepText, "string CapabilityId");
        StringAssert.Contains(traceStepText, "string? PackId");
        StringAssert.Contains(traceStepText, "string ExplanationKey");
        StringAssert.Contains(traceStepText, "IReadOnlyList<RulesetExplainParameter> ExplanationParameters");
        StringAssert.Contains(traceStepText, "decimal? Modifier = null");
        StringAssert.Contains(traceStepText, "bool? Certain = null");
        StringAssert.Contains(evidenceText, "IReadOnlyList<RulesetExplainParameter>? LabelParameters = null");
        StringAssert.Contains(provenanceText, "public record ExplainProvenanceDto");
        StringAssert.Contains(provenanceText, "string RuntimeFingerprint");
        StringAssert.Contains(provenanceText, "IReadOnlyList<string>? RulePacks = null");
        StringAssert.Contains(provenanceText, "IReadOnlyDictionary<string, string>? ProviderBindings = null");
        Assert.IsFalse(explainTraceText.Contains("int FinalValue", StringComparison.Ordinal));
        Assert.IsFalse(explainTraceText.Contains("IReadOnlyDictionary<string, string> SummaryParameters", StringComparison.Ordinal));
        Assert.IsFalse(traceStepText.Contains("string SourcePackId", StringComparison.Ordinal));
        Assert.IsFalse(traceStepText.Contains("string LocalizationKey", StringComparison.Ordinal));
        Assert.IsFalse(traceStepText.Contains("IReadOnlyDictionary<string, string> LocalizationParameters", StringComparison.Ordinal));
        Assert.IsFalse(evidenceText.Contains("IReadOnlyDictionary<string, string>? LabelParameters", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Ruleset_typed_capability_contracts_lock_in_schema_and_descriptor_vocabulary()
    {
        string typedCapabilityContractsPath = FindPath("Chummer.Contracts", "Rulesets", "RulesetTypedCapabilityContracts.cs");
        string typedCapabilityContractsText = File.ReadAllText(typedCapabilityContractsPath);
        string artifactContractsPath = FindPath("Chummer.Contracts", "Content", "ArtifactContracts.cs");
        string artifactContractsText = File.ReadAllText(artifactContractsPath);

        StringAssert.Contains(typedCapabilityContractsText, "public sealed record TypedCapabilityContractDescriptor");
        StringAssert.Contains(typedCapabilityContractsText, "public static class TypedCapabilitySchemaIds");
        StringAssert.Contains(typedCapabilityContractsText, "public static class RulesetTypedCapabilityCatalog");
        StringAssert.Contains(typedCapabilityContractsText, "derive.attribute-limit.input.v1");
        StringAssert.Contains(typedCapabilityContractsText, "buildlab.recommendation.output.v1");
        StringAssert.Contains(artifactContractsText, "public const string DeriveAttributeLimit = \"derive.attribute-limit\"");
        StringAssert.Contains(artifactContractsText, "public const string DeriveInitiative = \"derive.initiative\"");
        StringAssert.Contains(artifactContractsText, "public const string ValidateChoice = \"validate.choice\"");
        StringAssert.Contains(artifactContractsText, "public const string BuildLabRecommendation = \"buildlab.recommendation\"");
    }

    [TestMethod]
    public void Session_overlay_projection_contracts_lock_in_delta_only_event_projection_vocabulary()
    {
        string overlayContractsPath = FindPath("Chummer.Contracts", "Session", "SessionOverlayProjectionContracts.cs");
        string overlayContractsText = File.ReadAllText(overlayContractsPath);
        string projectionServicePath = FindPath("Chummer.Application", "Session", "DefaultSessionOverlayProjectionService.cs");
        string projectionServiceText = File.ReadAllText(projectionServicePath);

        StringAssert.Contains(overlayContractsText, "public static class SessionOverlayEventKinds");
        StringAssert.Contains(overlayContractsText, "public sealed record SessionOverlayEventDto");
        StringAssert.Contains(overlayContractsText, "public sealed record SessionOverlayProjection");
        StringAssert.Contains(overlayContractsText, "[Obsolete(\"Compatibility-only. Use SessionEventEnvelope.\")]");
        StringAssert.Contains(overlayContractsText, "IReadOnlyList<SessionEventEnvelope> AppliedEvents");
        StringAssert.Contains(projectionServiceText, "public sealed class DefaultSessionOverlayProjectionService");
        StringAssert.Contains(projectionServiceText, "switch (item.EventType)");
        StringAssert.Contains(projectionServiceText, "SessionOverlayEventValidator.AllowsEvent");
        StringAssert.Contains(projectionServiceText, "session.replay.absolute-write-blocked");
        Assert.IsFalse(projectionServiceText.Contains("CurrentEdge =", StringComparison.Ordinal));
        Assert.IsFalse(projectionServiceText.Contains("currentEdge =", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Build_lab_contracts_lock_in_structured_simulation_service_surface()
    {
        string buildLabContractsPath = FindPath("Chummer.Contracts", "BuildLab", "BuildLabContracts.cs");
        string buildLabContractsText = File.ReadAllText(buildLabContractsPath);
        string buildLabServicePath = FindPath("Chummer.Application", "BuildLab", "IBuildLabService.cs");
        string buildLabServiceText = File.ReadAllText(buildLabServicePath);

        StringAssert.Contains(buildLabContractsText, "public sealed record BuildVariantProjection");
        StringAssert.Contains(buildLabContractsText, "public sealed record KarmaSpendProjection");
        StringAssert.Contains(buildLabContractsText, "public sealed record BuildTrapChoice");
        StringAssert.Contains(buildLabContractsText, "IReadOnlyList<RulesetExplainParameter> LabelParameters");
        StringAssert.Contains(buildLabContractsText, "string SummaryKey");
        StringAssert.Contains(buildLabContractsText, "IReadOnlyList<RulesetCapabilityDiagnostic>? Diagnostics = null");
        StringAssert.Contains(buildLabServiceText, "GenerateBuildVariants");
        StringAssert.Contains(buildLabServiceText, "ScoreBuildVariant");
        StringAssert.Contains(buildLabServiceText, "ProjectKarmaSpend");
        StringAssert.Contains(buildLabServiceText, "DetectTrapChoices");
        StringAssert.Contains(buildLabServiceText, "DetectRoleOverlap");
        StringAssert.Contains(buildLabServiceText, "SuggestCorePackages");
    }

    [TestMethod]
    public void Semantic_seed_contracts_lock_in_dossier_and_shadowfeed_seed_vocabulary()
    {
        string seedContractsPath = FindPath("Chummer.Contracts", "Seeds", "SemanticSeedContracts.cs");
        string seedContractsText = File.ReadAllText(seedContractsPath);
        string seedServicePath = FindPath("Chummer.Application", "Seeds", "ISemanticSeedService.cs");
        string seedServiceText = File.ReadAllText(seedServicePath);
        string aestheticDigestServicePath = FindPath("Chummer.Application", "Seeds", "IAestheticDigestService.cs");
        string aestheticDigestServiceText = File.ReadAllText(aestheticDigestServicePath);
        string defaultAestheticDigestServicePath = FindPath("Chummer.Application", "Seeds", "DefaultAestheticDigestService.cs");
        string defaultAestheticDigestServiceText = File.ReadAllText(defaultAestheticDigestServicePath);

        StringAssert.Contains(seedContractsText, "public sealed record CharacterDossierSeed");
        StringAssert.Contains(seedContractsText, "public sealed record NpcDossierSeed");
        StringAssert.Contains(seedContractsText, "public sealed record RunSummarySeed");
        StringAssert.Contains(seedContractsText, "public sealed record BuildIdeaSeed");
        StringAssert.Contains(seedContractsText, "public sealed record ShadowfeedSeed");
        StringAssert.Contains(seedServiceText, "BuildCharacterDossierSeed");
        StringAssert.Contains(seedServiceText, "BuildNpcDossierSeed");
        StringAssert.Contains(seedServiceText, "BuildRunSummarySeed");
        StringAssert.Contains(seedServiceText, "BuildBuildIdeaSeed");
        StringAssert.Contains(seedServiceText, "BuildShadowfeedSeed");
        StringAssert.Contains(aestheticDigestServiceText, "public interface IAestheticDigestService");
        StringAssert.Contains(defaultAestheticDigestServiceText, "public sealed class DefaultAestheticDigestService");
        StringAssert.Contains(defaultAestheticDigestServiceText, "CharacterAestheticDigest");
    }

    [TestMethod]
    public void Relationship_and_heat_simulation_contracts_lock_in_computation_primitives()
    {
        string simulationContractsPath = FindPath("Chummer.Contracts", "Simulation", "RelationshipHeatContracts.cs");
        string simulationContractsText = File.ReadAllText(simulationContractsPath);
        string simulationServiceContractPath = FindPath("Chummer.Application", "Simulation", "IRelationshipHeatService.cs");
        string simulationServiceContractText = File.ReadAllText(simulationServiceContractPath);
        string simulationServicePath = FindPath("Chummer.Application", "Simulation", "DefaultRelationshipHeatService.cs");
        string simulationServiceText = File.ReadAllText(simulationServicePath);

        StringAssert.Contains(simulationContractsText, "public sealed record HeatComputationInput");
        StringAssert.Contains(simulationContractsText, "public sealed record PublicAwarenessComputationInput");
        StringAssert.Contains(simulationContractsText, "public sealed record FavorDebtComputationInput");
        StringAssert.Contains(simulationContractsText, "public sealed record DowntimeProgressionInput");
        StringAssert.Contains(simulationContractsText, "public sealed record AddictionScheduleInput");
        StringAssert.Contains(simulationContractsText, "public sealed record HealingScheduleInput");
        StringAssert.Contains(simulationContractsText, "public sealed record FactionResponseSeed");
        StringAssert.Contains(simulationServiceContractText, "ComputeHeat");
        StringAssert.Contains(simulationServiceContractText, "ComputePublicAwareness");
        StringAssert.Contains(simulationServiceContractText, "ComputeFavorDebt");
        StringAssert.Contains(simulationServiceContractText, "ComputeDowntimeProgression");
        StringAssert.Contains(simulationServiceContractText, "ComputeAddictionSchedule");
        StringAssert.Contains(simulationServiceContractText, "ComputeHealingSchedule");
        StringAssert.Contains(simulationServiceContractText, "ComputeFactionResponseSeed");
        StringAssert.Contains(simulationServiceText, "heat.low");
        StringAssert.Contains(simulationServiceText, "faction.response.extreme");
    }

    [TestMethod]
    public void Api_registers_request_owner_context_accessor_with_opt_in_forwarded_owner_support()
    {
        string apiProgramPath = FindPath("Chummer.Api", "Program.cs");
        string apiProgramText = File.ReadAllText(apiProgramPath);
        string requestOwnerAccessorPath = FindPath("Chummer.Api", "Owners", "RequestOwnerContextAccessor.cs");
        string requestOwnerAccessorText = File.ReadAllText(requestOwnerAccessorPath);
        string portalProgramPath = FindPath("Chummer.Portal", "Program.cs");
        string portalProgramText = File.ReadAllText(portalProgramPath);
        string portalOwnerPropagationPath = FindPath("Chummer.Portal", "PortalAuthenticatedOwnerPropagation.cs");
        string portalOwnerPropagationText = File.ReadAllText(portalOwnerPropagationPath);
        string portalAuthenticationEndpointsPath = FindPath("Chummer.Portal", "PortalAuthenticationEndpoints.cs");
        string portalAuthenticationEndpointsText = File.ReadAllText(portalAuthenticationEndpointsPath);
        string portalProtectedRouteMatcherPath = FindPath("Chummer.Portal", "PortalProtectedRouteMatcher.cs");
        string portalProtectedRouteMatcherText = File.ReadAllText(portalProtectedRouteMatcherPath);
        string portalPageBuilderPath = FindPath("Chummer.Portal", "PortalPageBuilder.cs");
        string portalPageBuilderText = File.ReadAllText(portalPageBuilderPath);
        string ownerContractPath = FindPath("Chummer.Contracts", "Owners", "PortalOwnerPropagationContract.cs");
        string ownerContractText = File.ReadAllText(ownerContractPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);
        string backlogPath = FindPath("docs", "MIGRATION_BACKLOG.md");
        string backlogText = File.ReadAllText(backlogPath);

        StringAssert.Contains(apiProgramText, "AddHttpContextAccessor();");
        StringAssert.Contains(apiProgramText, "CHUMMER_ALLOW_OWNER_HEADER");
        StringAssert.Contains(apiProgramText, "CHUMMER_OWNER_HEADER_NAME");
        StringAssert.Contains(apiProgramText, "CHUMMER_PORTAL_OWNER_MAX_AGE_SECONDS");
        StringAssert.Contains(apiProgramText, "PortalOwnerPropagationContract.SharedKeyEnvironmentVariable");
        StringAssert.Contains(apiProgramText, "AddSingleton<IOwnerContextAccessor>(");
        StringAssert.Contains(apiProgramText, "new RequestOwnerContextAccessor(");
        StringAssert.Contains(apiProgramText, "\"X-Chummer-Owner\"");
        StringAssert.Contains(apiProgramText, "ResolvePortalOwnerSharedKey");
        StringAssert.Contains(apiProgramText, "Treat X-Api-Key mode as local/dev/ops or private-upstream protection");
        StringAssert.Contains(apiProgramText, "Hosted/public deployments should expose Chummer.Portal as the public edge and keep Chummer.Api private behind signed portal-owner propagation.");
        StringAssert.Contains(apiProgramText, "Neither CHUMMER_API_KEY nor CHUMMER_PORTAL_OWNER_SHARED_KEY is configured");

        StringAssert.Contains(requestOwnerAccessorText, "public sealed class RequestOwnerContextAccessor");
        StringAssert.Contains(requestOwnerAccessorText, "OwnerScope.LocalSingleUser");
        StringAssert.Contains(requestOwnerAccessorText, "ClaimTypes.NameIdentifier");
        StringAssert.Contains(requestOwnerAccessorText, "principal.FindFirst(\"sub\")?.Value");
        StringAssert.Contains(requestOwnerAccessorText, "PortalOwnerPropagationContract.OwnerHeaderName");
        StringAssert.Contains(requestOwnerAccessorText, "ResolvePortalAuthenticatedOwner");
        StringAssert.Contains(requestOwnerAccessorText, "context.Request.Headers[_headerName].FirstOrDefault()");
        StringAssert.Contains(requestOwnerAccessorText, "CreatePortalOwnerSignature");
        StringAssert.Contains(requestOwnerAccessorText, "CryptographicOperations.FixedTimeEquals");
        StringAssert.Contains(portalProgramText, "PortalOwnerPropagationContract.SharedKeyEnvironmentVariable");
        StringAssert.Contains(portalProgramText, "PortalAuthenticatedOwnerPropagation.Apply");
        StringAssert.Contains(portalProgramText, "CHUMMER_PORTAL_REQUIRE_AUTH");
        StringAssert.Contains(portalProgramText, "CHUMMER_PORTAL_DEV_AUTH_ENABLED");
        StringAssert.Contains(portalProgramText, "CHUMMER_PORTAL_HUB_URL");
        StringAssert.Contains(portalProgramText, "CHUMMER_PORTAL_HUB_PROXY_URL");
        StringAssert.Contains(portalProgramText, "CHUMMER_PORTAL_SESSION_URL");
        StringAssert.Contains(portalProgramText, "CHUMMER_PORTAL_SESSION_PROXY_URL");
        StringAssert.Contains(portalProgramText, "RouteId = \"portal-hub\"");
        StringAssert.Contains(portalProgramText, "Path = \"/hub/{**catch-all}\"");
        StringAssert.Contains(portalProgramText, "RouteId = \"portal-session\"");
        StringAssert.Contains(portalProgramText, "Path = \"/session/{**catch-all}\"");
        StringAssert.Contains(portalProgramText, "AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)");
        StringAssert.Contains(portalProgramText, "PortalAuthenticationEndpoints.MapPortalAuthenticationEndpoints");
        StringAssert.Contains(portalProgramText, "PortalProtectedRouteMatcher.RequiresAuthenticatedUser");
        StringAssert.Contains(portalOwnerPropagationText, "public static class PortalAuthenticatedOwnerPropagation");
        StringAssert.Contains(portalOwnerPropagationText, "PortalOwnerPropagationContract.OwnerHeaderName");
        StringAssert.Contains(portalOwnerPropagationText, "ClaimTypes.NameIdentifier");
        StringAssert.Contains(portalOwnerPropagationText, "path.StartsWithSegments(\"/api\"");
        StringAssert.Contains(portalAuthenticationEndpointsText, "public static class PortalAuthenticationEndpoints");
        StringAssert.Contains(portalAuthenticationEndpointsText, "MapPortalAuthenticationEndpoints");
        StringAssert.Contains(portalAuthenticationEndpointsText, "context.SignInAsync");
        StringAssert.Contains(portalAuthenticationEndpointsText, "new ClaimsIdentity(claims, \"portal-dev\")");
        StringAssert.Contains(portalProtectedRouteMatcherText, "path.StartsWithSegments(\"/blazor\"");
        StringAssert.Contains(portalProtectedRouteMatcherText, "path.StartsWithSegments(\"/hub\"");
        StringAssert.Contains(portalProtectedRouteMatcherText, "path.StartsWithSegments(\"/session\"");
        StringAssert.Contains(portalProtectedRouteMatcherText, "path.StartsWithSegments(\"/avalonia\"");
        StringAssert.Contains(portalPageBuilderText, "Open Hub");
        StringAssert.Contains(portalPageBuilderText, "Open Session");
        StringAssert.Contains(portalPageBuilderText, "<code>/hub</code>");
        StringAssert.Contains(portalPageBuilderText, "<code>/session</code>");
        StringAssert.Contains(portalPageBuilderText, "enabled for internal <code>/api</code>, <code>/openapi</code>, and <code>/docs</code> upstream compatibility only");
        StringAssert.Contains(portalPageBuilderText, "signed authenticated owner headers enabled for hosted/public <code>/api</code>, <code>/openapi</code>, and <code>/docs</code> proxy traffic");
        StringAssert.Contains(ownerContractText, "X-Chummer-Portal-Owner");
        StringAssert.Contains(ownerContractText, "X-Chummer-Portal-Owner-Signature");
        StringAssert.Contains(ownerContractText, "BuildSignaturePayload");
        StringAssert.Contains(readmeText, "CHUMMER_ALLOW_OWNER_HEADER=true");
        StringAssert.Contains(readmeText, "It is not public authentication");
        StringAssert.Contains(readmeText, "CHUMMER_PORTAL_OWNER_SHARED_KEY");
        StringAssert.Contains(readmeText, "signed authenticated owner headers");
        StringAssert.Contains(readmeText, "CHUMMER_PORTAL_DEV_AUTH_ENABLED");
        StringAssert.Contains(readmeText, "CHUMMER_PORTAL_REQUIRE_AUTH");
        StringAssert.Contains(readmeText, "Hosted/public deployment posture:");
        StringAssert.Contains(readmeText, "Expose `Chummer.Portal` as the only public origin.");
        StringAssert.Contains(readmeText, "Treat raw `X-Api-Key` mode as local/dev/ops or internal proxy compatibility only.");
        StringAssert.Contains(readmeText, "This is the minimal direct-access fallback for local/dev/ops workflows or private upstream protection.");
        StringAssert.Contains(backlogText, "signed portal-owner propagation seam");
        StringAssert.Contains(backlogText, "API key mode remains documented as minimal/dev fallback.");
    }

    [TestMethod]
    public void App_command_catalog_ids_are_unique()
    {
        List<string> duplicateIds = AppCommandCatalog.All
            .GroupBy(command => command.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id)
            .ToList();

        Assert.IsEmpty(duplicateIds, "Duplicate command ids in AppCommandCatalog: " + string.Join(", ", duplicateIds));
    }

    [TestMethod]
    public void Navigation_tab_catalog_ids_are_unique_and_cover_legacy_shell_tabs()
    {
        HashSet<string> legacyTabIds = LoadParityOracleIds("tabs");

        List<string> duplicateIds = NavigationTabCatalog.All
            .GroupBy(tab => tab.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id)
            .ToList();

        Assert.IsEmpty(duplicateIds, "Duplicate tab ids in NavigationTabCatalog: " + string.Join(", ", duplicateIds));

        HashSet<string> catalogTabIds = NavigationTabCatalog.All
            .Select(tab => tab.Id)
            .ToHashSet(StringComparer.Ordinal);

        List<string> missingInCatalog = legacyTabIds
            .Where(tabId => !catalogTabIds.Contains(tabId))
            .OrderBy(x => x)
            .ToList();

        Assert.IsEmpty(missingInCatalog, "Legacy shell tabs missing from NavigationTabCatalog: " + string.Join(", ", missingInCatalog));

        List<string> tabsWithoutSection = NavigationTabCatalog.All
            .Where(tab => string.IsNullOrWhiteSpace(tab.SectionId))
            .Select(tab => tab.Id)
            .OrderBy(id => id)
            .ToList();

        Assert.IsEmpty(tabsWithoutSection, "Navigation tabs without section bindings: " + string.Join(", ", tabsWithoutSection));
    }

    [TestMethod]
    public void Workspace_surface_action_catalog_covers_legacy_shell_actions()
    {
        HashSet<string> legacyActionIds = LoadParityOracleIds("workspaceActions");

        HashSet<string> catalogTargets = WorkspaceSurfaceActionCatalog.All
            .Select(action => action.TargetId)
            .ToHashSet(StringComparer.Ordinal);

        List<string> missingTargets = legacyActionIds
            .Where(actionId => !catalogTargets.Contains(actionId))
            .OrderBy(x => x)
            .ToList();
        Assert.IsEmpty(missingTargets, "Legacy data-action ids missing in WorkspaceSurfaceActionCatalog: " + string.Join(", ", missingTargets));

        List<string> duplicateActionIds = WorkspaceSurfaceActionCatalog.All
            .GroupBy(action => action.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id)
            .ToList();
        Assert.IsEmpty(duplicateActionIds, "Duplicate workspace surface action ids: " + string.Join(", ", duplicateActionIds));
    }

    [TestMethod]
    public void Legacy_ui_control_dialog_templates_cover_legacy_shell_controls()
    {
        string presenterPath = FindPath("Chummer.Presentation", "Overview", "CharacterOverviewPresenter.cs");
        string dialogFactoryPath = FindPath("Chummer.Presentation", "Overview", "DesktopDialogFactory.cs");
        string dialogTemplateText = string.Join(
            Environment.NewLine,
            File.ReadAllText(presenterPath),
            File.ReadAllText(dialogFactoryPath));

        HashSet<string> legacyControlIds = LoadParityOracleIds("desktopControls");
        List<string> controlsMissingPresenterTemplate = legacyControlIds
            .Where(controlId => !dialogTemplateText.Contains($"\"{controlId}\" =>", StringComparison.Ordinal))
            .OrderBy(x => x)
            .ToList();
        Assert.IsEmpty(controlsMissingPresenterTemplate, "Controls missing dialog templates: " + string.Join(", ", controlsMissingPresenterTemplate));
    }

    [TestMethod]
    public void Ui_exposes_summary_validate_and_metadata_actions()
    {
        HashSet<string> actionTargets = WorkspaceSurfaceActionCatalog.All
            .Select(action => action.TargetId)
            .ToHashSet(StringComparer.Ordinal);

        CollectionAssert.IsSubsetOf(
            SummaryValidateMetadataTargets,
            actionTargets.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        Assert.IsTrue(WorkspaceSurfaceActionCatalog.All.Any(action => action.Kind == WorkspaceSurfaceActionKind.Summary));
        Assert.IsTrue(WorkspaceSurfaceActionCatalog.All.Any(action => action.Kind == WorkspaceSurfaceActionKind.Validate));
        Assert.IsTrue(WorkspaceSurfaceActionCatalog.All.Any(action => action.Kind == WorkspaceSurfaceActionKind.Metadata));
    }

    [TestMethod]
    public void Critical_commands_are_not_placeholder_stubs()
    {
        string presenterTestsPath = FindPath("Chummer.Tests", "Presentation", "CharacterOverviewPresenterTests.cs");
        string presenterTestsText = File.ReadAllText(presenterTestsPath);

        StringAssert.Contains(presenterTestsText, "ExecuteCommandAsync_all_catalog_commands_are_handled");
        StringAssert.Contains(presenterTestsText, "ExecuteCommandAsync_dialog_commands_use_non_generic_dialog_templates");
        StringAssert.Contains(presenterTestsText, "Print_character_command_prepares_html_preview");
    }

    [TestMethod]
    public void Desktop_shell_layout_contains_core_winforms_like_regions()
    {
        string blazorShellPath = FindPath("Chummer.Blazor", "Components", "Layout", "DesktopShell.razor");
        string blazorShellText = File.ReadAllText(blazorShellPath);
        string avaloniaWindowPath = FindPath("Chummer.Avalonia", "MainWindow.axaml");
        string avaloniaWindowText = File.ReadAllText(avaloniaWindowPath);

        StringAssert.Contains(blazorShellText, "<MenuBar");
        StringAssert.Contains(blazorShellText, "<ToolStrip");
        StringAssert.Contains(blazorShellText, "<MdiStrip");
        StringAssert.Contains(blazorShellText, "<WorkspaceLeftPane");
        StringAssert.Contains(blazorShellText, "<SummaryHeader");
        StringAssert.Contains(blazorShellText, "<SectionPane");
        StringAssert.Contains(blazorShellText, "<StatusStrip");
        StringAssert.Contains(blazorShellText, "<DialogHost");

        StringAssert.Contains(avaloniaWindowText, "x:Name=\"ShellMenuBarControl\"");
        StringAssert.Contains(avaloniaWindowText, "x:Name=\"WorkspaceStripControl\"");
        StringAssert.Contains(avaloniaWindowText, "x:Name=\"NavigatorPaneControl\"");
        StringAssert.Contains(avaloniaWindowText, "x:Name=\"SectionHostControl\"");
        StringAssert.Contains(avaloniaWindowText, "x:Name=\"SummaryHeaderControl\"");
        StringAssert.Contains(avaloniaWindowText, "x:Name=\"StatusStripControl\"");

        HashSet<string> tabIds = NavigationTabCatalog.All
            .Select(tab => tab.Id)
            .ToHashSet(StringComparer.Ordinal);
        Assert.IsGreaterThanOrEqualTo(LoadParityOracleIds("tabs").Count, tabIds.Count);
        CollectionAssert.Contains(tabIds.ToList(), "tab-info");
        CollectionAssert.Contains(tabIds.ToList(), "tab-gear");
        CollectionAssert.Contains(tabIds.ToList(), "tab-magician");
        CollectionAssert.Contains(tabIds.ToList(), "tab-improvements");
    }

    [TestMethod]
    public void Workspace_uses_live_document_state_and_recent_file_hooks()
    {
        string sessionStatePath = FindPath("Chummer.Presentation", "Overview", "WorkspaceSessionState.cs");
        string sessionStateText = File.ReadAllText(sessionStatePath);
        string sessionPresenterPath = FindPath("Chummer.Presentation", "Overview", "WorkspaceSessionPresenter.cs");
        string sessionPresenterText = File.ReadAllText(sessionPresenterPath);
        string shellPresenterPath = FindPath("Chummer.Presentation", "Shell", "ShellPresenter.cs");
        string shellPresenterText = File.ReadAllText(shellPresenterPath);

        StringAssert.Contains(sessionStateText, "ActiveWorkspaceId");
        StringAssert.Contains(sessionStateText, "OpenWorkspaces");
        StringAssert.Contains(sessionStateText, "RecentWorkspaceIds");
        StringAssert.Contains(sessionPresenterText, "TouchRecent");
        StringAssert.Contains(sessionPresenterText, "BuildRecentList");
        StringAssert.Contains(sessionPresenterText, "SelectMostRecentOpenWorkspace");
        StringAssert.Contains(shellPresenterText, "ActiveWorkspaceId");
        StringAssert.Contains(shellPresenterText, "OpenWorkspaces");
        StringAssert.Contains(shellPresenterText, "BuildUpdatedWorkspaceTabMap");
    }

    [TestMethod]
    public void Character_overview_presenter_delegates_workspace_lifecycle_sequencing_to_coordinator()
    {
        string presenterPath = FindPath("Chummer.Presentation", "Overview", "CharacterOverviewPresenter.cs");
        string presenterText = File.ReadAllText(presenterPath);
        string presenterWorkspacePath = FindPath("Chummer.Presentation", "Overview", "CharacterOverviewPresenter.Workspace.cs");
        string presenterWorkspaceText = File.ReadAllText(presenterWorkspacePath);
        string coordinatorContractPath = FindPath("Chummer.Presentation", "Overview", "IWorkspaceOverviewLifecycleCoordinator.cs");
        string coordinatorContractText = File.ReadAllText(coordinatorContractPath);
        string coordinatorPath = FindPath("Chummer.Presentation", "Overview", "WorkspaceOverviewLifecycleCoordinator.cs");
        string coordinatorText = File.ReadAllText(coordinatorPath);

        StringAssert.Contains(presenterText, "IWorkspaceOverviewLifecycleCoordinator");
        StringAssert.Contains(coordinatorContractText, "interface IWorkspaceOverviewLifecycleCoordinator");
        StringAssert.Contains(presenterWorkspaceText, "_workspaceOverviewLifecycleCoordinator.ImportAsync");
        StringAssert.Contains(presenterWorkspaceText, "_workspaceOverviewLifecycleCoordinator.LoadAsync");
        StringAssert.Contains(presenterWorkspaceText, "_workspaceOverviewLifecycleCoordinator.SwitchAsync");
        StringAssert.Contains(presenterWorkspaceText, "_workspaceOverviewLifecycleCoordinator.CloseAsync");
        StringAssert.Contains(presenterWorkspaceText, "_workspaceOverviewLifecycleCoordinator.CloseAllAsync");
        StringAssert.Contains(presenterWorkspaceText, "_workspaceOverviewLifecycleCoordinator.CreateResetState");

        Assert.IsFalse(
            presenterWorkspaceText.Contains("_client.ImportAsync", StringComparison.Ordinal),
            "CharacterOverviewPresenter workspace flow should not import workspaces directly.");
        Assert.IsFalse(
            presenterWorkspaceText.Contains("_workspaceSessionPresenter.Close(", StringComparison.Ordinal),
            "CharacterOverviewPresenter workspace flow should not own close sequencing directly.");
        Assert.IsFalse(
            presenterWorkspaceText.Contains("_workspaceSessionActivationService.Activate", StringComparison.Ordinal),
            "CharacterOverviewPresenter workspace flow should not own workspace activation sequencing directly.");
        Assert.IsFalse(
            presenterWorkspaceText.Contains("_workspaceOverviewLoader.LoadAsync", StringComparison.Ordinal),
            "CharacterOverviewPresenter workspace flow should not load overview payloads directly.");

        StringAssert.Contains(coordinatorText, "_workspaceOverviewLoader.LoadAsync");
        StringAssert.Contains(coordinatorText, "_workspaceRemoteCloseService.TryCloseAsync");
        StringAssert.Contains(coordinatorText, "_workspaceSessionActivationService.Activate");
        StringAssert.Contains(coordinatorText, "_workspaceViewStateStore.Capture");
        StringAssert.Contains(coordinatorText, "_workspaceShellStateFactory.CreateEmptyShellState");
    }

    [TestMethod]
    public void Ui_click_paths_are_wired_for_commands_controls_and_dialogs()
    {
        string blazorShellPath = FindPath("Chummer.Blazor", "Components", "Layout", "DesktopShell.razor");
        string blazorShellText = File.ReadAllText(blazorShellPath);
        string dialogFactoryPath = FindPath("Chummer.Presentation", "Overview", "DesktopDialogFactory.cs");
        string dialogFactoryText = File.ReadAllText(dialogFactoryPath);

        StringAssert.Contains(blazorShellText, "ExecuteCommandRequested=\"@ExecuteCommandAsync\"");
        StringAssert.Contains(blazorShellText, "ExecuteWorkflowSurfaceRequested=\"@ExecuteWorkflowSurfaceAsync\"");
        StringAssert.Contains(blazorShellText, "ExecuteDialogActionRequested=\"@ExecuteDialogActionAsync\"");
        StringAssert.Contains(blazorShellText, "CloseRequested=\"@CloseDialogAsync\"");
        StringAssert.Contains(blazorShellText, "FieldInputRequested=\"@OnDialogFieldInputAsync\"");
        StringAssert.Contains(blazorShellText, "FieldCheckboxRequested=\"@OnDialogCheckboxChangedAsync\"");

        string[] dialogBackedCommands =
        [
            "print_setup",
            "dice_roller",
            "global_settings",
            "character_settings",
            "translator",
            "xml_editor",
            "master_index",
            "character_roster",
            "data_exporter",
            "report_bug",
            "about"
        ];

        foreach (string command in dialogBackedCommands)
        {
            StringAssert.Contains(dialogFactoryText, $"\"{command}\" =>", $"Expected dialog-backed command definition missing: {command}");
        }

        string[] dialogControlIds =
        [
            "globalUiScale",
            "globalTheme",
            "globalLanguage",
            "globalCompactMode",
            "characterPriority",
            "characterKarmaNuyen",
            "characterHouseRulesEnabled",
            "characterNotes",
            "diceExpression",
            "translatorSearch",
            "xmlEditorDialog",
            "dataExportPreview"
        ];

        foreach (string controlId in dialogControlIds)
        {
            StringAssert.Contains(dialogFactoryText, controlId, $"Expected dialog control id missing: {controlId}");
        }
    }

    [TestMethod]
    public void Dual_head_acceptance_suite_is_present_for_primary_migration_gate()
    {
        string testPath = FindPath("Chummer.Tests", "Presentation", "DualHeadAcceptanceTests.cs");
        string testText = File.ReadAllText(testPath);

        StringAssert.Contains(testText, "Avalonia_and_Blazor_overview_flows_show_equivalent_state_after_import");
        StringAssert.Contains(testText, "Avalonia_and_Blazor_metadata_save_roundtrip_match");
        StringAssert.Contains(testText, "Avalonia_and_Blazor_tab_selection_loads_same_workspace_section");
        StringAssert.Contains(testText, "Avalonia_and_Blazor_command_dispatch_save_character_matches");
        StringAssert.Contains(testText, "Avalonia_and_Blazor_workspace_action_summary_matches");
        StringAssert.Contains(testText, "Avalonia_and_Blazor_info_family_workspace_actions_render_matching_sections");
        StringAssert.Contains(testText, "Avalonia_and_Blazor_attributes_and_skills_workspace_actions_render_matching_sections");
        StringAssert.Contains(testText, "Avalonia_and_Blazor_gear_family_workspace_actions_render_matching_sections");
        StringAssert.Contains(testText, "Avalonia_and_Blazor_magic_family_workspace_actions_render_matching_sections");
        StringAssert.Contains(testText, "Avalonia_and_Blazor_support_family_workspace_actions_render_matching_sections");
        StringAssert.Contains(testText, "Avalonia_and_Blazor_combat_and_cyberware_workspace_actions_render_matching_sections");
        StringAssert.Contains(testText, "Avalonia_and_Blazor_dialog_workflow_keeps_shell_regions_in_parity");
    }

    [TestMethod]
    public void Blazor_shell_component_suite_is_present_for_phase4_gate()
    {
        string testPath = FindPath("Chummer.Tests", "Presentation", "BlazorShellComponentTests.cs");
        string testText = File.ReadAllText(testPath);
        string desktopShellRulesetPath = FindPath("Chummer.Tests", "Presentation", "DesktopShellRulesetCatalogTests.cs");
        string desktopShellRulesetText = File.ReadAllText(desktopShellRulesetPath);

        StringAssert.Contains(testText, "MenuBar_renders_open_menu_items_and_applies_enablement_state");
        StringAssert.Contains(testText, "MenuBar_invokes_toggle_and_execute_callbacks");
        StringAssert.Contains(testText, "ToolStrip_applies_selected_and_disabled_states");
        StringAssert.Contains(testText, "WorkspaceLeftPane_renders_shell_controls_and_invokes_callbacks");
        StringAssert.Contains(testText, "SectionPane_switches_between_placeholder_and_section_payload");
        StringAssert.Contains(testText, "DialogHost_renders_dialog_and_emits_events");
        StringAssert.Contains(desktopShellRulesetText, "DesktopShell_uses_active_ruleset_plugin_catalogs_for_actions_and_workflow_surfaces");
    }

    [TestMethod]
    public void Playwright_ui_e2e_gate_is_present_for_phase4_gate()
    {
        string uiE2ePath = FindPath("scripts", "e2e-ui.sh");
        string uiE2eText = File.ReadAllText(uiE2ePath);
        string migrationLoopPath = FindPath("scripts", "migration-loop.sh");
        string migrationLoopText = File.ReadAllText(migrationLoopPath);
        string playwrightScriptPath = FindPath("scripts", "e2e-ui-playwright.cjs");
        string playwrightScriptText = File.ReadAllText(playwrightScriptPath);

        StringAssert.Contains(uiE2eText, "CHUMMER_UI_PLAYWRIGHT");
        StringAssert.Contains(uiE2eText, "CHUMMER_E2E_PLAYWRIGHT_SOFT_FAIL");
        StringAssert.Contains(uiE2eText, "CHUMMER_E2E_DOCKER_FALLBACK");
        StringAssert.Contains(uiE2eText, "CHUMMER_E2E_HOST_PROBE_ATTEMPTS");
        StringAssert.Contains(uiE2eText, "CHUMMER_E2E_DOCKER_PROBE_ATTEMPTS");
        StringAssert.Contains(uiE2eText, "docker_fetch_with_key");
        StringAssert.Contains(uiE2eText, "docker compose --profile test run --build --rm -T chummer-playwright");
        StringAssert.Contains(migrationLoopText, "bash scripts/e2e-ui.sh");

        StringAssert.Contains(playwrightScriptText, "input[type=\"file\"]");
        StringAssert.Contains(playwrightScriptText, "CHUMMER_UI_SAMPLE_FILE");
        StringAssert.Contains(playwrightScriptText, "BLUE.chum5");
        StringAssert.Contains(playwrightScriptText, "#tab-skills");
        StringAssert.Contains(playwrightScriptText, "global_settings");
        StringAssert.Contains(playwrightScriptText, "Save Workspace");
        StringAssert.Contains(playwrightScriptText, "playwright UI flow completed");
    }

    [TestMethod]
    public void Portal_playwright_e2e_uses_portal_stack_dependencies()
    {
        string portalScriptPath = FindPath("scripts", "e2e-portal.sh");
        string portalScriptText = File.ReadAllText(portalScriptPath);
        string portalPlaywrightPath = FindPath("scripts", "e2e-portal.cjs");
        string portalPlaywrightText = File.ReadAllText(portalPlaywrightPath);

        StringAssert.Contains(portalScriptText, "CHUMMER_E2E_PLAYWRIGHT_SOFT_FAIL");
        StringAssert.Contains(portalScriptText, "skipping portal e2e: docker daemon permission denied in this environment.");
        StringAssert.Contains(portalScriptText, "chummer-playwright-portal");
        StringAssert.Contains(portalScriptText, "compose_args=(-f docker-compose.yml)");
        StringAssert.Contains(portalScriptText, "docker compose \"${compose_args[@]}\" --profile portal up -d --build chummer-api chummer-blazor-portal chummer-hub-web-portal chummer-avalonia-browser chummer-portal");
        StringAssert.Contains(portalScriptText, "chummer-session-web-portal");
        StringAssert.Contains(portalScriptText, "docker compose \"${compose_args[@]}\" --profile test --profile portal run --build --rm chummer-playwright-portal");
        StringAssert.Contains(portalPlaywrightText, "requiredLandingLinks");
        StringAssert.Contains(portalPlaywrightText, "requiredLandingLinks.every(link => text.includes(link))");
        StringAssert.Contains(portalPlaywrightText, "'/blazor/'");
        StringAssert.Contains(portalPlaywrightText, "'/hub/'");
        StringAssert.Contains(portalPlaywrightText, "'/session/'");
        StringAssert.Contains(portalPlaywrightText, "'/avalonia/'");
        StringAssert.Contains(portalPlaywrightText, "'/downloads/'");
        StringAssert.Contains(portalPlaywrightText, "'/docs/'");
        StringAssert.Contains(portalPlaywrightText, "'/api/health'");
        StringAssert.Contains(portalPlaywrightText, "http://chummer-portal:8080/api/ai/status");
        StringAssert.Contains(portalPlaywrightText, "http://chummer-portal:8080/hub/health");
        StringAssert.Contains(portalPlaywrightText, "http://chummer-portal:8080/session/health");
        StringAssert.Contains(portalPlaywrightText, "No published desktop builds yet");
        StringAssert.Contains(portalPlaywrightText, "fallback-link");
    }

    [TestMethod]
    public void Blazor_desktop_host_project_is_present_and_photino_backed()
    {
        string projectPath = FindPath("Chummer.Blazor.Desktop", "Chummer.Blazor.Desktop.csproj");
        string projectText = File.ReadAllText(projectPath);
        string programPath = FindPath("Chummer.Blazor.Desktop", "Program.cs");
        string programText = File.ReadAllText(programPath);
        string runtimePath = FindPath("Chummer.Desktop.Runtime", "ServiceCollectionDesktopRuntimeExtensions.cs");
        string runtimeText = File.ReadAllText(runtimePath);
        string indexPath = FindPath("Chummer.Blazor.Desktop", "wwwroot", "index.html");
        string indexText = File.ReadAllText(indexPath);

        StringAssert.Contains(projectText, "Photino.Blazor");
        StringAssert.Contains(projectText, @"..\Chummer.Blazor\Chummer.Blazor.csproj");
        StringAssert.Contains(projectText, @"..\Chummer.Desktop.Runtime\Chummer.Desktop.Runtime.csproj");
        StringAssert.Contains(projectText, @"..\Chummer.Presentation\Chummer.Presentation.csproj");

        StringAssert.Contains(programText, "PhotinoBlazorAppBuilder.CreateDefault");
        StringAssert.Contains(programText, "RootComponents.Add<App>(\"app\")");
        StringAssert.Contains(programText, "AddChummerLocalRuntimeClient");

        StringAssert.Contains(runtimeText, "CHUMMER_CLIENT_MODE");
        StringAssert.Contains(runtimeText, "CHUMMER_DESKTOP_CLIENT_MODE");
        StringAssert.Contains(runtimeText, "CHUMMER_API_BASE_URL");
        StringAssert.Contains(runtimeText, "CHUMMER_API_KEY");
        StringAssert.Contains(runtimeText, "AddChummerHeadlessCore");
        StringAssert.Contains(runtimeText, "Set {ApiBaseUrlEnvironmentVariable} when {ClientModeEnvironmentVariable}=http (legacy: {LegacyDesktopClientModeEnvironmentVariable}=http).");
        Assert.IsFalse(runtimeText.Contains("http://127.0.0.1:8088", StringComparison.Ordinal));

        StringAssert.Contains(indexText, "<app>Loading...</app>");
        StringAssert.Contains(indexText, "_content/Chummer.Blazor/app.css");
        StringAssert.Contains(indexText, "_framework/blazor.webview.js");
    }

    [TestMethod]
    public void Blazor_shell_is_promoted_to_layout_layer()
    {
        string mainLayoutPath = FindPath("Chummer.Blazor", "Components", "Layout", "MainLayout.razor");
        string mainLayoutText = File.ReadAllText(mainLayoutPath);
        string desktopShellPath = FindPath("Chummer.Blazor", "Components", "Layout", "DesktopShell.razor");
        string desktopShellText = File.ReadAllText(desktopShellPath);
        string homePath = FindPath("Chummer.Blazor", "Components", "Pages", "Home.razor");
        string homeText = File.ReadAllText(homePath);
        string deepLinkPath = FindPath("Chummer.Blazor", "Components", "Pages", "DeepLinkCheck.razor");
        string deepLinkText = File.ReadAllText(deepLinkPath);

        StringAssert.Contains(mainLayoutText, "<DesktopShell />");
        Assert.IsFalse(mainLayoutText.Contains("IsHomeRoute()", StringComparison.Ordinal));
        Assert.IsFalse(mainLayoutText.Contains("@Body", StringComparison.Ordinal));
        StringAssert.Contains(desktopShellText, "class=\"desktop-shell\"");
        StringAssert.Contains(desktopShellText, "ImportedFileName=\"@ImportedFileName\"");
        StringAssert.Contains(desktopShellText, "ImportError=\"@ImportError\"");
        StringAssert.Contains(desktopShellText, "LastUiUtc=\"@_lastUiUtc\"");
        Assert.IsFalse(desktopShellText.Contains("ImportedFileName=\"ImportedFileName\"", StringComparison.Ordinal));
        Assert.IsFalse(desktopShellText.Contains("ImportError=\"ImportError\"", StringComparison.Ordinal));
        StringAssert.Contains(homeText, "@page \"/\"");
        StringAssert.Contains(deepLinkText, "@layout Chummer.Blazor.Components.Layout.NoLayout");
        Assert.IsFalse(homeText.Contains("desktop-shell", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Blazor_workbench_head_embeds_coach_sidecar_for_active_runtime_context()
    {
        string blazorProgramPath = FindPath("Chummer.Blazor", "Program.cs");
        string blazorProgramText = File.ReadAllText(blazorProgramPath);
        string desktopShellPath = FindPath("Chummer.Blazor", "Components", "Layout", "DesktopShell.razor");
        string desktopShellText = File.ReadAllText(desktopShellPath);
        string desktopShellCodePath = FindPath("Chummer.Blazor", "Components", "Layout", "DesktopShell.Coach.cs");
        string desktopShellCodeText = File.ReadAllText(desktopShellCodePath);
        string coachClientPath = FindPath("Chummer.Blazor", "WorkbenchCoachApiClient.cs");
        string coachClientText = File.ReadAllText(coachClientPath);
        string coachClientContractPath = FindPath("Chummer.Blazor", "IWorkbenchCoachApiClient.cs");
        string coachClientContractText = File.ReadAllText(coachClientContractPath);
        string componentTestsPath = FindPath("Chummer.Tests", "Presentation", "DesktopShellRulesetCatalogTests.cs");
        string componentTestsText = File.ReadAllText(componentTestsPath);

        StringAssert.Contains(blazorProgramText, "AddHttpClient<IWorkbenchCoachApiClient, WorkbenchCoachApiClient>");
        StringAssert.Contains(desktopShellText, "Coach Sidecar");
        StringAssert.Contains(desktopShellText, "Grounded Guidance");
        StringAssert.Contains(desktopShellText, "Recent Coach Guidance");
        StringAssert.Contains(desktopShellText, "data-testid=\"open-workbench-coach-sidecar\"");
        StringAssert.Contains(desktopShellText, "data-testid=\"refresh-workbench-coach-sidecar\"");
        StringAssert.Contains(desktopShellText, "data-testid=\"workbench-coach-provider-transport\"");
        StringAssert.Contains(desktopShellText, "data-testid=\"open-workbench-coach-thread\"");
        StringAssert.Contains(desktopShellText, "data-testid=\"workbench-coach-audit-flavor\"");
        StringAssert.Contains(desktopShellText, "data-testid=\"workbench-coach-audit-budget\"");
        StringAssert.Contains(desktopShellText, "data-testid=\"workbench-coach-audit-structured\"");
        StringAssert.Contains(desktopShellCodeText, "RefreshCoachSidecarIfNeededAsync");
        StringAssert.Contains(desktopShellCodeText, "BuildCoachLaunchUri");
        StringAssert.Contains(desktopShellCodeText, "AiCoachLaunchQuery.BuildRelativeUri");
        StringAssert.Contains(desktopShellCodeText, "IWorkbenchCoachApiClient");
        StringAssert.Contains(coachClientContractText, "public interface IWorkbenchCoachApiClient");
        StringAssert.Contains(coachClientText, "public sealed class WorkbenchCoachApiClient");
        StringAssert.Contains(coachClientText, "/api/ai/status");
        StringAssert.Contains(coachClientText, "/api/ai/provider-health");
        StringAssert.Contains(coachClientText, "(\"routeType\", routeType)");
        StringAssert.Contains(coachClientText, "/api/ai/conversation-audits");
        StringAssert.Contains(coachClientText, "HttpClient");
        StringAssert.Contains(componentTestsText, "DesktopShell_renders_coach_sidecar_for_active_runtime");
        StringAssert.Contains(componentTestsText, "open-workbench-coach-sidecar");
    }

    [TestMethod]
    public void Portal_docs_route_shares_api_cluster_contract()
    {
        string portalProgramPath = FindPath("Chummer.Portal", "Program.cs");
        string portalProgramText = File.ReadAllText(portalProgramPath);
        string portalSettingsPath = FindPath("Chummer.Portal", "appsettings.json");
        string portalSettingsText = File.ReadAllText(portalSettingsPath);

        StringAssert.Contains(portalProgramText, "RouteId = \"portal-docs\"");
        StringAssert.Contains(portalProgramText, "ClusterId = \"api-cluster\"");
        StringAssert.Contains(portalProgramText, "Path = \"/docs/{**catch-all}\"");
        Assert.IsFalse(portalProgramText.Contains("CHUMMER_PORTAL_DOCS_URL", StringComparison.Ordinal));
        Assert.IsFalse(portalProgramText.Contains("docs-cluster", StringComparison.Ordinal));
        Assert.IsFalse(portalProgramText.Contains("BuildRouteTransforms(apiRouteTransforms, \"/docs\")", StringComparison.Ordinal));
        Assert.IsFalse(portalSettingsText.Contains("DocsBaseUrl", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Portal_downloads_page_shows_explicit_unpublished_state()
    {
        string portalPageBuilderPath = FindPath("Chummer.Portal", "PortalPageBuilder.cs");
        string portalPageBuilderText = File.ReadAllText(portalPageBuilderPath);
        string portalProgramPath = FindPath("Chummer.Portal", "Program.cs");
        string portalProgramText = File.ReadAllText(portalProgramPath);

        StringAssert.Contains(portalProgramText, "string Status = \"published\"");
        StringAssert.Contains(portalProgramText, "string Source = \"manifest\"");
        StringAssert.Contains(portalPageBuilderText, "case 'unpublished'");
        StringAssert.Contains(portalPageBuilderText, "case 'manifest-empty'");
        StringAssert.Contains(portalPageBuilderText, "case 'manifest-missing'");
        StringAssert.Contains(portalPageBuilderText, "case 'manifest-error'");
        StringAssert.Contains(portalPageBuilderText, "case 'fallback-source'");
        StringAssert.Contains(portalPageBuilderText, "No published desktop builds yet");
        StringAssert.Contains(portalPageBuilderText, "Run desktop-downloads workflow and deploy the generated bundle.");
    }

    [TestMethod]
    public void Portal_downloads_repo_snapshot_is_local_dev_only_and_not_published()
    {
        string portalProjectPath = FindPath("Chummer.Portal", "Chummer.Portal.csproj");
        string portalProjectText = File.ReadAllText(portalProjectPath);
        string downloadsReadmePath = FindPath("Docker", "Downloads", "README.md");
        string downloadsReadmeText = File.ReadAllText(downloadsReadmePath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);
        string runbookPath = FindPath("docs", "SELF_HOSTED_DOWNLOADS_RUNBOOK.md");
        string runbookText = File.ReadAllText(runbookPath);

        StringAssert.Contains(portalProjectText, "<Content Update=\"downloads\\**\\*\">");
        StringAssert.Contains(portalProjectText, "<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>");
        StringAssert.Contains(portalProjectText, "<CopyToPublishDirectory>Never</CopyToPublishDirectory>");
        StringAssert.Contains(readmeText, "is excluded from published portal output");
        StringAssert.Contains(downloadsReadmeText, "production source of truth");
        StringAssert.Contains(runbookText, "Published portal builds do not ship the checked-in `Chummer.Portal/downloads/releases.json` snapshot");
        StringAssert.Contains(runbookText, "should surface as `manifest-missing`");
    }

    [TestMethod]
    public void Portal_download_manifest_discovers_local_artifacts_when_manifest_is_empty()
    {
        string portalProgramPath = FindPath("Chummer.Portal", "Program.cs");
        string portalProgramText = File.ReadAllText(portalProgramPath);
        string portalDownloadsServicePath = FindPath("Chummer.Portal", "PortalDownloadsService.cs");
        string portalDownloadsServiceText = File.ReadAllText(portalDownloadsServicePath);

        StringAssert.Contains(portalProgramText, "LoadReleaseManifest(resolvedManifestPath, resolvedReleaseFilesPath, downloadsFallbackUrl)");
        StringAssert.Contains(portalProgramText, "CHUMMER_PORTAL_DOWNLOADS_FALLBACK_URL");
        StringAssert.Contains(portalProgramText, "return Results.NotFound(new");
        StringAssert.Contains(portalDownloadsServiceText, "DiscoverLocalArtifacts");
        StringAssert.Contains(portalDownloadsServiceText, "LocalArtifactPattern");
        StringAssert.Contains(portalDownloadsServiceText, "chummer-(?<app>avalonia|blazor-desktop)-(?<rid>[^.]+)\\.(?<ext>zip|tar\\.gz)");
        StringAssert.Contains(portalDownloadsServiceText, "\"osx-x64\" => \"macOS x64\"");
        StringAssert.Contains(portalDownloadsServiceText, "if (parsedManifest is not null && parsedManifest.Downloads.Count > 0)");
        StringAssert.Contains(portalDownloadsServiceText, "return new DownloadReleaseManifest(");
        StringAssert.Contains(portalDownloadsServiceText, "Status: \"fallback-source\"");
        StringAssert.Contains(portalDownloadsServiceText, "Status: \"manifest-missing\"");
        StringAssert.Contains(portalDownloadsServiceText, "Status: \"manifest-error\"");
        StringAssert.Contains(portalDownloadsServiceText, "Status = ResolveManifestStatus");
        StringAssert.Contains(portalDownloadsServiceText, "Message = BuildManifestMessage");
        StringAssert.Contains(portalDownloadsServiceText, "Url: $\"/downloads/{relativePath}\"");
    }

    [TestMethod]
    public void Self_hosted_downloads_runbook_documents_portal_status_meanings()
    {
        string runbookPath = FindPath("docs", "SELF_HOSTED_DOWNLOADS_RUNBOOK.md");
        string runbookText = File.ReadAllText(runbookPath);
        string envExamplePath = FindPath("docs", "examples", "self-hosted-downloads.env.example");
        string envExampleText = File.ReadAllText(envExamplePath);

        StringAssert.Contains(runbookText, "Portal Status Meanings");
        StringAssert.Contains(runbookText, "`manifest-empty`");
        StringAssert.Contains(runbookText, "`manifest-missing`");
        StringAssert.Contains(runbookText, "`manifest-error`");
        StringAssert.Contains(runbookText, "`fallback-source`");
        StringAssert.Contains(runbookText, "Production/self-hosted deploys should end in `published`.");
        StringAssert.Contains(runbookText, "Recommended Production Topology");
        StringAssert.Contains(runbookText, "Default recommendation: use `CHUMMER_PORTAL_DOWNLOADS_DEPLOY_DIR`");
        StringAssert.Contains(runbookText, "Treat object storage as the alternate topology");
        StringAssert.Contains(runbookText, "docs/examples/self-hosted-downloads.env.example");
        StringAssert.Contains(runbookText, "RUNBOOK_MODE=downloads-smoke bash scripts/runbook.sh");
        StringAssert.Contains(runbookText, "RUNBOOK_LOG_DIR");
        StringAssert.Contains(runbookText, "RUNBOOK_STATE_DIR");

        StringAssert.Contains(envExampleText, "CHUMMER_PORTAL_DOWNLOADS_DEPLOY_ENABLED=true");
        StringAssert.Contains(envExampleText, "CHUMMER_PORTAL_DOWNLOADS_DEPLOY_DIR=/srv/chummer/portal-downloads");
        StringAssert.Contains(envExampleText, "CHUMMER_PORTAL_DOWNLOADS_VERIFY_URL=https://chummer.example.com/downloads/releases.json");
        StringAssert.Contains(envExampleText, "# Alternate object-storage topology:");
        StringAssert.Contains(envExampleText, "# CHUMMER_PORTAL_DOWNLOADS_S3_URI=s3://chummer-downloads/releases");
    }

    [TestMethod]
    public void Desktop_download_matrix_includes_avalonia_and_blazor_desktop_artifacts()
    {
        string workflowPath = FindPath(".github", "workflows", "desktop-downloads-matrix.yml");
        string workflowText = File.ReadAllText(workflowPath);
        string manifestScriptPath = FindPath("scripts", "generate-releases-manifest.sh");
        string manifestScriptText = File.ReadAllText(manifestScriptPath);
        string verifyScriptPath = FindPath("scripts", "verify-releases-manifest.sh");
        string verifyScriptText = File.ReadAllText(verifyScriptPath);

        StringAssert.Contains(workflowText, "project: Chummer.Avalonia/Chummer.Avalonia.csproj");
        StringAssert.Contains(workflowText, "project: Chummer.Blazor.Desktop/Chummer.Blazor.Desktop.csproj");
        StringAssert.Contains(
            workflowText,
            "app: avalonia\n            project: Chummer.Avalonia/Chummer.Avalonia.csproj\n            os: macos-latest\n            rid: osx-x64");
        StringAssert.Contains(
            workflowText,
            "app: blazor-desktop\n            project: Chummer.Blazor.Desktop/Chummer.Blazor.Desktop.csproj\n            os: macos-latest\n            rid: osx-x64");
        StringAssert.Contains(workflowText, "bash scripts/generate-releases-manifest.sh");
        StringAssert.Contains(workflowText, "Chummer.Application/**");
        StringAssert.Contains(workflowText, "Chummer.Core/**");
        StringAssert.Contains(workflowText, "Chummer.Desktop.Runtime/**");
        StringAssert.Contains(workflowText, "Chummer.Infrastructure/**");
        StringAssert.Contains(workflowText, "Chummer.Portal/**");
        StringAssert.Contains(workflowText, "scripts/generate-releases-manifest.sh");
        StringAssert.Contains(workflowText, "scripts/publish-download-bundle.sh");
        StringAssert.Contains(workflowText, "scripts/publish-download-bundle-s3.sh");
        StringAssert.Contains(workflowText, "deploy_portal_downloads");
        StringAssert.Contains(workflowText, "deploy-downloads");
        StringAssert.Contains(workflowText, "deploy-downloads-object-storage");
        StringAssert.Contains(workflowText, "CHUMMER_PORTAL_DOWNLOADS_VERIFY_URL");
        StringAssert.Contains(workflowText, "CHUMMER_PORTAL_DOWNLOADS_VERIFY_LINKS");
        StringAssert.Contains(workflowText, "CHUMMER_PORTAL_DOWNLOADS_DEPLOY_ENABLED");
        StringAssert.Contains(workflowText, "CHUMMER_PORTAL_DOWNLOADS_REQUIRE_PUBLISHED_VERSION");
        StringAssert.Contains(workflowText, "CHUMMER_PORTAL_DOWNLOADS_S3_URI");
        StringAssert.Contains(workflowText, "CHUMMER_PORTAL_DOWNLOADS_AWS_ACCESS_KEY_ID");
        StringAssert.Contains(workflowText, "Validate live verify URL");
        StringAssert.Contains(workflowText, "Set CHUMMER_PORTAL_DOWNLOADS_VERIFY_URL to verify the live portal manifest after deployment.");
        StringAssert.Contains(workflowText, "Verify deployed manifest has artifacts");
        StringAssert.Contains(workflowText, "bash scripts/verify-releases-manifest.sh \"$CHUMMER_PORTAL_DOWNLOADS_DEPLOY_DIR\"");
        StringAssert.Contains(workflowText, "Verify deployed portal manifest has artifacts");
        Assert.IsFalse(
            workflowText.Contains("if: ${{ vars.CHUMMER_PORTAL_DOWNLOADS_VERIFY_URL != '' }}", StringComparison.Ordinal),
            "Live portal manifest verification should be mandatory when deployment is enabled.");
        StringAssert.Contains(workflowText, "scripts/verify-releases-manifest.sh");

        StringAssert.Contains(manifestScriptText, "chummer-(?P<app>avalonia|blazor-desktop)-(?P<rid>[^.]+)\\.(?P<ext>zip|tar\\.gz)");
        StringAssert.Contains(manifestScriptText, "\"osx-x64\": \"macOS x64\"");
        StringAssert.Contains(manifestScriptText, "\"id\": f\"{app}-{rid}\"");
        StringAssert.Contains(manifestScriptText, "\"url\": f\"/downloads/files/{artifact.name}\"");
        StringAssert.Contains(verifyScriptText, "CHUMMER_PORTAL_DOWNLOADS_REQUIRE_PUBLISHED_VERSION");
        StringAssert.Contains(verifyScriptText, "CHUMMER_PORTAL_DOWNLOADS_VERIFY_LINKS");
        StringAssert.Contains(verifyScriptText, "failed artifact verification");
        StringAssert.Contains(verifyScriptText, "Verified artifact links/files");
        StringAssert.Contains(verifyScriptText, "version.lower() == \"unpublished\"");
    }

    [TestMethod]
    public void Readme_modern_stack_summary_tracks_current_gateway_and_runtime_contract()
    {
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);
        string portalSettingsPath = FindPath("Chummer.Portal", "appsettings.json");
        string portalSettingsText = File.ReadAllText(portalSettingsPath);

        StringAssert.Contains(readmeText, "Current multi-head runtime (Docker branch)");
        StringAssert.Contains(readmeText, "multi-head UI stack (`Chummer.Blazor`, `Chummer.Hub.Web`, `Chummer.Session.Web`, `Chummer.Coach.Web`, `Chummer.Avalonia`, `Chummer.Blazor.Desktop`, `Chummer.Avalonia.Browser`, `Chummer.Portal`)");
        StringAssert.Contains(readmeText, "## Decommissioned Legacy Runtime Components");
        StringAssert.Contains(readmeText, "`chummer-web` is no longer an active runtime service or parity-test dependency.");
        StringAssert.Contains(readmeText, "Static parity extraction from `Chummer.Web/wwwroot/index.html` has been replaced by the checked-in parity oracle");
        StringAssert.Contains(readmeText, "Chummer.Hub.Web");
        StringAssert.Contains(readmeText, "Chummer.Session.Web");
        StringAssert.Contains(readmeText, "Chummer.Coach.Web");
        StringAssert.Contains(readmeText, "CHUMMER_HUB_PATH_BASE");
        StringAssert.Contains(readmeText, "CHUMMER_SESSION_PATH_BASE");
        StringAssert.Contains(readmeText, "CHUMMER_COACH_PATH_BASE");
        StringAssert.Contains(readmeText, "IndexedDB");
        StringAssert.Contains(readmeText, "OPFS");
        StringAssert.Contains(readmeText, "chummer-hub-web");
        StringAssert.Contains(readmeText, "chummer-session-web");
        StringAssert.Contains(readmeText, "chummer-coach-web");
        StringAssert.Contains(readmeText, "chummer-blazor-portal");
        StringAssert.Contains(readmeText, "chummer-hub-web-portal");
        StringAssert.Contains(readmeText, "chummer-session-web-portal");
        StringAssert.Contains(readmeText, "chummer-coach-web-portal");
        StringAssert.Contains(readmeText, "chummer-avalonia-browser");
        StringAssert.Contains(readmeText, "chummer-portal");
        StringAssert.Contains(readmeText, "/api/*`, `/openapi/*`, and `/docs/*` share the same upstream contract through `CHUMMER_PORTAL_API_URL`.");
        StringAssert.Contains(readmeText, "CHUMMER_CLIENT_MODE");
        StringAssert.Contains(readmeText, "CHUMMER_DESKTOP_CLIENT_MODE");
        StringAssert.Contains(readmeText, "CHUMMER_PORTAL_DOWNLOADS_DEPLOY_DIR");
        StringAssert.Contains(readmeText, "CHUMMER_PORTAL_DOWNLOADS_VERIFY_URL");
        StringAssert.Contains(readmeText, "CHUMMER_PORTAL_DOWNLOADS_FALLBACK_URL");
        StringAssert.Contains(readmeText, "CHUMMER_PORTAL_DOWNLOADS_S3_URI");
        StringAssert.Contains(readmeText, "CHUMMER_PORTAL_DOWNLOADS_AWS_ACCESS_KEY_ID");
        StringAssert.Contains(readmeText, "DOWNLOADS_VERIFY_LINKS=1");
        StringAssert.Contains(readmeText, "RUNBOOK_MODE=host-prereqs");
        StringAssert.Contains(readmeText, "RUNBOOK_MODE=parity-checklist bash scripts/runbook.sh");
        StringAssert.Contains(readmeText, "CHUMMER_PORTAL_DOWNLOADS_DEPLOY_ENABLED");
        StringAssert.Contains(readmeText, "CHUMMER_PORTAL_DOWNLOADS_REQUIRE_PUBLISHED_VERSION");
        StringAssert.Contains(readmeText, "scripts/publish-download-bundle-s3.sh");
        StringAssert.Contains(readmeText, "docs/SELF_HOSTED_DOWNLOADS_RUNBOOK.md");
        StringAssert.Contains(readmeText, "scripts/runbook-strict-host-gates.sh");
        StringAssert.Contains(readmeText, "Live deployment verification is required");
        StringAssert.Contains(readmeText, "Recommended self-hosted deployment");
        StringAssert.Contains(readmeText, "Alternate object-storage deployment");
        StringAssert.Contains(readmeText, "Treat object storage as the alternate topology, not the default");
        StringAssert.Contains(readmeText, "docs/examples/self-hosted-downloads.env.example");
        StringAssert.Contains(readmeText, "RUNBOOK_MODE=downloads-smoke bash scripts/runbook.sh");
        StringAssert.Contains(readmeText, "RUNBOOK_LOG_DIR");
        StringAssert.Contains(readmeText, "RUNBOOK_STATE_DIR");
        StringAssert.Contains(portalSettingsText, "\"HubBaseUrl\": \"http://127.0.0.1:8092/\"");
        StringAssert.Contains(portalSettingsText, "\"HubProxyBaseUrl\": \"\"");
        StringAssert.Contains(portalSettingsText, "\"SessionBaseUrl\": \"http://127.0.0.1:8093/\"");
        StringAssert.Contains(portalSettingsText, "\"SessionProxyBaseUrl\": \"\"");
        StringAssert.Contains(portalSettingsText, "\"CoachBaseUrl\": \"http://127.0.0.1:8094/\"");
        StringAssert.Contains(portalSettingsText, "\"CoachProxyBaseUrl\": \"\"");
        StringAssert.Contains(portalSettingsText, "\"ChummerRunUrl\": \"\"");
        StringAssert.Contains(portalSettingsText, "\"AiProxyBaseUrl\": \"\"");
        StringAssert.Contains(portalSettingsText, "\"DownloadsBaseUrl\": \"/downloads/\"");
        StringAssert.Contains(portalSettingsText, "\"DownloadsFallbackUrl\": \"\"");
        Assert.IsFalse(
            portalSettingsText.Contains("github.com/ArchonMegalon/chummer5a/releases/latest", StringComparison.Ordinal),
            "Portal default downloads base URL should remain self-hosted by default.");
        Assert.IsFalse(
            readmeText.Contains("two UI heads (`Chummer.Blazor`, `Chummer.Avalonia`)", StringComparison.Ordinal),
            "README summary regressed to outdated two-head architecture language.");
    }

    [TestMethod]
    public void Shell_and_overview_share_bootstrap_provider_for_startup_contract_data()
    {
        string shellContractsPath = FindPresentationContractsPath("ShellBootstrapContracts.cs");
        string shellContractsText = File.ReadAllText(shellContractsPath);
        string providerContractPath = FindPath("Chummer.Presentation", "Shell", "IShellBootstrapDataProvider.cs");
        string providerContractText = File.ReadAllText(providerContractPath);
        string providerImplementationPath = FindPath("Chummer.Presentation", "Shell", "ShellBootstrapDataProvider.cs");
        string providerImplementationText = File.ReadAllText(providerImplementationPath);
        string shellEndpointsPath = FindPath("Chummer.Api", "Endpoints", "ShellEndpoints.cs");
        string shellEndpointsText = File.ReadAllText(shellEndpointsPath);
        string shellPresenterPath = FindPath("Chummer.Presentation", "Shell", "ShellPresenter.cs");
        string shellPresenterText = File.ReadAllText(shellPresenterPath);
        string overviewPresenterPath = FindPath("Chummer.Presentation", "Overview", "CharacterOverviewPresenter.cs");
        string overviewPresenterText = File.ReadAllText(overviewPresenterPath);
        string blazorProgramPath = FindPath("Chummer.Blazor", "Program.cs");
        string blazorProgramText = File.ReadAllText(blazorProgramPath);
        string desktopProgramPath = FindPath("Chummer.Blazor.Desktop", "Program.cs");
        string desktopProgramText = File.ReadAllText(desktopProgramPath);
        string avaloniaAppPath = FindPath("Chummer.Avalonia", "App.axaml.cs");
        string avaloniaAppText = File.ReadAllText(avaloniaAppPath);

        StringAssert.Contains(shellContractsText, "IReadOnlyList<WorkflowDefinition>? WorkflowDefinitions");
        StringAssert.Contains(shellContractsText, "IReadOnlyList<WorkflowSurfaceDefinition>? WorkflowSurfaces");
        StringAssert.Contains(shellContractsText, "ActiveRuntimeStatusProjection? ActiveRuntime = null");
        StringAssert.Contains(providerContractText, "public interface IShellBootstrapDataProvider");
        StringAssert.Contains(providerContractText, "GetWorkspacesAsync");
        StringAssert.Contains(providerContractText, "ShellBootstrapData");
        StringAssert.Contains(providerContractText, "WorkflowDefinitions");
        StringAssert.Contains(providerContractText, "WorkflowSurfaces");
        StringAssert.Contains(providerContractText, "ActiveRuntimeStatusProjection? ActiveRuntime = null");
        StringAssert.Contains(providerImplementationText, "public sealed class ShellBootstrapDataProvider");
        StringAssert.Contains(providerImplementationText, "BootstrapCacheWindow");
        StringAssert.Contains(providerImplementationText, "GetWorkspacesAsync");
        StringAssert.Contains(providerImplementationText, "_client.GetShellBootstrapAsync");
        StringAssert.Contains(providerImplementationText, "DefaultBootstrapCacheKey");
        StringAssert.Contains(providerImplementationText, "WorkflowDefinitions: snapshot.WorkflowDefinitions ?? []");
        StringAssert.Contains(providerImplementationText, "WorkflowSurfaces: snapshot.WorkflowSurfaces ?? []");
        StringAssert.Contains(providerImplementationText, "ActiveRuntime: snapshot.ActiveRuntime");
        Assert.IsFalse(providerImplementationText.Contains("_client.ListWorkspacesAsync", StringComparison.Ordinal));
        Assert.IsFalse(providerImplementationText.Contains("_client.GetShellPreferencesAsync", StringComparison.Ordinal));
        Assert.IsFalse(providerImplementationText.Contains("_client.GetShellSessionAsync", StringComparison.Ordinal));

        StringAssert.Contains(shellEndpointsText, "IActiveRuntimeStatusService activeRuntimeStatusService");
        StringAssert.Contains(shellEndpointsText, "WorkflowDefinitions: shellCatalogResolver.ResolveWorkflowDefinitions(requestedRulesetId)");
        StringAssert.Contains(shellEndpointsText, "WorkflowSurfaces: shellCatalogResolver.ResolveWorkflowSurfaces(requestedRulesetId)");
        StringAssert.Contains(shellEndpointsText, "ActiveRuntime: activeRuntimeStatusService.GetActiveProfileStatus(owner, requestedRulesetId)");
        StringAssert.Contains(shellPresenterText, "_bootstrapDataProvider.GetAsync");
        StringAssert.Contains(shellPresenterText, "WorkflowDefinitions = workflowDefinitions");
        StringAssert.Contains(shellPresenterText, "WorkflowSurfaces = workflowSurfaces");
        StringAssert.Contains(shellPresenterText, "ActiveRuntime = bootstrap.ActiveRuntime");
        StringAssert.Contains(overviewPresenterText, "_bootstrapDataProvider.GetAsync");
        StringAssert.Contains(overviewPresenterText, "WorkflowDefinitions: shellState.WorkflowDefinitions ?? []");
        StringAssert.Contains(overviewPresenterText, "WorkflowSurfaces: shellState.WorkflowSurfaces ?? []");
        StringAssert.Contains(overviewPresenterText, "ActiveRuntime: shellState.ActiveRuntime");
        Assert.IsFalse(shellPresenterText.Contains("_client.GetCommandsAsync", StringComparison.Ordinal));
        Assert.IsFalse(shellPresenterText.Contains("_runtimeClient.GetCommandsAsync", StringComparison.Ordinal));
        Assert.IsFalse(shellPresenterText.Contains("_runtimeClient.GetNavigationTabsAsync", StringComparison.Ordinal));
        Assert.IsFalse(overviewPresenterText.Contains("_client.GetCommandsAsync", StringComparison.Ordinal));

        StringAssert.Contains(blazorProgramText, "AddScoped<IShellBootstrapDataProvider, ShellBootstrapDataProvider>();");
        StringAssert.Contains(desktopProgramText, "AddSingleton<IShellBootstrapDataProvider, ShellBootstrapDataProvider>();");
        StringAssert.Contains(avaloniaAppText, "AddSingleton<IShellBootstrapDataProvider, ShellBootstrapDataProvider>();");
    }

    [TestMethod]
    public void Shell_preferences_and_session_are_persisted_through_separate_contracts()
    {
        string shellContractsPath = FindPresentationContractsPath("ShellBootstrapContracts.cs");
        string shellContractsText = File.ReadAllText(shellContractsPath);
        string ownerScopePath = FindPath("Chummer.Contracts", "Owners", "OwnerScope.cs");
        string ownerScopeText = File.ReadAllText(ownerScopePath);
        string ownerContextAccessorPath = FindPath("Chummer.Application", "Owners", "IOwnerContextAccessor.cs");
        string ownerContextAccessorText = File.ReadAllText(ownerContextAccessorPath);
        string clientContractPath = FindPath("Chummer.Presentation", "IChummerClient.cs");
        string clientContractText = File.ReadAllText(clientContractPath);
        string shellEndpointsPath = FindPath("Chummer.Api", "Endpoints", "ShellEndpoints.cs");
        string shellEndpointsText = File.ReadAllText(shellEndpointsPath);
        string shellPresenterPath = FindPath("Chummer.Presentation", "Shell", "ShellPresenter.cs");
        string shellPresenterText = File.ReadAllText(shellPresenterPath);
        string infrastructureDiPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string infrastructureDiText = File.ReadAllText(infrastructureDiPath);
        string localOwnerContextAccessorPath = FindPath("Chummer.Infrastructure", "Owners", "LocalOwnerContextAccessor.cs");
        string localOwnerContextAccessorText = File.ReadAllText(localOwnerContextAccessorPath);
        string shellPreferencesServicePath = FindPath("Chummer.Application", "Tools", "ShellPreferencesService.cs");
        string shellPreferencesServiceText = File.ReadAllText(shellPreferencesServicePath);
        string shellSessionServicePath = FindPath("Chummer.Application", "Tools", "ShellSessionService.cs");
        string shellSessionServiceText = File.ReadAllText(shellSessionServicePath);
        string shellPreferencesStorePath = FindPath("Chummer.Infrastructure", "Files", "SettingsShellPreferencesStore.cs");
        string shellPreferencesStoreText = File.ReadAllText(shellPreferencesStorePath);
        string shellSessionStorePath = FindPath("Chummer.Infrastructure", "Files", "SettingsShellSessionStore.cs");
        string shellSessionStoreText = File.ReadAllText(shellSessionStorePath);
        string settingsOwnerScopePath = FindPath("Chummer.Infrastructure", "Files", "SettingsOwnerScope.cs");
        string settingsOwnerScopeText = File.ReadAllText(settingsOwnerScopePath);

        StringAssert.Contains(shellContractsText, "public sealed record ShellPreferences");
        StringAssert.Contains(shellContractsText, "public sealed record ShellSessionState");
        StringAssert.Contains(ownerScopeText, "public readonly record struct OwnerScope");
        StringAssert.Contains(ownerScopeText, "LocalSingleUser");
        StringAssert.Contains(ownerContextAccessorText, "public interface IOwnerContextAccessor");
        StringAssert.Contains(ownerContextAccessorText, "OwnerScope Current");
        StringAssert.Contains(shellContractsText, "string? ActiveTabId");
        StringAssert.Contains(shellContractsText, "IReadOnlyDictionary<string, string>? ActiveTabsByWorkspace");
        Assert.IsFalse(shellContractsText.Contains("ShellUserPreferences", StringComparison.Ordinal));

        StringAssert.Contains(clientContractText, "GetShellPreferencesAsync");
        StringAssert.Contains(clientContractText, "SaveShellPreferencesAsync");
        StringAssert.Contains(clientContractText, "GetShellSessionAsync");
        StringAssert.Contains(clientContractText, "SaveShellSessionAsync");
        StringAssert.Contains(clientContractText, "Task<ShellBootstrapSnapshot> GetShellBootstrapAsync(string? rulesetId, CancellationToken ct);");
        Assert.IsFalse(
            clientContractText.Contains("GetShellBootstrapAsync(string? rulesetId, CancellationToken ct)\n    {", StringComparison.Ordinal),
            "IChummerClient should not include default interface method bodies for shell bootstrap composition.");
        Assert.IsFalse(
            clientContractText.Contains("GetShellBootstrapAsync(string? rulesetId, CancellationToken ct) =>", StringComparison.Ordinal),
            "IChummerClient should not include expression-bodied default interface methods for shell bootstrap composition.");

        StringAssert.Contains(shellEndpointsText, "/api/shell/preferences");
        StringAssert.Contains(shellEndpointsText, "/api/shell/session");
        StringAssert.Contains(shellEndpointsText, "IOwnerContextAccessor ownerContextAccessor");
        StringAssert.Contains(shellEndpointsText, "IShellSessionService shellSessionService");
        StringAssert.Contains(shellEndpointsText, "OwnerScope owner = ownerContextAccessor.Current;");
        StringAssert.Contains(shellEndpointsText, "shellPreferencesService.Load(owner)");
        StringAssert.Contains(shellEndpointsText, "shellPreferencesService.Save(owner, preferences ?? ShellPreferences.Default)");
        StringAssert.Contains(shellEndpointsText, "shellSessionService.Load(owner)");
        StringAssert.Contains(shellEndpointsText, "shellSessionService.Save(owner, session ?? ShellSessionState.Default)");
        StringAssert.Contains(shellEndpointsText, "ActiveTabId: session.ActiveTabId");
        StringAssert.Contains(shellEndpointsText, "ActiveTabsByWorkspace: session.ActiveTabsByWorkspace");

        StringAssert.Contains(shellPresenterText, "SaveShellPreferencesAsync");
        StringAssert.Contains(shellPresenterText, "SaveShellSessionAsync");
        StringAssert.Contains(shellPresenterText, "ActiveTabId = resolvedActiveTabId");
        StringAssert.Contains(shellPresenterText, "_activeTabsByWorkspace");
        StringAssert.Contains(shellPresenterText, "BuildUpdatedWorkspaceTabMap");
        Assert.IsFalse(shellPresenterText.Contains("new ShellUserPreferences", StringComparison.Ordinal));
        StringAssert.Contains(shellPreferencesServiceText, "Load(OwnerScope owner)");
        StringAssert.Contains(shellPreferencesServiceText, "Save(OwnerScope owner, ShellPreferences preferences)");
        StringAssert.Contains(shellPreferencesServiceText, "OwnerScope.LocalSingleUser");
        StringAssert.Contains(shellSessionServiceText, "Load(OwnerScope owner)");
        StringAssert.Contains(shellSessionServiceText, "Save(OwnerScope owner, ShellSessionState session)");
        StringAssert.Contains(shellSessionServiceText, "OwnerScope.LocalSingleUser");
        StringAssert.Contains(shellPreferencesStoreText, "_settingsStore.Load(owner, SettingsOwnerScope.GlobalSettingsScope)");
        StringAssert.Contains(shellPreferencesStoreText, "_settingsStore.Save(owner, SettingsOwnerScope.GlobalSettingsScope, settings)");
        StringAssert.Contains(shellSessionStoreText, "_settingsStore.Load(owner, SettingsOwnerScope.GlobalSettingsScope)");
        StringAssert.Contains(shellSessionStoreText, "_settingsStore.Save(owner, SettingsOwnerScope.GlobalSettingsScope, settings)");
        StringAssert.Contains(settingsOwnerScopeText, "GlobalSettingsScope");
        StringAssert.Contains(localOwnerContextAccessorText, "OwnerScope.LocalSingleUser");

        StringAssert.Contains(infrastructureDiText, "AddSingleton<IOwnerContextAccessor, LocalOwnerContextAccessor>();");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IShellPreferencesStore, SettingsShellPreferencesStore>();");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IShellSessionStore, SettingsShellSessionStore>();");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IShellPreferencesService, ShellPreferencesService>();");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IShellSessionService, ShellSessionService>();");
    }

    [TestMethod]
    public void Owner_context_accessor_routes_api_and_runtime_calls_through_owner_scoped_services()
    {
        string settingsEndpointsPath = FindPath("Chummer.Api", "Endpoints", "SettingsEndpoints.cs");
        string settingsEndpointsText = File.ReadAllText(settingsEndpointsPath);
        string workspaceEndpointsPath = FindPath("Chummer.Api", "Endpoints", "WorkspaceEndpoints.cs");
        string workspaceEndpointsText = File.ReadAllText(workspaceEndpointsPath);
        string rosterEndpointsPath = FindPath("Chummer.Api", "Endpoints", "RosterEndpoints.cs");
        string rosterEndpointsText = File.ReadAllText(rosterEndpointsPath);
        string inProcessClientPath = FindPath("Chummer.Desktop.Runtime", "InProcessChummerClient.cs");
        string inProcessClientText = File.ReadAllText(inProcessClientPath);

        StringAssert.Contains(workspaceEndpointsText, "IOwnerContextAccessor ownerContextAccessor");
        StringAssert.Contains(workspaceEndpointsText, "WorkspaceImportResult result = workspaceService.Import(owner, ToImportDocument(request));");
        StringAssert.Contains(workspaceEndpointsText, "workspaceService.List(owner, effectiveMaxCount)");
        StringAssert.Contains(workspaceEndpointsText, "workspaceService.Close(owner, workspaceId)");
        StringAssert.Contains(workspaceEndpointsText, "workspaceService.GetSection(owner, workspaceId, sectionId)");
        StringAssert.Contains(workspaceEndpointsText, "workspaceService.GetSummary(owner, workspaceId)");
        StringAssert.Contains(workspaceEndpointsText, "workspaceService.Validate(owner, workspaceId)");
        StringAssert.Contains(workspaceEndpointsText, "workspaceService.UpdateMetadata(owner, workspaceId, command)");
        StringAssert.Contains(workspaceEndpointsText, "workspaceService.Save(owner, workspaceId)");
        StringAssert.Contains(workspaceEndpointsText, "workspaceService.Download(owner, workspaceId)");
        StringAssert.Contains(workspaceEndpointsText, "workspaceService.Export(owner, workspaceId)");
        StringAssert.Contains(workspaceEndpointsText, "workspaceService.Print(owner, workspaceId)");

        StringAssert.Contains(rosterEndpointsText, "IOwnerContextAccessor ownerContextAccessor");
        StringAssert.Contains(rosterEndpointsText, "rosterStore.Load(owner)");
        StringAssert.Contains(rosterEndpointsText, "rosterStore.Upsert(owner, entry)");
        StringAssert.Contains(settingsEndpointsText, "IOwnerContextAccessor ownerContextAccessor");
        StringAssert.Contains(settingsEndpointsText, "settingsStore.Load(owner, normalizedScope)");
        StringAssert.Contains(settingsEndpointsText, "settingsStore.Save(owner, normalizedScope, settings ?? new JsonObject())");

        StringAssert.Contains(inProcessClientText, "private readonly IOwnerContextAccessor _ownerContextAccessor;");
        StringAssert.Contains(inProcessClientText, "_ownerContextAccessor = ownerContextAccessor ?? new LocalOwnerContextAccessor();");
        StringAssert.Contains(inProcessClientText, "_workspaceService.Import(owner, document)");
        StringAssert.Contains(inProcessClientText, "_workspaceService.List(owner)");
        StringAssert.Contains(inProcessClientText, "_workspaceService.Close(owner, id)");
        StringAssert.Contains(inProcessClientText, "_shellPreferencesService.Load(owner)");
        StringAssert.Contains(inProcessClientText, "_shellPreferencesService.Save(owner, preferences)");
        StringAssert.Contains(inProcessClientText, "_shellSessionService.Load(owner)");
        StringAssert.Contains(inProcessClientText, "_shellSessionService.Save(owner, new ShellSessionState(");
        StringAssert.Contains(inProcessClientText, "_workspaceService.List(owner, ShellBootstrapDefaults.MaxWorkspaces)");
        StringAssert.Contains(inProcessClientText, "_activeRuntimeStatusService?.GetActiveProfileStatus(owner, effectiveRulesetId)");
        StringAssert.Contains(inProcessClientText, "_workspaceService.GetSummary(owner, id)");
        StringAssert.Contains(inProcessClientText, "_workspaceService.Validate(owner, id)");
        StringAssert.Contains(inProcessClientText, "_workspaceService.UpdateMetadata(owner, id, command)");
        StringAssert.Contains(inProcessClientText, "_workspaceService.Save(owner, id)");
        StringAssert.Contains(inProcessClientText, "_workspaceService.Download(owner, id)");
        StringAssert.Contains(inProcessClientText, "_workspaceService.Export(owner, id)");
        StringAssert.Contains(inProcessClientText, "_workspaceService.Print(owner, id)");
    }

    [TestMethod]
    public void Shell_session_restore_does_not_infer_active_workspace_from_workspace_order()
    {
        string shellEndpointsPath = FindPath("Chummer.Api", "Endpoints", "ShellEndpoints.cs");
        string shellEndpointsText = File.ReadAllText(shellEndpointsPath);
        string bootstrapProviderPath = FindPath("Chummer.Presentation", "Shell", "ShellBootstrapDataProvider.cs");
        string bootstrapProviderText = File.ReadAllText(bootstrapProviderPath);
        string inProcessClientPath = FindPath("Chummer.Desktop.Runtime", "InProcessChummerClient.cs");
        string inProcessClientText = File.ReadAllText(inProcessClientPath);
        string shellPresenterPath = FindPath("Chummer.Presentation", "Shell", "ShellPresenter.cs");
        string shellPresenterText = File.ReadAllText(shellPresenterPath);

        Assert.IsTrue(
            Regex.IsMatch(shellEndpointsText, @"if\s*\(string\.IsNullOrWhiteSpace\(preferredActiveWorkspaceId\)\)\s*return null;", RegexOptions.Multiline),
            "Shell bootstrap endpoint should return no active workspace when session state is empty.");
        StringAssert.Contains(bootstrapProviderText, "ActiveWorkspaceId: snapshot.ActiveWorkspaceId");
        Assert.IsFalse(
            bootstrapProviderText.Contains("preferredActiveWorkspaceId", StringComparison.Ordinal),
            "Shell bootstrap provider should trust the bootstrap snapshot instead of reconstructing active workspace selection.");
        Assert.IsTrue(
            Regex.IsMatch(inProcessClientText, @"if\s*\(string\.IsNullOrWhiteSpace\(persistedActiveWorkspaceId\)\)\s*return null;", RegexOptions.Multiline),
            "In-process bootstrap client should return no active workspace when session state is empty.");
        Assert.IsTrue(
            Regex.IsMatch(shellPresenterText, @"if\s*\(requestedActiveWorkspaceId is null\)\s*return null;", RegexOptions.Multiline),
            "Shell presenter should preserve the explicit no-active-workspace state instead of auto-selecting one.");

        Assert.IsFalse(
            shellEndpointsText.Contains("workspaces[0]", StringComparison.Ordinal),
            "Shell bootstrap endpoint must not fall back to the first workspace.");
        Assert.IsFalse(
            bootstrapProviderText.Contains("workspaces[0]", StringComparison.Ordinal),
            "Shell bootstrap provider must not fall back to the first workspace.");
        Assert.IsFalse(
            inProcessClientText.Contains("workspaces[0]", StringComparison.Ordinal),
            "In-process bootstrap client must not fall back to the first workspace.");
    }

    [TestMethod]
    public void Shell_surface_resolution_uses_shell_state_for_renderer_session_facts()
    {
        string shellSurfaceResolverPath = FindPath("Chummer.Presentation", "Shell", "ShellSurfaceResolver.cs");
        string shellSurfaceResolverText = File.ReadAllText(shellSurfaceResolverPath);
        string characterOverviewPresenterPath = FindPath("Chummer.Presentation", "Overview", "CharacterOverviewPresenter.cs");
        string characterOverviewPresenterText = File.ReadAllText(characterOverviewPresenterPath);
        string avaloniaProjectorPath = FindPath("Chummer.Avalonia", "MainWindow.ShellFrameProjector.cs");
        string avaloniaProjectorText = File.ReadAllText(avaloniaProjectorPath);

        Assert.IsFalse(
            shellSurfaceResolverText.Contains("overviewState.Session.ActiveWorkspaceId", StringComparison.Ordinal),
            "Shell surface resolver must not source the active workspace from overview session state.");
        Assert.IsFalse(
            shellSurfaceResolverText.Contains("overviewState.WorkspaceId", StringComparison.Ordinal),
            "Shell surface resolver must not source the active workspace from overview workspace state.");
        Assert.IsFalse(
            shellSurfaceResolverText.Contains("overviewState.ActiveTabId", StringComparison.Ordinal),
            "Shell surface resolver must not source the active tab from overview state.");
        Assert.IsFalse(
            shellSurfaceResolverText.Contains("overviewState.Session.OpenWorkspaces", StringComparison.Ordinal),
            "Shell surface resolver must not source open workspaces from overview session state.");
        Assert.IsFalse(
            shellSurfaceResolverText.Contains("overviewState.OpenWorkspaces", StringComparison.Ordinal),
            "Shell surface resolver must not source open workspace saved status from overview state.");
        Assert.IsFalse(
            shellSurfaceResolverText.Contains("overviewState.LastCommandId", StringComparison.Ordinal),
            "Shell surface resolver must not fall back to overview command history.");
        Assert.IsFalse(
            shellSurfaceResolverText.Contains("overviewState.Notice", StringComparison.Ordinal),
            "Shell surface resolver must not fall back to overview notices.");
        Assert.IsFalse(
            shellSurfaceResolverText.Contains("overviewState.Error", StringComparison.Ordinal),
            "Shell surface resolver must not fall back to overview errors.");
        StringAssert.Contains(shellSurfaceResolverText, "string? activeTabId = shellState.ActiveTabId;");
        StringAssert.Contains(shellSurfaceResolverText, "CharacterWorkspaceId? activeWorkspaceId = shellState.ActiveWorkspaceId;");
        StringAssert.Contains(shellSurfaceResolverText, "shellState.OpenWorkspaces");
        StringAssert.Contains(shellSurfaceResolverText, "LastCommandId: shellState.LastCommandId");
        StringAssert.Contains(shellSurfaceResolverText, "Notice = shellState.Notice");
        StringAssert.Contains(characterOverviewPresenterText, "_shellPresenter?.SyncOverviewFeedback(CreateShellOverviewFeedback(state));");

        Assert.IsFalse(
            avaloniaProjectorText.Contains("shellSurface.ActiveWorkspaceId ?? state.WorkspaceId", StringComparison.Ordinal),
            "Avalonia shell projector must not fall back to overview workspace state for renderer shell selection.");
        StringAssert.Contains(avaloniaProjectorText, "CharacterWorkspaceId? activeWorkspaceId = shellSurface.ActiveWorkspaceId;");
    }

    [TestMethod]
    public void Dual_head_shell_actions_and_controls_are_scoped_by_active_ruleset()
    {
        string blazorShellCodePath = FindPath("Chummer.Blazor", "Components", "Layout", "DesktopShell.razor.cs");
        string blazorShellCodeText = File.ReadAllText(blazorShellCodePath);
        string shellSurfaceResolverPath = FindPath("Chummer.Presentation", "Shell", "ShellSurfaceResolver.cs");
        string shellSurfaceResolverText = File.ReadAllText(shellSurfaceResolverPath);
        string avaloniaStatePath = FindPath("Chummer.Avalonia", "MainWindow.StateRefresh.cs");
        string avaloniaStateText = File.ReadAllText(avaloniaStatePath);
        string avaloniaProjectorPath = FindPath("Chummer.Avalonia", "MainWindow.ShellFrameProjector.cs");
        string avaloniaProjectorText = File.ReadAllText(avaloniaProjectorPath);
        string dualHeadAcceptancePath = FindPath("Chummer.Tests", "Presentation", "DualHeadAcceptanceTests.cs");
        string dualHeadAcceptanceText = File.ReadAllText(dualHeadAcceptancePath);

        StringAssert.Contains(blazorShellCodeText, "public IShellSurfaceResolver ShellSurfaceResolver { get; set; } = default!;");
        StringAssert.Contains(blazorShellCodeText, "RefreshShellSurfaceState()");
        StringAssert.Contains(blazorShellCodeText, "ShellSurfaceResolver.Resolve(State, ShellState)");
        StringAssert.Contains(blazorShellCodeText, "_shellSurfaceState.Commands");
        StringAssert.Contains(blazorShellCodeText, "_shellSurfaceState.MenuRoots");
        StringAssert.Contains(blazorShellCodeText, "_shellSurfaceState.NavigationTabs");

        StringAssert.Contains(avaloniaStateText, "_shellSurfaceResolver.Resolve(state, _shellPresenter.State)");
        StringAssert.Contains(avaloniaStateText, "MainWindowShellFrameProjector.Project(");
        StringAssert.Contains(avaloniaStateText, "ApplyShellFrame(shellFrame);");
        Assert.IsFalse(avaloniaStateText.Contains("shellSurface.Commands", StringComparison.Ordinal));
        StringAssert.Contains(avaloniaProjectorText, "shellSurface.Commands");
        StringAssert.Contains(avaloniaProjectorText, "shellSurface.MenuRoots");
        StringAssert.Contains(avaloniaProjectorText, "shellSurface.NavigationTabs");
        StringAssert.Contains(avaloniaProjectorText, "shellSurface.WorkspaceActions");
        StringAssert.Contains(avaloniaProjectorText, "shellSurface.ActiveWorkflowSurfaceActions");
        StringAssert.Contains(avaloniaProjectorText, "ProjectCommandDialogState(");
        Assert.IsFalse(shellSurfaceResolverText.Contains("DesktopUiControls", StringComparison.Ordinal));
        Assert.IsFalse(shellSurfaceResolverText.Contains("ResolveDesktopUiControlsForTab(", StringComparison.Ordinal));

        StringAssert.Contains(dualHeadAcceptanceText, "ShellCatalogResolver.ResolveWorkspaceActionsForTab(");
        Assert.IsFalse(dualHeadAcceptanceText.Contains("WorkspaceSurfaceActionCatalog.ForTab(avaloniaState.ActiveTabId", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Ruleset_shell_catalog_resolver_service_is_registered_and_consumed_without_raw_plugin_injection()
    {
        string rulesetServicesPath = FindPath("Chummer.Contracts", "Rulesets", "RulesetShellServices.cs");
        string rulesetServicesText = File.ReadAllText(rulesetServicesPath);
        string rulesetHostingServicesPath = FindPath("Chummer.Rulesets.Hosting", "RulesetShellServices.cs");
        string rulesetHostingServicesText = File.ReadAllText(rulesetHostingServicesPath);
        string rulesetDiExtensionsPath = FindPath("Chummer.Rulesets.Sr5", "ServiceCollectionRulesetExtensions.cs");
        string rulesetDiExtensionsText = File.ReadAllText(rulesetDiExtensionsPath);
        string rulesetHostingDiExtensionsPath = FindPath("Chummer.Rulesets.Hosting", "ServiceCollectionRulesetHostingExtensions.cs");
        string rulesetHostingDiExtensionsText = File.ReadAllText(rulesetHostingDiExtensionsPath);
        string sr5RulesetPluginPath = FindPath("Chummer.Rulesets.Sr5", "Sr5RulesetPlugin.cs");
        string sr5RulesetPluginText = File.ReadAllText(sr5RulesetPluginPath);
        string sr5ShellCatalogsPath = FindPath("Chummer.Rulesets.Sr5", "Sr5ShellCatalogs.cs");
        string sr5ShellCatalogsText = File.ReadAllText(sr5ShellCatalogsPath);
        string sr4RulesetProjectPath = FindPath("Chummer.Rulesets.Sr4", "Chummer.Rulesets.Sr4.csproj");
        string sr4RulesetProjectText = File.ReadAllText(sr4RulesetProjectPath);
        string sr4RulesetPluginPath = FindPath("Chummer.Rulesets.Sr4", "Sr4RulesetPlugin.cs");
        string sr4RulesetPluginText = File.ReadAllText(sr4RulesetPluginPath);
        string sr4ShellCatalogsPath = FindPath("Chummer.Rulesets.Sr4", "Sr4ShellCatalogs.cs");
        string sr4ShellCatalogsText = File.ReadAllText(sr4ShellCatalogsPath);
        string sr4RulesetDiPath = FindPath("Chummer.Rulesets.Sr4", "ServiceCollectionRulesetExtensions.cs");
        string sr4RulesetDiText = File.ReadAllText(sr4RulesetDiPath);
        string sr6RulesetPluginPath = FindPath("Chummer.Rulesets.Sr6", "Sr6RulesetPlugin.cs");
        string sr6RulesetPluginText = File.ReadAllText(sr6RulesetPluginPath);
        string sr6ShellCatalogsPath = FindPath("Chummer.Rulesets.Sr6", "Sr6ShellCatalogs.cs");
        string sr6ShellCatalogsText = File.ReadAllText(sr6ShellCatalogsPath);
        string sr6RulesetDiPath = FindPath("Chummer.Rulesets.Sr6", "ServiceCollectionRulesetExtensions.cs");
        string sr6RulesetDiText = File.ReadAllText(sr6RulesetDiPath);
        string infrastructureDiPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string infrastructureDiText = File.ReadAllText(infrastructureDiPath);
        string desktopRuntimeDiPath = FindPath("Chummer.Desktop.Runtime", "ServiceCollectionDesktopRuntimeExtensions.cs");
        string desktopRuntimeDiText = File.ReadAllText(desktopRuntimeDiPath);
        string blazorProgramPath = FindPath("Chummer.Blazor", "Program.cs");
        string blazorProgramText = File.ReadAllText(blazorProgramPath);
        string commandEndpointsPath = FindPath("Chummer.Api", "Endpoints", "CommandEndpoints.cs");
        string commandEndpointsText = File.ReadAllText(commandEndpointsPath);
        string navigationEndpointsPath = FindPath("Chummer.Api", "Endpoints", "NavigationEndpoints.cs");
        string navigationEndpointsText = File.ReadAllText(navigationEndpointsPath);
        string blazorShellPath = FindPath("Chummer.Blazor", "Components", "Layout", "DesktopShell.razor.cs");
        string blazorShellText = File.ReadAllText(blazorShellPath);
        string avaloniaMainWindowPath = FindPath("Chummer.Avalonia", "MainWindow.axaml.cs");
        string avaloniaMainWindowText = File.ReadAllText(avaloniaMainWindowPath);
        string shellStatePath = FindPath("Chummer.Presentation", "Shell", "ShellState.cs");
        string shellStateText = File.ReadAllText(shellStatePath);
        string shellPresenterPath = FindPath("Chummer.Presentation", "Shell", "ShellPresenter.cs");
        string shellPresenterText = File.ReadAllText(shellPresenterPath);
        string shellPresenterContractPath = FindPath("Chummer.Presentation", "Shell", "IShellPresenter.cs");
        string shellPresenterContractText = File.ReadAllText(shellPresenterContractPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);
        string backlogPath = FindPath("docs", "MIGRATION_BACKLOG.md");
        string backlogText = File.ReadAllText(backlogPath);

        StringAssert.Contains(rulesetServicesText, "public interface IRulesetPluginRegistry");
        StringAssert.Contains(rulesetServicesText, "public interface IRulesetSelectionPolicy");
        StringAssert.Contains(rulesetServicesText, "public interface IRulesetShellCatalogResolver");
        StringAssert.Contains(rulesetServicesText, "ResolveWorkflowDefinitions");
        StringAssert.Contains(rulesetServicesText, "ResolveWorkflowSurfaces");
        Assert.IsFalse(rulesetServicesText.Contains("public sealed class RulesetPluginRegistry", StringComparison.Ordinal));
        Assert.IsFalse(rulesetServicesText.Contains("public sealed class RulesetShellCatalogResolverService", StringComparison.Ordinal));
        Assert.IsFalse(PathExistsInCandidateRoots("Chummer.Contracts", "Rulesets", "RulesetShellCatalogResolver.cs"));
        StringAssert.Contains(rulesetHostingServicesText, "public sealed class RulesetPluginRegistry");
        StringAssert.Contains(rulesetHostingServicesText, "public sealed class RulesetShellCatalogResolverService");
        StringAssert.Contains(rulesetHostingServicesText, "public sealed class DefaultRulesetSelectionPolicy");
        StringAssert.Contains(rulesetHostingServicesText, "namespace Chummer.Rulesets.Hosting;");
        Assert.IsFalse(rulesetHostingServicesText.Contains("namespace Chummer.Contracts.Rulesets;", StringComparison.Ordinal));
        Assert.IsFalse(PathExistsInCandidateRoots("Chummer.Contracts", "Rulesets", "Sr5RulesetPlugin.cs"));

        StringAssert.Contains(rulesetDiExtensionsText, "AddSr5Ruleset(this IServiceCollection services)");
        StringAssert.Contains(rulesetDiExtensionsText, "Chummer.Rulesets.Sr5.Sr5RulesetPlugin");
        StringAssert.Contains(sr5RulesetPluginText, "public class Sr5RulesetPlugin");
        Assert.IsFalse(sr5RulesetPluginText.Contains("AppCommandCatalog.ForRuleset(", StringComparison.Ordinal));
        Assert.IsFalse(sr5RulesetPluginText.Contains("NavigationTabCatalog.ForRuleset(", StringComparison.Ordinal));
        Assert.IsFalse(sr5RulesetPluginText.Contains("WorkspaceSurfaceActionCatalog.ForRuleset(", StringComparison.Ordinal));
        Assert.IsFalse(sr5RulesetPluginText.Contains("GetDesktopUiControls()", StringComparison.Ordinal));
        StringAssert.Contains(sr5RulesetPluginText, "Rules = new RulesetRuleHostCapabilityAdapter(Capabilities);");
        StringAssert.Contains(sr5RulesetPluginText, "Scripts = new RulesetScriptHostCapabilityAdapter(Capabilities);");
        Assert.IsFalse(sr5RulesetPluginText.Contains("public class NoOpRulesetRuleHost", StringComparison.Ordinal));
        Assert.IsFalse(sr5RulesetPluginText.Contains("public class NoOpRulesetScriptHost", StringComparison.Ordinal));
        StringAssert.Contains(sr5RulesetPluginText, "GetWorkflowDefinitions()");
        StringAssert.Contains(sr5RulesetPluginText, "GetWorkflowSurfaces()");
        StringAssert.Contains(sr5ShellCatalogsText, "internal static class Sr5AppCommandCatalog");
        StringAssert.Contains(sr5ShellCatalogsText, "internal static class Sr5NavigationTabCatalog");
        StringAssert.Contains(sr5ShellCatalogsText, "internal static class Sr5WorkspaceSurfaceActionCatalog");
        Assert.IsFalse(sr5ShellCatalogsText.Contains("Sr5DesktopUiControlCatalog", StringComparison.Ordinal));
        StringAssert.Contains(sr4RulesetProjectText, "<Project Sdk=\"Microsoft.NET.Sdk\">");
        StringAssert.Contains(sr4RulesetDiText, "AddSr4Ruleset(this IServiceCollection services)");
        StringAssert.Contains(sr4RulesetDiText, "Chummer.Rulesets.Sr4.Sr4RulesetPlugin");
        StringAssert.Contains(sr4RulesetPluginText, "public class Sr4RulesetPlugin");
        StringAssert.Contains(sr4RulesetPluginText, "public string DisplayName => \"Shadowrun 4\";");
        Assert.IsFalse(sr4RulesetPluginText.Contains("AppCommandCatalog.ForRuleset(", StringComparison.Ordinal));
        Assert.IsFalse(sr4RulesetPluginText.Contains("NavigationTabCatalog.ForRuleset(", StringComparison.Ordinal));
        Assert.IsFalse(sr4RulesetPluginText.Contains("WorkspaceSurfaceActionCatalog.ForRuleset(", StringComparison.Ordinal));
        Assert.IsFalse(sr4RulesetPluginText.Contains("GetDesktopUiControls()", StringComparison.Ordinal));
        StringAssert.Contains(sr4RulesetPluginText, "Rules = new RulesetRuleHostCapabilityAdapter(Capabilities);");
        StringAssert.Contains(sr4RulesetPluginText, "Scripts = new RulesetScriptHostCapabilityAdapter(Capabilities);");
        Assert.IsFalse(sr4RulesetPluginText.Contains("public class Sr4NoOpRulesetRuleHost", StringComparison.Ordinal));
        Assert.IsFalse(sr4RulesetPluginText.Contains("public class Sr4NoOpRulesetScriptHost", StringComparison.Ordinal));
        StringAssert.Contains(sr4RulesetPluginText, "GetWorkflowDefinitions()");
        StringAssert.Contains(sr4RulesetPluginText, "GetWorkflowSurfaces()");
        StringAssert.Contains(sr4RulesetPluginText, "SR4 rules engine is not implemented; this ruleset remains experimental.");
        StringAssert.Contains(sr4RulesetPluginText, "Success: false");
        Assert.IsFalse(sr4RulesetPluginText.Contains("no-op evaluation applied", StringComparison.Ordinal));
        Assert.IsFalse(sr4RulesetPluginText.Contains("Success: true", StringComparison.Ordinal));
        StringAssert.Contains(sr4ShellCatalogsText, "internal static class Sr4AppCommandCatalog");
        StringAssert.Contains(sr4ShellCatalogsText, "internal static class Sr4NavigationTabCatalog");
        StringAssert.Contains(sr4ShellCatalogsText, "internal static class Sr4WorkspaceSurfaceActionCatalog");
        Assert.IsFalse(sr4ShellCatalogsText.Contains("Sr4DesktopUiControlCatalog", StringComparison.Ordinal));
        StringAssert.Contains(sr6RulesetDiText, "AddSr6Ruleset(this IServiceCollection services)");
        StringAssert.Contains(sr6RulesetDiText, "Chummer.Rulesets.Sr6.Sr6RulesetPlugin");
        StringAssert.Contains(sr6RulesetPluginText, "public class Sr6RulesetPlugin");
        StringAssert.Contains(sr6RulesetPluginText, "public string DisplayName => \"Shadowrun 6\";");
        Assert.IsFalse(sr6RulesetPluginText.Contains("AppCommandCatalog.ForRuleset(", StringComparison.Ordinal));
        Assert.IsFalse(sr6RulesetPluginText.Contains("NavigationTabCatalog.ForRuleset(", StringComparison.Ordinal));
        Assert.IsFalse(sr6RulesetPluginText.Contains("WorkspaceSurfaceActionCatalog.ForRuleset(", StringComparison.Ordinal));
        Assert.IsFalse(sr6RulesetPluginText.Contains("GetDesktopUiControls()", StringComparison.Ordinal));
        StringAssert.Contains(sr6RulesetPluginText, "Rules = new RulesetRuleHostCapabilityAdapter(Capabilities);");
        StringAssert.Contains(sr6RulesetPluginText, "Scripts = new RulesetScriptHostCapabilityAdapter(Capabilities);");
        Assert.IsFalse(sr6RulesetPluginText.Contains("public class Sr6NoOpRulesetRuleHost", StringComparison.Ordinal));
        Assert.IsFalse(sr6RulesetPluginText.Contains("public class Sr6NoOpRulesetScriptHost", StringComparison.Ordinal));
        StringAssert.Contains(sr6RulesetPluginText, "GetWorkflowDefinitions()");
        StringAssert.Contains(sr6RulesetPluginText, "GetWorkflowSurfaces()");
        StringAssert.Contains(sr6RulesetPluginText, "SR6 rules engine is not implemented; this ruleset remains experimental.");
        StringAssert.Contains(sr6RulesetPluginText, "Success: false");
        Assert.IsFalse(sr6RulesetPluginText.Contains("no-op evaluation applied", StringComparison.Ordinal));
        Assert.IsFalse(sr6RulesetPluginText.Contains("Success: true", StringComparison.Ordinal));
        StringAssert.Contains(sr6ShellCatalogsText, "internal static class Sr6AppCommandCatalog");
        StringAssert.Contains(sr6ShellCatalogsText, "internal static class Sr6NavigationTabCatalog");
        StringAssert.Contains(sr6ShellCatalogsText, "internal static class Sr6WorkspaceSurfaceActionCatalog");
        Assert.IsFalse(sr6ShellCatalogsText.Contains("Sr6DesktopUiControlCatalog", StringComparison.Ordinal));
        StringAssert.Contains(rulesetHostingDiExtensionsText, "AddRulesetInfrastructure(this IServiceCollection services)");
        StringAssert.Contains(rulesetHostingDiExtensionsText, "CHUMMER_DEFAULT_RULESET");
        StringAssert.Contains(rulesetHostingDiExtensionsText, "CreateRulesetSelectionOptions()");
        StringAssert.Contains(rulesetHostingDiExtensionsText, "TryAddSingleton<IRulesetPluginRegistry, RulesetPluginRegistry>();");
        StringAssert.Contains(rulesetHostingDiExtensionsText, "TryAddSingleton(_ => CreateRulesetSelectionOptions());");
        StringAssert.Contains(rulesetHostingDiExtensionsText, "TryAddSingleton<IRulesetSelectionPolicy, DefaultRulesetSelectionPolicy>();");
        StringAssert.Contains(rulesetHostingDiExtensionsText, "TryAddSingleton<IRulesetShellCatalogResolver, RulesetShellCatalogResolverService>();");
        StringAssert.Contains(rulesetHostingDiExtensionsText, "TryAddSingleton<IRulesetWorkspaceCodecResolver, RulesetWorkspaceCodecResolver>();");
        StringAssert.Contains(rulesetServicesText, "public sealed record RulesetSelectionOptions");
        StringAssert.Contains(rulesetHostingServicesText, "RulesetSelectionOptions");
        StringAssert.Contains(rulesetHostingServicesText, "Configured default ruleset");
        Assert.IsFalse(rulesetHostingServicesText.Contains("FirstOrDefault(rulesetId => !string.IsNullOrWhiteSpace(rulesetId))", StringComparison.Ordinal));
        StringAssert.Contains(infrastructureDiText, "services.AddRulesetInfrastructure();");
        Assert.IsFalse(infrastructureDiText.Contains("services.AddSr4Ruleset();", StringComparison.Ordinal));
        StringAssert.Contains(infrastructureDiText, "services.AddSr5Ruleset();");
        StringAssert.Contains(infrastructureDiText, "services.AddSr6Ruleset();");
        StringAssert.Contains(desktopRuntimeDiText, "services.AddRulesetInfrastructure();");
        Assert.IsFalse(desktopRuntimeDiText.Contains("services.AddSr4Ruleset();", StringComparison.Ordinal));
        StringAssert.Contains(desktopRuntimeDiText, "services.AddSr5Ruleset();");
        StringAssert.Contains(desktopRuntimeDiText, "services.AddSr6Ruleset();");
        StringAssert.Contains(blazorProgramText, "builder.Services.AddRulesetInfrastructure();");
        Assert.IsFalse(blazorProgramText.Contains("builder.Services.AddSr4Ruleset();", StringComparison.Ordinal));
        StringAssert.Contains(blazorProgramText, "builder.Services.AddSr5Ruleset();");
        StringAssert.Contains(blazorProgramText, "builder.Services.AddSr6Ruleset();");
        StringAssert.Contains(blazorProgramText, "AddSingleton<IShellSurfaceResolver, ShellSurfaceResolver>();");
        StringAssert.Contains(readmeText, "Default runtime registration currently enables SR5 and SR6 only.");
        StringAssert.Contains(readmeText, "CHUMMER_DEFAULT_RULESET");
        StringAssert.Contains(readmeText, "`Chummer.Rulesets.Sr4` remains a scaffolded/experimental module");
        StringAssert.Contains(backlogText, "default headless/desktop/web paths register SR5 and SR6");
        StringAssert.Contains(backlogText, "`Chummer.Rulesets.Sr4` remains scaffolded/experimental");
        string desktopRuntimeTestsPath = FindPath("Chummer.Tests", "ServiceCollectionDesktopRuntimeExtensionsTests.cs");
        string desktopRuntimeTestsText = File.ReadAllText(desktopRuntimeTestsPath);
        StringAssert.Contains(desktopRuntimeTestsText, "Default_ruleset_environment_variable_controls_shell_catalog_resolution");
        StringAssert.Contains(desktopRuntimeTestsText, "Default_ruleset_environment_variable_fails_when_ruleset_is_not_registered");
        StringAssert.Contains(desktopRuntimeTestsText, "IRulesetShellCatalogResolver shellCatalogResolver");
        StringAssert.Contains(desktopRuntimeTestsText, "ResolveCommands(null)");
        StringAssert.Contains(desktopRuntimeTestsText, "CHUMMER_DEFAULT_RULESET");

        StringAssert.Contains(commandEndpointsText, "IRulesetShellCatalogResolver shellCatalogResolver");
        StringAssert.Contains(commandEndpointsText, "shellCatalogResolver.ResolveCommands(ruleset)");
        StringAssert.Contains(navigationEndpointsText, "IRulesetShellCatalogResolver shellCatalogResolver");
        StringAssert.Contains(navigationEndpointsText, "shellCatalogResolver.ResolveNavigationTabs(ruleset)");
        string shellEndpointsPath = FindPath("Chummer.Api", "Endpoints", "ShellEndpoints.cs");
        string shellEndpointsText = File.ReadAllText(shellEndpointsPath);
        StringAssert.Contains(shellEndpointsText, "IRulesetSelectionPolicy rulesetSelectionPolicy");
        StringAssert.Contains(shellEndpointsText, "rulesetSelectionPolicy.GetDefaultRulesetId()");

        StringAssert.Contains(blazorShellText, "public IShellSurfaceResolver ShellSurfaceResolver { get; set; } = default!;");
        Assert.IsFalse(blazorShellText.Contains("IEnumerable<IRulesetPlugin> RulesetPlugins", StringComparison.Ordinal));
        StringAssert.Contains(avaloniaMainWindowText, "private readonly IShellSurfaceResolver _shellSurfaceResolver;");
        StringAssert.Contains(shellStateText, "string PreferredRulesetId");
        StringAssert.Contains(shellPresenterContractText, "Task SetPreferredRulesetAsync(string rulesetId, CancellationToken ct);");
        StringAssert.Contains(shellPresenterText, "State.PreferredRulesetId");
    }

    [TestMethod]
    public void Ruleset_seam_contracts_are_declared_without_changing_default_sr5_catalog_behavior()
    {
        string rulesetContractsPath = FindPath("Chummer.Contracts", "Rulesets", "RulesetContracts.cs");
        string rulesetContractsText = File.ReadAllText(rulesetContractsPath);
        string capabilityContractsPath = FindPath("Chummer.Contracts", "Rulesets", "RulesetCapabilityContracts.cs");
        string capabilityContractsText = File.ReadAllText(capabilityContractsPath);
        string workspaceModelsPath = FindPath("Chummer.Contracts", "Workspaces", "CharacterWorkspaceModels.cs");
        string workspaceModelsText = File.ReadAllText(workspaceModelsPath);
        string workspaceApiModelsPath = FindPath("Chummer.Contracts", "Workspaces", "WorkspaceApiModels.cs");
        string workspaceApiModelsText = File.ReadAllText(workspaceApiModelsPath);
        string commandDefinitionPath = FindCorePresentationContractsPath("AppCommandDefinition.cs");
        string commandDefinitionText = File.ReadAllText(commandDefinitionPath);
        string tabDefinitionPath = FindCorePresentationContractsPath("NavigationTabDefinition.cs");
        string tabDefinitionText = File.ReadAllText(tabDefinitionPath);
        string actionDefinitionPath = FindCorePresentationContractsPath("WorkspaceSurfaceActionDefinition.cs");
        string actionDefinitionText = File.ReadAllText(actionDefinitionPath);

        Assert.IsTrue(PathExistsInCandidateRoots("Chummer.Contracts", "Presentation", "AppCommandDefinition.cs"));
        Assert.IsTrue(PathExistsInCandidateRoots("Chummer.Contracts", "Presentation", "NavigationTabDefinition.cs"));
        Assert.IsTrue(PathExistsInCandidateRoots("Chummer.Contracts", "Presentation", "WorkspaceSurfaceActionDefinition.cs"));
        Assert.IsTrue(PathExistsInCandidateRoots("Chummer.Contracts", "Presentation", "WorkflowSurfaceContracts.cs"));
        Assert.IsTrue(PathExistsInCandidateRoots("Chummer.Contracts", "Presentation", "AppCommandCatalogResponse.cs"));
        Assert.IsTrue(PathExistsInCandidateRoots("Chummer.Contracts", "Presentation", "AppCommandIds.cs"));
        Assert.IsTrue(PathExistsInCandidateRoots("Chummer.Contracts", "Presentation", "NavigationTabCatalogResponse.cs"));
        Assert.IsFalse(PathExistsInCandidateRoots("Chummer.Presentation.Contracts", "Presentation", "AppCommandDefinition.cs"));
        Assert.IsFalse(PathExistsInCandidateRoots("Chummer.Presentation.Contracts", "Presentation", "NavigationTabDefinition.cs"));
        Assert.IsFalse(PathExistsInCandidateRoots("Chummer.Presentation.Contracts", "Presentation", "WorkspaceSurfaceActionDefinition.cs"));
        Assert.IsFalse(PathExistsInCandidateRoots("Chummer.Presentation.Contracts", "Presentation", "WorkflowSurfaceContracts.cs"));
        Assert.IsFalse(PathExistsInCandidateRoots("Chummer.Presentation.Contracts", "Presentation", "AppCommandCatalogResponse.cs"));
        Assert.IsFalse(PathExistsInCandidateRoots("Chummer.Presentation.Contracts", "Presentation", "AppCommandIds.cs"));
        Assert.IsFalse(PathExistsInCandidateRoots("Chummer.Presentation.Contracts", "Presentation", "NavigationTabCatalogResponse.cs"));
        Assert.IsFalse(PathExistsInCandidateRoots("Chummer.Contracts", "Presentation", "AppCommandCatalog.cs"));
        Assert.IsFalse(PathExistsInCandidateRoots("Chummer.Contracts", "Presentation", "NavigationTabCatalog.cs"));
        Assert.IsFalse(PathExistsInCandidateRoots("Chummer.Contracts", "Presentation", "WorkspaceSurfaceActionCatalog.cs"));
        Assert.IsFalse(PathExistsInCandidateRoots("Chummer.Contracts", "Presentation", "DesktopUiControlCatalog.cs"));
        Assert.IsFalse(PathExistsInCandidateRoots("Chummer.Contracts", "Presentation", "DesktopUiControlDefinition.cs"));

        string commandCatalogPath = FindPath("Chummer.Rulesets.Hosting", "Presentation", "AppCommandCatalog.cs");
        string commandCatalogText = File.ReadAllText(commandCatalogPath);
        string tabCatalogPath = FindPath("Chummer.Rulesets.Hosting", "Presentation", "NavigationTabCatalog.cs");
        string tabCatalogText = File.ReadAllText(tabCatalogPath);
        string actionCatalogPath = FindPath("Chummer.Rulesets.Hosting", "Presentation", "WorkspaceSurfaceActionCatalog.cs");
        string actionCatalogText = File.ReadAllText(actionCatalogPath);
        string dialogFactoryPath = FindPath("Chummer.Presentation", "Overview", "DesktopDialogFactory.cs");
        string dialogFactoryText = File.ReadAllText(dialogFactoryPath);
        string overviewCommandDispatcherPath = FindPath("Chummer.Presentation", "Overview", "OverviewCommandDispatcher.cs");
        string overviewCommandDispatcherText = File.ReadAllText(overviewCommandDispatcherPath);
        string fileWorkspaceStorePath = FindPath("Chummer.Infrastructure", "Workspaces", "FileWorkspaceStore.cs");
        string fileWorkspaceStoreText = File.ReadAllText(fileWorkspaceStorePath);

        StringAssert.Contains(rulesetContractsText, "public static class RulesetDefaults");
        StringAssert.Contains(rulesetContractsText, "public const string Sr4 = \"sr4\";");
        StringAssert.Contains(rulesetContractsText, "public const string Sr6 = \"sr6\";");
        StringAssert.Contains(rulesetContractsText, "public readonly record struct RulesetId");
        StringAssert.Contains(rulesetContractsText, "public static RulesetId Default => new(string.Empty);");
        StringAssert.Contains(rulesetContractsText, "RulesetDefaults.NormalizeOptional(Value) ?? string.Empty");
        Assert.IsFalse(rulesetContractsText.Contains("public static string Normalize(", StringComparison.Ordinal));
        Assert.IsFalse(rulesetContractsText.Contains("NormalizeOrDefault", StringComparison.Ordinal));
        StringAssert.Contains(rulesetContractsText, "public sealed record WorkspacePayloadEnvelope");
        StringAssert.Contains(rulesetContractsText, "public interface IRulesetPlugin");
        StringAssert.Contains(rulesetContractsText, "public interface IRulesetSerializer");
        StringAssert.Contains(rulesetContractsText, "public interface IRulesetShellDefinitionProvider");
        StringAssert.Contains(rulesetContractsText, "public interface IRulesetCatalogProvider");
        StringAssert.Contains(rulesetContractsText, "IRulesetCapabilityDescriptorProvider CapabilityDescriptors");
        StringAssert.Contains(rulesetContractsText, "IRulesetCapabilityHost Capabilities");
        StringAssert.Contains(rulesetContractsText, "public interface IRulesetRuleHost");
        StringAssert.Contains(rulesetContractsText, "public interface IRulesetScriptHost");
        StringAssert.Contains(capabilityContractsText, "public static class RulesetCapabilityInvocationKinds");
        StringAssert.Contains(capabilityContractsText, "public static class RulesetCapabilityValueKinds");
        StringAssert.Contains(capabilityContractsText, "public sealed record RulesetCapabilityArgument");
        StringAssert.Contains(capabilityContractsText, "public sealed record RulesetCapabilityValue");
        StringAssert.Contains(capabilityContractsText, "public sealed record RulesetCapabilityInvocationRequest");
        StringAssert.Contains(capabilityContractsText, "public sealed record RulesetCapabilityInvocationResult");
        StringAssert.Contains(capabilityContractsText, "public sealed record RulesetCapabilityDescriptor");
        StringAssert.Contains(capabilityContractsText, "public interface IRulesetCapabilityDescriptorProvider");
        StringAssert.Contains(capabilityContractsText, "public interface IRulesetCapabilityHost");
        StringAssert.Contains(capabilityContractsText, "public sealed class RulesetRuleHostCapabilityAdapter");
        StringAssert.Contains(capabilityContractsText, "public sealed class RulesetScriptHostCapabilityAdapter");
        StringAssert.Contains(capabilityContractsText, "public static class RulesetCapabilityBridge");

        string rulesetServicesPath = FindPath("Chummer.Contracts", "Rulesets", "RulesetShellServices.cs");
        string rulesetServicesText = File.ReadAllText(rulesetServicesPath);
        string rulesetHostingServicesPath = FindPath("Chummer.Rulesets.Hosting", "RulesetShellServices.cs");
        string rulesetHostingServicesText = File.ReadAllText(rulesetHostingServicesPath);
        StringAssert.Contains(rulesetServicesText, "public interface IRulesetPluginRegistry");
        StringAssert.Contains(rulesetServicesText, "public interface IRulesetShellCatalogResolver");
        Assert.IsFalse(rulesetServicesText.Contains("public sealed class RulesetPluginRegistry", StringComparison.Ordinal));
        Assert.IsFalse(rulesetServicesText.Contains("public sealed class RulesetShellCatalogResolverService", StringComparison.Ordinal));
        StringAssert.Contains(rulesetHostingServicesText, "public sealed class RulesetPluginRegistry");
        StringAssert.Contains(rulesetHostingServicesText, "public sealed class RulesetShellCatalogResolverService");
        StringAssert.Contains(rulesetHostingServicesText, "IRulesetSelectionPolicy");
        Assert.IsFalse(rulesetHostingServicesText.Contains("AppCommandCatalog.ForRuleset(", StringComparison.Ordinal));
        Assert.IsFalse(rulesetHostingServicesText.Contains("NavigationTabCatalog.ForRuleset(", StringComparison.Ordinal));
        Assert.IsFalse(rulesetHostingServicesText.Contains("WorkspaceSurfaceActionCatalog.ForTab(", StringComparison.Ordinal));
        Assert.IsFalse(rulesetHostingServicesText.Contains("ResolveDesktopUiControlsForTab(", StringComparison.Ordinal));

        string catalogOnlyResolverPath = FindPath("Chummer.Presentation", "Shell", "CatalogOnlyRulesetShellCatalogResolver.cs");
        string catalogOnlyResolverText = File.ReadAllText(catalogOnlyResolverPath);
        StringAssert.Contains(catalogOnlyResolverText, "using Chummer.Rulesets.Hosting.Presentation;");
        StringAssert.Contains(catalogOnlyResolverText, "AppCommandCatalog.ForRuleset(");
        StringAssert.Contains(catalogOnlyResolverText, "NavigationTabCatalog.ForRuleset(");
        StringAssert.Contains(catalogOnlyResolverText, "WorkspaceSurfaceActionCatalog.ForTab(");
        Assert.IsFalse(catalogOnlyResolverText.Contains("ResolveDesktopUiControlsForTab(", StringComparison.Ordinal));

        StringAssert.Contains(commandCatalogText, "namespace Chummer.Rulesets.Hosting.Presentation;");
        StringAssert.Contains(tabCatalogText, "namespace Chummer.Rulesets.Hosting.Presentation;");
        StringAssert.Contains(actionCatalogText, "namespace Chummer.Rulesets.Hosting.Presentation;");
        Assert.IsFalse(commandCatalogText.Contains("namespace Chummer.Contracts.Presentation;", StringComparison.Ordinal));
        Assert.IsFalse(tabCatalogText.Contains("namespace Chummer.Contracts.Presentation;", StringComparison.Ordinal));
        Assert.IsFalse(actionCatalogText.Contains("namespace Chummer.Contracts.Presentation;", StringComparison.Ordinal));
        Assert.IsFalse(workspaceModelsText.Contains("string RulesetId = RulesetDefaults.Sr5", StringComparison.Ordinal));
        Assert.IsFalse(workspaceApiModelsText.Contains("string RulesetId = RulesetDefaults.Sr5", StringComparison.Ordinal));
        Assert.IsFalse(commandDefinitionText.Contains("string RulesetId = RulesetDefaults.Sr5", StringComparison.Ordinal));
        Assert.IsFalse(tabDefinitionText.Contains("string RulesetId = RulesetDefaults.Sr5", StringComparison.Ordinal));
        Assert.IsFalse(actionDefinitionText.Contains("string RulesetId = RulesetDefaults.Sr5", StringComparison.Ordinal));
        Assert.IsFalse(workspaceModelsText.Contains("RulesetDefaults.Normalize(rulesetId)", StringComparison.Ordinal));
        StringAssert.Contains(workspaceModelsText, "NativeXml = 0");
        StringAssert.Contains(workspaceModelsText, "WorkspaceDocumentFormat Format = WorkspaceDocumentFormat.NativeXml");

        StringAssert.Contains(commandCatalogText, "ForRuleset(string? rulesetId)");
        StringAssert.Contains(tabCatalogText, "ForRuleset(string? rulesetId)");
        StringAssert.Contains(actionCatalogText, "ForRuleset(string? rulesetId)");
        StringAssert.Contains(actionCatalogText, "ForTab(string? tabId, string? rulesetId)");
        StringAssert.Contains(commandCatalogText, "RulesetDefaults.Sr5");
        StringAssert.Contains(tabCatalogText, "RulesetDefaults.Sr5");
        StringAssert.Contains(actionCatalogText, "RulesetDefaults.Sr5");
        Assert.IsFalse(commandCatalogText.Contains("RulesetDefaults.Normalize(", StringComparison.Ordinal));
        Assert.IsFalse(tabCatalogText.Contains("RulesetDefaults.Normalize(", StringComparison.Ordinal));
        Assert.IsFalse(actionCatalogText.Contains("RulesetDefaults.Normalize(", StringComparison.Ordinal));
        Assert.IsFalse(dialogFactoryText.Contains("RulesetDefaults.Sr5", StringComparison.Ordinal));
        Assert.IsFalse(overviewCommandDispatcherText.Contains("RulesetDefaults.Sr5", StringComparison.Ordinal));
        string workspaceSessionManagerPath = FindPath("Chummer.Presentation", "Overview", "WorkspaceSessionManager.cs");
        string workspaceSessionManagerText = File.ReadAllText(workspaceSessionManagerPath);
        string presenterCommandsPath = FindPath("Chummer.Presentation", "Overview", "CharacterOverviewPresenter.Commands.cs");
        string presenterCommandsText = File.ReadAllText(presenterCommandsPath);
        string shellPreferencesServicePath = FindPath("Chummer.Application", "Tools", "ShellPreferencesService.cs");
        string shellPreferencesServiceText = File.ReadAllText(shellPreferencesServicePath);
        string httpChummerClientPath = FindPath("Chummer.Presentation", "HttpChummerClient.cs");
        string httpChummerClientText = File.ReadAllText(httpChummerClientPath);
        string presenterDialogsPath = FindPath("Chummer.Presentation", "Overview", "CharacterOverviewPresenter.Dialogs.cs");
        string presenterDialogsText = File.ReadAllText(presenterDialogsPath);
        Assert.IsFalse(workspaceSessionManagerText.Contains("RulesetDefaults.Sr5", StringComparison.Ordinal));
        Assert.IsFalse(presenterCommandsText.Contains("RulesetDefaults.Sr5", StringComparison.Ordinal));
        Assert.IsFalse(presenterCommandsText.Contains("RulesetShellCatalogResolver.", StringComparison.Ordinal));
        Assert.IsFalse(shellPreferencesServiceText.Contains("RulesetDefaults.Normalize(", StringComparison.Ordinal));
        Assert.IsFalse(httpChummerClientText.Contains("RulesetDefaults.Normalize(", StringComparison.Ordinal));
        Assert.IsFalse(workspaceSessionManagerText.Contains("RulesetDefaults.Normalize(", StringComparison.Ordinal));
        Assert.IsFalse(presenterDialogsText.Contains("RulesetDefaults.Normalize(", StringComparison.Ordinal));
        Assert.IsFalse(overviewCommandDispatcherText.Contains("RulesetDefaults.Normalize(", StringComparison.Ordinal));
        StringAssert.Contains(presenterCommandsText, "_shellCatalogResolver.ResolveNavigationTabs(");
        StringAssert.Contains(presenterCommandsText, "_shellCatalogResolver.ResolveWorkspaceActionsForTab(");
        string shellStatePath = FindPath("Chummer.Presentation", "Shell", "ShellState.cs");
        string shellStateText = File.ReadAllText(shellStatePath);
        string shellContractsPath = FindPresentationContractsPath("ShellBootstrapContracts.cs");
        string shellContractsText = File.ReadAllText(shellContractsPath);
        Assert.IsFalse(shellStateText.Contains("RulesetDefaults.Sr5", StringComparison.Ordinal));
        Assert.IsFalse(shellContractsText.Contains("RulesetDefaults.Sr5", StringComparison.Ordinal));
        StringAssert.Contains(fileWorkspaceStoreText, "WorkspacePayloadEnvelope");
        StringAssert.Contains(fileWorkspaceStoreText, "PayloadKind");
        StringAssert.Contains(fileWorkspaceStoreText, "Envelope");
    }

    [TestMethod]
    public void Canonical_engine_contract_project_owns_shared_shell_contract_sources()
    {
        string coreContractsProjectPath = FindPath("Chummer.Contracts", "Chummer.Contracts.csproj");
        string coreContractsProjectText = File.ReadAllText(coreContractsProjectPath);

        StringAssert.Contains(coreContractsProjectText, "<AssemblyName>Chummer.Engine.Contracts</AssemblyName>");
        StringAssert.Contains(coreContractsProjectText, "<PackageId>Chummer.Engine.Contracts</PackageId>");
        Assert.IsFalse(coreContractsProjectText.Contains(@"..\Chummer.Presentation.Contracts\Presentation\", StringComparison.Ordinal));
        Assert.IsFalse(coreContractsProjectText.Contains(@"../Chummer.Presentation.Contracts/Presentation/", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Workspace_service_routes_behavior_through_ruleset_codec_seam()
    {
        string workspaceServicePath = FindPath("Chummer.Application", "Workspaces", "WorkspaceService.cs");
        string workspaceServiceText = File.ReadAllText(workspaceServicePath);
        string workspaceModelsPath = FindPath("Chummer.Contracts", "Workspaces", "CharacterWorkspaceModels.cs");
        string workspaceModelsText = File.ReadAllText(workspaceModelsPath);
        string workspaceStorePath = FindPath("Chummer.Infrastructure", "Workspaces", "FileWorkspaceStore.cs");
        string workspaceStoreText = File.ReadAllText(workspaceStorePath);
        string codecContractPath = FindPath("Chummer.Application", "Workspaces", "IRulesetWorkspaceCodec.cs");
        string codecContractText = File.ReadAllText(codecContractPath);
        string codecResolverContractPath = FindPath("Chummer.Application", "Workspaces", "IRulesetWorkspaceCodecResolver.cs");
        string codecResolverContractText = File.ReadAllText(codecResolverContractPath);
        string importDetectorContractPath = FindPath("Chummer.Application", "Workspaces", "IWorkspaceImportRulesetDetector.cs");
        string importDetectorContractText = File.ReadAllText(importDetectorContractPath);
        string importDetectorPath = FindPath("Chummer.Application", "Workspaces", "WorkspaceImportRulesetDetector.cs");
        string importDetectorText = File.ReadAllText(importDetectorPath);
        string rulesetDetectionPath = FindPath("Chummer.Application", "Workspaces", "WorkspaceRulesetDetection.cs");
        string rulesetDetectionText = File.ReadAllText(rulesetDetectionPath);
        string codecResolverPath = FindPath("Chummer.Rulesets.Hosting", "RulesetWorkspaceCodecResolver.cs");
        string codecResolverText = File.ReadAllText(codecResolverPath);
        string sr5CodecPath = FindPath("Chummer.Rulesets.Sr5", "Sr5WorkspaceCodec.cs");
        string sr5CodecText = File.ReadAllText(sr5CodecPath);
        string sr4CodecPath = FindPath("Chummer.Rulesets.Sr4", "Sr4WorkspaceCodec.cs");
        string sr4CodecText = File.ReadAllText(sr4CodecPath);
        string sr6CodecPath = FindPath("Chummer.Rulesets.Sr6", "Sr6WorkspaceCodec.cs");
        string sr6CodecText = File.ReadAllText(sr6CodecPath);
        string infrastructureDiPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string infrastructureDiText = File.ReadAllText(infrastructureDiPath);
        string sr4RulesetDiPath = FindPath("Chummer.Rulesets.Sr4", "ServiceCollectionRulesetExtensions.cs");
        string sr4RulesetDiText = File.ReadAllText(sr4RulesetDiPath);
        string sr5RulesetDiPath = FindPath("Chummer.Rulesets.Sr5", "ServiceCollectionRulesetExtensions.cs");
        string sr5RulesetDiText = File.ReadAllText(sr5RulesetDiPath);
        string sr6RulesetDiPath = FindPath("Chummer.Rulesets.Sr6", "ServiceCollectionRulesetExtensions.cs");
        string sr6RulesetDiText = File.ReadAllText(sr6RulesetDiPath);

        StringAssert.Contains(codecContractText, "public interface IRulesetWorkspaceCodec");
        StringAssert.Contains(codecContractText, "WrapImport");
        StringAssert.Contains(codecContractText, "int SchemaVersion { get; }");
        StringAssert.Contains(codecContractText, "ParseSummary");
        StringAssert.Contains(codecContractText, "ParseSection");
        StringAssert.Contains(codecContractText, "Validate");
        StringAssert.Contains(codecContractText, "UpdateMetadata");
        StringAssert.Contains(codecContractText, "WorkspaceDownloadReceipt BuildDownload");
        StringAssert.Contains(codecContractText, "DataExportBundle BuildExportBundle");
        StringAssert.Contains(codecResolverContractText, "public interface IRulesetWorkspaceCodecResolver");
        StringAssert.Contains(importDetectorContractText, "public interface IWorkspaceImportRulesetDetector");
        StringAssert.Contains(importDetectorText, "WorkspaceRulesetDetection.Detect(");
        StringAssert.Contains(rulesetDetectionText, "public static class WorkspaceRulesetDetection");
        StringAssert.Contains(rulesetDetectionText, "RulesetDefaults.Sr4");
        StringAssert.Contains(rulesetDetectionText, "RulesetDefaults.Sr5");
        StringAssert.Contains(rulesetDetectionText, "RulesetDefaults.Sr6");
        StringAssert.Contains(codecResolverText, "public sealed class RulesetWorkspaceCodecResolver");
        StringAssert.Contains(codecResolverText, "namespace Chummer.Rulesets.Hosting;");
        Assert.IsFalse(codecResolverText.Contains("namespace Chummer.Application.Workspaces;", StringComparison.Ordinal));
        StringAssert.Contains(codecResolverText, "RulesetDefaults.NormalizeOptional(rulesetId)");
        StringAssert.Contains(codecResolverText, "RulesetDefaults.NormalizeRequired(codec.RulesetId)");
        Assert.IsFalse(codecResolverText.Contains("RulesetDefaults.Sr5", StringComparison.Ordinal));
        Assert.IsFalse(codecResolverText.Contains("_fallbackCodec", StringComparison.Ordinal));
        Assert.IsFalse(codecResolverText.Contains("return _fallbackCodec", StringComparison.Ordinal));
        StringAssert.Contains(codecResolverText, "Workspace ruleset id is required to resolve a workspace codec.");
        StringAssert.Contains(codecResolverText, "No workspace codec is registered for ruleset");
        StringAssert.Contains(workspaceServiceText, "IWorkspaceImportRulesetDetector");
        StringAssert.Contains(workspaceServiceText, "_workspaceImportRulesetDetector.Detect(document)");
        StringAssert.Contains(workspaceServiceText, "Workspace ruleset is required or must be detectable from import content.");
        StringAssert.Contains(workspaceModelsText, "public sealed record WorkspaceDocumentState");
        StringAssert.Contains(workspaceModelsText, "WorkspaceDocumentState State");
        StringAssert.Contains(workspaceModelsText, "public WorkspacePayloadEnvelope PayloadEnvelope => State.ToEnvelope();");
        StringAssert.Contains(workspaceModelsText, "public string Content => State.Payload;");
        StringAssert.Contains(workspaceStoreText, "WorkspaceDocumentState state = ResolveState(record, content, rulesetId);");
        StringAssert.Contains(workspaceStoreText, "Envelope = NormalizeEnvelope(document.State)");
        StringAssert.Contains(workspaceStoreText, "WorkspaceRulesetDetection.Detect(");
        Assert.IsFalse(workspaceStoreText.Contains("private static string? DetectRulesetId(", StringComparison.Ordinal));
        StringAssert.Contains(infrastructureDiText, "services.AddSingleton<IWorkspaceImportRulesetDetector, WorkspaceImportRulesetDetector>();");

        StringAssert.Contains(workspaceServiceText, "IRulesetWorkspaceCodecResolver _workspaceCodecResolver");
        StringAssert.Contains(workspaceServiceText, "_workspaceCodecResolver.Resolve");
        StringAssert.Contains(workspaceServiceText, "codec.SchemaVersion");
        StringAssert.Contains(workspaceServiceText, "codec.PayloadKind");
        StringAssert.Contains(workspaceServiceText, "codec.BuildDownload(id, envelope, document.Format)");
        StringAssert.Contains(workspaceServiceText, "codec.BuildExportBundle(envelope)");
        Assert.IsFalse(workspaceServiceText.Contains("_characterFileQueries.ParseSummary", StringComparison.Ordinal));
        Assert.IsFalse(workspaceServiceText.Contains("_characterSectionQueries.ParseSection", StringComparison.Ordinal));
        Assert.IsFalse(workspaceServiceText.Contains("_characterMetadataCommands.UpdateMetadata", StringComparison.Ordinal));
        Assert.IsFalse(workspaceServiceText.Contains("TryParseExportSection", StringComparison.Ordinal));
        Assert.IsFalse(workspaceServiceText.Contains("DefaultEnvelopeSchemaVersion", StringComparison.Ordinal));
        Assert.IsFalse(workspaceServiceText.Contains("DefaultEnvelopePayloadKind", StringComparison.Ordinal));
        Assert.IsFalse(workspaceServiceText.Contains("WorkspaceDocumentFormat.NativeXml => \".chum5\"", StringComparison.Ordinal));

        StringAssert.Contains(sr5CodecText, "public sealed class Sr5WorkspaceCodec");
        StringAssert.Contains(sr5CodecText, "public const string Sr5PayloadKind = \"sr5/chum5-xml\"");
        StringAssert.Contains(sr4CodecText, "RulesetDefaults.NormalizeRequired(rulesetId)");
        StringAssert.Contains(sr5CodecText, "RulesetDefaults.NormalizeRequired(rulesetId)");
        StringAssert.Contains(sr6CodecText, "RulesetDefaults.NormalizeRequired(rulesetId)");
        StringAssert.Contains(sr4CodecText, "RulesetDefaults.NormalizeOptional(envelope.RulesetId) ?? RulesetDefaults.Sr4");
        StringAssert.Contains(sr5CodecText, "RulesetDefaults.NormalizeOptional(envelope.RulesetId) ?? RulesetDefaults.Sr5");
        StringAssert.Contains(sr6CodecText, "RulesetDefaults.NormalizeOptional(envelope.RulesetId) ?? RulesetDefaults.Sr6");
        Assert.IsFalse(sr4CodecText.Contains("RulesetDefaults.NormalizeOrDefault(", StringComparison.Ordinal));
        Assert.IsFalse(sr5CodecText.Contains("RulesetDefaults.NormalizeOrDefault(", StringComparison.Ordinal));
        Assert.IsFalse(sr6CodecText.Contains("RulesetDefaults.NormalizeOrDefault(", StringComparison.Ordinal));
        StringAssert.Contains(sr5CodecText, "UpdateMetadata");
        StringAssert.Contains(sr5CodecText, "WorkspaceDownloadReceipt BuildDownload");
        StringAssert.Contains(sr5CodecText, "DataExportBundle BuildExportBundle");
        StringAssert.Contains(sr4CodecText, "public sealed class Sr4WorkspaceCodec");
        StringAssert.Contains(sr4CodecText, "public const string Sr4PayloadKind = \"sr4/chum4-xml\"");
        StringAssert.Contains(sr4CodecText, "WorkspaceDownloadReceipt BuildDownload");
        StringAssert.Contains(sr4CodecText, "DataExportBundle BuildExportBundle");
        StringAssert.Contains(sr6CodecText, "public sealed class Sr6WorkspaceCodec");
        StringAssert.Contains(sr6CodecText, "public const string Sr6PayloadKind = \"sr6/chum6-xml\"");
        StringAssert.Contains(sr6CodecText, "WorkspaceDownloadReceipt BuildDownload");
        StringAssert.Contains(sr6CodecText, "DataExportBundle BuildExportBundle");

        Assert.IsFalse(infrastructureDiText.Contains("AddSingleton<IRulesetWorkspaceCodecResolver, RulesetWorkspaceCodecResolver>();", StringComparison.Ordinal));
        StringAssert.Contains(sr4RulesetDiText, "TryAddEnumerable(ServiceDescriptor.Singleton<IRulesetWorkspaceCodec, Sr4WorkspaceCodec>());");
        StringAssert.Contains(sr5RulesetDiText, "TryAddEnumerable(ServiceDescriptor.Singleton<IRulesetWorkspaceCodec, Sr5WorkspaceCodec>());");
        StringAssert.Contains(sr6RulesetDiText, "TryAddEnumerable(ServiceDescriptor.Singleton<IRulesetWorkspaceCodec, Sr6WorkspaceCodec>());");
    }

    [TestMethod]
    public void Architecture_guardrails_treat_portal_as_ui_head()
    {
        string guardrailPath = FindPath("Chummer.Tests", "Compliance", "ArchitectureGuardrailTests.cs");
        string guardrailText = File.ReadAllText(guardrailPath);

        StringAssert.Contains(guardrailText, "private static readonly string[] UiHeadProjects");
        StringAssert.Contains(guardrailText, "\"Chummer.Portal\"");
        StringAssert.Contains(guardrailText, "Ui_head_projects_do_not_import_non_presentation_layers");
    }

    [TestMethod]
    public void Runbook_supports_download_manifest_generation_mode()
    {
        string runbookPath = FindPath("scripts", "runbook.sh");
        string runbookText = File.ReadAllText(runbookPath);
        string generatorPath = FindPath("scripts", "generate-releases-manifest.sh");
        string generatorText = File.ReadAllText(generatorPath);
        string publisherPath = FindPath("scripts", "publish-download-bundle.sh");
        string publisherText = File.ReadAllText(publisherPath);
        string s3PublisherPath = FindPath("scripts", "publish-download-bundle-s3.sh");
        string s3PublisherText = File.ReadAllText(s3PublisherPath);
        string hostPrereqPath = FindPath("scripts", "check-host-gate-prereqs.sh");
        string hostPrereqText = File.ReadAllText(hostPrereqPath);
        string strictHostGatesPath = FindPath("scripts", "runbook-strict-host-gates.sh");
        string strictHostGatesText = File.ReadAllText(strictHostGatesPath);
        string amendValidatorPath = FindPath("scripts", "validate-amend-manifests.sh");
        string amendValidatorText = File.ReadAllText(amendValidatorPath);
        string parityGeneratorPath = FindPath("scripts", "generate-parity-checklist.sh");
        string parityGeneratorText = File.ReadAllText(parityGeneratorPath);
        string parityChecklistPath = FindPath("docs", "PARITY_CHECKLIST.md");
        string parityChecklistText = File.ReadAllText(parityChecklistPath);

        StringAssert.Contains(runbookText, "RUNBOOK_MODE\" == \"downloads-manifest\"");
        StringAssert.Contains(runbookText, "RUNBOOK_MODE\" == \"host-prereqs\"");
        StringAssert.Contains(runbookText, "RUNBOOK_MODE\" == \"downloads-sync\"");
        StringAssert.Contains(runbookText, "RUNBOOK_MODE\" == \"downloads-sync-s3\"");
        StringAssert.Contains(runbookText, "RUNBOOK_MODE\" == \"downloads-verify\"");
        StringAssert.Contains(runbookText, "RUNBOOK_MODE\" == \"downloads-smoke\"");
        StringAssert.Contains(runbookText, "RUNBOOK_MODE\" == \"parity-checklist\"");
        StringAssert.Contains(runbookText, "RUNBOOK_MODE\" == \"amend-checksums\"");
        StringAssert.Contains(runbookText, "docs/SELF_HOSTED_DOWNLOADS_RUNBOOK.md");
        StringAssert.Contains(runbookText, "bash scripts/generate-releases-manifest.sh");
        StringAssert.Contains(runbookText, "bash scripts/generate-parity-checklist.sh");
        StringAssert.Contains(runbookText, "bash scripts/publish-download-bundle.sh");
        StringAssert.Contains(runbookText, "bash scripts/publish-download-bundle-s3.sh");
        StringAssert.Contains(runbookText, "bash scripts/verify-releases-manifest.sh");
        StringAssert.Contains(runbookText, "bash scripts/validate-amend-manifests.sh");
        StringAssert.Contains(runbookText, "permission denied while trying to connect to the Docker daemon socket");
        StringAssert.Contains(runbookText, "DOWNLOADS_SYNC_DEPLOY_MODE");
        StringAssert.Contains(runbookText, "DOWNLOADS_SYNC_VERIFY_LINKS");
        StringAssert.Contains(runbookText, "DOWNLOADS_SYNC_S3_VERIFY_LINKS");
        StringAssert.Contains(runbookText, "DOWNLOADS_VERIFY_LINKS");
        StringAssert.Contains(runbookText, "CHUMMER_PORTAL_DOWNLOADS_DEPLOY_ENABLED=true");
        StringAssert.Contains(runbookText, "DOCKER_TESTS_BUILD");
        StringAssert.Contains(runbookText, "DOCKER_TESTS_SOFT_FAIL");
        StringAssert.Contains(runbookText, "DOCKER_TESTS_PREFLIGHT_LOG");
        StringAssert.Contains(runbookText, "COMPOSE_FILE=\"$REPO_ROOT/docker-compose.yml\"");
        StringAssert.Contains(runbookText, "CHUMMER_RUNBOOK_INCLUDE_LOCAL_COMPOSE_OVERRIDE");
        StringAssert.Contains(runbookText, "resolve_runbook_log_file");
        StringAssert.Contains(runbookText, "resolve_runbook_dir");
        StringAssert.Contains(runbookText, "resolve_runbook_log_file migration-loop-runbook");
        StringAssert.Contains(runbookText, "resolve_runbook_log_file chummer-local-tests");
        StringAssert.Contains(runbookText, "resolve_runbook_log_file chummer-desktop-build");
        StringAssert.Contains(runbookText, "resolve_runbook_log_file chummer-amend-checksums");
        StringAssert.Contains(runbookText, "resolve_runbook_log_file chummer-parity-checklist");
        StringAssert.Contains(runbookText, "resolve_runbook_log_file chummer-downloads-manifest");
        StringAssert.Contains(runbookText, "resolve_runbook_log_file chummer-downloads-sync");
        StringAssert.Contains(runbookText, "resolve_runbook_log_file chummer-downloads-sync-s3");
        StringAssert.Contains(runbookText, "resolve_runbook_log_file chummer-downloads-verify");
        StringAssert.Contains(runbookText, "resolve_runbook_log_file chummer-downloads-smoke");
        StringAssert.Contains(runbookText, "resolve_runbook_log_file chummer-ui-e2e");
        StringAssert.Contains(runbookText, "resolve_runbook_log_file chummer-portal-e2e");
        StringAssert.Contains(runbookText, "resolve_runbook_log_file chummer-docker-tests");
        StringAssert.Contains(runbookText, "resolve_runbook_log_file chummer-docker-tests-preflight");
        StringAssert.Contains(runbookText, "downloads-smoke sync_status=");
        StringAssert.Contains(runbookText, "docker ps >\"$DOCKER_TESTS_PREFLIGHT_LOG\" 2>&1");
        StringAssert.Contains(runbookText, "permission denied while trying to connect to the docker API");
        StringAssert.Contains(runbookText, "skipping docker-tests due docker daemon permissions");
        StringAssert.Contains(runbookText, "docker compose run $build_arg --rm chummer-tests");
        StringAssert.Contains(runbookText, "TEST_DISABLE_BUILD_SERVERS");
        StringAssert.Contains(runbookText, "TEST_NO_RESTORE");
        StringAssert.Contains(runbookText, "TEST_NUGET_SOFT_FAIL");
        StringAssert.Contains(runbookText, "DOTNET_CLI_HOME");
        StringAssert.Contains(runbookText, "resolve_runbook_dir dotnet-cli-home");
        StringAssert.Contains(runbookText, "DOTNET_NOLOGO");
        StringAssert.Contains(runbookText, "DOTNET_CLI_TELEMETRY_OPTOUT");
        StringAssert.Contains(runbookText, "AVALONIA_TELEMETRY_OPTOUT");
        StringAssert.Contains(runbookText, "DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER");
        StringAssert.Contains(runbookText, "--disable-build-servers");
        StringAssert.Contains(runbookText, "MSBUILDDISABLENODEREUSE");
        StringAssert.Contains(runbookText, "TEST_NUGET_PREFLIGHT");
        StringAssert.Contains(runbookText, "TEST_NUGET_ENDPOINT");
        StringAssert.Contains(runbookText, "skipping local-tests due NuGet preflight failure");
        StringAssert.Contains(runbookText, "NuGet preflight failed");
        Assert.IsFalse(
            runbookText.Contains("DOTNET_CLI_HOME:-/tmp", StringComparison.Ordinal),
            "local-tests should not hardcode DOTNET_CLI_HOME to /tmp.");
        Assert.IsFalse(
            runbookText.Contains("/tmp/chummer-local-tests.log", StringComparison.Ordinal),
            "local-tests should resolve logs through writable-path detection.");
        Assert.IsFalse(
            runbookText.Contains("/tmp/chummer-desktop-build.log", StringComparison.Ordinal),
            "desktop-build should resolve logs through writable-path detection.");
        Assert.IsFalse(
            runbookText.Contains("/tmp/chummer-amend-checksums.log", StringComparison.Ordinal),
            "amend-checksums should resolve logs through writable-path detection.");
        Assert.IsFalse(
            runbookText.Contains("/tmp/chummer-parity-checklist.log", StringComparison.Ordinal),
            "parity-checklist should resolve logs through writable-path detection.");
        Assert.IsFalse(
            runbookText.Contains("/tmp/chummer-downloads-sync.log", StringComparison.Ordinal),
            "downloads-sync should resolve logs through writable-path detection.");
        Assert.IsFalse(
            runbookText.Contains("/tmp/chummer-downloads-sync-s3.log", StringComparison.Ordinal),
            "downloads-sync-s3 should resolve logs through writable-path detection.");
        Assert.IsFalse(
            runbookText.Contains("/tmp/chummer-downloads-verify.log", StringComparison.Ordinal),
            "downloads-verify should resolve logs through writable-path detection.");
        Assert.IsFalse(
            runbookText.Contains("/tmp/chummer-ui-e2e.log", StringComparison.Ordinal),
            "ui-e2e should resolve logs through writable-path detection.");
        Assert.IsFalse(
            runbookText.Contains("/tmp/chummer-portal-e2e.log", StringComparison.Ordinal),
            "portal-e2e should resolve logs through writable-path detection.");
        Assert.IsFalse(
            runbookText.Contains("/tmp/chummer-docker-tests.log", StringComparison.Ordinal),
            "docker-tests should resolve logs through writable-path detection.");
        Assert.IsFalse(
            runbookText.Contains("/tmp/chummer-docker-tests-preflight.log", StringComparison.Ordinal),
            "docker-tests preflight should resolve logs through writable-path detection.");
        StringAssert.Contains(strictHostGatesText, "RUNBOOK_MODE=local-tests");
        StringAssert.Contains(strictHostGatesText, "RUNBOOK_MODE=docker-tests");
        StringAssert.Contains(strictHostGatesText, "TEST_NUGET_SOFT_FAIL=0");
        StringAssert.Contains(strictHostGatesText, "DOCKER_TESTS_SOFT_FAIL=0");
        StringAssert.Contains(strictHostGatesText, "check-host-gate-prereqs.sh");
        StringAssert.Contains(strictHostGatesText, "strict host prerequisite gate");
        StringAssert.Contains(strictHostGatesText, "Strict host gates completed successfully.");
        StringAssert.Contains(hostPrereqText, "strict host gate prerequisites");
        StringAssert.Contains(hostPrereqText, "resolve_log_file");
        StringAssert.Contains(hostPrereqText, "PREREQ_LOG_DIR");
        StringAssert.Contains(hostPrereqText, "[PASS]");
        StringAssert.Contains(hostPrereqText, "[FAIL]");
        StringAssert.Contains(hostPrereqText, "Strict host gates are");

        StringAssert.Contains(generatorText, "Docker/Downloads/releases.json");
        StringAssert.Contains(generatorText, "Chummer.Portal/downloads/releases.json");
        StringAssert.Contains(generatorText, "PORTAL_DOWNLOADS_DIR");
        StringAssert.Contains(generatorText, "synced ${#portal_artifacts[@]} local portal artifact(s)");
        StringAssert.Contains(generatorText, "/downloads/files/");

        StringAssert.Contains(publisherText, "Expected desktop-download-bundle layout");
        StringAssert.Contains(publisherText, "generate-releases-manifest.sh");
        StringAssert.Contains(publisherText, "verify-releases-manifest.sh");
        StringAssert.Contains(publisherText, "PORTAL_DOWNLOADS_DIR");
        StringAssert.Contains(publisherText, "CHUMMER_PORTAL_DOWNLOADS_DEPLOY_ENABLED");
        StringAssert.Contains(publisherText, "Deployment mode requires CHUMMER_PORTAL_DOWNLOADS_VERIFY_URL");
        StringAssert.Contains(publisherText, "CHUMMER_PORTAL_DOWNLOADS_VERIFY_LINKS");
        StringAssert.Contains(publisherText, "Published ${#artifacts[@]} desktop artifact(s)");
        StringAssert.Contains(s3PublisherText, "CHUMMER_PORTAL_DOWNLOADS_S3_URI");
        StringAssert.Contains(s3PublisherText, "CHUMMER_PORTAL_DOWNLOADS_VERIFY_URL");
        StringAssert.Contains(s3PublisherText, "CHUMMER_PORTAL_DOWNLOADS_VERIFY_LINKS");
        StringAssert.Contains(s3PublisherText, "aws s3 cp");
        StringAssert.Contains(s3PublisherText, "verify-releases-manifest.sh");
        StringAssert.Contains(s3PublisherText, "Published ${artifact_count} desktop artifact(s) to object storage target");

        string verifierPath = FindPath("scripts", "verify-releases-manifest.sh");
        string verifierText = File.ReadAllText(verifierPath);
        StringAssert.Contains(verifierText, "CHUMMER_PORTAL_DOWNLOADS_VERIFY_URL");
        StringAssert.Contains(verifierText, "CHUMMER_PORTAL_DOWNLOADS_VERIFY_LINKS");
        StringAssert.Contains(verifierText, "/downloads/releases.json");
        StringAssert.Contains(verifierText, "failed artifact verification");
        StringAssert.Contains(verifierText, "Verified artifact links/files");
        StringAssert.Contains(verifierText, "has no downloads");

        StringAssert.Contains(amendValidatorText, "checksums map is required");
        StringAssert.Contains(amendValidatorText, "missing checksum entry");
        StringAssert.Contains(amendValidatorText, "data");
        StringAssert.Contains(amendValidatorText, "lang");
        StringAssert.Contains(parityGeneratorText, "PARITY_ORACLE.json");
        StringAssert.Contains(parityGeneratorText, "Workspace Actions coverage compares parity-oracle action IDs to action `TargetId` values.");
        StringAssert.Contains(parityGeneratorText, "Wrote parity checklist");
        StringAssert.Contains(parityChecklistText, "# UI Parity Checklist");
        StringAssert.Contains(parityChecklistText, "Parity oracle source");
        StringAssert.Contains(parityChecklistText, "| Workspace Actions |");
    }

    [TestMethod]
    public void Amend_manifest_checksum_policy_is_enforced_in_ci()
    {
        string desktopWorkflowPath = FindPath(".github", "workflows", "desktop-downloads-matrix.yml");
        string desktopWorkflowText = File.ReadAllText(desktopWorkflowPath);
        string guardrailsWorkflowPath = FindPath(".github", "workflows", "docker-architecture-guardrails.yml");
        string guardrailsWorkflowText = File.ReadAllText(guardrailsWorkflowPath);
        string manifestPath = FindPath("Docker", "Amends", "manifest.json");
        string manifestText = File.ReadAllText(manifestPath);

        StringAssert.Contains(desktopWorkflowText, "scripts/validate-amend-manifests.sh");
        StringAssert.Contains(desktopWorkflowText, "Validate amend manifests checksums");
        StringAssert.Contains(guardrailsWorkflowText, "amend-manifest-checksums");
        StringAssert.Contains(guardrailsWorkflowText, "bash scripts/validate-amend-manifests.sh");

        StringAssert.Contains(manifestText, "\"checksums\"");
        StringAssert.Contains(manifestText, "\"data/qualities.test-amend.xml\"");
        StringAssert.Contains(manifestText, "\"lang/en-us.test-amend.xml\"");
    }

    [TestMethod]
    public void Docker_architecture_guardrails_workflow_validates_compose_and_portal_formatting()
    {
        string guardrailsWorkflowPath = FindPath(".github", "workflows", "docker-architecture-guardrails.yml");
        string guardrailsWorkflowText = File.ReadAllText(guardrailsWorkflowPath);

        StringAssert.Contains(guardrailsWorkflowText, "compose-config-validation");
        StringAssert.Contains(guardrailsWorkflowText, "docker compose config > /tmp/chummer-compose-config.out");
        StringAssert.Contains(guardrailsWorkflowText, "portal-format-guardrail");
        StringAssert.Contains(guardrailsWorkflowText, "dotnet restore Chummer.Portal/Chummer.Portal.csproj");
        StringAssert.Contains(guardrailsWorkflowText, "dotnet format style Chummer.Portal/Chummer.Portal.csproj --verify-no-changes --no-restore --include Chummer.Portal/Program.cs");
        StringAssert.Contains(guardrailsWorkflowText, "parity-checklist-sync");
        StringAssert.Contains(guardrailsWorkflowText, "RUNBOOK_MODE=parity-checklist bash scripts/runbook.sh");
        StringAssert.Contains(guardrailsWorkflowText, "downloads-smoke-runbook");
        StringAssert.Contains(guardrailsWorkflowText, "RUNBOOK_MODE=downloads-smoke bash scripts/runbook.sh");
        StringAssert.Contains(guardrailsWorkflowText, "fresh-state-local-runbook");
        StringAssert.Contains(guardrailsWorkflowText, "RUNBOOK_LOG_DIR=\"$PWD/.tmp/runbook-logs\"");
        StringAssert.Contains(guardrailsWorkflowText, "RUNBOOK_STATE_DIR=\"$PWD/.tmp/runbook-state\"");
        StringAssert.Contains(guardrailsWorkflowText, "TEST_NUGET_SOFT_FAIL=0");
        StringAssert.Contains(guardrailsWorkflowText, "bash scripts/runbook.sh local-tests net10.0 \"FullyQualifiedName~MigrationComplianceTests\"");
        StringAssert.Contains(guardrailsWorkflowText, "git diff --exit-code -- docs/PARITY_CHECKLIST.md");
    }

    [TestMethod]
    public void Dockerfile_tests_excludes_archived_chummerhub_project_from_container_build_checks()
    {
        string dockerfilePath = FindPath("Docker", "Dockerfile.tests");
        string dockerfileText = File.ReadAllText(dockerfilePath);

        StringAssert.Contains(dockerfileText, "COPY Chummer.Desktop.Runtime/Chummer.Desktop.Runtime.csproj Chummer.Desktop.Runtime/");
        StringAssert.Contains(dockerfileText, "COPY Chummer.Desktop.Runtime/ Chummer.Desktop.Runtime/");
        StringAssert.Contains(dockerfileText, "COPY Chummer.Blazor.Desktop/Chummer.Blazor.Desktop.csproj Chummer.Blazor.Desktop/");
        StringAssert.Contains(dockerfileText, "COPY Chummer.Blazor.Desktop/ Chummer.Blazor.Desktop/");
        StringAssert.Contains(dockerfileText, "COPY Chummer.Infrastructure.Browser/Chummer.Infrastructure.Browser.csproj Chummer.Infrastructure.Browser/");
        StringAssert.Contains(dockerfileText, "COPY Chummer.Infrastructure.Browser/ Chummer.Infrastructure.Browser/");
        StringAssert.Contains(dockerfileText, "COPY Chummer.Rulesets.Sr4/Chummer.Rulesets.Sr4.csproj Chummer.Rulesets.Sr4/");
        StringAssert.Contains(dockerfileText, "COPY Chummer.Rulesets.Sr4/ Chummer.Rulesets.Sr4/");
        StringAssert.Contains(dockerfileText, "COPY Chummer.Rulesets.Sr6/Chummer.Rulesets.Sr6.csproj Chummer.Rulesets.Sr6/");
        StringAssert.Contains(dockerfileText, "COPY Chummer.Rulesets.Sr6/ Chummer.Rulesets.Sr6/");
        StringAssert.Contains(dockerfileText, "COPY Chummer.Hub.Web/Chummer.Hub.Web.csproj Chummer.Hub.Web/");
        StringAssert.Contains(dockerfileText, "COPY Chummer.Hub.Web/ Chummer.Hub.Web/");
        StringAssert.Contains(dockerfileText, "COPY Chummer.Session.Web/Chummer.Session.Web.csproj Chummer.Session.Web/");
        StringAssert.Contains(dockerfileText, "COPY Chummer.Session.Web/ Chummer.Session.Web/");
        StringAssert.Contains(dockerfileText, "COPY Chummer.Coach.Web/Chummer.Coach.Web.csproj Chummer.Coach.Web/");
        StringAssert.Contains(dockerfileText, "COPY Chummer.Coach.Web/ Chummer.Coach.Web/");
        StringAssert.Contains(dockerfileText, "COPY README.md ./");
        StringAssert.Contains(dockerfileText, "COPY docs/ docs/");
        Assert.IsFalse(dockerfileText.Contains("COPY ChummerHub/ ChummerHub/", StringComparison.Ordinal));
        StringAssert.Contains(dockerfileText, "COPY .github/PULL_REQUEST_TEMPLATE.md .github/");
        StringAssert.Contains(dockerfileText, "COPY Docker/Amends/ Docker/Amends/");
        StringAssert.Contains(dockerfileText, "COPY Docker/Downloads/ Docker/Downloads/");
    }

    [TestMethod]
    public void Repo_guidance_marks_legacy_heads_as_oracle_only()
    {
        string solutionPath = FindPath("Chummer.sln");
        string solutionText = File.ReadAllText(solutionPath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);
        string dockerfilePath = FindPath("Docker", "Dockerfile.tests");
        string dockerfileText = File.ReadAllText(dockerfilePath);
        string backlogPath = FindPath("docs", "MIGRATION_BACKLOG.md");
        string backlogText = File.ReadAllText(backlogPath);
        string prTemplatePath = FindPath(".github", "PULL_REQUEST_TEMPLATE.md");
        string prTemplateText = File.ReadAllText(prTemplatePath);

        Assert.IsFalse(solutionText.Contains(@"ChummerHub\ChummerHub.csproj", StringComparison.Ordinal));
        Assert.IsFalse(solutionText.Contains(@"Plugins\ChummerHub.Client\ChummerHub.Client.csproj", StringComparison.Ordinal));
        Assert.IsFalse(solutionText.Contains("Debug with ChummerHub", StringComparison.Ordinal));
        Assert.IsFalse(solutionText.Contains("Debuggable Release with ChummerHub", StringComparison.Ordinal));

        StringAssert.Contains(readmeText, "Legacy head policy: `Chummer` and `Chummer.Web` are oracle/parity assets only.");
        StringAssert.Contains(readmeText, "Net-new user-facing behavior belongs in the shared seam and active heads;");
        StringAssert.Contains(readmeText, "legacy changes must be limited to regression-oracle maintenance, parity extraction, or compatibility verification.");
        StringAssert.Contains(readmeText, "Legacy hub policy: `ChummerHub` and `ChummerHub.Client` are archived compatibility assets only.");
        StringAssert.Contains(readmeText, "They are not part of the active solution, public runtime, or future ChummerHub product path;");
        StringAssert.Contains(readmeText, "all public-edge and hub work belongs behind `Chummer.Portal`.");
        if (TryFindPath("ChummerHub", "README.md") is string legacyHubReadmePath
            && TryFindPath("ChummerHub", "Program.cs") is string legacyHubProgramPath
            && TryFindPath("ChummerHub", "appsettings.json") is string legacyHubAppSettingsPath
            && TryFindPath("ChummerHub", "ApplicationInsights.config") is string legacyHubInsightsConfigPath
            && TryFindPath("ChummerHub", "ChummerHub.csproj") is string legacyHubProjectPath)
        {
            string legacyHubReadmeText = File.ReadAllText(legacyHubReadmePath);
            string legacyHubProgramText = File.ReadAllText(legacyHubProgramPath);
            string legacyHubAppSettingsText = File.ReadAllText(legacyHubAppSettingsPath);
            string legacyHubInsightsConfigText = File.ReadAllText(legacyHubInsightsConfigPath);
            string legacyHubProjectText = File.ReadAllText(legacyHubProjectPath);

            StringAssert.Contains(legacyHubReadmeText, "This project is an archived compatibility asset only.");
            StringAssert.Contains(legacyHubReadmeText, "It is not part of the active solution or public runtime.");
            StringAssert.Contains(legacyHubReadmeText, "Active hub, portal, and public-edge work belongs behind `Chummer.Portal`");
            Assert.IsFalse(legacyHubProgramText.Contains("logging.AddApplicationInsights(\"", StringComparison.Ordinal));
            Assert.IsFalse(legacyHubAppSettingsText.Contains("95c486ab-aeb7-4361-8667-409b7bf62713", StringComparison.Ordinal));
            Assert.IsFalse(legacyHubAppSettingsText.Contains("1/-zsfciq55d9xfAYQ_-U1tmpsMiwHT7oKf1fEO8bm9hQ", StringComparison.Ordinal));
            Assert.IsFalse(legacyHubInsightsConfigText.Contains("8a551326-7224-4b2d-a0d1-81a7b0415824", StringComparison.Ordinal));
            Assert.IsFalse(legacyHubProjectText.Contains("<ApplicationInsightsResourceId>", StringComparison.Ordinal));
            Assert.IsFalse(legacyHubProjectText.Contains("<ApplicationInsightsAnnotationResourceId>", StringComparison.Ordinal));
        }
        else
        {
            Assert.IsFalse(dockerfileText.Contains("COPY ChummerHub/ ChummerHub/", StringComparison.Ordinal));
        }

        StringAssert.Contains(backlogText, "Exit state: `Chummer` (WinForms) and `Chummer.Web` are oracle/parity assets only.");
        StringAssert.Contains(backlogText, "Net-new user-facing behavior must land in the shared seam and active heads;");
        StringAssert.Contains(backlogText, "legacy changes are limited to parity extraction, regression-oracle maintenance, or compatibility verification.");

        StringAssert.Contains(prTemplateText, "Net-new user-facing behavior is implemented in the shared seam and active heads, not in legacy-only surfaces.");
        StringAssert.Contains(prTemplateText, "If this PR touches `Chummer` or `Chummer.Web`, the change is limited to parity extraction, regression-oracle maintenance, or compatibility verification");
        StringAssert.Contains(prTemplateText, "## Legacy Touch Rationale");
    }

    [TestMethod]
    public void Ci_wires_blazor_component_and_playwright_jobs_for_phase4_gate()
    {
        string componentSuitePath = FindPath("scripts", "test-blazor-components.sh");
        string componentSuiteText = File.ReadAllText(componentSuitePath);
        string uiE2ePath = FindPath("scripts", "e2e-ui.sh");
        string uiE2eText = File.ReadAllText(uiE2ePath);

        StringAssert.Contains(componentSuiteText, "dotnet test Chummer.Tests/Chummer.Tests.csproj");
        StringAssert.Contains(componentSuiteText, "FullyQualifiedName~BlazorShellComponentTests");

        StringAssert.Contains(uiE2eText, "CHUMMER_UI_PLAYWRIGHT");
        StringAssert.Contains(uiE2eText, "docker compose --profile test run --build --rm -T chummer-playwright");
    }

    [TestMethod]
    public void Avalonia_mainwindow_uses_named_controls_over_findcontrol_orchestration()
    {
        string xamlPath = FindPath("Chummer.Avalonia", "MainWindow.axaml");
        string xamlText = File.ReadAllText(xamlPath);
        string menuControlPath = FindPath("Chummer.Avalonia", "Controls", "ShellMenuBarControl.axaml");
        string menuControlText = File.ReadAllText(menuControlPath);
        string codePath = FindPath("Chummer.Avalonia", "MainWindow.axaml.cs");
        string codeText = File.ReadAllText(codePath);
        string controlBindingPath = FindPath("Chummer.Avalonia", "MainWindow.ControlBinding.cs");
        string controlBindingText = File.ReadAllText(controlBindingPath);
        string statePath = FindPath("Chummer.Avalonia", "MainWindow.StateRefresh.cs");
        string stateText = File.ReadAllText(statePath);
        string projectorPath = FindPath("Chummer.Avalonia", "MainWindow.ShellFrameProjector.cs");
        string projectorText = File.ReadAllText(projectorPath);
        string navigatorCodePath = FindPath("Chummer.Avalonia", "Controls", "NavigatorPaneControl.axaml.cs");
        string navigatorCodeText = File.ReadAllText(navigatorCodePath);
        string commandPaneCodePath = FindPath("Chummer.Avalonia", "Controls", "CommandDialogPaneControl.axaml.cs");
        string commandPaneCodeText = File.ReadAllText(commandPaneCodePath);
        string workspaceStripCodePath = FindPath("Chummer.Avalonia", "Controls", "WorkspaceStripControl.axaml.cs");
        string workspaceStripCodeText = File.ReadAllText(workspaceStripCodePath);
        string summaryHeaderCodePath = FindPath("Chummer.Avalonia", "Controls", "SummaryHeaderControl.axaml.cs");
        string summaryHeaderCodeText = File.ReadAllText(summaryHeaderCodePath);
        string summaryHeaderXamlPath = FindPath("Chummer.Avalonia", "Controls", "SummaryHeaderControl.axaml");
        string summaryHeaderXamlText = File.ReadAllText(summaryHeaderXamlPath);
        string statusStripCodePath = FindPath("Chummer.Avalonia", "Controls", "StatusStripControl.axaml.cs");
        string statusStripCodeText = File.ReadAllText(statusStripCodePath);
        string statusFormatterPath = FindPath("Chummer.Presentation", "Shell", "ShellStatusTextFormatter.cs");
        string statusFormatterText = File.ReadAllText(statusFormatterPath);
        string sectionHostCodePath = FindPath("Chummer.Avalonia", "Controls", "SectionHostControl.axaml.cs");
        string sectionHostCodeText = File.ReadAllText(sectionHostCodePath);
        string toolStripCodePath = FindPath("Chummer.Avalonia", "Controls", "ToolStripControl.axaml.cs");
        string toolStripCodeText = File.ReadAllText(toolStripCodePath);
        string menuBarCodePath = FindPath("Chummer.Avalonia", "Controls", "ShellMenuBarControl.axaml.cs");
        string menuBarCodeText = File.ReadAllText(menuBarCodePath);
        string postRefreshCoordinatorPath = FindPath("Chummer.Avalonia", "MainWindow.PostRefreshCoordinators.cs");
        string postRefreshCoordinatorText = File.ReadAllText(postRefreshCoordinatorPath);

        Assert.IsFalse(codeText.Contains("FindControl<", StringComparison.Ordinal));
        StringAssert.Contains(codeText, "public MainWindow(");
        StringAssert.Contains(codeText, "_controls = MainWindowControlBinder.Bind(");
        Assert.IsFalse(codeText.Contains("private readonly ToolStripControl _toolStrip;", StringComparison.Ordinal));
        Assert.IsFalse(codeText.Contains("private readonly WorkspaceStripControl _workspaceStrip;", StringComparison.Ordinal));
        Assert.IsFalse(codeText.Contains("private readonly ShellMenuBarControl _menuBar;", StringComparison.Ordinal));
        Assert.IsFalse(codeText.Contains("private readonly NavigatorPaneControl _navigatorPane;", StringComparison.Ordinal));
        Assert.IsFalse(codeText.Contains("private readonly CommandDialogPaneControl _commandDialogPane;", StringComparison.Ordinal));
        StringAssert.Contains(controlBindingText, "internal static class MainWindowControlBinder");
        StringAssert.Contains(controlBindingText, "toolStrip.ImportFileRequested +=");
        StringAssert.Contains(controlBindingText, "toolStrip.ImportRawRequested +=");
        StringAssert.Contains(controlBindingText, "toolStrip.SaveRequested +=");
        StringAssert.Contains(controlBindingText, "toolStrip.CloseWorkspaceRequested +=");
        StringAssert.Contains(controlBindingText, "menuBar.MenuSelected +=");
        StringAssert.Contains(controlBindingText, "navigatorPane.WorkspaceSelected +=");
        StringAssert.Contains(controlBindingText, "commandDialogPane.CommandSelected +=");
        StringAssert.Contains(controlBindingText, "internal sealed record MainWindowControls(");
        StringAssert.Contains(controlBindingText, "public string SectionHostInputText => SectionHost.XmlInputText;");
        StringAssert.Contains(controlBindingText, "public void ApplyShellFrame(MainWindowShellFrame shellFrame)");
        StringAssert.Contains(stateText, "MainWindowShellFrameProjector.Project(");
        StringAssert.Contains(stateText, "ApplyShellFrame(shellFrame);");
        StringAssert.Contains(stateText, "ApplyPostRefreshEffects(state);");
        StringAssert.Contains(stateText, "_controls.ApplyShellFrame(shellFrame);");
        StringAssert.Contains(stateText, "private void ApplyPostRefreshEffects(CharacterOverviewState state)");
        StringAssert.Contains(stateText, "MainWindowTransientDispatchSet pendingDispatches = _transientStateCoordinator.ApplyPostRefresh(");
        Assert.IsFalse(stateText.Contains("private void ApplyHeaderState", StringComparison.Ordinal));
        Assert.IsFalse(stateText.Contains("private void ApplyChromeState", StringComparison.Ordinal));
        StringAssert.Contains(navigatorCodeText, "public void SetState(NavigatorPaneState state)");
        StringAssert.Contains(navigatorCodeText, "SetOpenWorkspaces(state.OpenWorkspaces, state.SelectedWorkspaceId);");
        StringAssert.Contains(navigatorCodeText, "SetNavigationTabs(state.NavigationTabs, state.ActiveTabId);");
        StringAssert.Contains(navigatorCodeText, "SetSectionActions(state.SectionActions, state.ActiveActionId);");
        StringAssert.Contains(navigatorCodeText, "SetWorkflowSurfaces(state.WorkflowSurfaces);");
        StringAssert.Contains(commandPaneCodeText, "public void SetState(CommandDialogPaneState state)");
        StringAssert.Contains(commandPaneCodeText, "SetCommands(state.Commands, state.SelectedCommandId);");
        StringAssert.Contains(commandPaneCodeText, "SetDialog(");
        StringAssert.Contains(workspaceStripCodeText, "public void SetState(WorkspaceStripState state)");
        StringAssert.Contains(workspaceStripCodeText, "SetWorkspaceText(state.WorkspaceText);");
        StringAssert.Contains(summaryHeaderCodeText, "public void SetState(SummaryHeaderState state)");
        StringAssert.Contains(summaryHeaderCodeText, "SetValues(state.Name, state.Alias, state.Karma, state.Skills, state.RuntimeSummary, state.CanInspectRuntime);");
        StringAssert.Contains(summaryHeaderCodeText, "RuntimeInspectButton.IsEnabled = canInspectRuntime;");
        StringAssert.Contains(statusStripCodeText, "public void SetState(StatusStripState state)");
        StringAssert.Contains(statusStripCodeText, "SetValues(");
        StringAssert.Contains(statusFormatterText, "public static class ShellStatusTextFormatter");
        StringAssert.Contains(statusFormatterText, "BuildActiveRuntimeSummary");
        StringAssert.Contains(statusFormatterText, "BuildComplianceState");
        StringAssert.Contains(statusFormatterText, "Runtime: ");
        StringAssert.Contains(statusFormatterText, "TrimFingerprint");
        StringAssert.Contains(statusFormatterText, "Workflows:");
        StringAssert.Contains(sectionHostCodeText, "public void SetState(SectionHostState state)");
        StringAssert.Contains(sectionHostCodeText, "SetNotice(state.Notice);");
        StringAssert.Contains(sectionHostCodeText, "SetSectionPreview(state.PreviewJson, state.Rows);");
        StringAssert.Contains(toolStripCodeText, "public void SetState(ToolStripState state)");
        StringAssert.Contains(toolStripCodeText, "SetStatusText(state.StatusText);");
        StringAssert.Contains(menuBarCodeText, "public void SetState(MenuBarState state)");
        StringAssert.Contains(menuBarCodeText, "SetMenuState(");
        StringAssert.Contains(postRefreshCoordinatorText, "internal static class MainWindowPostRefreshCoordinator");
        StringAssert.Contains(postRefreshCoordinatorText, "public static MainWindowPostRefreshResult Apply(");
        StringAssert.Contains(postRefreshCoordinatorText, "private static DesktopDialogWindow? SyncDialogWindow(");
        StringAssert.Contains(postRefreshCoordinatorText, "private static PendingDownloadDispatchRequest? TryCreatePendingDownload(");
        StringAssert.Contains(postRefreshCoordinatorText, "private static PendingExportDispatchRequest? TryCreatePendingExport(");
        StringAssert.Contains(postRefreshCoordinatorText, "private static PendingPrintDispatchRequest? TryCreatePendingPrint(");
        StringAssert.Contains(projectorText, "BuildWorkspaceActionLookup");
        StringAssert.Contains(projectorText, "WorkspaceActionsById");
        StringAssert.Contains(projectorText, "HeaderState: new MainWindowHeaderState(");
        StringAssert.Contains(projectorText, "ToolStrip: new ToolStripState(");
        StringAssert.Contains(projectorText, "MenuBar: new MenuBarState(");
        StringAssert.Contains(projectorText, "ChromeState: new MainWindowChromeState(");
        StringAssert.Contains(projectorText, "WorkspaceStrip: new WorkspaceStripState(");
        StringAssert.Contains(projectorText, "SummaryHeader: new SummaryHeaderState(");
        StringAssert.Contains(projectorText, "RuntimeSummary: ShellStatusTextFormatter.BuildActiveRuntimeSummary(shellSurface.ActiveRuntime)");
        StringAssert.Contains(projectorText, "StatusStrip: new StatusStripState(");
        StringAssert.Contains(projectorText, "ShellStatusTextFormatter.BuildComplianceState");
        StringAssert.Contains(projectorText, "SectionHostState: new SectionHostState(");
        StringAssert.Contains(projectorText, "CommandDialogPaneState: ProjectCommandDialogState(");
        StringAssert.Contains(projectorText, "NavigatorPaneState: new NavigatorPaneState(");
        StringAssert.Contains(projectorText, "shellSurface.Commands");
        StringAssert.Contains(projectorText, "shellSurface.NavigationTabs");

        StringAssert.Contains(xamlText, "x:Name=\"ToolStripControl\"");
        StringAssert.Contains(xamlText, "x:Name=\"WorkspaceStripControl\"");
        StringAssert.Contains(xamlText, "x:Name=\"ShellMenuBarControl\"");
        StringAssert.Contains(xamlText, "x:Name=\"NavigatorPaneControl\"");
        StringAssert.Contains(xamlText, "x:Name=\"SectionHostControl\"");
        StringAssert.Contains(xamlText, "x:Name=\"CommandDialogPaneControl\"");
        StringAssert.Contains(xamlText, "x:Name=\"CoachSidecarControl\"");
        StringAssert.Contains(xamlText, "x:Name=\"SummaryHeaderControl\"");
        StringAssert.Contains(xamlText, "x:Name=\"StatusStripControl\"");
        StringAssert.Contains(xamlText, "<controls:ToolStripControl");
        StringAssert.Contains(xamlText, "<controls:WorkspaceStripControl");
        StringAssert.Contains(xamlText, "<controls:ShellMenuBarControl");
        StringAssert.Contains(xamlText, "<controls:NavigatorPaneControl");
        StringAssert.Contains(xamlText, "<controls:SectionHostControl");
        StringAssert.Contains(xamlText, "<controls:CommandDialogPaneControl");
        StringAssert.Contains(xamlText, "<controls:CoachSidecarControl");
        StringAssert.Contains(xamlText, "<controls:SummaryHeaderControl");
        StringAssert.Contains(xamlText, "<controls:StatusStripControl");
        StringAssert.Contains(summaryHeaderXamlText, "Text=\"Runtime\"");
        StringAssert.Contains(summaryHeaderXamlText, "x:Name=\"RuntimeValueText\"");
        StringAssert.Contains(menuControlText, "Classes=\"menu-button\"");
        StringAssert.Contains(xamlText, "Button.menu-button.active-menu");
    }

    [TestMethod]
    public void Avalonia_workbench_head_embeds_coach_sidecar_for_active_runtime_context()
    {
        string appPath = FindPath("Chummer.Avalonia", "App.axaml.cs");
        string appText = File.ReadAllText(appPath);
        string mainWindowPath = FindPath("Chummer.Avalonia", "MainWindow.axaml.cs");
        string mainWindowText = File.ReadAllText(mainWindowPath);
        string xamlPath = FindPath("Chummer.Avalonia", "MainWindow.axaml");
        string xamlText = File.ReadAllText(xamlPath);
        string coachSidecarCodePath = FindPath("Chummer.Avalonia", "MainWindow.CoachSidecar.cs");
        string coachSidecarCodeText = File.ReadAllText(coachSidecarCodePath);
        string coachClientPath = FindPath("Chummer.Avalonia", "AvaloniaCoachSidecarClient.cs");
        string coachClientText = File.ReadAllText(coachClientPath);
        string coachClientContractPath = FindPath("Chummer.Avalonia", "IAvaloniaCoachSidecarClient.cs");
        string coachClientContractText = File.ReadAllText(coachClientContractPath);
        string coachControlPath = FindPath("Chummer.Avalonia", "Controls", "CoachSidecarControl.axaml");
        string coachControlText = File.ReadAllText(coachControlPath);
        string coachControlCodePath = FindPath("Chummer.Avalonia", "Controls", "CoachSidecarControl.axaml.cs");
        string coachControlCodeText = File.ReadAllText(coachControlCodePath);
        string coachProjectorPath = FindPath("Chummer.Avalonia", "MainWindowCoachSidecarProjector.cs");
        string coachProjectorText = File.ReadAllText(coachProjectorPath);
        string projectorTestsPath = FindPath("Chummer.Tests", "Presentation", "AvaloniaCoachSidecarProjectorTests.cs");
        string projectorTestsText = File.ReadAllText(projectorTestsPath);

        StringAssert.Contains(appText, "AddSingleton<IAvaloniaCoachSidecarClient>");
        StringAssert.Contains(appText, "HttpAvaloniaCoachSidecarClient");
        StringAssert.Contains(appText, "InProcessAvaloniaCoachSidecarClient");
        StringAssert.Contains(mainWindowText, "private readonly IAvaloniaCoachSidecarClient _coachSidecarClient;");
        StringAssert.Contains(mainWindowText, "ResolveService<IAvaloniaCoachSidecarClient>()");
        StringAssert.Contains(xamlText, "CoachSidecarControl");
        StringAssert.Contains(coachClientContractText, "public interface IAvaloniaCoachSidecarClient");
        StringAssert.Contains(coachClientText, "public sealed class HttpAvaloniaCoachSidecarClient");
        StringAssert.Contains(coachClientText, "public sealed class InProcessAvaloniaCoachSidecarClient");
        StringAssert.Contains(coachClientText, "/api/ai/status");
        StringAssert.Contains(coachClientText, "/api/ai/provider-health");
        StringAssert.Contains(coachClientText, "(\"routeType\", routeType)");
        StringAssert.Contains(coachClientText, "/api/ai/conversation-audits");
        StringAssert.Contains(coachControlText, "Coach Sidecar");
        StringAssert.Contains(coachControlText, "Coach Launch");
        StringAssert.Contains(coachControlText, "Copy Coach Link");
        StringAssert.Contains(coachControlCodeText, "LaunchUri");
        StringAssert.Contains(coachControlCodeText, "CopyLaunchRequested");
        StringAssert.Contains(coachSidecarCodeText, "BuildCoachLaunchUri");
        StringAssert.Contains(coachSidecarCodeText, "CopyCoachLaunchUriAsync");
        StringAssert.Contains(coachSidecarCodeText, "AiCoachLaunchQuery.BuildRelativeUri");
        StringAssert.Contains(coachControlText, "Recent Coach Guidance");
        StringAssert.Contains(coachProjectorText, "public static class MainWindowCoachSidecarProjector");
        StringAssert.Contains(coachProjectorText, "LaunchUri");
        StringAssert.Contains(projectorTestsText, "AvaloniaCoachSidecarProjectorTests");
        StringAssert.Contains(projectorTestsText, "Project_formats_budget_provider_and_audit_state_for_active_runtime");
    }

    [TestMethod]
    public void Avalonia_mainwindow_routes_shell_interactions_through_a_single_coordinator()
    {
        string mainWindowPath = FindPath("Chummer.Avalonia", "MainWindow.axaml.cs");
        string mainWindowText = File.ReadAllText(mainWindowPath);
        string coordinatorPath = FindPath("Chummer.Avalonia", "MainWindow.InteractionCoordinator.cs");
        string coordinatorText = File.ReadAllText(coordinatorPath);
        string eventHandlersPath = FindPath("Chummer.Avalonia", "MainWindow.EventHandlers.cs");
        string eventHandlersText = File.ReadAllText(eventHandlersPath);
        string selectionHandlersPath = FindPath("Chummer.Avalonia", "MainWindow.SelectionHandlers.cs");
        string selectionHandlersText = File.ReadAllText(selectionHandlersPath);

        StringAssert.Contains(mainWindowText, "private readonly MainWindowInteractionCoordinator _interactionCoordinator;");
        StringAssert.Contains(mainWindowText, "_interactionCoordinator = new MainWindowInteractionCoordinator(");
        StringAssert.Contains(coordinatorText, "internal sealed class MainWindowInteractionCoordinator");
        StringAssert.Contains(coordinatorText, "public async Task ExecuteCommandAsync(string commandId, CancellationToken ct)");
        StringAssert.Contains(coordinatorText, "public async Task SelectTabAsync(string tabId, CancellationToken ct)");
        StringAssert.Contains(coordinatorText, "public Task ExecuteDialogActionAsync(string actionId, CancellationToken ct)");
        StringAssert.Contains(coordinatorText, "public bool TryGetActiveWorkspaceId(CharacterOverviewState state, out CharacterWorkspaceId activeWorkspaceId)");
        StringAssert.Contains(eventHandlersText, "_interactionCoordinator.SaveAsync");
        StringAssert.Contains(eventHandlersText, "_interactionCoordinator.ToggleMenuAsync");
        StringAssert.Contains(eventHandlersText, "_interactionCoordinator.ExecuteCommandAsync");
        StringAssert.Contains(selectionHandlersText, "_interactionCoordinator.ExecuteCommandAsync");
        StringAssert.Contains(selectionHandlersText, "_interactionCoordinator.SwitchWorkspaceAsync");
        StringAssert.Contains(selectionHandlersText, "_interactionCoordinator.SelectTabAsync");
        StringAssert.Contains(selectionHandlersText, "_interactionCoordinator.ExecuteDialogActionAsync");
        Assert.IsFalse(eventHandlersText.Contains("_shellPresenter.ToggleMenuAsync", StringComparison.Ordinal));
        Assert.IsFalse(selectionHandlersText.Contains("_shellPresenter.SelectTabAsync", StringComparison.Ordinal));
        Assert.IsFalse(selectionHandlersText.Contains("_adapter.ExecuteDialogActionAsync", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Avalonia_mainwindow_routes_post_refresh_lifecycle_through_a_single_coordinator()
    {
        string stateRefreshPath = FindPath("Chummer.Avalonia", "MainWindow.StateRefresh.cs");
        string stateRefreshText = File.ReadAllText(stateRefreshPath);
        string coordinatorPath = FindPath("Chummer.Avalonia", "MainWindow.PostRefreshCoordinators.cs");
        string coordinatorText = File.ReadAllText(coordinatorPath);

        StringAssert.Contains(stateRefreshText, "MainWindowTransientDispatchSet pendingDispatches = _transientStateCoordinator.ApplyPostRefresh(");
        Assert.IsFalse(stateRefreshText.Contains("MainWindowDialogWindowCoordinator.Sync", StringComparison.Ordinal));
        Assert.IsFalse(stateRefreshText.Contains("PendingDownloadDispatchCoordinator.TryCreate", StringComparison.Ordinal));
        StringAssert.Contains(coordinatorText, "internal static class MainWindowPostRefreshCoordinator");
        StringAssert.Contains(coordinatorText, "DesktopDialogWindow? dialogWindow = SyncDialogWindow(");
        StringAssert.Contains(coordinatorText, "PendingDownloadDispatchRequest? pendingDownloadRequest = TryCreatePendingDownload(");
        StringAssert.Contains(coordinatorText, "PendingExportDispatchRequest? pendingExportRequest = TryCreatePendingExport(");
        StringAssert.Contains(coordinatorText, "PendingPrintDispatchRequest? pendingPrintRequest = TryCreatePendingPrint(");
        StringAssert.Contains(coordinatorText, "internal sealed record MainWindowPostRefreshResult(");
    }

    [TestMethod]
    public void Avalonia_mainwindow_routes_storage_operations_through_desktop_file_coordinator()
    {
        string eventHandlersPath = FindPath("Chummer.Avalonia", "MainWindow.EventHandlers.cs");
        string eventHandlersText = File.ReadAllText(eventHandlersPath);
        string downloadsPath = FindPath("Chummer.Avalonia", "MainWindow.Downloads.cs");
        string downloadsText = File.ReadAllText(downloadsPath);
        string coordinatorPath = FindPath("Chummer.Avalonia", "MainWindow.DesktopFileCoordinator.cs");
        string coordinatorText = File.ReadAllText(coordinatorPath);

        StringAssert.Contains(coordinatorText, "internal static class MainWindowDesktopFileCoordinator");
        StringAssert.Contains(coordinatorText, "public static async Task<DesktopImportFileResult> OpenImportFileAsync");
        StringAssert.Contains(coordinatorText, "public static async Task<DesktopDownloadSaveResult> SaveDownloadAsync");
        StringAssert.Contains(coordinatorText, "public static async Task<DesktopDownloadSaveResult> SaveExportAsync");
        StringAssert.Contains(coordinatorText, "public static async Task<DesktopDownloadSaveResult> SavePrintAsync");
        StringAssert.Contains(coordinatorText, "storageProvider.OpenFilePickerAsync");
        StringAssert.Contains(coordinatorText, "storageProvider.SaveFilePickerAsync");
        StringAssert.Contains(eventHandlersText, "MainWindowDesktopFileCoordinator.OpenImportFileAsync(");
        StringAssert.Contains(downloadsText, "MainWindowDesktopFileCoordinator.SaveDownloadAsync(");
        StringAssert.Contains(downloadsText, "MainWindowDesktopFileCoordinator.SaveExportAsync(");
        StringAssert.Contains(downloadsText, "MainWindowDesktopFileCoordinator.SavePrintAsync(");
        Assert.IsFalse(eventHandlersText.Contains("StorageProvider.OpenFilePickerAsync", StringComparison.Ordinal));
        Assert.IsFalse(downloadsText.Contains("StorageProvider.SaveFilePickerAsync", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Avalonia_mainwindow_routes_fallback_feedback_through_feedback_coordinator()
    {
        string eventHandlersPath = FindPath("Chummer.Avalonia", "MainWindow.EventHandlers.cs");
        string eventHandlersText = File.ReadAllText(eventHandlersPath);
        string downloadsPath = FindPath("Chummer.Avalonia", "MainWindow.Downloads.cs");
        string downloadsText = File.ReadAllText(downloadsPath);
        string feedbackPath = FindPath("Chummer.Avalonia", "MainWindow.FeedbackCoordinator.cs");
        string feedbackText = File.ReadAllText(feedbackPath);
        string uiFeedbackPath = FindPath("Chummer.Avalonia", "MainWindow.UiActionFeedback.cs");
        string uiFeedbackText = File.ReadAllText(uiFeedbackPath);

        StringAssert.Contains(feedbackText, "internal static class MainWindowFeedbackCoordinator");
        StringAssert.Contains(feedbackText, "public static void ShowImportRawRequired");
        StringAssert.Contains(feedbackText, "public static void ShowImportFileUnavailable");
        StringAssert.Contains(feedbackText, "public static void ShowNoActiveWorkspace");
        StringAssert.Contains(feedbackText, "public static void ShowDownloadUnavailable");
        StringAssert.Contains(feedbackText, "public static void ShowDownloadCancelled");
        StringAssert.Contains(feedbackText, "public static void ShowDownloadCompleted");
        StringAssert.Contains(feedbackText, "public static void ShowExportUnavailable");
        StringAssert.Contains(feedbackText, "public static void ShowExportCancelled");
        StringAssert.Contains(feedbackText, "public static void ShowExportCompleted");
        StringAssert.Contains(feedbackText, "public static void ShowPrintUnavailable");
        StringAssert.Contains(feedbackText, "public static void ShowPrintCancelled");
        StringAssert.Contains(feedbackText, "public static void ShowPrintCompleted");
        StringAssert.Contains(feedbackText, "public static void ApplyUiActionFailure(");
        StringAssert.Contains(eventHandlersText, "MainWindowFeedbackCoordinator.ShowImportRawRequired(_controls.ToolStrip);");
        StringAssert.Contains(eventHandlersText, "MainWindowFeedbackCoordinator.ShowImportFileUnavailable(_controls.ToolStrip);");
        StringAssert.Contains(eventHandlersText, "MainWindowFeedbackCoordinator.ShowNoActiveWorkspace(_controls.ToolStrip);");
        StringAssert.Contains(downloadsText, "MainWindowFeedbackCoordinator.ShowDownloadUnavailable(_controls.SectionHost);");
        StringAssert.Contains(downloadsText, "MainWindowFeedbackCoordinator.ShowDownloadCancelled(_controls.SectionHost);");
        StringAssert.Contains(downloadsText, "MainWindowFeedbackCoordinator.ShowDownloadCompleted(");
        StringAssert.Contains(downloadsText, "MainWindowFeedbackCoordinator.ShowExportUnavailable(_controls.SectionHost);");
        StringAssert.Contains(downloadsText, "MainWindowFeedbackCoordinator.ShowExportCancelled(_controls.SectionHost);");
        StringAssert.Contains(downloadsText, "MainWindowFeedbackCoordinator.ShowExportCompleted(");
        StringAssert.Contains(downloadsText, "MainWindowFeedbackCoordinator.ShowPrintUnavailable(_controls.SectionHost);");
        StringAssert.Contains(downloadsText, "MainWindowFeedbackCoordinator.ShowPrintCancelled(_controls.SectionHost);");
        StringAssert.Contains(downloadsText, "MainWindowFeedbackCoordinator.ShowPrintCompleted(");
        StringAssert.Contains(uiFeedbackText, "MainWindowFeedbackCoordinator.ApplyUiActionFailure(");
        Assert.IsFalse(eventHandlersText.Contains("_toolStrip.SetStatusText(", StringComparison.Ordinal));
        Assert.IsFalse(downloadsText.Contains("_sectionHost.SetNotice(", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Avalonia_mainwindow_routes_action_execution_through_a_single_coordinator()
    {
        string mainWindowPath = FindPath("Chummer.Avalonia", "MainWindow.axaml.cs");
        string mainWindowText = File.ReadAllText(mainWindowPath);
        string dialogsPath = FindPath("Chummer.Avalonia", "MainWindow.Dialogs.cs");
        string dialogsText = File.ReadAllText(dialogsPath);
        string coordinatorPath = FindPath("Chummer.Avalonia", "MainWindow.ActionExecutionCoordinator.cs");
        string coordinatorText = File.ReadAllText(coordinatorPath);

        StringAssert.Contains(mainWindowText, "private readonly MainWindowActionExecutionCoordinator _actionExecutionCoordinator;");
        StringAssert.Contains(mainWindowText, "_actionExecutionCoordinator = new MainWindowActionExecutionCoordinator(");
        StringAssert.Contains(dialogsText, "_actionExecutionCoordinator.RunAsync(operation, operationName, CancellationToken.None);");
        Assert.IsFalse(dialogsText.Contains("SyncShellWorkspaceContextAsync", StringComparison.Ordinal));
        StringAssert.Contains(coordinatorText, "internal sealed class MainWindowActionExecutionCoordinator");
        StringAssert.Contains(coordinatorText, "public async Task RunAsync(Func<Task> operation, string operationName, CancellationToken ct)");
        StringAssert.Contains(coordinatorText, "_shellPresenter.SyncWorkspaceContextAsync(activeWorkspaceId, ct);");
        StringAssert.Contains(coordinatorText, "_onFailure(operationName, ex);");
    }

    [TestMethod]
    public void Avalonia_mainwindow_routes_lifecycle_hooks_through_a_single_coordinator()
    {
        string mainWindowPath = FindPath("Chummer.Avalonia", "MainWindow.axaml.cs");
        string mainWindowText = File.ReadAllText(mainWindowPath);
        string stateRefreshPath = FindPath("Chummer.Avalonia", "MainWindow.StateRefresh.cs");
        string stateRefreshText = File.ReadAllText(stateRefreshPath);
        string coordinatorPath = FindPath("Chummer.Avalonia", "MainWindow.LifecycleCoordinator.cs");
        string coordinatorText = File.ReadAllText(coordinatorPath);

        StringAssert.Contains(mainWindowText, "private readonly MainWindowLifecycleCoordinator _lifecycleCoordinator;");
        StringAssert.Contains(mainWindowText, "_lifecycleCoordinator = new MainWindowLifecycleCoordinator(");
        StringAssert.Contains(mainWindowText, "_lifecycleCoordinator.Attach();");
        StringAssert.Contains(mainWindowText, "_lifecycleCoordinator.Detach(_transientStateCoordinator.DetachDialogWindow());");
        Assert.IsFalse(mainWindowText.Contains("_adapter.Updated += (_, _) => RefreshState();", StringComparison.Ordinal));
        Assert.IsFalse(mainWindowText.Contains("_shellPresenter.StateChanged += ShellPresenter_OnStateChanged;", StringComparison.Ordinal));
        Assert.IsFalse(mainWindowText.Contains("Opened += OnOpened;", StringComparison.Ordinal));
        Assert.IsFalse(stateRefreshText.Contains("private void ShellPresenter_OnStateChanged", StringComparison.Ordinal));
        StringAssert.Contains(coordinatorText, "internal sealed class MainWindowLifecycleCoordinator");
        StringAssert.Contains(coordinatorText, "public void Attach()");
        StringAssert.Contains(coordinatorText, "public DesktopDialogWindow? Detach(DesktopDialogWindow? dialogWindow)");
        StringAssert.Contains(coordinatorText, "_adapter.Updated += Adapter_OnUpdated;");
        StringAssert.Contains(coordinatorText, "_shellPresenter.StateChanged += ShellPresenter_OnStateChanged;");
        StringAssert.Contains(coordinatorText, "_window.Opened += _onOpened;");
        StringAssert.Contains(coordinatorText, "_adapter.Dispose();");
    }

    [TestMethod]
    public void Avalonia_mainwindow_routes_transient_window_state_through_a_single_coordinator()
    {
        string mainWindowPath = FindPath("Chummer.Avalonia", "MainWindow.axaml.cs");
        string mainWindowText = File.ReadAllText(mainWindowPath);
        string stateRefreshPath = FindPath("Chummer.Avalonia", "MainWindow.StateRefresh.cs");
        string stateRefreshText = File.ReadAllText(stateRefreshPath);
        string selectionHandlersPath = FindPath("Chummer.Avalonia", "MainWindow.SelectionHandlers.cs");
        string selectionHandlersText = File.ReadAllText(selectionHandlersPath);
        string dialogsPath = FindPath("Chummer.Avalonia", "MainWindow.Dialogs.cs");
        string dialogsText = File.ReadAllText(dialogsPath);
        string downloadsPath = FindPath("Chummer.Avalonia", "MainWindow.Downloads.cs");
        string downloadsText = File.ReadAllText(downloadsPath);
        string coordinatorPath = FindPath("Chummer.Avalonia", "MainWindow.TransientStateCoordinator.cs");
        string coordinatorText = File.ReadAllText(coordinatorPath);

        StringAssert.Contains(mainWindowText, "private readonly MainWindowTransientStateCoordinator _transientStateCoordinator;");
        StringAssert.Contains(mainWindowText, "_transientStateCoordinator = new MainWindowTransientStateCoordinator();");
        Assert.IsFalse(mainWindowText.Contains("_dialogWindow", StringComparison.Ordinal));
        Assert.IsFalse(mainWindowText.Contains("_lastDownloadVersionHandled", StringComparison.Ordinal));
        Assert.IsFalse(mainWindowText.Contains("_workspaceActionsById", StringComparison.Ordinal));
        StringAssert.Contains(stateRefreshText, "_transientStateCoordinator.ApplyShellFrame(shellFrame);");
        StringAssert.Contains(stateRefreshText, "MainWindowTransientDispatchSet pendingDispatches = _transientStateCoordinator.ApplyPostRefresh(");
        StringAssert.Contains(selectionHandlersText, "_transientStateCoordinator.TryResolveWorkspaceAction(actionId, out WorkspaceSurfaceActionDefinition? action)");
        StringAssert.Contains(dialogsText, "_transientStateCoordinator.ClearDialogWindow(sender);");
        StringAssert.Contains(downloadsText, "if (!_transientStateCoordinator.ShouldHandleDownload(request))");
        StringAssert.Contains(downloadsText, "if (!_transientStateCoordinator.ShouldHandleExport(request))");
        StringAssert.Contains(downloadsText, "if (!_transientStateCoordinator.ShouldHandlePrint(request))");
        StringAssert.Contains(coordinatorText, "internal sealed class MainWindowTransientStateCoordinator");
        StringAssert.Contains(coordinatorText, "public void ApplyShellFrame(MainWindowShellFrame shellFrame)");
        StringAssert.Contains(coordinatorText, "public MainWindowTransientDispatchSet ApplyPostRefresh(");
        StringAssert.Contains(coordinatorText, "public bool TryResolveWorkspaceAction(string actionId, out WorkspaceSurfaceActionDefinition? action)");
        StringAssert.Contains(coordinatorText, "public DesktopDialogWindow? DetachDialogWindow()");
    }

    [TestMethod]
    public void Avalonia_shell_layout_contains_core_desktop_regions()
    {
        string xamlPath = FindPath("Chummer.Avalonia", "MainWindow.axaml");
        string xamlText = File.ReadAllText(xamlPath);
        string navigatorControlPath = FindPath("Chummer.Avalonia", "Controls", "NavigatorPaneControl.axaml");
        string navigatorControlText = File.ReadAllText(navigatorControlPath);
        string commandPaneControlPath = FindPath("Chummer.Avalonia", "Controls", "CommandDialogPaneControl.axaml");
        string commandPaneControlText = File.ReadAllText(commandPaneControlPath);

        StringAssert.Contains(xamlText, "x:Name=\"MenuBarRegion\"");
        StringAssert.Contains(xamlText, "x:Name=\"ToolStripRegion\"");
        StringAssert.Contains(xamlText, "x:Name=\"WorkspaceStripRegion\"");
        StringAssert.Contains(xamlText, "x:Name=\"LeftNavigatorRegion\"");
        StringAssert.Contains(xamlText, "x:Name=\"SummaryHeaderRegion\"");
        StringAssert.Contains(xamlText, "x:Name=\"SectionRegion\"");
        StringAssert.Contains(xamlText, "x:Name=\"RightShellRegion\"");
        StringAssert.Contains(xamlText, "x:Name=\"StatusStripRegion\"");

        StringAssert.Contains(navigatorControlText, "Open Characters");
        StringAssert.Contains(navigatorControlText, "Navigation Tabs");
        StringAssert.Contains(navigatorControlText, "Section Actions");
        StringAssert.Contains(commandPaneControlText, "Command Palette");
    }

    [TestMethod]
    public void Dual_heads_wire_keyboard_shortcuts_for_core_commands()
    {
        string blazorShellPath = FindPath("Chummer.Blazor", "Components", "Layout", "DesktopShell.razor");
        string blazorShellText = File.ReadAllText(blazorShellPath);
        string avaloniaXamlPath = FindPath("Chummer.Avalonia", "MainWindow.axaml");
        string avaloniaXamlText = File.ReadAllText(avaloniaXamlPath);
        string avaloniaCodePath = FindPath("Chummer.Avalonia", "MainWindow.EventHandlers.cs");
        string avaloniaCodeText = File.ReadAllText(avaloniaCodePath);
        string shortcutCatalogPath = FindPath("Chummer.Presentation", "Shell", "DesktopShortcutCatalog.cs");
        string shortcutCatalogText = File.ReadAllText(shortcutCatalogPath);

        StringAssert.Contains(blazorShellText, "@onkeydown=\"OnShellKeyDown\"");
        string blazorShellCodePath = FindPath("Chummer.Blazor", "Components", "Layout", "DesktopShell.Commands.cs");
        string blazorShellCodeText = File.ReadAllText(blazorShellCodePath);
        StringAssert.Contains(blazorShellCodeText, "args.MetaKey");
        StringAssert.Contains(blazorShellCodeText, "DesktopShortcutCatalog.TryResolveCommandId");

        StringAssert.Contains(avaloniaXamlText, "KeyDown=\"Window_OnKeyDown\"");
        StringAssert.Contains(avaloniaCodeText, "Window_OnKeyDown");
        StringAssert.Contains(avaloniaCodeText, "DesktopShortcutCatalog.TryResolveCommandId");

        StringAssert.Contains(shortcutCatalogText, "\"save_character\"");
        StringAssert.Contains(shortcutCatalogText, "\"save_character_as\"");
        StringAssert.Contains(shortcutCatalogText, "\"close_window\"");
        StringAssert.Contains(shortcutCatalogText, "\"global_settings\"");
        StringAssert.Contains(shortcutCatalogText, "\"open_character\"");
        StringAssert.Contains(shortcutCatalogText, "\"new_character\"");
        StringAssert.Contains(shortcutCatalogText, "\"new_critter\"");
        StringAssert.Contains(shortcutCatalogText, "\"print_character\"");
        StringAssert.Contains(shortcutCatalogText, "\"refresh_character\"");
    }

    [TestMethod]
    public void Avalonia_headless_smoke_suite_is_present_for_phase5_gate()
    {
        string testPath = FindPath("Chummer.Tests", "Presentation", "AvaloniaHeadlessSmokeTests.cs");
        string testText = File.ReadAllText(testPath);

        StringAssert.Contains(testText, "Avalonia_headless_import_edit_switch_save_smoke");
        StringAssert.Contains(testText, "UseHeadless(");
        StringAssert.Contains(testText, "EnsureHeadlessPlatform();");
        StringAssert.Contains(testText, "adapter.ImportAsync(");
        StringAssert.Contains(testText, "UpdateMetadataAsync(");
        StringAssert.Contains(testText, "SaveAsync(");
    }

    private static string ToSectionName(string pascalName)
    {
        return pascalName.ToLowerInvariant();
    }

    [TestMethod]
    public void Ai_gateway_surface_is_exposed_through_protected_api_seam()
    {
        string apiProgramPath = FindPath("Chummer.Api", "Program.cs");
        string apiProgramText = File.ReadAllText(apiProgramPath);
        string aiEndpointsPath = FindPath("Chummer.Api", "Endpoints", "AiEndpoints.cs");
        string aiEndpointsText = File.ReadAllText(aiEndpointsPath);
        string aiGatewayContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiGatewayContracts.cs");
        string aiGatewayContractsText = File.ReadAllText(aiGatewayContractsPath);
        string aiConversationCatalogContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiConversationCatalogContracts.cs");
        string aiConversationCatalogContractsText = File.ReadAllText(aiConversationCatalogContractsPath);
        string aiMediaContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiMediaContracts.cs");
        string aiMediaContractsText = File.ReadAllText(aiMediaContractsPath);
        string aiMediaQueueContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiMediaQueueContracts.cs");
        string aiMediaQueueContractsText = File.ReadAllText(aiMediaQueueContractsPath);
        string aiBuildIdeaCatalogContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiBuildIdeaCatalogContracts.cs");
        string aiBuildIdeaCatalogContractsText = File.ReadAllText(aiBuildIdeaCatalogContractsPath);
        string buildIdeaCardContractsPath = FindPath("Chummer.Run.Contracts", "AI", "BuildIdeaCardContracts.cs");
        string buildIdeaCardContractsText = File.ReadAllText(buildIdeaCardContractsPath);
        string aiExplainContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiExplainContracts.cs");
        string aiExplainContractsText = File.ReadAllText(aiExplainContractsPath);
        string aiPortraitPromptContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiPortraitPromptContracts.cs");
        string aiPortraitPromptContractsText = File.ReadAllText(aiPortraitPromptContractsPath);
        string aiHistoryDraftContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiHistoryDraftContracts.cs");
        string aiHistoryDraftContractsText = File.ReadAllText(aiHistoryDraftContractsPath);
        string aiDigestContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiDigestContracts.cs");
        string aiDigestContractsText = File.ReadAllText(aiDigestContractsPath);
        string aiActionPreviewContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiActionPreviewContracts.cs");
        string aiActionPreviewContractsText = File.ReadAllText(aiActionPreviewContractsPath);
        string aiHubProjectSearchContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiHubProjectSearchContracts.cs");
        string aiHubProjectSearchContractsText = File.ReadAllText(aiHubProjectSearchContractsPath);
        string aiMediaAssetContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiMediaAssetContracts.cs");
        string aiMediaAssetContractsText = File.ReadAllText(aiMediaAssetContractsPath);
        string aiEvaluationContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiEvaluationContracts.cs");
        string aiEvaluationContractsText = File.ReadAllText(aiEvaluationContractsPath);
        string aiApprovalContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiApprovalContracts.cs");
        string aiApprovalContractsText = File.ReadAllText(aiApprovalContractsPath);
        string aiTranscriptContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiTranscriptContracts.cs");
        string aiTranscriptContractsText = File.ReadAllText(aiTranscriptContractsPath);
        string aiRecapDraftContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiRecapDraftContracts.cs");
        string aiRecapDraftContractsText = File.ReadAllText(aiRecapDraftContractsPath);
        string aiPromptRegistryContractsPath = FindPath("Chummer.Run.Contracts", "AI", "AiPromptRegistryContracts.cs");
        string aiPromptRegistryContractsText = File.ReadAllText(aiPromptRegistryContractsPath);
        string aiGatewayServiceContractPath = FindPath("Chummer.Application", "AI", "IAiGatewayService.cs");
        string aiGatewayServiceContractText = File.ReadAllText(aiGatewayServiceContractPath);
        string aiMediaJobServicePath = FindPath("Chummer.Application", "AI", "IAiMediaJobService.cs");
        string aiMediaJobServiceText = File.ReadAllText(aiMediaJobServicePath);
        string aiMediaQueueServicePath = FindPath("Chummer.Application", "AI", "IAiMediaQueueService.cs");
        string aiMediaQueueServiceText = File.ReadAllText(aiMediaQueueServicePath);
        string notImplementedAiMediaJobServicePath = FindPath("Chummer.Application", "AI", "NotImplementedAiMediaJobService.cs");
        string notImplementedAiMediaJobServiceText = File.ReadAllText(notImplementedAiMediaJobServicePath);
        string aiMediaAssetCatalogServicePath = FindPath("Chummer.Application", "AI", "IAiMediaAssetCatalogService.cs");
        string aiMediaAssetCatalogServiceText = File.ReadAllText(aiMediaAssetCatalogServicePath);
        string notImplementedAiMediaAssetCatalogServicePath = FindPath("Chummer.Application", "AI", "NotImplementedAiMediaAssetCatalogService.cs");
        string notImplementedAiMediaAssetCatalogServiceText = File.ReadAllText(notImplementedAiMediaAssetCatalogServicePath);
        string aiEvaluationServicePath = FindPath("Chummer.Application", "AI", "IAiEvaluationService.cs");
        string aiEvaluationServiceText = File.ReadAllText(aiEvaluationServicePath);
        string notImplementedAiEvaluationServicePath = FindPath("Chummer.Application", "AI", "NotImplementedAiEvaluationService.cs");
        string notImplementedAiEvaluationServiceText = File.ReadAllText(notImplementedAiEvaluationServicePath);
        string aiApprovalOrchestratorPath = FindPath("Chummer.Application", "AI", "IAiApprovalOrchestrator.cs");
        string aiApprovalOrchestratorText = File.ReadAllText(aiApprovalOrchestratorPath);
        string notImplementedAiApprovalOrchestratorPath = FindPath("Chummer.Application", "AI", "NotImplementedAiApprovalOrchestrator.cs");
        string notImplementedAiApprovalOrchestratorText = File.ReadAllText(notImplementedAiApprovalOrchestratorPath);
        string transcriptProviderPath = FindPath("Chummer.Application", "AI", "ITranscriptProvider.cs");
        string transcriptProviderText = File.ReadAllText(transcriptProviderPath);
        string notImplementedTranscriptProviderPath = FindPath("Chummer.Application", "AI", "NotImplementedTranscriptProvider.cs");
        string notImplementedTranscriptProviderText = File.ReadAllText(notImplementedTranscriptProviderPath);
        string recapDraftServicePath = FindPath("Chummer.Application", "AI", "IAiRecapDraftService.cs");
        string recapDraftServiceText = File.ReadAllText(recapDraftServicePath);
        string notImplementedRecapDraftServicePath = FindPath("Chummer.Application", "AI", "NotImplementedAiRecapDraftService.cs");
        string notImplementedRecapDraftServiceText = File.ReadAllText(notImplementedRecapDraftServicePath);
        string aiProviderContractPath = FindPath("Chummer.Application", "AI", "IAiProvider.cs");
        string aiProviderContractText = File.ReadAllText(aiProviderContractPath);
        string aiProviderCatalogPath = FindPath("Chummer.Application", "AI", "IAiProviderCatalog.cs");
        string aiProviderCatalogText = File.ReadAllText(aiProviderCatalogPath);
        string aiProviderRouterPath = FindPath("Chummer.Application", "AI", "IAiProviderRouter.cs");
        string aiProviderRouterText = File.ReadAllText(aiProviderRouterPath);
        string aiBudgetServicePath = FindPath("Chummer.Application", "AI", "IAiBudgetService.cs");
        string aiBudgetServiceText = File.ReadAllText(aiBudgetServicePath);
        string aiRouteBudgetPolicyCatalogPath = FindPath("Chummer.Application", "AI", "IAiRouteBudgetPolicyCatalog.cs");
        string aiRouteBudgetPolicyCatalogText = File.ReadAllText(aiRouteBudgetPolicyCatalogPath);
        string aiUsageLedgerStorePath = FindPath("Chummer.Application", "AI", "IAiUsageLedgerStore.cs");
        string aiUsageLedgerStoreText = File.ReadAllText(aiUsageLedgerStorePath);
        string aiProviderHealthStorePath = FindPath("Chummer.Application", "AI", "IAiProviderHealthStore.cs");
        string aiProviderHealthStoreText = File.ReadAllText(aiProviderHealthStorePath);
        string aiResponseCacheStorePath = FindPath("Chummer.Application", "AI", "IAiResponseCacheStore.cs");
        string aiResponseCacheStoreText = File.ReadAllText(aiResponseCacheStorePath);
        string aiResponseCacheKeysPath = FindPath("Chummer.Application", "AI", "AiResponseCacheKeys.cs");
        string aiResponseCacheKeysText = File.ReadAllText(aiResponseCacheKeysPath);
        string aiPromptRegistryServicePath = FindPath("Chummer.Application", "AI", "IAiPromptRegistryService.cs");
        string aiPromptRegistryServiceText = File.ReadAllText(aiPromptRegistryServicePath);
        string aiExplainServicePath = FindPath("Chummer.Application", "AI", "IAiExplainService.cs");
        string aiExplainServiceText = File.ReadAllText(aiExplainServicePath);
        string aiPortraitPromptServicePath = FindPath("Chummer.Application", "AI", "IAiPortraitPromptService.cs");
        string aiPortraitPromptServiceText = File.ReadAllText(aiPortraitPromptServicePath);
        string aiHistoryDraftServicePath = FindPath("Chummer.Application", "AI", "IAiHistoryDraftService.cs");
        string aiHistoryDraftServiceText = File.ReadAllText(aiHistoryDraftServicePath);
        string aiDigestServicePath = FindPath("Chummer.Application", "AI", "IAiDigestService.cs");
        string aiDigestServiceText = File.ReadAllText(aiDigestServicePath);
        string aiActionPreviewServicePath = FindPath("Chummer.Application", "AI", "IAiActionPreviewService.cs");
        string aiActionPreviewServiceText = File.ReadAllText(aiActionPreviewServicePath);
        string aiHubProjectSearchServicePath = FindPath("Chummer.Application", "AI", "IAiHubProjectSearchService.cs");
        string aiHubProjectSearchServiceText = File.ReadAllText(aiHubProjectSearchServicePath);
        string retrievalServicePath = FindPath("Chummer.Application", "AI", "IRetrievalService.cs");
        string retrievalServiceText = File.ReadAllText(retrievalServicePath);
        string promptAssemblerPath = FindPath("Chummer.Application", "AI", "IPromptAssembler.cs");
        string promptAssemblerText = File.ReadAllText(promptAssemblerPath);
        string conversationStorePath = FindPath("Chummer.Application", "AI", "IConversationStore.cs");
        string conversationStoreText = File.ReadAllText(conversationStorePath);
        string inMemoryConversationStorePath = FindPath("Chummer.Application", "AI", "InMemoryConversationStore.cs");
        string inMemoryConversationStoreText = File.ReadAllText(inMemoryConversationStorePath);
        string fileConversationStorePath = FindPath("Chummer.Infrastructure", "Files", "FileAiConversationStore.cs");
        string fileConversationStoreText = File.ReadAllText(fileConversationStorePath);
        string inMemoryAiProviderHealthStorePath = FindPath("Chummer.Application", "AI", "InMemoryAiProviderHealthStore.cs");
        string inMemoryAiProviderHealthStoreText = File.ReadAllText(inMemoryAiProviderHealthStorePath);
        string fileAiProviderHealthStorePath = FindPath("Chummer.Infrastructure", "Files", "FileAiProviderHealthStore.cs");
        string fileAiProviderHealthStoreText = File.ReadAllText(fileAiProviderHealthStorePath);
        string inMemoryAiResponseCacheStorePath = FindPath("Chummer.Application", "AI", "InMemoryAiResponseCacheStore.cs");
        string inMemoryAiResponseCacheStoreText = File.ReadAllText(inMemoryAiResponseCacheStorePath);
        string fileAiResponseCacheStorePath = FindPath("Chummer.Infrastructure", "Files", "FileAiResponseCacheStore.cs");
        string fileAiResponseCacheStoreText = File.ReadAllText(fileAiResponseCacheStorePath);
        string credentialCatalogPath = FindPath("Chummer.Application", "AI", "IAiProviderCredentialCatalog.cs");
        string credentialCatalogText = File.ReadAllText(credentialCatalogPath);
        string credentialSelectorPath = FindPath("Chummer.Application", "AI", "IAiProviderCredentialSelector.cs");
        string credentialSelectorText = File.ReadAllText(credentialSelectorPath);
        string roundRobinCredentialSelectorPath = FindPath("Chummer.Application", "AI", "RoundRobinAiProviderCredentialSelector.cs");
        string roundRobinCredentialSelectorText = File.ReadAllText(roundRobinCredentialSelectorPath);
        string transportOptionsContractPath = FindPath("Chummer.Application", "AI", "IAiProviderTransportOptionsCatalog.cs");
        string transportOptionsContractText = File.ReadAllText(transportOptionsContractPath);
        string transportOptionsPath = FindPath("Chummer.Application", "AI", "AiProviderTransportOptions.cs");
        string transportOptionsText = File.ReadAllText(transportOptionsPath);
        string transportPayloadsPath = FindPath("Chummer.Application", "AI", "AiProviderTransportPayloads.cs");
        string transportPayloadsText = File.ReadAllText(transportPayloadsPath);
        string transportClientPath = FindPath("Chummer.Application", "AI", "IAiProviderTransportClient.cs");
        string transportClientText = File.ReadAllText(transportClientPath);
        string notImplementedTransportClientPath = FindPath("Chummer.Application", "AI", "NotImplementedAiProviderTransportClient.cs");
        string notImplementedTransportClientText = File.ReadAllText(notImplementedTransportClientPath);
        string httpTransportClientPath = FindPath("Chummer.Infrastructure", "AI", "HttpAiProviderTransportClient.cs");
        string httpTransportClientText = File.ReadAllText(httpTransportClientPath);
        string remoteHttpProviderPath = FindPath("Chummer.Application", "AI", "RemoteHttpAiProvider.cs");
        string remoteHttpProviderText = File.ReadAllText(remoteHttpProviderPath);
        string notImplementedAiGatewayServicePath = FindPath("Chummer.Application", "AI", "NotImplementedAiGatewayService.cs");
        string notImplementedAiGatewayServiceText = File.ReadAllText(notImplementedAiGatewayServicePath);
        string defaultAiProviderRouterPath = FindPath("Chummer.Application", "AI", "DefaultAiProviderRouter.cs");
        string defaultAiProviderRouterText = File.ReadAllText(defaultAiProviderRouterPath);
        string defaultAiProviderCatalogPath = FindPath("Chummer.Application", "AI", "DefaultAiProviderCatalog.cs");
        string defaultAiProviderCatalogText = File.ReadAllText(defaultAiProviderCatalogPath);
        string defaultAiBudgetServicePath = FindPath("Chummer.Application", "AI", "DefaultAiBudgetService.cs");
        string defaultAiBudgetServiceText = File.ReadAllText(defaultAiBudgetServicePath);
        string defaultAiExplainServicePath = FindPath("Chummer.Application", "AI", "DefaultAiExplainService.cs");
        string defaultAiExplainServiceText = File.ReadAllText(defaultAiExplainServicePath);
        string defaultAiPortraitPromptServicePath = FindPath("Chummer.Application", "AI", "DefaultAiPortraitPromptService.cs");
        string defaultAiPortraitPromptServiceText = File.ReadAllText(defaultAiPortraitPromptServicePath);
        string defaultAiMediaQueueServicePath = FindPath("Chummer.Application", "AI", "DefaultAiMediaQueueService.cs");
        string defaultAiMediaQueueServiceText = File.ReadAllText(defaultAiMediaQueueServicePath);
        string defaultAiHistoryDraftServicePath = FindPath("Chummer.Application", "AI", "DefaultAiHistoryDraftService.cs");
        string defaultAiHistoryDraftServiceText = File.ReadAllText(defaultAiHistoryDraftServicePath);
        string defaultAiDigestServicePath = FindPath("Chummer.Application", "AI", "DefaultAiDigestService.cs");
        string defaultAiDigestServiceText = File.ReadAllText(defaultAiDigestServicePath);
        string defaultAiActionPreviewServicePath = FindPath("Chummer.Application", "AI", "DefaultAiActionPreviewService.cs");
        string defaultAiActionPreviewServiceText = File.ReadAllText(defaultAiActionPreviewServicePath);
        string defaultAiHubProjectSearchServicePath = FindPath("Chummer.Application", "AI", "DefaultAiHubProjectSearchService.cs");
        string defaultAiHubProjectSearchServiceText = File.ReadAllText(defaultAiHubProjectSearchServicePath);
        string notImplementedAiProviderPath = FindPath("Chummer.Application", "AI", "NotImplementedAiProvider.cs");
        string notImplementedAiProviderText = File.ReadAllText(notImplementedAiProviderPath);
        string environmentCredentialCatalogPath = FindPath("Chummer.Infrastructure", "AI", "EnvironmentAiProviderCredentialCatalog.cs");
        string environmentCredentialCatalogText = File.ReadAllText(environmentCredentialCatalogPath);
        string environmentTransportOptionsCatalogPath = FindPath("Chummer.Infrastructure", "AI", "EnvironmentAiProviderTransportOptionsCatalog.cs");
        string environmentTransportOptionsCatalogText = File.ReadAllText(environmentTransportOptionsCatalogPath);
        string environmentRouteBudgetPolicyCatalogPath = FindPath("Chummer.Infrastructure", "AI", "EnvironmentAiRouteBudgetPolicyCatalog.cs");
        string environmentRouteBudgetPolicyCatalogText = File.ReadAllText(environmentRouteBudgetPolicyCatalogPath);
        string fileAiUsageLedgerStorePath = FindPath("Chummer.Infrastructure", "Files", "FileAiUsageLedgerStore.cs");
        string fileAiUsageLedgerStoreText = File.ReadAllText(fileAiUsageLedgerStorePath);
        string defaultRetrievalServicePath = FindPath("Chummer.Application", "AI", "DefaultRetrievalService.cs");
        string defaultRetrievalServiceText = File.ReadAllText(defaultRetrievalServicePath);
        string defaultAiPromptRegistryServicePath = FindPath("Chummer.Application", "AI", "DefaultAiPromptRegistryService.cs");
        string defaultAiPromptRegistryServiceText = File.ReadAllText(defaultAiPromptRegistryServicePath);
        string defaultPromptAssemblerPath = FindPath("Chummer.Application", "AI", "DefaultPromptAssembler.cs");
        string defaultPromptAssemblerText = File.ReadAllText(defaultPromptAssemblerPath);
        string infrastructureDiPath = FindPath("Chummer.Infrastructure", "DependencyInjection", "ServiceCollectionExtensions.cs");
        string infrastructureDiText = File.ReadAllText(infrastructureDiPath);
        string portalProgramPath = FindPath("Chummer.Portal", "Program.cs");
        string portalProgramText = File.ReadAllText(portalProgramPath);
        string gitignorePath = FindPath(".gitignore");
        string gitignoreText = File.ReadAllText(gitignorePath);
        string dockerignorePath = FindPath(".dockerignore");
        string dockerignoreText = File.ReadAllText(dockerignorePath);
        string envExamplePath = FindPath(".env.example");
        string envExampleText = File.ReadAllText(envExamplePath);
        string readmePath = FindPath("README.md");
        string readmeText = File.ReadAllText(readmePath);

        StringAssert.Contains(apiProgramText, "app.MapAiEndpoints();");
        StringAssert.Contains(aiEndpointsText, "/api/ai/status");
        StringAssert.Contains(aiEndpointsText, "/api/ai/providers");
        StringAssert.Contains(aiEndpointsText, "/api/ai/provider-health");
        StringAssert.Contains(aiEndpointsText, "string? routeType");
        StringAssert.Contains(aiEndpointsText, "/api/ai/conversations");
        StringAssert.Contains(aiEndpointsText, "/api/ai/conversation-audits");
        StringAssert.Contains(aiEndpointsText, "/api/ai/tools");
        StringAssert.Contains(aiEndpointsText, "/api/ai/retrieval-corpora");
        StringAssert.Contains(aiEndpointsText, "/api/ai/route-policies");
        StringAssert.Contains(aiEndpointsText, "/api/ai/route-budgets");
        StringAssert.Contains(aiEndpointsText, "/api/ai/route-budget-statuses");
        StringAssert.Contains(aiEndpointsText, "/api/ai/prompts");
        StringAssert.Contains(aiEndpointsText, "/api/ai/prompts/{promptId}");
        StringAssert.Contains(aiEndpointsText, "/api/ai/build-ideas");
        StringAssert.Contains(aiEndpointsText, "/api/ai/build-ideas/{ideaId}");
        StringAssert.Contains(aiEndpointsText, "/api/ai/hub/projects");
        StringAssert.Contains(aiEndpointsText, "/api/ai/hub/projects/{kind}/{itemId}");
        StringAssert.Contains(aiEndpointsText, "/api/ai/explain");
        StringAssert.Contains(aiEndpointsText, "/api/ai/runtime/{runtimeFingerprint}/summary");
        StringAssert.Contains(aiEndpointsText, "/api/ai/characters/{characterId}/digest");
        StringAssert.Contains(aiEndpointsText, "/api/ai/session/characters/{characterId}/digest");
        StringAssert.Contains(aiEndpointsText, "/api/ai/preview/karma-spend");
        StringAssert.Contains(aiEndpointsText, "/api/ai/preview/nuyen-spend");
        StringAssert.Contains(aiEndpointsText, "/api/ai/apply-preview");
        StringAssert.Contains(aiEndpointsText, "/api/ai/preview/{routeType}");
        StringAssert.Contains(aiEndpointsText, "/api/ai/conversations/{conversationId}");
        StringAssert.Contains(aiEndpointsText, "/api/ai/chat");
        StringAssert.Contains(aiEndpointsText, "/api/ai/coach");
        StringAssert.Contains(aiEndpointsText, "/api/ai/coach/query");
        StringAssert.Contains(aiEndpointsText, "/api/ai/build");
        StringAssert.Contains(aiEndpointsText, "/api/ai/build-lab/query");
        StringAssert.Contains(aiEndpointsText, "/api/ai/docs/query");
        StringAssert.Contains(aiEndpointsText, "/api/ai/media/portrait");
        StringAssert.Contains(aiEndpointsText, "/api/ai/media/portrait/prompt");
        StringAssert.Contains(aiEndpointsText, "/api/ai/history/drafts");
        StringAssert.Contains(aiEndpointsText, "/api/ai/media/queue");
        StringAssert.Contains(aiEndpointsText, "/api/ai/media/dossier");
        StringAssert.Contains(aiEndpointsText, "/api/ai/media/route-video");
        StringAssert.Contains(aiEndpointsText, "/api/ai/media/assets");
        StringAssert.Contains(aiEndpointsText, "/api/ai/media/assets/{assetId}");
        StringAssert.Contains(aiEndpointsText, "/api/ai/approvals");
        StringAssert.Contains(aiEndpointsText, "/api/ai/approvals/{approvalId}/resolve");
        StringAssert.Contains(aiEndpointsText, "/api/ai/session/transcripts");
        StringAssert.Contains(aiEndpointsText, "/api/ai/session/transcripts/{transcriptId}");
        StringAssert.Contains(aiEndpointsText, "/api/ai/session/recap-drafts");
        StringAssert.Contains(aiEndpointsText, "/api/ai/admin/evals");
        StringAssert.Contains(aiEndpointsText, "/api/ai/session/recap");
        StringAssert.Contains(aiEndpointsText, "/api/ai/recap");
        StringAssert.Contains(aiEndpointsText, "StatusCodes.Status501NotImplemented");
        Assert.IsFalse(aiEndpointsText.Contains("MarkPublicApi", StringComparison.Ordinal));

        StringAssert.Contains(aiGatewayContractsText, "AiApiOperations");
        StringAssert.Contains(aiGatewayContractsText, "AiRouteTypes");
        StringAssert.Contains(aiGatewayContractsText, "AiProviderIds");
        StringAssert.Contains(aiGatewayContractsText, "AiToolIds");
        StringAssert.Contains(aiGatewayContractsText, "AiProviderCredentialCounts");
        StringAssert.Contains(aiGatewayContractsText, "AiProviderExecutionPolicy");
        StringAssert.Contains(aiGatewayContractsText, "AiProviderExecutionPolicies");
        StringAssert.Contains(aiGatewayContractsText, "AiRoutePolicyDescriptor");
        StringAssert.Contains(aiGatewayContractsText, "AiRouteBudgetPolicyDescriptor");
        StringAssert.Contains(aiGatewayContractsText, "AiRouteBudgetStatusProjection");
        StringAssert.Contains(aiGatewayContractsText, "CurrentBurstConsumed");
        StringAssert.Contains(aiGatewayContractsText, "AdapterRegistered");
        StringAssert.Contains(aiGatewayContractsText, "AiProviderAdapterKinds");
        StringAssert.Contains(aiGatewayContractsText, "AiProviderCredentialTiers");
        StringAssert.Contains(aiGatewayContractsText, "AiRouteClassIds");
        StringAssert.Contains(aiGatewayContractsText, "AiPersonaIds");
        StringAssert.Contains(aiGatewayContractsText, "AdapterKind");
        StringAssert.Contains(aiGatewayContractsText, "LiveExecutionEnabled");
        StringAssert.Contains(aiGatewayContractsText, "SessionSafe");
        StringAssert.Contains(aiGatewayContractsText, "TransportBaseUrlConfigured");
        StringAssert.Contains(aiGatewayContractsText, "TransportModelConfigured");
        StringAssert.Contains(aiGatewayContractsText, "TransportMetadataConfigured");
        StringAssert.Contains(aiGatewayContractsText, "CredentialTier");
        StringAssert.Contains(aiGatewayContractsText, "CredentialSlotIndex");
        StringAssert.Contains(aiGatewayContractsText, "AiPersonaDescriptor");
        StringAssert.Contains(aiGatewayContractsText, "AiStructuredAnswer");
        StringAssert.Contains(aiGatewayContractsText, "AiActionDraft");
        StringAssert.Contains(aiGatewayContractsText, "FlavorLine");
        StringAssert.Contains(aiGatewayContractsText, "AiApiResult");
        StringAssert.Contains(aiGatewayContractsText, "AiConversationTurnPreview");
        StringAssert.Contains(aiGatewayContractsText, "AiGatewayStatusProjection");
        StringAssert.Contains(aiGatewayContractsText, "RouteBudgetStatuses");
        StringAssert.Contains(aiGatewayContractsText, "AiGroundingBundle");
        StringAssert.Contains(aiGatewayContractsText, "AiGroundingSectionIds");
        StringAssert.Contains(aiGatewayContractsText, "AiGroundingSection");
        StringAssert.Contains(aiGatewayContractsText, "AiProviderRouteDecision");
        StringAssert.Contains(aiGatewayContractsText, "AiProviderTurnPlan");
        StringAssert.Contains(aiGatewayContractsText, "AiConversationTurnRequest");
        StringAssert.Contains(aiGatewayContractsText, "AiConversationTurnResponse");
        StringAssert.Contains(aiGatewayContractsText, "AiConversationTurnRecord");
        StringAssert.Contains(aiGatewayContractsText, "WorkspaceId");
        StringAssert.Contains(aiGatewayContractsText, "AiNotImplementedReceipt");
        StringAssert.Contains(aiGatewayContractsText, "AiQuotaExceededReceipt");
        StringAssert.Contains(aiGatewayContractsText, "AiGatewayDefaults");
        StringAssert.Contains(aiGatewayContractsText, "GetRuntimeSummary");
        StringAssert.Contains(aiGatewayContractsText, "GetCharacterDigest");
        StringAssert.Contains(aiGatewayContractsText, "ExplainValue");
        StringAssert.Contains(aiGatewayContractsText, "SimulateKarmaSpend");
        StringAssert.Contains(aiGatewayContractsText, "SimulateNuyenSpend");
        StringAssert.Contains(aiGatewayContractsText, "SearchBuildIdeas");
        StringAssert.Contains(aiGatewayContractsText, "SearchHubProjects");
        StringAssert.Contains(aiGatewayContractsText, "GetSessionDigest");
        StringAssert.Contains(aiGatewayContractsText, "DraftHistoryEntries");
        StringAssert.Contains(aiGatewayContractsText, "CreatePortraitPrompt");
        StringAssert.Contains(aiGatewayContractsText, "QueueMediaJob");
        StringAssert.Contains(aiGatewayContractsText, "CreateApplyPreview");
        StringAssert.Contains(aiGatewayContractsText, "OwnerRepositoryScopeModes.Owned");
        StringAssert.Contains(aiGatewayContractsText, "OwnerRepositoryScopeModes.PublicCatalog");
        StringAssert.Contains(aiGatewayContractsText, "CreateRoutePolicies");
        StringAssert.Contains(aiGatewayContractsText, "CreateRouteBudgets");
        StringAssert.Contains(aiConversationCatalogContractsText, "public sealed record AiConversationAuditSummary");
        StringAssert.Contains(aiConversationCatalogContractsText, "FlavorLine");
        StringAssert.Contains(aiConversationCatalogContractsText, "AiBudgetSnapshot? Budget");
        StringAssert.Contains(aiConversationCatalogContractsText, "AiStructuredAnswer? StructuredAnswer");

        StringAssert.Contains(aiGatewayServiceContractText, "interface IAiGatewayService");
        StringAssert.Contains(aiGatewayServiceContractText, "ListConversations");
        StringAssert.Contains(aiGatewayServiceContractText, "AiConversationCatalogQuery");
        StringAssert.Contains(aiGatewayServiceContractText, "AiConversationCatalogPage");
        StringAssert.Contains(aiGatewayServiceContractText, "ListTools");
        StringAssert.Contains(aiGatewayServiceContractText, "ListRetrievalCorpora");
        StringAssert.Contains(aiGatewayServiceContractText, "ListRoutePolicies");
        StringAssert.Contains(aiGatewayServiceContractText, "ListRouteBudgets");
        StringAssert.Contains(aiGatewayServiceContractText, "ListRouteBudgetStatuses");
        StringAssert.Contains(aiGatewayServiceContractText, "PreviewTurn");
        StringAssert.Contains(aiGatewayServiceContractText, "SendCoachTurn");
        StringAssert.Contains(aiGatewayServiceContractText, "SendBuildTurn");
        StringAssert.Contains(aiGatewayServiceContractText, "SendDocsTurn");
        StringAssert.Contains(aiGatewayServiceContractText, "SendRecapTurn");
        StringAssert.Contains(aiMediaContractsText, "AiMediaJobRequest");
        StringAssert.Contains(aiMediaContractsText, "AiMediaJobReceipt");
        StringAssert.Contains(aiMediaContractsText, "AiMediaApiOperations");
        StringAssert.Contains(aiMediaQueueContractsText, "AiMediaQueueApiOperations");
        StringAssert.Contains(aiMediaQueueContractsText, "AiMediaQueueRequest");
        StringAssert.Contains(aiMediaQueueContractsText, "AiMediaQueueReceipt");
        StringAssert.Contains(aiMediaQueueContractsText, "AiMediaQueueStates");
        StringAssert.Contains(aiBuildIdeaCatalogContractsText, "AiBuildIdeaCatalogQuery");
        StringAssert.Contains(aiBuildIdeaCatalogContractsText, "AiBuildIdeaCatalog");
        StringAssert.Contains(buildIdeaCardContractsText, "public sealed record BuildIdeaCard");
        StringAssert.Contains(buildIdeaCardContractsText, "string? TitleKey = null");
        StringAssert.Contains(buildIdeaCardContractsText, "IReadOnlyList<RulesetExplainParameter>? SummaryParameters = null");
        StringAssert.Contains(aiExplainContractsText, "AiExplainApiOperations");
        StringAssert.Contains(aiExplainContractsText, "AiExplainValueQuery");
        StringAssert.Contains(aiExplainContractsText, "AiExplainValueProjection");
        StringAssert.Contains(aiExplainContractsText, "AiExplainFragmentProjection");
        StringAssert.Contains(aiPortraitPromptContractsText, "AiPortraitPromptApiOperations");
        StringAssert.Contains(aiPortraitPromptContractsText, "AiPortraitPromptRequest");
        StringAssert.Contains(aiPortraitPromptContractsText, "AiPortraitPromptProjection");
        StringAssert.Contains(aiPortraitPromptContractsText, "AiPortraitPromptVariant");
        StringAssert.Contains(aiHistoryDraftContractsText, "AiHistoryDraftApiOperations");
        StringAssert.Contains(aiHistoryDraftContractsText, "AiHistoryDraftRequest");
        StringAssert.Contains(aiHistoryDraftContractsText, "AiHistoryDraftProjection");
        StringAssert.Contains(aiHistoryDraftContractsText, "AiHistoryDraftEntry");
        StringAssert.Contains(aiDigestContractsText, "AiDigestApiOperations");
        StringAssert.Contains(aiDigestContractsText, "AiRuntimeSummaryProjection");
        StringAssert.Contains(aiDigestContractsText, "AiCharacterDigestProjection");
        StringAssert.Contains(aiDigestContractsText, "AiSessionDigestProjection");
        StringAssert.Contains(aiActionPreviewContractsText, "AiActionPreviewApiOperations");
        StringAssert.Contains(aiActionPreviewContractsText, "AiSpendPlanPreviewRequest");
        StringAssert.Contains(aiActionPreviewContractsText, "AiApplyPreviewRequest");
        StringAssert.Contains(aiActionPreviewContractsText, "AiActionPreviewReceipt");
        StringAssert.Contains(aiActionPreviewContractsText, "AiActionPreviewKinds");
        StringAssert.Contains(aiActionPreviewContractsText, "AiActionPreviewStates");
        StringAssert.Contains(aiActionPreviewContractsText, "WorkspaceId");
        StringAssert.Contains(aiHubProjectSearchContractsText, "AiHubProjectSearchApiOperations");
        StringAssert.Contains(aiHubProjectSearchContractsText, "AiHubProjectSearchQuery");
        StringAssert.Contains(aiHubProjectSearchContractsText, "AiHubProjectProjection");
        StringAssert.Contains(aiHubProjectSearchContractsText, "AiHubProjectCatalog");
        StringAssert.Contains(aiHubProjectSearchContractsText, "AiHubProjectDetailProjection");
        StringAssert.Contains(aiMediaAssetContractsText, "AiMediaAssetApiOperations");
        StringAssert.Contains(aiMediaAssetContractsText, "AiMediaAssetQuery");
        StringAssert.Contains(aiMediaAssetContractsText, "AiMediaAssetProjection");
        StringAssert.Contains(aiMediaAssetContractsText, "AiMediaAssetCatalog");
        StringAssert.Contains(aiMediaAssetContractsText, "AiMediaAssetKinds");
        StringAssert.Contains(aiEvaluationContractsText, "AiEvaluationCatalog");
        StringAssert.Contains(aiEvaluationContractsText, "AiEvaluationApiOperations");
        StringAssert.Contains(aiApprovalContractsText, "AiApprovalApiOperations");
        StringAssert.Contains(aiApprovalContractsText, "AiApprovalQuery");
        StringAssert.Contains(aiApprovalContractsText, "AiApprovalSubmitRequest");
        StringAssert.Contains(aiApprovalContractsText, "AiApprovalResolveRequest");
        StringAssert.Contains(aiApprovalContractsText, "AiApprovalCatalog");
        StringAssert.Contains(aiApprovalContractsText, "AiApprovalReceipt");
        StringAssert.Contains(aiApprovalContractsText, "AiApprovalTargetKinds");
        StringAssert.Contains(aiApprovalContractsText, "AiApprovalDecisionKinds");
        StringAssert.Contains(aiTranscriptContractsText, "AiTranscriptApiOperations");
        StringAssert.Contains(aiTranscriptContractsText, "AiTranscriptSubmissionRequest");
        StringAssert.Contains(aiTranscriptContractsText, "AiTranscriptDocumentReceipt");
        StringAssert.Contains(aiRecapDraftContractsText, "AiRecapDraftApiOperations");
        StringAssert.Contains(aiRecapDraftContractsText, "AiRecapDraftQuery");
        StringAssert.Contains(aiRecapDraftContractsText, "AiRecapDraftRequest");
        StringAssert.Contains(aiRecapDraftContractsText, "AiRecapDraftCatalog");
        StringAssert.Contains(aiRecapDraftContractsText, "AiRecapDraftReceipt");
        StringAssert.Contains(aiPromptRegistryContractsText, "AiPromptCatalogQuery");
        StringAssert.Contains(aiPromptRegistryContractsText, "AiPromptDescriptor");
        StringAssert.Contains(aiPromptRegistryContractsText, "AiPromptCatalog");
        StringAssert.Contains(aiPromptRegistryContractsText, "AiPromptKinds");
        StringAssert.Contains(aiMediaJobServiceText, "interface IAiMediaJobService");
        StringAssert.Contains(aiMediaJobServiceText, "QueuePortraitJob");
        StringAssert.Contains(aiMediaJobServiceText, "QueueRouteVideoJob");
        StringAssert.Contains(aiMediaQueueServiceText, "interface IAiMediaQueueService");
        StringAssert.Contains(aiMediaQueueServiceText, "QueueMediaJob");
        StringAssert.Contains(notImplementedAiMediaJobServiceText, "The Chummer AI media surface is not implemented yet.");
        StringAssert.Contains(aiMediaAssetCatalogServiceText, "interface IAiMediaAssetCatalogService");
        StringAssert.Contains(aiMediaAssetCatalogServiceText, "ListMediaAssets");
        StringAssert.Contains(aiMediaAssetCatalogServiceText, "GetMediaAsset");
        StringAssert.Contains(notImplementedAiMediaAssetCatalogServiceText, "The Chummer AI media-asset catalog surface is not implemented yet.");
        StringAssert.Contains(aiEvaluationServiceText, "interface IAiEvaluationService");
        StringAssert.Contains(aiEvaluationServiceText, "ListEvaluations");
        StringAssert.Contains(notImplementedAiEvaluationServiceText, "The Chummer AI evaluation surface is not implemented yet.");
        StringAssert.Contains(aiApprovalOrchestratorText, "interface IAiApprovalOrchestrator");
        StringAssert.Contains(aiApprovalOrchestratorText, "ListApprovals");
        StringAssert.Contains(aiApprovalOrchestratorText, "SubmitApproval");
        StringAssert.Contains(aiApprovalOrchestratorText, "ResolveApproval");
        StringAssert.Contains(notImplementedAiApprovalOrchestratorText, "The Chummer AI approval surface is not implemented yet.");
        StringAssert.Contains(transcriptProviderText, "interface ITranscriptProvider");
        StringAssert.Contains(transcriptProviderText, "SubmitTranscript");
        StringAssert.Contains(transcriptProviderText, "GetTranscript");
        StringAssert.Contains(notImplementedTranscriptProviderText, "The Chummer AI transcript surface is not implemented yet.");
        StringAssert.Contains(recapDraftServiceText, "interface IAiRecapDraftService");
        StringAssert.Contains(recapDraftServiceText, "ListRecapDrafts");
        StringAssert.Contains(recapDraftServiceText, "CreateRecapDraft");
        StringAssert.Contains(notImplementedRecapDraftServiceText, "The Chummer AI recap-draft surface is not implemented yet.");
        StringAssert.Contains(aiProviderContractText, "interface IAiProvider");
        StringAssert.Contains(aiProviderContractText, "AiProviderExecutionPolicy");
        StringAssert.Contains(aiProviderContractText, "AiProviderTurnPlan");
        StringAssert.Contains(aiProviderCatalogText, "interface IAiProviderCatalog");
        StringAssert.Contains(aiProviderRouterText, "interface IAiProviderRouter");
        StringAssert.Contains(aiBudgetServiceText, "interface IAiBudgetService");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "ai_quota_exceeded");
        StringAssert.Contains(aiPromptRegistryServiceText, "interface IAiPromptRegistryService");
        StringAssert.Contains(aiExplainServiceText, "interface IAiExplainService");
        StringAssert.Contains(aiExplainServiceText, "GetExplainValue");
        StringAssert.Contains(aiPortraitPromptServiceText, "interface IAiPortraitPromptService");
        StringAssert.Contains(aiPortraitPromptServiceText, "CreatePortraitPrompt");
        StringAssert.Contains(aiHistoryDraftServiceText, "interface IAiHistoryDraftService");
        StringAssert.Contains(aiHistoryDraftServiceText, "CreateHistoryDraft");
        StringAssert.Contains(aiDigestServiceText, "interface IAiDigestService");
        StringAssert.Contains(aiDigestServiceText, "GetRuntimeSummary");
        StringAssert.Contains(aiDigestServiceText, "GetCharacterDigest");
        StringAssert.Contains(aiDigestServiceText, "GetSessionDigest");
        StringAssert.Contains(aiActionPreviewServiceText, "interface IAiActionPreviewService");
        StringAssert.Contains(aiActionPreviewServiceText, "PreviewKarmaSpend");
        StringAssert.Contains(aiActionPreviewServiceText, "PreviewNuyenSpend");
        StringAssert.Contains(aiActionPreviewServiceText, "CreateApplyPreview");
        StringAssert.Contains(aiHubProjectSearchServiceText, "interface IAiHubProjectSearchService");
        StringAssert.Contains(aiHubProjectSearchServiceText, "SearchProjects");
        StringAssert.Contains(aiHubProjectSearchServiceText, "GetProjectDetail");
        StringAssert.Contains(aiPromptRegistryServiceText, "ListPrompts");
        StringAssert.Contains(aiPromptRegistryServiceText, "GetPrompt");
        StringAssert.Contains(retrievalServiceText, "interface IRetrievalService");
        StringAssert.Contains(promptAssemblerText, "interface IPromptAssembler");
        StringAssert.Contains(aiGatewayContractsText, "SearchBuildIdeas");
        StringAssert.Contains(conversationStoreText, "interface IConversationStore");
        StringAssert.Contains(conversationStoreText, "AiConversationCatalogPage");
        StringAssert.Contains(conversationStoreText, "List(OwnerScope owner, AiConversationCatalogQuery query)");
        StringAssert.Contains(inMemoryConversationStoreText, "InMemoryConversationStore");
        StringAssert.Contains(inMemoryConversationStoreText, "NormalizeQuery");
        StringAssert.Contains(fileConversationStoreText, "AiConversationCatalogPage");
        StringAssert.Contains(fileConversationStoreText, "FileAiConversationStore");
        StringAssert.Contains(credentialCatalogText, "interface IAiProviderCredentialCatalog");
        StringAssert.Contains(credentialCatalogText, "GetConfiguredCredentialSets");
        StringAssert.Contains(credentialSelectorText, "interface IAiProviderCredentialSelector");
        StringAssert.Contains(roundRobinCredentialSelectorText, "RoundRobinAiProviderCredentialSelector");
        StringAssert.Contains(transportOptionsContractText, "interface IAiProviderTransportOptionsCatalog");
        StringAssert.Contains(transportOptionsContractText, "GetConfiguredTransportOptions");
        StringAssert.Contains(transportOptionsText, "AiProviderTransportOptions");
        StringAssert.Contains(transportPayloadsText, "AiProviderTransportRequest");
        StringAssert.Contains(transportPayloadsText, "AiProviderTransportResponse");
        StringAssert.Contains(transportPayloadsText, "AiProviderTransportStates");
        StringAssert.Contains(transportClientText, "interface IAiProviderTransportClient");
        StringAssert.Contains(notImplementedTransportClientText, "NotImplementedAiProviderTransportClient");
        StringAssert.Contains(httpTransportClientText, "HttpAiProviderTransportClient");
        StringAssert.Contains(httpTransportClientText, "API-KEY");
        StringAssert.Contains(httpTransportClientText, "AuthenticationHeaderValue(\"Bearer\", apiKey)");
        StringAssert.Contains(httpTransportClientText, "AiProviderTransportStates.Failed");
        StringAssert.Contains(remoteHttpProviderText, "RemoteHttpAiProvider");
        StringAssert.Contains(remoteHttpProviderText, "AiProviderAdapterKinds.RemoteHttp");
        StringAssert.Contains(remoteHttpProviderText, "LiveExecutionEnabled");
        StringAssert.Contains(remoteHttpProviderText, "_scaffoldTransportClient.Execute");

        StringAssert.Contains(notImplementedAiGatewayServiceText, "ai_not_implemented");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "IAiProviderCredentialCatalog");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "IAiProviderCredentialSelector");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "IAiProviderCatalog");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "PreviewTurn");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "AiConversationTurnRecord");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "Turns:");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "stub provider adapter registered");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "remote provider transport registered");
        StringAssert.Contains(environmentCredentialCatalogText, "CHUMMER_AI_AIMAGICX_PRIMARY_API_KEY");
        StringAssert.Contains(environmentCredentialCatalogText, "CHUMMER_AI_AIMAGICX_FALLBACK_API_KEY");
        StringAssert.Contains(environmentCredentialCatalogText, "CHUMMER_AI_1MINAI_PRIMARY_API_KEY");
        StringAssert.Contains(environmentCredentialCatalogText, "CHUMMER_AI_1MINAI_FALLBACK_API_KEY");
        StringAssert.Contains(environmentCredentialCatalogText, "GetConfiguredCredentialSets");
        StringAssert.Contains(environmentTransportOptionsCatalogText, "CHUMMER_AI_ENABLE_REMOTE_EXECUTION");
        StringAssert.Contains(environmentTransportOptionsCatalogText, "CHUMMER_AI_AIMAGICX_BASE_URL");
        StringAssert.Contains(environmentTransportOptionsCatalogText, "CHUMMER_AI_AIMAGICX_MODEL");
        StringAssert.Contains(environmentTransportOptionsCatalogText, "CHUMMER_AI_1MINAI_BASE_URL");
        StringAssert.Contains(environmentTransportOptionsCatalogText, "CHUMMER_AI_1MINAI_MODEL");
        StringAssert.Contains(environmentTransportOptionsCatalogText, "GetConfiguredTransportOptions");
        StringAssert.Contains(environmentRouteBudgetPolicyCatalogText, "CHUMMER_AI_CHAT_MONTHLY_ALLOWANCE");
        StringAssert.Contains(environmentRouteBudgetPolicyCatalogText, "CHUMMER_AI_COACH_BURST_LIMIT_PER_MINUTE");
        StringAssert.Contains(environmentRouteBudgetPolicyCatalogText, "CHUMMER_AI_BUILD_MONTHLY_ALLOWANCE");
        StringAssert.Contains(environmentRouteBudgetPolicyCatalogText, "CHUMMER_AI_DOCS_BURST_LIMIT_PER_MINUTE");
        StringAssert.Contains(environmentRouteBudgetPolicyCatalogText, "CHUMMER_AI_RECAP_MONTHLY_ALLOWANCE");
        StringAssert.Contains(environmentRouteBudgetPolicyCatalogText, "ListPolicies()");
        StringAssert.Contains(defaultAiProviderRouterText, "DefaultAiProviderRouter");
        StringAssert.Contains(defaultAiProviderRouterText, "AiProviderCredentialTiers");
        StringAssert.Contains(aiRouteBudgetPolicyCatalogText, "interface IAiRouteBudgetPolicyCatalog");
        StringAssert.Contains(aiRouteBudgetPolicyCatalogText, "GetPolicy");
        StringAssert.Contains(aiUsageLedgerStoreText, "interface IAiUsageLedgerStore");
        StringAssert.Contains(aiUsageLedgerStoreText, "GetConsumedBetween");
        StringAssert.Contains(aiUsageLedgerStoreText, "RecordUsage");
        StringAssert.Contains(aiProviderHealthStoreText, "interface IAiProviderHealthStore");
        StringAssert.Contains(aiProviderHealthStoreText, "RecordSuccess");
        StringAssert.Contains(aiProviderHealthStoreText, "RecordFailure");
        StringAssert.Contains(aiResponseCacheStoreText, "interface IAiResponseCacheStore");
        StringAssert.Contains(aiResponseCacheStoreText, "AiCachedConversationTurn? Get");
        StringAssert.Contains(aiResponseCacheStoreText, "void Upsert");
        StringAssert.Contains(aiResponseCacheKeysText, "CreateLookup");
        StringAssert.Contains(aiResponseCacheKeysText, "CreateCacheKey");
        StringAssert.Contains(aiResponseCacheKeysText, "NormalizePrompt");
        StringAssert.Contains(fileAiUsageLedgerStoreText, "usage-ledger.json");
        StringAssert.Contains(fileAiUsageLedgerStoreText, "RecentEvents");
        StringAssert.Contains(fileAiUsageLedgerStoreText, "RecordUsage");
        StringAssert.Contains(inMemoryAiProviderHealthStoreText, "InMemoryAiProviderHealthStore");
        StringAssert.Contains(inMemoryAiProviderHealthStoreText, "ConsecutiveFailureCount");
        StringAssert.Contains(fileAiProviderHealthStoreText, "provider-health.json");
        StringAssert.Contains(fileAiProviderHealthStoreText, "RecordFailure");
        StringAssert.Contains(inMemoryAiResponseCacheStoreText, "InMemoryAiResponseCacheStore");
        StringAssert.Contains(inMemoryAiResponseCacheStoreText, "CreateCacheKey");
        StringAssert.Contains(fileAiResponseCacheStoreText, "response-cache.json");
        StringAssert.Contains(fileAiResponseCacheStoreText, "Normalize(");
        StringAssert.Contains(defaultAiProviderCatalogText, "DefaultAiProviderCatalog");
        StringAssert.Contains(defaultAiProviderCatalogText, "CreateDefaultProviders()");
        StringAssert.Contains(defaultAiProviderCatalogText, ".Concat(");
        StringAssert.Contains(defaultAiProviderCatalogText, "provider.ExecutionPolicy");
        StringAssert.Contains(notImplementedAiProviderText, "NotImplementedAiProvider");
        StringAssert.Contains(notImplementedAiProviderText, "AiProviderExecutionPolicies.Resolve");
        StringAssert.Contains(notImplementedAiProviderText, "AiProviderAdapterKinds.Stub");
        StringAssert.Contains(remoteHttpProviderText, "AiProviderExecutionPolicies.Resolve");
        StringAssert.Contains(remoteHttpProviderText, "AiProviderTransportRequest");
        StringAssert.Contains(remoteHttpProviderText, "_transportClient.Execute");
        StringAssert.Contains(defaultAiBudgetServiceText, "DefaultAiBudgetService");
        StringAssert.Contains(defaultAiExplainServiceText, "DefaultAiExplainService");
        StringAssert.Contains(defaultAiExplainServiceText, "IAiDigestService");
        StringAssert.Contains(defaultAiExplainServiceText, "IRulesetPluginRegistry");
        StringAssert.Contains(defaultAiExplainServiceText, "RulesetExecutionOptions(Explain: true)");
        StringAssert.Contains(defaultAiPortraitPromptServiceText, "DefaultAiPortraitPromptService");
        StringAssert.Contains(defaultAiPortraitPromptServiceText, "IAiDigestService");
        StringAssert.Contains(defaultAiPortraitPromptServiceText, "CreatePortraitPrompt");
        StringAssert.Contains(defaultAiMediaQueueServiceText, "DefaultAiMediaQueueService");
        StringAssert.Contains(defaultAiMediaQueueServiceText, "IAiDigestService");
        StringAssert.Contains(defaultAiMediaQueueServiceText, "IAiPortraitPromptService");
        StringAssert.Contains(defaultAiMediaQueueServiceText, "IAiMediaJobService");
        StringAssert.Contains(defaultAiMediaQueueServiceText, "QueueMediaJob");
        StringAssert.Contains(defaultAiHistoryDraftServiceText, "DefaultAiHistoryDraftService");
        StringAssert.Contains(defaultAiHistoryDraftServiceText, "IAiDigestService");
        StringAssert.Contains(defaultAiHistoryDraftServiceText, "ITranscriptProvider");
        StringAssert.Contains(defaultAiHistoryDraftServiceText, "CreateHistoryDraft");
        StringAssert.Contains(defaultAiDigestServiceText, "DefaultAiDigestService");
        StringAssert.Contains(defaultAiDigestServiceText, "GetRuntimeSummary");
        StringAssert.Contains(defaultAiDigestServiceText, "GetCharacterDigest");
        StringAssert.Contains(defaultAiDigestServiceText, "GetSessionDigest");
        StringAssert.Contains(defaultAiDigestServiceText, "_runtimeLockRegistryService");
        StringAssert.Contains(defaultAiDigestServiceText, "_workspaceService");
        StringAssert.Contains(defaultAiDigestServiceText, "_sessionService");
        StringAssert.Contains(defaultAiActionPreviewServiceText, "DefaultAiActionPreviewService");
        StringAssert.Contains(defaultAiActionPreviewServiceText, "PreviewKarmaSpend");
        StringAssert.Contains(defaultAiActionPreviewServiceText, "PreviewNuyenSpend");
        StringAssert.Contains(defaultAiActionPreviewServiceText, "CreateApplyPreview");
        StringAssert.Contains(defaultAiActionPreviewServiceText, "_aiDigestService");
        StringAssert.Contains(defaultAiHubProjectSearchServiceText, "DefaultAiHubProjectSearchService");
        StringAssert.Contains(defaultAiHubProjectSearchServiceText, "IHubCatalogService");
        StringAssert.Contains(defaultAiHubProjectSearchServiceText, "HubCatalogItemKinds.NormalizeOptional");
        StringAssert.Contains(defaultAiHubProjectSearchServiceText, "AiHubProjectCatalog");
        StringAssert.Contains(defaultAiPromptRegistryServiceText, "DefaultAiPromptRegistryService");
        StringAssert.Contains(defaultAiPromptRegistryServiceText, "BuildPromptCatalog");
        StringAssert.Contains(defaultAiPromptRegistryServiceText, "AiPromptKinds.RouteSystem");
        StringAssert.Contains(defaultRetrievalServiceText, "DefaultRetrievalService");
        StringAssert.Contains(defaultRetrievalServiceText, "_aiDigestService");
        StringAssert.Contains(defaultRetrievalServiceText, "ResolveRuntimeRetrievedItems");
        StringAssert.Contains(defaultRetrievalServiceText, "ResolvePrivateRetrievedItems");
        StringAssert.Contains(defaultRetrievalServiceText, "GetRuntimeSummary");
        StringAssert.Contains(defaultRetrievalServiceText, "GetCharacterDigest");
        StringAssert.Contains(defaultRetrievalServiceText, "GetSessionDigest");
        StringAssert.Contains(defaultRetrievalServiceText, "ResolveCommunityRetrievedItems");
        StringAssert.Contains(defaultRetrievalServiceText, "SearchBuildIdeas");
        StringAssert.Contains(defaultPromptAssemblerText, "DefaultPromptAssembler");
        StringAssert.Contains(defaultPromptAssemblerText, "AssembleTurnPlan");
        StringAssert.Contains(defaultPromptAssemblerText, "persona_rules");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "ListProviderHealth");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "ResolveRoutableProviderDecision");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "_providerHealthStore");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "_responseCacheStore");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "CreateCacheLookup");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "AiCacheStatuses.Hit");
        StringAssert.Contains(notImplementedAiGatewayServiceText, "AiCacheStatuses.Miss");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiProviderCredentialCatalog, EnvironmentAiProviderCredentialCatalog>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiProviderTransportOptionsCatalog, EnvironmentAiProviderTransportOptionsCatalog>()");
        StringAssert.Contains(infrastructureDiText, "new HttpAiProviderTransportClient(provider.GetRequiredService<IAiProviderCredentialCatalog>())");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiProviderCredentialSelector, RoundRobinAiProviderCredentialSelector>()");
        StringAssert.Contains(infrastructureDiText, "new DefaultAiProviderCatalog(CreateConfiguredAiProviders(");
        StringAssert.Contains(infrastructureDiText, "new RemoteHttpAiProvider(options, transportClient)");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiProviderRouter, DefaultAiProviderRouter>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiRouteBudgetPolicyCatalog, EnvironmentAiRouteBudgetPolicyCatalog>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiUsageLedgerStore>(_ => new FileAiUsageLedgerStore(stateDirectory))");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiProviderHealthStore>(_ => new FileAiProviderHealthStore(stateDirectory))");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiResponseCacheStore>(_ => new FileAiResponseCacheStore(stateDirectory))");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiBudgetService, DefaultAiBudgetService>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IBuildIdeaCardCatalogService, DefaultBuildIdeaCardCatalogService>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiExplainService, DefaultAiExplainService>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiPortraitPromptService, DefaultAiPortraitPromptService>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiHistoryDraftService, DefaultAiHistoryDraftService>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiMediaQueueService, DefaultAiMediaQueueService>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiDigestService, DefaultAiDigestService>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiActionPreviewService, DefaultAiActionPreviewService>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiHubProjectSearchService, DefaultAiHubProjectSearchService>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiPromptRegistryService, DefaultAiPromptRegistryService>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IRetrievalService, DefaultRetrievalService>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IPromptAssembler, DefaultPromptAssembler>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IConversationStore>(_ => new FileAiConversationStore(stateDirectory))");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiGatewayService, NotImplementedAiGatewayService>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiMediaJobService, NotImplementedAiMediaJobService>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiMediaAssetCatalogService, NotImplementedAiMediaAssetCatalogService>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiEvaluationService, NotImplementedAiEvaluationService>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiApprovalOrchestrator, NotImplementedAiApprovalOrchestrator>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<ITranscriptProvider, NotImplementedTranscriptProvider>()");
        StringAssert.Contains(infrastructureDiText, "AddSingleton<IAiRecapDraftService, NotImplementedAiRecapDraftService>()");
        StringAssert.Contains(portalProgramText, "CHUMMER_PORTAL_AI_PROXY_URL");
        StringAssert.Contains(portalProgramText, "CHUMMER_RUN_URL");
        StringAssert.Contains(portalProgramText, "Portal:ChummerRunUrl");
        StringAssert.Contains(portalProgramText, "Portal:AiProxyBaseUrl");
        StringAssert.Contains(portalProgramText, "Portal:CoachProxyBaseUrl");
        StringAssert.Contains(portalProgramText, "RouteId = \"portal-ai\"");
        StringAssert.Contains(portalProgramText, "Path = \"/api/ai/{**catch-all}\"");
        StringAssert.Contains(portalProgramText, "ClusterId = \"ai-cluster\"");
        StringAssert.Contains(portalProgramText, "IReadOnlyList<IReadOnlyDictionary<string, string>>? aiRouteTransforms = useAiProxy");
        StringAssert.Contains(portalProgramText, "Transforms = aiRouteTransforms");

        StringAssert.Contains(envExampleText, "CHUMMER_AI_AIMAGICX_PRIMARY_API_KEY");
        StringAssert.Contains(envExampleText, "CHUMMER_AI_AIMAGICX_FALLBACK_API_KEY");
        StringAssert.Contains(envExampleText, "CHUMMER_AI_1MINAI_PRIMARY_API_KEY");
        StringAssert.Contains(envExampleText, "CHUMMER_AI_1MINAI_FALLBACK_API_KEY");
        StringAssert.Contains(envExampleText, "CHUMMER_RUN_URL");
        StringAssert.Contains(envExampleText, "CHUMMER_AI_ENABLE_REMOTE_EXECUTION");
        StringAssert.Contains(envExampleText, "CHUMMER_AI_AIMAGICX_BASE_URL");
        StringAssert.Contains(envExampleText, "CHUMMER_AI_AIMAGICX_MODEL");
        StringAssert.Contains(envExampleText, "CHUMMER_AI_1MINAI_BASE_URL");
        StringAssert.Contains(envExampleText, "CHUMMER_AI_1MINAI_MODEL");
        StringAssert.Contains(envExampleText, "CHUMMER_AI_CHAT_MONTHLY_ALLOWANCE");
        StringAssert.Contains(envExampleText, "CHUMMER_AI_COACH_BURST_LIMIT_PER_MINUTE");
        StringAssert.Contains(envExampleText, "CHUMMER_AI_BUILD_MONTHLY_ALLOWANCE");
        StringAssert.Contains(envExampleText, "CHUMMER_AI_DOCS_BURST_LIMIT_PER_MINUTE");
        StringAssert.Contains(envExampleText, "CHUMMER_AI_RECAP_MONTHLY_ALLOWANCE");
        StringAssert.Contains(gitignoreText, ".env");
        StringAssert.Contains(gitignoreText, ".env.*");
        StringAssert.Contains(gitignoreText, "!.env.example");
        StringAssert.Contains(dockerignoreText, "**/.env");
        StringAssert.Contains(readmeText, "/api/ai/*");
        StringAssert.Contains(readmeText, "AI gateway");
        StringAssert.Contains(readmeText, "Chummer-grounded");
        StringAssert.Contains(readmeText, "/api/ai/prompts");
        StringAssert.Contains(readmeText, "/api/ai/build-ideas");
        StringAssert.Contains(readmeText, "/api/ai/hub/projects");
        StringAssert.Contains(readmeText, "/api/ai/hub/projects/{kind}/{itemId}");
        StringAssert.Contains(readmeText, "/api/ai/explain");
        StringAssert.Contains(readmeText, "/api/ai/runtime/{runtimeFingerprint}/summary");
        StringAssert.Contains(readmeText, "/api/ai/characters/{characterId}/digest");
        StringAssert.Contains(readmeText, "/api/ai/session/characters/{characterId}/digest");
        StringAssert.Contains(readmeText, "/api/ai/preview/karma-spend");
        StringAssert.Contains(readmeText, "/api/ai/preview/nuyen-spend");
        StringAssert.Contains(readmeText, "/api/ai/apply-preview");
        StringAssert.Contains(readmeText, "build-idea");
        StringAssert.Contains(readmeText, "prompt-registry");
        StringAssert.Contains(readmeText, "/api/ai/conversations");
        StringAssert.Contains(readmeText, "/api/ai/conversation-audits");
        StringAssert.Contains(readmeText, "workspace scope");
        StringAssert.Contains(readmeText, "filters replay lists by runtime/character/workspace scope");
        StringAssert.Contains(readmeText, "/api/ai/tools");
        StringAssert.Contains(readmeText, "/api/ai/retrieval-corpora");
        StringAssert.Contains(readmeText, "route-policy/route-budget");
        StringAssert.Contains(readmeText, "/api/ai/preview/{routeType}");
        StringAssert.Contains(readmeText, "CHUMMER_AI_AIMAGICX_PRIMARY_API_KEY");
        StringAssert.Contains(readmeText, "CHUMMER_AI_1MINAI_FALLBACK_API_KEY");
        StringAssert.Contains(readmeText, "forwards the AI provider credential and transport env vars into `chummer-api`");
        StringAssert.Contains(readmeText, "CHUMMER_PORTAL_AI_PROXY_URL");
        StringAssert.Contains(readmeText, "CHUMMER_RUN_URL");
        StringAssert.Contains(readmeText, "chummer.run");
        StringAssert.Contains(readmeText, "typed provider turn plan");
        StringAssert.Contains(readmeText, "typed execution-plan boundary");
        StringAssert.Contains(readmeText, "adapter registration");
        StringAssert.Contains(readmeText, "adapter kind");
        StringAssert.Contains(readmeText, "credential-slot rotation");
        StringAssert.Contains(readmeText, "remote-http transport");
        StringAssert.Contains(readmeText, "tool-calling provider");
        StringAssert.Contains(readmeText, "typed outbound transport-request/transport-response seams");
        StringAssert.Contains(readmeText, "typed outbound transport requests and responses");
        StringAssert.Contains(readmeText, "typed allowed-tool descriptors");
        StringAssert.Contains(readmeText, "typed Build Idea cards");
        StringAssert.Contains(readmeText, "runtime/corpus citations");
        StringAssert.Contains(readmeText, "suggested follow-up actions");
        StringAssert.Contains(readmeText, "prepared tool invocation receipts");
        StringAssert.Contains(readmeText, "route-class and persona metadata");
        StringAssert.Contains(readmeText, "decker-contact persona");
        StringAssert.Contains(readmeText, "short flavor line");
        StringAssert.Contains(readmeText, "structured answer payload");
        StringAssert.Contains(readmeText, "actionDrafts");
        StringAssert.Contains(readmeText, "session state are preferred before prose corpora");
        StringAssert.Contains(readmeText, "live-execution state");
        StringAssert.Contains(readmeText, "stub adapters");
        StringAssert.Contains(readmeText, "CHUMMER_AI_ENABLE_REMOTE_EXECUTION");
        StringAssert.Contains(readmeText, "CHUMMER_AI_AIMAGICX_BASE_URL");
        StringAssert.Contains(readmeText, "CHUMMER_AI_1MINAI_MODEL");
        StringAssert.Contains(readmeText, "CHUMMER_AI_CHAT_MONTHLY_ALLOWANCE");
        StringAssert.Contains(readmeText, "CHUMMER_AI_COACH_BURST_LIMIT_PER_MINUTE");
        StringAssert.Contains(readmeText, "CHUMMER_AI_BUILD_MONTHLY_ALLOWANCE");
        StringAssert.Contains(readmeText, "CHUMMER_AI_DOCS_BURST_LIMIT_PER_MINUTE");
        StringAssert.Contains(readmeText, "CHUMMER_AI_RECAP_MONTHLY_ALLOWANCE");
        StringAssert.Contains(readmeText, "owner-backed file-store scaffold");
        StringAssert.Contains(readmeText, "/api/ai/coach");
        StringAssert.Contains(readmeText, "/api/ai/coach/query");
        StringAssert.Contains(readmeText, "/api/ai/build-lab/query");
        StringAssert.Contains(readmeText, "/api/ai/docs/query");
        StringAssert.Contains(readmeText, "/api/ai/media/portrait/prompt");
        StringAssert.Contains(readmeText, "/api/ai/history/drafts");
        StringAssert.Contains(readmeText, "/api/ai/media/queue");
        StringAssert.Contains(readmeText, "/api/ai/media/portrait");
        StringAssert.Contains(readmeText, "/api/ai/media/dossier");
        StringAssert.Contains(readmeText, "/api/ai/media/route-video");
        StringAssert.Contains(readmeText, "/api/ai/media/assets");
        StringAssert.Contains(readmeText, "/api/ai/approvals");
        StringAssert.Contains(readmeText, "/api/ai/admin/evals");
        StringAssert.Contains(readmeText, "/api/ai/session/transcripts");
        StringAssert.Contains(readmeText, "/api/ai/session/recap-drafts");
        StringAssert.Contains(readmeText, "/api/ai/session/recap");
        StringAssert.Contains(readmeText, "/api/ai/chat");
        StringAssert.Contains(readmeText, "/api/ai/route-budget-statuses?routeType=...");
        StringAssert.Contains(readmeText, "/api/ai/provider-health");
        StringAssert.Contains(readmeText, "runtime summaries, character and session digests");
        StringAssert.Contains(readmeText, "apply-preview preparation");
        StringAssert.Contains(readmeText, "runtime summaries come from the runtime-lock registry");
        StringAssert.Contains(readmeText, "Coach grounding now uses those shared digest projections directly");
        StringAssert.Contains(readmeText, "owner-scoped response-cache seam");
        StringAssert.Contains(readmeText, "deterministic cache hits without burning additional Chummer AI units");
        StringAssert.Contains(readmeText, "grounding-coverage summaries");
        StringAssert.Contains(readmeText, "cache hit/miss metadata");
        StringAssert.Contains(readmeText, "route-decision receipts");
        StringAssert.Contains(readmeText, "provider-health, circuit-state, transport-readiness, and credential-slot projections");
        StringAssert.Contains(readmeText, "action-preview seams");
        StringAssert.Contains(readmeText, "karma, nuyen, and apply previews resolve through grounded runtime/character/session digests");
        StringAssert.Contains(readmeText, "AI-facing hub-search");
        StringAssert.Contains(readmeText, "search_hub_projects");
        StringAssert.Contains(readmeText, "explain-lookup");
        StringAssert.Contains(readmeText, "explain_value");
        StringAssert.Contains(readmeText, "history-draft");
        StringAssert.Contains(readmeText, "draft_history_entries");
        StringAssert.Contains(readmeText, "portrait-prompt");
        StringAssert.Contains(readmeText, "create_portrait_prompt");
        StringAssert.Contains(readmeText, "media-queue");
        StringAssert.Contains(readmeText, "queue_media_job");
        StringAssert.Contains(readmeText, "media-job");
        StringAssert.Contains(readmeText, "media-asset catalog");
        StringAssert.Contains(readmeText, "evaluation seams");
        StringAssert.Contains(readmeText, "approval-orchestrator");
        StringAssert.Contains(readmeText, "recap/media/canonical-write approval");
        StringAssert.Contains(readmeText, "transcript-provider");
        StringAssert.Contains(readmeText, "recap-draft seams");
        StringAssert.Contains(readmeText, "contract-first bounded stubs");
    }

    private static HashSet<string> LoadParityOracleIds(string propertyName)
    {
        string parityOraclePath = FindPath("docs", "PARITY_ORACLE.json");
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(parityOraclePath));
        return oracle.RootElement.GetProperty(propertyName)
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string FindPath(params string[] parts)
    {
        foreach (string? root in CandidateRoots())
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            DirectoryInfo current = new(root);
            while (true)
            {
                string candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
                if (File.Exists(candidate))
                    return candidate;

                if (current.Parent == null)
                    break;

                current = current.Parent;
            }
        }

        throw new FileNotFoundException("Could not locate file.", Path.Combine(parts));
    }

    private static string? TryFindPath(params string[] parts)
    {
        try
        {
            return FindPath(parts);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static void AssertProjectNamespacesMatch(string projectName, string expectedNamespacePrefix)
    {
        string projectFilePath = FindPath(projectName, $"{projectName}.csproj");
        string projectDirectory = Path.GetDirectoryName(projectFilePath)
            ?? throw new InvalidOperationException($"Could not resolve directory for project '{projectName}'.");

        foreach (string filePath in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (filePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || filePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string? namespaceLine = File.ReadLines(filePath)
                .FirstOrDefault(line => line.StartsWith("namespace ", StringComparison.Ordinal));
            if (namespaceLine is null)
            {
                continue;
            }

            string declaredNamespace = namespaceLine["namespace ".Length..]
                .Trim()
                .TrimEnd(';')
                .Trim();
            Assert.IsTrue(
                declaredNamespace.StartsWith(expectedNamespacePrefix, StringComparison.Ordinal),
                $"File '{filePath}' should declare a namespace starting with '{expectedNamespacePrefix}', but declared '{declaredNamespace}'.");
        }
    }

    private static bool PathExistsInCandidateRoots(params string[] parts)
    {
        foreach (string? root in CandidateRoots())
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            DirectoryInfo current = new(root);
            while (true)
            {
                string candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
                if (File.Exists(candidate))
                {
                    return true;
                }

                if (current.Parent is null)
                {
                    break;
                }

                current = current.Parent;
            }
        }

        return false;
    }

    private static string FindDirectory(params string[] parts)
    {
        foreach (string? root in CandidateRoots())
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            DirectoryInfo current = new(root);
            while (true)
            {
                string candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
                if (Directory.Exists(candidate))
                    return candidate;

                if (current.Parent == null)
                    break;

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate directory: " + Path.Combine(parts));
    }

    private static string FindPresentationContractsPath(string fileName)
    {
        return FindPath("Chummer.Contracts", "Presentation", fileName);
    }

    private static string FindCorePresentationContractsPath(string fileName)
    {
        string canonicalPath = FindPath("Chummer.Contracts", "Presentation", fileName);
        Assert.IsFalse(
            PathExistsInCandidateRoots("Chummer.Presentation.Contracts", "Presentation", fileName),
            $"Core-owned contract '{fileName}' should not remain under Chummer.Presentation.Contracts.");
        return canonicalPath;
    }

    private static IEnumerable<string?> CandidateRoots()
    {
        yield return Environment.GetEnvironmentVariable("CHUMMER_REPO_ROOT");
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
        yield return "/src";
    }
}
