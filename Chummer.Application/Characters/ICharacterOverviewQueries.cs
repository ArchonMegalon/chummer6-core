using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterOverviewQueries
{
    CharacterOverviewProjection ParseOverview(CharacterDocument document)
        => new(
            Profile: ParseProfile(document),
            Progress: ParseProgress(document),
            Skills: ParseSkills(document),
            Rules: ParseRules(document),
            Build: ParseBuild(document),
            Movement: ParseMovement(document),
            Awakening: ParseAwakening(document));

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
