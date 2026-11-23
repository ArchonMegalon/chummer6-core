using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

/// <summary>Source instance limits are not rating limits. The effective rating cap
/// is min(source maxlevel(s), current MAG), matching Power.TotalMaximumLevels.</summary>
public static class CharacterCreationAdeptPowerSourceRules
{
    public static bool TryReadMaximumLevels(XElement source, out int maximumLevels, out int savedMaximum)
    {
        maximumLevels = 1;
        savedMaximum = 0;
        if (!Scalar(source, "levels", "False", out string text) || !bool.TryParse(text, out bool levels)
            || source.Elements("maxlevel").Any() && source.Elements("maxlevels").Any()
            || !Scalar(source, source.Element("maxlevel") is null ? "maxlevels" : "maxlevel", "0", out string maximum)
            || !int.TryParse(maximum, NumberStyles.None, CultureInfo.InvariantCulture, out savedMaximum)) return false;
        maximumLevels = levels ? (savedMaximum > 0 ? savedMaximum : int.MaxValue) : 1;
        return true;
    }

    public static int EffectiveMaximumLevels(CharacterCreationMagicResonanceCatalogOption option, int magic) =>
        Math.Max(0, Math.Min(option.MaximumLevels, magic));

    /// <summary>Only linear-cost, prompt-free powers whose complete payload is
    /// consumed by this projection. Way eligibility is metadata; no discount is applied.</summary>
    public static bool IsUndiscountedPayloadSupported(XElement source,
        IReadOnlyList<string>? namedSkillSourceXml = null) =>
        IsPayloadShapeSupported(source)
        && TryReadEffects(source.Element("bonus"), 1, out var effects)
        && NamedSkillSourcesMatch(effects, namedSkillSourceXml);

