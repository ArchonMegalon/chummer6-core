using Chummer.Application.Seeds;
using Chummer.Application.Simulation;
using Chummer.Contracts.Campaign;
using Chummer.Contracts.Seeds;
using Chummer.Contracts.Simulation;

namespace Chummer.Application.Campaign;

public sealed class DefaultCampaignAdvanceReceiptService : ICampaignAdvanceReceiptService
{
    private readonly IRelationshipHeatService _relationshipHeatService;
    private readonly ISemanticSeedService _semanticSeedService;

    public DefaultCampaignAdvanceReceiptService(
        IRelationshipHeatService relationshipHeatService,
        ISemanticSeedService semanticSeedService)
    {
        _relationshipHeatService = relationshipHeatService ?? throw new ArgumentNullException(nameof(relationshipHeatService));
        _semanticSeedService = semanticSeedService ?? throw new ArgumentNullException(nameof(semanticSeedService));
    }

    public CampaignAdvanceReceiptBundle Build(CampaignAdvanceReceiptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        CampaignAdoptionConfidenceReceipt adoption = BuildAdoptionReceipt(input.Adoption);
        RunnerRewardReceipt reward = BuildRewardReceipt(input.Reward);
        DowntimeAllocationReceipt downtime = BuildDowntimeReceipt(input.RunnerId, input.Downtime);
        RunnerGoalUpdateReceipt goal = BuildGoalReceipt(input.RunnerId, input.Goal);
        RunSummarySeed runSummarySeed = _semanticSeedService.BuildRunSummarySeed(input.Consequence.RunId, input.RulesetId);
        ShadowfeedSeed shadowfeedSeed = _semanticSeedService.BuildShadowfeedSeed(input.Consequence.FeedId, input.RulesetId);
        BlackLedgerConsequenceReceipt consequence = BuildConsequenceReceipt(
            input.CampaignId,
            input.RulesetId,
            adoption,
            input.Consequence,
            runSummarySeed,
            shadowfeedSeed);

        return new CampaignAdvanceReceiptBundle(
            Adoption: adoption,
            Reward: reward,
            Downtime: downtime,
            Goal: goal,
            Consequence: consequence,
            DeterministicReceipt: new CampaignAdvanceDeterministicReceipt(
                ParityFamilyId: CampaignAdvanceReceiptFamilies.AdoptionRunnerGoalAndBlackLedger,
                ReceiptId: BuildReceiptId("campaign-advance", input.CampaignId, input.RunnerId, input.Consequence.ResolutionReportId),
                CampaignId: input.CampaignId,
                RunnerId: input.RunnerId,
                RulesetId: input.RulesetId,
                AdoptionPosture: adoption.Posture,
                GoalState: goal.State,
                ConsequencePosture: consequence.Posture,
                GoalProgressDelta: goal.AppliedProgressDelta,
                RemainingDowntimeBurden: downtime.RemainingBurden,
                ResolutionReportId: input.Consequence.ResolutionReportId,
                WorldTickId: consequence.WorldTickId,
                NewsItemId: consequence.NewsItemId,
                ConsequenceTags: consequence.ConsequenceTags,
                RewardTags: reward.RewardTags,
                AdoptionConflictTags: adoption.ConflictTags),
            RunSummarySeed: runSummarySeed,
            ShadowfeedSeed: shadowfeedSeed);
    }

    private static CampaignAdoptionConfidenceReceipt BuildAdoptionReceipt(CampaignAdoptionInput input)
    {
        string posture = input.MissingRunnerIds.Count > 0
            ? CampaignAdoptionConfidencePostures.Blocked
            : input.ConflictTags.Count > 0
                ? CampaignAdoptionConfidencePostures.ReviewRequired
                : CampaignAdoptionConfidencePostures.Governed;

        string nextSafeAction = posture switch
        {
            CampaignAdoptionConfidencePostures.Blocked => "Restore missing runner mappings before publishing adoption.",
            CampaignAdoptionConfidencePostures.ReviewRequired => "Review campaign context conflicts before promoting the adopted roster.",
            _ => "Adoption confidence is governed and can flow into runner-goal and consequence follow-through."
        };

        string summary = posture switch
        {
            CampaignAdoptionConfidencePostures.Blocked => $"Adoption is blocked until {input.MissingRunnerIds.Count} missing runner mapping(s) are restored.",
            CampaignAdoptionConfidencePostures.ReviewRequired => $"Adoption is review-required because {input.ConflictTags.Count} conflict tag(s) remain.",
            _ => $"Adoption is governed for {input.AdoptedRunnerIds.Count} mapped runner(s)."
        };

        return new CampaignAdoptionConfidenceReceipt(
            ReceiptId: BuildReceiptId("campaign-adoption", input.CampaignId),
            CampaignId: input.CampaignId,
            RulesetId: input.RulesetId,
            Posture: posture,
            AdoptedRunnerCount: input.AdoptedRunnerIds.Count,
            MissingRunnerCount: input.MissingRunnerIds.Count,
            ConflictCount: input.ConflictTags.Count,
            AdoptedRunnerIds: NormalizeDistinct(input.AdoptedRunnerIds),
            MissingRunnerIds: NormalizeDistinct(input.MissingRunnerIds),
            ConflictTags: NormalizeDistinct(input.ConflictTags),
            CrewContextTags: NormalizeDistinct(input.CrewContextTags),
            Summary: summary,
            NextSafeAction: nextSafeAction);
    }

