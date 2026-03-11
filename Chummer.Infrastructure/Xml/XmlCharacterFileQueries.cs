using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed class XmlCharacterFileQueries : ICharacterFileQueries
{
    private readonly ICharacterFileService _characterFileService;

    public XmlCharacterFileQueries(ICharacterFileService characterFileService)
    {
        _characterFileService = characterFileService;
    }

    public CharacterFileSummary ParseSummary(CharacterDocument document)
    {
        return _characterFileService.ParseSummaryFromXml(document.Content);
    }

    public CharacterValidationResult Validate(CharacterDocument document)
    {
        return _characterFileService.ValidateXml(document.Content);
    }
}
