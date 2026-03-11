using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed class XmlCharacterMetadataCommands : ICharacterMetadataCommands
{
    private readonly ICharacterFileService _characterFileService;

    public XmlCharacterMetadataCommands(ICharacterFileService characterFileService)
    {
        _characterFileService = characterFileService;
    }

    public UpdateCharacterMetadataResult UpdateMetadata(UpdateCharacterMetadataCommand command)
    {
        string updatedContent = _characterFileService.ApplyMetadataUpdate(command.Document.Content, command.Update);
        CharacterDocument updatedDocument = new(updatedContent, command.Document.Format);
        CharacterFileSummary summary = _characterFileService.ParseSummaryFromXml(updatedDocument.Content);
        return new UpdateCharacterMetadataResult(
            UpdatedDocument: updatedDocument,
            Summary: summary);
    }
}
