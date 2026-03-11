using Chummer.Contracts.AI;

namespace Chummer.Application.AI;

public interface IAiRouteBudgetPolicyCatalog
{
    IReadOnlyList<AiRouteBudgetPolicyDescriptor> ListPolicies();

    AiRouteBudgetPolicyDescriptor GetPolicy(string routeType);
}
