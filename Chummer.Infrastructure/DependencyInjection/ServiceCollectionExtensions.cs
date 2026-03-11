using Chummer.Application.Characters;
using Chummer.Application.AI;
using Chummer.Application.Content;
using Chummer.Application.Hub;
using Chummer.Application.Owners;
using Chummer.Application.LifeModules;
using Chummer.Application.Session;
using Chummer.Application.Tools;
using Chummer.Application.Workspaces;
using Chummer.Infrastructure.AI;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Chummer.Rulesets.Sr6;
using Microsoft.Extensions.DependencyInjection;

namespace Chummer.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    private const string StatePathEnvironmentVariable = "CHUMMER_STATE_PATH";
    private const string WorkspaceStorePathEnvironmentVariable = "CHUMMER_WORKSPACE_STORE_PATH";
    private const string AmendsPathEnvironmentVariable = "CHUMMER_AMENDS_PATH";
    private const string RequireContentBundleEnvironmentVariable = "CHUMMER_REQUIRE_CONTENT_BUNDLE";

    public static IServiceCollection AddChummerHeadlessCore(
        this IServiceCollection services,
        string baseDirectory,
        string currentDirectory,
        bool requireContentBundle = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        string stateDirectory = ResolveStateDirectory(baseDirectory);
        string? amendsDirectory = Environment.GetEnvironmentVariable(AmendsPathEnvironmentVariable);
        bool validateContentBundle = requireContentBundle || ResolveBooleanEnvironmentVariable(RequireContentBundleEnvironmentVariable);
        var overlays = new FileSystemContentOverlayCatalogService(baseDirectory, currentDirectory, amendsDirectory);
        if (validateContentBundle)
        {
            ValidateContentBundle(overlays);
        }

        services.AddSingleton<ICharacterFileService, CharacterFileService>();
        services.AddSingleton<IAiProviderCredentialCatalog, EnvironmentAiProviderCredentialCatalog>();
        services.AddSingleton<IAiProviderTransportOptionsCatalog, EnvironmentAiProviderTransportOptionsCatalog>();
        services.AddSingleton<IAiProviderTransportClient>(provider =>
            new HttpAiProviderTransportClient(provider.GetRequiredService<IAiProviderCredentialCatalog>()));
        services.AddSingleton<IAiProviderCatalog>(provider =>
            new DefaultAiProviderCatalog(CreateConfiguredAiProviders(
                provider.GetRequiredService<IAiProviderTransportOptionsCatalog>(),
                provider.GetRequiredService<IAiProviderTransportClient>())));
        services.AddSingleton<IAiProviderCredentialSelector, RoundRobinAiProviderCredentialSelector>();
        services.AddSingleton<IAiProviderRouter, DefaultAiProviderRouter>();
        services.AddSingleton<IAiRouteBudgetPolicyCatalog, EnvironmentAiRouteBudgetPolicyCatalog>();
        services.AddSingleton<IAiUsageLedgerStore>(_ => new FileAiUsageLedgerStore(stateDirectory));
        services.AddSingleton<IAiResponseCacheStore>(_ => new FileAiResponseCacheStore(stateDirectory));
        services.AddSingleton<IAiProviderHealthStore>(_ => new FileAiProviderHealthStore(stateDirectory));
        services.AddSingleton<IAiBudgetService, DefaultAiBudgetService>();
        services.AddSingleton<IBuildIdeaCardCatalogService, DefaultBuildIdeaCardCatalogService>();
        services.AddSingleton<IAiDigestService, DefaultAiDigestService>();
        services.AddSingleton<IAiExplainService, DefaultAiExplainService>();
        services.AddSingleton<IAiPortraitPromptService, DefaultAiPortraitPromptService>();
        services.AddSingleton<IAiHistoryDraftService, DefaultAiHistoryDraftService>();
        services.AddSingleton<IAiMediaQueueService, DefaultAiMediaQueueService>();
        services.AddSingleton<IAiActionPreviewService, DefaultAiActionPreviewService>();
        services.AddSingleton<IRetrievalService, DefaultRetrievalService>();
        services.AddSingleton<IAiPromptRegistryService, DefaultAiPromptRegistryService>();
        services.AddSingleton<IPromptAssembler, DefaultPromptAssembler>();
        services.AddSingleton<IConversationStore>(_ => new FileAiConversationStore(stateDirectory));
        services.AddSingleton<IAiGatewayService, NotImplementedAiGatewayService>();
        services.AddSingleton<IAiMediaJobService, NotImplementedAiMediaJobService>();
        services.AddSingleton<IAiMediaAssetCatalogService, NotImplementedAiMediaAssetCatalogService>();
        services.AddSingleton<IAiEvaluationService, NotImplementedAiEvaluationService>();
        services.AddSingleton<IAiApprovalOrchestrator, NotImplementedAiApprovalOrchestrator>();
        services.AddSingleton<ITranscriptProvider, NotImplementedTranscriptProvider>();
        services.AddSingleton<IAiRecapDraftService, NotImplementedAiRecapDraftService>();
        services.AddRulesetInfrastructure();
        services.AddSr5Ruleset();
        services.AddSr6Ruleset();
        services.AddSingleton<ICharacterSectionService, CharacterSectionService>();
        services.AddSingleton<ICharacterFileQueries, XmlCharacterFileQueries>();
        services.AddSingleton<ICharacterMetadataCommands, XmlCharacterMetadataCommands>();
        services.AddSingleton<ICharacterOverviewQueries, XmlCharacterOverviewQueries>();
        services.AddSingleton<ICharacterStatsQueries, XmlCharacterStatsQueries>();
        services.AddSingleton<ICharacterInventoryQueries, XmlCharacterInventoryQueries>();
        services.AddSingleton<ICharacterMagicResonanceQueries, XmlCharacterMagicResonanceQueries>();
        services.AddSingleton<ICharacterSocialNarrativeQueries, XmlCharacterSocialNarrativeQueries>();
        services.AddSingleton<ICharacterSectionQueries>(provider =>
            new XmlCharacterSectionQueries(
                provider.GetRequiredService<ICharacterOverviewQueries>(),
                provider.GetRequiredService<ICharacterStatsQueries>(),
                provider.GetRequiredService<ICharacterInventoryQueries>(),
                provider.GetRequiredService<ICharacterMagicResonanceQueries>(),
                provider.GetRequiredService<ICharacterSocialNarrativeQueries>()));
        services.AddSingleton<IContentOverlayCatalogService>(overlays);
        services.AddSingleton<IBuildKitRegistryService, DefaultBuildKitRegistryService>();
        services.AddSingleton<INpcVaultRegistryService, DefaultNpcVaultRegistryService>();
        services.AddSingleton<IRulePackManifestStore>(_ => new FileRulePackManifestStore(stateDirectory));
        services.AddSingleton<IRulePackInstallHistoryStore>(_ => new FileRulePackInstallHistoryStore(stateDirectory));
        services.AddSingleton<IRulePackInstallStateStore>(_ => new FileRulePackInstallStateStore(stateDirectory));
        services.AddSingleton<IRulePackPublicationStore>(_ => new FileRulePackPublicationStore(stateDirectory));
        services.AddSingleton<IRulePackRegistryService, OverlayRulePackRegistryService>();
        services.AddSingleton<IRulePackInstallService, DefaultRulePackInstallService>();
        services.AddSingleton<IRuntimeFingerprintService, DefaultRuntimeFingerprintService>();
        services.AddSingleton<IRuleProfileManifestStore>(_ => new FileRuleProfileManifestStore(stateDirectory));
        services.AddSingleton<IRuleProfileInstallHistoryStore>(_ => new FileRuleProfileInstallHistoryStore(stateDirectory));
        services.AddSingleton<IRuleProfileInstallStateStore>(_ => new FileRuleProfileInstallStateStore(stateDirectory));
        services.AddSingleton<IRuleProfilePublicationStore>(_ => new FileRuleProfilePublicationStore(stateDirectory));
        services.AddSingleton<IRuleProfileRegistryService, DefaultRuleProfileRegistryService>();
        services.AddSingleton<IRuleProfileApplicationService, DefaultRuleProfileApplicationService>();
        services.AddSingleton<IRuntimeInspectorService, DefaultRuntimeInspectorService>();
        services.AddSingleton<IActiveRuntimeStatusService, DefaultActiveRuntimeStatusService>();
        services.AddSingleton<IRuntimeLockInstallHistoryStore>(_ => new FileRuntimeLockInstallHistoryStore(stateDirectory));
        services.AddSingleton<IRuntimeLockStore>(_ => new FileRuntimeLockStore(stateDirectory));
        services.AddSingleton<IRuntimeLockRegistryService, OwnerScopedRuntimeLockRegistryService>();
        services.AddSingleton<IRuntimeLockInstallService, DefaultRuntimeLockInstallService>();
        services.AddSingleton<IHubCatalogService, DefaultHubCatalogService>();
        services.AddSingleton<IAiHubProjectSearchService, DefaultAiHubProjectSearchService>();
        services.AddSingleton<IHubInstallPreviewService, DefaultHubInstallPreviewService>();
        services.AddSingleton<IHubProjectCompatibilityService, DefaultHubProjectCompatibilityService>();
        services.AddSingleton<IHubPublisherStore>(_ => new FileHubPublisherStore(stateDirectory));
        services.AddSingleton<IHubPublisherService, DefaultHubPublisherService>();
        services.AddSingleton<IHubReviewStore>(_ => new FileHubReviewStore(stateDirectory));
        services.AddSingleton<IHubReviewService, DefaultHubReviewService>();
        services.AddSingleton<IHubDraftStore>(_ => new FileHubDraftStore(stateDirectory));
        services.AddSingleton<IHubModerationCaseStore>(_ => new FileHubModerationCaseStore(stateDirectory));
        services.AddSingleton<IHubPublicationService, DefaultHubPublicationService>();
        services.AddSingleton<IHubModerationService, DefaultHubModerationService>();

        services.AddSingleton<ILifeModulesCatalogService>(provider =>
        {
            var overlays = provider.GetRequiredService<IContentOverlayCatalogService>();
            string path = LifeModulesCatalogPathResolver.Resolve(overlays);
            return new XmlLifeModulesCatalogService(path);
        });

        services.AddSingleton<IDataExportService, DataExportService>();
        services.AddSingleton<IToolCatalogService>(provider =>
            new XmlToolCatalogService(provider.GetRequiredService<IContentOverlayCatalogService>()));
        services.AddSingleton<ISettingsStore>(_ => new FileSettingsStore(stateDirectory));
        services.AddSingleton<IOwnerContextAccessor, LocalOwnerContextAccessor>();
        services.AddSingleton<IShellPreferencesStore, SettingsShellPreferencesStore>();
        services.AddSingleton<IShellPreferencesService, ShellPreferencesService>();
        services.AddSingleton<IShellSessionStore, SettingsShellSessionStore>();
        services.AddSingleton<IShellSessionService, ShellSessionService>();
        services.AddSingleton<ISessionProfileSelectionStore>(_ => new FileSessionProfileSelectionStore(stateDirectory));
        services.AddSingleton<ISessionRuntimeBundleStore>(_ => new FileSessionRuntimeBundleStore(stateDirectory));
        services.AddSingleton<ISessionService, OwnerScopedSessionService>();
        services.AddSingleton<IRosterStore>(_ => new FileRosterStore(stateDirectory));
        services.AddSingleton<IWorkspaceStore>(_ =>
        {
            string? workspaceDirectory = Environment.GetEnvironmentVariable(WorkspaceStorePathEnvironmentVariable);
            return string.IsNullOrWhiteSpace(workspaceDirectory)
                ? new FileWorkspaceStore(stateDirectory)
                : new FileWorkspaceStore(workspaceDirectory);
        });
        services.AddSingleton<IWorkspaceImportRulesetDetector, WorkspaceImportRulesetDetector>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();

        return services;
    }

    private static void ValidateContentBundle(IContentOverlayCatalogService overlays)
    {
        ArgumentNullException.ThrowIfNull(overlays);

        IReadOnlyList<string> dataDirectories = overlays.GetDataDirectories();
        if (dataDirectories.Count == 0)
        {
            throw new InvalidOperationException(
                "Content bundle validation failed: no data directories were discovered. " +
                "Set CHUMMER_AMENDS_PATH correctly or include bundled /data content.");
        }

        IReadOnlyList<string> languageDirectories = overlays.GetLanguageDirectories();
        if (languageDirectories.Count == 0)
        {
            throw new InvalidOperationException(
                "Content bundle validation failed: no language directories were discovered. " +
                "Set CHUMMER_AMENDS_PATH correctly or include bundled /lang content.");
        }

        try
        {
            overlays.ResolveDataFile("lifemodules.xml");
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Content bundle validation failed: required data file 'lifemodules.xml' is missing from effective content paths.",
                ex);
        }

        bool hasAnyLanguageXml = languageDirectories
            .Any(directory => Directory.Exists(directory)
                && Directory.EnumerateFiles(directory, "*.xml", SearchOption.TopDirectoryOnly).Any());
        if (!hasAnyLanguageXml)
        {
            throw new InvalidOperationException(
                "Content bundle validation failed: no language XML files were discovered in effective language paths.");
        }
    }

    private static string ResolveStateDirectory(string baseDirectory)
    {
        string? configured = Environment.GetEnvironmentVariable(StatePathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return Path.Combine(baseDirectory, "state");
    }

    private static bool ResolveBooleanEnvironmentVariable(string variableName)
    {
        string? raw = Environment.GetEnvironmentVariable(variableName);
        return bool.TryParse(raw, out bool parsed) && parsed;
    }

    private static IReadOnlyList<IAiProvider> CreateConfiguredAiProviders(
        IAiProviderTransportOptionsCatalog transportOptionsCatalog,
        IAiProviderTransportClient transportClient)
        => transportOptionsCatalog.GetConfiguredTransportOptions()
            .Values
            .Where(static options => options.TransportConfigured)
            .OrderBy(static options => options.ProviderId, StringComparer.Ordinal)
            .Select(options => (IAiProvider)new RemoteHttpAiProvider(options, transportClient))
            .ToArray();
}
