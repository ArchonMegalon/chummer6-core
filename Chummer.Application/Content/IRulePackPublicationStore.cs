using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRulePackPublicationStore
{
    IReadOnlyList<RulePackPublicationRecord> List(OwnerScope owner, string? rulesetId = null);

    RulePackPublicationRecord? Get(OwnerScope owner, string packId, string version, string rulesetId);

    RulePackPublicationRecord Upsert(OwnerScope owner, RulePackPublicationRecord record);
}
