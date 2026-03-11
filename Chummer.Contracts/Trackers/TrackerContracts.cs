namespace Chummer.Contracts.Trackers;

public static class TrackerCategories
{
    public const string Condition = "condition";
    public const string Resource = "resource";
    public const string Reputation = "reputation";
    public const string Counter = "counter";
    public const string Index = "index";
}

public sealed record TrackerThresholdDefinition(
    string ThresholdId,
    int Value,
    string Label,
    string? Status = null);

public sealed record TrackerDefinition(
    string TrackerId,
    string Category,
    string Label,
    int DefaultValue,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<TrackerThresholdDefinition> Thresholds,
    bool SessionSafe = true);

public sealed record TrackerSnapshot(
    TrackerDefinition Definition,
    int CurrentValue,
    string? ThresholdState = null);
