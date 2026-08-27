using Chummer.Application.Characters;
using Chummer.Application.AI;
using Chummer.Application.BuildLab;
using Chummer.Application.Campaign;
using Chummer.Application.Content;
using Chummer.Application.Explain;
using Chummer.Application.Hub;
using Chummer.Application.Owners;
using Chummer.Application.LifeModules;
using Chummer.Application.Seeds;
using Chummer.Application.Session;
using Chummer.Application.Simulation;
using Chummer.Application.Tools;
using Chummer.Application.Workspaces;
using Chummer.Infrastructure.AI;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddSingleton<IBuildLabService, DefaultBuildLabService>();
        services.AddSingleton<IAiProviderCredentialCatalog, EmptyAiProviderCredentialCatalog>();
        services.AddSingleton<IAiProviderTransportOptionsCatalog, EmptyAiProviderTransportOptionsCatalog>();
        services.AddSingleton<IAiProviderTransportClient>(_ => new NotImplementedAiProviderTransportClient());
        services.AddSingleton<IAiProviderCatalog>(_ => new DefaultAiProviderCatalog());
        services.AddSingleton<IAiProviderCredentialSelector, RoundRobinAiProviderCredentialSelector>();
        services.AddSingleton<IAiProviderRouter, DefaultAiProviderRouter>();
        services.AddSingleton<IAiRouteBudgetPolicyCatalog, EnvironmentAiRouteBudgetPolicyCatalog>();
        services.AddStateDirectorySingleton<IAiUsageLedgerStore, FileAiUsageLedgerStore>(stateDirectory);
        services.AddStateDirectorySingleton<IAiResponseCacheStore, FileAiResponseCacheStore>(stateDirectory);
        services.AddStateDirectorySingleton<IAiProviderHealthStore, FileAiProviderHealthStore>(stateDirectory);
        services.AddSingleton<IAiBudgetService, DefaultAiBudgetService>();
        services.AddSingleton<IBuildIdeaCardCatalogService, DefaultBuildIdeaCardCatalogService>();
        services.AddSingleton<IAestheticDigestService, DefaultAestheticDigestService>();
        services.AddSingleton<ISemanticSeedService, DefaultSemanticSeedService>();
        services.AddSingleton<IRelationshipHeatService, DefaultRelationshipHeatService>();
        services.AddSingleton<IAiDigestService, DefaultAiDigestService>();
        services.AddSingleton<IAiExplainService, DefaultAiExplainService>();
        services.AddSingleton<IExplainValuePacketService, DefaultExplainValuePacketService>();
        services.AddSingleton<ICalculationReportService, DefaultCalculationReportService>();
        services.AddSingleton<IAiPortraitPromptService, DefaultAiPortraitPromptService>();
        services.AddSingleton<IAiHistoryDraftService, DefaultAiHistoryDraftService>();
        services.AddSingleton<IAiMediaQueueService, DefaultAiMediaQueueService>();
        services.AddSingleton<IAiActionPreviewService, DefaultAiActionPreviewService>();
        services.AddSingleton<IRetrievalService, DefaultRetrievalService>();
        services.AddSingleton<IAiPromptRegistryService, DefaultAiPromptRegistryService>();
        services.AddSingleton<IPromptAssembler, DefaultPromptAssembler>();
        services.AddStateDirectorySingleton<IConversationStore, FileAiConversationStore>(stateDirectory);
        services.AddSingleton<IAiGatewayService, NotImplementedAiGatewayService>();
        services.AddSingleton<IAiMediaJobService, NotImplementedAiMediaJobService>();
        services.AddSingleton<IAiMediaAssetCatalogService, NotImplementedAiMediaAssetCatalogService>();
        services.AddSingleton<IAiEvaluationService, NotImplementedAiEvaluationService>();
        services.AddSingleton<IAiApprovalOrchestrator, NotImplementedAiApprovalOrchestrator>();
        services.AddSingleton<ITranscriptProvider, NotImplementedTranscriptProvider>();
        services.AddSingleton<IAiRecapDraftService, NotImplementedAiRecapDraftService>();
        services.AddRulesetInfrastructure();
        services.AddSr5Ruleset();
        services.AddSingleton<ICharacterSourceDataResolver, FileSystemCharacterSourceDataResolver>();
        services.AddSingleton<ICharacterLinkedDocumentCodec, Chummer5LinkedDocumentCodec>();
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
                provider.GetRequiredService<ICharacterSocialNarrativeQueries>(),
                provider.GetRequiredService<IBuildLabService>()));
        services.AddSingleton<IContentOverlayCatalogService>(overlays);
        services.AddSingleton<ICharacterCyberwarePurchaseAuthority,
            FileSystemCharacterCyberwarePurchaseAuthority>();
        services.AddSingleton<ICharacterCustomDrugAuthority,
            FileSystemCharacterCustomDrugAuthority>();
        services.AddSingleton<ICharacterVehicleWorkshopAuthority,
            CharacterVehicleWorkshopAuthority>();
        services.AddSingleton<IBuildKitRegistryService, DefaultBuildKitRegistryService>();
        services.AddSingleton<INpcVaultRegistryService, DefaultNpcVaultRegistryService>();
        services.AddSingleton<IOppositionPacketContractService, DefaultOppositionPacketContractService>();
        services.AddSingleton<ICampaignAdvanceReceiptService, DefaultCampaignAdvanceReceiptService>();
        services.AddStateDirectorySingleton<IRulePackManifestStore, FileRulePackManifestStore>(stateDirectory);
        services.AddStateDirectorySingleton<IRulePackInstallHistoryStore, FileRulePackInstallHistoryStore>(stateDirectory);
        services.AddStateDirectorySingleton<IRulePackInstallStateStore, FileRulePackInstallStateStore>(stateDirectory);
        services.AddStateDirectorySingleton<IRulePackPublicationStore, FileRulePackPublicationStore>(stateDirectory);
        services.AddSingleton<IRulePackRegistryService, OverlayRulePackRegistryService>();
        services.AddSingleton<IRulePackInstallService, DefaultRulePackInstallService>();
        services.AddSingleton<IRuntimeFingerprintService, DefaultRuntimeFingerprintService>();
        services.AddStateDirectorySingleton<IRuleProfileManifestStore, FileRuleProfileManifestStore>(stateDirectory);
        services.AddStateDirectorySingleton<IRuleProfileInstallHistoryStore, FileRuleProfileInstallHistoryStore>(stateDirectory);
        services.AddStateDirectorySingleton<IRuleProfileInstallStateStore, FileRuleProfileInstallStateStore>(stateDirectory);
        services.AddStateDirectorySingleton<IRuleProfilePublicationStore, FileRuleProfilePublicationStore>(stateDirectory);
        services.AddSingleton<IRuleProfileRegistryService, DefaultRuleProfileRegistryService>();
        services.AddSingleton<IRuleProfileApplicationService, DefaultRuleProfileApplicationService>();
        services.AddSingleton<IRuntimeInspectorService, DefaultRuntimeInspectorService>();
        services.AddSingleton<IRuntimeLockDiffService, DefaultRuntimeLockDiffService>();
        services.AddSingleton<IRuleEnvironmentStudioService, DefaultRuleEnvironmentStudioService>();
        services.AddSingleton<IActiveRuntimeStatusService, DefaultActiveRuntimeStatusService>();
        services.AddStateDirectorySingleton<IRuntimeLockInstallHistoryStore, FileRuntimeLockInstallHistoryStore>(stateDirectory);
        services.AddStateDirectorySingleton<IRuntimeLockStore, FileRuntimeLockStore>(stateDirectory);
        services.AddSingleton<IRuntimeLockRegistryService, OwnerScopedRuntimeLockRegistryService>();
        services.AddSingleton<IRuntimeLockInstallService, DefaultRuntimeLockInstallService>();
        services.AddSingleton<IHubCatalogService, DefaultHubCatalogService>();
        services.AddSingleton<IAiHubProjectSearchService, DefaultAiHubProjectSearchService>();
        services.AddSingleton<IHubInstallPreviewService, DefaultHubInstallPreviewService>();
        services.AddSingleton<IHubProjectCompatibilityService, DefaultHubProjectCompatibilityService>();
        services.AddStateDirectorySingleton<IHubPublisherStore, FileHubPublisherStore>(stateDirectory);
        services.AddSingleton<IHubPublisherService, DefaultHubPublisherService>();
        services.AddStateDirectorySingleton<IHubReviewStore, FileHubReviewStore>(stateDirectory);
        services.AddSingleton<IHubReviewService, DefaultHubReviewService>();
        services.AddStateDirectorySingleton<IHubDraftStore, FileHubDraftStore>(stateDirectory);
        services.AddStateDirectorySingleton<IHubModerationCaseStore, FileHubModerationCaseStore>(stateDirectory);
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
        services.AddStateDirectorySingleton<ISettingsStore, FileSettingsStore>(stateDirectory);
        services.AddSingleton<IOwnerContextAccessor, LocalOwnerContextAccessor>();
        services.AddSingleton<IShellPreferencesStore, SettingsShellPreferencesStore>();
        services.AddSingleton<IShellPreferencesService, ShellPreferencesService>();
        services.AddSingleton<IShellSessionStore, SettingsShellSessionStore>();
        services.AddSingleton<IShellSessionService, ShellSessionService>();
        services.AddStateDirectorySingleton<ISessionProfileSelectionStore, FileSessionProfileSelectionStore>(stateDirectory);
        services.AddStateDirectorySingleton<ISessionRuntimeBundleStore, FileSessionRuntimeBundleStore>(stateDirectory);
        services.AddSingleton<ISessionService, OwnerScopedSessionService>();
        services.AddSingleton<ISessionActionBudgetService, DefaultSessionActionBudgetService>();
        services.AddStateDirectorySingleton<IRosterStore, FileRosterStore>(stateDirectory);
        services.AddSingleton<IWorkspaceStore>(_ =>
        {
            string? workspaceDirectory = Environment.GetEnvironmentVariable(WorkspaceStorePathEnvironmentVariable);
            return string.IsNullOrWhiteSpace(workspaceDirectory)
                ? new FileWorkspaceStore(stateDirectory)
                : new FileWorkspaceStore(workspaceDirectory);
        });
        services.AddSingleton<IWorkspaceStoreReadinessProbe>(provider =>
            provider.GetRequiredService<IWorkspaceStore>() as IWorkspaceStoreReadinessProbe
            ?? throw new InvalidOperationException(
                "The configured workspace store does not provide a readiness probe."));
        services.AddSingleton<IWorkspaceImportRulesetDetector, WorkspaceImportRulesetDetector>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddCharacterCreationFoundationDraftPersistence();
        services.AddSingleton<ICharacterCreationBootstrapActivationProjector,
            CharacterCreationBootstrapActivationProjector>();
        services.AddSingleton<ICharacterCreationBootstrapService,
            CharacterCreationBootstrapService>();
        services.AddSingleton<ICharacterCreationFoundationService,
            CharacterCreationFoundationService>();
        services.AddSingleton<ICharacterCreationPrerequisiteService,
            CharacterCreationPrerequisiteService>();
        services.AddSingleton<ICharacterCreationAttributesService,
            CharacterCreationAttributesService>();
        services.AddSingleton<ICharacterCreationSkillsService,
            CharacterCreationSkillsService>();
        services.AddSingleton<ICharacterCreationQualitiesService,
            CharacterCreationQualitiesService>();
        services.AddSingleton<ICharacterCreationMagicResonanceService,
            CharacterCreationMagicResonanceService>();
        services.AddSingleton<ICharacterCreationContactsService,
            CharacterCreationContactsService>();
        services.AddSingleton<ICharacterCreationLifestylesService,
            CharacterCreationLifestylesService>();
        services.AddSingleton<ICharacterCreationResourcesService,
            CharacterCreationResourcesService>();
        services.AddSingleton<ICharacterCreationGearService,
            CharacterCreationGearService>();
        services.AddSingleton<ICharacterCreationFinalizationService,
            CharacterCreationFinalizationService>();
        services.TryAddSingleton<ICharacterCareerSkillGroupAdvanceWorkspace,
            UnavailableCharacterCareerSkillGroupAdvanceWorkspace>();
        services.TryAddSingleton<ICharacterCareerSkillGroupAdvanceService,
            CharacterCareerSkillGroupAdvanceService>();
        services.AddCharacterAfterRunSettlementPersistence();

        return services;
    }

    /// <summary>
    /// Composes the saved-character After Run adapter without inventing a run
    /// proposal backend. Hosts may register their authoritative proposal source
    /// before this call; otherwise projection remains explicitly unavailable.
    /// </summary>
    public static IServiceCollection AddCharacterAfterRunSettlementPersistence(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<
            ICharacterAfterRunSettlementProposalProjectionSource,
            UnavailableCharacterAfterRunSettlementProposalProjectionSource>();
        services.TryAddSingleton<ICharacterAfterRunSettlementWorkspace>(provider =>
        {
            IWorkspaceStore store = provider.GetRequiredService<IWorkspaceStore>();
            return store is IWorkspaceAuxiliaryStateAtomicCommitCapability
                   { SupportsWorkspaceAuxiliaryStateAtomicCommit: true }
                ? new WorkspaceCharacterAfterRunSettlementWorkspace(
                    store,
                    provider.GetRequiredService<
                        ICharacterAfterRunSettlementProposalProjectionSource>())
                : new UnavailableCharacterAfterRunSettlementWorkspace();
        });
        services.TryAddSingleton<ICharacterAfterRunSettlementService,
            CharacterAfterRunSettlementService>();
        return services;
    }

    /// <summary>
    /// Selects the draft-only creation authority strictly from the configured store's
    /// explicit atomic auxiliary-state capability. Non-capable compositions remain
    /// fail-closed at Preview.
    /// </summary>
    public static IServiceCollection AddCharacterCreationFoundationDraftPersistence(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ICharacterCreationFoundationApplyAuthority>(provider =>
        {
            IWorkspaceStore store = provider.GetRequiredService<IWorkspaceStore>();
            return store is IWorkspaceAuxiliaryStateAtomicCommitCapability
                   {
                       SupportsWorkspaceAuxiliaryStateAtomicCommit: true
                   }
                ? new CharacterCreationFoundationDraftApplyAuthority(store)
                : new UnavailableCharacterCreationFoundationApplyAuthority();
        });
        return services;
    }

    public static IServiceCollection AddLegacyEnvironmentAiTransportCompatibility(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAiProviderCredentialCatalog, EnvironmentAiProviderCredentialCatalog>();
        services.AddSingleton<IAiProviderTransportOptionsCatalog, EnvironmentAiProviderTransportOptionsCatalog>();
        services.AddSingleton<IAiProviderTransportClient>(provider =>
            new HttpAiProviderTransportClient(provider.GetRequiredService<IAiProviderCredentialCatalog>()));
        services.AddSingleton<IAiProviderCatalog>(provider =>
            new DefaultAiProviderCatalog(CreateConfiguredAiProviders(
                provider.GetRequiredService<IAiProviderTransportOptionsCatalog>(),
                provider.GetRequiredService<IAiProviderTransportClient>())));

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

    private static IServiceCollection AddStateDirectorySingleton<TService, TImplementation>(
        this IServiceCollection services,
        string stateDirectory)
        where TService : class
        where TImplementation : class, TService
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);

        services.AddSingleton<TService>(_ => CreateStateDirectorySingleton<TImplementation>(stateDirectory));
        return services;
    }

    private static TImplementation CreateStateDirectorySingleton<TImplementation>(string stateDirectory)
        where TImplementation : class
    {
        object? instance = Activator.CreateInstance(typeof(TImplementation), stateDirectory);
        return instance as TImplementation
            ?? throw new InvalidOperationException(
                $"Could not create state-directory singleton '{typeof(TImplementation).FullName}'.");
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
