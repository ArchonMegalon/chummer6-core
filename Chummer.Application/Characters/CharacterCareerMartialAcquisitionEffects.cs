using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerMartialAcquisitionTransaction
{
    private static IReadOnlyList<CharacterCareerMartialAcquisitionEffect> MartialEffects(XElement root, XElement source,
        XElement settings, ICharacterSourceDataContext context, CharacterCareerMartialAcquisitionRequest request,
        out string authority)
        => MartialEffects(root, source, settings, context, request.Kind, (ordinal, _, _) =>
        {
            Guid id = InstanceId(request, "specialization/" + ordinal);
            Need(!root.Descendants("guid").Any(x => Guid.TryParse(x.Value, out var existing) && existing == id),
                "martial_acquisition_specialization_identity_conflict");
            return id;
        }, out authority);

    private static IReadOnlyList<CharacterCareerMartialAcquisitionEffect> MartialEffects(XElement root, XElement source,
        XElement settings, ICharacterSourceDataContext context, CharacterCareerMartialAcquisitionKind kind,
        Func<int, XElement, string, Guid> specializationId, out string authority)
    {
        authority = "";
        var result = new List<CharacterCareerMartialAcquisitionEffect>();
        var bonus = Node(source, "bonus");
        if (bonus is null) return result.AsReadOnly();
        Need(!bonus.HasAttributes, "martial_acquisition_bonus_variant_unsupported");
        foreach (var item in bonus.Elements())
        {
            if (item.Name == "unarmedreach" && kind == CharacterCareerMartialAcquisitionKind.Technique)
            {
                Need(!item.HasAttributes && !item.HasElements && int.TryParse(item.Value, out int reach),
                    "martial_acquisition_reach_expression_unsupported");
                result.Add(new("UnarmedReach", "", null, "", int.Parse(item.Value, Invariant)));
                continue;
            }
            Need(item.Name == "addskillspecializationoption" && !item.HasAttributes
                && kind == CharacterCareerMartialAcquisitionKind.Art
                && item.Elements().All(x => x.Name == "skill" || x.Name == "skills" || x.Name == "spec"),
                "martial_acquisition_bonus_variant_unsupported");
            var skills = Box(Box(root, "newskills"), "skills");
            Need(Node(root, "newskills") is not null && skills.Elements().All(x => x.Name == "skill"),
                "martial_acquisition_skill_collection_unavailable");
            var names = Node(item, "skills") is { } list
                ? list.Elements().Select(e => { Need(e.Name == "skill" && !e.HasAttributes && !e.HasElements,
                    "martial_acquisition_bonus_variant_unsupported"); return e.Value; }).ToArray()
                : new[] { Scalar(item, "skill") };
            Need(names.Length is > 0 and <= 64 && names.Distinct(StringComparer.Ordinal).Count() == names.Length,
                "martial_acquisition_bonus_variant_unsupported");
            string spec = Scalar(item, "spec");
            Need(spec.Length is > 0 and <= 512, "martial_acquisition_specialization_invalid");
            foreach (string name in names)
            {
                var matches = skills.Elements("skill").Where(s => Scalar(s, "name", "") == name
                    && !Flag(s, "isknowledge", false)).ToArray();
                Need(matches.Length <= 1, "martial_acquisition_skill_ambiguous");
                if (matches.Length == 0) continue; // canonical GetActiveSkill null means no improvement.
                var skill = matches[0];
                Guid skillId = GuidValue(skill, "guid"), sourceId = GuidValue(skill, "suid");
                Need(context.TryResolveActiveSkillSource(sourceId.ToString("D"), out var canonical)
                    && canonical.Name == name && !canonical.IsExotic
                    && Guid.TryParse(canonical.SourceSkillId, out var resolvedSkillId) && resolvedSkillId == sourceId,
                    "martial_acquisition_skill_source_unavailable");
                authority += "\0" + canonical.RawSourceXml;
                result.Add(new("SkillSpecializationOption", name, skillId, spec, 0));
                if (Flag(settings, "freemartialartspecialization", false))
                {
                    var specs = Box(skill, "specs");
                    Need(specs.Elements().All(x => x.Name == "spec"), "martial_acquisition_specializations_invalid");
                    Guid id = specializationId(result.Count, skill, spec);
                    result.Add(new("SkillSpecialization", name, skillId, spec, 0, id));
                }
            }
        }
        return result.AsReadOnly();
    }

    private static void ApplyEffects(XElement root, CharacterCareerMartialAcquisitionRequest request,
        IReadOnlyList<CharacterCareerMartialAcquisitionEffect> effects)
    {
        string source = request.Kind == CharacterCareerMartialAcquisitionKind.Art ? "MartialArt" : "MartialArtTechnique";
        foreach (var effect in effects)
        {
            AppendNode(root, "improvements", MartialImprovement2438(effect, source, InstanceId(request, "item")));
            if (effect.Type != "SkillSpecialization") continue;
            var skill = Box(Box(root, "newskills"), "skills").Elements("skill").Single(x => GuidValue(x, "guid") == effect.SkillId);
            // Canonical constructor defaults Free=False even for this free-setting acquisition.
            AppendNode(skill, "specs", new XElement("spec", new XElement("guid", effect.SpecializationId!.Value.ToString("D")),
                new XElement("name", effect.Specialization), new XElement("free", "False"), new XElement("expertise", "False")));
        }
    }

    private static XElement MartialImprovement2438(CharacterCareerMartialAcquisitionEffect effect, string source, Guid owner)
    {
        // The writer and current-state consumer must agree on the complete native record.
        var row = new XElement("improvement", new XElement("unique", effect.Type == "SkillSpecialization"
                ? effect.SpecializationId!.Value.ToString("D") : effect.Specialization),
            new XElement("target", ""), new XElement("improvedname", effect.SkillKey),
            new XElement("sourcename", owner.ToString("D")));
        foreach (string field in new[] { "min", "max", "aug", "augmax" }) row.Add(new XElement(field, 0));
        row.Add(new XElement("val", effect.Value), new XElement("rating", 1), new XElement("exclude", ""),
            new XElement("condition", ""), new XElement("improvementttype", effect.Type), new XElement("improvementsource", source),
            new XElement("custom", "False"), new XElement("customname", ""), new XElement("customid", ""),
            new XElement("customgroup", ""), new XElement("addtorating", 0), new XElement("enabled", 1),
            new XElement("order", 0), new XElement("notes", ""), new XElement("notesColor", "#000000"));
        return row;
    }

    // Calculation view only. The original XML remains the capture/binding and write payload.
    // These three native effects do not modify attribute/skill points, Karma prices,
    // lifestyle costs or the bounded weapon cost/damage kernels. Recompile their complete
    // owned source graph before excluding them from those kernels' generic effect readers.
    internal static bool TryProjectCurrentMartialEffects2438(XElement root, ICharacterSourceDataContext context,
        out XElement calculation)
    {
        calculation = root;
        try
        {
            var improvements = Box(root, "improvements");
            Need(improvements.Elements().All(row => row.Name == "improvement"), "martial_current_effect_shape");
            var saved = improvements.Elements().Where(row =>
                Scalar(row, "improvementsource", "") is "MartialArt" or "MartialArtTechnique"
                || Scalar(row, "improvementttype", "") is "SkillSpecializationOption" or "SkillSpecialization" or "UnarmedReach").ToArray();
            var arts = MartialArts(root);
            if (saved.Length == 0 && arts.Length == 0) return true;
            Need(Box(root, "martialarts").Nodes().All(node => node is XElement or XComment
                || node is XText text && string.IsNullOrWhiteSpace(text.Value)), "martial_current_owner_container_shape");
            Need(arts.Select(row => GuidValue(row, "sourceid")).Distinct().Count() == arts.Length
                && arts.Select(row => Scalar(row, "name")).Distinct(StringComparer.Ordinal).Count() == arts.Length,
                "martial_current_art_ambiguous");
            Need(context is ICharacterCareerMartialAcquisitionSourceContext, "martial_current_sources_unavailable");
            var sources = (ICharacterCareerMartialAcquisitionSourceContext)context;
            Need(context.TryResolveCareerSettingsProfile(out var settingsXml, out var settingsState)
                && !string.IsNullOrWhiteSpace(settingsState), "martial_current_settings_unavailable");
            var settings = Parse(settingsXml, "settings");
            var expected = new List<XElement>();
            foreach (var art in arts)
            {
                var definition = Definition(art, true);
                // MartialArt.cs:49,253,338-339 saves the source/default cost even for
                // a quality grant; this scalar is not evidence of a paid art purchase.
                Need(Flag(art, "isquality", false) == Flag(definition, "isquality", false) && Integer(art, "cost") ==
                    (Node(definition, "cost") is null ? 7 : Integer(definition, "cost")), "martial_current_art_changed");
                AddEffects(art, definition, true);
                var techniques = Techniques(art, definition, sources, out _);
                Need(Node(definition, "alltechniques") is null || techniques.Length <= 1, "martial_current_parent_changed");
                foreach (var technique in techniques)
                {
                    var techniqueSource = Definition(technique, false);
                    Need(IsTechniqueMember(definition, Scalar(techniqueSource, "name")), "martial_current_parent_changed");
                    AddEffects(technique, techniqueSource, false);
                }
            }
            // Multiset comparison rejects missing, duplicate, forged, disabled and malformed
            // native records; a type tag or an owned GUID alone is never effect authority.
            var remaining = expected.ToList();
            foreach (var row in saved)
            {
                int index = remaining.FindIndex(candidate => XNode.DeepEquals(row, candidate));
                Need(index >= 0, "martial_current_effect_changed");
                remaining.RemoveAt(index);
            }
            Need(remaining.Count == 0, "martial_current_effect_missing");
            calculation = new XElement(root);
            calculation.Elements("martialarts").Remove();
            foreach (var row in calculation.Elements("improvements").Elements().Where(row =>
                Scalar(row, "improvementsource", "") is "MartialArt" or "MartialArtTechnique"
                || Scalar(row, "improvementttype", "") is "SkillSpecializationOption" or "SkillSpecialization" or "UnarmedReach").ToArray())
                row.Remove();
            return true;

            XElement Definition(XElement owned, bool art)
            {
                string[] fields = art
                    ? ["name", "sourceid", "guid", "source", "page", "cost", "isquality", "notes", "notesColor", "martialarttechniques", "martialartadvantages"]
                    : ["name", "sourceid", "guid", "source", "page", "notes", "notesColor"];
                Need(!owned.HasAttributes && owned.Elements().All(field => field.Name.Namespace == XNamespace.None
                        && fields.Contains(field.Name.LocalName, StringComparer.Ordinal) && !field.HasAttributes
                        && (!field.HasElements || art && (field.Name.LocalName is "martialarttechniques" or "martialartadvantages")))
                    && owned.Elements().GroupBy(field => field.Name).All(group => group.Count() == 1)
                    && owned.Nodes().All(node => node is XElement or XComment || node is XText text && string.IsNullOrWhiteSpace(text.Value)),
                    "martial_current_owner_shape");
                Guid instance = GuidValue(owned, "guid"), id = GuidValue(owned, "sourceid");
                Need(root.Descendants("guid").Count(row => Guid.TryParse(row.Value, out var value) && value == instance) == 1,
                    "martial_current_owner_ambiguous");
                Need(sources.TryResolveCareerMartialAcquisitionSource(id, art ? CharacterCareerMartialAcquisitionKind.Art
                    : CharacterCareerMartialAcquisitionKind.Technique, out var evidence)
                    && evidence is not null && !string.IsNullOrWhiteSpace(evidence.AuthorityState), "martial_current_source_unavailable");
                var definition = Parse(evidence!.Xml, art ? "martialart" : "technique");
                ValidateMartialSource(definition, id, art, ownedParent: art);
                Need(new[] { "name", "source", "page" }.All(field => Scalar(owned, field) == Scalar(definition, field)),
                    "martial_current_owner_source_changed");
                return definition;
            }

            void AddEffects(XElement owned, XElement definition, bool art)
            {
                var effects = MartialEffects(root, definition, settings, context, art ? CharacterCareerMartialAcquisitionKind.Art
                    : CharacterCareerMartialAcquisitionKind.Technique, (_, skill, name) =>
                    {
                        var matches = Box(skill, "specs").Elements("spec").Where(row => Scalar(row, "name") == name).ToArray();
                        Need(matches.Length == 1, "martial_current_specialization_ambiguous");
                        Guid id = GuidValue(matches[0], "guid");
                        Need(root.Descendants("guid").Count(row => Guid.TryParse(row.Value, out var value) && value == id) == 1
                            && XNode.DeepEquals(matches[0], new XElement("spec", new XElement("guid", id.ToString("D")),
                                new XElement("name", name), new XElement("free", "False"), new XElement("expertise", "False"))),
                            "martial_current_specialization_changed");
                        return id;
                    }, out _);
                Need(effects.Count <= 256, "martial_current_effect_bound");
                expected.AddRange(effects.Select(effect => MartialImprovement2438(effect,
                    art ? "MartialArt" : "MartialArtTechnique", GuidValue(owned, "guid"))));
            }
        }
        catch (Exception error) when (Malformed(error) || error is IOException or UnauthorizedAccessException) { return false; }
    }
}
