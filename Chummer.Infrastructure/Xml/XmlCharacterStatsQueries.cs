using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed class XmlCharacterStatsQueries : ICharacterStatsQueries, ICharacterSheetDerivedQueries
{
    private readonly ICharacterSectionService _characterSectionService;

    public XmlCharacterStatsQueries(ICharacterSectionService characterSectionService)
    {
        _characterSectionService = characterSectionService;
    }

    public CharacterSheetDerivedSection ParseSheetDerived(Chummer.Application.Workspaces.WorkspaceStoredDocument observation) =>
        (_characterSectionService as ICharacterSheetDerivedQueries)?.ParseSheetDerived(observation)
        ?? CharacterSheetDerivedSection.Unavailable("The workspace-bound sheet read authority is unavailable.");

    public CharacterAttributesSection ParseAttributes(CharacterDocument document) => _characterSectionService.ParseAttributes(document.Content);

    public CharacterAttributeDetailsSection ParseAttributeDetails(CharacterDocument document) => _characterSectionService.ParseAttributeDetails(document.Content);

    public CharacterLimitModifiersSection ParseLimitModifiers(CharacterDocument document) => _characterSectionService.ParseLimitModifiers(document.Content);

    public CharacterSheetDerivedSection ParseSheetDerived(CharacterDocument document) =>
        (_characterSectionService as ICharacterSheetDerivedQueries)?.ParseSheetDerived(document)
        ?? CharacterSheetDerivedSection.Unavailable("The source-bound sheet read authority is unavailable.");
}
