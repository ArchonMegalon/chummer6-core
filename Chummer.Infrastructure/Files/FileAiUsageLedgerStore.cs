using System.Text.Json;
using System.Linq;
using Chummer.Application.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Files;

public sealed class FileAiUsageLedgerStore : IAiUsageLedgerStore
{
    private static readonly TimeSpan RecentEventRetention = TimeSpan.FromHours(2);

    private readonly string _stateDirectory;

    public FileAiUsageLedgerStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public int GetMonthlyConsumed(OwnerScope owner, string routeType, DateTimeOffset asOfUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeType);

        return Load(owner).MonthlyTotals
            .Where(entry => string.Equals(entry.PeriodKey, ResolvePeriodKey(asOfUtc), StringComparison.Ordinal)
                && string.Equals(entry.RouteType, routeType.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            .Sum(static entry => entry.ConsumedUnits);
    }

    public IReadOnlyDictionary<string, int> GetMonthlyConsumedByRoute(OwnerScope owner, DateTimeOffset asOfUtc)
        => Load(owner).MonthlyTotals
            .Where(entry => string.Equals(entry.PeriodKey, ResolvePeriodKey(asOfUtc), StringComparison.Ordinal))
            .GroupBy(static entry => entry.RouteType, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Sum(entry => entry.ConsumedUnits),
                StringComparer.Ordinal);

    public int GetConsumedBetween(OwnerScope owner, string routeType, DateTimeOffset fromInclusiveUtc, DateTimeOffset toExclusiveUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeType);

        string normalizedRouteType = routeType.Trim().ToLowerInvariant();
        return Load(owner).RecentEvents
            .Where(entry => string.Equals(entry.RouteType, normalizedRouteType, StringComparison.Ordinal)
                && entry.RecordedAtUtc >= fromInclusiveUtc
                && entry.RecordedAtUtc < toExclusiveUtc)
            .Sum(static entry => entry.ConsumedUnits);
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
        StoredUsageLedger ledger = Load(owner);
        List<StoredUsageEntry> entries = ledger.MonthlyTotals.ToList();
        int existingIndex = entries.FindIndex(entry =>
            string.Equals(entry.PeriodKey, periodKey, StringComparison.Ordinal)
            && string.Equals(entry.RouteType, normalizedRouteType, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            StoredUsageEntry existing = entries[existingIndex];
            entries[existingIndex] = existing with
            {
                ConsumedUnits = existing.ConsumedUnits + consumedUnits
            };
        }
        else
        {
            entries.Add(new StoredUsageEntry(periodKey, normalizedRouteType, consumedUnits));
        }

        List<StoredUsageEvent> recentEvents = ledger.RecentEvents
            .Where(entry => entry.RecordedAtUtc >= recordedAtUtc - RecentEventRetention)
            .ToList();
        recentEvents.Add(new StoredUsageEvent(normalizedRouteType, recordedAtUtc, consumedUnits));

        Save(owner, new StoredUsageLedger(entries, recentEvents));
    }

    private StoredUsageLedger Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return new StoredUsageLedger([], []);
        }

        string text = File.ReadAllText(path);
        try
        {
            StoredUsageLedger? ledger = JsonSerializer.Deserialize<StoredUsageLedger>(text);
            if (ledger is not null)
            {
                return ledger with
                {
                    MonthlyTotals = ledger.MonthlyTotals ?? [],
                    RecentEvents = ledger.RecentEvents ?? []
                };
            }
        }
        catch (JsonException)
        {
            // Fall through to the legacy monthly-total array format.
        }

        List<StoredUsageEntry>? legacyEntries = JsonSerializer.Deserialize<List<StoredUsageEntry>>(text);
        return new StoredUsageLedger(legacyEntries ?? [], []);
    }

    private void Save(OwnerScope owner, StoredUsageLedger ledger)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(ledger));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "ai", "usage-ledger.json");
    }

    private static string ResolvePeriodKey(DateTimeOffset asOfUtc)
        => $"{asOfUtc.UtcDateTime.Year:D4}-{asOfUtc.UtcDateTime.Month:D2}";

    private sealed record StoredUsageLedger(
        IReadOnlyList<StoredUsageEntry> MonthlyTotals,
        IReadOnlyList<StoredUsageEvent> RecentEvents);

    private sealed record StoredUsageEntry(
        string PeriodKey,
        string RouteType,
        int ConsumedUnits);

    private sealed record StoredUsageEvent(
        string RouteType,
        DateTimeOffset RecordedAtUtc,
        int ConsumedUnits);
}
