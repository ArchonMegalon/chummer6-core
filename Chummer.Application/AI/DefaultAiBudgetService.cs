using System;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class DefaultAiBudgetService(
    IAiRouteBudgetPolicyCatalog? routeBudgetPolicyCatalog = null,
    IAiUsageLedgerStore? usageLedgerStore = null) : IAiBudgetService
{
    private readonly IAiRouteBudgetPolicyCatalog _routeBudgetPolicyCatalog = routeBudgetPolicyCatalog ?? new DefaultAiRouteBudgetPolicyCatalog();
    private readonly IAiUsageLedgerStore _usageLedgerStore = usageLedgerStore ?? new InMemoryAiUsageLedgerStore();

    public AiBudgetSnapshot GetBudget(OwnerScope owner, string routeType)
    {
        ArgumentNullException.ThrowIfNull(routeType);

        DateTimeOffset asOfUtc = DateTimeOffset.UtcNow;
        AiRouteBudgetPolicyDescriptor policy = _routeBudgetPolicyCatalog.GetPolicy(routeType);
        int monthlyConsumed = _usageLedgerStore.GetMonthlyConsumed(owner, routeType, asOfUtc);
        int currentBurstConsumed = _usageLedgerStore.GetConsumedBetween(owner, routeType, asOfUtc.AddMinutes(-1), asOfUtc.AddTicks(1));

        return new AiBudgetSnapshot(
            BudgetUnit: policy.BudgetUnit,
            MonthlyAllowance: policy.MonthlyAllowance,
            MonthlyConsumed: monthlyConsumed,
            BurstLimitPerMinute: policy.BurstLimitPerMinute,
            CurrentBurstConsumed: currentBurstConsumed,
            IsLimited: true);
    }
}
