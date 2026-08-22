using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Exact-name resolver over the raw qualities.xml input used by composite Life
/// Module finalization planning. The raw-input digest is part of every binding;
/// duplicate names or ids deliberately make the target ambiguous.
/// </summary>
internal sealed class CharacterCreationFoundationQualitySourceAuthority
{
    private readonly IReadOnlyDictionary<string, QualityDefinition> _byName;
    private readonly IReadOnlyDictionary<string, QualityDefinition> _byId;

    private CharacterCreationFoundationQualitySourceAuthority(
        string sourceDigest,
        IReadOnlyDictionary<string, QualityDefinition> byName,
        IReadOnlyDictionary<string, QualityDefinition> byId)
    {
        SourceDigest = sourceDigest;
        _byName = byName;
        _byId = byId;
    }

    public string SourceDigest { get; }

    public static bool TryCreate(
        string? sourceXml,
        string? sourceDigest,
        out CharacterCreationFoundationQualitySourceAuthority? authority)
    {
        authority = null;
        if (string.IsNullOrEmpty(sourceXml)
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(sourceDigest)
            || !FixedTimeEquals(
                CharacterCreationFoundationDraftLedgerIntegrity
                    .ComputeRawCharacterXmlDigest(sourceXml),
                sourceDigest))
        {
            return false;
        }

        try
        {
            XDocument document = XDocument.Parse(sourceXml, LoadOptions.None);
            XElement? root = document.Root;
            XElement[] qualityContainers = root?.Elements()
                .Where(element => string.Equals(
                    element.Name.LocalName,
                    "qualities",
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray() ?? [];
            if (root is null
                || root.Name.NamespaceName.Length != 0
                || !string.Equals(root.Name.LocalName, "chummer", StringComparison.Ordinal)
                || qualityContainers.Length != 1
                || qualityContainers[0].Name.NamespaceName.Length != 0
                || qualityContainers[0].HasAttributes
                || qualityContainers[0].Elements().Any(element =>
                    element.Name.NamespaceName.Length != 0
                    || !string.Equals(
                        element.Name.LocalName,
                        "quality",
                        StringComparison.Ordinal)))
            {
                return false;
            }

            var definitions = new List<QualityDefinition>();
            foreach (XElement quality in qualityContainers[0].Elements("quality"))
            {
                XElement[] ids = quality.Elements("id").Take(2).ToArray();
                XElement[] names = quality.Elements("name").Take(2).ToArray();
                if (ids.Length != 1
                    || names.Length != 1
                    || ids[0].HasAttributes
                    || ids[0].HasElements
                    || names[0].HasAttributes
                    || names[0].HasElements)
                {
                    return false;
                }

                string id = ids[0].Value.Trim();
                string name = names[0].Value.Trim();
                if (!Guid.TryParseExact(id, "D", out Guid parsedId)
                    || parsedId == Guid.Empty
                    || string.IsNullOrWhiteSpace(name)
                    || !string.Equals(ids[0].Value, id, StringComparison.Ordinal)
                    || !string.Equals(names[0].Value, name, StringComparison.Ordinal))
                {
                    return false;
                }

                definitions.Add(new QualityDefinition(
                    parsedId.ToString("D"),
                    name,
                    new XElement(quality),
                    CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                        quality.ToString(SaveOptions.DisableFormatting))));
            }

            if (definitions.Count == 0
                || definitions.GroupBy(item => item.SourceId, StringComparer.Ordinal)
                    .Any(group => group.Count() != 1))
            {
                return false;
            }

            authority = new CharacterCreationFoundationQualitySourceAuthority(
                sourceDigest!,
                definitions.GroupBy(item => item.CanonicalName, StringComparer.Ordinal)
                    .Where(group => group.Count() == 1)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Single(),
                        StringComparer.Ordinal),
                definitions.ToDictionary(
                    item => item.SourceId,
                    item => item,
                    StringComparer.Ordinal));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or System.Xml.XmlException)
        {
            return false;
        }
    }

    public bool TryResolveExact(
        string canonicalName,
        out CharacterCreationFoundationEffectTargetBinding? binding)
    {
        binding = null;
        if (!_byName.TryGetValue(canonicalName, out QualityDefinition? definition))
            return false;

        binding = new CharacterCreationFoundationEffectTargetBinding(
            "quality",
            definition.SourceId,
            definition.CanonicalName,
            SourceDigest);
        return true;
    }

    public bool TryGetDefinition(
        CharacterCreationFoundationEffectTargetBinding binding,
        out XElement? source,
        out string sourceNodeDigest)
    {
        source = null;
        sourceNodeDigest = string.Empty;
        if (!string.Equals(binding.TargetKind, "quality", StringComparison.Ordinal)
            || !FixedTimeEquals(binding.SourceDigest, SourceDigest)
            || !_byId.TryGetValue(binding.SourceId, out QualityDefinition? definition)
            || !string.Equals(
                definition.CanonicalName,
                binding.CanonicalName,
                StringComparison.Ordinal))
        {
            return false;
        }

        source = new XElement(definition.Source);
        sourceNodeDigest = definition.SourceNodeDigest;
        return true;
    }

    private static bool FixedTimeEquals(string? left, string? right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private sealed record QualityDefinition(
        string SourceId,
        string CanonicalName,
        XElement Source,
        string SourceNodeDigest);
}
