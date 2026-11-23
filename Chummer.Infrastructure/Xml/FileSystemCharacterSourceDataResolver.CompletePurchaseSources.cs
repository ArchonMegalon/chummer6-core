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
        partial void ValidateCurrentPurchasesCatalog(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleCatalogInput input, CancellationToken cancellationToken,
            ref CharacterCurrentRuleCatalogValidation? result)
        {
            using var scope = resolver.CreateOperationScope();
            if (scope.TryCreateContext(current.CharacterXml) is SourceDataContext source
                && source.MatchesRetainedSelection(inputs))
                result = CharacterCurrentRuleEnvironmentConsumers.ValidatePurchasesCatalog(current, input, source, cancellationToken);
        }

        partial void ExecuteCurrentPurchases(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleCatalogValidation validation, CancellationToken cancellationToken,
            ref CharacterCurrentRuleDomainExecution? result)
        {
            using var scope = resolver.CreateOperationScope();
            if (scope.TryCreateContext(current.CharacterXml) is SourceDataContext source
                && source.MatchesRetainedSelection(inputs))
                result = CharacterCurrentRuleEnvironmentConsumers.ExecutePurchases(current, validation, source, cancellationToken);
        }

        // Multiple anatomical quality projections can occur in one read. Their
        // exact option sets have separate observations; a cache hit reuses only
        // an observation of the same immutable canonical projection.
        private readonly Dictionary<string, PurchaseCatalogueTrace> purchaseCatalogues = new(StringComparer.Ordinal);
        internal bool CapturePurchases { get; private set; }
        internal bool CaptureCurrentGear { get; private set; }
        internal bool CurrentSourceSemantics { get; private set; }
        internal bool DeferredRecursiveAncestors3000 => CurrentSourceSemantics
            && inputs.CompatibilityProfile == Chummer5aLegacyImportCompatibility.ProfileV2;
        private bool currentInterpretationLocked;

        public override bool EnableCurrentEnvironmentSourceSemantics(string profile, string fingerprint)
        {
            // DefinitionV1 explicitly excludes missing recursive parents. Never reinterpret it.
            if (inputs.CompatibilityProfile == Chummer5aLegacyImportCompatibility.ProfileV2
                && profile == CurrentCharacterRuleSourceSemantics.ProfileV1) return false;
            if (CapturePurchases || currentInterpretationLocked || files.Count != 0 || purchaseCatalogues.Count != 0
                || !((profile == CurrentCharacterRuleSourceSemantics.ProfileV1
                        && fingerprint == CurrentCharacterRuleSourceSemantics.FingerprintV1)
                    || CurrentCharacterRuleSourceSemantics.IsCurrentExecutionProfile(profile, fingerprint))) return false;
            currentEnvironmentProfile = profile;
            currentEnvironmentFingerprint = fingerprint;
            CurrentSourceSemantics = true;
            return true;
        }

        public override bool EnableCurrentGearSourceExecution()
        {
            // Never promote an already observed historical trace.
            if (CapturePurchases || !EnableCurrentEnvironmentSourceSemantics(
                    CurrentCharacterRuleSourceSemantics.ProfileV1, CurrentCharacterRuleSourceSemantics.FingerprintV1))
                return false;
            CaptureCurrentGear = true;
            return true;
        }

        internal void ChargeCurrentGearObservation() => Charge(1);

        public override IReadOnlyDictionary<string, string> ConsumeCurrentGearSource(
            CharacterCreationGearAuthority authority, IReadOnlyList<CharacterCreationGearLine> lines,
            CharacterCreationGearBudget budget)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!CaptureCurrentGear || !CharacterCreationGearRules.IsValidAuthority(authority)
                || !budget.IsExact || budget.Blockers.Count != 0
                || !purchaseCatalogues.TryGetValue("gear:" + authority.AuthorityDigest, out var trace)
                || trace.CurrentGearAmendments is not { } amendments
                || !trace.MatchesGear(authority, lines, [], out var decisions)
                || !amendments.MatchesExecution(authority, lines)) return result;

            // Actually execute the canonical kernel. Supplied prices, line
            // digests and a self-consistent budget are comparison inputs only.
            if (!CharacterCreationGearRules.TryProjectBasket(lines.Select(line =>
                    new CharacterCreationGearSelection(line.OptionId, line.Quantity)).ToArray(),
                    authority, budget.TotalStartingNuyen, out var executed, out var executedBudget, out _)
                || CharacterCreationFinalizationDigest.Compute(new { Lines = executed, Budget = executedBudget })
                    != CharacterCreationFinalizationDigest.Compute(new
                    { Lines = lines.OrderBy(line => line.OptionId, StringComparer.Ordinal).ToArray(), Budget = budget }))
                return result;
            using var scope = resolver.CreateOperationScope();
            if (scope.TryCreateContext(inputs.CharacterXml) is not SourceDataContext context
                || !context.MatchesRetainedSelection(inputs)
                || !context.TryResolveCreationEffectivePolicy(out var policy) || policy is null
                || policy.PolicyDigest != inputs.PolicyDigest || !trace.MatchesPolicy(policy)) return result;
            var sources = policy.Sources.Where(source => source.Kind == CharacterCreationPolicySourceKind.Rules
                && source.SourceId == "gear").ToArray();
            if (sources is not [var source] || source.ContentDigest is null) return result;
            result.Add("source:" + source.Kind + ":" + source.Order, CharacterCreationFinalizationDigest.Compute(new
            {
                Schema = "chummer.private-current-gear-cost-amendment-execution.v1",
                CaptureDigest = captureDigest, Source = source, policy.PolicyDigest,
                InterpretationProfile = CurrentCharacterRuleSourceSemantics.ProfileV1,
                InterpretationFingerprint = CurrentCharacterRuleSourceSemantics.FingerprintV1,
                trace.ProfileDigest, trace.MetadataDigest, Operations = amendments.ExecutionDigest,
                Decisions = decisions, Lines = executed, Budget = executedBudget
            }));
            return result;
        }

        public override bool EnablePurchaseSourceExecution()
        {
            if (CurrentSourceSemantics) return false;
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
                && gearTrace.CurrentGearAmendments is null
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
        internal CurrentGearAmendmentTrace? CurrentGearAmendments { get; }
        private bool valid = true;

        internal PurchaseCatalogueTrace(RuleSourceAccounting accounting, XElement[] sources,
            string profileId, string profileDigest, IEnumerable<string> enabledBooks, XElement root,
            CurrentGearAmendmentTrace? currentGearAmendments = null)
        {
            this.accounting = accounting;
            this.profileId = profileId;
            ProfileDigest = profileDigest;
            books = enabledBooks.Order(StringComparer.Ordinal).ToArray();
            MetadataDigest = CharacterCreationFinalizationDigest.Compute(root.ToString(SaveOptions.DisableFormatting));
            CurrentGearAmendments = currentGearAmendments;
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
            var retained1253 = lines.FirstOrDefault(line => line.Focus is not null)?.Focus?.Preserved;
            if (!CharacterCreationFocusSelectionRules.TryValidateBasket(lines, out _)) return false;
            foreach (var line in lines.Concat(retained1253?.Lines ?? []))
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
            var purchased = Decisions(lines.Select(row => row.OptionId));
            var granted = grantedGear.Select(grant => new
            {
                grant.SourceId, grant.EffectiveSourceDigest, grant.SourceNodeDigest,
                grant.CanonicalSourceXmlDigest, grant.SourceBook, grant.SourceAnchorIds
            }).ToArray();
            // An absent retained Focus selection keeps the historical v2 envelope.
            // A serialized null property would change its canonical execution digest.
            decisions = retained1253 is null
                ? new { Purchased = purchased, Granted = granted }
                : new
                {
                    Purchased = purchased,
                    Retained1253 = new
                    {
                        retained1253.Selection, retained1253.TotalCost,
                        Decisions = Decisions(retained1253.Lines.Select(row => row.OptionId))
                    },
                    Granted = granted
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

    // This deliberately narrow observer is evidence for fixed-price gear only.
    // It records the existing merger; it neither merges XML nor authorizes a
    // current environment. All other operations keep their existing refusal.
    private sealed class CurrentGearAmendmentTrace
    {
        private sealed record Input(int Order, string Digest, Guid Id, XElement Entry);
        private sealed record Operation(int InvocationOrder, int SourceOrder, string SourceDigest, Guid Id,
            string Before, string After, string BeforeCost, string AfterCost);
        private readonly RuleSourceAccounting accounting;
        private readonly Input[] inputs;
        private readonly Dictionary<XElement, Operation> operations = new(ReferenceEqualityComparer.Instance);
        private readonly List<Operation> observedOperations = [];
        private bool valid = true;

        private CurrentGearAmendmentTrace(RuleSourceAccounting accounting, Input[] inputs)
        { this.accounting = accounting; this.inputs = inputs; }

        internal static CurrentGearAmendmentTrace? TryCreate(RuleSourceAccounting accounting,
            IReadOnlyList<(string Path, string Prefix, XElement Root)> sources)
        {
            if (!accounting.CaptureCurrentGear || sources.Count == 0) return null;
            var inputs = new List<Input>();
            foreach (var (path, prefix, root) in sources)
            {
                if (prefix != "amend_" || root.Name != "chummer" || root.HasAttributes || HasDirectText(root)
                    || root.DescendantNodes().OfType<XProcessingInstruction>().Any()
                    || root.Elements().ToArray() is not [var gears] || gears.Name != "gears"
                    || gears.HasAttributes || HasDirectText(gears) || !gears.HasElements) return null;
                string digest = RetainedCharacterRuleSourceInputs.Digest(ReadSourceBytes(path));
                var ids = new HashSet<Guid>();
                foreach (var entry in gears.Elements())
                {
                    accounting.ChargeCurrentGearObservation();
                    if (entry.Name != "gear" || HasDirectText(entry)
                        || entry.Attributes().Any(attribute => attribute.Name != "amendoperation"
                            || attribute.Value.ToUpperInvariant() != "RECURSE")
                        || entry.Elements().Count() != 2
                        || entry.Elements("id").ToArray() is not [var id] || !PlainScalar(id)
                        || !Guid.TryParseExact(id.Value, "D", out var identity) || identity == Guid.Empty
                        || !ids.Add(identity)
                        || entry.Elements("cost").ToArray() is not [var cost] || cost.HasElements
                        || cost.Nodes().Any(node => node is not XText || node is XCData)
                        || cost.Attributes().ToArray() is not [var operation]
                        || operation.Name != "amendoperation" || operation.Value.ToUpperInvariant() != "REPLACE")
                        return null;
                    inputs.Add(new(inputs.Count, digest, identity, entry));
                }
            }
            return inputs.Count == 0 ? null : new(accounting, inputs.ToArray());
        }

        internal IDisposable Observe(XElement entry, XElement? target)
        {
            accounting.ChargeCurrentGearObservation();
            var input = inputs.SingleOrDefault(candidate => ReferenceEquals(candidate.Entry, entry));
            if (input is null || target is null || target.Elements("id").ToArray() is not [var id]
                || !PlainScalar(id) || id.Value != entry.Element("id")!.Value
                || target.Elements("cost").ToArray() is not [var cost] || !PlainScalar(cost))
            {
                valid = false;
                return new Completion(() => { });
            }
            string before = NodeDigest(target), beforeCost = cost.Value;
            return new Completion(() =>
            {
                accounting.ChargeCurrentGearObservation();
                if (target.Elements("cost").ToArray() is not [var afterCost] || !PlainScalar(afterCost)
                    || afterCost.Value != entry.Element("cost")!.Value
                    || target.Elements("id").ToArray() is not [var afterId]
                    || !PlainScalar(afterId) || afterId.Value != id.Value)
                { valid = false; return; }
                var observed = new Operation(observedOperations.Count, input.Order, input.Digest, input.Id,
                    before, NodeDigest(target), beforeCost, afterCost.Value);
                // Enumeration can revisit a GUID through ID and name locators.
                // Preserve every actual invocation, including an idempotent
                // replacement; do not invent a single desktop merge pass.
                observedOperations.Add(observed);
                operations[entry] = observed;
            });
        }

        internal bool MatchesTargets(IReadOnlyList<XElement> targets)
        {
            if (!valid || operations.Count != inputs.Length) return false;
            foreach (var group in inputs.GroupBy(input => input.Id))
            {
                var matches = targets.Where(target => Guid.TryParse(ReadValue(target, "id"), out var id)
                    && id == group.Key).Take(2).ToArray();
                if (matches is not [var target] || operations[group.Last().Entry].After != NodeDigest(target))
                    return false;
            }
            return true;
        }

        internal bool MatchesExecution(CharacterCreationGearAuthority authority,
            IReadOnlyList<CharacterCreationGearLine> lines)
        {
            if (!valid || operations.Count != inputs.Length) return false;
            foreach (var group in inputs.GroupBy(input => input.Id))
            {
                var selected = lines.Where(line => line.SourceId == group.Key).Take(2).ToArray();
                var options = authority.Options.Where(option => option.SourceId == group.Key).Take(2).ToArray();
                if (selected is not [var line] || line.Focus is not null
                    || options is not [var option] || !option.IsSelectable || option.FocusSource is not null
                    || option.Category == "Foci" || !option.PricingIsExact
                    || line.SourceNodeXml != option.SourceNodeXml
                    || operations[group.Last().Entry].After != NodeDigest(XElement.Parse(option.SourceNodeXml)))
                    return false;
            }
            return true;
        }

        internal string ExecutionDigest => CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.private-current-gear-cost-amendment-operations.v1",
            Operations = observedOperations.ToArray()
        });

        private static bool PlainScalar(XElement node) => !node.HasAttributes && !node.HasElements
            && node.Nodes().All(child => child is XText && child is not XCData);
        private static bool HasDirectText(XElement node) => node.Nodes().OfType<XText>()
            .Any(text => !string.IsNullOrWhiteSpace(text.Value));
        private static string NodeDigest(XElement node)
            => CharacterCreationFinalizationDigest.Compute(node.ToString(SaveOptions.DisableFormatting));
        private sealed class Completion(Action complete) : IDisposable
        { public void Dispose() => complete(); }
    }

    private sealed partial class SourceDataContext
    {
        private CurrentGearAmendmentTrace? _currentGearAmendmentTrace;

        private PurchaseCatalogueTrace? BeginPurchaseCatalogue(string scope, string container, string entry, XElement[] rows)
        {
            var accounting = _sourceInputs.Accounting;
            if (accounting is null || !accounting.CapturePurchases && !accounting.CaptureCurrentGear
                || _sourceInputs.HasSourceDrift) return null;
            string file = scope + ".xml";
            // Arbitrary custom/override/amend operations require their own source
            // semantics. Neither an identical digest nor an enabled book proves them.
            if (!TryHasSelectedCustomDataInputFor(_customDirectories, file, out bool custom)
                || custom && (scope != "gear" || !accounting.CaptureCurrentGear
                    || _currentGearAmendmentTrace is null || !_currentGearAmendmentTrace.MatchesTargets(rows))
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
            return new(accounting, rows, _settingsProfileId, _rawProfileInputsDigest, _enabledSourcebooks, root,
                custom ? _currentGearAmendmentTrace : null);
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
