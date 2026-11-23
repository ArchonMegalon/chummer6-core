using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerMartialAcquisitionTransaction
{
    // MartialArt.cs:49,253; CharacterSettings.cs:185,3793-3794; CharacterCareer.cs:12022-12024.
    private static int TechniqueRate(XElement settings)
    {
        var costs = Box(settings, "karmacost");
        if (!int.TryParse(Scalar(costs, "karmatechnique", ""), NumberStyles.Integer, Invariant, out int rate)
            && !int.TryParse(Scalar(costs, "karmamaneuver", ""), NumberStyles.Integer, Invariant, out rate)) rate = 5;
        Need(rate >= 0, "martial_acquisition_negative_technique_rate_unsupported");
        return rate;
    }
    private static void ValidateMartialSource(XElement row, Guid id, bool art, bool ownedParent = false)
    {
        Need(row.Name == (art ? "martialart" : "technique") && !row.HasAttributes
            && GuidValue(row, "id") == id && Scalar(row, "name").Length is > 0 and <= 512
            && Scalar(row, "source").Length is > 0 and <= 128 && Scalar(row, "page").Length is > 0 and <= 128,
            "martial_acquisition_source_identity_invalid");
        string[] fields = art
            ? ["id", "name", "translate", "source", "page", "altpage", "cost", "bonus", "required", "forbidden", "techniques", "isquality", "alltechniques", "notes", "altnotes", "notesColor"]
            : ["id", "name", "translate", "source", "page", "altpage", "bonus", "required", "forbidden", "notes", "altnotes", "notesColor"];
        Need(row.Elements().All(x => fields.Contains(x.Name.LocalName, StringComparer.Ordinal) && x.Name.NamespaceName.Length == 0)
            && row.Elements().GroupBy(x => x.Name).All(g => g.Count() == 1), "martial_acquisition_source_variant_unsupported");
        bool quality = Flag(row, "isquality", false);
        var all = Node(row, "alltechniques");
        if (quality || all is not null)
        {
            // Canonical all-techniques quality: an already-granted parent, never an art
            // purchase. Other grant/membership/effect shapes remain unsupported.
            Need(art && ownedParent && quality && all is not null
                && !all.HasElements && string.IsNullOrWhiteSpace(all.Value)
                && Node(row, "techniques") is null && Node(row, "bonus") is null,
                "martial_acquisition_quality_granted_variant_unsupported");
        }
    }

    // SelectMartialArtTechnique.cs:58-81. Source/profile admission still comes
    // from the resolver; alltechniques broadens membership only for an empty art.
    private static bool AllowsTechnique(XElement parentSource, string name, bool first)
        => (Node(parentSource, "alltechniques") is null || first) && IsTechniqueMember(parentSource, name);

    // Membership of a saved technique is independent of whether another may be selected.
    // Callers validate the parent definition and resolve enabled technique sources first.
    private static bool IsTechniqueMember(XElement parentSource, string name)
    {
        if (Node(parentSource, "alltechniques") is not null) return true;
        var allowed = Box(parentSource, "techniques");
        Need(allowed.Elements().All(x => x.Name == "technique" && !x.HasAttributes
            && x.Elements().All(n => n.Name == "name") && x.Elements("name").Count() == 1),
            "martial_acquisition_parent_technique_variant_unsupported");
        return allowed.Elements("technique").Any(x => Scalar(x, "name") == name);
    }
    // SelectionShared.cs:655-805: required groups are conjunctive; any forbidden
    // oneof match excludes the choice. Unsupported syntax is never permission.
    private static string CheckRequirements(XElement source, XElement character, XElement settings,
        ICharacterSourceDataContext context)
    {
        var required = Node(source, "required");
        var forbidden = Node(source, "forbidden");
        if (required is null && forbidden is null) return "";
        foreach (var group in new[] { required, forbidden }.OfType<XElement>())
        {
            Need(group.HasElements && group.Elements().Count() <= 128
                && group.Nodes().All(n => n is XElement or XComment || n is XText t && string.IsNullOrWhiteSpace(t.Value))
                && group.Elements().All(n => n.Name == "oneof" || group == required && n.Name == "allof"),
                "martial_acquisition_requirements_unsupported");
        }
        Need(context is ICharacterCareerComplexFormLearningSourceContext, "martial_acquisition_magic_sources_unavailable");
        // Existing retained capability resolves metatype + owned qualities, despite its resonance name.
        var resolver = (ICharacterCareerComplexFormLearningSourceContext)context;
        Need(resolver.TryResolveCareerResonanceSource(out var authority) && authority is not null
            && !string.IsNullOrWhiteSpace(authority.AuthorityState), "martial_acquisition_magic_sources_unavailable");
        var qualities = authority!.Qualities.Select(q => Parse(q.QualityXml, "quality")).ToArray();
        var metatype = Parse(authority.MetatypeXml, "metatype");
        var inputs = new List<string> { authority.AuthorityState };
        bool enabledBySource = qualities.Append(metatype).Any(q => q.Element("bonus")?.Elements("enableattribute")
            .Any(e => e.Elements("name").Any(n => !n.HasElements && n.Value == "MAG")) == true);
        bool Evaluate(XElement node, int depth)
        {
            Need(depth <= 16 && node.Name.NamespaceName.Length == 0
                && node.Attributes().All(a => node.Name == "quality" && a.Name == "extra"),
                "martial_acquisition_requirements_unsupported");
            switch (node.Name.LocalName)
            {
                case "allof":
                case "oneof":
                case "group":
                case "grouponeof":
                    // Only the outer wrappers use allof/oneof. Canonical nested
                    // predicates are group/grouponeof (SelectionShared.cs:1982,2022).
                    Need(depth == 0 ? node.Name == "allof" || node.Name == "oneof"
                        : node.Name == "group" || node.Name == "grouponeof",
                        "martial_acquisition_requirements_unsupported");
                    Need(node.HasElements && node.Elements().Count() <= 128
                        && node.Nodes().All(n => n is XElement or XComment || n is XText t && string.IsNullOrWhiteSpace(t.Value)),
                        "martial_acquisition_requirements_unsupported");
                    // Materialize every branch: an unknown clause cannot hide behind a true alternative.
                    var values = node.Elements().Select(n => Evaluate(n, depth + 1)).ToArray();
                    return node.Name == "oneof" || node.Name == "grouponeof" ? values.Any(x => x) : values.All(x => x);
                case "magenabled":
                    Need(!node.HasElements && string.IsNullOrWhiteSpace(node.Value), "martial_acquisition_requirements_unsupported");
                    bool enabled = Flag(character, "magenabled", false);
                    // A claimed flag without source support is not a false predicate that may
                    // authorize a forbidden clause. Refuse the inconsistent imported state.
                    Need(!enabled || enabledBySource, "martial_acquisition_requirements_not_met");
                    return enabled;
                case "quality":
                    RequirementLiteral(node);
                    string extra = (string?)node.Attribute("extra") ?? "";
                    Need(extra.Length <= 512, "martial_acquisition_requirements_unsupported");
                    // SelectionShared.cs:2749: extra is compared to the owned instance,
                    // never the source definition's select/bonus. Empty extra means any.
                    var savedQualities = Box(character, "qualities").Elements("quality").ToArray();
                    Need(savedQualities.Length == authority.Qualities.Count
                        && savedQualities.Select(q => GuidValue(q, "guid")).Distinct().Count() == savedQualities.Length,
                        "martial_acquisition_requirement_quality_ambiguous");
                    var qualityMatches = authority.Qualities.Select(q =>
                    {
                        var owned = savedQualities.Where(x => GuidValue(x, "guid") == q.InstanceId).ToArray();
                        Need(owned.Length == 1 && GuidValue(owned[0], "sourceid") == q.SourceId,
                            "martial_acquisition_requirement_quality_ambiguous");
                        var definition = Parse(q.QualityXml, "quality");
                        Need(GuidValue(definition, "id") == q.SourceId
                            && Scalar(owned[0], "name") == Scalar(definition, "name"),
                            "martial_acquisition_requirement_quality_ambiguous");
                        return RequirementIdentity(definition, node.Value)
                            && (extra.Length == 0 || Scalar(owned[0], "extra", "") == extra);
                    }).ToArray();
                    return qualityMatches.Any(x => x);
                case "martialart":
                case "martialtechnique":
                    RequirementLiteral(node);
                    return MartialRequirement(node, character, context, inputs);
                case "skill":
                    return SkillRequirement(node, character, settings, context, inputs);
                case "attribute":
                    return AttributeRequirement(node, character, metatype);
                default: throw new InvalidDataException("martial_acquisition_requirements_unsupported");
            }
        }
        bool[] requirements = required?.Elements().Select(n => Evaluate(n, 0)).ToArray() ?? [];
        bool[] exclusions = forbidden?.Elements().Select(n => Evaluate(n, 0)).ToArray() ?? [];
        Need(requirements.All(x => x), "martial_acquisition_requirements_not_met");
        Need(!exclusions.Any(x => x), "martial_acquisition_forbidden");
        return string.Join('\0', inputs);
    }

    private static void RequirementLiteral(XElement node)
        => Need(!node.HasElements && !string.IsNullOrWhiteSpace(node.Value)
            && node.Value == node.Value.Trim() && node.Value.Length <= 512,
            "martial_acquisition_requirements_unsupported");

    private static bool RequirementIdentity(XElement source, string value)
        => Scalar(source, "name") == value
            || string.Equals(GuidValue(source, "id").ToString("D"), value, StringComparison.OrdinalIgnoreCase);

    private static void RequirementFields(XElement node, params string[] fields)
        => Need(node.HasElements && node.Elements().All(x => fields.Contains(x.Name.LocalName, StringComparer.Ordinal)
            && x.Name.NamespaceName.Length == 0 && !x.HasElements && !x.HasAttributes)
            && node.Elements().GroupBy(x => x.Name).All(g => g.Count() == 1)
            && node.Nodes().All(n => n is XElement or XComment || n is XText t && string.IsNullOrWhiteSpace(t.Value)),
            "martial_acquisition_requirements_unsupported");

    private static int RequirementNumber(XElement node, string field, string? fallback = null)
    {
        Need(int.TryParse(Scalar(node, field, fallback), NumberStyles.Integer, Invariant, out int value)
            && value >= 0 && value <= 1000, "martial_acquisition_requirement_number_unsupported");
        return value;
    }

    // SelectionShared.cs:2077,2134 searches every owned art, including techniques
    // of other parent arts. Keep the canonical legacy identity view read-only.
    private static bool MartialRequirement(XElement node, XElement character,
        ICharacterSourceDataContext context, List<string> inputs)
    {
        Need(context is ICharacterCareerMartialAcquisitionSourceContext,
            "martial_acquisition_requirement_martial_sources_unavailable");
        var resolver = (ICharacterCareerMartialAcquisitionSourceContext)context;
        var identities = new HashSet<Guid>();
        bool matched = false;
        XElement Definition(XElement owned, bool art)
        {
            Need(identities.Add(GuidValue(owned, "guid")), "martial_acquisition_requirement_martial_ambiguous");
            var id = GuidValue(owned, "sourceid");
            Need(resolver.TryResolveCareerMartialAcquisitionSource(id, art ? CharacterCareerMartialAcquisitionKind.Art
                : CharacterCareerMartialAcquisitionKind.Technique, out var resolved) && resolved is not null
                && !string.IsNullOrWhiteSpace(resolved.AuthorityState),
                "martial_acquisition_requirement_martial_sources_unavailable");
            var definition = Parse(resolved!.Xml, art ? "martialart" : "technique");
            ValidateMartialSource(definition, id, art);
            bool legacy = owned.Name == "martialartadvantage";
            Need(Scalar(owned, "name") == Scalar(definition, "name")
                && Scalar(owned, "source", legacy ? Scalar(definition, "source") : null) == Scalar(definition, "source")
                && Scalar(owned, "page", legacy ? Scalar(definition, "page") : null) == Scalar(definition, "page")
                && (!art || !Flag(owned, "isquality", false)),
                "martial_acquisition_requirement_martial_source_mismatch");
            inputs.Add(resolved.AuthorityState);
            inputs.Add(resolved.Xml);
            return definition;
        }
        foreach (var art in MartialArts(character))
        {
            var definition = Definition(art, true);
            if (node.Name == "martialart") matched |= RequirementIdentity(definition, node.Value);
            else
            {
                var techniques = Techniques(art, definition, resolver, out string legacyAuthority);
                inputs.Add(legacyAuthority);
                foreach (var technique in techniques)
                    matched |= RequirementIdentity(Definition(technique, false), node.Value);
            }
        }
        return matched;
    }

    // SelectionShared.cs:3014 uses TotalBaseRating (not dice pool or skillsoft
    // rating). Reuse the Career free-point and learned-rating projection.
    private static bool SkillRequirement(XElement node, XElement character, XElement settings,
        ICharacterSourceDataContext context, List<string> inputs)
    {
        RequirementFields(node, "name", "id", "val", "spec", "type");
        Need(Node(node, "type") is null, "martial_acquisition_requirement_knowledge_skill_unsupported");
        string name = Scalar(node, "name", ""), idText = Scalar(node, "id", ""), spec = Scalar(node, "spec", "");
        Need((name.Length > 0 || idText.Length > 0) && name.Length <= 512 && name == name.Trim()
            && spec.Length <= 512 && (idText.Length == 0 || Guid.TryParse(idText, out var parsed) && parsed != Guid.Empty
                && string.Equals(parsed.ToString("D"), idText, StringComparison.OrdinalIgnoreCase)),
            "martial_acquisition_requirements_unsupported");
        int target = RequirementNumber(node, "val", "0");
        var newSkills = Box(character, "newskills");
        var skills = Box(newSkills, "skills");
        Need(skills.Elements().All(x => x.Name == "skill") && skills.Elements().Count() <= 4096,
            "martial_acquisition_requirement_skill_ambiguous");
        var rows = skills.Elements().ToArray();
        Need(rows.Select(x => GuidValue(x, "guid")).Distinct().Count() == rows.Length,
            "martial_acquisition_requirement_skill_ambiguous");
        // Validate all identities before selecting: duplicate names/IDs must not
        // turn a forbidden requirement into a false result or pick an arbitrary row.
        var resolvedRows = rows.Select(saved =>
        {
            var id = GuidValue(saved, "suid");
            Need(!Flag(saved, "isknowledge"),
                "martial_acquisition_requirement_skill_source_unavailable");
            Need(context.TryResolveActiveSkillSource(id.ToString("D"), out var exact)
                && Guid.TryParse(exact.SourceSkillId, out var sourceId) && sourceId == id
                && Scalar(saved, "name") == exact.Name
                && Scalar(saved, "skillcategory", exact.SkillCategory) == exact.SkillCategory,
                "martial_acquisition_requirement_skill_source_mismatch");
            var definition = Parse(exact.RawSourceXml, "skill");
            Need(GuidValue(definition, "id") == id && Scalar(definition, "name") == exact.Name,
                "martial_acquisition_requirement_skill_source_mismatch");
            inputs.Add(exact.RawSourceXml);
            return (Saved: saved, Id: id, Source: exact);
        }).ToArray();
        var matches = resolvedRows.Where(x => idText.Length > 0 ? x.Id == Guid.Parse(idText) : x.Source.Name == name).ToArray();
        Need(matches.Length == 1, "martial_acquisition_requirement_skill_ambiguous_or_missing");
        var match = matches[0];
        Need(!match.Source.IsExotic, "martial_acquisition_requirement_exotic_skill_unsupported");
        Need(name.Length == 0 || name == match.Source.Name, "martial_acquisition_requirement_skill_source_mismatch");
        Need(resolvedRows.Count(x => x.Id == match.Id || x.Source.Name == match.Source.Name) == 1,
            "martial_acquisition_requirement_skill_ambiguous_or_missing");
        var improvements = RequirementImprovements(character);
        Need(!SpecializationRatingProjection.HasUnsupportedRatingAuthority(improvements, match.Source, match.Source.Name),
            "martial_acquisition_requirement_skill_rating_unsupported");
        Need(SpecializationRatingProjection.TryReadPoints(match.Saved, improvements, match.Source.Name, false, out var points),
            "martial_acquisition_requirement_skill_rating_unsupported");
        if (match.Source.SkillGroup.Length != 0)
        {
            var groups = Box(newSkills, "groups");
            Need(groups.Elements().All(x => x.Name == "group"), "martial_acquisition_requirement_skill_group_unsupported");
            var group = groups.Elements().Where(x => Scalar(x, "name") == match.Source.SkillGroup).ToArray();
            Need(group.Length <= 1, "martial_acquisition_requirement_skill_group_unsupported");
            Need(SpecializationRatingProjection.TryReadPoints(group.SingleOrDefault(), improvements, match.Source.SkillGroup, true, out var gp)
                && gp.BasePoints == 0 && gp.KarmaPoints == 0 && gp.FreeBase == 0 && gp.FreeLevels == 0,
                "martial_acquisition_requirement_skill_group_unsupported");
        }
        int maximum = RequirementNumber(settings, "maxskillrating", "12");
        Need(CharacterCareerSkillRatingRules.TryProjectSkill(points, null, false, maximum, out var rating),
            "martial_acquisition_requirement_skill_rating_unsupported");
        if (spec.Length == 0) return rating.TotalBaseRating >= target;
        // Specialization eligibility also depends on enabled movement/magic state.
        // Those projections remain unsupported, rather than accepting a saved label.
        Need(!match.Source.RequiresGroundMovement && !match.Source.RequiresSwimMovement && !match.Source.RequiresFlyMovement
            && match.Source.DefaultAttribute is not ("MAG" or "MAGAdept" or "RES" or "DEP"),
            "martial_acquisition_requirement_skill_specialization_unsupported");
        var specs = Box(match.Saved, "specs");
        Need(specs.Elements().All(x => x.Name == "spec")
            && specs.Elements().Select(x => Scalar(x, "name")).Distinct(StringComparer.Ordinal).Count() == specs.Elements().Count(),
            "martial_acquisition_requirement_skill_specialization_unsupported");
        bool hasSpec = specs.Elements().Any(x => Scalar(x, "name") == spec);
        // Legacy HasSpecialization also compares translated display names. Without
        // that authority, a nonempty unmatched set is unknown, not a false forbidden
        // predicate. An empty set and an exact stored match are unambiguous.
        Need(hasSpec || !specs.HasElements, "martial_acquisition_requirement_skill_specialization_unsupported");
        Need(!Flag(match.Saved, "forcedisabled", false),
            "martial_acquisition_requirement_skill_specialization_unsupported");
        foreach (var effect in improvements.Elements())
        {
            if (!RequirementImprovementEnabled(effect)) continue;
            string type = Scalar(effect, "improvementttype", ""), affected = Scalar(effect, "improvedname", "");
            if ((type is "DisableSpecializationEffects" or "BlockSkillSpecializations" or "SkillDisable")
                    && (affected.Length == 0 || affected == match.Source.Name)
                || (type is "BlockSkillCategorySpecializations" or "SkillCategoryDisable")
                    && (affected.Length == 0 || affected == match.Source.SkillCategory))
                throw new InvalidDataException("martial_acquisition_requirement_skill_specialization_unsupported");
        }
        return rating.TotalBaseRating > 0 && rating.TotalBaseRating >= target
            && hasSpec;
    }

    private static XElement RequirementImprovements(XElement character)
    {
        var improvements = Box(character, "improvements");
        Need(improvements.Elements().All(x => x.Name == "improvement" && !x.HasAttributes
            && x.Elements().All(e => e.Name.NamespaceName.Length == 0 && !e.HasAttributes && !e.HasElements)
            && x.Elements().GroupBy(e => e.Name).All(g => g.Count() == 1)),
            "martial_acquisition_requirement_improvements_ambiguous");
        return improvements;
    }

    // Improvement.Save writes enabled as 0/1 (also used by this family's writer).
    private static bool RequirementImprovementEnabled(XElement effect)
        => Scalar(effect, "enabled", "1").Trim().ToLowerInvariant() switch
        {
            "" or "1" or "true" => true,
            "0" or "false" => false,
            _ => throw new InvalidDataException("martial_acquisition_requirement_improvements_ambiguous")
        };

    // SelectionShared.cs:876: <natural/> means Value; absent means TotalValue.
    // Attribute.Core.cs:927,1598: for unmodified standard attributes these share
    // the source-bound metatype minimum + saved base/karma calculation.
    private static bool AttributeRequirement(XElement node, XElement character, XElement metatype)
    {
        RequirementFields(node, "name", "total", "natural");
        string name = Scalar(node, "name");
        Need(new[] { "BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL", "EDG" }.Contains(name, StringComparer.Ordinal),
            "martial_acquisition_requirement_attribute_kind_unsupported");
        int target = RequirementNumber(node, "total", "0");
        Need(new[] { "", Guid.Empty.ToString("D") }.Contains(Scalar(character, "metavariantguid", ""), StringComparer.Ordinal)
            && new[] { "", Guid.Empty.ToString("D") }.Contains(Scalar(character, "metavariantid", ""), StringComparer.Ordinal)
            && (Scalar(character, "metavariant", "None") is "" or "None")
            && Scalar(character, "metatype") == Scalar(metatype, "name")
            && Scalar(metatype, "category") == "Metahuman"
            && !Flag(character, "critter", false) && !Flag(character, "critterenabled", false),
            "martial_acquisition_requirement_attribute_variant_unsupported");
        var attributes = Box(character, "attributes");
        Need(attributes.Elements().All(x => x.Name == "attribute"), "martial_acquisition_requirement_attribute_ambiguous");
        var matches = attributes.Elements().Where(x => Scalar(x, "name") == name).ToArray();
        Need(matches.Length == 1, "martial_acquisition_requirement_attribute_ambiguous");
        var saved = matches[0];
        Need(Scalar(saved, "metatypecategory", "Standard") == "Standard",
            "martial_acquisition_requirement_attribute_variant_unsupported");
        string prefix = name.ToLowerInvariant();
        int min = RequirementNumber(metatype, prefix + "min"), max = RequirementNumber(metatype, prefix + "max"),
            aug = RequirementNumber(metatype, prefix + "aug");
        Need(max >= min && aug >= max && Integer(saved, "metatypemin") == min
            && Integer(saved, "metatypemax") == max && Integer(saved, "metatypeaugmax") == aug,
            "martial_acquisition_requirement_attribute_caps_not_source_bound");
        foreach (var effect in RequirementImprovements(character).Elements())
        {
            if (!RequirementImprovementEnabled(effect)) continue;
            string type = Scalar(effect, "improvementttype", ""), affected = Scalar(effect, "improvedname", "");
            Need(!((type is "Attribute" or "AttributeBase" or "Attributelevel" or "AttributeLevel" or "AttributeMaxClamp" or "ReplaceAttribute")
                && (affected.Length == 0 || affected == name || affected == name + "Base")),
                "martial_acquisition_requirement_attribute_modifiers_unsupported");
        }
        Need(!Box(character, "cyberwares").HasElements && !Box(character, "biowares").HasElements
            && !Box(character, "armors").HasElements,
            "martial_acquisition_requirement_attribute_modifiers_unsupported");
        int paid = RequirementNumber(saved, "base"), karma = RequirementNumber(saved, "karma");
        int value = Math.Min(checked(Math.Max(paid + min, name == "EDG" || max == 0 ? 0 : 1) + karma), max);
        if (Node(node, "natural") is null)
            Need(Node(saved, "totalvalue") is null || Integer(saved, "totalvalue") == value,
                "martial_acquisition_requirement_attribute_projection_mismatch");
        return value >= target;
    }
    private static XElement[] MartialArts(XElement root)
    {
        var box = Box(root, "martialarts");
        Need(box.Elements().All(x => x.Name == "martialart") && box.Elements().Count() <= 4096,
            "martial_acquisition_owned_collection_invalid");
        var rows = box.Elements().ToArray();
        Need(rows.Select(x => GuidValue(x, "guid")).Distinct().Count() == rows.Length,
            "martial_acquisition_owned_identity_invalid");
        return rows;
    }
    private static XElement[] Techniques(XElement art, XElement parentSource,
        ICharacterCareerMartialAcquisitionSourceContext resolver, out string authority)
    {
        // MartialArt.LoadCoreAsync loads BOTH containers, legacy first (406-459).
        // This is an in-memory identity view only; Apply keeps every original saved node.
        authority = "";
        var legacy = Box(art, "martialartadvantages");
        var modern = Box(art, "martialarttechniques");
        Need(legacy.Elements().All(x => x.Name == "martialartadvantage")
            && modern.Elements().All(x => x.Name == "martialarttechnique")
            && legacy.Nodes().Concat(modern.Nodes()).All(n => n is XElement or XComment || n is XText t && string.IsNullOrWhiteSpace(t.Value))
            && legacy.Elements().Count() + modern.Elements().Count() <= 4096,
            "martial_acquisition_owned_techniques_invalid");
        var rows = new List<XElement>();
        if (legacy.HasElements)
        {
            Need(resolver.TryResolveCareerMartialAcquisitionCatalog(out var catalog) && catalog is not null
                && !string.IsNullOrWhiteSpace(catalog.AuthorityState), "martial_acquisition_legacy_sources_unavailable");
            authority = catalog!.AuthorityState;
            var definitions = catalog.Sources.Where(x => x.Kind == CharacterCareerMartialAcquisitionKind.Technique)
                .Select(x => (Source: x, Row: Parse(x.Xml, "technique"))).ToArray();
            string[] fields = ["sourceid", "guid", "name", "source", "page", "notes", "notesColor"];
            foreach (var saved in legacy.Elements())
            {
                Need(!saved.HasAttributes && saved.Elements().All(x => x.Name.NamespaceName.Length == 0
                    && fields.Contains(x.Name.LocalName, StringComparer.Ordinal) && !x.HasAttributes && !x.HasElements)
                    && saved.Elements().GroupBy(x => x.Name).All(g => g.Count() == 1)
                    && saved.Nodes().All(n => n is XElement or XComment || n is XText t && string.IsNullOrWhiteSpace(t.Value)),
                    "martial_acquisition_legacy_shape_unsupported");
                _ = GuidValue(saved, "guid"); // Never mint identity for imported records.
                string name = Scalar(saved, "name");
                Need(name.Length is > 0 and <= 512, "martial_acquisition_legacy_identity_invalid");
                var sourceId = Node(saved, "sourceid");
                // Name fallback also needs a saved book: an enabled-only catalog cannot
                // disambiguate a name from an unidentified, disabled-book legacy row.
                Need(sourceId is not null || !string.IsNullOrWhiteSpace(Scalar(saved, "source", "")),
                    "martial_acquisition_legacy_source_ambiguous_or_missing");
                // Canonical loader can resolve a missing source ID by name. Ambiguity is a blocker here.
                var matches = definitions.Where(x => sourceId is null
                    ? Scalar(x.Row, "name") == name : x.Source.SourceId == GuidValue(saved, "sourceid")).ToArray();
                Need(matches.Length == 1, "martial_acquisition_legacy_source_ambiguous_or_missing");
                var match = matches[0];
                ValidateMartialSource(match.Row, match.Source.SourceId, false);
                Need(Scalar(match.Row, "name") == name
                    && (Node(saved, "source") is null || Scalar(saved, "source") == Scalar(match.Row, "source"))
                    && (Node(saved, "page") is null || Scalar(saved, "page") == Scalar(match.Row, "page"))
                    && IsTechniqueMember(parentSource, name),
                    "martial_acquisition_legacy_source_mismatch");
                var view = new XElement(saved);
                if (sourceId is null) view.Add(new XElement("sourceid", match.Source.SourceId.ToString("D")));
                rows.Add(view);
            }
        }
        rows.AddRange(modern.Elements());
        Need(rows.Select(x => GuidValue(x, "guid")).Distinct().Count() == rows.Count
            && rows.Select(x => GuidValue(x, "sourceid")).Distinct().Count() == rows.Count
            && rows.Select(x => Scalar(x, "name")).Distinct(StringComparer.Ordinal).Count() == rows.Count,
            "martial_acquisition_owned_techniques_ambiguous");
        return rows.ToArray();
    }
}
