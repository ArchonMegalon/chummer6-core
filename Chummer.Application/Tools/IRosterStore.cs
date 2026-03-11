using Chummer.Contracts.Api;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Tools;

public interface IRosterStore
{
    IReadOnlyList<RosterEntry> Load();

    IReadOnlyList<RosterEntry> Upsert(RosterEntry entry);

    IReadOnlyList<RosterEntry> Load(OwnerScope owner);

    IReadOnlyList<RosterEntry> Upsert(OwnerScope owner, RosterEntry entry);
}
