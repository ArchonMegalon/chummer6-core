using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed class XmlCharacterMagicResonanceQueries : ICharacterMagicResonanceQueries
{
    private readonly ICharacterSectionService _characterSectionService;

    public XmlCharacterMagicResonanceQueries(ICharacterSectionService characterSectionService)
    {
        _characterSectionService = characterSectionService;
    }

    public CharacterSpellsSection ParseSpells(CharacterDocument document) => _characterSectionService.ParseSpells(document.Content);

    public CharacterPowersSection ParsePowers(CharacterDocument document) => _characterSectionService.ParsePowers(document.Content);

    public CharacterComplexFormsSection ParseComplexForms(CharacterDocument document) => _characterSectionService.ParseComplexForms(document.Content);

    public CharacterSpiritsSection ParseSpirits(CharacterDocument document) => _characterSectionService.ParseSpirits(document.Content);

    public CharacterFociSection ParseFoci(CharacterDocument document) => _characterSectionService.ParseFoci(document.Content);

    public CharacterAiProgramsSection ParseAiPrograms(CharacterDocument document) => _characterSectionService.ParseAiPrograms(document.Content);

    public CharacterMartialArtsSection ParseMartialArts(CharacterDocument document) => _characterSectionService.ParseMartialArts(document.Content);

    public CharacterMetamagicsSection ParseMetamagics(CharacterDocument document) => _characterSectionService.ParseMetamagics(document.Content);

    public CharacterArtsSection ParseArts(CharacterDocument document) => _characterSectionService.ParseArts(document.Content);

    public CharacterInitiationGradesSection ParseInitiationGrades(CharacterDocument document) => _characterSectionService.ParseInitiationGrades(document.Content);

    public CharacterCritterPowersSection ParseCritterPowers(CharacterDocument document) => _characterSectionService.ParseCritterPowers(document.Content);

    public CharacterMentorSpiritsSection ParseMentorSpirits(CharacterDocument document) => _characterSectionService.ParseMentorSpirits(document.Content);
}
