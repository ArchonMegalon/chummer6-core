using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRulePackRegistryService
{
    IReadOnlyList<RulePackRegistryEntry> List(OwnerScope owner, string? rulesetId = null);

    RulePackRegistryEntry? Get(OwnerScope owner, string packId, string? rulesetId = null);
}
