using System.Xml.Linq;
using System.Text.Json.Nodes;
using Chummer.Application.Content;
using Chummer.Application.Tools;
using Chummer.Contracts.Api;
using Chummer.Infrastructure.Files;

namespace Chummer.Infrastructure.Xml;

public sealed class XmlToolCatalogService : IToolCatalogService
{
    private static readonly string[] Sr6DesignerCatalogFiles =
    [
        "spells.xml",
        "vehicles.xml",
        "programs.xml",
        "drugcomponents.xml",
        "qualities.xml"
    ];

    private const int Sr6DesignerFamiliesExpected = 5;
    private const string CustomDataXmlBridgeParityFamilyId = "family:custom_data_xml_and_translator_bridge";
    private const string TranslatorParityRouteId = "source:translator_route";
    private const string ImportOracleParityFamilyId = "family:legacy_and_adjacent_import_oracles";
    private static readonly string[] RequiredAmendPackageSuiteIds = ["source_toggle", "amend_package"];
    private const string EngineProofPackContractName = "chummer6-core.engine_proof_pack";
    private readonly IContentOverlayCatalogService _overlays;
    private readonly record struct SettingsCatalogSummary(
        int ProfileCount,
        int ProfilesWithSourceToggles,
        int DistinctSourcebookToggles,
        string SettingsLanePosture,
        string SourceToggleLanePosture,
        int SourcebookToggleCoveragePercent,
        int ProfilesWithCustomDataDirectories,
        int DistinctCustomDataDirectoryCount);
    private readonly record struct ImportOracleSummary(
        string LanePosture,
        string ReceiptPosture,
        int LegacyChummer4FixtureCount,
        int LegacyChummer5FixtureCount,
        int HeroLabFixtureCount,
        string AdjacentSr6OracleReceiptPosture,
        int AdjacentSr6OracleSourcesCovered,
        int AdjacentSr6OracleSourcesExpected,
        int SourcesCovered,
        int SourcesExpected,
        int CoveragePercent,
        IReadOnlyList<string> MissingSources);
    private readonly record struct OnlineStorageSummary(
        string LanePosture,
        string ReceiptPosture,
        int ReceiptsCovered,
        int ReceiptsExpected,
        int CoveragePercent);
    private readonly record struct AmendPackageSummary(
        string ProofReceiptPath,
        string LanePosture,
        string ProofPackStatus,
        string SourceToggleSuiteStatus,
        string AmendPackageSuiteStatus,
        string AmendPackageCoverageFocus);
    private readonly record struct TranslatorCatalogSummary(
        string LanePosture,
        string BridgePosture,
        int LanguageCount,
        int EnabledOverlayCount);

    public XmlToolCatalogService(IContentOverlayCatalogService overlays)
    {
        _overlays = overlays;
    }

    public XmlToolCatalogService(string? baseDirectory = null)
    {
        string root = baseDirectory ?? AppContext.BaseDirectory;
        _overlays = new FileSystemContentOverlayCatalogService(root, Directory.GetCurrentDirectory(), configuredAmendsPath: null);
    }

