using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterMagicResonanceQueries
{
    CharacterSpellsSection ParseSpells(CharacterDocument document);

    CharacterPowersSection ParsePowers(CharacterDocument document);

    CharacterComplexFormsSection ParseComplexForms(CharacterDocument document);

    CharacterSpiritsSection ParseSpirits(CharacterDocument document);

    CharacterFociSection ParseFoci(CharacterDocument document);

    CharacterAiProgramsSection ParseAiPrograms(CharacterDocument document);

    CharacterMartialArtsSection ParseMartialArts(CharacterDocument document);

    CharacterMetamagicsSection ParseMetamagics(CharacterDocument document);

    CharacterArtsSection ParseArts(CharacterDocument document);

    CharacterInitiationGradesSection ParseInitiationGrades(CharacterDocument document);

    CharacterCritterPowersSection ParseCritterPowers(CharacterDocument document);

    CharacterMentorSpiritsSection ParseMentorSpirits(CharacterDocument document);
}
