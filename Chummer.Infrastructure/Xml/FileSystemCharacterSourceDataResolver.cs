using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Chummer.Application.Characters;
using Chummer.Application.Content;

namespace Chummer.Infrastructure.Xml;

public sealed class FileSystemCharacterSourceDataResolver : ICharacterSourceDataResolver
{
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

            string settingsKey = ReadValue(character, "settings");
            XElement? settings = settingsDocument.Root
                .Element("settings")?
                .Elements("setting")
                .FirstOrDefault(candidate => string.Equals(
                    ReadValue(candidate, "id"),
                    settingsKey,
                    StringComparison.OrdinalIgnoreCase));
            if (settings is null)
            {
                return null;
            }

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

    private static bool TryReadNonNegativeInt(XElement parent, string elementName, out int value)
    {
        value = 0;
        XElement[] values = parent.Elements(elementName).Take(2).ToArray();
        return values.Length == 1
            && int.TryParse(values[0].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && value >= 0;
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