    private static RunnerRewardReceipt BuildRewardReceipt(RewardEventInput input)
    {
        string normalizedKind = NormalizeToken(input.RewardKind, "reward");
        string normalizedUnit = NormalizeToken(input.Unit, "unit");
        string summary = $"{normalizedKind} reward posts {Math.Max(0m, input.Amount):0.##} {normalizedUnit} and contributes {Math.Max(0m, input.GoalProgressDelta):0.##} goal progress.";

        return new RunnerRewardReceipt(
            ReceiptId: BuildReceiptId("reward", input.RewardEventId),
            RewardEventId: input.RewardEventId,
            RewardKind: normalizedKind,
            Amount: Math.Max(0m, input.Amount),
            Unit: normalizedUnit,
            GoalProgressDelta: Math.Max(0m, input.GoalProgressDelta),
            RewardTags: NormalizeDistinct(input.RewardTags),
            Summary: summary);
    }

    private DowntimeAllocationReceipt BuildDowntimeReceipt(string runnerId, DowntimeAllocationInput input)
    {
        DowntimeProgressionResult progression = _relationshipHeatService.ComputeDowntimeProgression(new DowntimeProgressionInput(
            Days: input.Days,
            RecoveryRatePerDay: input.RecoveryRatePerDay,
            InitialBurden: input.InitialBurden));
        decimal initialBurden = Math.Max(0m, input.InitialBurden);
        decimal remainingBurden = progression.RemainingBurden;
        decimal recoveredBurden = Math.Max(0m, initialBurden - remainingBurden);
        string burdenState = remainingBurden <= 0m
            ? CampaignAdvanceBurdenStates.Clear
            : recoveredBurden > 0m
                ? CampaignAdvanceBurdenStates.Reduced
                : CampaignAdvanceBurdenStates.Unchanged;

        return new DowntimeAllocationReceipt(
            ReceiptId: BuildReceiptId("downtime", runnerId, input.Days.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Days: Math.Max(0, input.Days),
            InitialBurden: initialBurden,
            RecoveryRatePerDay: Math.Max(0m, input.RecoveryRatePerDay),
            RecoveredBurden: recoveredBurden,
            RemainingBurden: remainingBurden,
            BurdenState: burdenState,
            ActivityTags: NormalizeDistinct(input.ActivityTags),
            Summary: $"Downtime clears {recoveredBurden:0.##} burden over {Math.Max(0, input.Days)} day(s), leaving {remainingBurden:0.##} remaining.");
    }

    private static RunnerGoalUpdateReceipt BuildGoalReceipt(string runnerId, RunnerGoalUpdateInput input)
    {
        decimal previousProgress = Math.Max(0m, input.CurrentProgress);
        decimal targetProgress = Math.Max(previousProgress, input.TargetProgress);
        decimal rewardProgressDelta = Math.Max(0m, input.RewardProgressDelta);
        decimal downtimeProgressDelta = Math.Max(0m, input.DowntimeProgressDelta);
        decimal appliedProgressDelta = rewardProgressDelta + downtimeProgressDelta;
        decimal updatedProgress = Math.Min(targetProgress, previousProgress + appliedProgressDelta);
        string state = updatedProgress >= targetProgress && targetProgress > 0m
            ? RunnerGoalUpdateStates.Achieved
            : appliedProgressDelta > 0m
                ? RunnerGoalUpdateStates.Advanced
                : RunnerGoalUpdateStates.Unchanged;

        return new RunnerGoalUpdateReceipt(
            ReceiptId: BuildReceiptId("goal-update", runnerId, input.GoalId),
            GoalId: input.GoalId,
            GoalTitle: input.GoalTitle.Trim(),
            State: state,
            PreviousProgress: previousProgress,
            RewardProgressDelta: rewardProgressDelta,
            DowntimeProgressDelta: downtimeProgressDelta,
            AppliedProgressDelta: appliedProgressDelta,
            UpdatedProgress: updatedProgress,
            TargetProgress: targetProgress,
            GoalTags: NormalizeDistinct(input.GoalTags),
            Summary: state switch
            {
                RunnerGoalUpdateStates.Achieved => $"Goal '{input.GoalTitle.Trim()}' is achieved at {updatedProgress:0.##}/{targetProgress:0.##}.",
                RunnerGoalUpdateStates.Advanced => $"Goal '{input.GoalTitle.Trim()}' advances to {updatedProgress:0.##}/{targetProgress:0.##}.",
                _ => $"Goal '{input.GoalTitle.Trim()}' stays unchanged at {updatedProgress:0.##}/{targetProgress:0.##}."
            });
    }

    private BlackLedgerConsequenceReceipt BuildConsequenceReceipt(
        string campaignId,
        string rulesetId,
        CampaignAdoptionConfidenceReceipt adoption,
        BlackLedgerConsequenceInput input,
        RunSummarySeed runSummarySeed,
        ShadowfeedSeed shadowfeedSeed)
    {
        FactionResponseSeed factionResponse = _relationshipHeatService.ComputeFactionResponseSeed(
            input.FactionId,
            input.Hostility,
            input.Exposure);
        string posture = adoption.Posture == CampaignAdoptionConfidencePostures.Blocked
            ? BlackLedgerConsequencePostures.Blocked
            : input.SpoilerTags is { Count: > 0 } || adoption.Posture == CampaignAdoptionConfidencePostures.ReviewRequired
                ? BlackLedgerConsequencePostures.ReviewRequired
                : BlackLedgerConsequencePostures.Governed;
        string audience = input.SpoilerTags is { Count: > 0 }
            ? BlackLedgerConsequenceAudiences.GmOnly
            : BlackLedgerConsequenceAudiences.PlayerSafe;
        string worldTickId = BuildReceiptId("world-tick", campaignId, input.ResolutionReportId);
        string newsItemId = BuildReceiptId("news-item", input.FeedId, input.ResolutionReportId);
        List<string> consequenceTags = NormalizeDistinct(input.ConsequenceTags)
            .Concat(runSummarySeed.HeatThresholdKeys.Where(static tag => !string.IsNullOrWhiteSpace(tag)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static tag => tag, StringComparer.Ordinal)
            .ToList();

        return new BlackLedgerConsequenceReceipt(
            ReceiptId: BuildReceiptId("black-ledger", input.ResolutionReportId),
            ResolutionReportId: input.ResolutionReportId,
            WorldTickId: worldTickId,
            NewsItemId: newsItemId,
            Posture: posture,
            Audience: audience,
            FactionId: input.FactionId.Trim(),
            ResponseScore: factionResponse.ResponseScore,
            ResponseThresholdKey: factionResponse.ResponseTags.FirstOrDefault() ?? "faction.response.low",
            ConsequenceTags: consequenceTags,
            SpoilerTags: NormalizeDistinct(input.SpoilerTags),
            NewsTopicTags: NormalizeDistinct(shadowfeedSeed.TopicTags),
            NewsFactionTags: NormalizeDistinct(shadowfeedSeed.FactionTags),
            Summary: $"{audience} consequence projects {factionResponse.ResponseScore:0.##} faction pressure and issues world tick '{worldTickId}' plus news item '{newsItemId}'.",
            NextSafeAction: posture switch
            {
                BlackLedgerConsequencePostures.Blocked => "Do not publish the BLACK LEDGER consequence until campaign adoption is no longer blocked.",
                BlackLedgerConsequencePostures.ReviewRequired => "Review spoiler and adoption conflict posture before publishing the player-facing consequence.",
                _ => "Consequence can publish one governed WorldTick and one player-safe news item."
            });
    }

    private static string BuildReceiptId(string prefix, params string[] parts)
    {
        string suffix = string.Join(
            ":",
            parts
                .Select(part => NormalizeToken(part, "unknown"))
                .Where(static part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrWhiteSpace(suffix) ? NormalizeToken(prefix, "receipt") : $"{NormalizeToken(prefix, "receipt")}:{suffix}";
    }

    private static string NormalizeToken(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().Replace(' ', '-').ToLowerInvariant();
    }

    private static IReadOnlyList<string> NormalizeDistinct(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return Array.Empty<string>();
        }

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }
}
