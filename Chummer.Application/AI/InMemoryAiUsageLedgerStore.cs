using System.Linq;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class InMemoryAiUsageLedgerStore : IAiUsageLedgerStore
{
    private static readonly TimeSpan RecentEventRetention = TimeSpan.FromHours(2);

    private readonly object _gate = new();
    private readonly Dictionary<(string OwnerId, string PeriodKey, string RouteType), int> _usage = new();
    private readonly List<UsageEvent> _recentEvents = [];

    public int GetMonthlyConsumed(OwnerScope owner, string routeType, DateTimeOffset asOfUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeType);

        lock (_gate)
        {
            return _usage.TryGetValue((owner.NormalizedValue, ResolvePeriodKey(asOfUtc), routeType.Trim().ToLowerInvariant()), out int consumed)
                ? consumed
                : 0;
        }
    }

    public IReadOnlyDictionary<string, int> GetMonthlyConsumedByRoute(OwnerScope owner, DateTimeOffset asOfUtc)
    {
        string ownerId = owner.NormalizedValue;
        string periodKey = ResolvePeriodKey(asOfUtc);

        lock (_gate)
        {
            return _usage
                .Where(pair => string.Equals(pair.Key.OwnerId, ownerId, StringComparison.Ordinal)
                    && string.Equals(pair.Key.PeriodKey, periodKey, StringComparison.Ordinal))
                .ToDictionary(
                    static pair => pair.Key.RouteType,
                    static pair => pair.Value,
                StringComparer.Ordinal);
        }
    }

    public int GetConsumedBetween(OwnerScope owner, string routeType, DateTimeOffset fromInclusiveUtc, DateTimeOffset toExclusiveUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeType);

        string ownerId = owner.NormalizedValue;
        string normalizedRouteType = routeType.Trim().ToLowerInvariant();

        lock (_gate)
        {
            return _recentEvents
                .Where(entry => string.Equals(entry.OwnerId, ownerId, StringComparison.Ordinal)
                    && string.Equals(entry.RouteType, normalizedRouteType, StringComparison.Ordinal)
                    && entry.RecordedAtUtc >= fromInclusiveUtc
                    && entry.RecordedAtUtc < toExclusiveUtc)
                .Sum(static entry => entry.ConsumedUnits);
        }
    }

    public void RecordUsage(OwnerScope owner, string routeType, int consumedUnits, DateTimeOffset recordedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeType);
        if (consumedUnits <= 0)
        {
            return;
        }

        string normalizedRouteType = routeType.Trim().ToLowerInvariant();
        string periodKey = ResolvePeriodKey(recordedAtUtc);

        lock (_gate)
        {
            PruneRecentEvents(recordedAtUtc);
            (string OwnerId, string PeriodKey, string RouteType) key = (owner.NormalizedValue, periodKey, normalizedRouteType);
            _usage[key] = _usage.TryGetValue(key, out int existing)
                ? existing + consumedUnits
                : consumedUnits;
            _recentEvents.Add(new UsageEvent(owner.NormalizedValue, normalizedRouteType, recordedAtUtc, consumedUnits));
        }
    }

    private static string ResolvePeriodKey(DateTimeOffset asOfUtc)
        => $"{asOfUtc.UtcDateTime.Year:D4}-{asOfUtc.UtcDateTime.Month:D2}";

    private void PruneRecentEvents(DateTimeOffset asOfUtc)
        => _recentEvents.RemoveAll(entry => entry.RecordedAtUtc < asOfUtc - RecentEventRetention);

    private sealed record UsageEvent(
        string OwnerId,
        string RouteType,
        DateTimeOffset RecordedAtUtc,
        int ConsumedUnits);
}
