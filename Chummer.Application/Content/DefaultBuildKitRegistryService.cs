using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public sealed class DefaultBuildKitRegistryService : IBuildKitRegistryService
{
    public IReadOnlyList<BuildKitRegistryEntry> List(OwnerScope owner, string? rulesetId = null) => [];

    public BuildKitRegistryEntry? Get(OwnerScope owner, string buildKitId, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildKitId);
        return null;
    }
}
