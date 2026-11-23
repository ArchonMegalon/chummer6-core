using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class RuleSourceAccounting
    {
        partial void ValidateCurrentCreationCatalog(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleCatalogInput input, CancellationToken cancellationToken,
            ref CharacterCurrentRuleCatalogValidation? result)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var scope = resolver.CreateOperationScope();
            if (scope.TryCreateContext(current.CharacterXml) is not SourceDataContext source
                || !source.MatchesRetainedSelection(inputs)
                || !source.CurrentCreationCatalogMatches1949(input)) return;
            result = CharacterCurrentRuleEnvironmentConsumers.ValidateCreationCatalog(current, input, source, cancellationToken);
            if (!source.MatchesRetainedSelection(inputs)) result = null;
        }

        partial void ExecuteCurrentCreation(CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleCatalogValidation validation, CancellationToken cancellationToken,
            ref CharacterCurrentRuleDomainExecution? result)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var scope = resolver.CreateOperationScope();
            if (scope.TryCreateContext(current.CharacterXml) is not SourceDataContext source
                || !source.MatchesRetainedSelection(inputs)
                || !source.CurrentCreationCatalogMatches1949(validation.Input)) return;
            result = CharacterCurrentRuleEnvironmentConsumers.ExecuteCreation(current, validation, source, cancellationToken);
            if (!source.MatchesRetainedSelection(inputs)) result = null;
        }
    }

    private sealed partial class SourceDataContext
    {
        // Called only by the shared resolver's v2 branch. No historical
        // prerequisite/finalization authority is upgraded by these projections.
        private CharacterCreationMetatypeCatalogAuthority ProjectCurrentCreationMetatypes1950(
            XDocument document, CharacterCreationMetatypeSourceContextAuthority sourceContext)
        {
            var catalog = CharacterCreationMetatypeCatalogProjector.ProjectCurrent(document, sourceContext,
                _sourceInputs.CreationSourceExecutionSchema, careerCondition5497: UsesCareerCondition5497,
                importedSprite5732: UsesImportedCareerOwnerSemantics5698);
            if (!catalog.IsAuthoritative) return catalog;
            XElement[] sources = [];
            bool hasSources = TryComputeEffectiveInputDigest(_catalog, "qualities.xml", out string digest)
                && TryResolveRacialQualitySources(document, out sources);
            // Keep unavailable rows, and bind every enabled declaration to the
            // actual retained quality definitions before any future query.
            return catalog with { Options = catalog.Options.Select(option =>
            {
                if (!option.IsEnabled || option.GrantedQualities.Count == 0) return option;
                if (hasSources && TryCurrentRacialSources1950(option, sources!, digest,
                    sourceContext.EnabledSourcebooks, out _,
                    nativeSkillCosts: _buildMethod is CharacterCreationBuildMethods.Karma or CharacterCreationBuildMethods.LifeModules,
                    importedOwnerEffects: UsesImportedCareerOwnerSemantics5698)) return option;
                return option with { IsEnabled = false,
                    Blockers = ["current-metatype-racial-effects-unavailable"] };
            }).ToArray() };
        }

        private static bool TryCurrentRacialSources1950(CharacterCreationMetatypeOptionProjection metatype,
            XElement[] qualities, string qualityDigest, IReadOnlyList<string> books,
            out CharacterCreationTalentQualitySource[] grants, bool nativeSkillCosts = false, bool importedOwnerEffects = false)
        {
            grants = [];
            if (!metatype.IsEnabled || metatype.Blockers.Count != 0) return false;
            if (metatype.GrantedQualities.Count == 0) return true;
            var findings = new List<string>();
            grants = CharacterCreationMagicResonanceAuthorityProjector.ResolveQualityReferences(
                metatype.GrantedQualities.Select(item => (Reference: item.Name, Selection: string.Empty)).ToArray(),
                qualities, [], qualityDigest, string.Empty, books, findings);
            return findings.Count == 0 && grants.Length == metatype.GrantedQualities.Count
                && !grants.Where((source, index) => !string.Equals(
                    XElement.Parse(source.CanonicalSourceXml).Element("category")?.Value,
                    metatype.GrantedQualities[index].Polarity, StringComparison.OrdinalIgnoreCase)).Any()
                // V9 catalogs bind retained declarations, not synthetic Creation
                // owners. Its current consumers must reconcile every actual saved
                // owner/effect through the sheet compiler before issuing execution.
                && (importedOwnerEffects || (nativeSkillCosts
                    ? CharacterCreationPriorityRacialQualities.TryProjectCurrentNativeSkillCosts(grants, qualityDigest, out _, out _)
                    : CharacterCreationPriorityRacialQualities.TryProjectCurrent(grants, qualityDigest, out _, out _)));
        }

        private CharacterCreationPrerequisiteAuthority BindCurrentCreationRacialOptions1950(
            CharacterCreationPrerequisiteAuthority original, XDocument metatypes, XElement[] qualities,
            string qualityDigest, IReadOnlyList<string> books, string? sourceExecutionSchema)
        {
            if (!original.IsAuthoritative || original.Blockers.Count != 0
                || !TryEnumerateTargets("priorities.xml", ["priorities"], "priority", out var priorityRows)) return original;
            var declarations = new Dictionary<string, (CharacterCreationMetatypeOptionProjection Metatype,
                CharacterCreationTalentQualitySource[] Grants)>(StringComparer.Ordinal);
            foreach (var row in metatypes.Root?.Element("metatypes")?.Elements("metatype") ?? [])
                if (CharacterCreationMetatypeCatalogProjector.TryProjectCurrentOption(row, 1, 1, books,
                        out var metatype, sourceExecutionSchema,
                        importedSprite5732: UsesImportedCareerOwnerSemantics5698)
                    && TryCurrentRacialSources1950(metatype, qualities, qualityDigest, books, out var grants,
                        importedOwnerEffects: UsesImportedCareerOwnerSemantics5698))
                    if (!declarations.TryAdd(metatype.OptionId, (metatype, grants)))
                        return original with { IsAuthoritative = false,
                            Blockers = [CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported] };
            return original with { Options = original.Options.Select(priority => priority with
            {
                HeritageOptions = priority.HeritageOptions.Select(option =>
                {
                    var rows = priorityRows.Where(row => row.Element("id")?.Value == priority.SourceId).ToArray();
                    var children = rows.Length == 1 ? rows[0].Element("metatypes")?.Elements("metatype")
                        .Where(child => child.Element("name")?.Value == option.MetatypeName).ToArray() ?? [] : [];
                    bool exactChild = children is [var child] && !child.HasAttributes
                        && !child.Elements().Any(field => field.Name != "name" && field.Name != "value"
                            && field.Name != "karma" && field.Name != "metavariants")
                        && !child.Elements().GroupBy(field => field.Name).Any(group => group.Count() != 1);
                    if (option.MetavariantSourceId is not null || option.MetavariantName is not null
                        || option.Kind != CharacterCreationPriorityChildKinds.Metatype || !exactChild
                        || option.KarmaCost < 0 || option.HalvesNormalAttributePoints
                        || !declarations.TryGetValue(option.MetatypeSourceId, out var declaration)
                        || option.MetatypeName != declaration.Metatype.Label
                        || !option.Attributes.SequenceEqual(declaration.Metatype.Attributes)
                        || option.Movement != declaration.Metatype.Movement
                        || option.Blockers.Any(reason => reason != CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported))
                        return option with { IsEnabled = false,
                            Blockers = option.Blockers.Append(CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported)
                                .Distinct(StringComparer.Ordinal).ToArray() };
                    return option with { IsEnabled = true, Blockers = [],
                        RacialQualitySources = declaration.Grants.Length == 0 ? null : declaration.Grants,
                        BaseBonuses = declaration.Metatype.BaseBonuses };
                }).ToArray()
            }).ToArray(), AuthorityDigest = string.Empty };
        }

        // Guard exact future raw lookups in the shared v2 resolver. Enumeration
        // stays complete for merger/catalog proof. Projection uses enumeration,
        // so this guard does not recurse through an exact lookup.
        private bool CurrentCreationTargetAvailable1950(string fileName, string entryName, XElement target)
        {
            if (fileName == "metatypes.xml")
                return entryName == "metatype" && target.Name == "metatype"
                    && TryResolveCreationMetatypeCatalog(out var catalog) && catalog.IsAuthoritative
                    && catalog.Options.Any(option => option.OptionId == target.Element("id")?.Value
                        && option.Label == target.Element("name")?.Value && option.IsEnabled && option.Blockers.Count == 0);
            if (fileName == "priorities.xml")
                return entryName == "priority" && target.Name == "priority"
                    && !target.HasAttributes && !target.Elements().Any(field => field.Name.Namespace != XNamespace.None
                        || field.Name.LocalName is not ("id" or "name" or "value" or "category" or "prioritytable"
                            or "attributes" or "skills" or "skillgroups" or "resources" or "metatypes" or "talents"))
                    && TryResolveCreationPrerequisiteAuthority(out var authority) && authority.IsAuthoritative
                    && authority.Options.Any(option => option.SourceId == target.Element("id")?.Value);
            return true;
        }

        private bool ReconcileCurrentCreationMetatypeCost1950(decimal savedCost, out decimal currentCost)
        {
            currentCost = savedCost;
            string id = _character.Element("metatypeguid")?.Value ?? string.Empty;
            string name = _character.Element("metatype")?.Value ?? string.Empty;
            if (id.Length == 0 && name.Length == 0) return savedCost == 0m;
            if (!TryResolveCreationMetatypeCatalog(out var catalog) || !catalog.IsAuthoritative
                || CharacterCreationMetatypeSelectionRules.ResolveSaved(catalog, _character,
                    _buildMethod == CharacterCreationBuildMethods.LifeModules) is not { } metatype) return false;
            decimal expected = metatype.BaseBonuses?.LifestyleCostPercent ?? 0;
            bool savedRacialCost = _character.Elements("improvements").Elements("improvement").Any(effect =>
                effect.Element("improvementsource")?.Value is "Metatype" or "Metavariant" or "Heritage"
                && effect.Element("improvementttype")?.Value is "LifestyleCost" or "BasicLifestyleCost");
            if ((savedRacialCost || savedCost != 0m) && savedCost != expected) return false;
            currentCost = expected;
            return true;
        }

        // Check the actual retained merged tree, including supporting containers.
        // Matching a primary row alone must not hide an unknown global sibling.
        internal bool CurrentCreationCatalogMatches1949(CharacterCurrentRuleCatalogInput input)
        {
            string? file = input.Domain switch
            {
                CharacterCurrentRuleDomain.Profile => SettingsFile,
                CharacterCurrentRuleDomain.Metatypes => UsesSpriteCatalog5751 ? "critters.xml" : "metatypes.xml",
                CharacterCurrentRuleDomain.Priorities => "priorities.xml",
                CharacterCurrentRuleDomain.Lifestyles => "lifestyles.xml",
                _ => null
            };
            if (file is null || input.Query != file || input.Blockers.Count != 0) return false;
            using var read = _sourceInputs.Enter();
            if (_sourceInputs.HasSourceDrift || !TryLoadEffectiveDocument(_catalog, file, out var document)
                || document?.Root is not { } root) return false;
            try
            {
                string container = input.Domain == CharacterCurrentRuleDomain.Profile ? "settings"
                    : input.Domain.ToString().ToLowerInvariant();
                string rowName = input.Domain switch
                { CharacterCurrentRuleDomain.Profile => "setting", CharacterCurrentRuleDomain.Metatypes => "metatype",
                    CharacterCurrentRuleDomain.Priorities => "priority", _ => "lifestyle" };
                if (root.Elements(container).ToArray() is not [var primary] || primary.HasAttributes
                    || HasDirectScopeText(primary) || primary.Elements().Any(row => row.Name != rowName)) return false;
                if (!TryEnumerateTargets(file, [container], rowName, out var effectiveRows)) return false;
                var headerSource = root;
                if (input.Domain == CharacterCurrentRuleDomain.Lifestyles
                    && _sourceInputs.Accounting?.HasLifestyleMechanics2684 == true)
                {
                    // Use the same effective V7 supporting header as the catalog
                    // consumer, including source-bound quality amendments.
                    if (!TryReadLifestyleSource(out string sourceXml, out _)) return false;
                    headerSource = XElement.Parse(sourceXml, LoadOptions.PreserveWhitespace);
                }
                // Match the catalog's detached semantic header without changing retained XML.
                var global = CreateCurrentCatalogHeader6247(headerSource, [container]);
                var covered = new HashSet<XElement>();
                // These detached rows belong only to this match. Normalize each
                // on first comparison, preserving row order and duplicate matches.
                // Do not retain a tree or an admission result across calls.
                var comparisonRows = new XElement?[effectiveRows.Length];
                bool globalSeen = false;
                foreach (var entry in input.Entries)
                {
                    if (entry.State == CharacterCurrentRuleEntryState.Tombstone)
                    {
                        // Deletions are merger observations, not live options.
                        if (!input.Operations.Any(op => op.IsTombstone && op.BeforeXml == entry.Xml)) return false;
                        continue;
                    }
                    if (entry.State != CharacterCurrentRuleEntryState.AvailableNotSelected) return false;
                    var candidate = XElement.Parse(entry.Xml, LoadOptions.PreserveWhitespace);
                    if (entry.Key == "$global")
                    {
                        if (globalSeen || !XNode.DeepEquals(CurrentCreationTree1949(global), CurrentCreationTree1949(candidate))) return false;
                        globalSeen = true;
                        continue;
                    }
                    XElement? comparisonCandidate = null;
                    var matches = effectiveRows.Where((node, index) =>
                        XNode.DeepEquals(comparisonRows[index] ??= CurrentCreationTree1949(node),
                            comparisonCandidate ??= CurrentCreationTree1949(candidate))).ToArray();
                    if (matches.Length != 1 || !covered.Add(matches[0])) return false;
                }
                return globalSeen && covered.Count == effectiveRows.Length && !_sourceInputs.HasSourceDrift;
            }
            catch (Exception error) when (error is System.Xml.XmlException or InvalidOperationException or ArgumentException)
            { return false; }
        }

        private static XElement CurrentCreationTree1949(XElement node)
        {
            var copy = new XElement(node);
            copy.DescendantNodes().OfType<XText>().Where(text => text.GetType() == typeof(XText)
                && string.IsNullOrWhiteSpace(text.Value)).Remove();
            return copy;
        }

        private bool TryExecuteCreationSource718(CharacterCreationPolicySource source, string consumer,
            CharacterCreationRuleSourceScopeInvocation invocation, out CharacterCreationRuleSourceScopeResult? report)
        {
            report = null;
            if (source.SourceId is not ("profile" or "metatypes" or "priorities" or "lifestyles")) return false;
            using var read = _sourceInputs.Enter();
            string file = source.SourceId == "profile" ? SettingsFile : source.SourceId + ".xml";
            if (_sourceInputs.HasSourceDrift || !TryComputeEffectiveInputDigest(_catalog, file, out var raw)
                || (source.SourceId == "profile" ? BindSelectedProfile(raw, _settingsProfileId) : raw) != source.ContentDigest
                || !TryLoadEffectiveDocument(_catalog, file, out var document) || document?.Root is not { } root)
                return false;
            var ledger = new SourceScopeLedger(root, invocation, consumer, $"source:{source.Kind}:{source.Order}");
            report = new($"source:{source.Kind}:{source.Order}", source.SourceId, source.ContentDigest!, consumer, ledger.Operations);
            // This adapter cannot certify custom/amend operations from their
            // effective XML. 684's operation accounting remains independent.
            if (!TryHasSelectedCustomDataInputFor(_customDirectories, file, out bool custom) || custom
                || !NoOverlayForSource(file) || !CreationEnvelope718(root, source.SourceId)) return true;

            if (source.SourceId == "lifestyles")
            {
                ExecuteLifestyle718(root, invocation, ledger);
                return true;
            }
            if (invocation.CreationDraft718 is not { } draft || invocation.Attributes718 is null || invocation.Skills718 is null
                || !TryResolveCreationPrerequisiteAuthority(out var authority)
                || !CharacterCreationPrerequisiteService.TryExecuteSourceSelection718(authority, draft, out var executed))
                return true;
            if (invocation.PhysicalAuthority886 is { } physical
                && (!TryResolveCreationPhysicalInitiative886(executed!, out var freshPhysical)
                    || freshPhysical != physical)) return true;
            // ResolveAssignments, rank validation, heritage/talent grant choice,
            // point pools and BuildBudget have actually run. Merely resolving
            // authority cannot reach the following accounting.
            if (source.SourceId == "priorities") ExecutePriorities718(root, authority, executed!, invocation, ledger);
            else if (source.SourceId == "metatypes") ExecuteMetatype718(root, executed!, invocation, ledger);
            else ExecuteProfile718(root, authority, executed!, invocation, ledger, source);
            if (_sourceInputs.HasSourceDrift) ledger.UnconsumeRoot();
            return true;
        }

        private static bool CreationEnvelope718(XElement root, string scope)
        {
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            string schema = scope == "profile" ? "settings" : scope;
            if (root.Name != "chummer" || HasDirectScopeText(root)
                || root.DescendantNodes().OfType<XProcessingInstruction>().Any()
                || root.Attributes().Any(a => !a.IsNamespaceDeclaration
                    && !(a.Name == xsi + "schemaLocation"
                        && a.Value == "http://www.w3.org/2001/XMLSchema " + schema + ".xsd"))
                || root.Elements().GroupBy(n => n.Name).Any(g => g.Count() != 1)) return false;
            string[] containers = scope switch
            {
                "profile" => ["settings"],
                "metatypes" => ["categories", "metatypes"],
                "priorities" => ["categories", "prioritytables", "priortysumtotenvalues", "priorities"],
                "lifestyles" => ["categories", "lifestyles", "comforts", "neighborhoods", "securities", "qualities", "cities"],
                _ => []
            };
            string primary = scope == "profile" ? "settings" : scope;
            return root.Element(primary) is not null && root.Elements().All(n =>
                containers.Contains(n.Name.ToString(), StringComparer.Ordinal) && !n.HasAttributes && !HasDirectScopeText(n));
        }

        private static void Scalar718(SourceScopeLedger ledger, XElement parent, string field,
            object? expected, string operation, object result)
        {
            if (expected is null) return;
            var nodes = parent.Elements(field).ToArray();
            string text = Convert.ToString(expected, CultureInfo.InvariantCulture)!;
            if (nodes is not [var node] || node.HasElements || node.HasAttributes) return;
            bool matches = expected switch
            {
                bool boolean => bool.TryParse(node.Value, out bool value) && value == boolean,
                int integer => int.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int value) && value == integer,
                decimal number => decimal.TryParse(node.Value, NumberStyles.Number, CultureInfo.InvariantCulture,
                    out decimal value) && value == number,
                _ => node.Value.Trim() == text
            };
            if (matches) ledger.Tree(node, D.Compiled, new { Operation = operation, Input = node.Value, Result = result });
        }

        private static bool Container718(SourceScopeLedger ledger, XElement node, string operation)
        {
            if (node.HasAttributes || HasDirectScopeText(node)) return false;
            ledger.Node(node, D.Structural, new { Operation = operation });
            return true;
        }

        private static void ExecutePriorities718(XElement root, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationPrerequisiteDraft executed, CharacterCreationRuleSourceScopeInvocation invocation,
            SourceScopeLedger ledger)
        {
            var rows = root.Element("priorities")!.Elements().ToArray();
            if (rows.Any(row => row.Name != "priority")
                || rows.Select(row => row.Element("id")?.Value).Distinct(StringComparer.Ordinal).Count() != rows.Length)
                return;
            var selected = executed.Assignments.Select(row => row.SourceId).ToHashSet(StringComparer.Ordinal);
            if (!selected.All(id => rows.Count(row => row.Element("id")?.Value == id) == 1)) return;
            ledger.Node(root, D.Structural, new { Operation = "creation-priority-source", executed.BuildMethod });
            Container718(ledger, root.Element("priorities")!, "priority-choice-domain");
            foreach (var row in rows)
            {
                if (!TryScopeRowIdentity(row, new("priorities", "priority", true), out string id)) continue;
                if (!selected.Contains(id))
                {
                    ledger.Tree(row, D.NotSelected, new { Operation = "ResolveAssignments", Candidate = id, Selected = selected.Order().ToArray() });
                    continue;
                }
                var option = authority.Options.Single(item => item.SourceId == id);
                var assignment = executed.Assignments.Single(item => item.SourceId == id);
                if (RawSource718(row) != assignment.SourceNodeDigest) continue;
                if (!Container718(ledger, row, "selected-priority")) continue;
                object result = new { Assignment = assignment, executed.EffectiveNormalAttributePoints,
                    executed.TotalSpecialAttributePoints, Skills = invocation.Skills718,
                    Resources = invocation.ResourcesExecution718 };
                Scalar718(ledger, row, "id", option.SourceId, "ResolveAssignments", result);
                Scalar718(ledger, row, "name", option.Label, "ResolveAssignments", result);
                Scalar718(ledger, row, "value", option.Rank, "ValidateSelectedRanks", result);
                Scalar718(ledger, row, "category", option.CategoryName, "ResolveAssignments", result);
                Scalar718(ledger, row, "prioritytable", authority.PriorityTable, "ResolveAssignments", result);
                Scalar718(ledger, row, "attributes", option.BaseNormalAttributePoints, "ResolveAttributePointPools", result);
                Scalar718(ledger, row, "skills", option.BaseActiveSkillPoints, "selected-skill-point-consumer", result);
                Scalar718(ledger, row, "skillgroups", option.BaseSkillGroupPoints, "selected-skill-group-consumer", result);
                if (invocation.ResourcesExecution718 is not null)
                    Scalar718(ledger, row, "resources", option.BaseResourceNuyen, "selected-funding-consumer", result);

                if (row.Element("metatypes") is { } metatypes && Container718(ledger, metatypes, "ResolveHeritageSelection"))
                {
                    var heritage = executed.HeritageSelection!;
                    foreach (var child in metatypes.Elements())
                    {
                        if (child.Name != "metatype" || child.HasAttributes || HasDirectScopeText(child)) continue;
                        if (RawSource718(child) != heritage.PriorityChildNodeDigest)
                        {
                            ledger.Tree(child, D.NotSelected, new { Operation = "ResolveHeritageSelection", heritage.SelectionId, heritage.MetatypeName });
                            continue;
                        }
                        // Unknown siblings stay unconsumed even when the old
                        // catalogue projector did not inspect their names.
                        ledger.Node(child, D.Structural, "selected-priority-heritage");
                        Scalar718(ledger, child, "name", heritage.MetatypeName, "ResolveHeritageSelection", heritage);
                        Scalar718(ledger, child, "value", heritage.SpecialAttributePoints, "ResolveAttributePointPools", result);
                        Scalar718(ledger, child, "karma", heritage.KarmaCost, "BuildBudget", new { executed.CreationKarmaTotal, executed.CreationKarmaUsed });
                        if (child.Element("metavariants") is { } variants && heritage.MetavariantSourceId is null
                            && Container718(ledger, variants, "ResolveHeritageSelection"))
                            foreach (var variant in variants.Elements("metavariant"))
                                ledger.Tree(variant, D.NotSelected, new { Operation = "ResolveHeritageSelection", heritage.SelectionId, heritage.MetavariantSourceId });
                    }
                }
                if (row.Element("talents") is { } talents && Container718(ledger, talents, "ResolveTalentSelection"))
                {
                    var talent = executed.TalentSelection!;
                    int index = 0;
                    foreach (var child in talents.Elements())
                    {
                        string selection = id + ":talent:" + index++;
                        if (child.Name != "talent" || child.HasAttributes || HasDirectScopeText(child)) continue;
                        if (selection != talent.SelectionId)
                            ledger.Tree(child, D.NotSelected, new { Operation = "ResolveTalentSelection", Candidate = selection, Selected = talent.SelectionId });
                        else
                        {
                            // Canonical enabled talents have the exact supported
                            // source grammar, including their conditional/grant
                            // semantics, revalidated by ResolveTalentSelection.
                            var projected = option.TalentOptions.Single(t => t.SelectionId == selection);
                            if (!child.DescendantsAndSelf().Any(n => n.HasElements && HasDirectScopeText(n))
                                && NormalizeSource(child) == NormalizeSource(XElement.Parse(projected.RawTalentNode)))
                                ledger.Tree(child, D.Compiled, new { Operation = "ResolveTalentSelection", Result = talent,
                                    executed.TotalSpecialAttributePoints, Attributes = invocation.Attributes718 });
                        }
                    }
                }
            }
            foreach (var container in root.Elements().Where(n => n.Name != "priorities"))
            {
                if (!Container718(ledger, container, "priority-domain-selection")) continue;
                foreach (var node in container.Elements())
                {
                    if (node.HasElements || node.HasAttributes) continue;
                    if (container.Name == "categories" && node.Name == "category"
                        && authority.Options.Any(o => o.CategoryName == node.Value))
                        ledger.Tree(node, D.SelectorEvaluated, new { Operation = "ResolveAssignments", Category = node.Value,
                            Selected = executed.Assignments });
                    else if (container.Name == "prioritytables" && node.Name == "prioritytable")
                        ledger.Tree(node, D.SelectorEvaluated, new { Operation = "priority-table-selection",
                            Candidate = node.Value, Selected = authority.PriorityTable, Matches = node.Value == authority.PriorityTable });
                    else if (container.Name == "priortysumtotenvalues")
                    {
                        var weights = authority.RankWeights.Where(w => w.Rank == node.Name.ToString()).ToArray();
                        if (weights is [var weight])
                            Scalar718(ledger, container, node.Name.LocalName, weight.Value, "ValidateSelectedRanks",
                                new { executed.BuildMethod, Used = executed.Assignments.Sum(a => a.SumToTenValue), executed.SumToTenTarget });
                    }
                }
            }
        }

        private static string RawSource718(XElement node)
        {
            using var writer = new CharacterCreationPrerequisiteAuthorityProjector.SourceXmlWriter();
            return CharacterCreationFinalizationDigest.ComputeUtf8(writer.Serialize(node));
        }

        private void ExecuteMetatype718(XElement root, CharacterCreationPrerequisiteDraft executed,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            var heritage = executed.HeritageSelection!;
            var rows = root.Element("metatypes")!.Elements().ToArray();
            if (heritage.MetavariantSourceId is not null || rows.Any(row => row.Name != "metatype")
                || rows.Select(row => row.Element("id")?.Value).Distinct().Count() != rows.Length
                || rows.Count(row => row.Element("id")?.Value == heritage.MetatypeSourceId) != 1) return;
            ledger.Node(root, D.Structural, "metatype-creation-domain");
            Container718(ledger, root.Element("metatypes")!, "ResolveHeritageSelection");
            foreach (var row in rows)
            {
                if (!TryScopeRowIdentity(row, new("metatypes", "metatype", true), out string id)) continue;
                if (id != heritage.MetatypeSourceId)
                {
                    ledger.Tree(row, D.NotSelected, new { Operation = "ResolveHeritageSelection", Candidate = id, Selected = heritage.MetatypeSourceId });
                    continue;
                }
                if (!Container718(ledger, row, "selected-metatype")) continue;
                if (RawSource718(row) != heritage.MetatypeSourceNodeDigest) continue;
                Scalar718(ledger, row, "id", heritage.MetatypeSourceId, "ResolveHeritageSelection", heritage);
                Scalar718(ledger, row, "name", heritage.MetatypeName, "ResolveHeritageSelection", heritage);
                if (invocation.SourceExecution832
                    && executed.BuildMethod is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
                {
                    // Only exact scalar syntax is eligible. In particular, the
                    // readers' XElement.Value must not hide comments, CDATA,
                    // descendants, duplicate fields, or namespace collisions.
                    static bool ScalarShape999(XElement node) => !node.HasAttributes
                        && node.Nodes().ToArray() is [XText text] && text.GetType() == typeof(XText);
                    bool FieldShape999(string name) => row.Elements().Where(n => n.Name.LocalName == name).ToArray()
                        is [var field] && field.Name == name && ScalarShape999(field);

                    // ResolveHeritageSelection and BuildBudget have already
                    // executed from the selected priority child. Priority and
                    // Sum-to-Ten do not charge metatypes.xml's base Karma price.
                    // Validate that price without replacing it with a default,
                    // and bind nonapplicability to the actual selected cost.
                    if (FieldShape999("karma") && TryReadRequiredNonNegativeInt(row, "karma", out int baseKarma)
                        && executed.CreationKarmaUsed == heritage.KarmaCost)
                        ledger.Tree(row.Element("karma")!, D.NotSelected, new
                        {
                            Operation = "ResolveHeritageSelection-priority-child-KarmaCost",
                            executed.BuildMethod, BaseKarma = baseKarma, heritage.KarmaCost,
                            heritage.PriorityChildNodeDigest, heritage.MetatypeSourceNodeDigest,
                            executed.CreationKarmaTotal, executed.CreationKarmaUsed
                        });

                    if (FieldShape999("category") && FieldShape999("source") && FieldShape999("page")
                        && CharacterCreationMetatypeCatalogProjector.TryReadSourceReference999(row,
                            out string category, out string sourceBook, out int sourcePage)
                        && TryIsBookEnabled(sourceBook, out bool bookEnabled) && bookEnabled)
                    {
                        var reference = new
                        {
                            heritage.MetatypeSourceId, heritage.MetatypeSourceNodeDigest,
                            Category = category, SourceBook = sourceBook, SourcePage = sourcePage, BookEnabled = bookEnabled
                        };
                        ledger.Tree(row.Element("category")!, D.SelectorEvaluated,
                            new { Operation = "metatype-catalogue-category-admission", Result = reference });
                        ledger.Tree(row.Element("source")!, D.SelectorEvaluated,
                            new { Operation = "TryIsBookEnabled-selected-metatype", Result = reference });
                        ledger.Tree(row.Element("page")!, D.Structural,
                            new { Operation = "metatype-catalogue-source-citation", Result = reference });

                        // The catalogue's category table declares display labels;
                        // selection and mechanics come from the source-bound row.
                        // Account for this exact vocabulary shape, never a tree
                        // containing unknown instructions or a crossed category.
                        if (root.Element("categories") is { } categories
                            && !categories.HasAttributes && !HasDirectScopeText(categories)
                            && categories.Nodes().All(n => n is XElement || n is XText whitespace
                                && whitespace.GetType() == typeof(XText) && string.IsNullOrWhiteSpace(whitespace.Value)))
                        {
                            var labels = categories.Elements().ToArray();
                            if (labels.Length > 0 && labels.All(n => n.Name == "category" && ScalarShape999(n)
                                    && !string.IsNullOrWhiteSpace(n.Value) && n.Value == n.Value.Trim())
                                && labels.Select(n => n.Value).Distinct(StringComparer.Ordinal).Count() == labels.Length
                                && labels.Count(n => n.Value == category) == 1)
                            {
                                ledger.Node(categories, D.Structural,
                                    new { Operation = "metatype-catalogue-category-labels", Result = reference });
                                foreach (var label in labels)
                                    ledger.Tree(label, D.Structural, new
                                    {
                                        Operation = "metatype-catalogue-category-label", Label = label.Value, Result = reference
                                    });
                            }
                        }
                    }
                }
                foreach (var range in heritage.Attributes)
                {
                    var results = invocation.Attributes718!.Attributes.Where(a => a.AttributeId == range.AttributeId).ToArray();
                    if (results is not [var actual]) continue;
                    string prefix = range.AttributeId.ToLowerInvariant();
                    object result = new { Range = range, Allocated = actual };
                    if (!actual.IsEnabled && actual.AttributeId != "ESS")
                    {
                        foreach (string suffix in new[] { "min", "max", "aug" })
                            if (row.Elements(prefix + suffix).ToArray() is [var inactive]
                                && !inactive.HasElements && !inactive.HasAttributes)
                                ledger.Tree(inactive, D.NotSelected, new { Operation = "EvaluateAllocations-disabled-attribute", Result = actual });
                        continue;
                    }
                    bool talentReplacement = range.AttributeId == "MAG" && executed.TalentSelection!.Magic > 0
                        || range.AttributeId == "RES" && executed.TalentSelection!.Resonance > 0;
                    if (talentReplacement)
                    {
                        foreach (string suffix in new[] { "min", "aug" })
                            if (row.Elements(prefix + suffix).ToArray() is [var replaced]
                                && !replaced.HasElements && !replaced.HasAttributes)
                                ledger.Tree(replaced, D.NotSelected, new { Operation = "EvaluateAllocations-talent-replacement",
                                    Talent = executed.TalentSelection, Result = actual });
                        Scalar718(ledger, row, prefix + "max", range.Maximum, "EvaluateAllocations-special-cap", result);
                        continue;
                    }
                    // A talent replacement cannot borrow the racial source's
                    // minimum or augmented cap merely because other fields ran.
                    if (actual.Minimum == range.Minimum)
                        Scalar718(ledger, row, prefix + "min", range.Minimum, "EvaluateAllocations", result);
                    if (actual.Maximum == range.Maximum)
                        Scalar718(ledger, row, prefix + "max", range.Maximum, "EvaluateAllocations", result);
                    if (actual.AugmentedMaximum == range.AugmentedMaximum)
                        Scalar718(ledger, row, prefix + "aug", range.AugmentedMaximum, "EvaluateAllocations", result);
                }
                if (row.Element("metavariants") is { } variants && Container718(ledger, variants, "ResolveHeritageSelection"))
                    foreach (var variant in variants.Elements("metavariant"))
                        ledger.Tree(variant, D.NotSelected, new { Operation = "ResolveHeritageSelection", heritage.MetatypeSourceId,
                            heritage.MetavariantSourceId });
                if (invocation.SourceExecution832) ExecuteMetatypeGraph832(row, heritage, invocation, ledger);
                if (invocation.PhysicalAuthority886 is { } physical && invocation.PhysicalInitiative886 is { } initiative
                    && physical.MetatypeSourceNodeDigest == heritage.MetatypeSourceNodeDigest
                    && initiative.AuthorityDigest == CharacterCreationFinalizationDigest.Compute(physical))
                {
                    foreach (string field in new[] { "inimin", "inimax", "iniaug" })
                        if (row.Elements(field).ToArray() is [var scalar]) ledger.LegacyIni886(row, scalar);
                    Scalar718(ledger, row, "initiativedice", physical.Metatype.DirectDice886,
                        "physical-initiative-persisted-base", initiative);
                }
                // Nonempty racial effects still require their own consumer.
            }
        }

        private void ExecuteProfile718(XElement root, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationPrerequisiteDraft executed, CharacterCreationRuleSourceScopeInvocation invocation,
            SourceScopeLedger ledger, CharacterCreationPolicySource source)
        {
            var rows = root.Element("settings")!.Elements().ToArray();
            if (rows.Any(row => row.Name != "setting")
                || rows.Select(row => row.Element("id")?.Value).Distinct().Count() != rows.Length
                || rows.Count(row => row.Element("id")?.Value == _settingsProfileId) != 1) return;
            ledger.Node(root, D.Structural, "selected-profile-domain");
            Container718(ledger, root.Element("settings")!, "profile-selection");
            foreach (var row in rows)
            {
                if (!TryScopeRowIdentity(row, new("settings", "setting", true), out string identity)) continue;
                if (identity != _settingsProfileId)
                {
                    ledger.Tree(row, D.NotSelected, new { Operation = "profile-selection",
                        Selected = _settingsProfileId, Candidate = identity });
                    continue;
                }
                if (!Container718(ledger, row, "selected-profile")) continue;
                object result = new { executed.BuildMethod, executed.PriorityTable, executed.PriorityArray,
                    executed.CreationKarmaTotal, executed.CreationKarmaUsed, executed.EffectiveNormalAttributePoints,
                    executed.TotalSpecialAttributePoints, Attributes = invocation.Attributes718 };
                Scalar718(ledger, row, "id", executed.SettingsProfileId, "profile-selection", result);
                // Display metadata belongs only to this exact selected profile, not a mechanics result.
                var displayNames = row.Elements().Where(node => node.Name.LocalName == "name").Take(2).ToArray();
                if (displayNames is [var displayName] && displayName.Name == "name" && !displayName.HasAttributes
                    && displayName.Nodes().ToArray() is [var displayText]
                    && displayText.NodeType == System.Xml.XmlNodeType.Text
                    && !string.IsNullOrWhiteSpace(((XText)displayText).Value))
                    ledger.Tree(displayName, D.Structural, new { Operation = "selected-profile-display-name",
                        ProfileId = executed.SettingsProfileId, Value = displayName.Value });
                ExecuteProfileMetadata1202(row, executed.SettingsProfileId, source, ledger);
                ExecuteEssenceProfile1222(row, invocation, ledger);
                ExecuteProfileMetadataSemantics1209(row, authority, invocation, ledger);
                Scalar718(ledger, row, "prioritytable", executed.PriorityTable, "ResolveAssignments", result);
                ResolveCreationPrerequisiteProfileAuthority(row, out string method, out int? karma,
                    out string[] ranks, out string table, out int? target, out string[] profileBlockers);
                if (profileBlockers.Length == 0 && method == executed.BuildMethod && karma == executed.CreationKarmaTotal
                    && table == executed.PriorityTable && target == executed.SumToTenTarget
                    && ranks.SequenceEqual(executed.PriorityArray))
                {
                    // Preserve the canonical reader's case normalization and
                    // explicit-empty-array fallback. Do not duplicate defaults.
                    foreach (string field in new[] { "buildmethod", "priorityarray" })
                        if (row.Elements(field).ToArray() is [var normalized] && !normalized.HasElements && !normalized.HasAttributes)
                            ledger.Tree(normalized, D.Compiled, new { Operation = "ValidateSelectedRanks",
                                Input = normalized.Value, Result = result });
                    if (method == CharacterCreationBuildMethods.SumToTen)
                        Scalar718(ledger, row, "sumtoten", target, "ValidateSelectedRanks", result);
                    else if (row.Elements("sumtoten").ToArray() is [var inactiveTarget]
                        && !inactiveTarget.HasElements && !inactiveTarget.HasAttributes)
                        ledger.Tree(inactiveTarget, D.NotSelected, new { Operation = "ValidateSelectedRanks-Priority-branch",
                            Input = inactiveTarget.Value, Result = result });
                }
                if (invocation.PhysicalAuthority886 is { } physical && invocation.PhysicalInitiative886 is { } initiative
                    && physical.SettingsProfileId == authority.SettingsProfileId
                    && physical.ProfileInputsDigest == authority.RawProfileInputsDigest
                    && initiative.AuthorityDigest == CharacterCreationFinalizationDigest.Compute(physical))
                {
                    Scalar718(ledger, row, "mininitiativedice", physical.Metatype.MinimumDiceFallback,
                        physical.Metatype.DirectDice886 is null ? "physical-initiative-base-fallback" : "physical-initiative-direct-base-overrides-fallback",
                        new { physical.BaseDice, UsedFallback = physical.Metatype.DirectDice886 is null, Result = initiative });
                    Scalar718(ledger, row, "maxinitiativedice", physical.MaximumDice, "physical-initiative-final-dice-ceiling", initiative);
                }
                Scalar718(ledger, row, "buildpoints", executed.CreationKarmaTotal, "BuildBudget", result);
                if (invocation.AttributePolicy718 is { } spending
                    && spending.SettingsProfileId == authority.SettingsProfileId
                    && spending.RawProfileInputsDigest == authority.RawProfileInputsDigest
                    && spending.KarmaAttribute == authority.KarmaAttribute
                    && spending.MaxNumberMaxAttributesCreate == authority.MaxNumberMaxAttributesCreate
                    && spending.AlternateMetatypeAttributeKarma == authority.AlternateMetatypeAttributeKarma
                    && spending.ReverseAttributePriorityOrder == authority.ReverseAttributePriorityOrder)
                {
                    Scalar718(ledger, row, "maxnumbermaxattributescreate", spending.MaxNumberMaxAttributesCreate,
                        "EvaluateAllocations-maximum-attribute-count", invocation.Attributes718!);
                    if (row.Elements("karmacost").ToArray() is [var karmaCosts]
                        && Container718(ledger, karmaCosts, "canonical-Karma-cost-domain"))
                    {
                        if (_sourceInputs.CreationSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
                            ExecuteProfileAttributeKarma3201(row, authority, invocation, ledger);
                        else
                            Scalar718(ledger, karmaCosts, "karmaattribute", spending.KarmaAttribute,
                                "TryCalculateAllocation", invocation.Attributes718!);
                    }
                    ExecuteProfileAttributeHouseRules993(row, authority, invocation, ledger);
                    ExecuteProfileSecondMaximum3212(row, authority, invocation, ledger);
                }
                ExecuteProfileSourcebooks935(row, authority, invocation, ledger);
                ExecuteProfileBrokenGroup940(row, authority, invocation, ledger);
                ExecuteProfileQualityKarmaLimit947(row, authority, invocation, ledger);
                ExecuteProfileQualityCost973(row, authority, invocation, ledger);
                ExecuteProfileQualityPolicy1004(row, authority, invocation, ledger);
                ExecuteProfileContactPoints962(row, authority, invocation, ledger);
                ExecuteProfileKnowledgePoints971(row, authority, invocation, ledger);
                ExecuteProfileSkillCosts972(row, authority, invocation, ledger);
                ExecuteProfileMagic3200(row, authority, invocation, ledger);
                ExecuteProfileCompensation1015(row, authority, invocation, ledger);
                ExecuteProfileFunding994(row, authority, invocation, ledger);
                ExecuteEconomicPolicy3211(row, authority, invocation, ledger);
                ExecuteProfileCarryover995(row, authority, invocation, ledger);
                ExecuteProfileKarma1208(row, authority, invocation, ledger);
                ExecuteProfileLifecycle1217(row, authority, executed, invocation, ledger);
                ExecuteRetainedMagic1221(row, authority, invocation, ledger);
                ExecuteProfileLoadout1215(row, authority, invocation, ledger);
                ExecuteProfileContainers3212(row, authority, invocation, ledger);
                ExecuteRetainedSr5Compatibility1241(row, authority, invocation, ledger);
                ExecuteRetainedCompatibility1430(row, authority, invocation, ledger);
                ExecuteProfileFocus1220(row, authority, invocation, ledger);
                // No whitelist promotes the remainder of a selected profile.
                // Each other setting remains in the original ledger until its
                // own canonical consumer supplies an operation and result.
            }
        }

        private void ExecuteProfileFunding994(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            // Resolve the exact current profile with the canonical reader, then
            // require the privately executed options/selection for that authority.
            // No expression interpreter or second funding calculation lives here.
            if (!TryResolveCreationResourcesAuthority(out var resources)
                || !CharacterCreationResourcesRules.IsValidAuthority(resources)
                || resources.SettingsProfileId != authority.SettingsProfileId
                || resources.ProfileDigest != authority.RawProfileInputsDigest) return;
            string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
            foreach (string field in new[] { "chargenkarmatonuyenexpression", "nuyenmaxbp", "nuyenperbpwftm", "unrestrictednuyen" })
            {
                var leaves = profile.Elements().Where(node => node.Name.LocalName == field).ToArray();
                if (leaves is not [var leaf] || leaf.Name != field || leaf.HasAttributes
                    || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                    || !invocation.TryGetExecutedProfileFunding994(resources, profileDigest, field,
                        CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                        out var result, out var execution) || result is null || _sourceInputs.HasSourceDrift) continue;
                // The canonical reader has already selected the explicit
                // expression, so this exact legacy rate leaf is inactive.
                bool shadowedRate = field == "nuyenperbpwftm" && profile.Element("chargenkarmatonuyenexpression") is not null;
                ledger.Tree(leaf, shadowedRate ? D.NotSelected : D.Compiled, new
                {
                    Operation = "canonical-funding-options-and-selection", Field = field,
                    Authority = resources, Input = leaf.Value, Result = result, Execution = execution
                });
            }
        }

        private void ExecuteProfileContactPoints962(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            var leaves = profile.Elements().Where(node => node.Name.LocalName == "contactpointsexpression").ToArray();
            if (leaves is not [var leaf] || leaf.Name != "contactpointsexpression" || leaf.HasAttributes
                || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                || !TryResolveCreationContactsPolicy(out var policy) || policy is null
                || policy.SettingsProfileId != authority.SettingsProfileId
                || policy.RawProfileInputsDigest != authority.RawProfileInputsDigest
                || !invocation.TryGetExecutedContactPoints962(policy,
                    CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                    CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                    out var budget, out var execution) || budget is null || _sourceInputs.HasSourceDrift) return;
            ledger.Tree(leaf, D.Compiled, new
            {
                Operation = "canonical-contact-point-expression", Policy = policy,
                Attributes = invocation.Attributes718, Result = budget, Execution = execution
            });
        }

        private void ExecuteProfileAttributeHouseRules993(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            foreach (string field in new[] { "alternatemetatypeattributekarma", "reverseattributepriorityorder" })
            {
                if (profile.Elements().Where(node => node.Name.LocalName == field).ToArray() is not [var leaf]
                    || leaf.Name != field || leaf.HasAttributes
                    || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                    || !invocation.TryGetExecutedAttributeHouseRule993(authority.SettingsProfileId, authority.RawProfileInputsDigest,
                        CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)), field,
                        CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                        out bool executed, out bool noPaidTargets, out var result, out var execution)
                    || executed != (field == "alternatemetatypeattributekarma"
                        ? authority.AlternateMetatypeAttributeKarma : authority.ReverseAttributePriorityOrder)
                    || result is null || _sourceInputs.HasSourceDrift) continue;
                ledger.Tree(leaf, D.Compiled, new
                {
                    Operation = noPaidTargets ? "EvaluateAllocations-no-paid-attributes" : "TryCalculateAllocation-paid-base",
                    Input = leaf.Value, Value = executed, NoPaidTargets = noPaidTargets, Result = result, Execution = execution
                });
            }
        }

        private void ExecuteLifestyle718(XElement root, CharacterCreationRuleSourceScopeInvocation invocation,
            SourceScopeLedger ledger)
        {
            if (invocation.LifestyleAuthority718 is not { } actualAuthority
                || invocation.Lifestyles718 is not { } actual
                || !TryResolveCreationLifestylesAuthority(out var authority)
                || authority.ProfileDigest != actualAuthority.ProfileDigest || authority.SourceDigest != actualAuthority.SourceDigest)
                return;
            var projections = actual.ToList();
            XElement? fallbackXml = null;
            CharacterCreationStartingNuyenSource? cash = null;
            if (actual.Count == 0)
            {
                if (invocation.StartingLifestyleId718 is not null || invocation.WorkspaceId718 is not { } workspace
                    || !TryResolveCreationDefaultStartingNuyen(out cash) || cash is null
                    || !CharacterCreationFinalizationStartingCashRules.TryProjectDefaultLifestyle718(workspace,
                        cash, actualAuthority, out var fallback, out fallbackXml)) return;
                projections.Add(fallback!);
            }
            else
            {
                var starting = actual.Where(p => p.Configuration.LifestyleId == invocation.StartingLifestyleId718).ToArray();
                if (starting is not [var selected]) return;
                var option = actualAuthority.LifestyleOptions.Single(o => o.OptionId == selected.Configuration.BaseLifestyleOptionId);
                if (!TryResolveCreationPriorityStartingNuyen(option.SourceId, out cash) || cash is null
                    || !CharacterCreationFinalizationStartingCashRules.SourcesMatch(cash, actualAuthority)) return;
            }
            var ids = projections.Select(p => actualAuthority.LifestyleOptions.Single(o =>
                o.OptionId == p.Configuration.BaseLifestyleOptionId).SourceId.ToString("D")).ToHashSet(StringComparer.Ordinal);
            var rows = root.Element("lifestyles")!.Elements().ToArray();
            if (rows.Any(row => row.Name != "lifestyle")
                || rows.Select(row => row.Element("id")?.Value).Distinct().Count() != rows.Length
                || !ids.All(id => rows.Count(row => row.Element("id")?.Value == id) == 1)) return;
            ledger.Node(root, D.Structural, new { Operation = "PriceSelectedCompletion-and-starting-lifestyle", cash.SourceId });
            Container718(ledger, root.Element("lifestyles")!, "lifestyle-domain");
            foreach (var row in rows)
            {
                if (!TryScopeRowIdentity(row, new("lifestyles", "lifestyle", true), out string id)) continue;
                if (!ids.Contains(id))
                {
                    ledger.Tree(row, D.NotSelected, new { Operation = "purchase-and-starting-lifestyle-selection",
                        Candidate = id, Selected = ids.Order().ToArray() });
                    continue;
                }
                var option = actualAuthority.LifestyleOptions.Single(o => o.SourceId.ToString("D") == id);
                var results = projections.Where(p => p.Configuration.BaseLifestyleOptionId == option.OptionId).ToArray();
                if (!Container718(ledger, row, "selected-lifestyle")) continue;
                object result = new { Operation = actual.Count == 0 ? "TryProjectDefaultLifestyle718" : "PriceSelectedCompletion",
                    Results = results, SavedFallback = fallbackXml?.ToString(SaveOptions.DisableFormatting),
                    StartingSource = cash.SourceId, cash.Dice, cash.Multiplier, CashChoiceExecuted = false };
                Scalar718(ledger, row, "id", id, "lifestyle-source-selection", result);
                Scalar718(ledger, row, "name", option.Name, "lifestyle-source-selection", result);
                Scalar718(ledger, row, "cost", option.BaseCost, "TryProject-lifestyle-economics", result);
                // Fallback compilation really writes these terms, even when
                // the purchase basket is empty. No roll/amount is synthesized.
                if (fallbackXml is not null || invocation.SourceExecution832 && actual.Count != 0)
                {
                    Scalar718(ledger, row, "dice", option.StartingNuyenDice, "BuildLifestyleElement", result);
                    Scalar718(ledger, row, "multiplier", option.StartingNuyenMultiplier, "BuildLifestyleElement", result);
                    Scalar718(ledger, row, "lp", option.LifestylePoints, "BuildLifestyleElement", result);
                    if (invocation.SourceExecution832)
                        Scalar718(ledger, row, "allowbonuslp", option.AllowsBonusLifestylePoints, "BuildLifestyleElement", result);
                    Scalar718(ledger, row, "costforarea", option.CostPerArea, "BuildLifestyleElement", result);
                    Scalar718(ledger, row, "costforcomforts", option.CostPerComfort, "BuildLifestyleElement", result);
                    Scalar718(ledger, row, "costforsecurity", option.CostPerSecurity, "BuildLifestyleElement", result);
                    Scalar718(ledger, row, "source", option.SourceBook, "BuildLifestyleElement", result);
                    Scalar718(ledger, row, "page", option.Page, "BuildLifestyleElement", result);
                }
            }
            if (invocation.SourceExecution832) ExecuteLifestyleSupporting832(root, authority, projections, ledger);
            // Selected quality effects/freegrids and selected city rules
            // remain explicit. Cash multiplication happens only later,
            // with the user's actual choice, in TryPrepare/TryProject.
            if (_sourceInputs.HasSourceDrift) ledger.UnconsumeRoot();
        }
    }
}
