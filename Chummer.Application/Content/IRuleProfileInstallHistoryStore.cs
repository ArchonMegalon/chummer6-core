using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRuleProfileInstallHistoryStore
{
    IReadOnlyList<RuleProfileInstallHistoryRecord> List(OwnerScope owner, string? rulesetId = null);

    IReadOnlyList<RuleProfileInstallHistoryRecord> GetHistory(OwnerScope owner, string profileId, string rulesetId);

    RuleProfileInstallHistoryRecord Append(OwnerScope owner, RuleProfileInstallHistoryRecord record);
}
