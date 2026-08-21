using Chummer.Contracts.Api;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Tools;

public interface ICharacterRosterFavoriteStore
{
    CharacterRosterFavoriteState Load();

    CharacterRosterFavoriteState Load(OwnerScope owner);

    void Save(long expectedRevision, CharacterRosterFavoriteState state);

    void Save(OwnerScope owner, long expectedRevision, CharacterRosterFavoriteState state);
}
