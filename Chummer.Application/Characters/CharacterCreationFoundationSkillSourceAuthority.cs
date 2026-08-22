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

    private CharacterCreationFoundationSkillSourceAuthority(
        string sourceDigest,
        IReadOnlyDictionary<string, SkillDefinition[]> activeByName)
    {
        SourceDigest = sourceDigest;
        _activeByName = activeByName;
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
                if (ids.Length != 1
                    || names.Length != 1
                    || exoticValues.Length > 1
                    || ids[0].HasAttributes
                    || ids[0].HasElements
                    || names[0].HasAttributes
                    || names[0].HasElements
                    || exoticValues.Any(value => value.HasAttributes || value.HasElements))
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

                definitions.Add(new SkillDefinition(sourceId, canonicalName, isExotic));
            }

            if (definitions.Count == 0)
                return false;

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
                activeByName);
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
        bool IsExotic);
}
