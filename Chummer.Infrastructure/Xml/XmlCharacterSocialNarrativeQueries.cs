using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed class XmlCharacterSocialNarrativeQueries : ICharacterSocialNarrativeQueries
{
    private readonly ICharacterSectionService _characterSectionService;

    public XmlCharacterSocialNarrativeQueries(ICharacterSectionService characterSectionService)
    {
        _characterSectionService = characterSectionService;
    }

    public CharacterQualitiesSection ParseQualities(CharacterDocument document) => _characterSectionService.ParseQualities(document.Content);

    public CharacterContactsSection ParseContacts(CharacterDocument document) => _characterSectionService.ParseContacts(document.Content);

    public CharacterLifestylesSection ParseLifestyles(CharacterDocument document) => _characterSectionService.ParseLifestyles(document.Content);

    public CharacterSourcesSection ParseSources(CharacterDocument document) => _characterSectionService.ParseSources(document.Content);

    public CharacterExpensesSection ParseExpenses(CharacterDocument document) => _characterSectionService.ParseExpenses(document.Content);

    public CharacterCalendarSection ParseCalendar(CharacterDocument document) => _characterSectionService.ParseCalendar(document.Content);

    public CharacterImprovementsSection ParseImprovements(CharacterDocument document) => _characterSectionService.ParseImprovements(document.Content);

    public CharacterCustomDataDirectoryNamesSection ParseCustomDataDirectoryNames(CharacterDocument document) =>
        _characterSectionService.ParseCustomDataDirectoryNames(document.Content);
}
