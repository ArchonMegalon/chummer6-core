using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterSocialNarrativeQueries
{
    CharacterQualitiesSection ParseQualities(CharacterDocument document);

    CharacterContactsSection ParseContacts(CharacterDocument document);

    CharacterLifestylesSection ParseLifestyles(CharacterDocument document);

    CharacterSourcesSection ParseSources(CharacterDocument document);

    CharacterExpensesSection ParseExpenses(CharacterDocument document);

    CharacterCalendarSection ParseCalendar(CharacterDocument document);

    CharacterImprovementsSection ParseImprovements(CharacterDocument document);

    CharacterCustomDataDirectoryNamesSection ParseCustomDataDirectoryNames(CharacterDocument document);
}
