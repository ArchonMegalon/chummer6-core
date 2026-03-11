using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed class XmlCharacterOverviewQueries : ICharacterOverviewQueries
{
    private readonly ICharacterSectionService _characterSectionService;

    public XmlCharacterOverviewQueries(ICharacterSectionService characterSectionService)
    {
        _characterSectionService = characterSectionService;
    }

    public CharacterProfileSection ParseProfile(CharacterDocument document) => _characterSectionService.ParseProfile(document.Content);

    public CharacterProgressSection ParseProgress(CharacterDocument document) => _characterSectionService.ParseProgress(document.Content);

    public CharacterRulesSection ParseRules(CharacterDocument document) => _characterSectionService.ParseRules(document.Content);

    public CharacterBuildSection ParseBuild(CharacterDocument document) => _characterSectionService.ParseBuild(document.Content);

    public CharacterMovementSection ParseMovement(CharacterDocument document) => _characterSectionService.ParseMovement(document.Content);

    public CharacterAwakeningSection ParseAwakening(CharacterDocument document) => _characterSectionService.ParseAwakening(document.Content);

    public CharacterSkillsSection ParseSkills(CharacterDocument document) => _characterSectionService.ParseSkills(document.Content);
}
