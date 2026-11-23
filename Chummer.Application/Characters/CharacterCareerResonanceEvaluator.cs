using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Application.Characters.CharacterCareerComplexFormLearningTransaction;

namespace Chummer.Application.Characters;

public sealed record CharacterCareerResonanceEvaluation(bool IsTechnomancer, int EffectiveResonance,
    decimal LimitModifier, IReadOnlyList<CharacterCareerComplexFormLearningCostEffect> CostEffects,
    string SourceDigest, IReadOnlyList<string> Blockers)
{
    public bool IsSupported => Blockers.Count == 0;
}

/// <summary>Bounded source-authenticated RES evaluation. Never promotes cached flags or totals to authority.</summary>
public static partial class CharacterCareerResonanceEvaluator
{
    private sealed record Expected(Guid Owner, string Type, string Target, string Unique, decimal Value, int Rating, string Condition = "");
    private static readonly HashSet<string> EffectFields = new(StringComparer.Ordinal)
    {
        "unique", "target", "improvedname", "sourcename", "min", "max", "aug", "augmax", "val", "rating", "exclude",
        "condition", "improvementttype", "improvementsource", "custom", "customname", "customid", "customgroup",
        "addtorating", "enabled", "order", "notes", "notesColor"
    };

    public static CharacterCareerResonanceEvaluation Evaluate(XElement root, ICharacterSourceDataContext context)
    {
        try
        {
            Need(context is ICharacterCareerComplexFormLearningSourceContext, "resonance_source_capability_unavailable");
            Need(((ICharacterCareerComplexFormLearningSourceContext)context).TryResolveCareerResonanceSource(out var source)
                && source is not null && !string.IsNullOrWhiteSpace(source.AuthorityState), "resonance_source_unavailable");
            Need(Flag(root, "created") && Flag(root, "resenabled") && Flag(root, "technomancer"), "resonance_requires_technomancer_career");
            Need(!Flag(root, "magenabled", false) && !Flag(root, "depenabled", false)
                && !Flag(root, "critter", false) && !Flag(root, "critterenabled", false) && !Flag(root, "iscritter", false)
                && Number(root, "initiategrade", 0) == 0
                && !Box(root, "cyberwares").HasElements && !Box(root, "biowares").HasElements
                && Number(root, "essencehole", 0) == 0 && Number(root, "essencepenalty", 0) == 0
                && Number(root, "specialattburn", 0) == 0,
                "resonance_implanted_or_alternate_context_unsupported");
            int submersionGrade = ValidateSubmersionState(root);
            string? echoDigest = AuthenticateEchoHistory(root, context, submersionGrade);
            var metatype = Parse(source!.MetatypeXml, "metatype");
            Need(MetatypeIdentity(root) == Scalar(metatype, "id") && Scalar(root, "metatype") == Scalar(metatype, "name")
                && Scalar(metatype, "category") == "Metahuman" && Scalar(root, "metatypecategory") == "Metahuman"
                && NoVariant(root)
                && !Box(metatype, "bonus").HasElements, "resonance_metatype_unsupported");
            var qualities = Box(root, "qualities").Elements().ToArray();
            Need(qualities.Length == source.Qualities.Count && qualities.All(q => q.Name == "quality"), "resonance_quality_identity_invalid");
            var expected = new List<Expected>();
            var ids = new HashSet<Guid>();
            int talents = 0;
            foreach (var entry in source.Qualities)
            {
                Need(entry.InstanceId != Guid.Empty && ids.Add(entry.InstanceId), "resonance_quality_identity_invalid");
                var owned = qualities.Single(q => Scalar(q, "guid") == entry.InstanceId.ToString("D"));
                var quality = Parse(entry.QualityXml, "quality");
                Need(Scalar(quality, "id") == entry.SourceId.ToString("D") && Scalar(owned, "sourceid") == entry.SourceId.ToString("D")
                    && Scalar(owned, "name") == Scalar(quality, "name") && Scalar(owned, "source") == Scalar(quality, "source")
                    && Scalar(owned, "extra", "").Length == 0, "resonance_quality_binding_invalid");
                Need(!Box(quality, "firstlevelbonus").HasElements && !Box(owned, "firstlevelbonus").HasElements
                    && !Box(owned, "naturalweapons").HasElements, "resonance_quality_effects_unsupported");
                var bonus = Box(quality, "bonus");
                // A persisted bonus is optional input, never the authority. If present it must agree.
                if (Node(owned, "bonus") is { } savedBonus)
                    Need(XNode.DeepEquals(Normalize(savedBonus), Normalize(bonus)), "resonance_saved_bonus_changed");
                bool enablesRes = false, enablesTab = false, enablesSkills = false;
                foreach (var effect in bonus.Elements())
                {
                    Need(!effect.HasAttributes, "resonance_conditional_or_unique_source_unsupported");
                    string tag = effect.Name.LocalName;
                    Need(effect.Name.NamespaceName.Length == 0, "resonance_source_shape_invalid");
                    switch (tag)
                    {
                        case "enableattribute":
                            Need(Only(effect, "name") && Scalar(effect, "name") == "RES", "resonance_attribute_effect_unsupported");
                            enablesRes = true;
                            expected.Add(new(entry.InstanceId, "Attribute", "RES", "enableattribute", 0, 0));
                            break;
                        case "enabletab":
                            Need(Only(effect, "name") && Scalar(effect, "name").Equals("technomancer", StringComparison.OrdinalIgnoreCase),
                                "resonance_tab_effect_unsupported");
                            enablesTab = true;
                            expected.Add(new(entry.InstanceId, "SpecialTab", "Technomancer", "enabletab", 0, 0));
                            break;
                        case "unlockskills":
                            Need(!effect.HasElements && effect.Value == "Technomancer", "resonance_skill_authority_unsupported");
                            enablesSkills = true;
                            expected.Add(new(entry.InstanceId, "SpecialSkills", "Technomancer", "", 0, 1));
                            break;
                        case "newcomplexformkarmacost":
                        case "newcomplexformkarmacostmultiplier":
                        case "complexformlimit":
                            Need(!effect.HasElements && decimal.TryParse(effect.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                CultureInfo.InvariantCulture, out _), "resonance_cost_expression_unsupported");
                            decimal value = decimal.Parse(effect.Value, CultureInfo.InvariantCulture);
                            string type = tag == "complexformlimit" ? "ComplexFormLimit" : tag == "newcomplexformkarmacost"
                                ? "NewComplexFormKarmaCost" : "NewComplexFormKarmaCostMultiplier";
                            expected.Add(new(entry.InstanceId, type, "", "", value, 1));
                            break;
                        case "addgear":
                            Need(Only(effect, "name", "category") && Scalar(effect, "name") == "Living Persona"
                                && Scalar(effect, "category") == "Commlinks", "resonance_gear_effect_unsupported");
                            break;
                        case "specificskill":
                            Need(Only(effect, "name", "bonus", "condition") && Scalar(effect, "name") == "Computer"
                                && Number(effect, "bonus") == 2 && Scalar(effect, "condition") == "Matrix Perception",
                                "resonance_quality_effects_unsupported");
                            expected.Add(new(entry.InstanceId, "Skill", "Computer", "", 2, 1, "Matrix Perception"));
                            break;
                        default: throw new InvalidDataException("resonance_quality_effects_unsupported");
                    }
                }
                if (enablesRes && enablesTab && enablesSkills) talents++;
                else Need(!enablesRes && !enablesTab && !enablesSkills, "resonance_talent_incomplete");
            }
            Need(talents == 1, "resonance_source_technomancer_required");
            var remaining = expected.ToList();
            foreach (var effect in Box(root, "improvements").Elements())
            {
                Need(effect.Name == "improvement" && !effect.HasAttributes && effect.Elements().All(e => EffectFields.Contains(e.Name.LocalName)),
                    "resonance_effect_shape_unsupported");
                if (Scalar(effect, "improvementsource", "") == "Submersion") continue; // Fully validated above.
                string type = Scalar(effect, "improvementttype");
                // Native addgear creates a Gear reference; it does not alter RES, learning cost or the count cap.
                if (type == "Gear") continue;
                Need(Flag(effect, "enabled"), "resonance_disabled_effect_unsupported");
                foreach (string field in new[] { "min", "max", "aug", "augmax", "order", "addtorating" })
                    Need(Number(effect, field, 0) == 0, "resonance_effect_modifiers_unsupported");
                foreach (string field in new[] { "target", "exclude", "customname", "customid", "customgroup" })
                    Need(Scalar(effect, field, "").Length == 0, "resonance_effect_modifiers_unsupported");
                Need(!Flag(effect, "custom", false) && Scalar(effect, "improvementsource") == "Quality"
                    && Guid.TryParse(Scalar(effect, "sourcename"), out _), "resonance_effect_source_unavailable");
                var key = new Expected(Guid.Parse(Scalar(effect, "sourcename")), type, Scalar(effect, "improvedname", ""),
                    Scalar(effect, "unique", ""), Number(effect, "val"), Integer(effect, "rating"), Scalar(effect, "condition", ""));
                Need(remaining.Remove(key), "resonance_effect_not_source_bound");
            }
            Need(remaining.Count == 0, "resonance_source_effect_missing");
            var attributes = Box(root, "attributes").Elements().ToArray();
            Need(attributes.All(a => a.Name == "attribute"), "resonance_attribute_identity_invalid");
            var res = attributes.Single(a => Scalar(a, "name") == "RES");
            Need(Scalar(res, "metatypecategory", "Standard") == "Standard", "resonance_alternate_attribute_unsupported");
            int min = Integer(metatype, "resmin"), max = Integer(metatype, "resmax"), aug = Integer(metatype, "resaug");
            Need(min >= 0 && max >= min && max <= 1000 && aug >= max && aug <= 2000
                && Integer(res, "metatypemin") == min && Integer(res, "metatypemax") == max && Integer(res, "metatypeaugmax") == aug,
                "resonance_attribute_caps_not_source_bound");
            int paid = Integer(res, "base"), karma = Integer(res, "karma");
            // Attribute.Core TotalBase/Value/CalculatedTotalValue: only the authenticated Submersion maximum modifier is admitted.
            Need(paid >= 0 && karma >= 0, "resonance_attribute_allocation_invalid");
            int allocated = checked(Math.Max(paid + min, min) + karma);
            int totalMaximum = checked(max + submersionGrade);
            int totalAugmented = checked(aug + submersionGrade);
            Need(allocated <= totalMaximum, "resonance_attribute_overallocated");
            int effective = Math.Max(0, Math.Min(allocated, Math.Min(totalMaximum, totalAugmented)));
            Need(effective > 0 && (Node(res, "totalvalue") is null || Integer(res, "totalvalue") == effective), "resonance_projection_mismatch");
            var costs = expected.Where(e => e.Type is "NewComplexFormKarmaCost" or "NewComplexFormKarmaCostMultiplier")
                .Select(e => new CharacterCareerComplexFormLearningCostEffect(e.Type, e.Value)).ToArray();
            decimal limit = expected.Where(e => e.Type == "ComplexFormLimit").Sum(e => e.Value);
            return new(true, effective, limit, Array.AsReadOnly(costs), Hash(echoDigest is null ? source.AuthorityState : source.AuthorityState + "\0" + echoDigest), Array.Empty<string>());
        }
        catch (InvalidDataException e) { return Failed(e.Message); }
        catch (Exception e) when (e is FormatException or OverflowException or InvalidOperationException or ArgumentException
            or NullReferenceException or System.Xml.XmlException or IOException or UnauthorizedAccessException)
        { return Failed("resonance_state_invalid"); }
    }

