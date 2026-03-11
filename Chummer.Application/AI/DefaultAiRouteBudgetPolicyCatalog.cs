using Chummer.Contracts.AI;

namespace Chummer.Application.AI;

public sealed class DefaultAiRouteBudgetPolicyCatalog : IAiRouteBudgetPolicyCatalog
{
    public IReadOnlyList<AiRouteBudgetPolicyDescriptor> ListPolicies()
        => AiGatewayDefaults.CreateRouteBudgets();

    public AiRouteBudgetPolicyDescriptor GetPolicy(string routeType)
        => AiGatewayDefaults.ResolveRouteBudget(routeType);
}
