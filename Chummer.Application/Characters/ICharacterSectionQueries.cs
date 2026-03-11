using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public interface ICharacterSectionQueries
{
    object ParseSection(string sectionId, CharacterDocument document);
}
