using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiBudgetService
{
    AiBudgetSnapshot GetBudget(OwnerScope owner, string routeType);
}
