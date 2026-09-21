using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.Characters;

/// <summary>
/// Source-owned group-to-quality mapping for Life Module QualityLevel
/// contributions. Resolving the highest contribution is not permission to
/// replace an existing quality or choose its instance text.
/// </summary>
internal sealed class CharacterCreationFoundationQualityLevelSourceAuthority
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> _groups;

    private CharacterCreationFoundationQualityLevelSourceAuthority(string sourceDigest,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> groups)
    {
        SourceDigest = sourceDigest;
        _groups = groups;
    }

    public string SourceDigest { get; }

    public static bool TryCreate(string? xml, string? digest,
        out CharacterCreationFoundationQualityLevelSourceAuthority? authority)
    {
        authority = null;
        if (string.IsNullOrWhiteSpace(xml)
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(digest)
            || CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(xml) != digest)
            return false;

        try
        {
            XElement? root = XDocument.Parse(xml).Root;
            if (root?.Name != "chummer" || root.HasAttributes
                || !IsContainer(root)
                || root.Elements().Count() != 1 || root.Element("qualitygroups") is not XElement container
                || container.HasAttributes || !IsContainer(container)
                || container.Elements().Any(node => node.Name != "qualitygroup"))
                return false;

            var groups = new Dictionary<string, IReadOnlyDictionary<int, string>>(StringComparer.Ordinal);
            foreach (XElement group in container.Elements("qualitygroup"))
            {
                if (group.HasAttributes || !IsContainer(group) || group.Elements().Count() != 2
                    || group.Elements("name").Count() != 1 || group.Elements("levels").Count() != 1
                    || !IsLiteral(group.Element("name"))
                    || group.Element("levels") is not XElement levels || levels.HasAttributes || !IsContainer(levels))
                    return false;

                var values = new Dictionary<int, string>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (XElement level in levels.Elements())
                {
                    if (level.Name != "level" || level.HasElements
                        || level.Attributes().Count() != 1 || level.Attribute("value") is not XAttribute value
                        || !int.TryParse(value.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                        || number <= 0 || value.Value != number.ToString(CultureInfo.InvariantCulture)
                        || !IsLiteral(level, allowAttribute: true)
                        || !values.TryAdd(number, level.Value) || !names.Add(level.Value))
                        return false;
                }
                if (values.Count == 0 || !groups.TryAdd(group.Element("name")!.Value, values))
                    return false;
            }
            if (groups.Count == 0) return false;
            authority = new CharacterCreationFoundationQualityLevelSourceAuthority(digest!, groups);
            return true;
        }
        catch (System.Xml.XmlException) { return false; }
    }

    public bool TryResolveExact(string group, int level,
        CharacterCreationFoundationQualitySourceAuthority qualities,
        out CharacterCreationFoundationEffectTargetBinding? binding)
    {
        binding = null;
        return _groups.TryGetValue(group, out var levels) && levels.TryGetValue(level, out string? name)
            && qualities.TryResolveExact(name, out binding);
    }

    public bool TryResolveHighest(string group, IReadOnlyList<int> contributions,
        CharacterCreationFoundationQualitySourceAuthority qualities, out int highest,
        out CharacterCreationFoundationEffectTargetBinding? binding)
    {
        highest = 0;
        binding = null;
        if (contributions.Count == 0) return false;
        // Validate every contribution, including one superseded by a higher
        // tier. An unknown/disabled lower tier must not disappear silently.
        foreach (int level in contributions)
            if (!TryResolveExact(group, level, qualities, out _)) return false;
        highest = contributions.Max();
        return TryResolveExact(group, highest, qualities, out binding);
    }

    public bool TryResolveEffect(LifeModuleEffectProjectionDto effect,
        CharacterCreationFoundationQualitySourceAuthority qualities,
        out CharacterCreationFoundationEffectTargetBinding? binding)
    {
        binding = null;
        if (!effect.IsFullyTyped || effect.Domain != "quality" || effect.BudgetId is not null
            || effect.BudgetDelta != 0 || effect.SourceAnchorIds.Count == 0
            || effect.SourceAnchorIds.Any(string.IsNullOrWhiteSpace))
            return false;
        try
        {
            XElement node = XElement.Parse(effect.RawXml);
            if (node.Name != "qualitylevel" || node.HasElements
                || node.Attributes().Count() != 1 || node.Attribute("group") is not XAttribute group
                || !IsLiteral(node, allowAttribute: true)
                || !int.TryParse(node.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int level)
                || level <= 0 || node.Value != level.ToString(CultureInfo.InvariantCulture)
                || effect.TargetId != node.Value || effect.AfterValue != node.Value
                || effect.Parameters.Count != 1 || !effect.Parameters.TryGetValue("@group", out string? projectedGroup)
                || group.Value != projectedGroup)
                return false;

            return TryResolveExact(group.Value, level, qualities, out binding);
        }
        catch (System.Xml.XmlException) { return false; }
    }

    private static bool IsLiteral(XElement? node, bool allowAttribute = false)
        => node is not null && !node.HasElements && (allowAttribute || !node.HasAttributes)
            && node.Nodes().All(child => child is XText)
            && !string.IsNullOrWhiteSpace(node.Value) && node.Value == node.Value.Trim()
            && !node.Value.Contains('$') && !node.Value.Contains('[') && !node.Value.Contains('{');

    private static bool IsContainer(XElement node)
        => node.Nodes().All(child => child is XElement or XComment
            || child is XText text && string.IsNullOrWhiteSpace(text.Value));
}
