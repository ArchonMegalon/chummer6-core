using System.Xml;
using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

/// <summary>Validates source identity and exact talent-to-quality references. This does
/// not interpret a bonus, grant legality, or authorize appending source XML to a runner.</summary>
public static class CharacterCreationTalentQualitySourceRules
{
    public const string Unresolved = "creation-magic-resonance-talent-quality-source-unresolved";

    public static string ComputeSourceNodeDigest(string inputsDigest, string sourceId, string canonicalXml) =>
        CharacterCreationMagicResonanceDigest.Compute(new
        {
            Schema = "chummer.sr5.standard_priority_talent_quality_source.v1",
            EffectiveInputsDigest = inputsDigest,
            SourceId = sourceId,
            RawNode = canonicalXml
        });

    public static bool TryReadReferences(string talentXml, out (string Reference, string Selection)[] references)
    {
        references = [];
        if (!TryParse(talentXml, "talent", out XElement? talent) || talent is null)
            return false;
        XElement[] containers = talent.Elements("qualities").Take(2).ToArray();
        if (containers.Length == 0)
            return true;
        if (containers.Length != 1 || containers[0].HasAttributes
            || containers[0].Elements().Any(item => item.Name != "quality")
            || containers[0].Nodes().OfType<XText>().Any(item => !string.IsNullOrWhiteSpace(item.Value)))
            return false;
        XElement[] rows = containers[0].Elements().Take(33).ToArray();
        if (rows.Length > 32 || rows.Any(item => item.HasElements
                || item.Attributes().Any(attribute => attribute.Name != "select")
                || !IsLabel(item.Value)
                || (item.Attribute("select") is { } select && !IsLabel(select.Value))))
            return false;
        references = rows.Select(item => (item.Value, item.Attribute("select")?.Value ?? string.Empty)).ToArray();
        return references.Distinct().Count() == references.Length;
    }

    public static bool IsValidSource(CharacterCreationTalentQualitySource? source)
    {
        if (source is null || !IsLabel(source.Reference) || string.IsNullOrEmpty(source.CanonicalSourceXml)
            || source.CanonicalSourceXml.Length > 262144
            || (source.ForcedSelection != string.Empty && !IsLabel(source.ForcedSelection))
            || !Guid.TryParseExact(source.SourceId, "D", out Guid id) || id == Guid.Empty
            || source.SourceId != id.ToString("D") || !IsLabel(source.Name)
            || !IsLabel(source.SourceBook) || !IsLabel(source.Page)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(source.EffectiveSourceDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(source.SourceNodeDigest,
                ComputeSourceNodeDigest(source.EffectiveSourceDigest, source.SourceId, source.CanonicalSourceXml))
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(source.CanonicalSourceXmlDigest,
                CharacterCreationMagicResonanceDigest.ComputeUtf8(source.CanonicalSourceXml))
            || !TryParse(source.CanonicalSourceXml, "quality", out XElement? row) || row is null
            || source.CanonicalSourceXml != row.ToString(SaveOptions.DisableFormatting)
            || !HasSourceIdentity(row, id) || !HasScalar(row, "name", source.Name)
            || !HasScalar(row, "source", source.SourceBook) || !HasScalar(row, "page", source.Page)
            || source.SourceAnchorIds is null
            || !source.SourceAnchorIds.SequenceEqual(["qualities.xml#quality:" + source.SourceId], StringComparer.Ordinal))
            return false;
        return source.Reference == source.Name
            || (Guid.TryParse(source.Reference, out Guid referenceId) && referenceId == id);
    }

    public static bool MatchesTalent(string talentXml, IReadOnlyList<CharacterCreationTalentQualitySource>? sources)
    {
        if (sources is null || !TryReadReferences(talentXml, out var references)
            || sources.Count != references.Length || sources.Any(item => !IsValidSource(item)))
            return false;
        // No alias may grant the same source/selection twice (e.g. once by name and once by ID).
        return sources.Select(item => (item.SourceId, item.ForcedSelection)).Distinct().Count() == sources.Count
            && sources.Select(item => (item.Reference, item.ForcedSelection)).SequenceEqual(references);
    }

    private static bool HasScalar(XElement row, string name, string expected)
    {
        XElement[] values = row.Elements(name).Take(2).ToArray();
        return values.Length == 1 && !values[0].HasAttributes && !values[0].HasElements
            && string.Equals(values[0].Value, expected, StringComparison.Ordinal);
    }

    private static bool HasSourceIdentity(XElement row, Guid expected)
    {
        XElement[] values = row.Elements("id").Take(2).ToArray();
        return values.Length == 1 && !values[0].HasAttributes && !values[0].HasElements
            && Guid.TryParseExact(values[0].Value, "D", out Guid actual) && actual == expected;
    }

    private static bool IsLabel(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 512 && value == value.Trim() && !value.Any(char.IsControl);

    private static bool TryParse(string? xml, string root, out XElement? value)
    {
        value = null;
        if (string.IsNullOrEmpty(xml) || xml.Length > 262144)
            return false;
        try
        {
            using var input = new StringReader(xml);
            using XmlReader reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true,
                MaxCharactersInDocument = 262144
            });
            XDocument document = XDocument.Load(reader, LoadOptions.None);
            value = document.Root;
            return value is not null && value.Name == root && !value.HasAttributes
                && document.DescendantNodes().All(item => item is not XProcessingInstruction)
                && value.DescendantsAndSelf().All(item => item.Name.Namespace == XNamespace.None
                    && item.Attributes().All(attribute => !attribute.IsNamespaceDeclaration
                        && attribute.Name.Namespace == XNamespace.None));
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            return false;
        }
    }
}
