using System.Linq;
using Chummer.Application.AI;
using Chummer.Contracts.AI;

namespace Chummer.Infrastructure.AI;

public sealed class EnvironmentAiRouteBudgetPolicyCatalog : IAiRouteBudgetPolicyCatalog
{
    public const string ChatMonthlyAllowanceEnvironmentVariable = "CHUMMER_AI_CHAT_MONTHLY_ALLOWANCE";
    public const string ChatBurstLimitEnvironmentVariable = "CHUMMER_AI_CHAT_BURST_LIMIT_PER_MINUTE";
    public const string CoachMonthlyAllowanceEnvironmentVariable = "CHUMMER_AI_COACH_MONTHLY_ALLOWANCE";
    public const string CoachBurstLimitEnvironmentVariable = "CHUMMER_AI_COACH_BURST_LIMIT_PER_MINUTE";
    public const string BuildMonthlyAllowanceEnvironmentVariable = "CHUMMER_AI_BUILD_MONTHLY_ALLOWANCE";
    public const string BuildBurstLimitEnvironmentVariable = "CHUMMER_AI_BUILD_BURST_LIMIT_PER_MINUTE";
    public const string DocsMonthlyAllowanceEnvironmentVariable = "CHUMMER_AI_DOCS_MONTHLY_ALLOWANCE";
    public const string DocsBurstLimitEnvironmentVariable = "CHUMMER_AI_DOCS_BURST_LIMIT_PER_MINUTE";
    public const string RecapMonthlyAllowanceEnvironmentVariable = "CHUMMER_AI_RECAP_MONTHLY_ALLOWANCE";
    public const string RecapBurstLimitEnvironmentVariable = "CHUMMER_AI_RECAP_BURST_LIMIT_PER_MINUTE";

    public IReadOnlyList<AiRouteBudgetPolicyDescriptor> ListPolicies()
        => AiGatewayDefaults.CreateRouteBudgets()
            .Select(ApplyEnvironmentOverride)
            .ToArray();

    public AiRouteBudgetPolicyDescriptor GetPolicy(string routeType)
        => ListPolicies()
            .FirstOrDefault(policy => string.Equals(policy.RouteType, routeType, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Unknown AI route type '{routeType}'.");

    private static AiRouteBudgetPolicyDescriptor ApplyEnvironmentOverride(AiRouteBudgetPolicyDescriptor policy)
    {
        (string monthlyAllowanceVariable, string burstLimitVariable) = ResolveVariables(policy.RouteType);

        int monthlyAllowance = ResolveIntEnvironmentVariable(monthlyAllowanceVariable, policy.MonthlyAllowance);
        int burstLimit = ResolveIntEnvironmentVariable(burstLimitVariable, policy.BurstLimitPerMinute);

        return policy with
        {
            MonthlyAllowance = monthlyAllowance,
            BurstLimitPerMinute = burstLimit
        };
    }

    private static (string MonthlyAllowanceVariable, string BurstLimitVariable) ResolveVariables(string routeType)
        => routeType switch
        {
            AiRouteTypes.Chat => (ChatMonthlyAllowanceEnvironmentVariable, ChatBurstLimitEnvironmentVariable),
            AiRouteTypes.Coach => (CoachMonthlyAllowanceEnvironmentVariable, CoachBurstLimitEnvironmentVariable),
            AiRouteTypes.Build => (BuildMonthlyAllowanceEnvironmentVariable, BuildBurstLimitEnvironmentVariable),
            AiRouteTypes.Docs => (DocsMonthlyAllowanceEnvironmentVariable, DocsBurstLimitEnvironmentVariable),
            AiRouteTypes.Recap => (RecapMonthlyAllowanceEnvironmentVariable, RecapBurstLimitEnvironmentVariable),
            _ => throw new InvalidOperationException($"Unknown AI route type '{routeType}'.")
        };

    private static int ResolveIntEnvironmentVariable(string environmentVariable, int fallbackValue)
    {
        string? raw = Environment.GetEnvironmentVariable(environmentVariable);
        return int.TryParse(raw, out int parsed) && parsed >= 0
            ? parsed
            : fallbackValue;
    }
}
