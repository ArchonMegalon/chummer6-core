using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IActiveRuntimeStatusService
{
    ActiveRuntimeStatusProjection? GetActiveProfileStatus(OwnerScope owner, string? rulesetId = null);
}