    public MasterIndexResponse GetMasterIndex()
    {
        ContentOverlayCatalog catalog = _overlays.GetCatalog();
        IReadOnlyDictionary<string, XDocument?> filesByName = BuildEffectiveDocuments(
            catalog,
            catalog.BaseDataPath,
            pack => pack.DataPath);
        IReadOnlyDictionary<string, XDocument?> languageFilesByName = BuildEffectiveDocuments(
            catalog,
            catalog.BaseLanguagePath,
            pack => pack.LanguagePath);
        ImportOracleSummary importOracleSummary = BuildImportOracleSummary(catalog.BaseDataPath);
        AmendPackageSummary amendPackageSummary = BuildAmendPackageSummary(catalog.BaseDataPath);
        OnlineStorageSummary onlineStorageSummary = BuildOnlineStorageSummary(catalog.BaseDataPath);
        TranslatorCatalogSummary translatorSummary = BuildTranslatorCatalogSummary(catalog, languageFilesByName);
        if (filesByName.Count == 0)
            return new MasterIndexResponse(
                Count: 0,
                GeneratedUtc: DateTimeOffset.UtcNow,
                Files: Array.Empty<MasterIndexFileEntry>(),
                ReferenceLanePosture: "missing",
                ReferenceLaneReceipt: "No sourcebooks were discovered in books.xml.",
                SourcebookCount: 0,
                Sourcebooks: Array.Empty<MasterIndexSourcebookEntry>(),
                SourcebooksWithSnippets: 0,
                SourcebooksMissingSnippets: 0,
                ReferenceCoveragePercent: 0,
                ReferenceSourceLanePosture: "missing",
                SourcebooksWithGovernedReferenceSources: 0,
                SourcebooksWithStaleReferenceSources: 0,
                SourcebooksMissingReferenceSources: 0,
                ReferenceSourceLaneReceipt: BuildReferenceSourceLaneReceipt(0, 0, 0),
                SettingsLanePosture: "missing",
                SettingsProfileCount: 0,
                SettingsProfilesWithSourceToggles: 0,
                DistinctSourcebookToggles: 0,
                SourceToggleLanePosture: "missing",
                SourceToggleLaneReceipt: "No sourcebook toggle codes were discovered in settings.xml profiles.",
                SourceSelectionLaneReceipt: BuildSourceSelectionLaneReceipt(
                    sourcebookCount: 0,
                    profileCount: 0,
                    profilesWithSourceToggles: 0,
                    distinctSourcebookToggles: 0,
                    sourcebookToggleCoveragePercent: 0,
                    sourceToggleLanePosture: "missing"),
                SettingsLaneReceipt: "No settings profiles were discovered in settings.xml.",
                SourcebookToggleCoveragePercent: 0,
                CustomDataLanePosture: "missing",
                CustomDataLaneReceipt: "No enabled custom data directory entries were discovered in settings.xml.",
                CustomDataAuthoringLaneReceipt: BuildCustomDataAuthoringLaneReceipt(
                    customDataLanePosture: "missing",
                    distinctCustomDataDirectoryCount: 0,
                    enabledDataOverlayCount: CountEnabledDataOverlays(catalog)),
                SettingsProfilesWithCustomDataDirectories: 0,
                DistinctCustomDataDirectoryCount: 0,
                XmlBridgePosture: ResolveXmlBridgePosture(catalog),
                XmlBridgeLaneReceipt: BuildXmlBridgeLaneReceipt(
                    ResolveXmlBridgePosture(catalog),
                    CountEnabledDataOverlays(catalog)),
                EnabledDataOverlayCount: CountEnabledDataOverlays(catalog),
                TranslatorLanePosture: translatorSummary.LanePosture,
                TranslatorLaneReceipt: BuildTranslatorLaneReceipt(translatorSummary),
                TranslatorBridgePosture: translatorSummary.BridgePosture,
                TranslatorLanguageCount: translatorSummary.LanguageCount,
                EnabledLanguageOverlayCount: translatorSummary.EnabledOverlayCount,
                Sr6SupplementLanePosture: ResolveSr6SupplementLanePosture(Array.Empty<MasterIndexSourcebookEntry>()),
                Sr6DesignerToolsPosture: ResolveSr6DesignerToolsPosture(0, Sr6DesignerFamiliesExpected),
                Sr6DesignerFamiliesAvailable: 0,
                Sr6DesignerFamiliesExpected: Sr6DesignerFamiliesExpected,
                HouseRuleLanePosture: ResolveHouseRuleLanePosture(catalog),
                HouseRuleOverlayCount: CountHouseRuleOverlays(catalog),
                OnlineStorageLanePosture: onlineStorageSummary.LanePosture,
                OnlineStorageReceiptPosture: onlineStorageSummary.ReceiptPosture,
                OnlineStorageLaneReceipt: BuildOnlineStorageLaneReceipt(onlineStorageSummary),
                OnlineStorageReceiptsCovered: onlineStorageSummary.ReceiptsCovered,
                OnlineStorageReceiptsExpected: onlineStorageSummary.ReceiptsExpected,
                OnlineStorageCoveragePercent: onlineStorageSummary.CoveragePercent,
                ImportOracleLanePosture: importOracleSummary.LanePosture,
                ImportOracleReceiptPosture: importOracleSummary.ReceiptPosture,
                LegacyChummer4FixtureCount: importOracleSummary.LegacyChummer4FixtureCount,
                LegacyChummer5FixtureCount: importOracleSummary.LegacyChummer5FixtureCount,
                HeroLabFixtureCount: importOracleSummary.HeroLabFixtureCount,
                AdjacentSr6OracleReceiptPosture: importOracleSummary.AdjacentSr6OracleReceiptPosture,
                AdjacentSr6OracleSourcesCovered: importOracleSummary.AdjacentSr6OracleSourcesCovered,
                AdjacentSr6OracleSourcesExpected: importOracleSummary.AdjacentSr6OracleSourcesExpected,
                ImportOracleSourcesCovered: importOracleSummary.SourcesCovered,
                ImportOracleSourcesExpected: importOracleSummary.SourcesExpected,
                ImportOracleCoveragePercent: importOracleSummary.CoveragePercent,
                ImportOracleMissingSources: importOracleSummary.MissingSources,
                ImportOracleLaneReceipt: BuildImportOracleLaneReceipt(importOracleSummary),
                AdjacentSr6OracleLaneReceipt: BuildAdjacentSr6OracleLaneReceipt(importOracleSummary),
                Sr6SuccessorLaneReceipt: BuildSr6SuccessorLaneReceipt(
                    ResolveSr6SupplementLanePosture(Array.Empty<MasterIndexSourcebookEntry>()),
                    ResolveSr6DesignerToolsPosture(0, Sr6DesignerFamiliesExpected),
                    0,
                    Sr6DesignerFamiliesExpected,
                    ResolveHouseRuleLanePosture(catalog),
                    CountHouseRuleOverlays(catalog),
                    onlineStorageSummary.LanePosture,
                    onlineStorageSummary.ReceiptsCovered,
                    onlineStorageSummary.ReceiptsExpected),
                CustomDataXmlBridgeDeterministicReceipt: BuildCustomDataXmlBridgeDeterministicReceipt(
                    customDataLanePosture: "missing",
                    customDataLaneReceipt: "No enabled custom data directory entries were discovered in settings.xml.",
                    customDataAuthoringLaneReceipt: BuildCustomDataAuthoringLaneReceipt(
                        customDataLanePosture: "missing",
                        distinctCustomDataDirectoryCount: 0,
                        enabledDataOverlayCount: CountEnabledDataOverlays(catalog)),
                    settingsProfilesWithCustomDataDirectories: 0,
                    distinctCustomDataDirectoryCount: 0,
                    xmlBridgePosture: ResolveXmlBridgePosture(catalog),
                    xmlBridgeLaneReceipt: BuildXmlBridgeLaneReceipt(
                        ResolveXmlBridgePosture(catalog),
                        CountEnabledDataOverlays(catalog)),
                    enabledDataOverlayCount: CountEnabledDataOverlays(catalog)),
                TranslatorDeterministicReceipt: BuildTranslatorDeterministicReceipt(translatorSummary),
                ImportOracleDeterministicReceipt: BuildImportOracleDeterministicReceipt(importOracleSummary),
                AmendPackageDeterministicReceipt: BuildAmendPackageDeterministicReceipt(amendPackageSummary),
                Sr6SuccessorDeterministicReceipt: BuildSr6SuccessorDeterministicReceipt(
                    ResolveSr6SupplementLanePosture(Array.Empty<MasterIndexSourcebookEntry>()),
                    ResolveSr6DesignerToolsPosture(0, Sr6DesignerFamiliesExpected),
                    0,
                    Sr6DesignerFamiliesExpected,
                    ResolveHouseRuleLanePosture(catalog),
                    CountHouseRuleOverlays(catalog),
                    onlineStorageSummary.LanePosture,
                    onlineStorageSummary.ReceiptPosture,
                    onlineStorageSummary.ReceiptsCovered,
                    onlineStorageSummary.ReceiptsExpected,
                    onlineStorageSummary.CoveragePercent));

        IReadOnlyList<MasterIndexSourcebookEntry> sourcebooks = BuildSourcebookEntries(filesByName);
        string referenceLanePosture = ResolveReferenceLanePosture(sourcebooks);
        int sourcebooksWithSnippets = CountSourcebooksWithSnippets(sourcebooks);
        int sourcebooksMissingSnippets = sourcebooks.Count - sourcebooksWithSnippets;
        int referenceCoveragePercent = CalculateReferenceCoveragePercent(sourcebooks.Count, sourcebooksWithSnippets);
        int sourcebooksWithGovernedReferenceSources = sourcebooks.Count(sourcebook =>
            string.Equals(sourcebook.ReferenceSourcePosture, "governed", StringComparison.Ordinal));
        int sourcebooksWithStaleReferenceSources = sourcebooks.Count(sourcebook =>
            string.Equals(sourcebook.ReferenceSourcePosture, "stale", StringComparison.Ordinal));
        int sourcebooksMissingReferenceSources =
            Math.Max(0, sourcebooks.Count - sourcebooksWithGovernedReferenceSources - sourcebooksWithStaleReferenceSources);
        string referenceSourceLanePosture = ResolveReferenceSourceLanePosture(
            sourcebooks.Count,
            sourcebooksWithStaleReferenceSources,
            sourcebooksMissingReferenceSources);
        var settingsSummary = BuildSettingsCatalogSummary(filesByName, sourcebooks);
        int enabledDataOverlayCount = CountEnabledDataOverlays(catalog);
        string customDataLanePosture = ResolveCustomDataLanePosture(
            settingsSummary.DistinctCustomDataDirectoryCount,
            enabledDataOverlayCount);
        int sr6DesignerFamiliesAvailable = CountSr6DesignerFamiliesAvailable(filesByName);
        string sr6SupplementLanePosture = ResolveSr6SupplementLanePosture(sourcebooks);
        string sr6DesignerToolsPosture = ResolveSr6DesignerToolsPosture(sr6DesignerFamiliesAvailable, Sr6DesignerFamiliesExpected);
        int houseRuleOverlayCount = CountHouseRuleOverlays(catalog);
        string houseRuleLanePosture = ResolveHouseRuleLanePosture(catalog);

        List<MasterIndexFileEntry> files = new();
        foreach ((string fileName, XDocument? document) in filesByName.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (document is null)
            {
                files.Add(new MasterIndexFileEntry(
                    File: fileName,
                    Root: string.Empty,
                    ElementCount: 0));
                continue;
            }

            files.Add(new MasterIndexFileEntry(
                File: fileName,
                Root: document.Root?.Name.LocalName ?? string.Empty,
                ElementCount: document.Descendants().Count()));
        }

        return new MasterIndexResponse(
            Count: files.Count,
            GeneratedUtc: DateTimeOffset.UtcNow,
            Files: files,
            ReferenceLanePosture: referenceLanePosture,
            ReferenceLaneReceipt: BuildReferenceLaneReceipt(sourcebooks.Count, sourcebooksWithSnippets),
            SourcebookCount: sourcebooks.Count,
            Sourcebooks: sourcebooks,
            SourcebooksWithSnippets: sourcebooksWithSnippets,
            SourcebooksMissingSnippets: sourcebooksMissingSnippets,
            ReferenceCoveragePercent: referenceCoveragePercent,
            ReferenceSourceLanePosture: referenceSourceLanePosture,
            SourcebooksWithGovernedReferenceSources: sourcebooksWithGovernedReferenceSources,
            SourcebooksWithStaleReferenceSources: sourcebooksWithStaleReferenceSources,
            SourcebooksMissingReferenceSources: sourcebooksMissingReferenceSources,
            ReferenceSourceLaneReceipt: BuildReferenceSourceLaneReceipt(
                sourcebooks.Count,
                sourcebooksWithGovernedReferenceSources,
                sourcebooksWithStaleReferenceSources),
            SettingsLanePosture: settingsSummary.SettingsLanePosture,
            SettingsProfileCount: settingsSummary.ProfileCount,
            SettingsProfilesWithSourceToggles: settingsSummary.ProfilesWithSourceToggles,
            DistinctSourcebookToggles: settingsSummary.DistinctSourcebookToggles,
            SourceToggleLanePosture: settingsSummary.SourceToggleLanePosture,
            SourceToggleLaneReceipt: BuildSourceToggleLaneReceipt(settingsSummary),
            SourceSelectionLaneReceipt: BuildSourceSelectionLaneReceipt(
                sourcebooks.Count,
                settingsSummary.ProfileCount,
                settingsSummary.ProfilesWithSourceToggles,
                settingsSummary.DistinctSourcebookToggles,
                settingsSummary.SourcebookToggleCoveragePercent,
                settingsSummary.SourceToggleLanePosture),
            SettingsLaneReceipt: BuildSettingsLaneReceipt(settingsSummary),
            SourcebookToggleCoveragePercent: settingsSummary.SourcebookToggleCoveragePercent,
            CustomDataLanePosture: customDataLanePosture,
            CustomDataLaneReceipt: BuildCustomDataLaneReceipt(
                customDataLanePosture,
                settingsSummary.DistinctCustomDataDirectoryCount,
                enabledDataOverlayCount),
            CustomDataAuthoringLaneReceipt: BuildCustomDataAuthoringLaneReceipt(
                customDataLanePosture,
                settingsSummary.DistinctCustomDataDirectoryCount,
                enabledDataOverlayCount),
            SettingsProfilesWithCustomDataDirectories: settingsSummary.ProfilesWithCustomDataDirectories,
            DistinctCustomDataDirectoryCount: settingsSummary.DistinctCustomDataDirectoryCount,
            XmlBridgePosture: ResolveXmlBridgePosture(catalog),
            XmlBridgeLaneReceipt: BuildXmlBridgeLaneReceipt(
                ResolveXmlBridgePosture(catalog),
                enabledDataOverlayCount),
            EnabledDataOverlayCount: enabledDataOverlayCount,
            TranslatorLanePosture: translatorSummary.LanePosture,
            TranslatorLaneReceipt: BuildTranslatorLaneReceipt(translatorSummary),
            TranslatorBridgePosture: translatorSummary.BridgePosture,
            TranslatorLanguageCount: translatorSummary.LanguageCount,
            EnabledLanguageOverlayCount: translatorSummary.EnabledOverlayCount,
            Sr6SupplementLanePosture: sr6SupplementLanePosture,
            Sr6DesignerToolsPosture: sr6DesignerToolsPosture,
            Sr6DesignerFamiliesAvailable: sr6DesignerFamiliesAvailable,
            Sr6DesignerFamiliesExpected: Sr6DesignerFamiliesExpected,
            HouseRuleLanePosture: houseRuleLanePosture,
            HouseRuleOverlayCount: houseRuleOverlayCount,
            OnlineStorageLanePosture: onlineStorageSummary.LanePosture,
            OnlineStorageReceiptPosture: onlineStorageSummary.ReceiptPosture,
            OnlineStorageLaneReceipt: BuildOnlineStorageLaneReceipt(onlineStorageSummary),
            OnlineStorageReceiptsCovered: onlineStorageSummary.ReceiptsCovered,
            OnlineStorageReceiptsExpected: onlineStorageSummary.ReceiptsExpected,
            OnlineStorageCoveragePercent: onlineStorageSummary.CoveragePercent,
            ImportOracleLanePosture: importOracleSummary.LanePosture,
            ImportOracleReceiptPosture: importOracleSummary.ReceiptPosture,
            LegacyChummer4FixtureCount: importOracleSummary.LegacyChummer4FixtureCount,
            LegacyChummer5FixtureCount: importOracleSummary.LegacyChummer5FixtureCount,
            HeroLabFixtureCount: importOracleSummary.HeroLabFixtureCount,
            AdjacentSr6OracleReceiptPosture: importOracleSummary.AdjacentSr6OracleReceiptPosture,
            AdjacentSr6OracleSourcesCovered: importOracleSummary.AdjacentSr6OracleSourcesCovered,
            AdjacentSr6OracleSourcesExpected: importOracleSummary.AdjacentSr6OracleSourcesExpected,
            ImportOracleSourcesCovered: importOracleSummary.SourcesCovered,
            ImportOracleSourcesExpected: importOracleSummary.SourcesExpected,
            ImportOracleCoveragePercent: importOracleSummary.CoveragePercent,
            ImportOracleMissingSources: importOracleSummary.MissingSources,
            ImportOracleLaneReceipt: BuildImportOracleLaneReceipt(importOracleSummary),
            AdjacentSr6OracleLaneReceipt: BuildAdjacentSr6OracleLaneReceipt(importOracleSummary),
            Sr6SuccessorLaneReceipt: BuildSr6SuccessorLaneReceipt(
                sr6SupplementLanePosture,
                sr6DesignerToolsPosture,
                sr6DesignerFamiliesAvailable,
                Sr6DesignerFamiliesExpected,
                houseRuleLanePosture,
                houseRuleOverlayCount,
                onlineStorageSummary.LanePosture,
                onlineStorageSummary.ReceiptsCovered,
                onlineStorageSummary.ReceiptsExpected),
            CustomDataXmlBridgeDeterministicReceipt: BuildCustomDataXmlBridgeDeterministicReceipt(
                customDataLanePosture,
                BuildCustomDataLaneReceipt(
                    customDataLanePosture,
                    settingsSummary.DistinctCustomDataDirectoryCount,
                    enabledDataOverlayCount),
                BuildCustomDataAuthoringLaneReceipt(
                    customDataLanePosture,
                    settingsSummary.DistinctCustomDataDirectoryCount,
                    enabledDataOverlayCount),
                settingsSummary.ProfilesWithCustomDataDirectories,
                settingsSummary.DistinctCustomDataDirectoryCount,
                ResolveXmlBridgePosture(catalog),
                BuildXmlBridgeLaneReceipt(
                    ResolveXmlBridgePosture(catalog),
                    enabledDataOverlayCount),
                enabledDataOverlayCount),
            TranslatorDeterministicReceipt: BuildTranslatorDeterministicReceipt(translatorSummary),
            ImportOracleDeterministicReceipt: BuildImportOracleDeterministicReceipt(importOracleSummary),
            AmendPackageDeterministicReceipt: BuildAmendPackageDeterministicReceipt(amendPackageSummary),
            Sr6SuccessorDeterministicReceipt: BuildSr6SuccessorDeterministicReceipt(
                sr6SupplementLanePosture,
                sr6DesignerToolsPosture,
                sr6DesignerFamiliesAvailable,
                Sr6DesignerFamiliesExpected,
                houseRuleLanePosture,
                houseRuleOverlayCount,
                onlineStorageSummary.LanePosture,
                onlineStorageSummary.ReceiptPosture,
                onlineStorageSummary.ReceiptsCovered,
                onlineStorageSummary.ReceiptsExpected,
                onlineStorageSummary.CoveragePercent));
    }

