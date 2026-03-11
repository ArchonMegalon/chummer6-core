#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiRouteBudgetPolicyCatalogTests
{
    private static readonly string[] EnvironmentVariables =
    [
        EnvironmentAiRouteBudgetPolicyCatalog.ChatMonthlyAllowanceEnvironmentVariable,
        EnvironmentAiRouteBudgetPolicyCatalog.ChatBurstLimitEnvironmentVariable,
        EnvironmentAiRouteBudgetPolicyCatalog.CoachMonthlyAllowanceEnvironmentVariable,
        EnvironmentAiRouteBudgetPolicyCatalog.CoachBurstLimitEnvironmentVariable,
        EnvironmentAiRouteBudgetPolicyCatalog.BuildMonthlyAllowanceEnvironmentVariable,
        EnvironmentAiRouteBudgetPolicyCatalog.BuildBurstLimitEnvironmentVariable,
        EnvironmentAiRouteBudgetPolicyCatalog.DocsMonthlyAllowanceEnvironmentVariable,
        EnvironmentAiRouteBudgetPolicyCatalog.DocsBurstLimitEnvironmentVariable,
        EnvironmentAiRouteBudgetPolicyCatalog.RecapMonthlyAllowanceEnvironmentVariable,
        EnvironmentAiRouteBudgetPolicyCatalog.RecapBurstLimitEnvironmentVariable
    ];

    [TestMethod]
    public void Environment_route_budget_catalog_overrides_route_defaults_from_environment()
    {
        Dictionary<string, string?> originalValues = CaptureEnvironmentValues();

        try
        {
            Environment.SetEnvironmentVariable(EnvironmentAiRouteBudgetPolicyCatalog.CoachMonthlyAllowanceEnvironmentVariable, "240");
            Environment.SetEnvironmentVariable(EnvironmentAiRouteBudgetPolicyCatalog.CoachBurstLimitEnvironmentVariable, "11");

            EnvironmentAiRouteBudgetPolicyCatalog catalog = new();
            AiRouteBudgetPolicyDescriptor coachBudget = catalog.GetPolicy(AiRouteTypes.Coach);

            Assert.AreEqual(240, coachBudget.MonthlyAllowance);
            Assert.AreEqual(11, coachBudget.BurstLimitPerMinute);
            Assert.AreEqual(AiBudgetUnits.ChummerAiUnits, coachBudget.BudgetUnit);
        }
        finally
        {
            RestoreEnvironmentValues(originalValues);
        }
    }

    [TestMethod]
    public void Default_budget_service_uses_route_budget_catalog_policy_values()
    {
        IAiRouteBudgetPolicyCatalog catalog = new InMemoryAiRouteBudgetPolicyCatalog(
            new AiRouteBudgetPolicyDescriptor(AiRouteTypes.Build, AiBudgetUnits.ChummerAiUnits, 222, 9, "Custom build policy."));
        DefaultAiBudgetService budgetService = new(catalog);

        AiBudgetSnapshot budget = budgetService.GetBudget(OwnerScope.LocalSingleUser, AiRouteTypes.Build);

        Assert.AreEqual(222, budget.MonthlyAllowance);
        Assert.AreEqual(9, budget.BurstLimitPerMinute);
    }

    [TestMethod]
    public void Not_implemented_gateway_status_uses_configured_route_budget_catalog_values()
    {
        IAiRouteBudgetPolicyCatalog catalog = new InMemoryAiRouteBudgetPolicyCatalog(
            new AiRouteBudgetPolicyDescriptor(AiRouteTypes.Chat, AiBudgetUnits.ChummerAiUnits, 350, 14, "Custom chat policy."),
            new AiRouteBudgetPolicyDescriptor(AiRouteTypes.Coach, AiBudgetUnits.ChummerAiUnits, 240, 11, "Custom coach policy."),
            new AiRouteBudgetPolicyDescriptor(AiRouteTypes.Build, AiBudgetUnits.ChummerAiUnits, 160, 7, "Custom build policy."),
            new AiRouteBudgetPolicyDescriptor(AiRouteTypes.Docs, AiBudgetUnits.ChummerAiUnits, 200, 9, "Custom docs policy."),
            new AiRouteBudgetPolicyDescriptor(AiRouteTypes.Recap, AiBudgetUnits.ChummerAiUnits, 120, 5, "Custom recap policy."));
        NotImplementedAiGatewayService service = new(routeBudgetPolicyCatalog: catalog);

        AiGatewayStatusProjection status = service.GetStatus(OwnerScope.LocalSingleUser).Payload
            ?? throw new AssertFailedException("Expected AI gateway status projection.");

        Assert.HasCount(5, status.RouteBudgets);
        Assert.AreEqual(350 + 240 + 160 + 200 + 120, status.Budget.MonthlyAllowance);
        Assert.AreEqual(14, status.Budget.BurstLimitPerMinute);
        Assert.AreEqual(240, status.RouteBudgets.Single(policy => policy.RouteType == AiRouteTypes.Coach).MonthlyAllowance);
    }

    private static Dictionary<string, string?> CaptureEnvironmentValues()
        => EnvironmentVariables.ToDictionary(static key => key, static key => Environment.GetEnvironmentVariable(key), StringComparer.Ordinal);

    private static void RestoreEnvironmentValues(IReadOnlyDictionary<string, string?> values)
    {
        foreach ((string key, string? value) in values)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private sealed class InMemoryAiRouteBudgetPolicyCatalog(params AiRouteBudgetPolicyDescriptor[] policies) : IAiRouteBudgetPolicyCatalog
    {
        private readonly IReadOnlyList<AiRouteBudgetPolicyDescriptor> _policies = policies;

        public IReadOnlyList<AiRouteBudgetPolicyDescriptor> ListPolicies()
            => _policies;

        public AiRouteBudgetPolicyDescriptor GetPolicy(string routeType)
            => _policies.Single(policy => string.Equals(policy.RouteType, routeType, StringComparison.Ordinal));
    }
}
