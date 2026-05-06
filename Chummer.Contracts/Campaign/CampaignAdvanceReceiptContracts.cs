using Chummer.Contracts.Seeds;

namespace Chummer.Contracts.Campaign;

public static class CampaignAdvanceReceiptFamilies
{
    public const string AdoptionRunnerGoalAndBlackLedger = "family:campaign_adoption_runner_goal_and_black_ledger";
}

public static class CampaignAdoptionConfidencePostures
{
    public const string Governed = "governed";
    public const string ReviewRequired = "review-required";
    public const string Blocked = "blocked";
}

public static class RunnerGoalUpdateStates
{
    public const string Unchanged = "unchanged";
    public const string Advanced = "advanced";
    public const string Achieved = "achieved";
}

public static class BlackLedgerConsequencePostures
{
    public const string Governed = "governed";
    public const string ReviewRequired = "review-required";
    public const string Blocked = "blocked";
}

public static class BlackLedgerConsequenceAudiences
{
    public const string PlayerSafe = "player-safe";
    public const string GmOnly = "gm-only";
}

public static class CampaignAdvanceBurdenStates
{
    public const string Clear = "clear";
    public const string Reduced = "reduced";
    public const string Unchanged = "unchanged";
}

public sealed record CampaignAdoptionInput(
    string CampaignId,
    string RulesetId,
    IReadOnlyList<string> AdoptedRunnerIds,
    IReadOnlyList<string> MissingRunnerIds,
    IReadOnlyList<string> ConflictTags,
    IReadOnlyList<string>? CrewContextTags = null);

public sealed record RewardEventInput(
    string RewardEventId,
    string RewardKind,
    decimal Amount,
    string Unit,
    decimal GoalProgressDelta,
    IReadOnlyList<string>? RewardTags = null);

public sealed record DowntimeAllocationInput(
    int Days,
    decimal RecoveryRatePerDay,
    decimal InitialBurden,
    IReadOnlyList<string>? ActivityTags = null);

public sealed record RunnerGoalUpdateInput(
    string GoalId,
    string GoalTitle,
    decimal CurrentProgress,
    decimal TargetProgress,
    decimal RewardProgressDelta = 0m,
    decimal DowntimeProgressDelta = 0m,
    IReadOnlyList<string>? GoalTags = null);

public sealed record BlackLedgerConsequenceInput(
    string ResolutionReportId,
    string RunId,
    string FeedId,
    string FactionId,
    decimal Hostility,
    decimal Exposure,
    IReadOnlyList<string>? ConsequenceTags = null,
    IReadOnlyList<string>? SpoilerTags = null);

public sealed record CampaignAdvanceReceiptInput(
    string CampaignId,
    string RunnerId,
    string RulesetId,
    CampaignAdoptionInput Adoption,
    RewardEventInput Reward,
    DowntimeAllocationInput Downtime,
    RunnerGoalUpdateInput Goal,
    BlackLedgerConsequenceInput Consequence);

public sealed record CampaignAdoptionConfidenceReceipt(
    string ReceiptId,
    string CampaignId,
    string RulesetId,
    string Posture,
    int AdoptedRunnerCount,
    int MissingRunnerCount,
    int ConflictCount,
    IReadOnlyList<string> AdoptedRunnerIds,
    IReadOnlyList<string> MissingRunnerIds,
    IReadOnlyList<string> ConflictTags,
    IReadOnlyList<string> CrewContextTags,
    string Summary,
    string NextSafeAction);

public sealed record RunnerRewardReceipt(
    string ReceiptId,
    string RewardEventId,
    string RewardKind,
    decimal Amount,
    string Unit,
    decimal GoalProgressDelta,
    IReadOnlyList<string> RewardTags,
    string Summary);

public sealed record DowntimeAllocationReceipt(
    string ReceiptId,
    int Days,
    decimal InitialBurden,
    decimal RecoveryRatePerDay,
    decimal RecoveredBurden,
    decimal RemainingBurden,
    string BurdenState,
    IReadOnlyList<string> ActivityTags,
    string Summary);

public sealed record RunnerGoalUpdateReceipt(
    string ReceiptId,
    string GoalId,
    string GoalTitle,
    string State,
    decimal PreviousProgress,
    decimal RewardProgressDelta,
    decimal DowntimeProgressDelta,
    decimal AppliedProgressDelta,
    decimal UpdatedProgress,
    decimal TargetProgress,
    IReadOnlyList<string> GoalTags,
    string Summary);

public sealed record BlackLedgerConsequenceReceipt(
    string ReceiptId,
    string ResolutionReportId,
    string WorldTickId,
    string NewsItemId,
    string Posture,
    string Audience,
    string FactionId,
    decimal ResponseScore,
    string ResponseThresholdKey,
    IReadOnlyList<string> ConsequenceTags,
    IReadOnlyList<string> SpoilerTags,
    IReadOnlyList<string> NewsTopicTags,
    IReadOnlyList<string> NewsFactionTags,
    string Summary,
    string NextSafeAction);

public sealed record CampaignAdvanceDeterministicReceipt(
    string ParityFamilyId,
    string ReceiptId,
    string CampaignId,
    string RunnerId,
    string RulesetId,
    string AdoptionPosture,
    string GoalState,
    string ConsequencePosture,
    decimal GoalProgressDelta,
    decimal RemainingDowntimeBurden,
    string ResolutionReportId,
    string WorldTickId,
    string NewsItemId,
    IReadOnlyList<string> ConsequenceTags,
    IReadOnlyList<string> RewardTags,
    IReadOnlyList<string> AdoptionConflictTags);

public sealed record CampaignAdvanceReceiptBundle(
    CampaignAdoptionConfidenceReceipt Adoption,
    RunnerRewardReceipt Reward,
    DowntimeAllocationReceipt Downtime,
    RunnerGoalUpdateReceipt Goal,
    BlackLedgerConsequenceReceipt Consequence,
    CampaignAdvanceDeterministicReceipt DeterministicReceipt,
    RunSummarySeed RunSummarySeed,
    ShadowfeedSeed ShadowfeedSeed);
