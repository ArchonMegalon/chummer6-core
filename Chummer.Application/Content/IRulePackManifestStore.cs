using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRulePackManifestStore
{
    IReadOnlyList<RulePackManifestRecord> List(OwnerScope owner, string? rulesetId = null);

    RulePackManifestRecord? Get(OwnerScope owner, string packId, string version, string rulesetId);

    RulePackManifestRecord Upsert(OwnerScope owner, RulePackManifestRecord record);
}