    public TranslatorLanguagesResponse GetTranslatorLanguages()
    {
        ContentOverlayCatalog catalog = _overlays.GetCatalog();
        IReadOnlyDictionary<string, XDocument?> filesByName = BuildEffectiveDocuments(
            catalog,
            catalog.BaseLanguagePath,
            pack => pack.LanguagePath);
        if (filesByName.Count == 0)
            return new TranslatorLanguagesResponse(
                0,
                Array.Empty<TranslatorLanguageEntry>(),
                TranslatorBridgePosture: ResolveTranslatorBridgePosture(catalog),
                EnabledLanguageOverlayCount: CountEnabledLanguageOverlays(catalog));

        List<TranslatorLanguageEntry> languages = new();
        Dictionary<string, XDocument?> filesByCode = CollapseLanguageFilesByCode(filesByName);
        foreach ((string code, XDocument? languageDocument) in filesByCode.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            string name = code;
            if (languageDocument is not null)
            {
                name = languageDocument.Root?.Element("name")?.Value?.Trim() ?? code;
            }

            languages.Add(new TranslatorLanguageEntry(
                Code: code,
                Name: name));
        }

        return new TranslatorLanguagesResponse(
            Count: languages.Count,
            Languages: languages,
            TranslatorBridgePosture: ResolveTranslatorBridgePosture(catalog),
            EnabledLanguageOverlayCount: CountEnabledLanguageOverlays(catalog));
    }

    private static string ResolveXmlBridgePosture(ContentOverlayCatalog catalog)
    {
        int enabledOverlayCount = CountEnabledDataOverlays(catalog);
        if (enabledOverlayCount == 0)
        {
            return "missing";
        }

        return CountOverlayXmlFiles(catalog, pack => pack.DataPath) > 0
            ? "governed"
            : "stale";
    }

    private static string ResolveReferenceLanePosture(IReadOnlyList<MasterIndexSourcebookEntry> sourcebooks)
    {
        if (sourcebooks.Count == 0)
        {
            return "missing";
        }

        bool hasSnippetGaps = sourcebooks.Any(sourcebook =>
            string.Equals(sourcebook.ReferencePosture, "no-snippets", StringComparison.Ordinal));
        return hasSnippetGaps ? "stale" : "governed";
    }

    private static int CountSourcebooksWithSnippets(IReadOnlyList<MasterIndexSourcebookEntry> sourcebooks)
    {
        return sourcebooks.Count(sourcebook =>
            string.Equals(sourcebook.ReferencePosture, "matched-snippets", StringComparison.Ordinal)
            && sourcebook.RuleSnippetCount > 0);
    }

    private static int CalculateReferenceCoveragePercent(int sourcebookCount, int sourcebooksWithSnippets)
    {
        if (sourcebookCount <= 0)
        {
            return 0;
        }

        return (int)Math.Round(sourcebooksWithSnippets * 100d / sourcebookCount, MidpointRounding.AwayFromZero);
    }

    private static string ResolveReferenceSourceLanePosture(
        int sourcebookCount,
        int sourcebooksWithStaleReferenceSources,
        int sourcebooksMissingReferenceSources)
    {
        if (sourcebookCount <= 0)
        {
            return "missing";
        }

        return sourcebooksWithStaleReferenceSources > 0 || sourcebooksMissingReferenceSources > 0
            ? "stale"
            : "governed";
    }

