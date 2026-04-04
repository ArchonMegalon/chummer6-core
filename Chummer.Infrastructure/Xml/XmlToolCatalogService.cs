using System.Xml.Linq;
using Chummer.Application.Content;
using Chummer.Application.Tools;
using Chummer.Contracts.Api;
using Chummer.Infrastructure.Files;

namespace Chummer.Infrastructure.Xml;

public sealed class XmlToolCatalogService : IToolCatalogService
{
    private readonly IContentOverlayCatalogService _overlays;

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
                XmlBridgePosture: ResolveXmlBridgePosture(catalog),
                EnabledDataOverlayCount: CountEnabledDataOverlays(catalog));

        IReadOnlyList<MasterIndexSourcebookEntry> sourcebooks = BuildSourcebookEntries(filesByName);
        string referenceLanePosture = ResolveReferenceLanePosture(sourcebooks);
        int sourcebooksWithSnippets = CountSourcebooksWithSnippets(sourcebooks);
        int sourcebooksMissingSnippets = sourcebooks.Count - sourcebooksWithSnippets;
        int referenceCoveragePercent = CalculateReferenceCoveragePercent(sourcebooks.Count, sourcebooksWithSnippets);

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
            XmlBridgePosture: ResolveXmlBridgePosture(catalog),
            EnabledDataOverlayCount: CountEnabledDataOverlays(catalog));
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
        return CountEnabledDataOverlays(catalog) > 0 ? "governed" : "missing";
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

    private static int CountEnabledDataOverlays(ContentOverlayCatalog catalog)
    {
        return catalog.Overlays.Count(pack =>
            pack.Enabled
            && !string.IsNullOrWhiteSpace(pack.DataPath)
            && Directory.Exists(pack.DataPath));
    }

    private static string ResolveTranslatorBridgePosture(ContentOverlayCatalog catalog)
    {
        return CountEnabledLanguageOverlays(catalog) > 0 ? "governed" : "missing";
    }

    private static int CountEnabledLanguageOverlays(ContentOverlayCatalog catalog)
    {
        return catalog.Overlays.Count(pack =>
            pack.Enabled
            && !string.IsNullOrWhiteSpace(pack.LanguagePath)
            && Directory.Exists(pack.LanguagePath));
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
                RuleSnippets: snippets));
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
