using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiUsageLedgerStore
{
    int GetMonthlyConsumed(OwnerScope owner, string routeType, DateTimeOffset asOfUtc);

    IReadOnlyDictionary<string, int> GetMonthlyConsumedByRoute(OwnerScope owner, DateTimeOffset asOfUtc);

    int GetConsumedBetween(OwnerScope owner, string routeType, DateTimeOffset fromInclusiveUtc, DateTimeOffset toExclusiveUtc);

    void RecordUsage(OwnerScope owner, string routeType, int consumedUnits, DateTimeOffset recordedAtUtc);
}
