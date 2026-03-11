using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRuleProfileManifestStore
{
    IReadOnlyList<RuleProfileManifestRecord> List(OwnerScope owner, string? rulesetId = null);

    RuleProfileManifestRecord? Get(OwnerScope owner, string profileId, string rulesetId);

    RuleProfileManifestRecord Upsert(OwnerScope owner, RuleProfileManifestRecord record);
}
