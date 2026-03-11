using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterFileQueries
{
    CharacterFileSummary ParseSummary(CharacterDocument document);

    CharacterValidationResult Validate(CharacterDocument document);
}
