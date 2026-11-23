using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterCareerSkillGroupAdvanceService
{
    CharacterCareerSkillGroupQuoteResult Quote(
        CharacterCareerSkillGroupQuoteRequest request);

    CharacterCareerSkillGroupAdvanceResult Advance(
        CharacterCareerSkillGroupAdvanceCommand command);
    CharacterCareerSkillGroupQuoteResult Quote(CharacterCareerSkillGroupQuoteRequest request, CancellationToken token)
        => Quote(request);
    CharacterCareerSkillGroupAdvanceResult Advance(CharacterCareerSkillGroupAdvanceCommand command, CancellationToken token)
        => Advance(command);
    // Recovery never dispatches an advance. Older services must fail closed.
    CharacterCareerSkillGroupAdvanceResult Lookup(CharacterCareerSkillGroupAdvanceCommand command, CancellationToken token = default)
        => throw new NotSupportedException("Read-only skill-group recovery is unavailable.");
}
