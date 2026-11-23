using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Application.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private static readonly AsyncLocal<CurrentCatalogTrace?> ActiveCurrentCatalogTrace = new();
    private ValidatedReadScope? _currentEvaluationReads2764;
    private int _currentEvaluationThread2764;

    private static XElement CreateCurrentCatalogHeader6247(XElement root, XName[] primaryContainers)
    {
        var header = new XElement(root.Name);
        // Omit only the same inert metadata as the former full-copy/remove path.
        foreach (var attribute in root.Attributes())
            if (!CurrentStockXmlMetadata.IsSupported(attribute)) header.Add(new XAttribute(attribute));
        bool hadNodes = false;
        foreach (var node in root.Nodes())
        {
            hadNodes = true;
            if (node is XElement element && primaryContainers.Contains(element.Name)) continue;
            // Every source node has a parent, so Add clones it rather than moving it.
            // This preserves all surviving node kinds, order and independent ownership.
            header.Add(node);
        }
        // An explicitly empty root has no nodes but retains its full end tag.
        // Removing the last primary node, by contrast, leaves a self-closing root.
        if (!hadNodes && !root.IsEmpty) header.Add(string.Empty);
        return header;
    }

    private sealed partial class RuleSourceCapture
    {
        internal bool OwnsEvaluationInputs2764(RetainedCharacterRuleSourceInputs retained)
            => ReferenceEquals(_inputs, retained);
    }

    private sealed partial class SourceInputSnapshot
    {
        // Same private immutable byte ownership as _documents/_digests. This memo
        // belongs to this source snapshot only, never an execution or owner cache.
        private Dictionary<string, (string Identity, string Digest)>? _currentCatalogFragments7644;
        private int _currentCatalogFragmentHashCount7644;
        private int _currentCatalogFragmentReferenceCount7644;

        public (string Identity, string Digest) DescribeCurrentCatalogFragment7644(string path)
        {
            lock (_sync)
            {
                // Preserve every source-byte request. ReadAllBytes returns the
                // snapshot's private bytes; the enclosing source/operation scopes
                // still admit live files, inventories and retained inputs.
                byte[] bytes = ReadAllBytes(path);
                _currentCatalogFragmentReferenceCount7644++;
                var fragments = _currentCatalogFragments7644 ??= new(StringComparer.Ordinal);
                if (fragments.TryGetValue(path, out var observed)) return observed;
                observed = (CharacterCreationFinalizationDigest.Compute(path),
                    RetainedCharacterRuleSourceInputs.Digest(bytes));
                fragments.Add(path, observed);
                _currentCatalogFragmentHashCount7644++;
                return observed;
            }
        }
    }

    private sealed class CurrentCatalogTrace(string query, IReadOnlyList<string> dependencies)
    {
        internal readonly List<CharacterCurrentRuleMergeOperation> Operations = [];
        internal string SourceIdentity = string.Empty;
        internal string SourceDigest = string.Empty;
        internal string Phase = string.Empty;

        internal int Begin(string operation, XElement selector, XElement? before)
        {
            if (Operations.Count >= RetainedCharacterRuleSourceInputs.MaximumObservations)
                throw RetainedCharacterRuleSourceInputs.Rejected();
            int index = Operations.Count;
            Operations.Add(new(index, query, SourceIdentity, SourceDigest, Phase, operation,
                selector.ToString(SaveOptions.DisableFormatting),
                before?.ToString(SaveOptions.DisableFormatting), null, false, dependencies));
            return index;
        }

        internal void End(int index, XElement? after, bool removed = false)
        {
            var previous = Operations[index];
            Operations[index] = previous with { AfterXml = after?.ToString(SaveOptions.DisableFormatting),
                IsTombstone = removed || previous.BeforeXml is not null && after is null };
        }

        internal IDisposable ObserveNode(XElement parent, XElement amendment, string operation, int matches)
        {
            int index = Begin(operation, amendment, parent);
            return new Observation(() => End(index, parent, operation == "REMOVE" && matches != 0));
        }

        internal sealed class Observation(Action finish) : IDisposable
        {
            public void Dispose() => finish();
        }
    }

    private sealed partial class SourceDataContext : ICurrentLifestyleSettingsSource2678, ICurrentLifestyleMechanicsSource2684
    {
        public bool HasLifestyleMechanics2684 => _sourceInputs.Accounting is { HasCurrentEnvironmentSemantics: true } accounting
            ? accounting.HasLifestyleMechanics2684
            : _character.Elements(CharacterCreationLifestyleMechanicsRules.CapabilityElement).ToArray() is [var marker]
                && !marker.HasAttributes && !marker.HasElements && marker.Value == CharacterCreationLifestyleMechanicsRules.Capability;

        public bool TryReadLifestylePromptSource2684(string fileName, out string xml, out string digest)
        {
            using var scope = _sourceInputs.Enter();
            xml = digest = string.Empty;
            if (fileName != "skills.xml" || !TryComputeEffectiveInputDigest(_catalog, fileName, out digest)
                || !TryLoadEffectiveDocument(_catalog, fileName, out var document)
                || document?.Root is not { } root || _sourceInputs.HasSourceDrift) return false;
            xml = root.ToString(SaveOptions.DisableFormatting);
            return true;
        }

        public bool TryReadLifestyleCharacterFacts2684(out CharacterCreationLifestyleMechanicsContext context)
        {
            using var scope = _sourceInputs.Enter();
            // SelectionShared tests the actual Character.Qualities collection (name/id/extra),
            // not catalogue presence or the availability of a fresh quality purchase.
            // The exact character bytes are already bound by this context and its caller's owner/CAS guards.
            var facts = new List<CharacterCreationLifestyleCharacterFact>();
            var containers = _character.Elements("qualities").ToArray();
            bool exact = containers.Length <= 1;
            foreach (var saved in containers.Elements("quality"))
            {
                if (facts.Count >= 4096) { exact = false; break; }
                var names = saved.Elements("name").ToArray();
                var ids = saved.Elements("sourceid").ToArray();
                var extras = saved.Elements("extra").ToArray();
                Guid id = Guid.Empty;
                if (names is not [var name] || name.HasAttributes || name.HasElements || string.IsNullOrWhiteSpace(name.Value)
                    || ids.Length > 1 || extras.Length > 1
                    || ids.Concat(extras).Any(field => field.HasAttributes || field.HasElements)
                    || ids.Length == 1 && !string.IsNullOrWhiteSpace(ids[0].Value) && !Guid.TryParse(ids[0].Value, out id))
                { exact = false; continue; }
                facts.Add(new(id, name.Value, extras.FirstOrDefault()?.Value ?? string.Empty));
            }
            context = new(CharacterCreationLifestyleMechanicsRules.Capability, exact,
                facts.OrderBy(fact => fact.SourceId).ThenBy(fact => fact.Name, StringComparer.Ordinal)
                    .ThenBy(fact => fact.Extra, StringComparer.Ordinal).ToArray());
            return !_sourceInputs.HasSourceDrift;
        }

        public bool TryReadLifestyleSource(out string xml, out string digest)
        {
            using var scope = _sourceInputs.Enter();
            xml = string.Empty;
            digest = string.Empty;
            if (!TryComputeEffectiveInputDigest(_catalog, "lifestyles.xml", out digest)
                || !TryLoadEffectiveDocument(_catalog, "lifestyles.xml", out var document)
                || !TryProjectCurrentSourceDocument("lifestyles.xml", [("lifestyles", "lifestyle")], ref document)
                || document?.Root is not { } root || _sourceInputs.HasSourceDrift) return false;
            var copy = new XElement(root);
            if (UsesSelectedV7Sources2852)
            {
                bool qualityAmendment = false;
                foreach (var directory in _customDirectories)
                foreach (string path in EnumerateSourceFiles(directory.Path, "*_lifestyles.xml", SearchOption.AllDirectories)
                    .Where(path => IsLegacyCustomDataInputFor(path, "lifestyles.xml")))
                {
                    if (!TryLoadXml(path, out var fragment) || fragment?.Root is not { } fragmentRoot) return false;
                    qualityAmendment |= fragmentRoot.Element("qualities") is not null;
                }
                if (qualityAmendment)
                {
                    if (!TryEnumerateTargets("lifestyles.xml", ["qualities"], "quality", out var qualities)) return false;
                    // Match the V7 catalog's effective quality header in place;
                    // its registered validation still checks every traced operation.
                    var effective = new XElement("qualities", qualities);
                    if (copy.Element("qualities") is { } original) original.ReplaceWith(effective);
                    else copy.Add(effective);
                }
            }
            if (_sourceInputs.HasSourceDrift) return false;
            copy.Attributes().Where(CurrentStockXmlMetadata.IsSupported).Remove();
            xml = copy.ToString(SaveOptions.DisableFormatting);
            return true;
        }

        public bool TryReadLifestyleSettings(out string xml)
        {
            using var scope = _sourceInputs.Enter();
            xml = string.Empty;
            if (!TryResolveTarget(SettingsFile, ["settings"], "setting", _settingsProfileId, string.Empty, out var settings)
                || settings is null || _sourceInputs.HasSourceDrift) return false;
            xml = settings.ToString(SaveOptions.DisableFormatting);
            return true;
        }
        private bool UsesCurrentCatalogSemantics => _sourceInputs.Accounting?.HasCurrentEnvironmentSemantics == true;

        private bool UsesImportedCareerOwnerSemantics5698 =>
            _sourceInputs.Accounting?.HasImportedCareerOwnerSemantics5698 == true
            && _buildMethod is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen
            && bool.TryParse(_character.Element("created")?.Value, out bool created) && created;

        private bool TryResolveImportedCareerMetatype5698(out string xml, out string authority)
        {
            xml = authority = string.Empty;
            if (_sourceInputs.HasSourceDrift
                || !CharacterMetatypeIdentity.TryRead(_character, out var name, out var id,
                    out var variantName, out var variantId)
                || variantId is { } variant && variant != Guid.Empty || variantName is not ("" or "None")
                || !TryResolveSheetNamedSourceId7573("Metatype", id?.ToString("D") ?? name, out var sourceId)
                || !TryResolveSheetOwnerRow7573("Metatype", sourceId, out var selected, out var selectedName, out _)
                || selected is null || selectedName != name
                || !TryComputeEffectiveInputDigest(_catalog, SheetLocation("Metatype").File, out var sourceDigest)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, SheetLocation("Metatype").File,
                    out var customDigest) || _sourceInputs.HasSourceDrift)
                return false;
            xml = selected.ToString(SaveOptions.DisableFormatting);
            // Anatomy consumes the exact admitted metatype/profile/source inputs.
            // A sheet receipt also binds effective policy, which itself depends on
            // knowledge quality/anatomy resolution. Do not make an input depend on
            // that downstream derived receipt. Public sheet receipts keep their gate.
            authority = CharacterCreationFinalizationDigest.Compute(new
            {
                Schema = "sr5-career-quality-metatype-inputs/v1",
                File = SheetLocation("Metatype").File, SourceId = sourceId,
                SourceXml = xml, SourceDigest = sourceDigest, CustomDataDigest = customDigest,
                ProfileDigest = _rawProfileInputsDigest
            });
            return true;
        }

        // Current-only document input for existing canonical projectors. Legacy/Complete
        // contexts retain their old vetoes and documents. Admission still independently
        // validates all catalog syntax, metadata, operation traces and current consumers.
        private bool TryProjectCurrentSourceDocument(string file,
            (string Container, string Entry)[] groups, ref XDocument? document)
        {
            if (!UsesCurrentCatalogSemantics) return true;
            if (document?.Root is null || _catalog.Overlays.Any(pack => pack.Enabled)) return false;
            var projected = new XDocument(document);
            foreach (var group in groups)
            {
                if (!TryEnumerateTargets(file, [group.Container], group.Entry, out var rows)) return false;
                projected.Root!.Elements(group.Container).Remove();
                projected.Root.Add(new XElement(group.Container, rows.Select(row => new XElement(row))));
            }
            if (_sourceInputs.HasSourceDrift) return false;
            document = projected;
            return true;
        }

        // Context construction has already merged only the selected settings row into its
        // effective cache. Catalog enumeration must start from retained raw settings and
        // apply every ordered amendment once, including unselected profile rows.
        private bool TryLoadCurrentCatalogBaseDocument(string fileName, out XDocument? document)
        {
            if (UsesCurrentCatalogSemantics && fileName == SettingsFile
                && _sourceInputs.StandaloneSettingsBase5691 is { } standalone)
            {
                document = new XDocument(standalone);
                return !_sourceInputs.HasSourceDrift;
            }
            if (UsesCurrentCatalogSemantics && _sourceInputs.HasSelectedProfile(fileName))
            {
                document = null;
                return !_catalog.Overlays.Any(pack => pack.Enabled)
                    && TryLoadXml(Path.Combine(_catalog.BaseDataPath, fileName), out document);
            }
            return TryLoadEffectiveDocument(_catalog, fileName, out document);
        }

        internal CharacterCurrentRuleCatalogInput? ReadCurrentCatalog(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleDomain domain, CancellationToken cancellationToken)
        {
            using var sourceScope = _sourceInputs.Enter();
            if (_sourceInputs.HasSourceDrift || _catalog.Overlays.Any(pack => pack.Enabled)
                || _sourceInputs.Accounting is not { } accounting || !accounting.MatchesCurrentEnvironment(current))
                return null;
            bool lifestyleQualities2778 = domain == CharacterCurrentRuleDomain.Lifestyles && accounting.HasLifestyleMechanics2684;
            (string file, (string Container, string Entry)[] groups) domainCatalogMapping = domain switch
            {
                CharacterCurrentRuleDomain.Profile => (SettingsFile, [("settings", "setting")]),
                CharacterCurrentRuleDomain.Metatypes => (UsesSpriteCatalog5751 ? "critters.xml" : "metatypes.xml", [("metatypes", "metatype")]),
                CharacterCurrentRuleDomain.Priorities => ("priorities.xml", [("priorities", "priority")]),
                CharacterCurrentRuleDomain.Skills => ("skills.xml", [("skills", "skill"), ("knowledgeskills", "skill")]),
                CharacterCurrentRuleDomain.Qualities => ("qualities.xml", [("qualities", "quality")]),
                CharacterCurrentRuleDomain.Traditions => ("traditions.xml", [("traditions", "tradition"), ("spirits", "spirit")]),
                CharacterCurrentRuleDomain.Streams => ("streams.xml", [("traditions", "tradition")]),
                CharacterCurrentRuleDomain.Powers => ("powers.xml", [("powers", "power")]),
                CharacterCurrentRuleDomain.Spells => ("spells.xml", [("spells", "spell")]),
                CharacterCurrentRuleDomain.ComplexForms => ("complexforms.xml", [("complexforms", "complexform")]),
                CharacterCurrentRuleDomain.Gear => ("gear.xml", [("gears", "gear")]),
                CharacterCurrentRuleDomain.Lifestyles => ("lifestyles.xml", lifestyleQualities2778
                    ? [("lifestyles", "lifestyle"), ("qualities", "quality")] : [("lifestyles", "lifestyle")]),
                CharacterCurrentRuleDomain.Weapons => ("weapons.xml", [("weapons", "weapon")]),
                CharacterCurrentRuleDomain.Armor => ("armor.xml", [("armors", "armor")]),
                _ => (string.Empty, [])
            };
            var (file, groups) = domainCatalogMapping;
            if (file.Length == 0 || !TryLoadCurrentCatalogBaseDocument(file, out var document)
                || document?.Root is not { } root || root.Name != "chummer"
                || !root.Attributes().All(CurrentStockXmlMetadata.IsSupported))
                return null;
            var dependencies = Array.AsReadOnly(new[] { current.CaptureDigest, current.EffectivePolicyDigest,
                current.RequiredPackageSetDigest ?? string.Empty, current.SemanticsProfile, current.SemanticsFingerprint });
            var trace = new CurrentCatalogTrace(file, dependencies);
            var fragments = new List<(string Path, string Phase, XElement Root)>();
            foreach (var directory in _customDirectories)
            {
                var paths = EnumerateSourceFiles(directory.Path, "*_" + file, SearchOption.AllDirectories)
                    .Where(path => IsLegacyCustomDataInputFor(path, file))
                    .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(path => path, StringComparer.Ordinal).ToArray();
                foreach (string phase in new[] { "override_", "custom_", "amend_" })
                foreach (string path in paths.Where(path => Path.GetFileName(path).StartsWith(phase, StringComparison.OrdinalIgnoreCase)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryLoadXml(path, out var fragment) || fragment?.Root is not { } fragmentRoot
                        || !AdmitCurrentFragment(fragmentRoot, phase, groups,
                            domain == CharacterCurrentRuleDomain.Metatypes && UsesSpriteCatalog5751)) return null;
                    fragments.Add((path, phase, fragmentRoot));
                }
            }
            // Reuse writer buffers only for this catalog read. Every call returns
            // a complete, independent fragment; source/identity checks still run.
            using var xmlWriter = new ElementXmlWriter();
            Func<XElement, string> serialize = xmlWriter.Serialize;
            var entries = new List<CharacterCurrentRuleCatalogEntry>();
            var global = CreateCurrentCatalogHeader6247(root, groups
                .Where(group => !(lifestyleQualities2778 && group.Container == "qualities"))
                .Select(group => (XName)group.Container).ToArray());
            entries.Add(new("$global", "$global", serialize(global),
                CharacterCurrentRuleEntryState.AvailableNotSelected));
            var previousTrace = ActiveCurrentCatalogTrace.Value;
            try
            {
                ActiveCurrentCatalogTrace.Value = trace;
                foreach (var group in groups)
                {
                    if (root.Elements(group.Container).Count() > 1
                        || root.Elements(group.Container).Any(container => container.HasAttributes
                            || container.Elements().Any(row => row.Name != group.Entry
                                || !Guid.TryParse(ReadValue(row, "id"), out _) && string.IsNullOrWhiteSpace(ReadValue(row, "name")))))
                        return null;
                    var locators = new List<TargetLocator>();
                    AddLocators(root, [group.Container], group.Entry, locators);
                    foreach (var fragment in fragments) AddLocators(fragment.Root, [group.Container], group.Entry, locators);
                    var effective = new List<XElement>();
                    var effectiveXml = new List<string>();
                    var resolvedLocators = new Dictionary<string, CharacterCurrentRuleCatalogEntry?>(StringComparer.Ordinal);
                    // Targets below are cloned before amendment, so this base-document
                    // lookup remains valid for this group and this invocation only.
                    CatalogTargetLookup? baseLookup = null;
                    foreach (var locator in locators.Distinct()
                        .OrderBy(value => value.SourceId?.ToString("D") ?? string.Empty, StringComparer.Ordinal)
                        .ThenBy(value => value.Name, StringComparer.Ordinal))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        baseLookup ??= new(root, [group.Container], group.Entry);
                        if (baseLookup.Count(locator) > 1) return null;
                        XElement? target = baseLookup.Find(locator);
                        target = target is null ? null : new XElement(target);
                        XElement? last = target;
                        foreach (var fragment in fragments)
                        {
                            (trace.SourceIdentity, trace.SourceDigest) =
                                _sourceInputs.DescribeCurrentCatalogFragment7644(fragment.Path);
                            trace.Phase = fragment.Phase;
                            foreach (var row in fragment.Root.Elements(group.Container).Elements(group.Entry))
                            {
                                int observation = trace.Begin(ReadOperation(row), row, target);
                                var single = new XElement("chummer", new XElement(group.Container, new XElement(row)));
                                if (!TryApplyCustomFile(single, fragment.Phase, [group.Container], group.Entry,
                                        locator, ref target, currentSemantics: true)) return null;
                                trace.End(observation, target);
                                if (target is not null) last = target;
                            }
                        }
                        // Locator drift or ambiguous aliases would diverge from later lookup/quote calls.
                        if (target is not null && !LocatorMatches(target, locator)) return null;
                        XElement? recorded = target ?? last;
                        var identity = recorded is null ? locator
                            : TargetLocator.Create(ReadValue(recorded, "id"), ReadValue(recorded, "name"));
                        string key = group.Container + "/" + (identity.SourceId is { } id
                            ? "id:" + id.ToString("D") : "name:" + identity.Name);
                        CharacterCurrentRuleCatalogEntry? entry = recorded is null ? null : new(key,
                            ReadValue(recorded, "name"), serialize(recorded),
                            target is null ? CharacterCurrentRuleEntryState.Tombstone
                                : CharacterCurrentRuleEntryState.AvailableNotSelected);
                        // ID-only, named-ID and name-only locators can resolve one identity.
                        // Retain every trace, but emit that identity once only if all locators
                        // agree exactly, including tombstone/absent outcomes.
                        if (resolvedLocators.TryGetValue(key, out var previous))
                        {
                            if (previous != entry) return null;
                            continue;
                        }
                        resolvedLocators.Add(key, entry);
                        if (entry is not null && !(lifestyleQualities2778 && group.Container == "qualities")) entries.Add(entry);
                        if (target is not null)
                        {
                            effective.Add(target);
                            // The entry just serialized this exact final target. Retain that
                            // immutable string for comparison; no amendment runs between here
                            // and the independent ordinary-reader check below.
                            effectiveXml.Add(entry!.Xml);
                        }
                    }
                    // Re-run the ordinary target path on these same retained inputs. A trace cannot
                    // claim a catalog that the actual source/quote readers would resolve differently.
                    ActiveCurrentCatalogTrace.Value = null;
                    if (!TryEnumerateTargets(file, [group.Container], group.Entry, out var actual)
                        || !effectiveXml.Order(StringComparer.Ordinal)
                            .SequenceEqual(actual.Select(serialize).Order(StringComparer.Ordinal)))
                        return null;
                    ActiveCurrentCatalogTrace.Value = trace;
                    if (lifestyleQualities2778 && group.Container == "qualities"
                        && fragments.Any(fragment => fragment.Root.Element("qualities") is not null))
                    {
                        // Keep the established global quality catalog shape, but bind it
                        // to the same effective amendments as the ordinary source reader.
                        // All source operations and removed rows remain in the trace.
                        var effectiveQualities = new XElement("qualities", effective);
                        if (global.Element("qualities") is { } existingQualities) existingQualities.ReplaceWith(effectiveQualities);
                        else global.Add(effectiveQualities);
                        entries[0] = entries[0] with { Xml = serialize(global) };
                    }
                }
            }
            finally { ActiveCurrentCatalogTrace.Value = previousTrace; }
            if (_sourceInputs.HasSourceDrift || entries.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count() != entries.Count)
                return null;
            return CharacterCurrentRuleCatalogInput.CreateSnapshot(domain, file, current.BindingDigest,
                entries, trace.Operations, dependencies, []);
        }

        private static bool AdmitCurrentFragment(XElement root, string phase, (string Container, string Entry)[] groups,
            bool spriteCatalog5751)
        {
            if (root.Name != "chummer" || root.HasAttributes
                || root.DescendantNodes().Any(node => node is XProcessingInstruction or XComment)
                || root.Elements().Any(container => !groups.Any(group => group.Container == container.Name))
                || root.Elements().GroupBy(node => node.Name).Any(group => group.Count() != 1)) return false;
            foreach (var container in root.Elements())
            {
                var group = groups.Single(value => value.Container == container.Name);
                if (container.HasAttributes || container.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
                    || container.Elements().Any(node => node.Name != group.Entry)) return false;
                foreach (var entry in container.Elements())
                {
                    if (!Guid.TryParse(ReadValue(entry, "id"), out _) && string.IsNullOrWhiteSpace(ReadValue(entry, "name")))
                        return false;
                    if (entry.DescendantsAndSelf().Any(node => node.Name.NamespaceName.Length != 0
                        || node.Attributes().Any(attribute => attribute.Name.NamespaceName.Length != 0
                            || attribute.Name.LocalName is not ("amendoperation" or "addifnotfound" or "isidnode")
                                // Preserve source skill ratings in a Sprite skills replacement. This is
                                // raw syntax only: SupportsSource and the typed owner compiler still
                                // validate the selected effective source and its saved grants.
                                && !(spriteCatalog5751 && phase == "amend_" && attribute.Name == "rating"
                                    && container.Name == "metatypes" && entry.Name == "metatype"
                                    && node.Name == "skill" && !node.HasElements
                                    && !string.IsNullOrWhiteSpace(node.Value)
                                    && node.Parent is { } skills && skills.Name == "skills"
                                    && ReferenceEquals(skills.Parent, entry)
                                    && ReadOperation(skills) == "REPLACE")
                            || attribute.Name.LocalName is "addifnotfound" or "isidnode" && !bool.TryParse(attribute.Value, out _))
                        || node.Attribute("amendoperation") is { } operation && operation.Value != operation.Value.Trim()
                        || ReadOperation(node) is not ("" or "RECURSE" or "REPLACE" or "REMOVE" or "APPEND" or "ADDNODE")))
                        return false;
                    if (phase != "amend_" && entry.DescendantsAndSelf().Any(node => node.HasAttributes)
                        || phase == "amend_" && ReadOperation(entry) is "ADDNODE" or "APPEND") return false;
                }
            }
            return true;
        }
    }

    private sealed partial class SourceDataContext : IRetainedCurrentSourceSnapshot8110
    {
        bool IRetainedCurrentSourceSnapshot8110.IsCurrentRetainedSourceSnapshot8110()
            => _sourceInputs.HasImmutableRetainedInputs7956
                && _sourceInputs.TryAdmitReuse(_catalog);
    }

    private sealed partial class RuleSourceAccounting : ICurrentLifestyleSourceCapabilities2678,
        IRetainedCurrentRuleEvaluationIssuer2764, IRetainedOperationReportIssuer8005
    {
        public bool RequiresStockLifestyles(CharacterCurrentRuleEnvironmentContext current,
            CancellationToken cancellationToken)
        {
            var input = ReadCurrentCatalog(current, CharacterCurrentRuleDomain.Lifestyles, cancellationToken);
            return input is not null && CharacterCurrentRuleEnvironmentConsumers.RequiresStockLifestyles2678(input);
        }
        IDisposable? IRetainedCurrentRuleEvaluationIssuer2764.BeginRetainedEvaluation2764(
            CharacterCurrentRuleEnvironmentContext current)
        {
            // Digest equality cannot issue this lifetime. Require the concrete
            // capture's private inputs, private retained resolver and pure queries.
            // Caller-supplied captures, sources and query adapters keep replay.
            if (!OwnsImportedSheetInputs6371(current))
                return null;
            if (resolver._currentEvaluationReads2764 is not null || current.RetainedEvaluation2764 is not null)
                throw new InvalidOperationException("A retained evaluation is already active.");
            return new RetainedCurrentEvaluation2764(this, current);
        }

        private bool OwnsImportedSheetInputs6371(CharacterCurrentRuleEnvironmentContext current)
            => MatchesCurrentEnvironment(current)
                && current.Capture is RuleSourceCapture capture && capture.OwnsEvaluationInputs2764(inputs)
                && ReferenceEquals(resolver._retainedInputs, inputs)
                && current.Queries is XmlCharacterFileQueries { HasImmutableEvaluationQueries2764: true };

        ICharacterSourceDataResolverOperationScope? IRetainedCurrentRuleEvaluationIssuer2764.BeginImportedSheetOperation6371(
            CharacterCurrentRuleEnvironmentContext current)
        {
            if (!OwnsImportedSheetInputs6371(current) || current.RetainedEvaluation2764 is not null
                || resolver._currentEvaluationReads2764 is not null) return null;
            var reads = resolver.CreateValidatedReadScope();
            try { return new ImportedSheetOperation6371(this, current, reads, null, null, ownsReads: true); }
            catch { reads.Dispose(); throw; }
        }

        // Only the private accounting/retained issuer can construct this operation.
        // Exact XML is an additional restriction, never the authority for issuance.
        // The existing synchronous scope re-admits source bytes/membership on every
        // request and checks all discovered inputs on exit. A nested borrower owns
        // only that scope; standalone calculation also owns the enclosing reads.
        private sealed class ImportedSheetOperation6371 : ICharacterSourceDataResolverOperationScope
        {
            private readonly RuleSourceAccounting owner;
            private readonly CharacterCurrentRuleEnvironmentContext current;
            private readonly ValidatedReadScope reads;
            private readonly CharacterCurrentRuleEnvironmentConsumers.RetainedEvaluation2764? evaluation;
            private readonly ICharacterSourceDataResolverOperationScope operation;
            private readonly bool ownsReads;
            private readonly int thread = Environment.CurrentManagedThreadId;
            private SourceDataContext? source;
            private bool disposed;
            private bool invalidated;

            internal ImportedSheetOperation6371(RuleSourceAccounting owner, CharacterCurrentRuleEnvironmentContext current,
                ValidatedReadScope reads, SourceDataContext? source,
                CharacterCurrentRuleEnvironmentConsumers.RetainedEvaluation2764? evaluation, bool ownsReads)
            {
                this.owner = owner;
                this.current = current;
                this.reads = reads;
                this.source = source;
                this.evaluation = evaluation;
                this.ownsReads = ownsReads;
                Verify();
                operation = ownsReads ? reads.CreateOperationScope() : owner.resolver.CreateOperationScope();
            }

            public ICharacterSourceDataContext? TryCreateContext(string characterXml)
            {
                // A closed borrower cannot poison its still-active parent.
                ObjectDisposedException.ThrowIf(disposed, this);
                try
                {
                    Verify();
                    if (!string.Equals(characterXml, current.CharacterXml, StringComparison.Ordinal))
                        throw new InvalidOperationException("Imported sheet inputs changed.");
                    var admitted = operation.TryCreateContext(characterXml) as SourceDataContext;
                    if (admitted is null || !admitted.MatchesRetainedSelection(owner.inputs)
                        || source is not null && !ReferenceEquals(source, admitted))
                        throw new InvalidOperationException("Imported sheet source changed.");
                    source = admitted;
                    Verify();
                    return admitted;
                }
                catch { Invalidate(); throw; }
            }

            private void Verify()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (invalidated || thread != Environment.CurrentManagedThreadId
                    || !owner.OwnsImportedSheetInputs6371(current) || source?.SourceInputs.HasSourceDrift == true
                    || evaluation is not null && (source is null || !evaluation.Allows(current, source)
                        || !ReferenceEquals(owner.resolver._currentEvaluationReads2764, reads)))
                    throw new InvalidOperationException("Imported sheet operation is no longer current.");
            }

            private void Invalidate()
            {
                invalidated = true;
                evaluation?.Invalidate6371();
            }

            public void Dispose()
            {
                if (disposed) return;
                if (thread != Environment.CurrentManagedThreadId)
                {
                    Invalidate();
                    throw new InvalidOperationException("Imported sheet operations must remain synchronous.");
                }
                // Leave the existing synchronous operation to enforce thread and
                // nesting order too; never dispose an enclosing evaluation here.
                try
                {
                    try { Verify(); }
                    finally { operation.Dispose(); }
                    Verify();
                }
                catch { Invalidate(); throw; }
                finally
                {
                    disposed = true;
                    source = null;
                    if (ownsReads) reads.Dispose();
                }
            }
        }

        private sealed class RetainedCurrentEvaluation2764 : IDisposable
        {
            private readonly RuleSourceAccounting owner;
            private readonly ValidatedReadScope reads;
            private readonly SourceDataContext source;
            private readonly CharacterCurrentRuleEnvironmentConsumers.RetainedEvaluation2764 reuse;
            private readonly int thread = Environment.CurrentManagedThreadId;
            private bool disposed;

            internal RetainedCurrentEvaluation2764(RuleSourceAccounting owner,
                CharacterCurrentRuleEnvironmentContext current)
            {
                this.owner = owner;
                reads = owner.resolver.CreateValidatedReadScope();
                try
                {
                    source = reads.TryCreateContext(current.CharacterXml) as SourceDataContext
                        ?? throw new InvalidOperationException("Retained evaluation source unavailable.");
                    if (!source.MatchesRetainedSelection(owner.inputs))
                        throw new InvalidOperationException("Retained evaluation selection changed.");
                    reuse = new(current, source, () => !disposed && thread == Environment.CurrentManagedThreadId
                        && owner.OwnsImportedSheetInputs6371(current) && !source.SourceInputs.HasSourceDrift,
                        proof => new ImportedSheetOperation6371(owner, current, reads, source, proof, ownsReads: false));
                    owner.resolver._currentEvaluationThread2764 = thread;
                    owner.resolver._currentEvaluationReads2764 = reads;
                }
                catch { reads.Dispose(); throw; }
            }

            public void Dispose()
            {
                if (disposed) return;
                if (thread != Environment.CurrentManagedThreadId)
                    throw new InvalidOperationException("Retained evaluations must remain synchronous.");
                try
                {
                    // Checks include every input discovered in nested operations.
                    reads.ValidateOperationBoundary([source]);
                    reuse.VerifyBoundary();
                }
                finally
                {
                    disposed = true;
                    owner.resolver._currentEvaluationReads2764 = null;
                    owner.resolver._currentEvaluationThread2764 = 0;
                    reuse.Dispose();
                    reads.Dispose();
                }
            }
        }

        private string? currentEnvironmentProfile;
        private string? currentEnvironmentFingerprint;

        internal bool HasStockLifestyleSemantics2678
            => currentEnvironmentProfile == CurrentCharacterRuleSourceSemantics.ProfileV6
                && currentEnvironmentFingerprint == CurrentCharacterRuleSourceSemantics.FingerprintV6
                || HasLifestyleMechanics2684
                || CurrentCharacterRuleSourceSemantics.IsImportedPriorityCareerProfile5667(currentEnvironmentProfile, currentEnvironmentFingerprint);

        internal bool HasLifestyleMechanics2684
            => currentEnvironmentProfile == CurrentCharacterRuleSourceSemantics.ProfileV7
                && currentEnvironmentFingerprint == CurrentCharacterRuleSourceSemantics.FingerprintV7
                || CurrentCharacterRuleSourceSemantics.IsNativePriorityCareerProfile9569(currentEnvironmentProfile, currentEnvironmentFingerprint);

        internal bool HasCareerConditionSemantics5497
            => CurrentCharacterRuleSourceSemantics.IsCareerConditionProfile5497(currentEnvironmentProfile, currentEnvironmentFingerprint);

        internal bool HasImportedCareerOwnerSemantics5698 =>
            CurrentCharacterRuleSourceSemantics.IsOwnedPriorityCareerProfile9569(currentEnvironmentProfile, currentEnvironmentFingerprint);

        internal bool HasNativeCurrentEnvironmentSemantics1958
            => CurrentCharacterRuleSourceSemantics.IsNativeExecutionProfile(currentEnvironmentProfile, currentEnvironmentFingerprint);

        internal bool HasCurrentEnvironmentSemantics
            => CurrentCharacterRuleSourceSemantics.IsCurrentExecutionProfile(currentEnvironmentProfile, currentEnvironmentFingerprint);

        internal bool MatchesCurrentEnvironment(CharacterCurrentRuleEnvironmentContext current)
            => current.IsUnchanged && current.CaptureDigest == captureDigest
                && CurrentCharacterRuleSourceSemantics.IsCurrentExecutionProfile(currentEnvironmentProfile, currentEnvironmentFingerprint)
                && current.SemanticsProfile == currentEnvironmentProfile
                && current.SemanticsFingerprint == currentEnvironmentFingerprint;

        private sealed class CurrentEnvironmentObservations
        {
            internal readonly Dictionary<CharacterCurrentRuleCatalogValidation, string> Catalogs
                = new(ReferenceEqualityComparer.Instance);
            internal readonly Dictionary<CharacterCurrentRuleDomainExecution,
                (CharacterCurrentRuleCatalogValidation Catalog, string Digest)> Executions
                = new(ReferenceEqualityComparer.Instance);
        }

        private readonly ConditionalWeakTable<CharacterCurrentRuleEnvironmentContext,
            CurrentEnvironmentObservations> currentEnvironmentObservations = new();

        bool? IRetainedOperationReportIssuer8005.ValidateRetainedOperationReports8005(
            CharacterCurrentRuleEnvironmentContext current,
            IReadOnlyList<CharacterCurrentRuleCatalogValidation> catalogs,
            IReadOnlyList<CharacterCurrentRuleDomainExecution> executions,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.RetainedEvaluation2764 is not null || resolver._currentEvaluationReads2764 is not null
                || catalogs.Count != 14 || executions.Count != 14) return false;
            // The execution owns the outer arrays. Every catalog must also own
            // its complete detached graph before any repeated mutable check is
            // coalesced. Public/copy reports keep the original independent path.
            for (int index = 0; index < catalogs.Count; index++)
                if (!catalogs[index].IsDetachedSnapshot8005) return null;

            // Fresh complete mutable observation plus exact private capture,
            // retained source and immutable query ownership. Digest equality
            // alone cannot issue this attestation.
            if (!OwnsImportedSheetInputs6371(current)
                || !currentEnvironmentObservations.TryGetValue(current, out var observations)) return false;
            for (int index = 0; index < catalogs.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var catalog = catalogs[index];
                var execution = executions[index];
                // This loop touches only detached scalar/immutable reports and
                // this issuer's private identity tables. No source queries,
                // mutable input enumeration, owner callbacks or evaluation run
                // between the fresh mutable observations surrounding it.
                if ((int)catalog.Domain != index || execution.Domain != catalog.Domain
                    || !catalog.IsComplete || catalog.Input.ContextDigest != current.BindingDigest
                    || !observations.Catalogs.TryGetValue(catalog, out string? catalogDigest)
                    || catalogDigest != catalog.ContentDigest
                    || !execution.IsComplete || execution.ContextDigest != current.BindingDigest
                    || execution.CatalogDigest != catalogDigest
                    || !observations.Executions.TryGetValue(execution, out var frozen)
                    || !ReferenceEquals(frozen.Catalog, catalog) || frozen.Digest != execution.ContentDigest)
                    return false;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return OwnsImportedSheetInputs6371(current);
        }

        public override CharacterCurrentRuleCatalogValidation? ValidateCurrentRuleCatalog(
            CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleDomain domain,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchesCurrentEnvironment(current)) return null;
            var input = ReadCurrentCatalog(current, domain, cancellationToken);
            if (input is null || input.Domain != domain || input.ContextDigest != current.BindingDigest) return null;
            string frozenInput = input.ContentDigest;
            CharacterCurrentRuleCatalogValidation? result = null;
            switch (domain)
            {
                case CharacterCurrentRuleDomain.Skills:
                    ValidateCurrentSkillsCatalog(current, input, cancellationToken, ref result); break;
                case CharacterCurrentRuleDomain.Profile:
                case CharacterCurrentRuleDomain.Metatypes:
                case CharacterCurrentRuleDomain.Priorities:
                case CharacterCurrentRuleDomain.Lifestyles:
                    ValidateCurrentCreationCatalog(current, input, cancellationToken, ref result); break;
                case CharacterCurrentRuleDomain.Gear:
                case CharacterCurrentRuleDomain.Qualities:
                    ValidateCurrentPurchasesCatalog(current, input, cancellationToken, ref result); break;
                default:
                    ValidateCurrentTalentsLoadoutCatalog(current, input, cancellationToken, ref result); break;
            }
            if (result is null || !result.IsComplete || !ReferenceEquals(result.Input, input)
                || input.ContentDigest != frozenInput || !current.IsUnchanged
                || !SameCurrentCatalogCoverage(input, result)) return null;
            currentEnvironmentObservations.GetValue(current, static _ => new CurrentEnvironmentObservations()).Catalogs[result] = result.ContentDigest;
            return result;
        }

        private static bool SameCurrentCatalogCoverage(CharacterCurrentRuleCatalogInput input,
            CharacterCurrentRuleCatalogValidation validation)
        {
            if (input.Entries.Count != validation.Entries.Count) return false;
            var validatedByKey = new Dictionary<string, CharacterCurrentRuleCatalogEntry>(StringComparer.Ordinal);
            foreach (var entry in validation.Entries)
                if (entry is null || entry.Key is null || !validatedByKey.TryAdd(entry.Key, entry)) return false;
            foreach (var original in input.Entries)
            {
                if (original is null || original.Key is null
                    || !validatedByKey.TryGetValue(original.Key, out var result)
                    || result.Name != original.Name || result.Xml != original.Xml
                    || !Enum.IsDefined(result.State)
                    || original.Key == "$global" && result.State != CharacterCurrentRuleEntryState.AvailableNotSelected
                    || original.State == CharacterCurrentRuleEntryState.Tombstone && result.State != original.State
                    || original.State != CharacterCurrentRuleEntryState.Tombstone && result.State == CharacterCurrentRuleEntryState.Tombstone
                    || result.State == CharacterCurrentRuleEntryState.Unavailable && string.IsNullOrWhiteSpace(result.UnavailableReason))
                    return false;
            }
            return true;
        }

        public override bool HasValidatedCurrentRuleCatalog(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleCatalogValidation validation)
            => MatchesCurrentEnvironment(current) && validation.IsComplete
                && validation.Input.ContextDigest == current.BindingDigest
                && currentEnvironmentObservations.TryGetValue(current, out var observations)
                && observations.Catalogs.TryGetValue(validation, out string? frozen)
                && frozen == validation.ContentDigest;

        public override CharacterCurrentRuleDomainExecution? ExecuteCurrentRuleDomain(
            CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation validation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasValidatedCurrentRuleCatalog(current, validation)) return null;
            CharacterCurrentRuleDomainExecution? result = null;
            switch (validation.Domain)
            {
                case CharacterCurrentRuleDomain.Skills:
                    ExecuteCurrentSkills(current, validation, cancellationToken, ref result); break;
                case CharacterCurrentRuleDomain.Profile:
                case CharacterCurrentRuleDomain.Metatypes:
                case CharacterCurrentRuleDomain.Priorities:
                case CharacterCurrentRuleDomain.Lifestyles:
                    ExecuteCurrentCreation(current, validation, cancellationToken, ref result); break;
                case CharacterCurrentRuleDomain.Gear:
                case CharacterCurrentRuleDomain.Qualities:
                    ExecuteCurrentPurchases(current, validation, cancellationToken, ref result); break;
                default:
                    ExecuteCurrentTalentsLoadout(current, validation, cancellationToken, ref result); break;
            }
            if (result is null || !result.IsComplete || result.Domain != validation.Domain
                || result.ContextDigest != current.BindingDigest || result.CatalogDigest != validation.ContentDigest
                || !HasValidatedCurrentRuleCatalog(current, validation)) return null;
            currentEnvironmentObservations.GetValue(current, static _ => new CurrentEnvironmentObservations()).Executions[result] = (validation, result.ContentDigest);
            return result;
        }

        public override bool HasExecutedCurrentRuleDomain(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleCatalogValidation validation, CharacterCurrentRuleDomainExecution execution)
            => HasValidatedCurrentRuleCatalog(current, validation) && execution.IsComplete
                && currentEnvironmentObservations.TryGetValue(current, out var observations)
                && observations.Executions.TryGetValue(execution, out var frozen)
                && ReferenceEquals(frozen.Catalog, validation) && frozen.Digest == execution.ContentDigest;

        internal CharacterCurrentRuleCatalogInput? ReadCurrentCatalog(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleDomain domain, CancellationToken cancellationToken)
        {
            using var scope = resolver.CreateOperationScope();
            return scope.TryCreateContext(current.CharacterXml) is SourceDataContext source
                && source.MatchesRetainedSelection(inputs)
                ? source.ReadCurrentCatalog(current, domain, cancellationToken) : null;
        }

        // Optional partial hooks disappear until their owning lane supplies a real implementation.
        // Null is failure, never NotConfigured. Do not add duplicate stubs in domain afterfiles.
        partial void ValidateCurrentSkillsCatalog(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleCatalogInput input, CancellationToken cancellationToken,
            ref CharacterCurrentRuleCatalogValidation? result);
        partial void ExecuteCurrentSkills(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleCatalogValidation validation, CancellationToken cancellationToken,
            ref CharacterCurrentRuleDomainExecution? result);
        partial void ValidateCurrentCreationCatalog(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleCatalogInput input, CancellationToken cancellationToken,
            ref CharacterCurrentRuleCatalogValidation? result);
        partial void ExecuteCurrentCreation(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleCatalogValidation validation, CancellationToken cancellationToken,
            ref CharacterCurrentRuleDomainExecution? result);
        partial void ValidateCurrentTalentsLoadoutCatalog(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleCatalogInput input, CancellationToken cancellationToken,
            ref CharacterCurrentRuleCatalogValidation? result);
        partial void ExecuteCurrentTalentsLoadout(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleCatalogValidation validation, CancellationToken cancellationToken,
            ref CharacterCurrentRuleDomainExecution? result);
        partial void ValidateCurrentPurchasesCatalog(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleCatalogInput input, CancellationToken cancellationToken,
            ref CharacterCurrentRuleCatalogValidation? result);
        partial void ExecuteCurrentPurchases(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleCatalogValidation validation, CancellationToken cancellationToken,
            ref CharacterCurrentRuleDomainExecution? result);
    }
}

internal static class CurrentStockXmlMetadata
{
    private static readonly XNamespace SchemaInstance = "http://www.w3.org/2001/XMLSchema-instance";

    // These declarations and the expanded schemaLocation name are inert metadata.
    // No schema is loaded; other namespace declarations and semantic attributes
    // (including xsi:type/xsi:nil) remain unsupported.
    internal static bool IsSupported(XAttribute attribute)
        => attribute.IsNamespaceDeclaration
            ? attribute.Name == "xmlns" && attribute.Value.Length == 0
                || attribute.Name.Namespace == XNamespace.Xmlns && attribute.Value == SchemaInstance.NamespaceName
            : attribute.Name == SchemaInstance + "schemaLocation";
}
