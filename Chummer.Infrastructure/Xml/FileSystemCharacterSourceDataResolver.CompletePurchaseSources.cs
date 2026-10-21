using System.Xml.Linq;
using System.Xml.XPath;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class RuleSourceAccounting
    {
        // Multiple anatomical quality projections can occur in one read. Their
        // exact option sets have separate observations; a cache hit reuses only
        // an observation of the same immutable canonical projection.
        private readonly Dictionary<string, PurchaseCatalogueTrace> purchaseCatalogues = new(StringComparer.Ordinal);
        internal bool CapturePurchases { get; private set; }

        public override bool EnablePurchaseSourceExecution()
        {
            CapturePurchases = true;
            return true;
        }

        internal void RetainPurchaseCatalogue(string key, PurchaseCatalogueTrace trace)
        {
            Charge(1);
            purchaseCatalogues[key] = trace;
        }

        public override IReadOnlyDictionary<string, string> ConsumeGearAndQualitySources(
            CharacterCreationGearAuthority gear, CharacterCreationQualitiesAuthority qualities,
            IReadOnlyList<CharacterCreationGearLine> lines, CharacterCreationGearBudget budget,
            CharacterCreationQualitiesCompletionEvaluation evaluatedQualities,
            IReadOnlyList<CharacterCreationTalentGearSource> grantedGear)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!CapturePurchases) return result;
            using var scope = resolver.CreateOperationScope();
            if (scope.TryCreateContext(inputs.CharacterXml) is not SourceDataContext context
                || !context.MatchesRetainedSelection(inputs)
                || !context.TryResolveCreationEffectivePolicy(out var policy)
                || policy is null || policy.PolicyDigest != inputs.PolicyDigest)
                return result;

            // These DTOs are comparison values from Complete's privately executed
            // consumers. They cannot populate the projection observations below.
            if (gear.IsAuthoritative && budget.IsExact && budget.Blockers.Count == 0
                && purchaseCatalogues.TryGetValue("gear:" + gear.AuthorityDigest, out var gearTrace)
                && gearTrace.MatchesGear(gear, lines, grantedGear, out var gearDecisions))
                Add("gear", gearTrace, gearDecisions, new { Lines = lines, Budget = budget, GrantedGear = grantedGear });
            if (qualities.IsAuthoritative && evaluatedQualities.Blockers.Count == 0
                && purchaseCatalogues.TryGetValue(PurchaseCatalogueTrace.QualityKey(qualities.Options,
                    qualities.ProfileDigest), out var qualityTrace)
                && qualityTrace.MatchesQualities(qualities, evaluatedQualities.Selections, out var qualityDecisions))
                Add("qualities", qualityTrace, qualityDecisions, evaluatedQualities);
            return result;

            void Add(string sourceId, PurchaseCatalogueTrace trace, object decisions, object evaluated)
            {
                var sources = policy.Sources.Where(row => row.Kind == CharacterCreationPolicySourceKind.Rules
                    && row.SourceId == sourceId).ToArray();
                if (sources is not [var source] || source.ContentDigest is null
                    || !trace.MatchesPolicy(policy)) return;
                result.Add("source:" + source.Kind + ":" + source.Order,
                    CharacterCreationFinalizationDigest.Compute(new
                    {
                        Schema = "chummer.private-purchase-source-execution.v2",
                        Source = source, policy.PolicyDigest,
                        Profile = trace.ProfileDigest, trace.MetadataDigest,
                        Decisions = decisions, Evaluated = evaluated
                    }));
            }
        }
    }

    private sealed class PurchaseCatalogueTrace
    {
        private sealed record Choice(string OptionId, string Digest, bool SourceEnabled, bool Selectable);
        private sealed record Row(Guid SourceId, string Xml, string Book, List<Choice> Choices);
        private readonly RuleSourceAccounting accounting;
        private readonly Dictionary<Guid, Row> rows;
        private readonly string[] books;
        private readonly string profileId;
        internal string ProfileDigest { get; }
        internal string MetadataDigest { get; }
        private bool valid = true;

        internal PurchaseCatalogueTrace(RuleSourceAccounting accounting, XElement[] sources,
            string profileId, string profileDigest, IEnumerable<string> enabledBooks, XElement root)
        {
            this.accounting = accounting;
            this.profileId = profileId;
            ProfileDigest = profileDigest;
            books = enabledBooks.Order(StringComparer.Ordinal).ToArray();
            MetadataDigest = CharacterCreationFinalizationDigest.Compute(root.ToString(SaveOptions.DisableFormatting));
            rows = sources.ToDictionary(row => Guid.Parse(ReadValue(row, "id")), row =>
                new Row(Guid.Parse(ReadValue(row, "id")), row.ToString(SaveOptions.DisableFormatting),
                    ReadValue(row, "source"), []));
        }

        internal void Gear(XElement row, bool enabled, CharacterCreationGearCatalogOption option)
            => Observe(row, option.SourceId, option.OptionId, option.OptionDigest, enabled, option.IsSelectable);

        internal void Quality(XElement row, bool enabled, CharacterCreationQualityCatalogOption option)
            => Observe(row, option.SourceId, option.OptionId, option.OptionDigest, enabled,
                option.IsSelectable && option.EligibilityIsExact);

        private void Observe(XElement source, Guid id, string optionId, string digest, bool enabled, bool selectable)
        {
            if (!rows.TryGetValue(id, out var row) || row.Xml != source.ToString(SaveOptions.DisableFormatting)
                || row.Choices.Any(choice => choice.OptionId == optionId)) { valid = false; return; }
            // enabled/selectable are the actual values from the canonical loop.
            row.Choices.Add(new(optionId, digest, enabled, selectable));
        }

        internal void Finish(CharacterCreationGearAuthority authority)
        {
            if (valid && authority.IsAuthoritative)
                accounting.RetainPurchaseCatalogue("gear:" + authority.AuthorityDigest, this);
        }

        internal void FinishQualities(IReadOnlyList<CharacterCreationQualityCatalogOption> options, bool blocked)
        {
            if (valid && !blocked && options.Count > 0)
                accounting.RetainPurchaseCatalogue(QualityKey(options, ProfileDigest), this);
        }

        internal static string QualityKey(IReadOnlyList<CharacterCreationQualityCatalogOption> options, string profileDigest)
            => "qualities:" + CharacterCreationFinalizationDigest.Compute(new
            {
                profileDigest,
                // The completion service may enable unrelated knowledge-cost
                // options. Source custody is unchanged; selected options must
                // still match this parser's exact eligibility/result below.
                Options = options.Select(option => new
                {
                    option.OptionId, option.SourceNodeXml, option.ResolvedArmCount
                }).ToArray()
            });

        internal bool MatchesPolicy(CharacterCreationEffectivePolicyContext policy)
            => valid && profileId == policy.SettingsProfileId && ProfileDigest == policy.ProfileInputsDigest
                && books.SequenceEqual(policy.EnabledSourcebooks.Order(StringComparer.Ordinal), StringComparer.Ordinal);

        internal bool MatchesGear(CharacterCreationGearAuthority authority, IReadOnlyList<CharacterCreationGearLine> lines,
            IReadOnlyList<CharacterCreationTalentGearSource> grantedGear, out object decisions)
        {
            decisions = Array.Empty<string>();
            if (authority.ProfileDigest != ProfileDigest || lines.Select(row => row.OptionId).Distinct().Count() != lines.Count)
                return false;
            foreach (var line in lines)
            {
                var matches = authority.Options.Where(option => option.OptionId == line.OptionId).ToArray();
                if (matches is not [var option] || option.SourceId != line.SourceId
                    || option.SourceNodeXml != line.SourceNodeXml || option.SourceNodeDigest != line.SourceNodeDigest
                    || !Matches(option.SourceId, option.OptionId, option.OptionDigest, option.SourceNodeXml))
                    return false;
            }
            // A grant is not a purchase: hidden/non-purchasable rows can be
            // granted by the canonical Talent compiler. Match the retained
            // definition and enabled book, never invent a zero-price purchase.
            foreach (var grant in grantedGear)
            {
                if (!CharacterCreationTalentQualitySourceRules.IsValidGearSource(grant)
                    || !Guid.TryParse(grant.SourceId, out var id)
                    || !rows.TryGetValue(id, out var row)
                    // Same canonicalization as the existing Talent source
                    // projector; private source metadata still binds raw input.
                    || XElement.Parse(row.Xml, LoadOptions.None).ToString(SaveOptions.DisableFormatting) != grant.CanonicalSourceXml
                    || row.Book != grant.SourceBook
                    || !books.Contains(grant.SourceBook, StringComparer.OrdinalIgnoreCase))
                    return false;
            }
            decisions = new
            {
                Purchased = Decisions(lines.Select(row => row.OptionId)),
                Granted = grantedGear.Select(grant => new
                {
                    grant.SourceId, grant.EffectiveSourceDigest, grant.SourceNodeDigest,
                    grant.CanonicalSourceXmlDigest, grant.SourceBook, grant.SourceAnchorIds
                }).ToArray()
            };
            return true;
        }

        internal bool MatchesQualities(CharacterCreationQualitiesAuthority authority,
            IReadOnlyList<CharacterCreationQualitySelection> selections, out object decisions)
        {
            decisions = Array.Empty<string>();
            if (authority.ProfileDigest != ProfileDigest
                || selections.Select(row => row.OptionId).Distinct().Count() != selections.Count) return false;
            foreach (var selection in selections)
            {
                var matches = authority.Options.Where(option => option.OptionId == selection.OptionId).ToArray();
                if (matches is not [var option] || option.SourceId != selection.SourceId
                    || option.SourceNodeXml != selection.SourceNodeXml || option.OptionDigest != selection.OptionDigest
                    || !Matches(option.SourceId, option.OptionId, option.OptionDigest, option.SourceNodeXml))
                    return false;
            }
            decisions = Decisions(selections.Select(row => row.OptionId));
            return true;
        }

        private bool Matches(Guid sourceId, string optionId, string digest, string xml)
            => valid && rows.TryGetValue(sourceId, out var row) && row.Xml == xml
                && row.Choices.Count(choice => choice.OptionId == optionId && choice.Digest == digest
                    && choice.SourceEnabled && choice.Selectable) == 1;

        private object Decisions(IEnumerable<string> selectedOptions)
        {
            var selected = selectedOptions.ToHashSet(StringComparer.Ordinal);
            return rows.Values.OrderBy(row => row.SourceId).Select(row => new
            {
                row.SourceId, SourceNode = CharacterCreationFinalizationDigest.Compute(row.Xml), row.Book,
                // A malformed/non-projectable row skipped by the ordinary parser
                // is not activated merely because it is in the full catalogue.
                Projection = row.Choices.Count == 0 ? "parser-skipped" : "projected",
                Choices = row.Choices.Select(choice => new
                {
                    choice.OptionId, choice.Digest, choice.SourceEnabled, choice.Selectable,
                    Selection = selected.Contains(choice.OptionId) ? "executed" : "not-selected"
                }).ToArray()
            }).ToArray();
        }
    }

    private sealed partial class SourceDataContext
    {
        private PurchaseCatalogueTrace? BeginPurchaseCatalogue(string scope, string container, string entry, XElement[] rows)
        {
            var accounting = _sourceInputs.Accounting;
            if (accounting is not { CapturePurchases: true } || _sourceInputs.HasSourceDrift) return null;
            string file = scope + ".xml";
            // Arbitrary custom/override/amend operations require their own source
            // semantics. Neither an identical digest nor an enabled book proves them.
            if (!TryHasSelectedCustomDataInputFor(_customDirectories, file, out bool custom) || custom
                || !TryLoadEffectiveDocument(_catalog, file, out var document) || document?.Root is not { } root
                || !ValidPurchaseCatalogue(root, scope, container, entry, rows)) return null;
            foreach (var pack in _catalog.Overlays.Where(pack => pack.Enabled))
            {
                if (string.IsNullOrWhiteSpace(pack.DataPath) || !SourceDirectoryExists(pack.DataPath)) return null;
                if (pack.Mode == ContentOverlayModes.ReplaceFile)
                {
                    if (SourceFileExists(Path.Combine(pack.DataPath, file))) return null;
                }
                else if (pack.Mode == ContentOverlayModes.MergeCatalog)
                {
                    if (EnumerateSourceFiles(pack.DataPath, "*.xml", SearchOption.TopDirectoryOnly).Any(path =>
                        string.Equals(ResolveCatalogTargetFileName(Path.GetFileName(path)), file,
                            StringComparison.OrdinalIgnoreCase))) return null;
                }
                else return null;
            }
            // Validate the raw base as well: the ordinary merger can collapse
            // duplicate IDs before enumeration. Do not let that erase ambiguity.
            string basePath = Path.Combine(_catalog.BaseDataPath, file);
            if (!TryLoadXml(basePath, out var raw) || raw?.Root is not { } rawRoot
                || !ValidPurchaseCatalogue(rawRoot, scope, container, entry, rows)) return null;
            return new(accounting, rows, _settingsProfileId, _rawProfileInputsDigest, _enabledSourcebooks, root);
        }

        private static bool ValidPurchaseCatalogue(XElement root, string scope, string container, string entry, XElement[] rows)
        {
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            if (root.Name != "chummer" || HasDirectText(root)
                || root.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration
                    && !(attribute.Name == xsi + "schemaLocation"
                        && attribute.Value == "http://www.w3.org/2001/XMLSchema " + scope + ".xsd"))
                || root.Elements().Any(node => node.Name != container && node.Name != "categories"
                    && !(scope == "qualities" && node.Name == "xpathqueries"))
                || root.Elements(container).ToArray() is not [var records]
                || records.HasAttributes || HasDirectText(records)
                || records.Elements().Any(node => node.Name != entry)
                || records.Elements().Count() != rows.Length) return false;
            var ids = new HashSet<Guid>();
            foreach (var row in records.Elements())
            {
                if (row.HasAttributes || HasDirectText(row) || row.Elements("id").ToArray() is not [var id]
                    || !Scalar(id) || !Guid.TryParse(id.Value.Trim(), out Guid parsed) || parsed == Guid.Empty
                    || !ids.Add(parsed)) return false;
            }
            // Compare original and effective enumeration, not only their counts.
            if (!ids.SetEquals(rows.Select(row => Guid.TryParse(ReadValue(row, "id"), out var id) ? id : Guid.Empty)))
                return false;
            var categories = root.Elements("categories").ToArray();
            if (categories.Length > 1) return false;
            foreach (var catalogue in categories)
            {
                if (catalogue.HasAttributes || HasDirectText(catalogue)) return false;
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var category in catalogue.Elements())
                {
                    // These are labels/optional discount-selection groups, never
                    // implicit global effects. The fixed-price purchase API has no
                    // discount mode; selected discount effects fail its compiler.
                    if (category.Name != "category" || category.HasElements
                        || string.IsNullOrWhiteSpace(category.Value) || !names.Add(category.Value.Trim())
                        || category.Attributes().Any(attribute => scope != "gear" || attribute.Name != "blackmarket"
                            || attribute.Value.Split(',').Any(value => value.Length == 0
                                || value.Any(character => !char.IsAsciiLetter(character))))) return false;
                }
            }
            var queries = root.Elements("xpathqueries").ToArray();
            if (queries.Length > 1) return false;
            foreach (var catalogue in queries)
            {
                if (catalogue.HasAttributes || HasDirectText(catalogue)) return false;
                var queryIds = new HashSet<Guid>();
                foreach (var query in catalogue.Elements())
                {
                    // Schema-defined UI search presets: inert until explicitly
                    // selected by a search UI, not source-selection predicates.
                    if (query.Name != "query" || query.HasAttributes || HasDirectText(query)
                        || query.Elements().Select(node => node.Name.LocalName).Order(StringComparer.Ordinal).SequenceEqual(
                            new[] { "display", "id", "xpath" }) == false
                        || query.Elements().Any(node => node.Name.Namespace != XNamespace.None || !Scalar(node))
                        || !Guid.TryParse(ReadValue(query, "id"), out Guid id) || id == Guid.Empty
                        || !queryIds.Add(id)) return false;
                    try { XPathExpression.Compile(ReadValue(query, "xpath")); }
                    catch (XPathException) { return false; }
                }
            }
            return true;
        }

        private static bool Scalar(XElement node) => !node.HasAttributes && !node.HasElements
            && !string.IsNullOrWhiteSpace(node.Value);
        private static bool HasDirectText(XElement node) => node.Nodes().OfType<XText>()
            .Any(text => !string.IsNullOrWhiteSpace(text.Value));
    }
}
