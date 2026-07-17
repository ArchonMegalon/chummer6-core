using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterOverviewQueries
{
    CharacterProfileSection ParseProfile(CharacterDocument document);

    CharacterProgressSection ParseProgress(CharacterDocument document);

    CharacterProgressSection ParseKarmaSummary(CharacterDocument document);

    CharacterRulesSection ParseRules(CharacterDocument document);

    CharacterBuildSection ParseBuild(CharacterDocument document);

    CharacterMovementSection ParseMovement(CharacterDocument document);

    CharacterConditionMonitorSection ParseConditionMonitor(CharacterDocument document);

    CharacterAwakeningSection ParseAwakening(CharacterDocument document);

    CharacterSpellDefenseSection ParseSpellDefense(CharacterDocument document);

    CharacterSkillsSection ParseSkills(CharacterDocument document);
}
