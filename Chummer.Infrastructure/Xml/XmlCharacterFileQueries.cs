using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed class XmlCharacterFileQueries : ICharacterFileQueries
{
    private readonly ICharacterFileService _characterFileService;
    // Only this sealed, stateless parser is immutable. An injected implementation
    // can change answers or invoke user code and must retain every observation.
    internal bool HasImmutableEvaluationQueries2764 => _characterFileService is CharacterFileService;

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