    private static bool IsPayloadShapeSupported(XElement source)
    {
        string[] fields = ["id", "name", "points", "levels", "limit", "source", "page", "action",
            "adeptway", "adeptwayrequires", "bonus", "maxlevel", "maxlevels"];
        if (source.Name != "power" || source.HasAttributes || HasText(source)
            || source.DescendantNodes().OfType<XProcessingInstruction>().Any()
            || source.DescendantsAndSelf().Any(item => item.Name.Namespace != XNamespace.None)
            || source.Elements().GroupBy(item => item.Name).Any(group => group.Count() != 1)
            || source.Elements().Any(item => !fields.Contains(item.Name.LocalName, StringComparer.Ordinal)
                || item.HasAttributes || item.HasElements && item.Name != "bonus" && item.Name != "adeptwayrequires")
            || !TryReadMaximumLevels(source, out _, out _)
            || !Scalar(source, "limit", "1", out string limitText)
            || !int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out int limit) || limit < 1
            || !Scalar(source, "points", "", out string pointsText)
            || !decimal.TryParse(pointsText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal points)
            || points <= 0 || !Scalar(source, "adeptway", "0", out string wayText)
            || !decimal.TryParse(wayText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal way)
            || way < 0 || way > points
            || !TryReadEffects(source.Element("bonus"), 1, out _)
            || source.Elements("adeptwayrequires").Count() > 1) return false;
        XElement? requirements = source.Element("adeptwayrequires");
        if (requirements is null) return true;
        if (requirements.HasAttributes || HasText(requirements)) return false;
        foreach (var rule in requirements.Elements())
        {
            if (rule.HasAttributes) return false;
            if (rule.Name == "magicianswayforbids")
            {
                if (rule.HasElements || !string.IsNullOrWhiteSpace(rule.Value)) return false;
                continue;
            }
            if (rule.Name != "required" || HasText(rule) || rule.Elements().Count() != 1
                || rule.Element("oneof") is not { } oneOf || oneOf.HasAttributes || HasText(oneOf)
                || !oneOf.HasElements || oneOf.Elements().Any(item => item.Name != "quality"
                    || item.HasAttributes || item.HasElements || string.IsNullOrWhiteSpace(item.Value))) return false;
        }
        return true;
    }

    /// <summary>Compile only source-declared mechanics. Admission and Magic/PP budgets
    /// remain the caller's responsibility. Skill values modify dice pools, never ranks.</summary>
    public static bool TryCompileUndiscountedEffects(XElement source, int levels,
        out IReadOnlyList<CharacterCreationAdeptPowerEffect> effects,
        IReadOnlyList<string>? namedSkillSourceXml = null)
    {
        effects = [];
        return IsUndiscountedPayloadSupported(source, namedSkillSourceXml)
            && TryReadMaximumLevels(source, out int maximum, out _)
            && levels > 0 && levels <= maximum
            && TryReadEffects(source.Element("bonus"), levels, out effects);
    }

    /// <summary>Bind literal names to unique enabled selected sources supplied by
    /// the catalog resolver. This reads source identity only; it never buys ranks
    /// or unlocks magical skills. No partial binding escapes a failed lookup.</summary>
    public static bool TryBindNamedSkillSources(XElement source, Func<string, string?>? resolveNamedSkill,
        out IReadOnlyList<string> namedSkillSourceXml)
    {
        namedSkillSourceXml = [];
        if (!IsPayloadShapeSupported(source) || !TryReadEffects(source.Element("bonus"), 1, out var effects))
            return false;
        var result = new List<string>();
        foreach (var effect in effects.Where(effect => effect.Type == "Skill"))
        {
            string? xml = resolveNamedSkill?.Invoke(effect.Target);
            if (xml is null) return false;
            result.Add(xml);
        }
        if (!NamedSkillSourcesMatch(effects, result)) return false;
        namedSkillSourceXml = result.ToArray();
        return true;
    }

    private static bool NamedSkillSourcesMatch(IReadOnlyList<CharacterCreationAdeptPowerEffect> effects,
        IReadOnlyList<string>? sourceXml)
    {
        var names = effects.Where(effect => effect.Type == "Skill").Select(effect => effect.Target)
            .ToHashSet(StringComparer.Ordinal);
        if ((sourceXml?.Count ?? 0) != names.Count) return false;
        var ids = new HashSet<Guid>();
        foreach (string xml in sourceXml ?? [])
        {
            if (string.IsNullOrWhiteSpace(xml) || xml.Length > 1_048_576) return false;
            try
            {
                using var input = new StringReader(xml);
                using var reader = XmlReader.Create(input, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_048_576 });
                XElement skill = XElement.Load(reader);
                if (skill.Name != "skill" || skill.HasAttributes
                    || !Scalar(skill, "id", "", out string id) || !Guid.TryParse(id, out var sourceId)
                    || sourceId == Guid.Empty || !ids.Add(sourceId)
                    || !Scalar(skill, "name", "", out string name) || !names.Remove(name)
                    || !Scalar(skill, "source", "", out string book) || string.IsNullOrWhiteSpace(book)) return false;
            }
            catch (Exception exception) when (exception is XmlException or InvalidOperationException or ArgumentException)
            { return false; }
        }
        return names.Count == 0;
    }

    private static bool TryReadEffects(XElement? bonus, int levels,
        out IReadOnlyList<CharacterCreationAdeptPowerEffect> effects)
    {
        effects = [];
        if (bonus is null) return true;
        if (bonus.HasAttributes || HasText(bonus)) return false;
        var result = new List<CharacterCreationAdeptPowerEffect>();
        var targets = new HashSet<(string Type, string Target)>();
        foreach (XElement node in bonus.Elements())
        {
            if (node.HasAttributes) return false;
            string type, target = "", expression;
            if (node.Name == "dodge" || node.Name == "armor")
            {
                if (node.HasElements) return false;
                type = node.Name == "dodge" ? "Dodge" : "Armor";
                expression = node.Value;
            }
            else if (node.Name == "specificskill")
            {
                if (HasText(node) || node.Elements().Count() != 2
                    || node.Elements("name").Count() != 1 || node.Elements("bonus").Count() != 1
                    || !Scalar(node, "name", "", out target) || target.Length is 0 or > 1024
                    || target != target.Trim() || !Scalar(node, "bonus", "", out expression)) return false;
                type = "Skill";
            }
            else return false; // Unknown or choice-bearing effects must never become inert grants.
            int value = levels;
            if (expression != "Rating"
                && (!int.TryParse(expression, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value <= 0))
                return false;
            if (!targets.Add((type, target))) return false;
            result.Add(new(type, target, value));
        }
        effects = result.ToArray();
        return true;
    }

    private static bool Scalar(XElement source, string name, string fallback, out string value)
    {
        XElement[] nodes = source.Elements(name).ToArray();
        value = nodes.FirstOrDefault()?.Value ?? fallback;
        return nodes.Length <= 1 && nodes.All(item => !item.HasAttributes && !item.HasElements);
    }
    private static bool HasText(XElement node) => node.Nodes().OfType<XText>().Any(item => !string.IsNullOrWhiteSpace(item.Value));
}

public sealed record CharacterCreationAdeptPowerEffect(string Type, string Target, int Value);
