using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed class XmlCharacterStatsQueries : ICharacterStatsQueries
{
    private readonly ICharacterSectionService _characterSectionService;

    public XmlCharacterStatsQueries(ICharacterSectionService characterSectionService)
    {
        _characterSectionService = characterSectionService;
    }

    public CharacterAttributesSection ParseAttributes(CharacterDocument document) => _characterSectionService.ParseAttributes(document.Content);

    public CharacterAttributeDetailsSection ParseAttributeDetails(CharacterDocument document) => _characterSectionService.ParseAttributeDetails(document.Content);

    public CharacterLimitModifiersSection ParseLimitModifiers(CharacterDocument document) => _characterSectionService.ParseLimitModifiers(document.Content);
}