    private static bool Only(XElement node, params string[] names) => node.Elements().All(e => names.Contains(e.Name.LocalName)
        && !e.HasElements && !e.HasAttributes && e.Name.NamespaceName.Length == 0)
        && node.Elements().Select(e => e.Name).Distinct().Count() == node.Elements().Count();
    private static XElement Normalize(XElement node) => node.HasElements
        ? new XElement(node.Name, node.Attributes(), node.Elements().Select(Normalize))
        : new XElement(node.Name, node.Attributes(), node.Value);
    private static string MetatypeIdentity(XElement root)
    {
        string id = Scalar(root, "metatypeid", ""), guid = Scalar(root, "metatypeguid", "");
        Need(id.Length == 0 || guid.Length == 0 || id == guid, "resonance_metatype_identity_ambiguous");
        return id.Length != 0 ? id : guid;
    }
    private static bool NoVariant(XElement root)
    {
        if (Scalar(root, "metavariant", "") is not ("" or "None")) return false;
        foreach (string field in new[] { "metavariantid", "metavariantguid" })
        {
            string id = Scalar(root, field, "");
            if (id.Length != 0 && (!Guid.TryParse(id, out var value) || value != Guid.Empty)) return false;
        }
        return true;
    }
    private static CharacterCareerResonanceEvaluation Failed(string blocker)
        => new(false, 0, 0, Array.Empty<CharacterCareerComplexFormLearningCostEffect>(), "", Array.AsReadOnly(new[] { blocker }));
}
