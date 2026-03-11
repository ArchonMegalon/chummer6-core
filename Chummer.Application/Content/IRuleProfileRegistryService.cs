using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRuleProfileRegistryService
{
    IReadOnlyList<RuleProfileRegistryEntry> List(OwnerScope owner, string? rulesetId = null);

    RuleProfileRegistryEntry? Get(OwnerScope owner, string profileId, string? rulesetId = null);
}