    private static SettingsCatalogSummary BuildSettingsCatalogSummary(
        IReadOnlyDictionary<string, XDocument?> filesByName,
        IReadOnlyList<MasterIndexSourcebookEntry> sourcebooks)
    {
        if (!filesByName.TryGetValue("settings.xml", out XDocument? settingsDocument) || settingsDocument?.Root is null)
        {
            return new SettingsCatalogSummary(
                ProfileCount: 0,
                ProfilesWithSourceToggles: 0,
                DistinctSourcebookToggles: 0,
                SettingsLanePosture: "missing",
                SourceToggleLanePosture: "missing",
                SourcebookToggleCoveragePercent: 0,
                ProfilesWithCustomDataDirectories: 0,
                DistinctCustomDataDirectoryCount: 0);
        }

        IEnumerable<XElement> profileNodes = settingsDocument.Root
            .Element("settings")?
            .Elements("setting")
            ?? Enumerable.Empty<XElement>();

        int profileCount = 0;
        int profilesWithSourceToggles = 0;
        HashSet<string> distinctToggles = new(StringComparer.OrdinalIgnoreCase);
        int profilesWithCustomDataDirectories = 0;
        HashSet<string> distinctCustomDataDirectories = new(StringComparer.OrdinalIgnoreCase);
        foreach (XElement profileNode in profileNodes)
        {
            profileCount++;
            HashSet<string> profileBooks = profileNode
                .Element("books")?
                .Elements("book")
                .Select(book => book.Value?.Trim() ?? string.Empty)
                .Where(static code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            HashSet<string> profileCustomDataDirectories = profileNode
                .Element("customdatadirectorynames")?
                .Elements("customdatadirectoryname")
                .Where(entry => ParseBool(ReadChildValue(entry, "enabled")) || entry.Element("enabled") is null)
                .Select(entry => ReadChildValue(entry, "directoryname"))
                .Where(static directoryName => !string.IsNullOrWhiteSpace(directoryName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (profileCustomDataDirectories.Count > 0)
            {
                profilesWithCustomDataDirectories++;
                distinctCustomDataDirectories.UnionWith(profileCustomDataDirectories);
            }

            if (profileBooks.Count == 0)
            {
                continue;
            }

            profilesWithSourceToggles++;
            distinctToggles.UnionWith(profileBooks);
        }

        string settingsLanePosture = profileCount <= 0
            ? "missing"
            : profilesWithSourceToggles > 0
                ? "governed"
                : "stale";
        if (distinctToggles.Count == 0)
        {
            return new SettingsCatalogSummary(
                ProfileCount: profileCount,
                ProfilesWithSourceToggles: profilesWithSourceToggles,
                DistinctSourcebookToggles: 0,
                SettingsLanePosture: settingsLanePosture,
                SourceToggleLanePosture: "missing",
                SourcebookToggleCoveragePercent: 0,
                ProfilesWithCustomDataDirectories: profilesWithCustomDataDirectories,
                DistinctCustomDataDirectoryCount: distinctCustomDataDirectories.Count);
        }

        HashSet<string> knownSourcebooks = sourcebooks
            .Select(sourcebook => sourcebook.Code?.Trim() ?? string.Empty)
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int knownToggleCount = distinctToggles.Count(code => knownSourcebooks.Contains(code));
        bool hasUnknownToggle = distinctToggles.Any(code => !knownSourcebooks.Contains(code));
        string sourceToggleLanePosture =
            knownSourcebooks.Count == 0 || hasUnknownToggle
                ? "stale"
                : "governed";
        int sourcebookToggleCoveragePercent = knownSourcebooks.Count <= 0
            ? 0
            : (int)Math.Round(knownToggleCount * 100d / knownSourcebooks.Count, MidpointRounding.AwayFromZero);

        return new SettingsCatalogSummary(
            ProfileCount: profileCount,
            ProfilesWithSourceToggles: profilesWithSourceToggles,
            DistinctSourcebookToggles: distinctToggles.Count,
            SettingsLanePosture: settingsLanePosture,
            SourceToggleLanePosture: sourceToggleLanePosture,
            SourcebookToggleCoveragePercent: sourcebookToggleCoveragePercent,
            ProfilesWithCustomDataDirectories: profilesWithCustomDataDirectories,
            DistinctCustomDataDirectoryCount: distinctCustomDataDirectories.Count);
    }

    private static string ResolveCustomDataLanePosture(int distinctCustomDataDirectoryCount, int enabledDataOverlayCount)
    {
        if (distinctCustomDataDirectoryCount <= 0)
        {
            return "missing";
        }

        return enabledDataOverlayCount > 0
            ? "governed"
            : "stale";
    }

    private static string BuildReferenceLaneReceipt(int sourcebookCount, int sourcebooksWithSnippets)
    {
        if (sourcebookCount <= 0)
        {
            return "No sourcebooks were discovered in books.xml.";
        }

        if (sourcebooksWithSnippets <= 0)
        {
            return $"All {sourcebookCount} sourcebooks are missing rule snippets.";
        }

        if (sourcebooksWithSnippets >= sourcebookCount)
        {
            return $"All {sourcebookCount} sourcebooks include rule snippets.";
        }

        int sourcebooksMissingSnippets = sourcebookCount - sourcebooksWithSnippets;
        return $"{sourcebooksMissingSnippets} of {sourcebookCount} sourcebooks are missing rule snippets.";
    }

    private static string BuildReferenceSourceLaneReceipt(
        int sourcebookCount,
        int sourcebooksWithGovernedReferenceSources,
        int sourcebooksWithStaleReferenceSources)
    {
        if (sourcebookCount <= 0)
        {
            return "No sourcebook reference sources were discovered (PDF, URL, or site snapshot).";
        }

        int sourcebooksMissingReferenceSources = Math.Max(
            0,
            sourcebookCount - sourcebooksWithGovernedReferenceSources - sourcebooksWithStaleReferenceSources);
        if (sourcebooksWithGovernedReferenceSources >= sourcebookCount)
        {
            return $"All {sourcebookCount} sourcebooks have governed PDF, URL, or site-snapshot references.";
        }

        return
            $"{sourcebooksWithGovernedReferenceSources} governed, {sourcebooksWithStaleReferenceSources} stale, and {sourcebooksMissingReferenceSources} missing sourcebook reference sources (PDF, URL, or site snapshot).";
    }

    private static string BuildSettingsLaneReceipt(SettingsCatalogSummary summary)
    {
        if (summary.ProfileCount <= 0)
        {
            return "No settings profiles were discovered in settings.xml.";
        }

        if (summary.ProfilesWithSourceToggles <= 0)
        {
            return $"{summary.ProfileCount} settings profiles were discovered, but none define sourcebook toggles.";
        }

        return $"{summary.ProfilesWithSourceToggles} of {summary.ProfileCount} settings profiles define sourcebook toggles.";
    }

    private static string BuildSourceToggleLaneReceipt(SettingsCatalogSummary summary)
    {
        if (summary.DistinctSourcebookToggles <= 0)
        {
            return "No sourcebook toggle codes were discovered in settings.xml profiles.";
        }

        return string.Equals(summary.SourceToggleLanePosture, "governed", StringComparison.Ordinal)
            ? $"{summary.DistinctSourcebookToggles} toggle codes map to known sourcebooks ({summary.SourcebookToggleCoveragePercent}% catalog coverage)."
            : $"{summary.DistinctSourcebookToggles} toggle codes were discovered, but one or more codes do not map to known sourcebooks.";
    }

    private static string BuildSourceSelectionLaneReceipt(
        int sourcebookCount,
        int profileCount,
        int profilesWithSourceToggles,
        int distinctSourcebookToggles,
        int sourcebookToggleCoveragePercent,
        string sourceToggleLanePosture)
    {
        if (sourcebookCount <= 0)
        {
            return "No sourcebook catalog was discovered to project source selection posture.";
        }

        if (profileCount <= 0)
        {
            return "No settings profiles were discovered to project source selection posture.";
        }

        if (distinctSourcebookToggles <= 0)
        {
            return $"{profileCount} settings profiles were discovered, but none define source selection toggles.";
        }

        return string.Equals(sourceToggleLanePosture, "governed", StringComparison.Ordinal)
            ? $"{profilesWithSourceToggles} of {profileCount} settings profiles project source selection with {distinctSourcebookToggles} sourcebook toggles ({sourcebookToggleCoveragePercent}% catalog coverage)."
            : $"{profilesWithSourceToggles} of {profileCount} settings profiles project source selection, but one or more sourcebook toggles do not map to the catalog.";
    }

    private static CustomDataXmlBridgeDeterministicReceipt BuildCustomDataXmlBridgeDeterministicReceipt(
        string customDataLanePosture,
        string customDataLaneReceipt,
        string customDataAuthoringLaneReceipt,
        int settingsProfilesWithCustomDataDirectories,
        int distinctCustomDataDirectoryCount,
        string xmlBridgePosture,
        string xmlBridgeLaneReceipt,
        int enabledDataOverlayCount)
    {
        return new CustomDataXmlBridgeDeterministicReceipt(
            ParityFamilyId: CustomDataXmlBridgeParityFamilyId,
            CustomDataLanePosture: customDataLanePosture,
            CustomDataLaneReceipt: customDataLaneReceipt,
            CustomDataAuthoringLaneReceipt: customDataAuthoringLaneReceipt,
            SettingsProfilesWithCustomDataDirectories: settingsProfilesWithCustomDataDirectories,
            DistinctCustomDataDirectoryCount: distinctCustomDataDirectoryCount,
            XmlBridgePosture: xmlBridgePosture,
            XmlBridgeLaneReceipt: xmlBridgeLaneReceipt,
            EnabledDataOverlayCount: enabledDataOverlayCount);
    }

    private static TranslatorLaneDeterministicReceipt BuildTranslatorDeterministicReceipt(TranslatorCatalogSummary summary)
    {
        return new TranslatorLaneDeterministicReceipt(
            ParityRouteId: TranslatorParityRouteId,
            TranslatorLanePosture: summary.LanePosture,
            TranslatorLaneReceipt: BuildTranslatorLaneReceipt(summary),
            TranslatorBridgePosture: summary.BridgePosture,
            TranslatorLanguageCount: summary.LanguageCount,
            EnabledLanguageOverlayCount: summary.EnabledOverlayCount);
    }

    private static ImportOracleLaneDeterministicReceipt BuildImportOracleDeterministicReceipt(ImportOracleSummary summary)
    {
        return new ImportOracleLaneDeterministicReceipt(
            ParityFamilyId: ImportOracleParityFamilyId,
            ImportOracleLanePosture: summary.LanePosture,
            ImportOracleLaneReceipt: BuildImportOracleLaneReceipt(summary),
            ImportOracleReceiptPosture: summary.ReceiptPosture,
            LegacyChummer4FixtureCount: summary.LegacyChummer4FixtureCount,
            LegacyChummer5FixtureCount: summary.LegacyChummer5FixtureCount,
            HeroLabFixtureCount: summary.HeroLabFixtureCount,
            AdjacentSr6OracleReceiptPosture: summary.AdjacentSr6OracleReceiptPosture,
            AdjacentSr6OracleSourcesCovered: summary.AdjacentSr6OracleSourcesCovered,
            AdjacentSr6OracleSourcesExpected: summary.AdjacentSr6OracleSourcesExpected,
            ImportOracleSourcesCovered: summary.SourcesCovered,
            ImportOracleSourcesExpected: summary.SourcesExpected,
            ImportOracleCoveragePercent: summary.CoveragePercent,
            ImportOracleMissingSources: summary.MissingSources.ToArray(),
            AdjacentSr6OracleLaneReceipt: BuildAdjacentSr6OracleLaneReceipt(summary));
    }

    private static AmendPackageDeterministicReceipt BuildAmendPackageDeterministicReceipt(AmendPackageSummary summary)
    {
        return new AmendPackageDeterministicReceipt(
            ProofContractName: EngineProofPackContractName,
            ProofReceiptPath: summary.ProofReceiptPath,
            LanePosture: summary.LanePosture,
            LaneReceipt: BuildAmendPackageLaneReceipt(summary),
            ProofPackStatus: summary.ProofPackStatus,
            SourceToggleSuiteStatus: summary.SourceToggleSuiteStatus,
            AmendPackageSuiteStatus: summary.AmendPackageSuiteStatus,
            AmendPackageCoverageFocus: summary.AmendPackageCoverageFocus,
            RequiredSuiteIds: RequiredAmendPackageSuiteIds);
    }

    private static Sr6SuccessorLaneDeterministicReceipt BuildSr6SuccessorDeterministicReceipt(
        string sr6SupplementLanePosture,
        string sr6DesignerToolsPosture,
        int sr6DesignerFamiliesAvailable,
        int sr6DesignerFamiliesExpected,
        string houseRuleLanePosture,
        int houseRuleOverlayCount,
        string onlineStorageLanePosture,
        string onlineStorageReceiptPosture,
        int onlineStorageReceiptsCovered,
        int onlineStorageReceiptsExpected,
        int onlineStorageCoveragePercent)
    {
        return new Sr6SuccessorLaneDeterministicReceipt(
            ParityFamilyId: "family:sr6_supplements_designers_and_house_rules",
            Sr6SupplementLanePosture: sr6SupplementLanePosture,
            Sr6SuccessorLaneReceipt: BuildSr6SuccessorLaneReceipt(
                sr6SupplementLanePosture,
                sr6DesignerToolsPosture,
                sr6DesignerFamiliesAvailable,
                sr6DesignerFamiliesExpected,
                houseRuleLanePosture,
                houseRuleOverlayCount,
                onlineStorageLanePosture,
                onlineStorageReceiptsCovered,
                onlineStorageReceiptsExpected),
            Sr6DesignerToolsPosture: sr6DesignerToolsPosture,
            Sr6DesignerFamiliesAvailable: sr6DesignerFamiliesAvailable,
            Sr6DesignerFamiliesExpected: sr6DesignerFamiliesExpected,
            HouseRuleLanePosture: houseRuleLanePosture,
            HouseRuleOverlayCount: houseRuleOverlayCount,
            OnlineStorageLanePosture: onlineStorageLanePosture,
            OnlineStorageReceiptPosture: onlineStorageReceiptPosture,
            OnlineStorageReceiptsCovered: onlineStorageReceiptsCovered,
            OnlineStorageReceiptsExpected: onlineStorageReceiptsExpected,
            OnlineStorageCoveragePercent: onlineStorageCoveragePercent);
    }

    private static string BuildCustomDataLaneReceipt(string customDataLanePosture, int distinctCustomDataDirectoryCount, int enabledDataOverlayCount)
    {
        if (string.Equals(customDataLanePosture, "missing", StringComparison.Ordinal))
        {
            return "No enabled custom data directory entries were discovered in settings.xml.";
        }

        if (string.Equals(customDataLanePosture, "stale", StringComparison.Ordinal))
        {
            return $"{distinctCustomDataDirectoryCount} custom data directories are referenced, but no enabled data overlay bridge was detected.";
        }

        return $"{distinctCustomDataDirectoryCount} custom data directories are backed by {enabledDataOverlayCount} enabled data overlay bridge(s).";
    }

    private static string BuildCustomDataAuthoringLaneReceipt(
        string customDataLanePosture,
        int distinctCustomDataDirectoryCount,
        int enabledDataOverlayCount)
    {
        if (string.Equals(customDataLanePosture, "missing", StringComparison.Ordinal))
        {
            return "No custom-data authoring directories were discovered in settings.xml.";
        }

        if (string.Equals(customDataLanePosture, "stale", StringComparison.Ordinal))
        {
            return $"{distinctCustomDataDirectoryCount} custom-data authoring directories are referenced, but no enabled data overlay bridge was detected.";
        }

        return $"{distinctCustomDataDirectoryCount} custom-data authoring directories are backed by {enabledDataOverlayCount} enabled data overlay bridge(s).";
    }

    private static string BuildXmlBridgeLaneReceipt(string xmlBridgePosture, int enabledDataOverlayCount)
    {
        if (string.Equals(xmlBridgePosture, "missing", StringComparison.Ordinal))
        {
            return "No enabled data overlay bridge was detected for XML bridge continuity.";
        }

        if (string.Equals(xmlBridgePosture, "stale", StringComparison.Ordinal))
        {
            return $"{enabledDataOverlayCount} enabled data overlay bridge(s) were detected, but no overlay XML payloads were discovered.";
        }

        return $"{enabledDataOverlayCount} enabled data overlay bridge(s) with XML payload coverage were detected.";
    }

    private static string BuildTranslatorLaneReceipt(TranslatorCatalogSummary summary)
    {
        if (string.Equals(summary.LanePosture, "missing", StringComparison.Ordinal))
        {
            return "No translator language corpus or language overlay bridge was detected.";
        }

        if (string.Equals(summary.LanePosture, "stale", StringComparison.Ordinal))
        {
            if (summary.LanguageCount <= 0)
            {
                return "Language overlays are enabled, but no translator language corpus files were discovered.";
            }

            return $"{summary.LanguageCount} translator language files were discovered, but language overlay bridge posture is {summary.BridgePosture}.";
        }

        return $"{summary.LanguageCount} translator language files and {summary.EnabledOverlayCount} enabled language overlay bridge(s) were detected.";
    }

    private static string BuildImportOracleLaneReceipt(ImportOracleSummary summary)
    {
        string missingSourcesSuffix = summary.MissingSources.Count > 0
            ? $" Missing sources: {string.Join(", ", summary.MissingSources)}."
            : string.Empty;
        if (string.Equals(summary.LanePosture, "missing", StringComparison.Ordinal))
        {
            return $"No import oracle fixtures or certification receipt were discovered.{missingSourcesSuffix}";
        }

        return $"Import oracle coverage is {summary.SourcesCovered}/{summary.SourcesExpected} with certification receipt posture {summary.ReceiptPosture} and adjacent SR6 oracle posture {summary.AdjacentSr6OracleReceiptPosture}.{missingSourcesSuffix}";
    }

    private static string BuildAdjacentSr6OracleLaneReceipt(ImportOracleSummary summary)
    {
        if (string.Equals(summary.AdjacentSr6OracleReceiptPosture, "missing", StringComparison.Ordinal))
        {
            return "No adjacent SR6 oracle certification receipt coverage was discovered for Genesis/CommLink6.";
        }

        return $"Adjacent SR6 oracle coverage is {summary.AdjacentSr6OracleSourcesCovered}/{summary.AdjacentSr6OracleSourcesExpected} with receipt posture {summary.AdjacentSr6OracleReceiptPosture} (Genesis/CommLink6).";
    }

    private static string BuildAmendPackageLaneReceipt(AmendPackageSummary summary)
    {
        if (string.Equals(summary.LanePosture, "missing", StringComparison.Ordinal))
        {
            return "No engine proof pack receipt was discovered for the source-toggle and amend-package suites.";
        }

        return $"Engine proof pack status is {summary.ProofPackStatus}; source-toggle suite is {summary.SourceToggleSuiteStatus}; amend-package suite is {summary.AmendPackageSuiteStatus} ({summary.AmendPackageCoverageFocus}).";
    }

    private static string BuildOnlineStorageLaneReceipt(OnlineStorageSummary summary)
    {
        if (string.Equals(summary.LanePosture, "missing", StringComparison.Ordinal))
        {
            return "No online-storage continuity receipts were discovered for Hub/mobile install-restore lanes.";
        }

        return $"Online-storage continuity receipts cover {summary.ReceiptsCovered}/{summary.ReceiptsExpected} lanes with receipt posture {summary.ReceiptPosture}.";
    }

    private static string BuildSr6SuccessorLaneReceipt(
        string sr6SupplementLanePosture,
        string sr6DesignerToolsPosture,
        int sr6DesignerFamiliesAvailable,
        int sr6DesignerFamiliesExpected,
        string houseRuleLanePosture,
        int houseRuleOverlayCount,
        string onlineStorageLanePosture,
        int onlineStorageReceiptsCovered,
        int onlineStorageReceiptsExpected)
    {
        return $"Supplement posture is {sr6SupplementLanePosture}; designer families are {sr6DesignerFamiliesAvailable}/{sr6DesignerFamiliesExpected} ({sr6DesignerToolsPosture}); house-rule overlays are {houseRuleOverlayCount} ({houseRuleLanePosture}); online-storage receipts are {onlineStorageReceiptsCovered}/{onlineStorageReceiptsExpected} ({onlineStorageLanePosture}).";
    }

    private static TranslatorCatalogSummary BuildTranslatorCatalogSummary(
        ContentOverlayCatalog catalog,
        IReadOnlyDictionary<string, XDocument?> languageFilesByName)
    {
        int enabledLanguageOverlayCount = CountEnabledLanguageOverlays(catalog);
        string translatorBridgePosture = ResolveTranslatorBridgePosture(catalog);
        int translatorLanguageCount = CollapseLanguageFilesByCode(languageFilesByName).Count;
        string lanePosture =
            translatorLanguageCount <= 0
                ? enabledLanguageOverlayCount > 0 ? "stale" : "missing"
                : string.Equals(translatorBridgePosture, "governed", StringComparison.Ordinal)
                    ? "governed"
                    : "stale";

        return new TranslatorCatalogSummary(
            LanePosture: lanePosture,
            BridgePosture: translatorBridgePosture,
            LanguageCount: translatorLanguageCount,
            EnabledOverlayCount: enabledLanguageOverlayCount);
    }

    private static int CountEnabledDataOverlays(ContentOverlayCatalog catalog)
    {
        return catalog.Overlays.Count(pack =>
            pack.Enabled
            && !string.IsNullOrWhiteSpace(pack.DataPath)
            && Directory.Exists(pack.DataPath));
    }

    private static string ResolveSr6SupplementLanePosture(IReadOnlyList<MasterIndexSourcebookEntry> sourcebooks)
    {
        if (sourcebooks.Count == 0)
        {
            return "missing";
        }

        return sourcebooks.Any(sourcebook =>
                string.Equals(sourcebook.ReferencePosture, "no-snippets", StringComparison.Ordinal)
                && !IsSupplementSnippetExempt(sourcebook))
            ? "stale"
            : "governed";
    }

    private static bool IsSupplementSnippetExempt(MasterIndexSourcebookEntry sourcebook)
    {
        return sourcebook.Name.Contains("(German-Only)", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountSr6DesignerFamiliesAvailable(IReadOnlyDictionary<string, XDocument?> filesByName)
    {
        return Sr6DesignerCatalogFiles.Count(fileName => filesByName.ContainsKey(fileName));
    }

    private static string ResolveSr6DesignerToolsPosture(int familiesAvailable, int familiesExpected)
    {
        if (familiesAvailable <= 0 || familiesExpected <= 0)
        {
            return "missing";
        }

        return familiesAvailable >= familiesExpected
            ? "governed"
            : "stale";
    }

    private static string ResolveHouseRuleLanePosture(ContentOverlayCatalog catalog)
    {
        int houseRuleOverlayCount = CountHouseRuleOverlays(catalog);
        if (houseRuleOverlayCount == 0)
        {
            return "missing";
        }

        return CountOverlayXmlFiles(catalog, pack => pack.DataPath) > 0
            ? "governed"
            : "stale";
    }

    private static OnlineStorageSummary BuildOnlineStorageSummary(string baseDataPath)
    {
        const int receiptsExpected = 2;
        string? evidenceRoot = TryResolveOnlineStorageEvidenceRoot(baseDataPath);
        if (string.IsNullOrWhiteSpace(evidenceRoot))
        {
            return new OnlineStorageSummary(
                LanePosture: "missing",
                ReceiptPosture: "missing",
                ReceiptsCovered: 0,
                ReceiptsExpected: receiptsExpected,
                CoveragePercent: 0);
        }

        string hubReceiptPath = Path.Combine(
            evidenceRoot,
            "chummer.run-services",
            ".codex-studio",
            "published",
            "HUB_LOCAL_RELEASE_PROOF.generated.json");
        string mobileReceiptPath = Path.Combine(
            evidenceRoot,
            "chummer-play",
            ".codex-studio",
            "published",
            "MOBILE_LOCAL_RELEASE_PROOF.generated.json");
        bool hubReceiptCovered = IsOnlineStorageContinuityReceiptCovered(hubReceiptPath);
        bool mobileReceiptCovered = IsOnlineStorageContinuityReceiptCovered(mobileReceiptPath);
        int receiptsCovered = (hubReceiptCovered ? 1 : 0) + (mobileReceiptCovered ? 1 : 0);
        int receiptsFound = (File.Exists(hubReceiptPath) ? 1 : 0) + (File.Exists(mobileReceiptPath) ? 1 : 0);
        int coveragePercent = (int)Math.Round(receiptsCovered * 100d / receiptsExpected, MidpointRounding.AwayFromZero);

        string receiptPosture = receiptsFound <= 0
            ? "missing"
            : receiptsCovered >= receiptsExpected
                ? "governed"
                : "stale";
        string lanePosture = receiptsCovered <= 0
            ? "missing"
            : receiptsCovered >= receiptsExpected
                ? "governed"
                : "stale";
        return new OnlineStorageSummary(
            LanePosture: lanePosture,
            ReceiptPosture: receiptPosture,
            ReceiptsCovered: receiptsCovered,
            ReceiptsExpected: receiptsExpected,
            CoveragePercent: coveragePercent);
    }

    private static bool IsOnlineStorageContinuityReceiptCovered(string receiptPath)
    {
        if (!File.Exists(receiptPath))
        {
            return false;
        }

        try
        {
            JsonNode? payload = JsonNode.Parse(File.ReadAllText(receiptPath));
            string status = payload?["status"]?.GetValue<string>()?.Trim() ?? string.Empty;
            if (!string.Equals(status, "passed", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            JsonArray? journeysPassed = payload?["journeys_passed"] as JsonArray;
            return journeysPassed?.OfType<JsonNode>().Any(journeyNode =>
                string.Equals(
                    journeyNode?.GetValue<string>()?.Trim(),
                    "install_claim_restore_continue",
                    StringComparison.Ordinal)) ?? false;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryResolveOnlineStorageEvidenceRoot(string baseDataPath)
    {
        foreach (string candidateRoot in EnumerateEvidenceCandidateRoots(baseDataPath))
        {
            bool hasHubProofPath = Directory.Exists(Path.Combine(candidateRoot, "chummer.run-services", ".codex-studio", "published"));
            bool hasMobileProofPath = Directory.Exists(Path.Combine(candidateRoot, "chummer-play", ".codex-studio", "published"));
            if (hasHubProofPath || hasMobileProofPath)
            {
                return candidateRoot;
            }    
        }

        return null;
    }

    private static IEnumerable<string> EnumerateEvidenceCandidateRoots(string? baseDataPath)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (string? seed in new[] { baseDataPath, Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(seed))
            {
                continue;
            }

            DirectoryInfo? current = new(Path.GetFullPath(seed));
            while (current is not null)
            {
                if (seen.Add(current.FullName))
                {
                    yield return current.FullName;
                }

                current = current.Parent;
            }
        }

        string? repoRoot = Environment.GetEnvironmentVariable("CHUMMER_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            string normalized = Path.GetFullPath(repoRoot);
            if (seen.Add(normalized))
            {
                yield return normalized;
            }
        }

        const string workspaceRoot = "/docker/chummercomplete";
        if (Directory.Exists(workspaceRoot) && seen.Add(workspaceRoot))
        {
            yield return workspaceRoot;
        }

        const string coreEngineRoot = "/docker/chummercomplete/chummer-core-engine";
        if (Directory.Exists(coreEngineRoot) && seen.Add(coreEngineRoot))
        {
            yield return coreEngineRoot;
        }
    }

    private static ImportOracleSummary BuildImportOracleSummary(string baseDataPath)
    {
        const int sourcesExpected = 4;
        const int adjacentSourcesExpected = 2;
        string? oracleRoot = TryResolveImportOracleRoot(baseDataPath);
        if (string.IsNullOrWhiteSpace(oracleRoot))
        {
            return new ImportOracleSummary(
                LanePosture: "missing",
                ReceiptPosture: "missing",
                LegacyChummer4FixtureCount: 0,
                LegacyChummer5FixtureCount: 0,
                HeroLabFixtureCount: 0,
                AdjacentSr6OracleReceiptPosture: "missing",
                AdjacentSr6OracleSourcesCovered: 0,
                AdjacentSr6OracleSourcesExpected: adjacentSourcesExpected,
                SourcesCovered: 0,
                SourcesExpected: sourcesExpected,
                CoveragePercent: 0,
                MissingSources: ["chummer4", "chummer5a", "hero-lab-classic", "genesis-commlink6"]);
        }

        int chummer4FixtureCount = CountFiles(
            Path.Combine(oracleRoot, "Chummer.CoreEngine.Tests", "Fixtures", "Sr4"),
            "*.chum4",
            SearchOption.TopDirectoryOnly);
        int chummer5FixtureCount = CountFiles(
            Path.Combine(oracleRoot, "Chummer.Tests", "TestFiles"),
            "*.chum5",
            SearchOption.TopDirectoryOnly);
        int heroLabFixtureCount =
            CountFiles(Path.Combine(oracleRoot, "Chummer.CoreEngine.Tests", "Fixtures", "HeroLab"), "*.por", SearchOption.AllDirectories)
            + CountFiles(Path.Combine(oracleRoot, "Chummer.CoreEngine.Tests", "Fixtures", "HeroLab"), "*.hlo", SearchOption.AllDirectories);

        string receiptPath = Path.Combine(oracleRoot, ".codex-studio", "published", "IMPORT_PARITY_CERTIFICATION.generated.json");
        string receiptPosture = ResolveImportOracleReceiptPosture(receiptPath);
        (string adjacentReceiptPosture, int adjacentSourcesCovered) = ResolveAdjacentSr6OracleCoverage(receiptPath);

        int sourcesCovered = 0;
        if (chummer4FixtureCount > 0)
        {
            sourcesCovered++;
        }

        if (chummer5FixtureCount > 0)
        {
            sourcesCovered++;
        }

        if (heroLabFixtureCount > 0)
        {
            sourcesCovered++;
        }

        if (string.Equals(adjacentReceiptPosture, "governed", StringComparison.Ordinal))
        {
            // Adjacent SR6 oracles (Genesis + CommLink6 class posture) are proven through the parity receipt.
            sourcesCovered++;
        }

        int coveragePercent = (int)Math.Round(sourcesCovered * 100d / sourcesExpected, MidpointRounding.AwayFromZero);
        string lanePosture = sourcesCovered <= 0
            ? "missing"
            : sourcesCovered >= sourcesExpected && string.Equals(receiptPosture, "governed", StringComparison.Ordinal)
                ? "governed"
                : "stale";
        List<string> missingSources = new(4);
        if (chummer4FixtureCount <= 0)
        {
            missingSources.Add("chummer4");
        }

        if (chummer5FixtureCount <= 0)
        {
            missingSources.Add("chummer5a");
        }

        if (heroLabFixtureCount <= 0)
        {
            missingSources.Add("hero-lab-classic");
        }

        if (!string.Equals(adjacentReceiptPosture, "governed", StringComparison.Ordinal))
        {
            missingSources.Add("genesis-commlink6");
        }

        return new ImportOracleSummary(
            LanePosture: lanePosture,
            ReceiptPosture: receiptPosture,
            LegacyChummer4FixtureCount: chummer4FixtureCount,
            LegacyChummer5FixtureCount: chummer5FixtureCount,
            HeroLabFixtureCount: heroLabFixtureCount,
            AdjacentSr6OracleReceiptPosture: adjacentReceiptPosture,
            AdjacentSr6OracleSourcesCovered: adjacentSourcesCovered,
            AdjacentSr6OracleSourcesExpected: adjacentSourcesExpected,
            SourcesCovered: sourcesCovered,
            SourcesExpected: sourcesExpected,
            CoveragePercent: coveragePercent,
            MissingSources: missingSources);
    }

    private static AmendPackageSummary BuildAmendPackageSummary(string baseDataPath)
    {
        string? proofRoot = TryResolveEngineProofRoot(baseDataPath);
        string receiptPath = string.IsNullOrWhiteSpace(proofRoot)
            ? string.Empty
            : Path.Combine(proofRoot, ".codex-studio", "published", "ENGINE_PROOF_PACK.generated.json");
        if (string.IsNullOrWhiteSpace(receiptPath) || !File.Exists(receiptPath))
        {
            return new AmendPackageSummary(
                ProofReceiptPath: receiptPath,
                LanePosture: "missing",
                ProofPackStatus: "missing",
                SourceToggleSuiteStatus: "missing",
                AmendPackageSuiteStatus: "missing",
                AmendPackageCoverageFocus: string.Empty);
        }

        try
        {
            JsonNode? payload = JsonNode.Parse(File.ReadAllText(receiptPath));
            string proofPackStatus = NormalizeReceiptStatus(payload?["status"]?.GetValue<string>());
            JsonArray? suites = payload?["oracle_suites"] as JsonArray;
            string sourceToggleSuiteStatus = ResolveOracleSuiteStatus(suites, "source_toggle");
            string amendPackageSuiteStatus = ResolveOracleSuiteStatus(suites, "amend_package");
            string amendPackageCoverageFocus = ResolveOracleSuiteCoverageFocus(suites, "amend_package");
            bool governed =
                string.Equals(proofPackStatus, "governed", StringComparison.Ordinal)
                && string.Equals(sourceToggleSuiteStatus, "governed", StringComparison.Ordinal)
                && string.Equals(amendPackageSuiteStatus, "governed", StringComparison.Ordinal);

            return new AmendPackageSummary(
                ProofReceiptPath: receiptPath,
                LanePosture: governed ? "governed" : "stale",
                ProofPackStatus: proofPackStatus,
                SourceToggleSuiteStatus: sourceToggleSuiteStatus,
                AmendPackageSuiteStatus: amendPackageSuiteStatus,
                AmendPackageCoverageFocus: amendPackageCoverageFocus);
        }
        catch
        {
            return new AmendPackageSummary(
                ProofReceiptPath: receiptPath,
                LanePosture: "stale",
                ProofPackStatus: "stale",
                SourceToggleSuiteStatus: "stale",
                AmendPackageSuiteStatus: "stale",
                AmendPackageCoverageFocus: string.Empty);
        }
    }

    private static string ResolveImportOracleReceiptPosture(string receiptPath)
    {
        if (!File.Exists(receiptPath))
        {
            return "missing";
        }

        try
        {
            JsonNode? payload = JsonNode.Parse(File.ReadAllText(receiptPath));
            string status = payload?["status"]?.GetValue<string>()?.Trim() ?? string.Empty;
            return string.Equals(status, "passed", StringComparison.OrdinalIgnoreCase)
                ? "governed"
                : "stale";
        }
        catch
        {
            return "stale";
        }
    }

    private static string ResolveOracleSuiteStatus(JsonArray? suites, string suiteId)
    {
        if (suites is null || suites.Count == 0)
        {
            return "missing";
        }

        JsonObject? suite = suites
            .OfType<JsonObject>()
            .FirstOrDefault(node => string.Equals(node["id"]?.GetValue<string>(), suiteId, StringComparison.Ordinal));
        if (suite is null)
        {
            return "missing";
        }

        return NormalizeReceiptStatus(suite["status"]?.GetValue<string>());
    }

    private static string ResolveOracleSuiteCoverageFocus(JsonArray? suites, string suiteId)
    {
        if (suites is null || suites.Count == 0)
        {
            return string.Empty;
        }

        JsonObject? suite = suites
            .OfType<JsonObject>()
            .FirstOrDefault(node => string.Equals(node["id"]?.GetValue<string>(), suiteId, StringComparison.Ordinal));
        return suite?["coverage_focus"]?.GetValue<string>()?.Trim() ?? string.Empty;
    }

    private static string NormalizeReceiptStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "missing";
        }

        return string.Equals(status.Trim(), "passed", StringComparison.OrdinalIgnoreCase)
            ? "governed"
            : "stale";
    }

    private static (string ReceiptPosture, int SourcesCovered) ResolveAdjacentSr6OracleCoverage(string receiptPath)
    {
        if (!File.Exists(receiptPath))
        {
            return ("missing", 0);
        }

        try
        {
            JsonNode? payload = JsonNode.Parse(File.ReadAllText(receiptPath));
            string status = payload?["status"]?.GetValue<string>()?.Trim() ?? string.Empty;
            if (!string.Equals(status, "passed", StringComparison.OrdinalIgnoreCase))
            {
                return ("stale", 0);
            }

            JsonArray? adjacentOracles = payload?["adjacent_oracles"] as JsonArray;
            if (adjacentOracles is null || adjacentOracles.Count == 0)
            {
                return ("stale", 0);
            }

            bool hasGenesis = false;
            bool hasCommLink = false;
            foreach (JsonNode? adjacentOracleNode in adjacentOracles)
            {
                string token = adjacentOracleNode?["name"]?.GetValue<string>()
                               ?? adjacentOracleNode?.GetValue<string>()
                               ?? string.Empty;
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (token.Contains("genesis", StringComparison.OrdinalIgnoreCase))
                {
                    hasGenesis = true;
                }

                if (token.Contains("commlink", StringComparison.OrdinalIgnoreCase))
                {
                    hasCommLink = true;
                }
            }

            int sourcesCovered = (hasGenesis ? 1 : 0) + (hasCommLink ? 1 : 0);
            return sourcesCovered >= 2
                ? ("governed", sourcesCovered)
                : ("stale", sourcesCovered);
        }
        catch
        {
            return ("stale", 0);
        }
    }

    private static string? TryResolveEngineProofRoot(string baseDataPath)
    {
        foreach (string candidateRoot in EnumerateEvidenceCandidateRoots(baseDataPath))
        {
            if (Directory.Exists(Path.Combine(candidateRoot, ".codex-studio", "published")))
            {
                return candidateRoot;
            }    
        }

        return null;
    }

    private static string? TryResolveImportOracleRoot(string baseDataPath)
    {
        foreach (string candidateRoot in EnumerateEvidenceCandidateRoots(baseDataPath))
        {
            bool hasChummer5Fixtures = Directory.Exists(Path.Combine(candidateRoot, "Chummer.Tests", "TestFiles"));
            bool hasChummer4Fixtures = Directory.Exists(Path.Combine(candidateRoot, "Chummer.CoreEngine.Tests", "Fixtures", "Sr4"));
            if (hasChummer5Fixtures || hasChummer4Fixtures)
            {
                return candidateRoot;
            }
        }

        return null;
    }

    private static int CountFiles(string directory, string pattern, SearchOption searchOption)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        return Directory.EnumerateFiles(directory, pattern, searchOption).Count();
    }

    private static int CountHouseRuleOverlays(ContentOverlayCatalog catalog)
    {
        return catalog.Overlays.Count(pack =>
            pack.Enabled
            && !string.IsNullOrWhiteSpace(pack.DataPath)
            && Directory.Exists(pack.DataPath)
            && IsHouseRuleOverlay(pack));
    }

    private static bool IsHouseRuleOverlay(ContentOverlayPack pack)
    {
        return pack.Mode == ContentOverlayModes.MergeCatalog
               || pack.Id.Contains("house", StringComparison.OrdinalIgnoreCase)
               || pack.Name.Contains("house", StringComparison.OrdinalIgnoreCase)
               || pack.Id.Contains("rule", StringComparison.OrdinalIgnoreCase)
               || pack.Name.Contains("rule", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveTranslatorBridgePosture(ContentOverlayCatalog catalog)
    {
        int enabledOverlayCount = CountEnabledLanguageOverlays(catalog);
        if (enabledOverlayCount == 0)
        {
            return "missing";
        }

        return CountOverlayXmlFiles(catalog, pack => pack.LanguagePath) > 0
            ? "governed"
            : "stale";
    }

    private static int CountEnabledLanguageOverlays(ContentOverlayCatalog catalog)
    {
        return catalog.Overlays.Count(pack =>
            pack.Enabled
            && !string.IsNullOrWhiteSpace(pack.LanguagePath)
            && Directory.Exists(pack.LanguagePath));
    }

    private static int CountOverlayXmlFiles(ContentOverlayCatalog catalog, Func<ContentOverlayPack, string> selector)
    {
        int total = 0;
        foreach (ContentOverlayPack pack in catalog.Overlays.Where(static pack => pack.Enabled))
        {
            string directory = selector(pack);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            total += Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories).Count();
        }

        return total;
    }

    private static Dictionary<string, XDocument?> CollapseLanguageFilesByCode(IReadOnlyDictionary<string, XDocument?> filesByName)
    {
        Dictionary<string, XDocument?> filesByCode = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string fileName, XDocument? fileDocument) in filesByName.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(stem))
            {
                continue;
            }

            if (stem.Contains('.', StringComparison.Ordinal))
            {
                // Fragment-like language files are merged into canonical files and should never appear as synthetic language codes.
                continue;
            }

            if (!LooksLikeLanguageCode(stem))
            {
                continue;
            }

            filesByCode[stem] = fileDocument;
        }

        return filesByCode;
    }

    private static bool LooksLikeLanguageCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        ReadOnlySpan<char> span = code.AsSpan();
        for (int index = 0; index < span.Length; index++)
        {
            char character = span[index];
            if (!(char.IsLetterOrDigit(character) || character == '-'))
            {
                return false;
            }
        }

        return code.Contains('-', StringComparison.Ordinal);
    }

    private static IReadOnlyList<MasterIndexSourcebookEntry> BuildSourcebookEntries(IReadOnlyDictionary<string, XDocument?> filesByName)
    {
        if (!filesByName.TryGetValue("books.xml", out XDocument? booksDocument) || booksDocument?.Root is null)
        {
            return Array.Empty<MasterIndexSourcebookEntry>();
        }

        IEnumerable<XElement> bookNodes = booksDocument.Root
            .Element("books")?
            .Elements("book")
            ?? Enumerable.Empty<XElement>();

        List<MasterIndexSourcebookEntry> sourcebooks = new();
        foreach (XElement bookNode in bookNodes)
        {
            string id = ReadChildValue(bookNode, "id");
            string code = ReadChildValue(bookNode, "code");
            string name = ReadChildValue(bookNode, "name");
            bool permanent = ParseBool(ReadChildValue(bookNode, "permanent")) || bookNode.Element("permanent") is not null;
            string localPdfPath = ReadChildValue(bookNode, "pdf");
            string referenceUrl = ReadChildValue(bookNode, "url");
            string referenceSnapshot = ResolveReferenceSnapshot(bookNode);
            List<MasterIndexRuleSnippetEntry> snippets = bookNode
                .Element("matches")?
                .Elements("match")
                .Select(match => new MasterIndexRuleSnippetEntry(
                    Language: ReadChildValue(match, "language"),
                    Page: ParseInt(ReadChildValue(match, "page")),
                    Snippet: ReadChildValue(match, "text"),
                    Provenance: "books.xml"))
                .Where(snippet => !string.IsNullOrWhiteSpace(snippet.Snippet))
                .ToList()
                ?? [];

            sourcebooks.Add(new MasterIndexSourcebookEntry(
                Id: id,
                Code: code,
                Name: name,
                Permanent: permanent,
                ReferencePosture: snippets.Count > 0 ? "matched-snippets" : "no-snippets",
                RuleSnippetCount: snippets.Count,
                RuleSnippets: snippets,
                ReferenceSourcePosture: ResolveReferenceSourcePosture(localPdfPath, referenceUrl, referenceSnapshot),
                LocalPdfPath: localPdfPath,
                ReferenceUrl: referenceUrl,
                ReferenceSnapshot: referenceSnapshot,
                ReferenceSnapshotPosture: ResolveReferenceSnapshotPosture(referenceSnapshot)));
        }

        return sourcebooks
            .OrderBy(sourcebook => sourcebook.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(sourcebook => sourcebook.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, XDocument?> BuildEffectiveDocuments(
        ContentOverlayCatalog catalog,
        string baseDirectory,
        Func<ContentOverlayPack, string> selector)
    {
        Dictionary<string, XDocument?> filesByName = new(StringComparer.OrdinalIgnoreCase);
        ApplyReplaceFileDirectory(filesByName, baseDirectory);

        foreach (ContentOverlayPack pack in catalog.Overlays
                     .Where(pack => pack.Enabled)
                     .OrderBy(pack => pack.Priority)
                     .ThenBy(pack => pack.Id, StringComparer.Ordinal))
        {
            string directory = selector(pack);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            if (string.Equals(pack.Mode, ContentOverlayModes.MergeCatalog, StringComparison.Ordinal))
            {
                ApplyMergeCatalogDirectory(filesByName, directory);
                continue;
            }

            ApplyReplaceFileDirectory(filesByName, directory);
        }

        return filesByName;
    }

    private static void ApplyReplaceFileDirectory(IDictionary<string, XDocument?> filesByName, string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string filePath in Directory.EnumerateFiles(directory, "*.xml", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            string fileName = Path.GetFileName(filePath);
            filesByName[fileName] = LoadXmlDocument(filePath);
        }
    }

    private static void ApplyMergeCatalogDirectory(IDictionary<string, XDocument?> filesByName, string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string filePath in Directory.EnumerateFiles(directory, "*.xml", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            XDocument? fragmentDocument = LoadXmlDocument(filePath);
            if (fragmentDocument is null)
            {
                continue;
            }

            string targetFileName = ResolveCatalogTargetFileName(Path.GetFileName(filePath));
            if (string.IsNullOrWhiteSpace(targetFileName))
            {
                continue;
            }

            if (!filesByName.TryGetValue(targetFileName, out XDocument? currentDocument) || currentDocument is null)
            {
                filesByName[targetFileName] = new XDocument(fragmentDocument);
                continue;
            }

            filesByName[targetFileName] = MergeCatalogDocument(currentDocument, fragmentDocument);
        }
    }

    private static string ResolveCatalogTargetFileName(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(stem))
        {
            return string.Empty;
        }

        int markerIndex = stem.IndexOf('.');
        string canonicalStem = markerIndex >= 0 ? stem[..markerIndex] : stem;
        if (string.IsNullOrWhiteSpace(canonicalStem))
        {
            return string.Empty;
        }

        return $"{canonicalStem}.xml";
    }

    private static XDocument? LoadXmlDocument(string filePath)
    {
        try
        {
            return XDocument.Load(filePath, LoadOptions.None);
        }
        catch
        {
            return null;
        }
    }

    private static string ReadChildValue(XElement parent, string name)
    {
        return parent
            .Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim()
            ?? string.Empty;
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, out int parsed)
            ? parsed
            : 0;
    }

    private static string ResolveReferenceSourcePosture(string localPdfPath, string referenceUrl, string referenceSnapshot)
    {
        bool hasPdf = !string.IsNullOrWhiteSpace(localPdfPath);
        bool hasUrl = !string.IsNullOrWhiteSpace(referenceUrl);
        bool hasSnapshot = !string.IsNullOrWhiteSpace(referenceSnapshot);
        if (!hasPdf && !hasUrl && !hasSnapshot)
        {
            return "missing";
        }

        bool validPdf = !hasPdf || localPdfPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        bool validUrl = !hasUrl || Uri.TryCreate(referenceUrl, UriKind.Absolute, out Uri? parsedUri)
            && (string.Equals(parsedUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(parsedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        bool validSnapshot = !hasSnapshot
                             || string.Equals(
                                 ResolveReferenceSnapshotPosture(referenceSnapshot),
                                 "governed",
                                 StringComparison.Ordinal);
        return validPdf && validUrl && validSnapshot
            ? "governed"
            : "stale";
    }

    private static string ResolveReferenceSnapshot(XElement bookNode)
    {
        string snapshot = ReadChildValue(bookNode, "snapshot");
        if (!string.IsNullOrWhiteSpace(snapshot))
        {
            return snapshot;
        }

        snapshot = ReadChildValue(bookNode, "snapshoturl");
        if (!string.IsNullOrWhiteSpace(snapshot))
        {
            return snapshot;
        }

        return ReadChildValue(bookNode, "referenceSnapshot");
    }

    private static string ResolveReferenceSnapshotPosture(string referenceSnapshot)
    {
        if (string.IsNullOrWhiteSpace(referenceSnapshot))
        {
            return "missing";
        }

        if (Uri.TryCreate(referenceSnapshot, UriKind.Absolute, out Uri? snapshotUri))
        {
            return string.Equals(snapshotUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(snapshotUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                ? "governed"
                : "stale";
        }

        return referenceSnapshot.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
               || referenceSnapshot.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
               || referenceSnapshot.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)
               || referenceSnapshot.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
               || referenceSnapshot.EndsWith(".mhtml", StringComparison.OrdinalIgnoreCase)
               || referenceSnapshot.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? "governed"
            : "stale";
    }

    private static bool ParseBool(string value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.Ordinal);
    }

    private static XDocument MergeCatalogDocument(XDocument baseDocument, XDocument fragmentDocument)
    {
        XElement mergedRoot = baseDocument.Root is null
            ? new XElement("chummer")
            : new XElement(baseDocument.Root);

        XElement? fragmentRoot = fragmentDocument.Root;
        if (fragmentRoot is not null)
        {
            foreach (XElement fragmentChild in fragmentRoot.Elements())
            {
                MergeRootChildElement(mergedRoot, fragmentChild);
            }
        }

        return baseDocument.Declaration is null
            ? new XDocument(mergedRoot)
            : new XDocument(baseDocument.Declaration, mergedRoot);
    }

    private static void MergeRootChildElement(XElement targetRoot, XElement fragmentChild)
    {
        XElement? targetChild = targetRoot.Elements(fragmentChild.Name).FirstOrDefault();
        if (targetChild is null)
        {
            targetRoot.Add(new XElement(fragmentChild));
            return;
        }

        bool fragmentHasNestedElements = fragmentChild.Elements().Any();
        if (!fragmentHasNestedElements)
        {
            targetChild.ReplaceWith(new XElement(fragmentChild));
            return;
        }

        MergeContainerElements(targetChild, fragmentChild);
    }

    private static void MergeContainerElements(XElement targetContainer, XElement fragmentContainer)
    {
        foreach (XElement fragmentEntry in fragmentContainer.Elements())
        {
            string? mergeKey = TryResolveMergeKey(fragmentEntry);
            XElement? existing = null;

            if (!string.IsNullOrWhiteSpace(mergeKey))
            {
                existing = targetContainer.Elements(fragmentEntry.Name)
                    .FirstOrDefault(candidate =>
                        string.Equals(TryResolveMergeKey(candidate), mergeKey, StringComparison.Ordinal));
            }
            else
            {
                existing = targetContainer.Elements(fragmentEntry.Name)
                    .FirstOrDefault(candidate => XNode.DeepEquals(candidate, fragmentEntry));
            }

            if (existing is null)
            {
                targetContainer.Add(new XElement(fragmentEntry));
                continue;
            }

            if (!XNode.DeepEquals(existing, fragmentEntry))
            {
                existing.ReplaceWith(new XElement(fragmentEntry));
            }
        }
    }

    private static string? TryResolveMergeKey(XElement element)
    {
        static string? ChildValue(XElement current, XName name)
            => NormalizeMergeKeyValue(current.Element(name)?.Value);

        static string? AttributeValue(XElement current, XName name)
            => NormalizeMergeKeyValue(current.Attribute(name)?.Value);

        string? id = ChildValue(element, "id") ?? AttributeValue(element, "id");
        if (!string.IsNullOrWhiteSpace(id))
        {
            return $"id:{id}";
        }

        string? key = ChildValue(element, "key") ?? AttributeValue(element, "key");
        if (!string.IsNullOrWhiteSpace(key))
        {
            return $"key:{key}";
        }

        string? name = ChildValue(element, "name") ?? AttributeValue(element, "name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return $"name:{name}";
        }

        bool hasNestedElements = element.Elements().Any();
        if (hasNestedElements)
        {
            return null;
        }

        string? value = NormalizeMergeKeyValue(element.Value);
        return string.IsNullOrWhiteSpace(value) ? null : $"value:{value}";
    }

    private static string? NormalizeMergeKeyValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
