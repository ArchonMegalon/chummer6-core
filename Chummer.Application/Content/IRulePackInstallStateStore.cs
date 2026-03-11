using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRulePackInstallStateStore
{
    IReadOnlyList<RulePackInstallRecord> List(OwnerScope owner, string? rulesetId = null);

    RulePackInstallRecord? Get(OwnerScope owner, string packId, string version, string rulesetId);

    RulePackInstallRecord Upsert(OwnerScope owner, RulePackInstallRecord record);
}
