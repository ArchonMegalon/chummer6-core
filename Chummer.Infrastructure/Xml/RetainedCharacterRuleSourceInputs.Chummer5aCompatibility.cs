using Chummer.Contracts.Characters;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Content;

namespace Chummer.Infrastructure.Xml;

internal sealed partial class RetainedCharacterRuleSourceInputs
{
    internal string? CompatibilityProfile { get; }
    internal IReadOnlyList<RulePackResolutionDiagnostic> CompatibilityDiagnostics { get; } = [];

    internal sealed class CompatibilityRejection : InvalidOperationException
    {
        internal IReadOnlyList<RulePackResolutionDiagnostic> Diagnostics { get; }
        internal CompatibilityRejection(IReadOnlyList<RulePackResolutionDiagnostic> diagnostics)
            : base("Retained rule-source compatibility could not be admitted.")
        {
            Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
            FileSystemCharacterSourceDataResolver.ReportCompatibilityDiagnostics(Diagnostics);
        }
    }

    internal static RulePackResolutionDiagnostic CompatibilityDiagnostic(string code, string source,
        string? digest, IXmlLineInfo? location, string message, string? dependency = null,
        string severity = RulePackResolutionSeverityLevels.Error)
        => new(code switch
            {
                "missing-dependency" or "missing-manifest" => RulePackResolutionDiagnosticKinds.MissingDependency,
                "dependency-version-conflict" => RulePackResolutionDiagnosticKinds.VersionConflict,
                "package-conflict" or "duplicate-package-id" => RulePackResolutionDiagnosticKinds.PackConflict,
                _ => RulePackResolutionDiagnosticKinds.CapabilityBlocked
            }, severity,
            location?.HasLineInfo() == true
                ? source + ":" + location.LineNumber.ToString(CultureInfo.InvariantCulture)
                    + ":" + location.LinePosition.ToString(CultureInfo.InvariantCulture) : source,
            message, digest, dependency, "chummer5a-import." + code);

    // Input-only seed for a real canonical capture pass. Never return this seed as
    // an observed capture or use its copied policy metadata as admission evidence.
    internal RetainedCharacterRuleSourceInputs SeedCurrentCharacterObservation(string characterXml)
        => new(_catalog, characterXml, PolicyDigest, ProfileId, _selection, _files, _existence,
            _inventories.Values, _portable, SourcePathSeparator, DependencyRecipe1244,
            NativeFocusGearCatalog1339, CompatibilityProfile);

    internal bool HasSameCurrentSourceClosure(RetainedCharacterRuleSourceInputs observed)
    {
        if (CompatibilityProfile != observed.CompatibilityProfile || PolicyDigest != observed.PolicyDigest
            || ProfileId != observed.ProfileId || _portable != observed._portable
            || SourcePathSeparator != observed.SourcePathSeparator
            || DependencyRecipe1244 != observed.DependencyRecipe1244
            || NativeFocusGearCatalog1339 != observed.NativeFocusGearCatalog1339
            || CharacterCreationFinalizationDigest.Compute(_catalog) != CharacterCreationFinalizationDigest.Compute(observed._catalog)
            || !_selection.SequenceEqual(observed._selection)
            || !_files.Keys.Order(StringComparer.Ordinal).SequenceEqual(observed._files.Keys.Order(StringComparer.Ordinal))
            || !_existence.Keys.Order(StringComparer.Ordinal).SequenceEqual(observed._existence.Keys.Order(StringComparer.Ordinal))
            || _inventories.Count != observed._inventories.Count) return false;
        foreach (var pair in _files)
            if (!pair.Value.AsSpan().SequenceEqual(observed._files[pair.Key])) return false;
        foreach (var pair in _existence)
            if (pair.Value != observed._existence[pair.Key]) return false;
        foreach (var pair in _inventories)
            if (!observed._inventories.TryGetValue(pair.Key, out var other)
                || pair.Value.Exists != other.Exists
                || !pair.Value.Entries.SequenceEqual(other.Entries, StringComparer.Ordinal)) return false;
        return true;
    }

