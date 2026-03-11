using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRuntimeLockInstallHistoryStore
{
    IReadOnlyList<RuntimeLockInstallHistoryRecord> List(OwnerScope owner, string? rulesetId = null);

    IReadOnlyList<RuntimeLockInstallHistoryRecord> GetHistory(OwnerScope owner, string lockId, string rulesetId);

    RuntimeLockInstallHistoryRecord Append(OwnerScope owner, RuntimeLockInstallHistoryRecord record);
}
