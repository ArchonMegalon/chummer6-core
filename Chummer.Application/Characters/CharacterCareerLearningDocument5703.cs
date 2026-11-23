using System.Xml;
using System.Xml.Linq;

namespace Chummer.Application.Characters;

internal static class CharacterCareerLearningDocument5703
{
    // The existing typed learning edit owns the root mutation. Preserve all
    // document-level comments, processing instructions and the XML declaration.
    internal static string PreserveEnvelope(string original, string editedRoot)
    {
        using var input = new StringReader(original);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 67_108_864
        });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root is null) throw new InvalidDataException("learning_document_root_missing");
        document.Root.ReplaceWith(XElement.Parse(editedRoot, LoadOptions.PreserveWhitespace));
        return (document.Declaration?.ToString() ?? "") + document.ToString(SaveOptions.DisableFormatting);
    }
}
