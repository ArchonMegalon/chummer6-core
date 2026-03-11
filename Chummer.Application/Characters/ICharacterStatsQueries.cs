using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterStatsQueries
{
    CharacterAttributesSection ParseAttributes(CharacterDocument document);

    CharacterAttributeDetailsSection ParseAttributeDetails(CharacterDocument document);

    CharacterLimitModifiersSection ParseLimitModifiers(CharacterDocument document);
}
