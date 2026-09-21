using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Immutable, digest-bound view of the canonical English skill identities in
/// skills.xml. This is source authority for static write planning only; it does
/// not infer enabled sources or mutate a character's runtime skill collection.
/// </summary>
internal sealed class CharacterCreationFoundationSkillSourceAuthority
{
    private readonly IReadOnlyDictionary<string, SkillDefinition[]> _activeByName;
    private readonly IReadOnlySet<string> _groupNames;

    private CharacterCreationFoundationSkillSourceAuthority(
        string sourceDigest,
        IReadOnlyDictionary<string, SkillDefinition[]> activeByName,
        IReadOnlySet<string> groupNames)
    {
        SourceDigest = sourceDigest;
        _activeByName = activeByName;
        _groupNames = groupNames;
    }

    public string SourceDigest { get; }

    public static bool TryCreate(
        string? sourceXml,
        string? sourceDigest,
        out CharacterCreationFoundationSkillSourceAuthority? authority)
    {
        authority = null;
        if (!CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(sourceDigest)
            || !FixedTimeEquals(
                sourceDigest,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(
                    sourceXml ?? string.Empty)))
        {
            return false;
        }

        try
        {
            XDocument document = XDocument.Parse(sourceXml ?? string.Empty, LoadOptions.None);
            XElement? root = document.Root;
            XElement[] activeContainers = root?.Elements()
                .Where(element => string.Equals(
                    element.Name.LocalName,
                    "skills",
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray() ?? [];
            if (root is null
                || root.Name.NamespaceName.Length != 0
                || !string.Equals(root.Name.LocalName, "chummer", StringComparison.Ordinal)
                || activeContainers.Length != 1
                || activeContainers[0].Name.NamespaceName.Length != 0
                || activeContainers[0].HasAttributes
                || activeContainers[0].Elements().Any(element =>
                    element.Name.NamespaceName.Length != 0
                    || !string.Equals(element.Name.LocalName, "skill", StringComparison.Ordinal)))
            {
                return false;
            }

            var definitions = new List<SkillDefinition>();
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (XElement skill in activeContainers[0].Elements("skill"))
            {
                XElement[] ids = skill.Elements("id").Take(2).ToArray();
                XElement[] names = skill.Elements("name").Take(2).ToArray();
                XElement[] exoticValues = skill.Elements("exotic").Take(2).ToArray();
                XElement[] groups = skill.Elements("skillgroup").Take(2).ToArray();
                if (ids.Length != 1
                    || names.Length != 1
                    || exoticValues.Length > 1
                    || groups.Length > 1
                    || ids[0].HasAttributes
                    || ids[0].HasElements
                    || names[0].HasAttributes
                    || names[0].HasElements
                    || exoticValues.Any(value => value.HasAttributes || value.HasElements)
                    || groups.Any(value => value.HasAttributes || value.HasElements))
                {
                    return false;
                }

                string sourceId = ids[0].Value;
                string canonicalName = names[0].Value;
                bool sourceIdIsCanonical = Guid.TryParseExact(
                                               sourceId,
                                               "D",
                                               out Guid parsedSourceId)
                                           && parsedSourceId != Guid.Empty
                                           && string.Equals(
                                               sourceId,
                                               parsedSourceId.ToString("D"),
                                               StringComparison.Ordinal);
                bool isExotic = false;
                if (!sourceIdIsCanonical
                    || string.IsNullOrWhiteSpace(canonicalName)
                    || !string.Equals(
                        canonicalName,
                        canonicalName.Trim(),
                        StringComparison.Ordinal)
                    || !sourceIds.Add(sourceId)
                    || (exoticValues.Length == 1
                        && !bool.TryParse(exoticValues[0].Value.Trim(), out isExotic)))
                {
                    return false;
                }

                definitions.Add(new SkillDefinition(sourceId, canonicalName, isExotic,
                    groups.Length == 0 ? string.Empty : groups[0].Value));
            }

            if (definitions.Count == 0)
                return false;

            XElement[] groupContainers = root.Elements("skillgroups").Take(2).ToArray();
            var groupNames = new HashSet<string>(StringComparer.Ordinal);
            if (groupContainers.Length > 1)
                return false;
            if (groupContainers.Length == 1)
            {
                XElement groupContainer = groupContainers[0];
                if (groupContainer.HasAttributes || groupContainer.Nodes().Any(node =>
                        node is XText text ? !string.IsNullOrWhiteSpace(text.Value)
                            : node is not XElement && node is not XComment))
                    return false;
                foreach (XElement group in groupContainer.Elements())
                {
                    if (group.Name != "name" || group.HasAttributes || group.HasElements
                        || string.IsNullOrWhiteSpace(group.Value)
                        || !string.Equals(group.Value, group.Value.Trim(), StringComparison.Ordinal)
                        || !groupNames.Add(group.Value))
                        return false;
                }
            }

            IReadOnlyDictionary<string, SkillDefinition[]> activeByName = definitions
                .GroupBy(definition => definition.CanonicalName, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(
                            definition => definition.SourceId,
                            StringComparer.Ordinal)
                        .ToArray(),
                    StringComparer.Ordinal);
            authority = new CharacterCreationFoundationSkillSourceAuthority(
                sourceDigest!,
                activeByName,
                groupNames);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or System.Xml.XmlException)
        {
            return false;
        }
    }

    public bool TryResolveExactActive(
        string canonicalName,
        out CharacterCreationFoundationEffectTargetBinding? binding)
    {
        binding = null;
        if (!_activeByName.TryGetValue(canonicalName, out SkillDefinition[]? matches)
            || matches.Length != 1
            || matches[0].IsExotic)
        {
            return false;
        }

        SkillDefinition match = matches[0];
        binding = new CharacterCreationFoundationEffectTargetBinding(
            TargetKind: "active-skill",
            SourceId: match.SourceId,
            CanonicalName: match.CanonicalName,
            SourceDigest: SourceDigest);
        return true;
    }

    public bool TryResolveExactGroup(string canonicalName,
        out CharacterCreationFoundationEffectTargetBinding? binding)
    {
        binding = null;
        SkillDefinition[][] members = _activeByName.Values.Where(items => items.Any(skill =>
            string.Equals(skill.GroupName, canonicalName, StringComparison.Ordinal))).ToArray();
        if (!_groupNames.Contains(canonicalName) || members.Length == 0
            || members.Any(items => items.Length != 1 || items[0].IsExotic))
            return false;

        // skills.xml groups have names, not GUIDs. Keep that source identity
        // explicit; never manufacture a skill GUID or collapse the group into
        // separate per-skill grants (which changes group-break semantics).
        binding = new CharacterCreationFoundationEffectTargetBinding(
            TargetKind: "skill-group", SourceId: canonicalName,
            CanonicalName: canonicalName, SourceDigest: SourceDigest);
        return true;
    }

    private static bool FixedTimeEquals(string? left, string? right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private sealed record SkillDefinition(
        string SourceId,
        string CanonicalName,
        bool IsExotic,
        string GroupName);
}
