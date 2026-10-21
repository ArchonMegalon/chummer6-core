using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

// In-process host admission. Never deserialize or implement this with a JSON flag.
// IsCurrent must be synchronous, side-effect free, and must not reacquire the owner lease.
public interface ICharacterCreationReservationAuthority
{
    bool IsCurrent(OwnerContextStamp owner, CharacterCreationReservation reservation);
}

public interface IReservedCharacterCreationAtomicCreateCapability
{
    WorkspaceStoreMutationResult CreateReservedCharacterCreation(OwnerScope owner,
        WorkspaceDocument document, CharacterCreationReservation reservation, Action beforeCommit);
}
