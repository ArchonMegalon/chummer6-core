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
        int SourcesCovered,
        int SourcesExpected,
        int CoveragePercent);

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
        ImportOracleSummary importOracleSummary = BuildImportOracleSummary(catalog.BaseDataPath);
        if (filesByName.Count == 0)
            return new MasterIndexResponse(
                Count: 0,
                GeneratedUtc: DateTimeOffset.UtcNow,
                Files: Array.Empty<MasterIndexFileEntry>(),
                ReferenceLanePosture: "missing",
                SourcebookCount: 0,
                Sourcebooks: Array.Empty<MasterIndexSourcebookEntry>(),
                SourcebooksWithSnippets: 0,
                SourcebooksMissingSnippets: 0,
                ReferenceCoveragePercent: 0,
                SettingsLanePosture: "missing",
                SettingsProfileCount: 0,
                SettingsProfilesWithSourceToggles: 0,
                DistinctSourcebookToggles: 0,
                SourceToggleLanePosture: "missing",
                SourcebookToggleCoveragePercent: 0,
                CustomDataLanePosture: "missing",
                SettingsProfilesWithCustomDataDirectories: 0,
                DistinctCustomDataDirectoryCount: 0,
                XmlBridgePosture: ResolveXmlBridgePosture(catalog),
                EnabledDataOverlayCount: CountEnabledDataOverlays(catalog),
                Sr6SupplementLanePosture: ResolveSr6SupplementLanePosture(Array.Empty<MasterIndexSourcebookEntry>()),
                Sr6DesignerToolsPosture: ResolveSr6DesignerToolsPosture(0, Sr6DesignerFamiliesExpected),
                Sr6DesignerFamiliesAvailable: 0,
                Sr6DesignerFamiliesExpected: Sr6DesignerFamiliesExpected,
                HouseRuleLanePosture: ResolveHouseRuleLanePosture(catalog),
                HouseRuleOverlayCount: CountHouseRuleOverlays(catalog),
                ImportOracleLanePosture: importOracleSummary.LanePosture,
                ImportOracleReceiptPosture: importOracleSummary.ReceiptPosture,
                LegacyChummer4FixtureCount: importOracleSummary.LegacyChummer4FixtureCount,
                LegacyChummer5FixtureCount: importOracleSummary.LegacyChummer5FixtureCount,
                HeroLabFixtureCount: importOracleSummary.HeroLabFixtureCount,
                ImportOracleSourcesCovered: importOracleSummary.SourcesCovered,
                ImportOracleSourcesExpected: importOracleSummary.SourcesExpected,
                ImportOracleCoveragePercent: importOracleSummary.CoveragePercent);

        IReadOnlyList<MasterIndexSourcebookEntry> sourcebooks = BuildSourcebookEntries(filesByName);
        string referenceLanePosture = ResolveReferenceLanePosture(sourcebooks);
        int sourcebooksWithSnippets = CountSourcebooksWithSnippets(sourcebooks);
        int sourcebooksMissingSnippets = sourcebooks.Count - sourcebooksWithSnippets;
        int referenceCoveragePercent = CalculateReferenceCoveragePercent(sourcebooks.Count, sourcebooksWithSnippets);
        var settingsSummary = BuildSettingsCatalogSummary(filesByName, sourcebooks);
        int enabledDataOverlayCount = CountEnabledDataOverlays(catalog);
        string customDataLanePosture = ResolveCustomDataLanePosture(
            settingsSummary.DistinctCustomDataDirectoryCount,
            enabledDataOverlayCount);
        int sr6DesignerFamiliesAvailable = CountSr6DesignerFamiliesAvailable(filesByName);

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
            SourcebookCount: sourcebooks.Count,
            Sourcebooks: sourcebooks,
            SourcebooksWithSnippets: sourcebooksWithSnippets,
            SourcebooksMissingSnippets: sourcebooksMissingSnippets,
            ReferenceCoveragePercent: referenceCoveragePercent,
            SettingsLanePosture: settingsSummary.SettingsLanePosture,
            SettingsProfileCount: settingsSummary.ProfileCount,
            SettingsProfilesWithSourceToggles: settingsSummary.ProfilesWithSourceToggles,
            DistinctSourcebookToggles: settingsSummary.DistinctSourcebookToggles,
            SourceToggleLanePosture: settingsSummary.SourceToggleLanePosture,
            SourcebookToggleCoveragePercent: settingsSummary.SourcebookToggleCoveragePercent,
            CustomDataLanePosture: customDataLanePosture,
            SettingsProfilesWithCustomDataDirectories: settingsSummary.ProfilesWithCustomDataDirectories,
            DistinctCustomDataDirectoryCount: settingsSummary.DistinctCustomDataDirectoryCount,
            XmlBridgePosture: ResolveXmlBridgePosture(catalog),
            EnabledDataOverlayCount: enabledDataOverlayCount,
            Sr6SupplementLanePosture: ResolveSr6SupplementLanePosture(sourcebooks),
            Sr6DesignerToolsPosture: ResolveSr6DesignerToolsPosture(sr6DesignerFamiliesAvailable, Sr6DesignerFamiliesExpected),
            Sr6DesignerFamiliesAvailable: sr6DesignerFamiliesAvailable,
            Sr6DesignerFamiliesExpected: Sr6DesignerFamiliesExpected,
            HouseRuleLanePosture: ResolveHouseRuleLanePosture(catalog),
            HouseRuleOverlayCount: CountHouseRuleOverlays(catalog),
            ImportOracleLanePosture: importOracleSummary.LanePosture,
            ImportOracleReceiptPosture: importOracleSummary.ReceiptPosture,
            LegacyChummer4FixtureCount: importOracleSummary.LegacyChummer4FixtureCount,
            LegacyChummer5FixtureCount: importOracleSummary.LegacyChummer5FixtureCount,
            HeroLabFixtureCount: importOracleSummary.HeroLabFixtureCount,
            ImportOracleSourcesCovered: importOracleSummary.SourcesCovered,
            ImportOracleSourcesExpected: importOracleSummary.SourcesExpected,
            ImportOracleCoveragePercent: importOracleSummary.CoveragePercent);
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

        return sourcebooks.Any(sourcebook => string.Equals(sourcebook.ReferencePosture, "no-snippets", StringComparison.Ordinal))
            ? "stale"
            : "governed";
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

    private static ImportOracleSummary BuildImportOracleSummary(string baseDataPath)
    {
        const int sourcesExpected = 4;
        string? oracleRoot = TryResolveImportOracleRoot(baseDataPath);
        if (string.IsNullOrWhiteSpace(oracleRoot))
        {
            return new ImportOracleSummary(
                LanePosture: "missing",
                ReceiptPosture: "missing",
                LegacyChummer4FixtureCount: 0,
                LegacyChummer5FixtureCount: 0,
                HeroLabFixtureCount: 0,
                SourcesCovered: 0,
                SourcesExpected: sourcesExpected,
                CoveragePercent: 0);
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

        if (string.Equals(receiptPosture, "governed", StringComparison.Ordinal))
        {
            // The parity harness receipt currently carries SR4/SR5/SR6 import coverage in one governed record.
            sourcesCovered++;
        }

        int coveragePercent = (int)Math.Round(sourcesCovered * 100d / sourcesExpected, MidpointRounding.AwayFromZero);
        string lanePosture = sourcesCovered <= 0
            ? "missing"
            : sourcesCovered >= sourcesExpected && string.Equals(receiptPosture, "governed", StringComparison.Ordinal)
                ? "governed"
                : "stale";

        return new ImportOracleSummary(
            LanePosture: lanePosture,
            ReceiptPosture: receiptPosture,
            LegacyChummer4FixtureCount: chummer4FixtureCount,
            LegacyChummer5FixtureCount: chummer5FixtureCount,
            HeroLabFixtureCount: heroLabFixtureCount,
            SourcesCovered: sourcesCovered,
            SourcesExpected: sourcesExpected,
            CoveragePercent: coveragePercent);
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

    private static string? TryResolveImportOracleRoot(string baseDataPath)
    {
        if (string.IsNullOrWhiteSpace(baseDataPath))
        {
            return null;
        }

        DirectoryInfo? current = new(Path.GetFullPath(baseDataPath));
        while (current is not null)
        {
            bool hasChummer5Fixtures = Directory.Exists(Path.Combine(current.FullName, "Chummer.Tests", "TestFiles"));
            bool hasChummer4Fixtures = Directory.Exists(Path.Combine(current.FullName, "Chummer.CoreEngine.Tests", "Fixtures", "Sr4"));
            if (hasChummer5Fixtures || hasChummer4Fixtures)
            {
                return current.FullName;
            }

            current = current.Parent;
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
                ReferenceSourcePosture: ResolveReferenceSourcePosture(localPdfPath, referenceUrl),
                LocalPdfPath: localPdfPath,
                ReferenceUrl: referenceUrl));
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

    private static string ResolveReferenceSourcePosture(string localPdfPath, string referenceUrl)
    {
        bool hasPdf = !string.IsNullOrWhiteSpace(localPdfPath);
        bool hasUrl = !string.IsNullOrWhiteSpace(referenceUrl);
        if (!hasPdf && !hasUrl)
        {
            return "missing";
        }

        bool validPdf = !hasPdf || localPdfPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        bool validUrl = !hasUrl || Uri.TryCreate(referenceUrl, UriKind.Absolute, out Uri? parsedUri)
            && (string.Equals(parsedUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(parsedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        return validPdf && validUrl
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
