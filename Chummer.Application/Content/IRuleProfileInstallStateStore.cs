using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRuleProfileInstallStateStore
{
    IReadOnlyList<RuleProfileInstallRecord> List(OwnerScope owner, string? rulesetId = null);

    RuleProfileInstallRecord? Get(OwnerScope owner, string profileId, string rulesetId);

    RuleProfileInstallRecord Upsert(OwnerScope owner, RuleProfileInstallRecord record);
}
