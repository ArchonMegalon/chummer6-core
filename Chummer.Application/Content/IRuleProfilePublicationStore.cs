using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRuleProfilePublicationStore
{
    IReadOnlyList<RuleProfilePublicationRecord> List(OwnerScope owner, string? rulesetId = null);

    RuleProfilePublicationRecord? Get(OwnerScope owner, string profileId, string rulesetId);

    RuleProfilePublicationRecord Upsert(OwnerScope owner, RuleProfilePublicationRecord record);
}
