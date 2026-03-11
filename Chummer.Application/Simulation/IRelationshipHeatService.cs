using Chummer.Contracts.Simulation;

namespace Chummer.Application.Simulation;

public interface IRelationshipHeatService
{
    HeatComputationResult ComputeHeat(HeatComputationInput input);
    PublicAwarenessComputationResult ComputePublicAwareness(PublicAwarenessComputationInput input);
    FavorDebtComputationResult ComputeFavorDebt(FavorDebtComputationInput input);
    DowntimeProgressionResult ComputeDowntimeProgression(DowntimeProgressionInput input);
    AddictionScheduleResult ComputeAddictionSchedule(AddictionScheduleInput input);
    HealingScheduleResult ComputeHealingSchedule(HealingScheduleInput input);
    FactionResponseSeed ComputeFactionResponseSeed(string factionId, decimal hostility, decimal exposure);
}
