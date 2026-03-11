using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public interface ISessionProfileSelectionStore
{
    IReadOnlyList<SessionProfileBinding> List(OwnerScope owner);

    SessionProfileBinding? Get(OwnerScope owner, string characterId);

    SessionProfileBinding Upsert(OwnerScope owner, SessionProfileBinding binding);
}
