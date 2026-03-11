using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRulePackInstallHistoryStore
{
    IReadOnlyList<RulePackInstallHistoryRecord> List(OwnerScope owner, string? rulesetId = null);

    IReadOnlyList<RulePackInstallHistoryRecord> GetHistory(OwnerScope owner, string packId, string version, string rulesetId);

    RulePackInstallHistoryRecord Append(OwnerScope owner, RulePackInstallHistoryRecord record);
}
