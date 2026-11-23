using System.Xml;

namespace Chummer.Application.Characters;

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    private static XmlReader CreateCurrentRuleXmlReader(string xml, XmlReaderSettings settings)
    {
        // Large documents retain the normal reader and its exact document limits.
        // DTDs cannot expand a short input beyond either caller's document limit.
        // Legacy decimal whitespace references differ when whitespace is ignored.
        if (settings.IgnoreWhitespace || xml is not { Length: <= 2048 })
            return XmlReader.Create(new StringReader(xml), settings);

        var reader = new SmallCurrentRuleXmlReader(xml);
        try { return XmlReader.Create(reader, settings); }
        catch { reader.Dispose(); throw; }
    }

    private sealed class SmallCurrentRuleXmlReader : XmlTextReader
    {
        internal SmallCurrentRuleXmlReader(string xml) : base(xml, XmlNodeType.Document, null)
        {
            DtdProcessing = DtdProcessing.Prohibit;
            XmlResolver = null;
            Normalization = true;
            EntityHandling = EntityHandling.ExpandEntities;
            WhitespaceHandling = WhitespaceHandling.All;
        }

        // Report the legacy reader's missing character check so the standard
        // XmlReader wrapper adds it, including numeric character references.
        public override XmlReaderSettings Settings => new()
        {
            CheckCharacters = false,
            ConformanceLevel = ConformanceLevel.Document,
            DtdProcessing = DtdProcessing.Prohibit
        };
    }
}
