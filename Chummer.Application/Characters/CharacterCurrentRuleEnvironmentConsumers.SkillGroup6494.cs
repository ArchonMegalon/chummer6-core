using Chummer.Contracts.Characters;
namespace Chummer.Application.Characters;
public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    internal static CharacterCareerSkillGroupAdvanceInput? ReadCurrentSkillGroupInput6494(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, Guid groupId, CancellationToken cancellationToken)
        => ReadCareerSkills1949(current, catalog, source, cancellationToken)?.GroupInputs
            .SingleOrDefault(input => input.Identity.InternalId == groupId);
}
