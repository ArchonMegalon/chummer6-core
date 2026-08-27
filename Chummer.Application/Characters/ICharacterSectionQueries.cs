using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterSectionQueries
{
    CharacterOverviewProjection ParseOverview(CharacterDocument document)
        => new(
            Profile: Require<CharacterProfileSection>("profile", document),
            Progress: Require<CharacterProgressSection>("progress", document),
            Skills: Require<CharacterSkillsSection>("skills", document),
            Rules: Require<CharacterRulesSection>("rules", document),
            Build: Require<CharacterBuildSection>("build", document),
            Movement: Require<CharacterMovementSection>("movement", document),
            Awakening: Require<CharacterAwakeningSection>("awakening", document));

    object ParseSection(string sectionId, CharacterDocument document);

    private TSection Require<TSection>(string sectionId, CharacterDocument document)
        where TSection : class
        => ParseSection(sectionId, document) as TSection
            ?? throw new InvalidOperationException(
                $"Section '{sectionId}' did not return {typeof(TSection).Name}.");
}
