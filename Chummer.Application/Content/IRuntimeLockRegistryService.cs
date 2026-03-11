using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRuntimeLockRegistryService
{
    RuntimeLockRegistryPage List(OwnerScope owner, string? rulesetId = null);

    RuntimeLockRegistryEntry? Get(OwnerScope owner, string lockId, string? rulesetId = null);

    RuntimeLockRegistryEntry Upsert(OwnerScope owner, string lockId, RuntimeLockSaveRequest request);
}
