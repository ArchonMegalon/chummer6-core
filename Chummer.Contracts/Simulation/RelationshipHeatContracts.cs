namespace Chummer.Contracts.Simulation;

public sealed record HeatComputationInput(
    decimal MatrixHeat,
    decimal LawEnforcementHeat,
    decimal CorpAttention,
    decimal MagicalAttention,
    decimal DistrictAlertness);

public sealed record HeatComputationResult(
    decimal CompositeHeat,
    string ThresholdKey);

public sealed record PublicAwarenessComputationInput(
    decimal Notoriety,
    decimal StreetCred,
    decimal ExistingPublicAwareness);

public sealed record PublicAwarenessComputationResult(
    decimal PublicAwareness,
    decimal Delta);

public sealed record FavorDebtComputationInput(
    decimal ExistingDebt,
    decimal FavorValue,
    bool IsRepayment);

public sealed record FavorDebtComputationResult(
    decimal UpdatedDebt);

public sealed record DowntimeProgressionInput(
    int Days,
    decimal RecoveryRatePerDay,
    decimal InitialBurden);

public sealed record DowntimeProgressionResult(
    decimal RemainingBurden);

public sealed record AddictionScheduleInput(
    decimal Severity,
    int DaysElapsed);

public sealed record AddictionScheduleResult(
    bool RequiresCheck,
    string CheckKey);

public sealed record HealingScheduleInput(
    decimal Damage,
    int DaysElapsed,
    decimal HealRatePerDay);

public sealed record HealingScheduleResult(
    decimal RemainingDamage);

public sealed record FactionResponseSeed(
    string FactionId,
    decimal ResponseScore,
    IReadOnlyList<string> ResponseTags);
