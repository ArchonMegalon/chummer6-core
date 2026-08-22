using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed class FileSystemCharacterSourceDataResolver : ICharacterSourceDataResolver
{
    private const string CanonicalLifeModuleSettingsProfileId = "8a31af6d-7137-4284-872b-7d8087e156c6";

    private sealed record CustomDirectory(
        string Name,
        string Path,
        Guid? ManifestId,
        LegacyVersion Version,
        bool ManifestValid);

    private readonly record struct OrderedCustomDataKey(
        int DocumentIndex,
        int? Order,
        string Key);

    private readonly record struct LegacyVersion(IReadOnlyList<int> Parts) : IComparable<LegacyVersion>
    {
        public static LegacyVersion Default { get; } = new([1]);

        public static bool TryParse(string? value, out LegacyVersion version)
        {
            version = Default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string[] segments = value.Trim().Split('.', StringSplitOptions.None);
            int[] parts = new int[segments.Length];
            for (int index = 0; index < segments.Length; index++)
            {
                if (!int.TryParse(
                        segments[index],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out parts[index])
                    || parts[index] < 0)
                {
                    return false;
                }
            }

            version = new LegacyVersion(parts);
            return true;
        }

        public int CompareTo(LegacyVersion other)
        {
            int count = Math.Max(Parts.Count, other.Parts.Count);
            for (int index = 0; index < count; index++)
            {
                int left = index < Parts.Count ? Parts[index] : 0;
                int right = index < other.Parts.Count ? other.Parts[index] : 0;
                int comparison = left.CompareTo(right);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }
    }

    private readonly IContentOverlayCatalogService _overlays;

    public FileSystemCharacterSourceDataResolver(IContentOverlayCatalogService overlays)
    {
        _overlays = overlays ?? throw new ArgumentNullException(nameof(overlays));
    }

    public ICharacterSourceDataContext? TryCreateContext(string characterXml)
    {
        if (string.IsNullOrWhiteSpace(characterXml))
        {
            return null;
        }

        try
        {
            XDocument characterDocument = XDocument.Parse(characterXml, LoadOptions.None);
            XElement? character = characterDocument.Root;
            if (character is null || !string.Equals(character.Name.LocalName, "character", StringComparison.Ordinal))
            {
                return null;
            }

            ContentOverlayCatalog catalog = _overlays.GetCatalog();
            if (!TryLoadEffectiveDocument(catalog, "settings.xml", out XDocument? settingsDocument)
                || settingsDocument?.Root is null)
            {
                return null;
            }
            if (!TryComputeEffectiveInputDigest(catalog, "settings.xml", out string settingsInputsDigest))
            {
                return null;
            }

            string settingsKey = ReadValue(character, "settings");
            XElement[] settingsMatches = settingsDocument.Root
                .Element("settings")?
                .Elements("setting")
                .Where(candidate => string.Equals(
                    ReadValue(candidate, "id"),
                    settingsKey,
                    StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray()
                ?? [];
            if (settingsMatches.Length != 1)
            {
                return null;
            }
            XElement settings = settingsMatches[0];

            IReadOnlyList<CustomDirectory> installedDirectories = DiscoverCustomDirectories(catalog);
            if (!TryResolveProfileDirectories(settings, installedDirectories, out CustomDirectory[] enabledDirectories))
            {
                return null;
            }

            string[] savedDirectoryNames = character
                .Element("customdatadirectorynames")?
                .Elements("directoryname")
                .Select(node => node.Value.Trim())
                .Where(value => !string.IsNullOrEmpty(value))
                .ToArray()
                ?? [];
            if (!savedDirectoryNames.SequenceEqual(
                    enabledDirectories.Select(directory => directory.Name),
                    StringComparer.Ordinal))
            {
                return null;
            }

            string[] enabledSourcebooks = settings
                .Element("books")?
                .Elements("book")
                .Select(node => node.Value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
                ?? [];
            ResolveLifeModuleBudgetAuthority(
                settings,
                out string profileBuildMethod,
                out int? profileBuildPoints,
                out string[] lifeModuleBudgetBlockers);
            ResolveCreationPrerequisiteProfileAuthority(
                settings,
                out string prerequisiteBuildMethod,
                out int? creationKarmaTotal,
                out string[] priorityArray,
                out string priorityTable,
                out int? sumToTenTarget,
                out string[] prerequisiteProfileBlockers);
            ResolveCreationAttributeProfileAuthority(
                settings,
                out int? maxNumberMaxAttributesCreate,
                out int? karmaAttribute,
                out bool? alternateMetatypeAttributeKarma,
                out bool? reverseAttributePriorityOrder,
                out string[] attributeProfileBlockers);
            prerequisiteProfileBlockers = prerequisiteProfileBlockers
                .Concat(attributeProfileBlockers)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            ResolveMetatypeProfileAuthority(
                settings,
                out int? metatypeKarmaMultiplier,
                out int? minimumInitiativeDice,
                out bool? droneMods,
                out string[] metatypeProfileBlockers);
            string boundProfileInputsDigest = BindSelectedProfile(settingsInputsDigest, settingsKey);
            if (!TryComputeSelectedCustomDataInputsDigest(
                    enabledDirectories,
                    out string selectedCustomDataInputsDigest))
            {
                return null;
            }
            _ = TryComputeRawBaseFileDigest(
                catalog,
                "metatypes.xml",
                out string rawMetatypesXmlDigest);
            _ = TryComputeEffectiveInputDigest(
                catalog,
                "metatypes.xml",
                out string effectiveMetatypesInputsDigest);
            _ = TryComputeRawBaseFileDigest(
                catalog,
                "priorities.xml",
                out string rawPrioritiesXmlDigest);
            _ = TryComputeEffectiveInputDigest(
                catalog,
                "priorities.xml",
                out string effectivePrioritiesInputsDigest);
            _ = TryComputeSelectedPriorityCustomDataInputsDigest(
                enabledDirectories,
                out string selectedPriorityCustomDataInputsDigest);

            int? maximumNuyenDecimals = TryReadMaximumNuyenDecimals(settings, out int resolvedDecimals)
                ? resolvedDecimals
                : null;
            int? joinGroupKarma = TryReadNonNegativeInt(settings, "karmajoingroup", out int resolvedJoinGroupKarma)
                ? resolvedJoinGroupKarma
                : null;
            int? leaveGroupKarma = TryReadNonNegativeInt(settings, "karmaleavegroup", out int resolvedLeaveGroupKarma)
                ? resolvedLeaveGroupKarma
                : null;
            decimal? workingForPeopleRate = TryReadPositiveDecimal(
                settings,
                "nuyenperbpwftp",
                out decimal resolvedWorkingForPeopleRate)
                ? resolvedWorkingForPeopleRate
                : null;
            decimal? workingForManRate = TryReadPositiveDecimal(
                settings,
                "nuyenperbpwftm",
                out decimal resolvedWorkingForManRate)
                ? resolvedWorkingForManRate
                : null;
            int? essenceDecimals = TryReadDecimalPlaces(
                ReadValue(settings, "essenceformat"),
                out int resolvedEssenceDecimals)
                ? resolvedEssenceDecimals
                : null;
            bool doNotRoundEssenceInternally = ParseBool(ReadValue(settings, "donotroundessenceinternally"));
            string essenceModifierPostExpression = ReadValue(settings, "essencemodifierpostexpression");
            if (string.IsNullOrWhiteSpace(essenceModifierPostExpression))
            {
                essenceModifierPostExpression = "{Modifier}";
            }

            return new SourceDataContext(
                catalog,
                enabledDirectories,
                enabledSourcebooks,
                settingsKey,
                boundProfileInputsDigest,
                selectedCustomDataInputsDigest,
                rawMetatypesXmlDigest,
                effectiveMetatypesInputsDigest,
                rawPrioritiesXmlDigest,
                effectivePrioritiesInputsDigest,
                selectedPriorityCustomDataInputsDigest,
                profileBuildMethod,
                profileBuildPoints,
                lifeModuleBudgetBlockers,
                prerequisiteBuildMethod,
                creationKarmaTotal,
                priorityArray,
                priorityTable,
                sumToTenTarget,
                prerequisiteProfileBlockers,
                maxNumberMaxAttributesCreate,
                karmaAttribute,
                alternateMetatypeAttributeKarma,
                reverseAttributePriorityOrder,
                metatypeKarmaMultiplier,
                minimumInitiativeDice,
                droneMods,
                metatypeProfileBlockers,
                maximumNuyenDecimals,
                joinGroupKarma,
                leaveGroupKarma,
                workingForPeopleRate,
                workingForManRate,
                essenceDecimals,
                doNotRoundEssenceInternally,
                essenceModifierPostExpression);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or XmlException
                                          or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool TryReadMaximumNuyenDecimals(XElement settings, out int decimalPlaces)
    {
        decimalPlaces = 0;
        string format = ReadValue(settings, "nuyenformat").Trim();
        if (string.IsNullOrEmpty(format))
        {
            return false;
        }

        int separator = format.IndexOf('.');
        decimalPlaces = separator < 0 ? 0 : format.Length - separator - 1;
        return decimalPlaces is >= 0 and <= 28;
    }

    private static void ResolveLifeModuleBudgetAuthority(
        XElement settings,
        out string buildMethod,
        out int? buildPoints,
        out string[] blockers)
    {
        var resolvedBlockers = new List<string>();
        string[] buildMethodValues = settings
            .Elements("buildmethod")
            .Select(item => item.Value.Trim())
            .ToArray();
        if (buildMethodValues.Length != 1 || string.IsNullOrWhiteSpace(buildMethodValues[0]))
        {
            buildMethod = string.Empty;
            resolvedBlockers.Add(
                CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildMethodInvalid);
        }
        else
        {
            buildMethod = buildMethodValues[0];
            if (!string.Equals(
                    buildMethod,
                    CharacterCreationBuildMethods.LifeModules,
                    StringComparison.OrdinalIgnoreCase))
            {
                resolvedBlockers.Add(
                    CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildMethodMismatch);
            }
        }

        string[] buildPointValues = settings
            .Elements("buildpoints")
            .Select(item => item.Value.Trim())
            .ToArray();
        if (buildPointValues.Length == 1
            && int.TryParse(
                buildPointValues[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsedBuildPoints)
            && parsedBuildPoints >= 0)
        {
            buildPoints = parsedBuildPoints;
        }
        else
        {
            buildPoints = null;
            resolvedBlockers.Add(
                CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildPointsInvalid);
        }

        blockers = resolvedBlockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ResolveCreationPrerequisiteProfileAuthority(
        XElement settings,
        out string buildMethod,
        out int? creationKarmaTotal,
        out string[] priorityArray,
        out string priorityTable,
        out int? sumToTenTarget,
        out string[] blockers)
    {
        var resolvedBlockers = new List<string>();
        string[] buildMethods = settings.Elements("buildmethod").Take(2)
            .Select(element => element.Value.Trim())
            .ToArray();
        if (buildMethods.Length != 1)
        {
            buildMethod = string.Empty;
            resolvedBlockers.Add(CharacterCreationPrerequisiteBlockers.BuildMethodUnsupported);
        }
        else if (string.Equals(
                     buildMethods[0],
                     CharacterCreationBuildMethods.Priority,
                     StringComparison.OrdinalIgnoreCase))
        {
            buildMethod = CharacterCreationBuildMethods.Priority;
        }
        else if (string.Equals(
                     buildMethods[0],
                     CharacterCreationBuildMethods.SumToTen,
                     StringComparison.OrdinalIgnoreCase))
        {
            buildMethod = CharacterCreationBuildMethods.SumToTen;
        }
        else
        {
            buildMethod = buildMethods[0];
            resolvedBlockers.Add(CharacterCreationPrerequisiteBlockers.BuildMethodUnsupported);
        }

        if (TryReadNonNegativeInt(settings, "buildpoints", out int parsedBuildPoints))
        {
            creationKarmaTotal = parsedBuildPoints;
        }
        else
        {
            creationKarmaTotal = null;
            resolvedBlockers.Add(
                CharacterCreationPrerequisiteBlockers.CreationKarmaAuthorityRequired);
        }

        XElement[] priorityArrays = settings.Elements("priorityarray").Take(2).ToArray();
        string rawArray = priorityArrays.Length == 1 ? priorityArrays[0].Value : string.Empty;
        if (priorityArrays.Length == 1 && rawArray.Length == 0)
        {
            // SelectMetatypePriority replaces an explicitly-empty settings value
            // with its A/B/C/D/E list before the controls are populated.
            priorityArray = ["A", "B", "C", "D", "E"];
        }
        else if (priorityArrays.Length != 1
            || !string.Equals(rawArray, rawArray.Trim(), StringComparison.Ordinal)
            || rawArray.Length != CharacterCreationPriorityCategoryIds.Ordered.Count
            || rawArray.Any(character => !char.IsAsciiLetter(character)))
        {
            priorityArray = [];
            resolvedBlockers.Add(CharacterCreationPrerequisiteBlockers.PriorityArrayInvalid);
        }
        else
        {
            priorityArray = rawArray.ToUpperInvariant()
                .Select(character => character.ToString())
                .ToArray();
        }

        XElement[] priorityTables = settings.Elements("prioritytable").Take(2).ToArray();
        priorityTable = priorityTables.Length == 1 ? priorityTables[0].Value : string.Empty;
        if (priorityTables.Length != 1
            || string.IsNullOrWhiteSpace(priorityTable)
            || !string.Equals(priorityTable, priorityTable.Trim(), StringComparison.Ordinal))
        {
            priorityTable = string.Empty;
            resolvedBlockers.Add(CharacterCreationPrerequisiteBlockers.PriorityTableInvalid);
        }

        if (TryReadNonNegativeInt(settings, "sumtoten", out int parsedTarget))
        {
            sumToTenTarget = parsedTarget;
        }
        else
        {
            sumToTenTarget = null;
            resolvedBlockers.Add(CharacterCreationPrerequisiteBlockers.SumToTenTargetInvalid);
        }

        blockers = resolvedBlockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ResolveCreationAttributeProfileAuthority(
        XElement settings,
        out int? maxNumberMaxAttributesCreate,
        out int? karmaAttribute,
        out bool? alternateMetatypeAttributeKarma,
        out bool? reverseAttributePriorityOrder,
        out string[] blockers)
    {
        var resolvedBlockers = new List<string>();
        XElement[] maximumNodes = settings.Elements("maxnumbermaxattributescreate").Take(2).ToArray();
        if (maximumNodes.Length == 0)
        {
            XElement[] legacyNodes = settings.Elements("allow2ndmaxattribute").Take(2).ToArray();
            if (legacyNodes.Length == 0)
            {
                maxNumberMaxAttributesCreate = 1;
            }
            else if (legacyNodes.Length == 1
                     && TryParseStrictBoolElement(legacyNodes[0], out bool allowSecond))
            {
                maxNumberMaxAttributesCreate = allowSecond ? 2 : 1;
            }
            else
            {
                maxNumberMaxAttributesCreate = null;
                resolvedBlockers.Add(CharacterCreationPrerequisiteBlockers.AttributeSettingsInvalid);
            }
        }
        else if (maximumNodes.Length == 1
                 && TryParseNonNegativeIntElement(maximumNodes[0], out int maximum))
        {
            maxNumberMaxAttributesCreate = maximum;
        }
        else
        {
            maxNumberMaxAttributesCreate = null;
            resolvedBlockers.Add(CharacterCreationPrerequisiteBlockers.AttributeSettingsInvalid);
        }

        alternateMetatypeAttributeKarma = TryReadStrictBool(
            settings,
            "alternatemetatypeattributekarma",
            out bool alternate)
            ? alternate
            : null;
        reverseAttributePriorityOrder = TryReadStrictBool(
            settings,
            "reverseattributepriorityorder",
            out bool reverse)
            ? reverse
            : null;
        if (!alternateMetatypeAttributeKarma.HasValue || !reverseAttributePriorityOrder.HasValue)
            resolvedBlockers.Add(CharacterCreationPrerequisiteBlockers.AttributeSettingsInvalid);

        XElement[] karmaContainers = settings.Elements("karmacost").Take(2).ToArray();
        XElement[] karmaNodes = karmaContainers.Length == 1
            ? karmaContainers[0].Elements("karmaattribute").Take(2).ToArray()
            : [];
        if (karmaContainers.Length == 1
            && !karmaContainers[0].HasAttributes
            && karmaNodes.Length == 1
            && TryParseNonNegativeIntElement(karmaNodes[0], out int parsedKarma)
            && parsedKarma > 0)
        {
            karmaAttribute = parsedKarma;
        }
        else
        {
            karmaAttribute = null;
            resolvedBlockers.Add(CharacterCreationPrerequisiteBlockers.AttributeSettingsInvalid);
        }

        blockers = resolvedBlockers.Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryReadStrictBool(XElement parent, string name, out bool value)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        value = false;
        return matches.Length == 1 && TryParseStrictBoolElement(matches[0], out value);
    }

    private static bool TryParseStrictBoolElement(XElement element, out bool value)
    {
        value = false;
        return !element.HasAttributes
               && !element.HasElements
               && string.Equals(element.Value, element.Value.Trim(), StringComparison.Ordinal)
               && bool.TryParse(element.Value, out value);
    }

    private static bool TryParseNonNegativeIntElement(XElement element, out int value)
    {
        value = 0;
        return !element.HasAttributes
               && !element.HasElements
               && string.Equals(element.Value, element.Value.Trim(), StringComparison.Ordinal)
               && int.TryParse(
                   element.Value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out value)
               && value >= 0;
    }

    private static void ResolveMetatypeProfileAuthority(
        XElement settings,
        out int? metatypeKarmaMultiplier,
        out int? minimumInitiativeDice,
        out bool? droneMods,
        out string[] blockers)
    {
        var resolvedBlockers = new List<string>();
        if (!TryReadSingleBool(settings, "metatypecostskarma", out bool metatypeCostsKarma)
            || !metatypeCostsKarma)
        {
            resolvedBlockers.Add(CharacterCreationMetatypeCatalogBlockers.ProfileKarmaModeInvalid);
        }

        if (TryReadNonNegativeInt(settings, "metatypecostskarmamultiplier", out int multiplier)
            && multiplier is >= 1 and <= 10)
        {
            metatypeKarmaMultiplier = multiplier;
        }
        else
        {
            metatypeKarmaMultiplier = null;
            resolvedBlockers.Add(CharacterCreationMetatypeCatalogBlockers.ProfileKarmaMultiplierInvalid);
        }

        if (TryReadNonNegativeInt(settings, "mininitiativedice", out int initiativeDice)
            && initiativeDice <= 99)
        {
            minimumInitiativeDice = initiativeDice;
        }
        else
        {
            minimumInitiativeDice = null;
            resolvedBlockers.Add(CharacterCreationMetatypeCatalogBlockers.ProfileInitiativeFallbackInvalid);
        }

        if (TryReadSingleBool(settings, "dronemods", out bool resolvedDroneMods))
        {
            droneMods = resolvedDroneMods;
        }
        else
        {
            droneMods = null;
            resolvedBlockers.Add(CharacterCreationMetatypeCatalogBlockers.ProfileDroneModsInvalid);
        }

        string[] buildMethods = settings.Elements("buildmethod").Take(2).Select(item => item.Value.Trim()).ToArray();
        if (buildMethods.Length != 1
            || !string.Equals(buildMethods[0], CharacterCreationBuildMethods.LifeModules, StringComparison.OrdinalIgnoreCase))
        {
            resolvedBlockers.Add(CharacterCreationMetatypeCatalogBlockers.ProfileBuildMethodUnsupported);
        }

        blockers = resolvedBlockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryReadNonNegativeInt(XElement parent, string elementName, out int value)
    {
        value = 0;
        XElement[] values = parent.Elements(elementName).Take(2).ToArray();
        return values.Length == 1
            && int.TryParse(values[0].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && value >= 0;
    }

    private static bool TryReadSingleBool(XElement parent, string elementName, out bool value)
    {
        value = false;
        XElement[] values = parent.Elements(elementName).Take(2).ToArray();
        return values.Length == 1 && bool.TryParse(values[0].Value.Trim(), out value);
    }

    private static bool TryReadPositiveDecimal(XElement parent, string elementName, out decimal value)
    {
        value = 0m;
        XElement[] values = parent.Elements(elementName).Take(2).ToArray();
        return values.Length == 1
            && decimal.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value)
            && value > 0m;
    }

    private static bool TryReadDecimalPlaces(string format, out int decimalPlaces)
    {
        decimalPlaces = 0;
        if (string.IsNullOrWhiteSpace(format))
        {
            return false;
        }

        int separator = format.IndexOf('.');
        decimalPlaces = separator < 0 ? 0 : format.Length - separator - 1;
        return decimalPlaces is >= 0 and <= 28;
    }

    private static IReadOnlyList<CustomDirectory> DiscoverCustomDirectories(ContentOverlayCatalog catalog)
    {
        HashSet<string> roots = new(StringComparer.Ordinal);
        AddCustomDataRoot(roots, Path.GetDirectoryName(catalog.BaseDataPath));
        foreach (ContentOverlayPack pack in catalog.Overlays.Where(pack => pack.Enabled))
        {
            AddCustomDataRoot(roots, pack.RootPath);
            AddCustomDataRoot(roots, Path.GetDirectoryName(pack.DataPath));
        }

        List<CustomDirectory> result = [];
        foreach (string root in roots.OrderBy(path => path, StringComparer.Ordinal))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(directory);
                string manifestPath = Path.Combine(directory, "manifest.xml");
                if (!File.Exists(manifestPath))
                {
                    result.Add(new CustomDirectory(name, directory, null, LegacyVersion.Default, ManifestValid: true));
                    continue;
                }

                if (!TryLoadXml(manifestPath, out XDocument? manifestDocument)
                    || manifestDocument?.Root is null
                    || !Guid.TryParse(ReadValue(manifestDocument.Root, "guid"), out Guid manifestId)
                    || !LegacyVersion.TryParse(ReadValue(manifestDocument.Root, "version"), out LegacyVersion version))
                {
                    result.Add(new CustomDirectory(name, directory, null, LegacyVersion.Default, ManifestValid: false));
                    continue;
                }

                result.Add(new CustomDirectory(name, directory, manifestId, version, ManifestValid: true));
            }
        }

        return result;
    }

    private static void AddCustomDataRoot(ISet<string> roots, string? parentPath)
    {
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return;
        }

        roots.Add(Path.GetFullPath(Path.Combine(parentPath, "customdata")));
    }

    private static bool TryResolveProfileDirectories(
        XElement settings,
        IReadOnlyList<CustomDirectory> installedDirectories,
        out CustomDirectory[] directories)
    {
        List<OrderedCustomDataKey> orderedKeys = [];
        int documentIndex = 0;
        foreach (XElement entry in settings
                     .Element("customdatadirectorynames")?
                     .Elements("customdatadirectoryname")
                     ?? [])
        {
            string key = ReadValue(entry, "directoryname");
            if (string.IsNullOrEmpty(key) || !ParseBool(ReadValue(entry, "enabled")))
            {
                documentIndex++;
                continue;
            }

            int? order = int.TryParse(
                ReadValue(entry, "order"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsedOrder)
                ? parsedOrder
                : null;
            orderedKeys.Add(new OrderedCustomDataKey(documentIndex++, order, key));
        }

        OrderedCustomDataKey[] keys = orderedKeys
            .Where(entry => entry.Order.HasValue)
            .OrderBy(entry => entry.Order)
            .ThenBy(entry => entry.DocumentIndex)
            .Concat(orderedKeys
                .Where(entry => !entry.Order.HasValue)
                .OrderBy(entry => entry.DocumentIndex))
            .ToArray();

        List<CustomDirectory> resolved = [];
        HashSet<string> resolvedPaths = new(StringComparer.Ordinal);
        foreach (OrderedCustomDataKey key in keys)
        {
            if (!TryResolveCustomDirectory(key.Key, installedDirectories, out CustomDirectory? directory)
                || directory is null
                || !directory.ManifestValid
                || !resolvedPaths.Add(directory.Path))
            {
                directories = [];
                return false;
            }
            resolved.Add(directory);
        }

        directories = resolved.ToArray();
        return true;
    }

    private static bool TryResolveCustomDirectory(
        string key,
        IReadOnlyList<CustomDirectory> installedDirectories,
        out CustomDirectory? directory)
    {
        directory = null;
        string idText = key;
        LegacyVersion minimumVersion = LegacyVersion.Default;
        int separatorIndex = key.IndexOf('>');
        bool hasVersion = separatorIndex >= 0 && separatorIndex + 1 < key.Length;
        if (hasVersion)
        {
            idText = key[..separatorIndex];
            if (!LegacyVersion.TryParse(key[(separatorIndex + 1)..], out minimumVersion))
            {
                return false;
            }
        }

        IEnumerable<CustomDirectory> candidates;
        if (hasVersion && Guid.TryParse(idText, out Guid manifestId))
        {
            candidates = installedDirectories.Where(candidate =>
                candidate.ManifestId == manifestId
                && candidate.Version.CompareTo(minimumVersion) >= 0);
        }
        else
        {
            candidates = installedDirectories.Where(candidate =>
                string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase));
        }

        CustomDirectory[] matches = candidates
            .OrderByDescending(candidate => candidate.Version)
            .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
            .ToArray();
        if (matches.Length == 0)
        {
            return false;
        }
        if (matches.Length > 1 && matches[0].Version.CompareTo(matches[1].Version) == 0)
        {
            return false;
        }

        directory = matches[0];
        return true;
    }

    private sealed class SourceDataContext : ICharacterSourceDataContext
    {
        private readonly ContentOverlayCatalog _catalog;
        private readonly IReadOnlyList<CustomDirectory> _customDirectories;
        private readonly IReadOnlySet<string> _enabledSourcebooks;
        private readonly string _settingsProfileId;
        private readonly string _rawProfileInputsDigest;
        private readonly string _selectedCustomDataInputsDigest;
        private readonly string _rawMetatypesXmlDigest;
        private readonly string _effectiveMetatypesInputsDigest;
        private readonly string _rawPrioritiesXmlDigest;
        private readonly string _effectivePrioritiesInputsDigest;
        private readonly string _selectedPriorityCustomDataInputsDigest;
        private readonly string _buildMethod;
        private readonly int? _buildPoints;
        private readonly IReadOnlyList<string> _lifeModuleBudgetBlockers;
        private readonly string _prerequisiteBuildMethod;
        private readonly int? _creationKarmaTotal;
        private readonly IReadOnlyList<string> _priorityArray;
        private readonly string _priorityTable;
        private readonly int? _sumToTenTarget;
        private readonly IReadOnlyList<string> _prerequisiteProfileBlockers;
        private readonly int? _maxNumberMaxAttributesCreate;
        private readonly int? _karmaAttribute;
        private readonly bool? _alternateMetatypeAttributeKarma;
        private readonly bool? _reverseAttributePriorityOrder;
        private readonly int? _metatypeKarmaMultiplier;
        private readonly int? _minimumInitiativeDice;
        private readonly bool? _droneMods;
        private readonly IReadOnlyList<string> _metatypeProfileBlockers;
        private readonly int? _maximumNuyenDecimals;
        private readonly int? _joinGroupKarma;
        private readonly int? _leaveGroupKarma;
        private readonly decimal? _workingForPeopleRate;
        private readonly decimal? _workingForManRate;
        private readonly int? _essenceDecimals;
        private readonly bool _doNotRoundEssenceInternally;
        private readonly string _essenceModifierPostExpression;

        public SourceDataContext(
            ContentOverlayCatalog catalog,
            IReadOnlyList<CustomDirectory> customDirectories,
            IReadOnlyList<string> enabledSourcebooks,
            string settingsProfileId,
            string rawProfileInputsDigest,
            string selectedCustomDataInputsDigest,
            string rawMetatypesXmlDigest,
            string effectiveMetatypesInputsDigest,
            string rawPrioritiesXmlDigest,
            string effectivePrioritiesInputsDigest,
            string selectedPriorityCustomDataInputsDigest,
            string buildMethod,
            int? buildPoints,
            IReadOnlyList<string> lifeModuleBudgetBlockers,
            string prerequisiteBuildMethod,
            int? creationKarmaTotal,
            IReadOnlyList<string> priorityArray,
            string priorityTable,
            int? sumToTenTarget,
            IReadOnlyList<string> prerequisiteProfileBlockers,
            int? maxNumberMaxAttributesCreate,
            int? karmaAttribute,
            bool? alternateMetatypeAttributeKarma,
            bool? reverseAttributePriorityOrder,
            int? metatypeKarmaMultiplier,
            int? minimumInitiativeDice,
            bool? droneMods,
            IReadOnlyList<string> metatypeProfileBlockers,
            int? maximumNuyenDecimals,
            int? joinGroupKarma,
            int? leaveGroupKarma,
            decimal? workingForPeopleRate,
            decimal? workingForManRate,
            int? essenceDecimals,
            bool doNotRoundEssenceInternally,
            string essenceModifierPostExpression)
        {
            _catalog = catalog;
            _customDirectories = customDirectories;
            _enabledSourcebooks = enabledSourcebooks.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _settingsProfileId = settingsProfileId;
            _rawProfileInputsDigest = rawProfileInputsDigest;
            _selectedCustomDataInputsDigest = selectedCustomDataInputsDigest;
            _rawMetatypesXmlDigest = rawMetatypesXmlDigest;
            _effectiveMetatypesInputsDigest = effectiveMetatypesInputsDigest;
            _rawPrioritiesXmlDigest = rawPrioritiesXmlDigest;
            _effectivePrioritiesInputsDigest = effectivePrioritiesInputsDigest;
            _selectedPriorityCustomDataInputsDigest = selectedPriorityCustomDataInputsDigest;
            _buildMethod = buildMethod;
            _buildPoints = buildPoints;
            _lifeModuleBudgetBlockers = lifeModuleBudgetBlockers;
            _prerequisiteBuildMethod = prerequisiteBuildMethod;
            _creationKarmaTotal = creationKarmaTotal;
            _priorityArray = priorityArray;
            _priorityTable = priorityTable;
            _sumToTenTarget = sumToTenTarget;
            _prerequisiteProfileBlockers = prerequisiteProfileBlockers;
            _maxNumberMaxAttributesCreate = maxNumberMaxAttributesCreate;
            _karmaAttribute = karmaAttribute;
            _alternateMetatypeAttributeKarma = alternateMetatypeAttributeKarma;
            _reverseAttributePriorityOrder = reverseAttributePriorityOrder;
            _metatypeKarmaMultiplier = metatypeKarmaMultiplier;
            _minimumInitiativeDice = minimumInitiativeDice;
            _droneMods = droneMods;
            _metatypeProfileBlockers = metatypeProfileBlockers;
            _maximumNuyenDecimals = maximumNuyenDecimals;
            _joinGroupKarma = joinGroupKarma;
            _leaveGroupKarma = leaveGroupKarma;
            _workingForPeopleRate = workingForPeopleRate;
            _workingForManRate = workingForManRate;
            _essenceDecimals = essenceDecimals;
            _doNotRoundEssenceInternally = doNotRoundEssenceInternally;
            _essenceModifierPostExpression = essenceModifierPostExpression;
        }

        public bool TryResolveMaxNuyenDecimals(out int decimalPlaces)
        {
            decimalPlaces = _maximumNuyenDecimals.GetValueOrDefault();
            return _maximumNuyenDecimals.HasValue;
        }

        public bool TryResolveCreationSourceProfile(
            out CharacterCreationSourceProfileAuthority authority)
        {
            if (string.IsNullOrWhiteSpace(_settingsProfileId)
                || string.IsNullOrWhiteSpace(_rawProfileInputsDigest))
            {
                authority = CharacterCreationSourceProfileAuthority.Unavailable;
                return false;
            }

            authority = new CharacterCreationSourceProfileAuthority(
                SettingsProfileId: _settingsProfileId,
                EnabledSourcebooks: _enabledSourcebooks
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                BuildMethod: _buildMethod,
                BuildPoints: _buildPoints,
                LifeModuleBudgetIsExact: _lifeModuleBudgetBlockers.Count == 0
                    && _buildPoints.HasValue,
                BudgetBlockers: _lifeModuleBudgetBlockers,
                RawProfileInputsDigest: _rawProfileInputsDigest,
                SourceAnchorIds: [$"settings.xml#setting:{_settingsProfileId}"]);
            return true;
        }

        public bool TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority)
        {
            authority = CharacterCreationPrerequisiteAuthority.Unavailable;
            if (string.IsNullOrWhiteSpace(_settingsProfileId)
                || !TryComputeEffectiveInputDigest(
                    _catalog,
                    "settings.xml",
                    out string currentSettingsInputsDigest)
                || !TryComputeRawBaseFileDigest(
                    _catalog,
                    "priorities.xml",
                    out string currentRawPrioritiesXmlDigest)
                || !TryComputeEffectiveInputDigest(
                    _catalog,
                    "priorities.xml",
                    out string currentEffectivePrioritiesInputsDigest)
                || !TryComputeSelectedPriorityCustomDataInputsDigest(
                    _customDirectories,
                    out string currentPriorityCustomDataInputsDigest)
                || !TryLoadCreationPriorityDocument(
                    _catalog,
                    _customDirectories,
                    out XDocument? document,
                    out bool customDataUnsupported,
                    out string[] customSourceAnchors)
                || document?.Root is null
                || !TryComputeSelectedCustomDataInputsDigest(
                    _customDirectories,
                    out string currentCustomDataInputsDigest)
                || !TryHasSelectedCustomDataInputFor(
                    _customDirectories,
                    "metatypes.xml",
                    out bool hasMetatypeCustomData)
                || !TryComputeRawBaseFileDigest(
                    _catalog,
                    "metatypes.xml",
                    out string currentRawMetatypesXmlDigest)
                || !TryComputeEffectiveInputDigest(
                    _catalog,
                    "metatypes.xml",
                    out string currentEffectiveMetatypesInputsDigest)
                || !TryLoadEffectiveDocument(
                    _catalog,
                    "metatypes.xml",
                    out XDocument? metatypesDocument)
                || metatypesDocument?.Root is null
                || !TryHasEnabledOverlayInput(
                    _catalog,
                    "metatypes.xml",
                    out bool hasMetatypeOverlay))
            {
                return false;
            }

            var blockers = new List<string>(_prerequisiteProfileBlockers);
            if (!string.Equals(
                    BindSelectedProfile(currentSettingsInputsDigest, _settingsProfileId),
                    _rawProfileInputsDigest,
                    StringComparison.Ordinal))
            {
                blockers.Add(CharacterCreationPrerequisiteBlockers.SettingsProfileDrift);
            }
            if (string.IsNullOrWhiteSpace(_rawPrioritiesXmlDigest)
                || string.IsNullOrWhiteSpace(_effectivePrioritiesInputsDigest)
                || string.IsNullOrWhiteSpace(_selectedPriorityCustomDataInputsDigest))
            {
                blockers.Add(CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
            }
            else if (!string.Equals(
                         currentRawPrioritiesXmlDigest,
                         _rawPrioritiesXmlDigest,
                         StringComparison.Ordinal)
                     || !string.Equals(
                         currentEffectivePrioritiesInputsDigest,
                         _effectivePrioritiesInputsDigest,
                         StringComparison.Ordinal)
                     || !string.Equals(
                         currentPriorityCustomDataInputsDigest,
                         _selectedPriorityCustomDataInputsDigest,
                         StringComparison.Ordinal))
            {
                blockers.Add(CharacterCreationPrerequisiteBlockers.PrioritiesSourceDrift);
            }
            if (customDataUnsupported)
            {
                blockers.Add(CharacterCreationPrerequisiteBlockers.PriorityCustomDataUnsupported);
            }
            if (string.IsNullOrWhiteSpace(_selectedCustomDataInputsDigest))
            {
                blockers.Add(CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
            }
            else if (!string.Equals(
                         currentCustomDataInputsDigest,
                         _selectedCustomDataInputsDigest,
                         StringComparison.Ordinal))
            {
                blockers.Add(CharacterCreationPrerequisiteBlockers.CustomDataDrift);
            }
            if (hasMetatypeCustomData)
            {
                blockers.Add(CharacterCreationPrerequisiteBlockers.MetatypeCustomDataUnsupported);
            }
            if (hasMetatypeOverlay)
            {
                blockers.Add(CharacterCreationPrerequisiteBlockers.MetatypeOverlayUnsupported);
            }
            if (!string.Equals(
                    currentRawMetatypesXmlDigest,
                    _rawMetatypesXmlDigest,
                    StringComparison.Ordinal)
                || !string.Equals(
                    currentEffectiveMetatypesInputsDigest,
                    _effectiveMetatypesInputsDigest,
                    StringComparison.Ordinal))
            {
                blockers.Add(CharacterCreationPrerequisiteBlockers.MetatypeSourceDrift);
            }

            var projectionContext = new CharacterCreationPrerequisiteProjectionContext(
                SettingsProfileId: _settingsProfileId,
                BuildMethod: _prerequisiteBuildMethod,
                CreationKarmaTotal: _creationKarmaTotal,
                PriorityArray: _priorityArray,
                PriorityTable: _priorityTable,
                SumToTenTarget: _sumToTenTarget,
                RawProfileInputsDigest: _rawProfileInputsDigest,
                RawPrioritiesXmlDigest: _rawPrioritiesXmlDigest,
                EffectivePrioritiesInputsDigest: _effectivePrioritiesInputsDigest,
                SelectedPriorityCustomDataInputsDigest: _selectedPriorityCustomDataInputsDigest,
                SelectedCustomDataInputsDigest: _selectedCustomDataInputsDigest,
                RawMetatypesXmlDigest: _rawMetatypesXmlDigest,
                EffectiveMetatypesInputsDigest: _effectiveMetatypesInputsDigest,
                EnabledSourcebooks: _enabledSourcebooks
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                MaxNumberMaxAttributesCreate: _maxNumberMaxAttributesCreate,
                KarmaAttribute: _karmaAttribute,
                AlternateMetatypeAttributeKarma: _alternateMetatypeAttributeKarma,
                ReverseAttributePriorityOrder: _reverseAttributePriorityOrder,
                SourceAnchorIds:
                [
                    $"settings.xml#setting:{_settingsProfileId}",
                    "priorities.xml",
                    "metatypes.xml",
                    .. _customDirectories.Select(directory => $"customdata:{directory.Name}"),
                    .. customSourceAnchors
                ],
                Blockers: blockers);
            authority = CharacterCreationPrerequisiteAuthorityProjector.Project(
                document,
                metatypesDocument,
                projectionContext);
            return true;
        }

        public bool TryResolveCreationMetatypeCatalog(
            out CharacterCreationMetatypeCatalogAuthority authority)
        {
            authority = CharacterCreationMetatypeCatalogAuthority.Unavailable;
            if (string.IsNullOrWhiteSpace(_settingsProfileId)
                || string.IsNullOrWhiteSpace(_rawProfileInputsDigest)
                || string.IsNullOrWhiteSpace(_selectedCustomDataInputsDigest)
                || !TryComputeEffectiveInputDigest(_catalog, "settings.xml", out string currentSettingsInputsDigest)
                || !TryComputeSelectedCustomDataInputsDigest(
                    _customDirectories,
                    out string currentCustomDataInputsDigest)
                || !TryComputeRawBaseFileDigest(
                    _catalog,
                    "metatypes.xml",
                    out string currentRawMetatypesXmlDigest)
                || !TryComputeEffectiveInputDigest(
                    _catalog,
                    "metatypes.xml",
                    out string currentEffectiveMetatypesInputsDigest)
                || !TryLoadEffectiveDocument(_catalog, "metatypes.xml", out XDocument? document)
                || document?.Root is null
                || !TryHasEnabledOverlayInput(
                    _catalog,
                    "metatypes.xml",
                    out bool hasMetatypeOverlay))
            {
                return false;
            }

            var blockers = new List<string>(_metatypeProfileBlockers);
            if (string.IsNullOrWhiteSpace(_rawMetatypesXmlDigest)
                || string.IsNullOrWhiteSpace(_effectiveMetatypesInputsDigest))
            {
                blockers.Add(CharacterCreationMetatypeCatalogBlockers.AuthorityUnavailable);
            }
            else if (!string.Equals(
                         currentRawMetatypesXmlDigest,
                         _rawMetatypesXmlDigest,
                         StringComparison.Ordinal)
                     || !string.Equals(
                         currentEffectiveMetatypesInputsDigest,
                         _effectiveMetatypesInputsDigest,
                         StringComparison.Ordinal))
            {
                blockers.Add(CharacterCreationMetatypeCatalogBlockers.MetatypesSourceDrift);
            }
            if (!string.Equals(
                    _settingsProfileId,
                    CanonicalLifeModuleSettingsProfileId,
                    StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add(CharacterCreationMetatypeCatalogBlockers.ProfileUnsupported);
            }
            if (!string.Equals(
                    BindSelectedProfile(currentSettingsInputsDigest, _settingsProfileId),
                    _rawProfileInputsDigest,
                    StringComparison.Ordinal))
            {
                blockers.Add(CharacterCreationMetatypeCatalogBlockers.ProfileSettingsDrift);
            }
            if (!string.Equals(
                    currentCustomDataInputsDigest,
                    _selectedCustomDataInputsDigest,
                    StringComparison.Ordinal))
            {
                blockers.Add(CharacterCreationMetatypeCatalogBlockers.CustomDataDrift);
            }
            if (_customDirectories.Count != 0)
            {
                blockers.Add(CharacterCreationMetatypeCatalogBlockers.CustomDataUnsupported);
            }
            if (hasMetatypeOverlay)
            {
                blockers.Add(CharacterCreationMetatypeCatalogBlockers.OverlayUnsupported);
            }

            string[] orderedBlockers = blockers
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            string[] enabledSourcebooks = _enabledSourcebooks
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string authorityDigest = ComputeMetatypeAuthorityDigest(
                _rawMetatypesXmlDigest,
                _effectiveMetatypesInputsDigest,
                _rawProfileInputsDigest,
                _selectedCustomDataInputsDigest,
                _settingsProfileId,
                _metatypeKarmaMultiplier,
                _minimumInitiativeDice,
                _droneMods,
                enabledSourcebooks);
            var sourceContext = new CharacterCreationMetatypeSourceContextAuthority(
                SettingsProfileId: _settingsProfileId,
                RawMetatypesXmlDigest: _rawMetatypesXmlDigest,
                EffectiveMetatypesInputsDigest: _effectiveMetatypesInputsDigest,
                RawProfileInputsDigest: _rawProfileInputsDigest,
                SelectedCustomDataInputsDigest: _selectedCustomDataInputsDigest,
                AuthorityDigest: authorityDigest,
                MetatypeKarmaMultiplier: _metatypeKarmaMultiplier,
                MinimumInitiativeDiceFallback: _minimumInitiativeDice,
                DroneMods: _droneMods,
                EnabledSourcebooks: enabledSourcebooks,
                SourceAnchorIds:
                [
                    "metatypes.xml",
                    $"settings.xml#setting:{_settingsProfileId}",
                    .. _customDirectories.Select(directory => $"customdata:{directory.Name}")
                ],
                Blockers: orderedBlockers,
                IsAuthoritative: orderedBlockers.Length == 0
                    && _metatypeKarmaMultiplier.HasValue
                    && _minimumInitiativeDice.HasValue
                    && _droneMods.HasValue);
            authority = CharacterCreationMetatypeCatalogProjector.Project(document, sourceContext);
            return true;
        }

        public bool TryResolveGroupMembershipKarmaCosts(out int joinCost, out int leaveCost)
        {
            joinCost = _joinGroupKarma.GetValueOrDefault();
            leaveCost = _leaveGroupKarma.GetValueOrDefault();
            return _joinGroupKarma.HasValue && _leaveGroupKarma.HasValue;
        }

        public bool TryResolveKarmaNuyenExchangeRates(
            out decimal workingForPeopleRate,
            out decimal workingForManRate)
        {
            workingForPeopleRate = _workingForPeopleRate.GetValueOrDefault();
            workingForManRate = _workingForManRate.GetValueOrDefault();
            return _workingForPeopleRate.HasValue && _workingForManRate.HasValue;
        }

        public bool TryIsBookEnabled(string sourceCode, out bool enabled)
        {
            enabled = false;
            if (string.IsNullOrWhiteSpace(sourceCode))
            {
                return false;
            }

            enabled = _enabledSourcebooks.Contains(sourceCode.Trim());
            return true;
        }

        public bool TryResolveCyberwareGradeDeviceRating(
            string gradeName,
            string improvementSource,
            out int deviceRating)
        {
            deviceRating = 0;
            string fileName;
            if (string.Equals(improvementSource, "Cyberware", StringComparison.Ordinal))
            {
                fileName = "cyberware.xml";
            }
            else if (string.Equals(improvementSource, "Bioware", StringComparison.Ordinal))
            {
                fileName = "bioware.xml";
            }
            else
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(gradeName)
                || !TryResolveTarget(
                    fileName,
                    ["grades"],
                    "grade",
                    sourceId: string.Empty,
                    gradeName,
                    out XElement? grade)
                || grade is null)
            {
                return false;
            }

            string configuredRating = ReadValue(grade, "devicerating");
            if (!string.IsNullOrEmpty(configuredRating))
            {
                return int.TryParse(
                    configuredRating,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out deviceRating);
            }

            deviceRating = gradeName.Contains("Alphaware", StringComparison.Ordinal) ? 3
                : gradeName.Contains("Betaware", StringComparison.Ordinal) ? 4
                : gradeName.Contains("Deltaware", StringComparison.Ordinal) ? 5
                : gradeName.Contains("Gammaware", StringComparison.Ordinal) ? 6
                : 2;
            return true;
        }

        public bool TryResolveCyberwareCommerceSource(
            string sourceId,
            string name,
            string improvementSource,
            out CharacterCyberwareCommerceSource source)
        {
            source = CharacterCyberwareCommerceSource.Unavailable;
            if (!string.Equals(improvementSource, "Cyberware", StringComparison.Ordinal)
                || _customDirectories.Count != 0
                || !TryLoadEffectiveDocument(_catalog, "cyberware.xml", out XDocument? document)
                || document?.Root is null
                || !TryResolveTarget(
                    "cyberware.xml",
                    ["cyberwares"],
                    "cyberware",
                    sourceId,
                    name,
                    out XElement? item)
                || item is null)
            {
                return false;
            }

            if (_essenceDecimals is not int essenceDecimals
                || !string.Equals(ReadValue(item, "id"), sourceId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string itemSource = ReadValue(item, "source");
            if (!string.IsNullOrWhiteSpace(itemSource)
                && (!_enabledSourcebooks.Contains(itemSource) || _enabledSourcebooks.Count == 0))
            {
                return false;
            }

            List<CharacterCyberwareCommerceGradeSource> grades = [];
            foreach (XElement grade in document.Root.Element("grades")?.Elements("grade") ?? [])
            {
                string gradeSource = ReadValue(grade, "source");
                if (!string.IsNullOrWhiteSpace(gradeSource) && !_enabledSourcebooks.Contains(gradeSource))
                {
                    continue;
                }
                if (!Guid.TryParse(ReadValue(grade, "id"), out Guid gradeId)
                    || gradeId == Guid.Empty
                    || !decimal.TryParse(
                        ReadValue(grade, "cost"),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out decimal costMultiplier)
                    || !decimal.TryParse(
                        ReadValue(grade, "ess"),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out decimal essenceMultiplier)
                    || costMultiplier < 0m
                    || essenceMultiplier < 0m)
                {
                    return false;
                }

                string gradeName = ReadValue(grade, "name");
                HashSet<string> simpleFields = new(StringComparer.Ordinal)
                {
                    "id", "name", "translate", "ess", "cost", "devicerating", "avail", "source", "page", "altpage"
                };
                bool specialSemantics = string.IsNullOrWhiteSpace(gradeName)
                    || gradeName.Contains('(')
                    || grade.Elements().Any(element => !simpleFields.Contains(element.Name.LocalName));
                grades.Add(new CharacterCyberwareCommerceGradeSource(
                    gradeId.ToString("D"),
                    gradeName,
                    costMultiplier,
                    essenceMultiplier,
                    gradeSource,
                    specialSemantics));
            }

            if (grades.Count == 0)
            {
                return false;
            }

            HashSet<string> unsafeSourceFields = new(StringComparer.Ordinal)
            {
                "bonus", "pairbonus", "wirelessbonus", "wirelesspairbonus",
                "gears", "weapons", "vehicles", "addweapon", "addvehicle",
                "modularmount", "plugsintomodularmount"
            };
            bool unsafeSemantics = item.Elements().Any(element =>
                unsafeSourceFields.Contains(element.Name.LocalName));
            source = new CharacterCyberwareCommerceSource(
                SourceId: ReadValue(item, "id"),
                Name: ReadValue(item, "name"),
                Source: itemSource,
                MinimumRatingExpression: ReadValue(item, "minrating"),
                MaximumRatingExpression: ReadValue(item, "rating"),
                CostExpression: ReadValue(item, "cost"),
                EssenceExpression: ReadValue(item, "ess"),
                CapacityExpression: ReadValue(item, "capacity"),
                ForcedGrade: ReadValue(item, "forcegrade"),
                BannedGrades: item.Element("bannedgrades")?
                    .Elements("grade")
                    .Select(node => node.Value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray()
                    ?? [],
                Grades: grades,
                EssenceDecimals: essenceDecimals,
                DoNotRoundEssenceInternally: _doNotRoundEssenceInternally,
                EssenceModifierPostExpression: _essenceModifierPostExpression,
                SourceEntryUsesGeneratedOrImprovementSemantics: unsafeSemantics);
            return true;
        }

        public bool TryResolveQualityLevelSource(
            string sourceId,
            string name,
            out CharacterQualityLevelSource source)
        {
            source = CharacterQualityLevelSource.Unavailable;
            if (!TryResolveTarget(
                    "qualities.xml",
                    ["qualities"],
                    "quality",
                    sourceId,
                    name,
                    out XElement? quality)
                || quality is null
                || !Guid.TryParse(ReadValue(quality, "id"), out Guid parsedSourceId)
                || parsedSourceId == Guid.Empty
                || !int.TryParse(
                    ReadValue(quality, "limit"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int maximumLevel)
                || maximumLevel <= 0)
            {
                return false;
            }

            string sourceBook = ReadValue(quality, "source");
            if (!string.IsNullOrWhiteSpace(sourceBook)
                && (!_enabledSourcebooks.Contains(sourceBook) || _enabledSourcebooks.Count == 0))
            {
                return false;
            }

            HashSet<string> safeFields = new(StringComparer.Ordinal)
            {
                "id", "name", "translate", "karma", "category", "limit",
                "bonus", "source", "page", "altpage"
            };
            bool unsupported = quality.Elements().Any(element =>
                    !safeFields.Contains(element.Name.LocalName))
                || quality.Elements("bonus").Any(element =>
                    element.HasElements || !string.IsNullOrWhiteSpace(element.Value));

            source = new CharacterQualityLevelSource(
                SourceId: parsedSourceId.ToString("D"),
                Name: ReadValue(quality, "name"),
                QualityType: ReadValue(quality, "category"),
                MaximumLevel: maximumLevel,
                NoLevels: quality.Element("nolevels") is not null,
                UsesUnsupportedSemantics: unsupported);
            return true;
        }

        public bool TryResolveTraditionDrainExpressions(out IReadOnlyList<string> expressions)
        {
            expressions = Array.Empty<string>();
            if (!TryLoadEffectiveDocument(_catalog, "traditions.xml", out XDocument? document)
                || document?.Root is null)
            {
                return false;
            }

            XElement[] containers = document.Root.Elements("drainattributes").Take(2).ToArray();
            if (containers.Length != 1)
            {
                return false;
            }

            var values = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (XElement entry in containers[0].Elements("drainattribute"))
            {
                XElement[] names = entry.Elements("name").Take(2).ToArray();
                if (names.Length != 1
                    || string.IsNullOrWhiteSpace(names[0].Value)
                    || !seen.Add(names[0].Value))
                {
                    return false;
                }
                values.Add(names[0].Value);
            }

            if (values.Count == 0)
            {
                return false;
            }
            expressions = values;
            return true;
        }

        public bool TryResolveSpiritCatalogNames(
            string entityType,
            out IReadOnlyList<string> names)
        {
            names = Array.Empty<string>();
            string fileName = entityType switch
            {
                "Spirit" => "traditions.xml",
                "Sprite" => "streams.xml",
                _ => string.Empty
            };
            if (fileName.Length == 0
                || !TryLoadEffectiveDocument(_catalog, fileName, out XDocument? document)
                || document?.Root is null)
            {
                return false;
            }

            XElement[] containers = document.Root.Elements("spirits").Take(2).ToArray();
            if (containers.Length != 1)
            {
                return false;
            }

            var locators = new List<TargetLocator>();
            var locatorKeys = new HashSet<string>(StringComparer.Ordinal);
            bool TryAddLocator(XElement entry, bool duplicateIsError)
            {
                if (!string.Equals(entry.Name.LocalName, "spirit", StringComparison.Ordinal))
                {
                    return false;
                }
                XElement[] ids = entry.Elements("id").Take(2).ToArray();
                XElement[] nameElements = entry.Elements("name").Take(2).ToArray();
                if (ids.Length > 1 || nameElements.Length > 1)
                {
                    return false;
                }

                string id = ids.SingleOrDefault()?.Value.Trim() ?? string.Empty;
                string name = nameElements.SingleOrDefault()?.Value ?? string.Empty;
                if (id.Length != 0
                    && (!Guid.TryParseExact(id, "D", out Guid parsedId) || parsedId == Guid.Empty))
                {
                    return false;
                }
                if (id.Length == 0 && string.IsNullOrWhiteSpace(name))
                {
                    return false;
                }

                TargetLocator locator = TargetLocator.Create(id, name);
                string key = locator.SourceId is Guid sourceId
                    ? $"id:{sourceId:D}"
                    : $"name:{locator.Name}";
                if (!locatorKeys.Add(key))
                {
                    return !duplicateIsError;
                }
                locators.Add(locator);
                return true;
            }

            foreach (XElement entry in containers[0].Elements())
            {
                if (!TryAddLocator(entry, duplicateIsError: true))
                {
                    return false;
                }
            }

            foreach (CustomDirectory directory in _customDirectories)
            {
                string[] relevantFiles;
                try
                {
                    relevantFiles = Directory
                        .EnumerateFiles(directory.Path, $"*_{fileName}", SearchOption.AllDirectories)
                        .Where(path =>
                        {
                            string candidate = Path.GetFileName(path);
                            return candidate.StartsWith("override_", StringComparison.OrdinalIgnoreCase)
                                || candidate.StartsWith("custom_", StringComparison.OrdinalIgnoreCase)
                                || candidate.StartsWith("amend_", StringComparison.OrdinalIgnoreCase);
                        })
                        .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(path => path, StringComparer.Ordinal)
                        .ToArray();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return false;
                }

                foreach (string prefix in new[] { "override_", "custom_", "amend_" })
                {
                    foreach (string path in relevantFiles.Where(path =>
                                 Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!TryLoadXml(path, out XDocument? customDocument)
                            || customDocument?.Root is null)
                        {
                            return false;
                        }
                        XElement[] customContainers = customDocument.Root.Elements("spirits").Take(2).ToArray();
                        if (customContainers.Length > 1)
                        {
                            return false;
                        }
                        if (customContainers.Length == 0)
                        {
                            continue;
                        }
                        foreach (XElement entry in customContainers[0].Elements())
                        {
                            if (!TryAddLocator(entry, duplicateIsError: false))
                            {
                                return false;
                            }
                        }
                    }
                }
            }

            var values = new List<string>(locators.Count);
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (TargetLocator locator in locators)
            {
                string sourceId = locator.SourceId?.ToString("D") ?? string.Empty;
                if (!TryResolveTarget(
                        fileName,
                        ["spirits"],
                        "spirit",
                        sourceId,
                        locator.Name,
                        out XElement? resolved))
                {
                    return false;
                }
                if (resolved is null)
                {
                    continue;
                }

                XElement[] nameElements = resolved.Elements("name").Take(2).ToArray();
                if (nameElements.Length != 1
                    || string.IsNullOrWhiteSpace(nameElements[0].Value)
                    || nameElements[0].Value.Length > CharacterSpiritNameChoiceRules.MaximumNameLength
                    || nameElements[0].Value.IndexOfAny(['\r', '\n', '\0']) >= 0
                    || !seenNames.Add(nameElements[0].Value))
                {
                    return false;
                }
                values.Add(nameElements[0].Value);
            }
            if (values.Count == 0)
            {
                return false;
            }
            names = values;
            return true;
        }

        public bool TryResolveTraditionSpiritNames(
            string entityType,
            string sourceId,
            out IReadOnlyList<string> names)
        {
            names = Array.Empty<string>();
            string fileName = entityType switch
            {
                "Spirit" => "traditions.xml",
                "Sprite" => "streams.xml",
                _ => string.Empty
            };
            if (fileName.Length == 0
                || !Guid.TryParseExact(sourceId, "D", out Guid parsedSourceId)
                || parsedSourceId == Guid.Empty
                || !TryResolveTarget(
                    fileName,
                    ["traditions"],
                    "tradition",
                    sourceId,
                    string.Empty,
                    out XElement? tradition)
                || tradition is null)
            {
                return false;
            }

            XElement[] containers = tradition.Elements("spirits").Take(2).ToArray();
            if (containers.Length != 1)
            {
                return false;
            }

            var values = new List<string>();
            foreach (XElement entry in containers[0].Elements())
            {
                string value = entry.Value;
                if (string.IsNullOrWhiteSpace(value)
                    || value.Length > CharacterSpiritNameChoiceRules.MaximumNameLength
                    || value.IndexOfAny(['\r', '\n', '\0']) >= 0)
                {
                    return false;
                }
                values.Add(value);
            }
            if (values.Count == 0)
            {
                return false;
            }
            names = values;
            return true;
        }

        public bool TryResolveVehicleModBonuses(
            string sourceId,
            string name,
            out CharacterVehicleModSourceBonuses bonuses)
        {
            bonuses = CharacterVehicleModSourceBonuses.Empty;
            if (!TryResolveTarget(
                    "vehicles.xml",
                    ["mods", "weaponmountmods"],
                    "mod",
                    sourceId,
                    name,
                    out XElement? modifier))
            {
                return false;
            }

            if (modifier is null)
            {
                return true;
            }

            XElement? bonus = modifier.Element("bonus");
            XElement? wirelessBonus = modifier.Element("wirelessbonus");
            bonuses = new CharacterVehicleModSourceBonuses(
                BodyExpression: ReadValue(bonus, "body"),
                DeviceRatingExpression: ReadValue(bonus, "devicerating"),
                MatrixConditionExpression: ReadValue(bonus, "matrixcmbonus"),
                WirelessBodyExpression: ReadValue(wirelessBonus, "body"),
                WirelessDeviceRatingExpression: ReadValue(wirelessBonus, "devicerating"),
                WirelessMatrixConditionExpression: ReadValue(wirelessBonus, "matrixcmbonus"));
            return true;
        }

        private bool TryResolveTarget(
            string fileName,
            IReadOnlyList<string> containerNames,
            string entryName,
            string sourceId,
            string name,
            out XElement? target)
        {
            target = null;
            if (!TryLoadEffectiveDocument(_catalog, fileName, out XDocument? document)
                || document?.Root is null)
            {
                return false;
            }

            TargetLocator locator = TargetLocator.Create(sourceId, name);
            target = FindTarget(document.Root, containerNames, entryName, locator);
            foreach (CustomDirectory directory in _customDirectories)
            {
                string[] relevantFiles;
                try
                {
                    relevantFiles = Directory
                        .EnumerateFiles(directory.Path, $"*_{fileName}", SearchOption.AllDirectories)
                        .Where(path =>
                        {
                            string candidate = Path.GetFileName(path);
                            return candidate.StartsWith("override_", StringComparison.OrdinalIgnoreCase)
                                || candidate.StartsWith("custom_", StringComparison.OrdinalIgnoreCase)
                                || candidate.StartsWith("amend_", StringComparison.OrdinalIgnoreCase);
                        })
                        .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(path => path, StringComparer.Ordinal)
                        .ToArray();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return false;
                }

                foreach (string prefix in new[] { "override_", "custom_", "amend_" })
                {
                    foreach (string path in relevantFiles.Where(path =>
                                 Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!TryLoadXml(path, out XDocument? customDocument)
                            || customDocument?.Root is null
                            || !TryApplyCustomFile(
                                customDocument.Root,
                                prefix,
                                containerNames,
                                entryName,
                                locator,
                                ref target))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }
    }

    private readonly record struct TargetLocator(Guid? SourceId, string Name)
    {
        public static TargetLocator Create(string sourceId, string name)
            => Guid.TryParse(sourceId, out Guid parsed) && parsed != Guid.Empty
                ? new TargetLocator(parsed, name?.Trim() ?? string.Empty)
                : new TargetLocator(null, name?.Trim() ?? string.Empty);
    }

    private static XElement? FindTarget(
        XElement root,
        IReadOnlyList<string> containerNames,
        string entryName,
        TargetLocator locator)
    {
        IEnumerable<XElement> entries = containerNames
            .SelectMany(containerName => root.Element(containerName)?.Elements(entryName) ?? []);
        return entries.FirstOrDefault(entry => LocatorMatches(entry, locator));
    }

    private static bool LocatorMatches(XElement entry, TargetLocator locator)
    {
        if (locator.SourceId is Guid sourceId)
        {
            return Guid.TryParse(ReadValue(entry, "id"), out Guid entryId) && entryId == sourceId;
        }

        return !string.IsNullOrEmpty(locator.Name)
            && string.Equals(ReadValue(entry, "name"), locator.Name, StringComparison.Ordinal);
    }

    private static bool TryApplyCustomFile(
        XElement customRoot,
        string prefix,
        IReadOnlyList<string> containerNames,
        string entryName,
        TargetLocator locator,
        ref XElement? target)
    {
        foreach (XElement entry in containerNames.SelectMany(containerName =>
                     customRoot.Element(containerName)?.Elements(entryName) ?? []))
        {
            if (string.Equals(prefix, "override_", StringComparison.OrdinalIgnoreCase))
            {
                if (target is not null && AmendmentMatchesTarget(entry, target, locator, out bool exact) && exact)
                {
                    target = StripAmendAttributes(new XElement(entry));
                }
                continue;
            }

            if (string.Equals(prefix, "custom_", StringComparison.OrdinalIgnoreCase))
            {
                if (target is null && LocatorMatches(entry, locator))
                {
                    target = StripAmendAttributes(new XElement(entry));
                }
                continue;
            }

            if (!AmendmentMatchesTarget(entry, target, locator, out bool amendmentExact))
            {
                if (!amendmentExact)
                {
                    return false;
                }
                continue;
            }

            string operation = ReadOperation(entry);
            switch (operation)
            {
                case "ADDNODE":
                    if (target is null && LocatorMatches(entry, locator))
                    {
                        target = StripAmendAttributes(new XElement(entry));
                    }
                    break;
                case "REMOVE":
                    target = null;
                    break;
                case "REPLACE":
                    target = StripAmendAttributes(new XElement(entry));
                    break;
                case "":
                case "RECURSE":
                    if (target is null)
                    {
                        if (ParseBool(entry.Attribute("addifnotfound")?.Value))
                        {
                            target = StripAmendAttributes(new XElement(entry));
                        }
                        break;
                    }
                    if (!TryMergeAmendment(target, entry))
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static bool AmendmentMatchesTarget(
        XElement amendment,
        XElement? target,
        TargetLocator locator,
        out bool exact)
    {
        exact = true;
        string? xpathFilter = amendment.Attribute("xpathfilter")?.Value;
        if (xpathFilter is not null)
        {
            if (target is null)
            {
                return false;
            }

            try
            {
                return (bool)target.XPathEvaluate($"boolean(self::*[{xpathFilter}])");
            }
            catch (XPathException)
            {
                exact = false;
                return false;
            }
        }

        XElement? id = amendment.Element("id");
        if (id is not null)
        {
            if (!Guid.TryParse(id.Value.Trim(), out Guid amendmentId))
            {
                exact = false;
                return false;
            }
            Guid? targetId = target is not null && Guid.TryParse(ReadValue(target, "id"), out Guid parsedTargetId)
                ? parsedTargetId
                : locator.SourceId;
            return targetId == amendmentId;
        }

        XElement? name = amendment.Element("name");
        string operation = ReadOperation(amendment);
        bool nameIsIdentifier = name is not null
            && (string.Equals(operation, "REMOVE", StringComparison.Ordinal)
                || amendment.Elements().Any(child => child != name));
        if (nameIsIdentifier)
        {
            string targetName = target is null ? locator.Name : ReadValue(target, "name");
            if (!string.Equals(name!.Value.Trim(), targetName, StringComparison.Ordinal))
            {
                return false;
            }
        }

        foreach (XElement identifier in amendment.Elements()
                     .Where(child => ParseBool(child.Attribute("isidnode")?.Value)))
        {
            if (target is null
                || !string.Equals(ReadValue(target, identifier.Name.LocalName), identifier.Value.Trim(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryMergeAmendment(XElement target, XElement amendment)
    {
        foreach (XElement child in amendment.Elements())
        {
            if (!TryApplyAmendmentNode(target, child))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryApplyAmendmentNode(XElement targetParent, XElement amendment)
    {
        string operation = ReadOperation(amendment);
        string? xpathFilter = amendment.Attribute("xpathfilter")?.Value;
        List<XElement> candidates = targetParent.Elements(amendment.Name).ToList();
        if (xpathFilter is not null)
        {
            try
            {
                candidates = candidates
                    .Where(candidate => (bool)candidate.XPathEvaluate($"boolean(self::*[{xpathFilter}])"))
                    .ToList();
            }
            catch (XPathException)
            {
                return false;
            }
        }
        else
        {
            XElement? id = amendment.Element("id");
            XElement? name = amendment.Element("name");
            if (id is not null)
            {
                candidates = candidates.Where(candidate =>
                    string.Equals(ReadValue(candidate, "id"), id.Value.Trim(), StringComparison.Ordinal)).ToList();
            }
            else if (name is not null
                     && (string.Equals(operation, "REMOVE", StringComparison.Ordinal)
                         || amendment.Elements().Any(child => child != name)))
            {
                candidates = candidates.Where(candidate =>
                    string.Equals(ReadValue(candidate, "name"), name.Value.Trim(), StringComparison.Ordinal)).ToList();
            }

            XElement[] extraIdentifiers = amendment.Elements()
                .Where(child => ParseBool(child.Attribute("isidnode")?.Value))
                .ToArray();
            if (extraIdentifiers.Length > 0)
            {
                candidates = candidates.Where(candidate => extraIdentifiers.All(identifier =>
                    string.Equals(
                        ReadValue(candidate, identifier.Name.LocalName),
                        identifier.Value.Trim(),
                        StringComparison.Ordinal))).ToList();
            }
        }

        if (string.Equals(operation, "ADDNODE", StringComparison.Ordinal))
        {
            targetParent.Add(StripAmendAttributes(new XElement(amendment)));
            return true;
        }

        bool hasElementChildren = amendment.Elements().Any();
        if (string.IsNullOrEmpty(operation))
        {
            operation = hasElementChildren ? "RECURSE" : candidates.Count == 0 ? "APPEND" : "REPLACE";
        }

        if (candidates.Count == 0)
        {
            if (operation is "APPEND"
                || ParseBool(amendment.Attribute("addifnotfound")?.Value)
                || operation == "RECURSE")
            {
                targetParent.Add(StripAmendAttributes(new XElement(amendment)));
                return true;
            }
            return operation == "REMOVE";
        }

        foreach (XElement candidate in candidates)
        {
            switch (operation)
            {
                case "REMOVE":
                    candidate.Remove();
                    break;
                case "REPLACE":
                    candidate.ReplaceWith(StripAmendAttributes(new XElement(amendment)));
                    break;
                case "APPEND":
                    if (hasElementChildren)
                    {
                        candidate.Add(amendment.Nodes().Select(CloneCleanNode));
                    }
                    else
                    {
                        candidate.Value += amendment.Value;
                    }
                    break;
                case "RECURSE":
                    if (!TryMergeAmendment(candidate, amendment))
                    {
                        return false;
                    }
                    break;
                case "REGEXREPLACE":
                    string? pattern = amendment.Attribute("regexpattern")?.Value;
                    if (pattern is null)
                    {
                        candidate.ReplaceWith(StripAmendAttributes(new XElement(amendment)));
                        break;
                    }
                    try
                    {
                        candidate.Value = Regex.Replace(candidate.Value, pattern, amendment.Value);
                    }
                    catch (ArgumentException)
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static XNode CloneCleanNode(XNode node)
        => node switch
        {
            XElement element => StripAmendAttributes(new XElement(element)),
            XCData cdata => new XCData(cdata.Value),
            XText text => new XText(text.Value),
            _ => new XText(string.Empty)
        };

    private static XElement StripAmendAttributes(XElement element)
    {
        foreach (XElement current in element.DescendantsAndSelf())
        {
            current.Attribute("isidnode")?.Remove();
            current.Attribute("xpathfilter")?.Remove();
            current.Attribute("amendoperation")?.Remove();
            current.Attribute("addifnotfound")?.Remove();
            current.Attribute("regexpattern")?.Remove();
        }
        return element;
    }

    private static string ReadOperation(XElement element)
        => element.Attribute("amendoperation")?.Value.Trim().ToUpperInvariant() ?? string.Empty;

    private static bool TryLoadEffectiveDocument(
        ContentOverlayCatalog catalog,
        string fileName,
        out XDocument? document)
    {
        document = null;
        string basePath = Path.Combine(catalog.BaseDataPath, fileName);
        if (File.Exists(basePath) && !TryLoadXml(basePath, out document))
        {
            return false;
        }

        foreach (ContentOverlayPack pack in catalog.Overlays
                     .Where(pack => pack.Enabled)
                     .OrderBy(pack => pack.Priority)
                     .ThenBy(pack => pack.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(pack.DataPath) || !Directory.Exists(pack.DataPath))
            {
                continue;
            }

            if (string.Equals(pack.Mode, ContentOverlayModes.ReplaceFile, StringComparison.Ordinal))
            {
                string replacementPath = Path.Combine(pack.DataPath, fileName);
                if (File.Exists(replacementPath) && !TryLoadXml(replacementPath, out document))
                {
                    return false;
                }
                continue;
            }

            if (!string.Equals(pack.Mode, ContentOverlayModes.MergeCatalog, StringComparison.Ordinal))
            {
                return false;
            }

            foreach (string fragmentPath in Directory.EnumerateFiles(pack.DataPath, "*.xml", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                if (!string.Equals(ResolveCatalogTargetFileName(Path.GetFileName(fragmentPath)), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!TryLoadXml(fragmentPath, out XDocument? fragment) || fragment?.Root is null)
                {
                    return false;
                }
                document = document?.Root is null
                    ? new XDocument(fragment)
                    : MergeCatalogDocument(document, fragment);
            }
        }

        return document?.Root is not null;
    }

    private static bool TryComputeEffectiveInputDigest(
        ContentOverlayCatalog catalog,
        string fileName,
        out string digest)
    {
        digest = string.Empty;
        try
        {
            var inputs = new List<(string AuthorityId, string Path)>();
            string basePath = Path.Combine(catalog.BaseDataPath, fileName);
            if (File.Exists(basePath))
                inputs.Add(("base", basePath));

            foreach (ContentOverlayPack pack in catalog.Overlays
                         .Where(pack => pack.Enabled)
                         .OrderBy(pack => pack.Priority)
                         .ThenBy(pack => pack.Id, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(pack.DataPath) || !Directory.Exists(pack.DataPath))
                    continue;

                if (string.Equals(pack.Mode, ContentOverlayModes.ReplaceFile, StringComparison.Ordinal))
                {
                    string replacementPath = Path.Combine(pack.DataPath, fileName);
                    if (File.Exists(replacementPath))
                        inputs.Add(($"overlay:{pack.Id}:replace", replacementPath));
                    continue;
                }

                if (!string.Equals(pack.Mode, ContentOverlayModes.MergeCatalog, StringComparison.Ordinal))
                    return false;

                foreach (string fragmentPath in Directory.EnumerateFiles(
                             pack.DataPath,
                             "*.xml",
                             SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.Ordinal))
                {
                    if (string.Equals(
                            ResolveCatalogTargetFileName(Path.GetFileName(fragmentPath)),
                            fileName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        inputs.Add(($"overlay:{pack.Id}:merge:{Path.GetFileName(fragmentPath)}", fragmentPath));
                    }
                }
            }

            if (inputs.Count == 0)
                return false;

            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach ((string authorityId, string path) in inputs)
            {
                AppendFramed(hash, Encoding.UTF8.GetBytes(authorityId));
                AppendFramed(hash, File.ReadAllBytes(path));
            }

            digest = "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryComputeRawBaseFileDigest(
        ContentOverlayCatalog catalog,
        string fileName,
        out string digest)
    {
        digest = string.Empty;
        try
        {
            string path = Path.Combine(catalog.BaseDataPath, fileName);
            if (!File.Exists(path))
            {
                return false;
            }
            digest = "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryComputeSelectedCustomDataInputsDigest(
        IReadOnlyList<CustomDirectory> directories,
        out string digest)
    {
        digest = string.Empty;
        try
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendFramed(hash, Encoding.UTF8.GetBytes("selected-metatype-custom-data-inputs-v1"));
            foreach (CustomDirectory directory in directories)
            {
                AppendFramed(hash, Encoding.UTF8.GetBytes(directory.Name));
                AppendFramed(hash, Encoding.UTF8.GetBytes(directory.ManifestId?.ToString("D") ?? string.Empty));
                AppendFramed(hash, Encoding.UTF8.GetBytes(string.Join('.', directory.Version.Parts)));

                string manifestPath = Path.Combine(directory.Path, "manifest.xml");
                if (File.Exists(manifestPath))
                {
                    AppendFramed(hash, Encoding.UTF8.GetBytes("manifest.xml"));
                    AppendFramed(hash, File.ReadAllBytes(manifestPath));
                }

                foreach (string path in Directory.EnumerateFiles(directory.Path, "*.xml", SearchOption.AllDirectories)
                             .Where(path => IsLegacyCustomDataInputFor(path, "metatypes.xml"))
                             .OrderBy(path => Path.GetRelativePath(directory.Path, path), StringComparer.Ordinal))
                {
                    string relativePath = Path.GetRelativePath(directory.Path, path).Replace('\\', '/');
                    AppendFramed(hash, Encoding.UTF8.GetBytes(relativePath));
                    AppendFramed(hash, File.ReadAllBytes(path));
                }
            }
            digest = "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryComputeSelectedPriorityCustomDataInputsDigest(
        IReadOnlyList<CustomDirectory> directories,
        out string digest)
    {
        digest = string.Empty;
        try
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendFramed(hash, Encoding.UTF8.GetBytes("selected-priority-custom-data-inputs-v1"));
            foreach (CustomDirectory directory in directories)
            {
                AppendFramed(hash, Encoding.UTF8.GetBytes(directory.Name));
                AppendFramed(hash, Encoding.UTF8.GetBytes(
                    directory.ManifestId?.ToString("D") ?? string.Empty));
                AppendFramed(hash, Encoding.UTF8.GetBytes(
                    string.Join('.', directory.Version.Parts)));

                string manifestPath = Path.Combine(directory.Path, "manifest.xml");
                if (File.Exists(manifestPath))
                {
                    AppendFramed(hash, Encoding.UTF8.GetBytes("manifest.xml"));
                    AppendFramed(hash, File.ReadAllBytes(manifestPath));
                }

                foreach (string path in Directory.EnumerateFiles(
                             directory.Path,
                             "*.xml",
                             SearchOption.AllDirectories)
                         .Where(path => IsLegacyCustomDataInputFor(path, "priorities.xml"))
                         .OrderBy(
                             path => Path.GetRelativePath(directory.Path, path),
                             StringComparer.Ordinal))
                {
                    string relativePath = Path.GetRelativePath(directory.Path, path)
                        .Replace('\\', '/');
                    AppendFramed(hash, Encoding.UTF8.GetBytes(relativePath));
                    AppendFramed(hash, File.ReadAllBytes(path));
                }
            }

            digest = "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryHasSelectedCustomDataInputFor(
        IReadOnlyList<CustomDirectory> directories,
        string targetFileName,
        out bool hasInput)
    {
        hasInput = false;
        try
        {
            foreach (CustomDirectory directory in directories)
            {
                if (Directory.EnumerateFiles(directory.Path, "*.xml", SearchOption.AllDirectories)
                    .Any(path => IsLegacyCustomDataInputFor(path, targetFileName)))
                {
                    hasInput = true;
                    return true;
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryLoadCreationPriorityDocument(
        ContentOverlayCatalog catalog,
        IReadOnlyList<CustomDirectory> directories,
        out XDocument? document,
        out bool customDataUnsupported,
        out string[] sourceAnchors)
    {
        customDataUnsupported = false;
        sourceAnchors = [];
        if (!TryLoadEffectiveDocument(catalog, "priorities.xml", out document)
            || document?.Root is null)
        {
            return false;
        }

        var anchors = new List<string>();
        try
        {
            foreach (CustomDirectory directory in directories)
            {
                foreach (string path in Directory.EnumerateFiles(
                             directory.Path,
                             "*.xml",
                             SearchOption.AllDirectories)
                         .Where(path => IsLegacyCustomDataInputFor(path, "priorities.xml"))
                         .OrderBy(
                             path => Path.GetRelativePath(directory.Path, path),
                             StringComparer.Ordinal))
                {
                    string relativePath = Path.GetRelativePath(directory.Path, path)
                        .Replace('\\', '/');
                    anchors.Add($"customdata:{directory.Name}:{relativePath}");
                    if (!Path.GetFileName(path).StartsWith(
                            "amend_",
                            StringComparison.OrdinalIgnoreCase)
                        || !TryApplyCreationPriorityWeightAmendment(document, path))
                    {
                        customDataUnsupported = true;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or XmlException)
        {
            return false;
        }

        sourceAnchors = anchors.ToArray();
        return true;
    }

    private static bool TryApplyCreationPriorityWeightAmendment(
        XDocument target,
        string amendmentPath)
    {
        if (!TryLoadXml(amendmentPath, out XDocument? amendment)
            || target.Root is null
            || amendment?.Root is null
            || amendment.Root.Name.NamespaceName.Length != 0
            || !string.Equals(amendment.Root.Name.LocalName, "chummer", StringComparison.Ordinal)
            || amendment.Root.HasAttributes)
        {
            return false;
        }

        XElement[] containers = amendment.Root.Elements().ToArray();
        if (containers.Length != 1
            || containers[0].Name.NamespaceName.Length != 0
            || !string.Equals(
                containers[0].Name.LocalName,
                "priortysumtotenvalues",
                StringComparison.Ordinal)
            || containers[0].HasAttributes)
        {
            return false;
        }

        var replacements = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (XElement value in containers[0].Elements())
        {
            string rank = value.Name.LocalName;
            if (value.Name.NamespaceName.Length != 0
                || rank.Length != 1
                || rank[0] is < 'A' or > 'Z'
                || value.HasAttributes
                || value.HasElements
                || !string.Equals(value.Value, value.Value.Trim(), StringComparison.Ordinal)
                || !int.TryParse(
                    value.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsed)
                || parsed < 0
                || !replacements.TryAdd(rank, parsed))
            {
                return false;
            }
        }
        if (replacements.Count == 0)
            return false;

        XElement[] targetContainers = target.Root.Elements("priortysumtotenvalues")
            .Take(2)
            .ToArray();
        if (targetContainers.Length > 1
            || targetContainers.Any(container => container.HasAttributes))
        {
            return false;
        }

        XElement targetContainer;
        if (targetContainers.Length == 0)
        {
            targetContainer = new XElement("priortysumtotenvalues");
            XElement? priorities = target.Root.Element("priorities");
            if (priorities is null)
                target.Root.Add(targetContainer);
            else
                priorities.AddBeforeSelf(targetContainer);
        }
        else
        {
            targetContainer = targetContainers[0];
        }

        foreach ((string rank, int parsed) in replacements)
        {
            XElement[] existing = targetContainer.Elements(rank).Take(2).ToArray();
            if (existing.Length > 1
                || existing.Any(element => element.HasAttributes || element.HasElements))
            {
                return false;
            }
            if (existing.Length == 0)
                targetContainer.Add(new XElement(rank, parsed.ToString(CultureInfo.InvariantCulture)));
            else
                existing[0].Value = parsed.ToString(CultureInfo.InvariantCulture);
        }
        return true;
    }

    private static bool TryHasEnabledOverlayInput(
        ContentOverlayCatalog catalog,
        string fileName,
        out bool hasInput)
    {
        hasInput = false;
        try
        {
            foreach (ContentOverlayPack pack in catalog.Overlays.Where(pack => pack.Enabled))
            {
                if (string.IsNullOrWhiteSpace(pack.DataPath) || !Directory.Exists(pack.DataPath))
                {
                    continue;
                }
                if (string.Equals(pack.Mode, ContentOverlayModes.ReplaceFile, StringComparison.Ordinal))
                {
                    if (File.Exists(Path.Combine(pack.DataPath, fileName)))
                    {
                        hasInput = true;
                        return true;
                    }
                    continue;
                }
                if (!string.Equals(pack.Mode, ContentOverlayModes.MergeCatalog, StringComparison.Ordinal))
                {
                    return false;
                }
                if (Directory.EnumerateFiles(pack.DataPath, "*.xml", SearchOption.TopDirectoryOnly).Any(path =>
                        string.Equals(
                            ResolveCatalogTargetFileName(Path.GetFileName(path)),
                            fileName,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    hasInput = true;
                    return true;
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsLegacyCustomDataInputFor(string path, string targetFileName)
    {
        string fileName = Path.GetFileName(path);
        return (fileName.StartsWith("override_", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("custom_", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("amend_", StringComparison.OrdinalIgnoreCase))
            && fileName.EndsWith($"_{targetFileName}", StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeMetatypeAuthorityDigest(
        string rawMetatypesXmlDigest,
        string effectiveMetatypesInputsDigest,
        string rawProfileInputsDigest,
        string selectedCustomDataInputsDigest,
        string settingsProfileId,
        int? metatypeKarmaMultiplier,
        int? minimumInitiativeDice,
        bool? droneMods,
        IReadOnlyList<string> enabledSourcebooks)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        string[] authorityInputs =
        [
            CharacterCreationMetatypeCatalogSchemas.CatalogV1,
            rawMetatypesXmlDigest,
            effectiveMetatypesInputsDigest,
            rawProfileInputsDigest,
            selectedCustomDataInputsDigest,
            settingsProfileId.Trim(),
            metatypeKarmaMultiplier?.ToString(CultureInfo.InvariantCulture) ?? "missing",
            minimumInitiativeDice?.ToString(CultureInfo.InvariantCulture) ?? "missing",
            droneMods?.ToString(CultureInfo.InvariantCulture) ?? "missing",
            .. enabledSourcebooks
        ];
        foreach (string value in authorityInputs)
        {
            AppendFramed(hash, Encoding.UTF8.GetBytes(value));
        }
        return "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string BindSelectedProfile(string rawInputsDigest, string settingsProfileId)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFramed(hash, Encoding.UTF8.GetBytes(rawInputsDigest));
        AppendFramed(hash, Encoding.UTF8.GetBytes(settingsProfileId.Trim()));
        return "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendFramed(IncrementalHash hash, byte[] bytes)
    {
        Span<byte> length = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(length, bytes.LongLength);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static string ResolveCatalogTargetFileName(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        int markerIndex = stem.IndexOf('.');
        string canonicalStem = markerIndex >= 0 ? stem[..markerIndex] : stem;
        return string.IsNullOrWhiteSpace(canonicalStem) ? string.Empty : $"{canonicalStem}.xml";
    }

    private static XDocument MergeCatalogDocument(XDocument baseDocument, XDocument fragmentDocument)
    {
        XElement mergedRoot = baseDocument.Root is null ? new XElement("chummer") : new XElement(baseDocument.Root);
        if (fragmentDocument.Root is XElement fragmentRoot)
        {
            foreach (XElement fragmentChild in fragmentRoot.Elements())
            {
                XElement? targetChild = mergedRoot.Elements(fragmentChild.Name).FirstOrDefault();
                if (targetChild is null)
                {
                    mergedRoot.Add(new XElement(fragmentChild));
                    continue;
                }

                if (!fragmentChild.Elements().Any())
                {
                    targetChild.ReplaceWith(new XElement(fragmentChild));
                    continue;
                }

                foreach (XElement fragmentEntry in fragmentChild.Elements())
                {
                    string? key = TryResolveMergeKey(fragmentEntry);
                    XElement? existing = key is null
                        ? targetChild.Elements(fragmentEntry.Name).FirstOrDefault(candidate => XNode.DeepEquals(candidate, fragmentEntry))
                        : targetChild.Elements(fragmentEntry.Name).FirstOrDefault(candidate =>
                            string.Equals(TryResolveMergeKey(candidate), key, StringComparison.Ordinal));
                    if (existing is null)
                    {
                        targetChild.Add(new XElement(fragmentEntry));
                    }
                    else if (!XNode.DeepEquals(existing, fragmentEntry))
                    {
                        existing.ReplaceWith(new XElement(fragmentEntry));
                    }
                }
            }
        }

        return baseDocument.Declaration is null
            ? new XDocument(mergedRoot)
            : new XDocument(baseDocument.Declaration, mergedRoot);
    }

    private static string? TryResolveMergeKey(XElement element)
    {
        static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        string? id = Normalize(element.Element("id")?.Value) ?? Normalize(element.Attribute("id")?.Value);
        if (id is not null)
        {
            return $"id:{id}";
        }
        string? key = Normalize(element.Element("key")?.Value) ?? Normalize(element.Attribute("key")?.Value);
        if (key is not null)
        {
            return $"key:{key}";
        }
        string? name = Normalize(element.Element("name")?.Value) ?? Normalize(element.Attribute("name")?.Value);
        if (name is not null)
        {
            return $"name:{name}";
        }
        if (element.Elements().Any())
        {
            return null;
        }
        string? value = Normalize(element.Value);
        return value is null ? null : $"value:{value}";
    }

    private static bool TryLoadXml(string path, out XDocument? document)
    {
        document = null;
        try
        {
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using XmlReader reader = XmlReader.Create(path, settings);
            document = XDocument.Load(reader, LoadOptions.None);
            return document.Root is not null;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or XmlException)
        {
            return false;
        }
    }

    private static string ReadValue(XElement? parent, XName name)
        => parent?.Element(name)?.Value.Trim() ?? string.Empty;

    private static bool ParseBool(string? value)
        => string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "1", StringComparison.Ordinal);
}
