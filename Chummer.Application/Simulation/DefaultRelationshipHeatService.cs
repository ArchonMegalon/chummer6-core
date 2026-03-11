using Chummer.Contracts.Simulation;

namespace Chummer.Application.Simulation;

public sealed class DefaultRelationshipHeatService : IRelationshipHeatService
{
    public HeatComputationResult ComputeHeat(HeatComputationInput input)
    {
        decimal composite = Math.Max(0m, input.MatrixHeat)
            + Math.Max(0m, input.LawEnforcementHeat)
            + Math.Max(0m, input.CorpAttention)
            + Math.Max(0m, input.MagicalAttention)
            + Math.Max(0m, input.DistrictAlertness);

        string threshold = composite switch
        {
            < 20m => "heat.low",
            < 40m => "heat.medium",
            < 70m => "heat.high",
            _ => "heat.critical"
        };

        return new HeatComputationResult(composite, threshold);
    }

    public PublicAwarenessComputationResult ComputePublicAwareness(PublicAwarenessComputationInput input)
    {
        decimal target = Math.Max(0m, input.ExistingPublicAwareness + (input.Notoriety * 0.5m) - (input.StreetCred * 0.25m));
        return new PublicAwarenessComputationResult(target, target - input.ExistingPublicAwareness);
    }

    public FavorDebtComputationResult ComputeFavorDebt(FavorDebtComputationInput input)
    {
        decimal delta = input.IsRepayment ? -Math.Abs(input.FavorValue) : Math.Abs(input.FavorValue);
        return new FavorDebtComputationResult(Math.Max(0m, input.ExistingDebt + delta));
    }

    public DowntimeProgressionResult ComputeDowntimeProgression(DowntimeProgressionInput input)
    {
        decimal recovered = Math.Max(0m, input.Days) * Math.Max(0m, input.RecoveryRatePerDay);
        return new DowntimeProgressionResult(Math.Max(0m, input.InitialBurden - recovered));
    }

    public AddictionScheduleResult ComputeAddictionSchedule(AddictionScheduleInput input)
    {
        int cadenceDays = input.Severity switch
        {
            < 2m => 14,
            < 4m => 7,
            _ => 3
        };
        bool requiresCheck = input.DaysElapsed >= cadenceDays;
        return new AddictionScheduleResult(requiresCheck, requiresCheck ? "addiction.check.required" : "addiction.check.not-required");
    }

    public HealingScheduleResult ComputeHealingSchedule(HealingScheduleInput input)
    {
        decimal healed = Math.Max(0, input.DaysElapsed) * Math.Max(0m, input.HealRatePerDay);
        return new HealingScheduleResult(Math.Max(0m, input.Damage - healed));
    }

    public FactionResponseSeed ComputeFactionResponseSeed(string factionId, decimal hostility, decimal exposure)
    {
        decimal response = Math.Max(0m, hostility) + (Math.Max(0m, exposure) * 0.75m);
        string level = response switch
        {
            < 20m => "faction.response.low",
            < 45m => "faction.response.medium",
            < 75m => "faction.response.high",
            _ => "faction.response.extreme"
        };

        return new FactionResponseSeed(
            factionId,
            response,
            [level]);
    }
}
