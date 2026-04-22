using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public interface ISessionActionBudgetService
{
    SessionActionBudgetResult Compute(SessionActionBudgetInput input);
}
