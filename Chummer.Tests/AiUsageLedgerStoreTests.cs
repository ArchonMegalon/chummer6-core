#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiUsageLedgerStoreTests
{
    [TestMethod]
    public void In_memory_usage_ledger_tracks_monthly_consumption_by_owner_and_route()
    {
        InMemoryAiUsageLedgerStore store = new();
        DateTimeOffset asOfUtc = new(2026, 03, 07, 12, 00, 00, TimeSpan.Zero);

        store.RecordUsage(OwnerScope.LocalSingleUser, AiRouteTypes.Coach, 2, asOfUtc);
        store.RecordUsage(OwnerScope.LocalSingleUser, AiRouteTypes.Chat, 1, asOfUtc);

        Assert.AreEqual(2, store.GetMonthlyConsumed(OwnerScope.LocalSingleUser, AiRouteTypes.Coach, asOfUtc));
        Assert.AreEqual(3, store.GetMonthlyConsumedByRoute(OwnerScope.LocalSingleUser, asOfUtc).Values.Sum());
    }

    [TestMethod]
    public void In_memory_usage_ledger_tracks_recent_consumption_windows_for_burst_limits()
    {
        InMemoryAiUsageLedgerStore store = new();
        DateTimeOffset now = new(2026, 03, 07, 12, 00, 30, TimeSpan.Zero);

        store.RecordUsage(OwnerScope.LocalSingleUser, AiRouteTypes.Coach, 1, now.AddSeconds(-50));
        store.RecordUsage(OwnerScope.LocalSingleUser, AiRouteTypes.Coach, 2, now.AddSeconds(-10));
        store.RecordUsage(OwnerScope.LocalSingleUser, AiRouteTypes.Coach, 5, now.AddMinutes(-2));

        Assert.AreEqual(3, store.GetConsumedBetween(OwnerScope.LocalSingleUser, AiRouteTypes.Coach, now.AddMinutes(-1), now.AddTicks(1)));
    }

    [TestMethod]
    public void File_usage_ledger_roundtrips_owner_scoped_monthly_consumption()
    {
        string stateDirectory = Path.Combine(Path.GetTempPath(), $"chummer-ai-usage-{Guid.NewGuid():N}");

        try
        {
            FileAiUsageLedgerStore store = new(stateDirectory);
            DateTimeOffset asOfUtc = new(2026, 03, 07, 12, 00, 00, TimeSpan.Zero);

            store.RecordUsage(OwnerScope.LocalSingleUser, AiRouteTypes.Build, 3, asOfUtc);

            FileAiUsageLedgerStore reloadedStore = new(stateDirectory);
            Assert.AreEqual(3, reloadedStore.GetMonthlyConsumed(OwnerScope.LocalSingleUser, AiRouteTypes.Build, asOfUtc));
        }
        finally
        {
            if (Directory.Exists(stateDirectory))
            {
                Directory.Delete(stateDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void File_usage_ledger_roundtrips_recent_consumption_windows()
    {
        string stateDirectory = Path.Combine(Path.GetTempPath(), $"chummer-ai-usage-window-{Guid.NewGuid():N}");

        try
        {
            FileAiUsageLedgerStore store = new(stateDirectory);
            DateTimeOffset now = new(2026, 03, 07, 12, 00, 30, TimeSpan.Zero);

            store.RecordUsage(OwnerScope.LocalSingleUser, AiRouteTypes.Build, 1, now.AddSeconds(-45));
            store.RecordUsage(OwnerScope.LocalSingleUser, AiRouteTypes.Build, 2, now.AddSeconds(-5));

            FileAiUsageLedgerStore reloadedStore = new(stateDirectory);
            Assert.AreEqual(3, reloadedStore.GetConsumedBetween(OwnerScope.LocalSingleUser, AiRouteTypes.Build, now.AddMinutes(-1), now.AddTicks(1)));
        }
        finally
        {
            if (Directory.Exists(stateDirectory))
            {
                Directory.Delete(stateDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Not_implemented_gateway_turns_increment_budget_consumption_for_status_and_response()
    {
        InMemoryAiUsageLedgerStore usageLedgerStore = new();
        IAiRouteBudgetPolicyCatalog routeBudgetPolicyCatalog = new StaticAiRouteBudgetPolicyCatalog();
        NotImplementedAiGatewayService service = new(
            routeBudgetPolicyCatalog: routeBudgetPolicyCatalog,
            usageLedgerStore: usageLedgerStore);

        AiConversationTurnResponse response = service.SendCoachTurn(
            OwnerScope.LocalSingleUser,
            new AiConversationTurnRequest(
                Message: "Burn one coach unit.",
                ConversationId: "conv-budget-1")).Payload
            ?? throw new AssertFailedException("Expected AI turn response.");
        AiGatewayStatusProjection status = service.GetStatus(OwnerScope.LocalSingleUser).Payload
            ?? throw new AssertFailedException("Expected AI gateway status projection.");

        Assert.AreEqual(1, response.Budget.MonthlyConsumed);
        Assert.AreEqual(1, response.Budget.CurrentBurstConsumed);
        Assert.AreEqual(1, status.Budget.MonthlyConsumed);
        Assert.AreEqual(1, usageLedgerStore.GetMonthlyConsumed(OwnerScope.LocalSingleUser, AiRouteTypes.Coach, DateTimeOffset.UtcNow));
    }

    private sealed class StaticAiRouteBudgetPolicyCatalog : IAiRouteBudgetPolicyCatalog
    {
        private readonly IReadOnlyList<AiRouteBudgetPolicyDescriptor> _policies = AiGatewayDefaults.CreateRouteBudgets();

        public IReadOnlyList<AiRouteBudgetPolicyDescriptor> ListPolicies()
            => _policies;

        public AiRouteBudgetPolicyDescriptor GetPolicy(string routeType)
            => _policies.Single(policy => string.Equals(policy.RouteType, routeType, StringComparison.Ordinal));
    }
}
