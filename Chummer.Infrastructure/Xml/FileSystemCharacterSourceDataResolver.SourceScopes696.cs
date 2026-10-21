using System.Xml.Linq;
using System.Runtime.CompilerServices;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class RuleSourceAccounting
    {
        private readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation,
            Dictionary<CharacterCreationRuleSourceScopeResult, string>> executedScopes955 = new();

        public override bool HasExecutedSourceScope955(CharacterCreationRuleSourceScopeInvocation invocation,
            CharacterCreationRuleSourceScopeResult report)
            => report.IsComplete && executedScopes955.TryGetValue(invocation, out var results)
                && results.TryGetValue(report, out var digest)
                && digest == SourceScopeExecutionDigest955(invocation, report);

        private static string SourceScopeExecutionDigest955(CharacterCreationRuleSourceScopeInvocation invocation,
            CharacterCreationRuleSourceScopeResult report)
            => CharacterCreationFinalizationDigest.Compute(new
            {
                invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
                Consumer = invocation.TryGetCompletedConsumer(report.Scope, out var consumer) ? consumer : null,
                Report = report
            });

        public override string? ExecuteAmendmentAbsence954()
        {
            if (!CapturePurchases || !captureCompleteSkillCaps
                || inputs.Catalog.Overlays.Any(pack => pack.Enabled)
                || inputs.SelectionInputs.Count != 0) return null;
            using var operation = resolver.CreateOperationScope();
            if (operation.TryCreateContext(inputs.CharacterXml) is not SourceDataContext context
                || !context.MatchesRetainedSelection(inputs)
                || !context.TryResolveCreationEffectivePolicy(out var policy) || policy is null
                || policy.PolicyDigest != inputs.PolicyDigest) return null;
            return context.ExecuteAmendmentAbsence954(inputs, captureDigest);
        }

        public override IReadOnlyList<CharacterCreationRuleSourceScopeResult> ExecuteRemainingSourceScopes(
            CharacterCreationRuleSourceScopeInvocation invocation)
        {
            executedScopes955.Remove(invocation);
            if (!CapturePurchases
                || invocation.CaptureDigest != captureDigest)
                return [];
            using var operation = resolver.CreateOperationScope();
            // The capture binds the raw inventory. Invocation.PolicyDigest binds
            // the enriched consumer policy, checked by the private books consumer;
            // these are different contracts, as in the owner-bound input reader.
            if (operation.TryCreateContext(inputs.CharacterXml) is not SourceDataContext context
                || !context.MatchesRetainedSelection(inputs)
                || !context.TryResolveCreationEffectivePolicy(out var policy) || policy is null
                || policy.PolicyDigest != inputs.PolicyDigest) return [];

            var result = new List<CharacterCreationRuleSourceScopeResult>();
            foreach (var source in policy.Sources.Where(row => row.Kind == CharacterCreationPolicySourceKind.Rules))
            {
                if (!invocation.TryGetCompletedConsumer(source.SourceId, out var consumer)
                    || source.ContentDigest is null) continue;
                Charge(1);
                if (context.TryExecuteSourceScope(source, consumer, invocation, out var report))
                    result.Add(report!);
            }
            // Identity and the frozen contents both matter: neither a record
            // copy nor mutation of an operation list inherits completed execution.
            var executed = new Dictionary<CharacterCreationRuleSourceScopeResult, string>(ReferenceEqualityComparer.Instance);
            foreach (var report in result)
                if (report.IsComplete) executed.Add(report, SourceScopeExecutionDigest955(invocation, report));
            lock (executedScopes955)
            {
                executedScopes955.Remove(invocation);
                executedScopes955.Add(invocation, executed);
            }
            return result;
        }
    }

    private sealed partial class SourceDataContext
    {
        internal string? ExecuteAmendmentAbsence954(RetainedCharacterRuleSourceInputs retained, string capture)
        {
            using var read = _sourceInputs.Enter();
            // Re-enter the real source selection after retained policy validation.
            // Catalog enumeration and resolved directories are private inputs, not
            // the public policy inventory or the candidate's proposed selections.
            if (_sourceInputs.HasSourceDrift || _catalog.Overlays.Any(pack => pack.Enabled)
                || _customDirectories.Count != 0 || !MatchesRetainedSelection(retained)
                || !TryResolveTarget(SettingsFile, ["settings"], "setting", _settingsProfileId, string.Empty, out var profile)
                || profile is null || !EmptyAmendmentSelection954(profile)
                || XDocument.Parse(retained.CharacterXml).Root is not { } character
                || !EmptyAmendmentSelection954(character) || _sourceInputs.HasSourceDrift) return null;
            return CharacterCreationFinalizationDigest.Compute(new
            {
                CaptureDigest = capture, RawPolicyDigest = retained.PolicyDigest,
                Profile = profile.ToString(SaveOptions.DisableFormatting),
                OriginalCharacter = CharacterCreationFinalizationDigest.ComputeUtf8(retained.CharacterXml),
                Evaluation = "resolved-global-and-selected-amendment-absence954"
            });
        }

        private static bool EmptyAmendmentSelection954(XElement parent)
        {
            // The existing selector can ignore malformed/unknown entries. This
            // bounded evaluator accepts only an absent or plain empty container;
            // even disabled nonempty custom selections remain unsupported here.
            var nodes = parent.Elements().Where(candidate => candidate.Name.LocalName == "customdatadirectorynames").ToArray();
            return nodes.Length == 0 || nodes is [var selection]
                && selection.Name == "customdatadirectorynames" && !selection.HasAttributes
                && selection.Nodes().All(child => child is XText text && child is not XCData
                    && string.IsNullOrWhiteSpace(text.Value));
        }

        internal bool TryExecuteSourceScope(CharacterCreationPolicySource source, string consumer,
            CharacterCreationRuleSourceScopeInvocation invocation,
            out CharacterCreationRuleSourceScopeResult? report)
        {
            report = null;
            using var read = _sourceInputs.Enter();
            string scope = source.SourceId;
            if (scope is "profile" or "metatypes" or "priorities" or "lifestyles")
                return TryExecuteCreationSource718(source, consumer, invocation, out report);
            if (!SourceScopeLayout.TryGetValue(scope, out var layout)) return false;
            string file = scope + ".xml";
            if (_sourceInputs.HasSourceDrift
                || !TryComputeEffectiveInputDigest(_catalog, file, out string digest)
                || digest != source.ContentDigest
                || !TryLoadEffectiveDocument(_catalog, file, out var document)
                || document?.Root is not { } root) return false;
            var ledger = new SourceScopeLedger(root, invocation, consumer);
            report = new($"source:{source.Kind}:{source.Order}", scope, digest, consumer, ledger.Operations);

            // No result can conceal a custom/overlay operation whose selector,
            // ordering, deletion, or affected target has not been accounted.
            // Those remain explicit unconsumed source nodes in this report.
            if (!TryHasSelectedCustomDataInputFor(_customDirectories, file, out bool custom) || custom
                || !NoOverlayForSource(file)
                || !ValidScopeEnvelope(root, scope, layout)) return true;

            // Re-enter the domain resolver on the retained snapshot. This is a
            // source-to-consumer cross-check, not itself evidence of execution.
            bool resolved = scope switch
            {
                "traditions" or "streams" or "powers" or "spells" or "complexforms" =>
                    TryResolveCreationMagicResonanceAuthority(out var magic) && magic.IsAuthoritative,
                "weapons" or "armor" => TryResolveCreationLoadoutAuthority(out var loadout) && loadout.IsAuthoritative,
                _ => false
            };
            if (!resolved || _sourceInputs.HasSourceDrift) return true;
            var active = invocation.CompiledEffects.Where(row => row.Scope == scope).ToArray();
            if (active.Select(row => row.SourceId).Distinct(StringComparer.Ordinal).Count() != active.Length)
                return true;
            var encountered = new HashSet<string>(StringComparer.Ordinal);
            ledger.Node(root, D.Structural, "catalogue-envelope");
            foreach (var container in root.Elements())
            {
                var definition = layout.Single(row => row.Container == container.Name.LocalName);
                // Spell category skill fallbacks are not executed by the current
                // OptionSource compiler. Retain that real active-content gap.
                if (scope == "spells" && active.Length != 0 && container.Name == "categories") continue;
                ledger.Node(container, D.Structural, "catalogue-domain");
                var identities = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in container.Elements())
                {
                    if (!TryScopeRowIdentity(row, definition, out string identity,
                        inactiveWeaponCategory832: invocation.SourceExecution832 && scope == "weapons" && definition.Entry == "category" && active.Length == 0)
                        || !identities.Add(identity)) continue;
                    var selected = definition.Primary
                        ? active.Where(effect => effect.SourceId == identity).ToArray() : [];
                    if (selected.Length == 0)
                    {
                        // The completed typed consumer supplied its *entire*
                        // selection set. These are per-row negative selector
                        // results, not inference from an empty inventory.
                        ledger.Tree(row, D.NotSelected, new
                        {
                            Domain = definition.Entry, Identity = identity,
                            Selections = active.Select(effect => effect.SourceId).ToArray()
                        });
                        continue;
                    }
                    if (selected is not [var effect]
                        || NormalizeSource(row) != effect.SourceXml
                        || !CharacterCreationFinalizationDigest.IsCanonical(effect.ProjectionDigest))
                        continue;
                    encountered.Add(identity);
                    ledger.Tree(row, D.Compiled, new
                    {
                        effect.ProjectionDigest,
                        Effect = CharacterCreationFinalizationDigest.Compute(effect.EffectXml)
                    });
                }
            }
            if (!encountered.SetEquals(active.Select(row => row.SourceId)))
                ledger.UnconsumeRoot();
            return true;
        }

        private bool NoOverlayForSource(string file)
        {
            foreach (var pack in _catalog.Overlays.Where(pack => pack.Enabled))
            {
                if (string.IsNullOrWhiteSpace(pack.DataPath) || !SourceDirectoryExists(pack.DataPath)) return false;
                if (pack.Mode == ContentOverlayModes.ReplaceFile)
                {
                    if (SourceFileExists(Path.Combine(pack.DataPath, file))) return false;
                }
                else if (pack.Mode == ContentOverlayModes.MergeCatalog)
                {
                    if (EnumerateSourceFiles(pack.DataPath, "*.xml", SearchOption.TopDirectoryOnly)
                        .Any(path => string.Equals(ResolveCatalogTargetFileName(Path.GetFileName(path)),
                            file, StringComparison.OrdinalIgnoreCase))) return false;
                }
                else return false;
            }
            return true;
        }

        private static string NormalizeSource(XElement source)
            => XElement.Parse(source.ToString(SaveOptions.DisableFormatting), LoadOptions.None)
                .ToString(SaveOptions.DisableFormatting);

        private static bool ValidScopeEnvelope(XElement root, string scope, SourceScopeContainer[] layout)
        {
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            if (root.Name != "chummer" || HasDirectScopeText(root)
                || root.DescendantNodes().OfType<XProcessingInstruction>().Any()
                || root.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration
                    && !(attribute.Name == xsi + "schemaLocation"
                        && attribute.Value == "http://www.w3.org/2001/XMLSchema " + scope + ".xsd"))
                || root.Elements().GroupBy(row => row.Name).Any(group => group.Count() != 1)
                || root.Elements().Any(container => container.Name.Namespace != XNamespace.None
                    || container.HasAttributes || HasDirectScopeText(container)
                    || !layout.Any(row => row.Container == container.Name.LocalName))) return false;
            // Every declared container which exists is accounted. The primary
            // consumer's catalogue must exist even when its row count is zero.
            return layout.Where(row => row.Primary).All(row => root.Element(row.Container) is not null)
                && root.Elements().All(container =>
                    container.Elements().All(row => row.Name == layout.Single(
                        definition => definition.Container == container.Name.LocalName).Entry));
        }

        private static bool TryScopeRowIdentity(XElement row, SourceScopeContainer definition, out string identity,
            bool inactiveWeaponCategory832 = false)
        {
            identity = string.Empty;
            if (row.Name != definition.Entry) return false;
            if (definition.Entry == "category")
            {
                if (row.HasElements || string.IsNullOrWhiteSpace(row.Value)
                    || row.Attributes().Any(attribute => attribute.Name.Namespace != XNamespace.None
                        || attribute.Name.LocalName is not ("blackmarket" or "type" or "useskill"
                            or "alchemicalskill" or "barehandedadeptskill")
                        && !(inactiveWeaponCategory832 && attribute.Name.LocalName == "gunneryspec"))) return false;
                identity = row.Value;
                return true;
            }
            if (row.HasAttributes || HasDirectScopeText(row)) return false;
            var keys = row.Elements("id").ToArray();
            if (keys is not [var key] || key.HasAttributes || key.HasElements || string.IsNullOrWhiteSpace(key.Value))
                return false;
            if (!Guid.TryParse(key.Value, out var id) || id == Guid.Empty) return false;
            identity = id.ToString("D");
            return true;
        }

        private static bool HasDirectScopeText(XElement node) => node.Nodes().OfType<XText>()
            .Any(text => !string.IsNullOrWhiteSpace(text.Value));
    }

    private sealed record SourceScopeContainer(string Container, string Entry, bool Primary = false);

    private static readonly IReadOnlyDictionary<string, SourceScopeContainer[]> SourceScopeLayout =
        new Dictionary<string, SourceScopeContainer[]>(StringComparer.Ordinal)
        {
            ["traditions"] = [new("traditions", "tradition", true), new("spirits", "spirit"), new("drainattributes", "drainattribute")],
            ["streams"] = [new("traditions", "tradition", true), new("spirits", "spirit")],
            ["powers"] = [new("powers", "power", true), new("enhancements", "enhancement")],
            ["spells"] = [new("spells", "spell", true), new("categories", "category")],
            ["complexforms"] = [new("complexforms", "complexform", true)],
            ["weapons"] = [new("weapons", "weapon", true), new("accessories", "accessory"), new("categories", "category")],
            ["armor"] = [new("armors", "armor", true), new("mods", "mod"), new("categories", "category"), new("modcategories", "category")]
        };

    // Every source element, attribute and non-whitespace text occurrence starts
    // unconsumed. Structural consumption never consumes mixed text. A complete
    // report therefore cannot lose an unknown sibling or silently skip a row.
    private sealed class SourceScopeLedger
    {
        private readonly Dictionary<XObject, int> indices = [];
        private readonly CharacterCreationRuleSourceScopeInvocation invocation;
        private readonly string consumer;
        private readonly string obligation886;
        private readonly List<CharacterCreationSourceOperation> operations = [];
        internal IReadOnlyList<CharacterCreationSourceOperation> Operations => operations.AsReadOnly();

        internal SourceScopeLedger(XElement root, CharacterCreationRuleSourceScopeInvocation invocation, string consumer, string obligation886 = "")
        {
            this.invocation = invocation;
            this.consumer = consumer;
            this.obligation886 = obligation886;
            Walk(root, "/chummer[0]");
        }

        private void Walk(XElement node, string path, int depth = 0)
        {
            if (depth > 128 || path.Length > RetainedCharacterRuleSourceInputs.MaximumTextBytes)
                throw new InvalidDataException("Rule-source execution structure bound exceeded.");
            Add(node, path, node.ToString(SaveOptions.DisableFormatting));
            foreach (var attribute in node.Attributes()) Add(attribute, path + "/@" + attribute.Name, attribute.ToString());
            int index = 0;
            foreach (var child in node.Nodes())
            {
                if (child is XElement element) Walk(element, path + "/" + element.Name + "[" + index + "]", depth + 1);
                else if (child is XText text && !string.IsNullOrWhiteSpace(text.Value))
                    Add(text, path + "/text()[" + index + "]", text.Value);
                else if (child is XProcessingInstruction instruction)
                    Add(instruction, path + "/instruction()[" + index + "]", instruction.ToString());
                index++;
            }
        }

        private void Add(XObject node, string path, string source)
        {
            if (operations.Count >= 131072) throw new InvalidDataException("Rule-source execution observation bound exceeded.");
            indices.Add(node, operations.Count);
            operations.Add(new(operations.Count, path, CharacterCreationFinalizationDigest.Compute(source), D.Unconsumed, null));
        }

        internal void Node(XElement node, D disposition, object result)
            => Consume(new XObject[] { node }.Concat(node.Attributes()), disposition, result);

        internal void Tree(XElement node, D disposition, object result)
        {
            var selected = node.DescendantsAndSelf().Cast<XObject>()
                .Concat(node.DescendantsAndSelf().SelectMany(element => element.Attributes()))
                .Concat(node.DescendantNodes().Where(child => child is not XElement));
            Consume(selected, disposition, result);
        }

        private void Consume(IEnumerable<XObject> selected, D disposition, object result)
        {
            string digest = CharacterCreationFinalizationDigest.Compute(new
            {
                invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
                Consumer = consumer, Disposition = disposition, Result = result
            });
            string diagnostic = System.Text.Json.JsonSerializer.Serialize(result);
            foreach (var node in selected)
                if (indices.TryGetValue(node, out int index))
                    operations[index] = operations[index] with
                    {
                        Disposition = disposition, ExecutionDigest = digest,
                        ConsumedValue718 = node switch
                        {
                            XElement element => element.HasElements ? element.Name.ToString() : element.Value,
                            XAttribute attribute => attribute.ToString(),
                            XText text => text.Value,
                            _ => node.ToString()
                        },
                        OperationResult718 = diagnostic
                    };
        }

        internal void LegacyIni886(XElement selected, XElement scalar)
        {
            foreach (XObject occurrence in new XObject[] { scalar }.Concat(scalar.Nodes().OfType<XText>()))
                if (indices.TryGetValue(occurrence, out int index)
                    && invocation.ClassifyLegacyIni886(obligation886, selected, scalar, occurrence, operations[index]) is { } classified)
                    operations[index] = classified;
        }

        internal void UnconsumeRoot() => operations[0] = operations[0] with { Disposition = D.Unconsumed, ExecutionDigest = null, ConsumedValue718 = null, OperationResult718 = null };
    }
}
