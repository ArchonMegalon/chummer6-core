using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Hub;

public interface IHubProjectCompatibilityService
{
    HubProjectCompatibilityMatrix? GetMatrix(OwnerScope owner, string kind, string itemId, string? rulesetId = null);
}
