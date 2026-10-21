using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class RuleSourceAccounting
    {
        private readonly Dictionary<string, SkillCatalogueTrace> skillCatalogues = new(StringComparer.Ordinal);

        internal void RetainSkillCatalogue(CharacterCreationSkillsAuthority authority, SkillCatalogueTrace trace)
        {
            Charge(1);
            skillCatalogues[SkillCatalogueTrace.Key(authority)] = trace;
        }

        public override IReadOnlyDictionary<string, string> ConsumeSkillsSource(
            CharacterCreationSkillsAuthority authority, CharacterCreationCompletionSelections selections,
            CharacterCreationAttributeSkillsPreview evaluated)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!CapturePurchases || !authority.IsAuthoritative
                || !skillCatalogues.TryGetValue(SkillCatalogueTrace.Key(authority), out var trace))
                return result;
            using var scope = resolver.CreateOperationScope();
            if (scope.TryCreateContext(inputs.CharacterXml) is not SourceDataContext context
                || !context.MatchesRetainedSelection(inputs)
                || !context.TryResolveCreationEffectivePolicy(out var policy)
                || policy is null || policy.PolicyDigest != inputs.PolicyDigest
                || !trace.Matches(policy, authority, evaluated, out var decisions))
                return result;
            var sources = policy.Sources.Where(row =>
                row.Kind == CharacterCreationPolicySourceKind.Rules && row.SourceId == "skills").ToArray();
            if (sources is not [var source] || source.ContentDigest is null) return result;
            result.Add("source:" + source.Kind + ":" + source.Order,
                CharacterCreationFinalizationDigest.Compute(new
                {
                    Schema = "chummer.private-skills-source-execution.v2",
                    Source = source, policy.PolicyDigest, trace.MetadataDigest,
                    Catalogue = SkillCatalogueTrace.Key(authority), Decisions = decisions,
                    // Bind real point spending and interval/specialization prices, not
                    // only caps or a count of selected rows. This is private source custody.
                    selections.SkillAllocations, selections.GroupAllocations,
                    selections.SkillKarmaPurchases, Evaluated = evaluated
                }));
            return result;
        }
    }

    private sealed class SkillCatalogueTrace
    {
        private sealed record Row(string Kind, string Id, string Xml, string Book)
        {
            internal string? Decision { get; set; }
            internal bool SourceEnabled { get; set; }
            internal CharacterCreationSkillCatalogEntry? Option { get; set; }
        }

        private readonly RuleSourceAccounting accounting;
        private readonly string profileId;
        private readonly string profileDigest;
        private readonly string[] books;
        private readonly Dictionary<(string Kind, string Id), Row> rows;
        private bool valid = true;
        internal string MetadataDigest { get; }

        internal SkillCatalogueTrace(RuleSourceAccounting accounting, XElement root,
            string profileId, string profileDigest, IEnumerable<string> enabledBooks)
        {
            this.accounting = accounting;
            this.profileId = profileId;
            this.profileDigest = profileDigest;
            books = enabledBooks.Order(StringComparer.Ordinal).ToArray();
            MetadataDigest = CharacterCreationFinalizationDigest.Compute(root.ToString(SaveOptions.DisableFormatting));
            rows = new Dictionary<(string, string), Row>();
            foreach (var (container, kind) in Containers)
                foreach (var row in root.Element(container)!.Elements("skill"))
                {
                    string id = Guid.Parse(ReadValue(row, "id")).ToString("D");
                    rows.Add((kind, id), new(kind, id, row.ToString(SaveOptions.DisableFormatting), ReadValue(row, "source")));
                }
        }

        internal static readonly (string Container, string Kind)[] Containers =
        [
            ("skills", CharacterCreationSkillKinds.Active),
            ("knowledgeskills", CharacterCreationSkillKinds.Knowledge)
        ];

        // Talent access is bound by the canonical allocation execution. Its later
        // enrichment adds source anchors as well as access; both are checked by the
        // actual canonical result replay, independently of base catalogue custody.
        internal static string Key(CharacterCreationSkillsAuthority authority)
            => CharacterCreationFinalizationDigest.Compute(authority with { TalentAccess = null, SourceAnchorIds = [], AuthorityDigest = string.Empty });

        private Row? Observe(XElement element, string kind)
        {
            if (!Guid.TryParse(ReadValue(element, "id"), out var id)
                || !rows.TryGetValue((kind, id.ToString("D")), out var row)
                || row.Xml != element.ToString(SaveOptions.DisableFormatting) || row.Decision is not null)
            {
                valid = false;
                return null;
            }
            return row;
        }

        internal void Skip(XElement element, string kind, string reason, bool exact, bool sourceEnabled = false)
        {
            var row = Observe(element, kind);
            if (row is null || !exact) { valid = false; return; }
            row.Decision = reason;
            row.SourceEnabled = sourceEnabled;
        }

        internal void Project(XElement element, string kind, bool sourceEnabled, CharacterCreationSkillCatalogEntry option)
        {
            var row = Observe(element, kind);
            if (row is null || option.Kind != kind || option.SourceSkillId != row.Id)
            { valid = false; return; }
            row.Decision = "projected";
            row.SourceEnabled = sourceEnabled;
            row.Option = option;
        }

        internal void Finish(CharacterCreationSkillsAuthority authority)
        {
            if (!valid || !authority.IsAuthoritative || rows.Values.Any(row => row.Decision is null)) return;
            var observed = rows.Values.Where(row => row.Option is not null).Select(row => row.Option!)
                .OrderBy(row => row.Kind, StringComparer.Ordinal).ThenBy(row => row.SourceSkillId, StringComparer.Ordinal).ToArray();
            var actual = authority.ActiveSkills.Concat(authority.KnowledgeSkills)
                .OrderBy(row => row.Kind, StringComparer.Ordinal).ThenBy(row => row.SourceSkillId, StringComparer.Ordinal).ToArray();
            if (CharacterCreationFinalizationDigest.EqualsFixedTime(
                    CharacterCreationFinalizationDigest.Compute(observed),
                    CharacterCreationFinalizationDigest.Compute(actual)))
                accounting.RetainSkillCatalogue(authority, this);
        }

        internal bool Matches(CharacterCreationEffectivePolicyContext policy, CharacterCreationSkillsAuthority authority,
            CharacterCreationAttributeSkillsPreview evaluated, out object decisions)
        {
            decisions = Array.Empty<object>();
            if (!valid || policy.SettingsProfileId != profileId || policy.ProfileInputsDigest != profileDigest
                || !books.SequenceEqual(policy.EnabledSourcebooks.Order(StringComparer.Ordinal), StringComparer.Ordinal)
                || evaluated.Blockers.Count != 0) return false;
            var selected = new HashSet<(string Kind, string Id)>();
            foreach (var skill in evaluated.Skills)
            {
                if (!selected.Add((skill.Kind, skill.SourceSkillId))
                    || !rows.TryGetValue((skill.Kind, skill.SourceSkillId), out var row)
                    || row.Option is not { } option
                    || !(row.SourceEnabled || option.IgnoresSourceDisabled)
                    || skill.Name != option.Name || skill.Category != option.Category
                    || skill.DefaultAttribute != option.DefaultAttribute || skill.SkillGroup != option.SkillGroup
                    || skill.SpecializationOptionId is { } spec
                        && option.Specializations.Count(choice => choice.OptionId == spec
                            && choice.Name == skill.SpecializationName) != 1)
                    return false;
            }
            var groupMembers = new HashSet<(string Kind, string Id)>();
            foreach (var group in evaluated.SkillGroups)
            {
                var matches = authority.SkillGroups.Where(row => row.GroupId == group.GroupId).ToArray();
                if (matches is not [var option] || group.Name != option.Name
                    || !group.MemberSkillSourceIds.SequenceEqual(option.MemberSkillSourceIds, StringComparer.Ordinal))
                    return false;
                foreach (string id in group.MemberSkillSourceIds)
                {
                    if (!rows.TryGetValue((CharacterCreationSkillKinds.Active, id), out var row)
                        || row.Option is not { } member
                        || !(row.SourceEnabled || member.IgnoresSourceDisabled)) return false;
                    groupMembers.Add((CharacterCreationSkillKinds.Active, id));
                }
            }
            decisions = rows.Values.OrderBy(row => row.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.Id, StringComparer.Ordinal).Select(row => new
                {
                    row.Kind, row.Id, row.Book, row.Decision, row.SourceEnabled,
                    RawSource = CharacterCreationFinalizationDigest.Compute(row.Xml),
                    Option = row.Option, Selected = selected.Contains((row.Kind, row.Id)),
                    UsedByGroup = groupMembers.Contains((row.Kind, row.Id))
                }).ToArray();
            return true;
        }
    }

    private sealed partial class SourceDataContext
    {
        private SkillCatalogueTrace? BeginSkillCatalogue(XElement[] active, XElement[] knowledge)
        {
            var accounting = _sourceInputs.Accounting;
            if (accounting is not { CapturePurchases: true } || _sourceInputs.HasSourceDrift
                || !TryHasSelectedCustomDataInputFor(_customDirectories, "skills.xml", out bool custom) || custom
                || !TryLoadEffectiveDocument(_catalog, "skills.xml", out var effective)
                || effective?.Root is not { } root || !ValidSkillCatalogue(root, active, knowledge))
                return null;
            // Do not claim source semantics for replacement/custom/amend contributors.
            // Their obligations are independently unresolved, even if numbers match.
            foreach (var pack in _catalog.Overlays.Where(pack => pack.Enabled))
            {
                if (string.IsNullOrWhiteSpace(pack.DataPath) || !SourceDirectoryExists(pack.DataPath)) return null;
                if (pack.Mode == ContentOverlayModes.ReplaceFile)
                {
                    if (SourceFileExists(Path.Combine(pack.DataPath, "skills.xml"))) return null;
                }
                else if (pack.Mode == ContentOverlayModes.MergeCatalog)
                {
                    if (EnumerateSourceFiles(pack.DataPath, "*.xml", SearchOption.TopDirectoryOnly)
                        .Any(path => string.Equals(ResolveCatalogTargetFileName(Path.GetFileName(path)),
                            "skills.xml", StringComparison.OrdinalIgnoreCase))) return null;
                }
                else return null;
            }
            // The merger can collapse duplicate IDs. Check the raw base too, and
            // require the exact same rows, not merely a surviving identity set.
            if (!TryLoadXml(Path.Combine(_catalog.BaseDataPath, "skills.xml"), out var raw)
                || raw?.Root is not { } original || !ValidSkillCatalogue(original, active, knowledge)) return null;
            return new(accounting, root, _settingsProfileId, _rawProfileInputsDigest, _enabledSourcebooks);
        }

        private static bool ValidSkillCatalogue(XElement root, XElement[] active, XElement[] knowledge)
        {
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            if (root.Name != "chummer" || HasDirectText(root)
                || root.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration
                    && !(attribute.Name == xsi + "schemaLocation"
                        && attribute.Value == "http://www.w3.org/2001/XMLSchema skills.xsd"))
                || root.Elements().Any(node => node.Name != "skills" && node.Name != "knowledgeskills"
                    && node.Name != "categories" && node.Name != "skillgroups")) return false;
            var ids = new HashSet<Guid>();
            foreach (var (container, kind) in SkillCatalogueTrace.Containers)
            {
                if (root.Elements(container).ToArray() is not [var records]
                    || records.HasAttributes || HasDirectText(records)
                    || records.Elements().Any(row => row.Name != "skill")) return false;
                foreach (var row in records.Elements())
                {
                    if (row.HasAttributes || HasDirectText(row)
                        || row.Elements("id").ToArray() is not [var id] || !Scalar(id)
                        || !Guid.TryParseExact(id.Value, "D", out var identity) || identity == Guid.Empty
                        || !ids.Add(identity)
                        || row.Elements().Any(child => child.Name.Namespace != XNamespace.None)
                        || row.Elements("specs").Any(specs => HasDirectText(specs)
                            || specs.Elements().Any(spec => spec.Name != "spec")
                            || specs.Elements().Select(spec => spec.Value).Distinct(StringComparer.Ordinal).Count()
                                != specs.Elements().Count())) return false;
                }
                var expected = container == "skills" ? active : knowledge;
                string[] Canonical(IEnumerable<XElement> source) => source
                    .Select(row => row.ToString(SaveOptions.DisableFormatting)).Order(StringComparer.Ordinal).ToArray();
                if (!Canonical(records.Elements()).SequenceEqual(Canonical(expected), StringComparer.Ordinal)) return false;
            }
            if (root.Elements("categories").Count() > 1 || root.Elements("skillgroups").Count() > 1) return false;
            foreach (var container in root.Elements("categories"))
            {
                if (container.HasAttributes || HasDirectText(container)) return false;
                var categories = new HashSet<(string, string)>();
                foreach (var category in container.Elements())
                {
                    string kind = category.Attribute("type")?.Value ?? "";
                    if (category.Name != "category" || category.HasElements
                        || category.Attributes().Count() != 1 || category.Attributes().Single().Name != "type"
                        || !CharacterCreationStandardPrioritySkillsRules.IsSupportedCategory(kind, category.Value)
                        || !categories.Add((kind, category.Value))) return false;
                }
                foreach (var (name, kind) in SkillCatalogueTrace.Containers)
                    if (root.Element(name)!.Elements().Any(row => !categories.Contains((kind, ReadValue(row, "category")))))
                        return false;
            }
            foreach (var container in root.Elements("skillgroups"))
            {
                if (container.HasAttributes || HasDirectText(container)) return false;
                var groups = new HashSet<string>(StringComparer.Ordinal);
                foreach (var group in container.Elements())
                    if (group.Name != "name" || !Scalar(group) || group.Value != group.Value.Trim()
                        || !groups.Add(group.Value)) return false;
                if (root.Element("skills")!.Elements().Select(row => ReadValue(row, "skillgroup"))
                    .Any(name => name.Length != 0 && !groups.Contains(name))) return false;
            }
            return true;
        }
    }
}
