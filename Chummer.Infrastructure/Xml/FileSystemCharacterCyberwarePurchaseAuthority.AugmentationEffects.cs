using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterCyberwarePurchaseAuthority
{
    // Each mapping is the corresponding AddImprovementCollection handler's exact native type.
    private static readonly IReadOnlyDictionary<string, string> BiowareScalarEffects = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["coldarmor"] = "ColdArmor", ["composure"] = "Composure", ["damageresistance"] = "DamageResistance",
        ["decreaseagiresist"] = "DecreaseAGIResist", ["decreasebodresist"] = "DecreaseBODResist", ["decreasecharesist"] = "DecreaseCHAResist",
        ["decreaseintresist"] = "DecreaseINTResist", ["decreaselogresist"] = "DecreaseLOGResist", ["decreaserearesist"] = "DecreaseREAResist",
        ["decreasestrresist"] = "DecreaseSTRResist", ["decreasewilresist"] = "DecreaseWILResist", ["detectionspellresist"] = "DetectionSpellResist",
        ["directmanaspellresist"] = "DirectManaSpellResist", ["dodge"] = "Dodge", ["drainresist"] = "DrainResistance",
        ["electricityarmor"] = "ElectricityArmor", ["fadingresist"] = "FadingResistance", ["fatigueresist"] = "FatigueResist",
        ["firearmor"] = "FireArmor", ["initiative"] = "Initiative", ["judgeintentionsdefense"] = "JudgeIntentionsDefense",
        ["manaillusionresist"] = "ManaIllusionResist", ["memory"] = "Memory", ["mentallimit"] = "MentalLimit",
        ["mentalmanipulationresist"] = "MentalManipulationResist", ["pathogencontactresist"] = "PathogenContactResist",
        ["pathogeninhalationresist"] = "PathogenInhalationResist", ["pathogeningestionresist"] = "PathogenIngestionResist",
        ["pathogeninjectionresist"] = "PathogenInjectionResist", ["physicalcmrecovery"] = "PhysicalCMRecovery", ["physicallimit"] = "PhysicalLimit",
        ["physiologicaladdictionalreadyaddicted"] = "PhysiologicalAddictionAlreadyAddicted", ["physiologicaladdictionfirsttime"] = "PhysiologicalAddictionFirstTime",
        ["psychologicaladdictionalreadyaddicted"] = "PsychologicalAddictionAlreadyAddicted", ["psychologicaladdictionfirsttime"] = "PsychologicalAddictionFirstTime",
        ["radiationresist"] = "RadiationResist", ["sociallimit"] = "SocialLimit", ["stuncmrecovery"] = "StunCMRecovery",
        ["toxincontactresist"] = "ToxinContactResist", ["toxiningestionresist"] = "ToxinIngestionResist",
        ["toxininhalationresist"] = "ToxinInhalationResist", ["toxininjectionresist"] = "ToxinInjectionResist",
        ["unarmeddv"] = "UnarmedDV", ["unarmedreach"] = "UnarmedReach"
    };

    private static readonly HashSet<string> BiowareSourceFields = new(StringComparer.Ordinal)
    {
        "id", "name", "category", "ess", "capacity", "avail", "cost", "source", "page", "rating", "minrating",
        "bonus", "limit", "forcegrade", "isgeneware", "bannedgrades", "required", "forbidden", "notes"
    };

    private static string? BiowareSourceBlocker(XElement row)
    {
        foreach (var field in row.Elements())
            if (!BiowareSourceFields.Contains(field.Name.LocalName) || field.Name.NamespaceName.Length != 0)
                return $"Bioware source field '{field.Name}' requires a separate canonical choice, generated-object, paired, parent, or mount implementation.";
        if (row.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)) return "The Bioware source has ambiguous fields.";
        if (!TryReadRatingBounds(row, out int min, out int max)) return "The Bioware rating bounds require unsupported character-dependent evaluation.";
        foreach (int rating in new[] { min, max })
        {
            if (!CharacterCyberwarePurchaseRules.TryEvaluateSourceExpression(ReadOptionalScalar(row, "cost"), rating, out _))
                return "The Bioware cost expression requires an unsupported source variant or variable price choice.";
            if (!CharacterCyberwarePurchaseRules.TryEvaluateSourceExpression(ReadOptionalScalar(row, "ess"), rating, out _))
                return "The Bioware Essence expression is outside the exact arithmetic interpreter.";
            if (!CharacterCyberwarePurchaseRules.TryEvaluateAvailability(ReadOptionalScalar(row, "avail"), rating, out _))
                return "The Bioware availability expression is outside the exact arithmetic interpreter.";
            if (!TryProjectBiowareEffects(row.Element("bonus"), rating, out _, out string? reason)) return reason;
        }
        string limit = ReadOptionalScalar(row, "limit");
        if (limit.Length != 0 && limit != "False" && (!int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1))
            return $"Bioware limit '{limit}' needs character attribute or limb-count authority.";
        return null;
    }

    private static string? BiowareCharacterAdmission(XElement source, XElement character, int quantity)
    {
        // Native Quality.RefreshSuppressed disables the target quality's improvements.
        // A dormant DisableQuality marker is exact; changing an installed quality stack
        // needs a separate reconciliation and cannot be represented by the marker alone.
        foreach (var disabled in source.Element("bonus")?.Elements("disablequality") ?? [])
            if (character.Element("qualities")?.Elements("quality").Any(q =>
                ReadOptionalScalar(q, "name") == disabled.Value
                || ReadOptionalScalar(q, "sourceid").Equals(disabled.Value, StringComparison.OrdinalIgnoreCase)) == true)
                return $"Bioware would suppress installed quality '{disabled.Value}'; its active improvements require native quality reconciliation.";
        string? prerequisite = EvaluateBiowareRequirementContainer(source.Element("required"), character, out bool required);
        if (prerequisite is not null) return prerequisite;
        if (source.Element("required") is not null && !required) return "The canonical Bioware required condition is not met.";
        string? forbiddenBlocker = EvaluateBiowareRequirementContainer(source.Element("forbidden"), character, out bool forbidden);
        if (forbiddenBlocker is not null) return forbiddenBlocker;
        if (source.Element("forbidden") is not null && forbidden) return "The canonical Bioware forbidden condition is present.";
        string limit = ReadOptionalScalar(source, "limit");
        // SelectionShared.RequirementsMet defaults both Cyberware and Bioware to one.
        if (limit != "False")
        {
            int maximum = limit.Length == 0 ? 1 : int.Parse(limit, CultureInfo.InvariantCulture);
            string id = ReadOptionalScalar(source, "id"), name = ReadOptionalScalar(source, "name");
            int existing = character.Element("cyberwares")?.Descendants("cyberware")
                .Count(w => ReadOptionalScalar(w, "improvementsource") == "Bioware"
                    && (ReadOptionalScalar(w, "sourceid").Equals(id, StringComparison.OrdinalIgnoreCase) || ReadOptionalScalar(w, "name") == name)) ?? 0;
            if ((long)existing + quantity > maximum) return $"The source-defined Bioware limit of {maximum} would be exceeded.";
        }
        return null;
    }

    private static string? EvaluateBiowareRequirementContainer(XElement? container, XElement character, out bool result)
    {
        result = true;
        if (container is null) return null;
        foreach (XElement group in container.Elements())
        {
            if (group.Name.LocalName is not ("oneof" or "allof") || group.HasAttributes)
                return $"Unsupported Bioware admission group '{group.Name}'.";
            var values = new List<bool>();
            foreach (XElement condition in group.Elements())
            {
                if (condition.HasElements || condition.HasAttributes)
                    return $"Bioware admission '{condition.Name}' requires count/selection variant authority.";
                string target = condition.Value;
                bool matches;
                switch (condition.Name.LocalName)
                {
                    case "bioware":
                    case "cyberware":
                        string kind = condition.Name.LocalName == "bioware" ? "Bioware" : "Cyberware";
                        matches = character.Element("cyberwares")?.Descendants("cyberware").Any(w =>
                            ReadOptionalScalar(w, "improvementsource") == kind && ReadOptionalScalar(w, "plugsintomodularmount").Length == 0
                            && (ReadOptionalScalar(w, "name") == target || ReadOptionalScalar(w, "sourceid").Equals(target, StringComparison.OrdinalIgnoreCase))) == true;
                        break;
                    case "quality":
                        matches = character.Element("qualities")?.Elements("quality").Any(q => ReadOptionalScalar(q, "name") == target
                            || ReadOptionalScalar(q, "sourceid").Equals(target, StringComparison.OrdinalIgnoreCase)) == true;
                        break;
                    case "metatype":
                        if (!TryReadScalar(character, "metatype", out string metatype)) return "Saved metatype identity is unavailable for Bioware admission.";
                        matches = metatype == target;
                        break;
                    default: return $"Unsupported Bioware admission predicate '{condition.Name}'.";
                }
                values.Add(matches);
            }
            if (values.Count == 0) return "An empty Bioware admission group has no exact authority.";
            result &= group.Name.LocalName == "oneof" ? values.Any(v => v) : values.All(v => v);
        }
        return null;
    }

    private static bool TryProjectBiowareEffects(XElement? bonus, int rating,
        out IReadOnlyList<CharacterAugmentationPurchaseEffect> effects, out string? blocker)
    {
        var projected = new List<CharacterAugmentationPurchaseEffect>();
        effects = projected;
        blocker = null;
        if (bonus is null) return true;
        if (bonus.Attributes().Any(a => a.Name != "unique")) { blocker = "Unsupported Bioware bonus attributes."; return false; }
        string unique = (string?)bonus.Attribute("unique") ?? string.Empty;
        try
        {
            foreach (XElement node in bonus.Elements())
            {
                string tag = node.Name.LocalName;
                string useUnique = (string?)node.Attribute("precedence") is string precedence ? "precedence" + precedence : unique;
                if (BiowareScalarEffects.TryGetValue(tag, out string? type))
                {
                    if (node.HasElements || node.HasAttributes) throw new InvalidOperationException($"Unsupported {tag} effect variant.");
                    projected.Add(new(type, string.Empty, EffectNumber(node.Value, rating), UniqueName: unique));
                }
                else switch (tag)
                {
                    case "specificattribute":
                        RequireEffectShape(node, ["name", "min", "max", "val", "aug"], ["precedence"]);
                        string attribute = ReadOptionalScalar(node, "name");
                        if (attribute is not ("BOD" or "AGI" or "REA" or "STR" or "CHA" or "INT" or "LOG" or "WIL"))
                            throw new InvalidOperationException("Special or unknown attribute bonus requires additional effect authority.");
                        projected.Add(new("Attribute", attribute, 0m, EffectInteger(ReadOptionalScalar(node, "min"), rating),
                            EffectInteger(ReadOptionalScalar(node, "max"), rating), EffectNumber(ReadOptionalScalar(node, "val"), rating),
                            EffectInteger(ReadOptionalScalar(node, "aug"), rating), UniqueName: useUnique));
                        break;
                    case "specificskill":
                    case "skillcategory":
                    case "skillgroup":
                    case "skillattribute":
                    case "skilllinkedattribute":
                        RequireEffectShape(node, ["name", "bonus", "condition", "exclude", "applytorating"], ["precedence"]);
                        string skillType = tag switch { "specificskill" => "Skill", "skillcategory" => "SkillCategory", "skillgroup" => "SkillGroup",
                            "skillattribute" => "SkillAttribute", _ => "SkillLinkedAttribute" };
                        projected.Add(new(skillType, ReadOptionalScalar(node, "name"), EffectNumber(ReadOptionalScalar(node, "bonus"), rating),
                            UniqueName: useUnique, Condition: ReadOptionalScalar(node, "condition"), Exclude: ReadOptionalScalar(node, "exclude"),
                            AddToRating: ReadOptionalScalar(node, "applytorating") == "True" ? 1 : 0));
                        break;
                    case "armor":
                        RequireEffectShape(node, [], ["precedence", "group"]);
                        projected.Add(new("Armor", string.Empty, EffectNumber(node.Value, rating),
                            UniqueName: node.Attribute("precedence") is not null ? useUnique : node.Attribute("group") is XAttribute group ? "group" + group.Value : unique));
                        break;
                    case "initiativepass":
                        RequireEffectShape(node, [], ["precedence"]);
                        projected.Add(new("InitiativeDice", string.Empty, EffectNumber(node.Value, rating),
                            UniqueName: node.Attribute("precedence") is null ? "initiativepass" : useUnique));
                        break;
                    case "lifestylecost":
                        RequireEffectShape(node, [], ["lifestyle", "condition"]);
                        projected.Add(new("LifestyleCost", (string?)node.Attribute("lifestyle") ?? string.Empty, EffectNumber(node.Value, rating),
                            UniqueName: unique, Condition: (string?)node.Attribute("condition") ?? string.Empty));
                        break;
                    case "conditionmonitor":
                        RequireEffectShape(node, ["sharedthresholdoffset"], []);
                        projected.Add(new("CMSharedThresholdOffset", string.Empty, EffectNumber(ReadOptionalScalar(node, "sharedthresholdoffset"), rating), UniqueName: unique));
                        break;
                    case "limitmodifier":
                        RequireEffectShape(node, ["limit", "value", "condition"], []);
                        projected.Add(new("LimitModifier", ReadOptionalScalar(node, "limit"), EffectInteger(ReadOptionalScalar(node, "value"), rating),
                            Rating: 0, UniqueName: unique, Condition: ReadOptionalScalar(node, "condition")));
                        break;
                    case "unarmeddvphysical":
                    case "ambidextrous":
                    case "reflexrecorderoptimization":
                        RequireEffectShape(node, [], []);
                        if (!string.IsNullOrWhiteSpace(node.Value)) throw new InvalidOperationException($"Unexpected {tag} effect value.");
                        projected.Add(new(tag switch { "ambidextrous" => "Ambidextrous", "unarmeddvphysical" => "UnarmedDVPhysical", _ => "ReflexRecorderOptimization" },
                            string.Empty, 0m, UniqueName: unique));
                        break;
                    case "attributekarmacost":
                        RequireEffectShape(node, ["name", "val"], []);
                        projected.Add(new("AttributeKarmaCost", ReadOptionalScalar(node, "name"), EffectNumber(ReadOptionalScalar(node, "val"), rating), UniqueName: unique));
                        break;
                    case "disablequality":
                        RequireEffectShape(node, [], []);
                        if (string.IsNullOrWhiteSpace(node.Value)) throw new InvalidOperationException("A DisableQuality effect has no target identity.");
                        projected.Add(new("DisableQuality", node.Value, 0m, UniqueName: unique));
                        break;
                    default: throw new InvalidOperationException($"Bioware bonus '{tag}' requires a canonical prompt, quality, generated-object, or special-effect implementation.");
                }
            }
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
        {
            effects = [];
            blocker = ex is InvalidOperationException ? ex.Message : "Bioware bonus arithmetic is not exactly supported.";
            return false;
        }
    }

    private static void RequireEffectShape(XElement node, string[] children, string[] attributes)
    {
        if (node.Name.NamespaceName.Length != 0 || node.Attributes().Any(a => !attributes.Contains(a.Name.LocalName, StringComparer.Ordinal))
            || node.Elements().Any(e => !children.Contains(e.Name.LocalName, StringComparer.Ordinal) || e.HasAttributes || e.HasElements)
            || node.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1))
            throw new InvalidOperationException($"Unsupported Bioware bonus variant '{node.Name}'.");
    }

    private static decimal EffectNumber(string expression, int rating)
    {
        if (expression.Length == 0) return 0m;
        if (decimal.TryParse(expression, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal scalar)) return scalar;
        if (CharacterCyberwarePurchaseRules.TryEvaluateSourceExpression(expression, rating, out decimal result)) return result;
        throw new InvalidOperationException($"Unsupported Bioware bonus expression '{expression}'.");
    }
    private static int EffectInteger(string expression, int rating) => CharacterCyberwarePurchaseRules.StandardRound(EffectNumber(expression, rating));

    private static void MaterializeBiowareEffects(XElement root, string name, Guid instanceId, IEnumerable<CharacterAugmentationPurchaseEffect> effects)
    {
        int index = 0;
        foreach (var effect in effects)
        {
            var savedEffect = effect;
            if (effect.Type == "LimitModifier")
            {
                byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes($"bioware-limit/v1:{instanceId:D}:{index.ToString(CultureInfo.InvariantCulture)}"));
                Guid limitId = new(digest.AsSpan(0, 16));
                if (ContainsGuid(root, limitId)) throw new InvalidOperationException("Generated limit modifier identity collides with saved state.");
                GetOrCreateUniqueContainer(root, "limitmodifiers").Add(new XElement("limitmodifier",
                    Scalar("guid", limitId.ToString("D")), Scalar("name", name), Scalar("limit", effect.ImprovedName),
                    Scalar("bonus", effect.Value.ToString(CultureInfo.InvariantCulture)), Scalar("condition", effect.Condition),
                    Scalar("candelete", "False"), Scalar("notes", string.Empty), Scalar("notesColor", "Chocolate")));
                savedEffect = effect with { ImprovedName = limitId.ToString("D") };
            }
            GetOrCreateUniqueContainer(root, "improvements").Add(SaveAugmentationImprovement(savedEffect, instanceId.ToString("D")));
            index++;
        }
    }

    private static XElement SaveAugmentationImprovement(CharacterAugmentationPurchaseEffect effect, string sourceName)
        => new("improvement", Scalar("unique", effect.UniqueName), Scalar("target", effect.Target), Scalar("improvedname", effect.ImprovedName),
            Scalar("sourcename", sourceName), Scalar("min", effect.Minimum.ToString(CultureInfo.InvariantCulture)),
            Scalar("max", effect.Maximum.ToString(CultureInfo.InvariantCulture)), Scalar("aug", effect.Augmented.ToString(CultureInfo.InvariantCulture)),
            Scalar("augmax", effect.AugmentedMaximum.ToString(CultureInfo.InvariantCulture)), Scalar("val", effect.Value.ToString(CultureInfo.InvariantCulture)),
            Scalar("rating", effect.Rating.ToString(CultureInfo.InvariantCulture)), Scalar("exclude", effect.Exclude), Scalar("condition", effect.Condition),
            Scalar("improvementttype", effect.Type), Scalar("improvementsource", effect.ImprovementSource), Scalar("custom", "False"),
            Scalar("customname", string.Empty), Scalar("customid", string.Empty), Scalar("customgroup", string.Empty),
            Scalar("addtorating", effect.AddToRating.ToString(CultureInfo.InvariantCulture)), Scalar("enabled", "1"), Scalar("order", "0"),
            Scalar("notes", string.Empty), Scalar("notesColor", "Chocolate"));
}
