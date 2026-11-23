using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static partial class CharacterCareerPowerGraph
{
    // Native MentorSpirit.Save stores the chosen bonus's children, not its name.
    // Resolve that payload to exactly one retained choice; it never supplies rules.
    internal static IReadOnlyList<(string Slot, string Extra, XElement Bonus)> SelectedGrantBonuses5715(
        string kind, XElement owned, XElement source)
    {
        var result = new List<(string, string, XElement)> { ("bonus", Value(owned, "extra", ""), Single(source, "bonus", false)) };
        if (kind != "MentorSpirit") return result;
        var used = new HashSet<XElement>(ReferenceEqualityComparer.Instance);
        foreach (string slot in new[] { "choice1", "choice2" })
        {
            var saved = Single(owned, slot, false);
            Need(!saved.HasAttributes, "power_mentor_choice_shape_unavailable");
            string extra = Value(owned, "extra" + slot, "");
            if (!saved.HasElements && saved.Value.Length == 0)
            {
                Need(extra.Length == 0, "power_mentor_choice_missing");
                continue;
            }
            var definitions = Single(source, "choices");
            Need(!definitions.HasAttributes && definitions.Elements().All(e => e.Name == "choice")
                && definitions.Nodes().All(n => n is XElement || n is XText text && string.IsNullOrWhiteSpace(text.Value)),
                "power_mentor_choice_shape_unavailable");
            var choices = definitions.Elements("choice").Where(choice =>
                saved.HasElements
                    ? XNode.DeepEquals(new XElement("bonus", saved.Nodes()), Single(choice, "bonus"))
                    : Value(choice, "name") == saved.Value).ToArray();
            Need(choices.Length == 1 && used.Add(choices[0]), "power_mentor_choice_missing_or_ambiguous");
            var selected = choices[0];
            Need(!selected.HasAttributes && selected.Elements().All(e => e.Name == "name" || e.Name == "bonus")
                && selected.Elements().GroupBy(e => e.Name).All(g => g.Count() == 1)
                && selected.Nodes().All(n => n is XElement || n is XText text && string.IsNullOrWhiteSpace(text.Value)),
                "power_mentor_choice_shape_unavailable");
            // Additional native prompt choices need their own exact selection
            // proof. This bounded join supports the selected choice label only.
            Need(extra.Length == 0 || extra == Value(selected, "name"), "power_mentor_choice_prompt_unavailable");
            result.Add((slot, extra == Value(selected, "name") ? "" : extra, Single(selected, "bonus")));
        }
        return result.AsReadOnly();
    }

    private sealed class SourcePolicy(ICharacterSourceDataContext context, XElement character, List<string> evidence,
        IReadOnlyList<CharacterCareerPowerSource> powers, CharacterFinalizedLifeModuleOwnership? modules)
    {
        private readonly Dictionary<(string, Guid), XElement> sources = [];
        private readonly HashSet<string> resolving = [];
        private readonly Dictionary<Guid, XElement> moduleSources = [];
        private readonly HashSet<XElement> moduleDefinitions = new(ReferenceEqualityComparer.Instance);
        internal List<ReadGrant5715> ProvenGrants { get; } = [];

        private XElement Source(string kind, XElement owned)
        {
            Guid id = PowerSourceId(owned);
            if (kind == "Quality" && modules is not null && modules.TryGetModuleDefinition(owned, out var module))
            {
                Guid owner = GuidValue(owned, "guid");
                if (!moduleSources.TryGetValue(owner, out var selected))
                {
                    selected = module!;
                    moduleSources.Add(owner, selected);
                    moduleDefinitions.Add(selected);
                }
                Need(GuidValue(selected, "id") == id && Value(selected, "name") == Value(owned, "name")
                    && Bool(owned, "implemented", true) && Bool(selected, "implemented", true),
                    "power_module_identity_unavailable");
                Need(!Single(character, "improvements").Elements("improvement").Any(e =>
                    Bool(e, "enabled") && Value(e, "improvementttype") == "DisableQuality"),
                    "power_disabled_quality_semantics_unavailable");
                return selected;
            }
            if (!sources.TryGetValue((kind, id), out var xml))
            {
                Need(context.TryResolveCareerPowerGrantSource(kind, id, out var source) && source is not null,
                    "power_grant_source_unavailable");
                Need(source!.Kind == kind && source.SourceId == id, "power_grant_identity_invalid");
                xml = Parse(source.RawSourceXml);
                evidence.Add(source.AuthorityState);
                sources[(kind, id)] = xml;
            }
            // Cache only the definition. Each owned row is a distinct trust boundary.
            Need(GuidValue(xml, "id") == id && Value(xml, "name") == Value(owned, "name"), "power_grant_identity_invalid");
            Need(Bool(owned, "implemented", true) && Bool(xml, "implemented", true), "power_grant_not_implemented");
            Need(!Single(character, "improvements").Elements("improvement").Any(e =>
                Bool(e, "enabled") && Value(e, "improvementttype") == "DisableQuality"),
                "power_disabled_quality_semantics_unavailable");
            return xml;
        }

        public void ValidateOwnedSources()
        {
            foreach (var pair in new[] { ("Quality", "qualities"), ("MentorSpirit", "mentorspirits"), ("Metamagic", "metamagics") })
                foreach (var owned in Single(character, pair.Item2, false).Elements())
                    _ = Source(pair.Item1, owned);
        }

        public bool Requirements(XElement source)
        {
            // Module selection prerequisites belong to the validated historical
            // sequence. Only this proof-issued module object takes that path;
            // ordinary quality and power requirements keep their current checks.
            if (moduleDefinitions.Contains(source)) return true;
            string key = source.Name + ":" + Value(source, "id");
            Need(resolving.Add(key), "power_requirement_cycle");
            try
            {
                var required = Single(source, "required", false);
                var forbidden = Single(source, "forbidden", false);
                bool[] requirements = required.Elements().Select(Predicate).ToArray();
                bool[] exclusions = forbidden.Elements().Select(Predicate).ToArray();
                Need(!required.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))
                    && !forbidden.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)),
                    "power_requirement_text_unavailable");
                return requirements.All(b => b) && !exclusions.Any(b => b);
            }
            finally { resolving.Remove(key); }
        }

        private bool Predicate(XElement node)
        {
            Need(!node.HasAttributes && node.Name.NamespaceName == "", "power_requirement_attributes_unavailable");
            if (node.Name == "resenabled" || node.Name == "depenabled" || node.Name == "magenabled")
            {
                Need(!node.HasElements && node.Value == "", "power_requirement_marker_invalid");
                return Bool(character, node.Name.LocalName, false);
            }
            if (node.Name is var name && (name == "oneof" || name == "allof"))
            {
                var values = node.Elements().Select(Predicate).ToArray();
                Need(values.Length > 0 && !node.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)),
                    "power_requirement_group_invalid");
                return name == "oneof" ? values.Any(b => b) : values.All(b => b);
            }
            if (node.Name == "quality")
            {
                Need(!node.HasElements && node.Value.Length > 0 && node.Value == node.Value.Trim(),
                    "power_quality_requirement_invalid");
                var matches = Single(character, "qualities").Elements("quality").Where(q =>
                    Value(q, "name") == node.Value || Guid.TryParse(node.Value, out var id) && PowerSourceId(q) == id).ToArray();
                if (matches.Length == 0) return false;
                foreach (var row in matches)
                {
                    var definition = Source("Quality", row);
                    if (!Bool(row, "implemented", true) || !Requirements(definition)) return false;
                }
                return true;
            }
            // Unknown minimums, nested selections and other actor requirements
            // cannot become permission by being ignored.
            throw new InvalidDataException("power_requirement_unavailable:" + node.Name.LocalName);
        }

        public bool WayAllowed(XElement power)
        {
            var way = Single(power, "adeptwayrequires", false);
            Need(way.Elements().All(e => e.Name == "required" || e.Name == "forbidden" || e.Name == "magicianswayforbids")
                && way.Elements().GroupBy(e => e.Name).All(g => g.Count() == 1), "power_way_semantics_unavailable");
            bool magicianWay = false;
            if (way.Element("magicianswayforbids") is null)
            {
                // Name-independent: resolve the actual owned quality bonus.
                foreach (var row in Single(character, "qualities").Elements("quality"))
                {
                    var source = Source("Quality", row);
                    if (source.Element("bonus")?.Element("magicianswaydiscount") is not { } marker) continue;
                    Need(!marker.HasAttributes && !marker.HasElements && marker.Value == "", "power_way_marker_invalid");
                    if (Requirements(source) && Bool(row, "implemented", true)) magicianWay = true;
                }
            }
            var required = Single(way, "required", false).Elements().Select(Predicate).ToArray();
            var forbidden = Single(way, "forbidden", false).Elements().Select(Predicate).ToArray();
            return magicianWay || required.Length > 0 && required.All(v => v) && !forbidden.Any(v => v);
        }

        public IReadOnlyList<CharacterCareerPowerGrant> Grants(XElement improvements)
        {
            var result = new List<CharacterCareerPowerGrant>();
            var used = new HashSet<string>();
            foreach (var effect in improvements.Elements())
            {
                string type = Value(effect, "improvementttype");
                if (type is not ("AdeptPowerPoints" or "AdeptPowerFreeLevels" or "AdeptPowerFreePoints")) continue;
                if (!Bool(effect, "enabled")) continue;
                Need(!Bool(effect, "custom") && Value(effect, "condition") == ""
                    && Value(effect, "exclude") == "" && Value(effect, "target") == ""
                    && !Bool(effect, "addtorating") && Decimal(effect, "min") == 0
                    && Decimal(effect, "max") == 0 && Decimal(effect, "aug") == 0 && Decimal(effect, "augmax") == 0,
                    "power_conditional_grant_unavailable");
                string kind = Value(effect, "improvementsource");
                string container = kind switch { "Quality" => "qualities", "MentorSpirit" => "mentorspirits",
                    "Metamagic" => "metamagics", _ => throw new InvalidDataException("power_grant_origin_unavailable") };
                Guid instance = GuidValue(effect, "sourcename");
                var owned = Single(character, container).Elements().Single(r => GuidValue(r, "guid") == instance);
                var source = Source(kind, owned);
                Need(Requirements(source), "power_grant_prerequisite_unavailable");
                var bonuses = SelectedGrantBonuses5715(kind, owned, source).Select(b => b.Bonus).ToArray();
                string powerName = Value(effect, "improvedname");
                string extra = Value(effect, "unique", "");
                int levels = 0, quarters = 0;
                decimal pool = 0;
                XElement[] matches;
                if (type == "AdeptPowerPoints")
                {
                    Need(powerName == "" && extra == "" && Int(effect, "rating") == 1, "power_pool_grant_invalid");
                    pool = Decimal(effect, "val");
                    matches = bonuses.SelectMany(b => b.Elements("adeptpowerpoints"))
                        .Where(n => !n.HasElements && !n.HasAttributes && n.Value != "Rating"
                            && Expression(n.Value, 1) == pool).ToArray();
                }
                else
                {
                    int rating = Int(effect, "rating");
                    Need(rating > 0 && Decimal(effect, "val") == 0, "power_free_grant_invalid");
                    matches = bonuses.SelectMany(b => b.Elements("specificpower"))
                        .Where(n => GrantMatches(n, type, powerName, extra, rating)).ToArray();
                    if (type == "AdeptPowerFreeLevels") levels = rating; else quarters = rating;
                }
                Need(matches.Length == 1 && pool >= 0, "power_grant_source_semantics_unavailable");
                string keyId = string.Join('\0', kind, instance, type, powerName, extra,
                    matches[0].ToString(SaveOptions.DisableFormatting));
                Need(used.Add(keyId), "power_duplicate_grant");
                var grant = new CharacterCareerPowerGrant(kind, instance, PowerSourceId(owned), powerName, extra, levels, quarters, pool);
                result.Add(grant);
                ProvenGrants.Add(new(grant, type, Int(effect, "rating"),
                    source.ToString(SaveOptions.DisableFormatting), matches[0].ToString(SaveOptions.DisableFormatting)));
            }
            return Array.AsReadOnly(result.ToArray());
        }

        private bool GrantMatches(XElement node, string type, string name, string extra, int rating)
        {
            if (node.HasAttributes || node.Elements().Any(n => n.Name != "name" && n.Name != "val"
                && n.Name != "pointsperlevel" && n.Name != "bonusoverride") || Value(node, "name") != name) return false;
            bool points = Value(node, "pointsperlevel", "").Length > 0;
            var power = powers.SingleOrDefault(p => p.Name == name);
            if (power is null) return false;
            int granted = Bool(Parse(power.RawSourceXml), "levels") ? Int(node, "val", 0) : 1;
            if (points != (type == "AdeptPowerFreePoints") || granted != rating) return false;
            var overrideNode = Single(node, "bonusoverride", false);
            if (!overrideNode.HasElements) return extra == "";
            // Only an exact single attribute choice is resolvable without a prompt.
            return overrideNode.Elements().Count() == 1 && overrideNode.Element("selectattribute") is { } select
                && !select.HasAttributes && select.Elements().Count() == 1
                && select.Element("attribute") is { } attribute && !attribute.HasAttributes && !attribute.HasElements
                && attribute.Value == extra;
        }
    }
}
