using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IBuildKitRegistryService
{
    IReadOnlyList<BuildKitRegistryEntry> List(OwnerScope owner, string? rulesetId = null);

    BuildKitRegistryEntry? Get(OwnerScope owner, string buildKitId, string? rulesetId = null);
}