    internal RetainedCharacterRuleSourceInputs BindLegacyImport(string profile = Chummer5aLegacyImportCompatibility.ProfileV1)
    {
        if (!Chummer5aLegacyImportCompatibility.IsSupportedProfile(profile)
            || CompatibilityProfile is not null && CompatibilityProfile != profile)
            throw new CompatibilityRejection([CompatibilityDiagnostic("unsupported-profile", "capture", null, null,
                "The requested import profile is unsupported or differs from the archived interpretation.")]);
        if (CompatibilityProfile is not null) return this;
        // Ordinary capture has already assigned historical portable slots. Only the
        // explicit new profile remaps those slots by catalog role, before binding it.
        var inputs = _portable ? ToPortable(logicalRootOrder: true) : this;
        return new(inputs._catalog, CharacterXml, PolicyDigest, ProfileId,
            inputs._selection, inputs._files, inputs._existence, inputs._inventories.Values,
            _portable, SourcePathSeparator, DependencyRecipe1244, NativeFocusGearCatalog1339,
            profile);
    }

    internal string? ComputeRequiredPackageSetDigest()
    {
        if (CompatibilityProfile is null) return null;
        if (!Chummer5aLegacyImportCompatibility.TryGetInterpretation(CompatibilityProfile,
            out var resolverSemantics, out var definitionFingerprint))
            throw new CompatibilityRejection([CompatibilityDiagnostic("unsupported-profile", "capture", null, null,
                "The archived import profile is unsupported.")]);
        void WriteBinding(BinaryWriter writer)
        {
            WriteText(writer, "chummer-required-retained-package-set-v1");
            WriteText(writer, Chummer5aLegacyImportCompatibility.RulesetId);
            WriteCaptureHeader1244(writer); // Profile, dependency recipe, path order and projection version.
            WriteText(writer, Chummer5aLegacyImportCompatibility.SourceOrderV1);
            WriteText(writer, resolverSemantics);
            if (definitionFingerprint is not null)
                WriteText(writer, definitionFingerprint);
            WriteText(writer, Chummer5aLegacyImportCompatibility.ResolverBaselineSha256);
            WriteText(writer, Chummer5aLegacyImportCompatibility.XmlManagerSha256);
            WriteText(writer, Chummer5aLegacyImportCompatibility.ManifestParserSha256);
            WriteEnvironmentInputs(writer); // Original bytes, settings, ordered selections and observations.
            var order = OrderedCustomSources();
            writer.Write(order.Count);
            foreach (var source in order)
            {
                WriteText(writer, source.Mode); WriteText(writer, source.RelativePath); WriteText(writer, source.Checksum);
            }
        }
        using var counter = new BoundedCountingStream();
        using (var writer = new BinaryWriter(counter, Utf8, leaveOpen: true)) WriteBinding(writer);
        using var stream = new MemoryStream(checked((int)counter.Length));
        using (var writer = new BinaryWriter(stream, Utf8, leaveOpen: true)) WriteBinding(writer);
        // This is an INPUT closure identity. PolicyDigest remains its own existing identity;
        // neither digest is a proof of equivalent merged output or complete rule-effect coverage.
        return Digest(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }

    internal IReadOnlyList<ArtifactVersionReference> RequiredRulePacks()
    {
        if (CompatibilityProfile is null) return [];
        var result = new List<ArtifactVersionReference>
        {
            new("sr5:captured-base", DigestAssets(_catalog.BaseDataPath, _catalog.BaseLanguagePath))
        };
        result.AddRange(_catalog.Overlays.Where(pack => pack.Enabled)
            .Select(pack => new ArtifactVersionReference("overlay:" + pack.Id, DigestAssets(pack.RootPath, pack.DataPath, pack.LanguagePath))));
        result.AddRange(_selection.Select(item => new ArtifactVersionReference("chummer5a:" + item.ManifestId, item.Version)));
        return Array.AsReadOnly(result.ToArray());
    }

    internal IReadOnlyList<RulePackAssetDescriptor> RequiredAssets()
        => CompatibilityProfile is null ? [] : Array.AsReadOnly(_files
            .OrderBy(row => PathOrderKey(row.Key), StringComparer.Ordinal)
            .Select(row => new RulePackAssetDescriptor(
                Path.GetExtension(row.Key).Equals(".xml", StringComparison.OrdinalIgnoreCase) ? RulePackAssetKinds.Xml : "retained-metadata",
                "original-retained-input", Address(row.Key), Digest(row.Value))).ToArray());

    internal IReadOnlyList<RulePackAssetDescriptor> OrderedCustomSources()
    {
        if (CompatibilityProfile is null) return [];
        var result = new List<RulePackAssetDescriptor>();
        foreach (var item in _selection)
        foreach (var inventory in _inventories.Values.Where(row => !row.Key.Directories
            && row.Key.Directory == item.Path && row.Key.Pattern.StartsWith("*_", StringComparison.Ordinal))
            .OrderBy(row => row.Key.Pattern, StringComparer.Ordinal))
        foreach (string prefix in new[] { "override_", "custom_", "amend_" })
        foreach (string path in inventory.Entries.Where(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ThenBy(PathOrderKey, StringComparer.Ordinal))
        {
            if (result.Count >= MaximumObservations) throw Rejected();
            result.Add(new(RulePackAssetKinds.Xml, prefix + ":" + inventory.Key.Pattern,
                Address(path), Digest(_files[path])));
        }
        return result.AsReadOnly();
    }

    private string Address(string path) => _portable ? ToAddress(path) : path;

    private string DigestAssets(params string[] roots)
    {
        string[] selectedRoots = roots.Where(root => !string.IsNullOrEmpty(root))
            .Select(Path.TrimEndingDirectorySeparator).Distinct(StringComparer.Ordinal).ToArray();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Utf8, leaveOpen: true))
        {
            WriteText(writer, "chummer-retained-package-assets-v1");
            writer.Write(selectedRoots.Length);
            foreach (string root in selectedRoots) WriteArchivePath(writer, root);
            foreach (var row in _files.Where(row => selectedRoots.Any(root => Within(root, row.Key)))
                .OrderBy(row => PathOrderKey(row.Key), StringComparer.Ordinal))
            {
                WriteArchivePath(writer, row.Key);
                WriteText(writer, Digest(row.Value));
            }
        }
        return Digest(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }

    private IReadOnlyList<RulePackResolutionDiagnostic> InspectLegacyImport()
    {
        var diagnostics = new List<RulePackResolutionDiagnostic>();
        void Add(RulePackResolutionDiagnostic diagnostic)
        {
            if (diagnostics.Count >= MaximumObservations) throw Rejected();
            diagnostics.Add(diagnostic);
        }
        void Error(string code, string source, string message, string? related = null)
            => Add(CompatibilityDiagnostic(code, source, null, null, message, related));
        if (!Chummer5aLegacyImportCompatibility.IsSupportedProfile(CompatibilityProfile))
        {
            Error("unsupported-profile", "capture", "The serialized interpretation profile is not supported.");
            return diagnostics.AsReadOnly();
        }
        if (!_portable)
            Error("nonportable-source", "capture", "This profile requires exact portable addresses; local legacy capture remains available.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in _files.Keys)
        {
            if (!paths.Add(Address(path))) Error("conflicting-source", Address(path), "Source addresses conflict on a destination filesystem.");
            if (!Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(path).Equals("manifest.json", StringComparison.Ordinal))
                Error("unsupported-asset", Address(path), "Executable and unknown assets are outside the retained XML import profile.");
        }

        // Existing capture is an SR5 source lane. Do not relabel an explicit foreign edition.
        using (var reader = XmlReader.Create(new StringReader(CharacterXml), XmlSettings()))
        {
            var character = XDocument.Load(reader).Root;
            foreach (var edition in character?.Elements().Where(node => node.Name.LocalName is "gameedition" or "edition") ?? [])
                if (edition.Value.Trim() is not ("5" or "SR5" or "sr5"))
                    Error("edition-mismatch", "character", "The retained-import profile is limited to the existing SR5 capture lane.");
        }

        var ids = new Dictionary<Guid, (Selection Selection, int Index)>();
        var selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < _selection.Length; index++)
        {
            var item = _selection[index];
            if (!selectedPaths.Add(item.Path) || !selectedNames.Add(item.Name))
                Error("duplicate-source", Address(item.Path), "Selected source names and paths must be unique.");
            if (!Guid.TryParse(item.ManifestId, out var id) || id == Guid.Empty)
                Error("missing-package-id", Address(item.Path), "An exact selected manifest GUID is required.");
            else if (!ids.TryAdd(id, (item, index)))
                Error("duplicate-package-id", Address(item.Path), "Selected packages have duplicate or conflicting GUIDs.", item.ManifestId);
        }
        var overlayIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pack in _catalog.Overlays.Where(pack => pack.Enabled))
        {
            if (string.IsNullOrWhiteSpace(pack.Id) || !overlayIds.Add(pack.Id))
                Error("duplicate-overlay-id", pack.Id, "Enabled overlay identifiers must be present and unique.");
            Add(CompatibilityDiagnostic("overlay-effects-unverified", pack.Id, null, null,
                "Global JSON overlays are retained inputs, not verified Chummer5a amendment packages.",
                severity: RulePackResolutionSeverityLevels.Warning));
        }

        foreach (var item in _selection)
        {
            string manifest = Path.Combine(item.Path, "manifest.xml");
            if (!_files.TryGetValue(manifest, out var bytes))
            {
                Error("missing-manifest", Address(manifest), "The exact selected manifest is missing; no stock fallback is permitted.");
                continue;
            }
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(stream, XmlSettings());
            var root = XDocument.Load(reader, LoadOptions.SetLineInfo).Root;
            string hash = Digest(bytes);
            void ManifestError(string code, XElement? node, string message, string? dependency = null)
                => Add(CompatibilityDiagnostic(code, Address(manifest), hash, node, message, dependency));
            if (root is null || root.Name != "manifest"
                || root.Elements("guid").Count() != 1 || root.Elements("version").Count() != 1
                || !Guid.TryParse(root.Element("guid")?.Value, out var manifestId)
                || manifestId.ToString("D") != item.ManifestId
                || !FileSystemCharacterSourceDataResolver.LegacyVersion.TryParse(root.Element("version")?.Value, out var manifestVersion)
                || !FileSystemCharacterSourceDataResolver.LegacyVersion.TryParse(item.Version, out var selectedVersion)
                || manifestVersion.CompareTo(selectedVersion) != 0)
            {
                ManifestError("manifest-identity-conflict", root, "Manifest GUID/version does not match the exact resolved selection.");
                continue;
            }
            int selectedIndex = Array.IndexOf(_selection, item);
            foreach (string container in new[] { "dependencies", "incompatibilities" })
            {
                bool conflict = container == "incompatibilities";
                string element = conflict ? "incompatibility" : "dependency";
                if (root.Elements(container).Count() > 1)
                    ManifestError("duplicate-dependency-container", root, "Duplicate dependency containers are ambiguous.");
                var seen = new HashSet<Guid>();
                foreach (var declaration in root.Elements(container).Elements())
                {
                    if (declaration.Name != element || declaration.Elements("guid").Count() != 1
                        || declaration.Elements("name").Count() != 1 || string.IsNullOrWhiteSpace(declaration.Element("name")?.Value)
                        || !Guid.TryParse(declaration.Element("guid")?.Value, out var dependencyId) || dependencyId == Guid.Empty)
                    {
                        ManifestError("invalid-dependency", declaration, "Dependency identity is missing or ambiguous; legacy skip behavior is not imported.");
                        continue;
                    }
                    string dependency = dependencyId.ToString("D");
                    if (!seen.Add(dependencyId))
                        ManifestError("duplicate-dependency", declaration, "A dependency identifier is declared more than once.", dependency);
                    if (!TryRange(declaration, out var minimum, out var maximum))
                    {
                        ManifestError("invalid-dependency-range", declaration, "Dependency version bounds are invalid or conflicting.", dependency);
                        continue;
                    }
                    if (!ids.TryGetValue(dependencyId, out var target))
                    {
                        if (!conflict) ManifestError("missing-dependency", declaration, "The required package is not selected. No automatic selection or stock fallback.", dependency);
                        continue;
                    }
                    bool matches = FileSystemCharacterSourceDataResolver.LegacyVersion.TryParse(target.Selection.Version, out var version)
                        && (minimum is null || version.CompareTo(minimum.Value) >= 0)
                        && (maximum is null || version.CompareTo(maximum.Value) <= 0);
                    if (conflict && matches)
                        ManifestError("package-conflict", declaration, "A selected package satisfies a declared incompatibility.", dependency);
                    if (!conflict && !matches)
                        ManifestError("dependency-version-conflict", declaration, "Selected dependency version is outside the declared range.", dependency);
                    if (!conflict && target.Index >= selectedIndex)
                        ManifestError("dependency-order-conflict", declaration, "Dependencies must precede the dependent package; source order was not rewritten.", dependency);
                }
            }
            Add(CompatibilityDiagnostic("selected-effects-unverified", Address(manifest), hash, root,
                "Original package bytes and current order are retained; legacy effective-output and arbitrary Creation mutation equivalence are unverified.",
                severity: RulePackResolutionSeverityLevels.Warning));
        }

        foreach (var row in _files.OrderBy(row => PathOrderKey(row.Key), StringComparer.Ordinal))
        {
            string name = Path.GetFileName(row.Key);
            if (!name.StartsWith("amend_", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = new MemoryStream(row.Value, writable: false);
            using var reader = XmlReader.Create(stream, XmlSettings());
            var document = XDocument.Load(reader, LoadOptions.SetLineInfo);
            string hash = Digest(row.Value);
            foreach (var node in document.Descendants())
            {
                var attribute = node.Attribute("amendoperation");
                string operation = attribute?.Value.ToUpperInvariant() ?? "";
                if (operation == "REGEXREPLACE" || operation.Length != 0
                    && operation is not ("REPLACE" or "REMOVE" or "APPEND" or "RECURSE" or "ADDNODE"))
                    Add(CompatibilityDiagnostic("unsupported-operation", Address(row.Key), hash, attribute,
                        "Regex, unknown, or whitespace-normalized operations are outside this profile; no fallback operation was substituted."));
            }
            Add(CompatibilityDiagnostic("amend-semantics-unverified", Address(row.Key), hash, document.Root,
                "Current ID/name matching, ADDNODE stripping, implicit recursion and missing-target behavior are not proven equivalent to the pinned XmlManager.",
                severity: RulePackResolutionSeverityLevels.Warning));
        }
        foreach (var item in _selection)
        foreach (var inventory in _inventories.Values.Where(row => !row.Key.Directories
            && row.Key.Directory == item.Path && row.Key.Pattern.StartsWith("*_", StringComparison.Ordinal)))
        foreach (string prefix in new[] { "override_", "custom_", "amend_" })
            if (inventory.Entries.Count(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) > 1)
                Add(CompatibilityDiagnostic("legacy-file-order-unverified", Address(item.Path), null, null,
                    "Multiple same-phase sources use the captured resolver's filename/path order. Historical desktop enumeration order was not supplied.",
                    severity: RulePackResolutionSeverityLevels.Warning));
        return diagnostics.AsReadOnly();
    }

    private static bool TryRange(XElement declaration,
        out FileSystemCharacterSourceDataResolver.LegacyVersion? minimum,
        out FileSystemCharacterSourceDataResolver.LegacyVersion? maximum)
    {
        minimum = maximum = null;
        foreach (string key in new[] { "minversion", "maxversion" })
        {
            var values = declaration.Elements(key).ToArray();
            if (values.Length == 0) continue;
            if (values.Length != 1 || !FileSystemCharacterSourceDataResolver.LegacyVersion.TryParse(values[0].Value, out var value)) return false;
            // Legacy default(ValueVersion) is an unbounded all-zero version.
            if (value.Parts.All(part => part == 0)) continue;
            if (key == "minversion") minimum = value; else maximum = value;
        }
        return minimum is null || maximum is null || minimum.Value.CompareTo(maximum.Value) <= 0;
    }
}
