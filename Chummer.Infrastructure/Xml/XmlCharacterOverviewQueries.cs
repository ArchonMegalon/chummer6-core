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

    public CharacterOverviewProjection ParseOverview(CharacterDocument document)
        => _characterSectionService.ParseOverview(document.Content);

    public CharacterProfileSection ParseProfile(CharacterDocument document) => _characterSectionService.ParseProfile(document.Content);

    public CharacterProgressSection ParseProgress(CharacterDocument document) => _characterSectionService.ParseProgress(document.Content);

    public CharacterProgressSection ParseKarmaSummary(CharacterDocument document) => _characterSectionService.ParseKarmaSummary(document.Content);

    public CharacterRulesSection ParseRules(CharacterDocument document) => _characterSectionService.ParseRules(document.Content);

    public CharacterBuildSection ParseBuild(CharacterDocument document) => _characterSectionService.ParseBuild(document.Content);

    public CharacterMovementSection ParseMovement(CharacterDocument document) => _characterSectionService.ParseMovement(document.Content);

    public CharacterConditionMonitorSection ParseConditionMonitor(CharacterDocument document) => _characterSectionService.ParseConditionMonitor(document.Content);

    public CharacterAwakeningSection ParseAwakening(CharacterDocument document) => _characterSectionService.ParseAwakening(document.Content);

    public CharacterSpellDefenseSection ParseSpellDefense(CharacterDocument document) => _characterSectionService.ParseSpellDefense(document.Content);

    public CharacterSkillsSection ParseSkills(CharacterDocument document) => _characterSectionService.ParseSkills(document.Content);
}
