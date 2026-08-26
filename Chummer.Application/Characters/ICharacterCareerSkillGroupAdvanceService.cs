using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterCareerSkillGroupAdvanceService
{
    CharacterCareerSkillGroupQuoteResult Quote(
        CharacterCareerSkillGroupQuoteRequest request);

    CharacterCareerSkillGroupAdvanceResult Advance(
        CharacterCareerSkillGroupAdvanceCommand command);
}
