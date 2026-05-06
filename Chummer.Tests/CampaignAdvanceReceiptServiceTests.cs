#nullable enable annotations

using Chummer.Application.Campaign;
using Chummer.Application.Seeds;
using Chummer.Application.Simulation;
using Chummer.Contracts.Campaign;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class CampaignAdvanceReceiptServiceTests
{
    [TestMethod]
    public void Build_emits_governed_campaign_adoption_goal_and_black_ledger_receipts()
    {
        DefaultCampaignAdvanceReceiptService service = CreateService();

        CampaignAdvanceReceiptBundle result = service.Build(CreateInput());

        Assert.AreEqual(CampaignAdoptionConfidencePostures.Governed, result.Adoption.Posture);
        Assert.AreEqual(2, result.Adoption.AdoptedRunnerCount);
        CollectionAssert.AreEqual(new[] { "runner-1", "runner-2" }, result.Adoption.AdoptedRunnerIds.ToArray());
        Assert.AreEqual(6m, result.Reward.Amount);
        Assert.AreEqual(1.5m, result.Reward.GoalProgressDelta);
        Assert.AreEqual(6m, result.Downtime.RecoveredBurden);
        Assert.AreEqual(3m, result.Downtime.RemainingBurden);
        Assert.AreEqual(CampaignAdvanceBurdenStates.Reduced, result.Downtime.BurdenState);
        Assert.AreEqual(RunnerGoalUpdateStates.Advanced, result.Goal.State);
        Assert.AreEqual(4m, result.Goal.UpdatedProgress);
        Assert.AreEqual(2m, result.Goal.AppliedProgressDelta);
        Assert.AreEqual(BlackLedgerConsequencePostures.Governed, result.Consequence.Posture);
        Assert.AreEqual(BlackLedgerConsequenceAudiences.PlayerSafe, result.Consequence.Audience);
        Assert.AreEqual("world-tick:campaign-7:rr-2048", result.Consequence.WorldTickId);
        Assert.AreEqual("news-item:shadowfeed-2048:rr-2048", result.Consequence.NewsItemId);
        CollectionAssert.AreEqual(
            new[] { "operations", "security" },
            result.Consequence.NewsTopicTags.ToArray());
        CollectionAssert.AreEqual(
            new[] { "heat.low", "heat.medium", "heat.high", "intel", "security" },
            result.Consequence.ConsequenceTags.ToArray());
        Assert.IsNotNull(result.DeterministicReceipt);
        Assert.AreEqual(CampaignAdvanceReceiptFamilies.AdoptionRunnerGoalAndBlackLedger, result.DeterministicReceipt.ParityFamilyId);
        Assert.AreEqual("advanced", result.DeterministicReceipt.GoalState);
        Assert.AreEqual("governed", result.DeterministicReceipt.ConsequencePosture);
    }

    [TestMethod]
    public void Build_downgrades_adoption_and_consequence_when_runner_mapping_or_spoiler_state_drifts()
    {
        DefaultCampaignAdvanceReceiptService service = CreateService();

        CampaignAdvanceReceiptBundle result = service.Build(CreateInput() with
        {
            Adoption = new CampaignAdoptionInput(
                CampaignId: "campaign-7",
                RulesetId: "sr5",
                AdoptedRunnerIds: ["runner-1"],
                MissingRunnerIds: ["runner-2"],
                ConflictTags: ["duplicate-contact-map"]),
            Consequence = new BlackLedgerConsequenceInput(
                ResolutionReportId: "rr-2048",
                RunId: "run-2048",
                FeedId: "shadowfeed-2048",
                FactionId: "redmond-watch",
                Hostility: 24m,
                Exposure: 32m,
                ConsequenceTags: ["intel"],
                SpoilerTags: ["betrayal"])
        });

        Assert.AreEqual(CampaignAdoptionConfidencePostures.Blocked, result.Adoption.Posture);
        Assert.AreEqual(1, result.Adoption.MissingRunnerCount);
        CollectionAssert.AreEqual(new[] { "runner-2" }, result.Adoption.MissingRunnerIds.ToArray());
        Assert.AreEqual(BlackLedgerConsequencePostures.Blocked, result.Consequence.Posture);
        Assert.AreEqual(BlackLedgerConsequenceAudiences.GmOnly, result.Consequence.Audience);
        CollectionAssert.AreEqual(new[] { "betrayal" }, result.Consequence.SpoilerTags.ToArray());
        Assert.AreEqual("blocked", result.DeterministicReceipt.AdoptionPosture);
        Assert.AreEqual("blocked", result.DeterministicReceipt.ConsequencePosture);
    }

    [TestMethod]
    public void Build_marks_adoption_and_consequence_review_required_when_conflicts_remain_without_spoilers()
    {
        DefaultCampaignAdvanceReceiptService service = CreateService();

        CampaignAdvanceReceiptBundle result = service.Build(CreateInput() with
        {
            Adoption = new CampaignAdoptionInput(
                CampaignId: "campaign-7",
                RulesetId: "sr5",
                AdoptedRunnerIds: ["runner-1", "runner-2"],
                MissingRunnerIds: [],
                ConflictTags: ["duplicate-contact-map", "roster-ambiguity"])
        });

        Assert.AreEqual(CampaignAdoptionConfidencePostures.ReviewRequired, result.Adoption.Posture);
        Assert.AreEqual(2, result.Adoption.ConflictCount);
        CollectionAssert.AreEqual(new[] { "duplicate-contact-map", "roster-ambiguity" }, result.Adoption.ConflictTags.ToArray());
        Assert.AreEqual(BlackLedgerConsequencePostures.ReviewRequired, result.Consequence.Posture);
        Assert.AreEqual(BlackLedgerConsequenceAudiences.PlayerSafe, result.Consequence.Audience);
        Assert.AreEqual("review-required", result.DeterministicReceipt.AdoptionPosture);
        Assert.AreEqual("review-required", result.DeterministicReceipt.ConsequencePosture);
    }

    [TestMethod]
    public void Build_marks_goal_achieved_when_reward_and_downtime_finish_progress()
    {
        DefaultCampaignAdvanceReceiptService service = CreateService();

        CampaignAdvanceReceiptBundle result = service.Build(CreateInput() with
        {
            Goal = new RunnerGoalUpdateInput(
                GoalId: "goal-7",
                GoalTitle: "Clear the bounty",
                CurrentProgress: 4m,
                TargetProgress: 5m,
                RewardProgressDelta: 0.5m,
                DowntimeProgressDelta: 1m,
                GoalTags: ["heat", "survival"])
        });

        Assert.AreEqual(RunnerGoalUpdateStates.Achieved, result.Goal.State);
        Assert.AreEqual(5m, result.Goal.UpdatedProgress);
        Assert.AreEqual(1.5m, result.Goal.AppliedProgressDelta);
        CollectionAssert.AreEqual(new[] { "heat", "survival" }, result.Goal.GoalTags.ToArray());
    }

    private static DefaultCampaignAdvanceReceiptService CreateService()
    {
        return new DefaultCampaignAdvanceReceiptService(
            new DefaultRelationshipHeatService(),
            new DefaultSemanticSeedService(new DefaultAestheticDigestService()));
    }

    private static CampaignAdvanceReceiptInput CreateInput()
    {
        return new CampaignAdvanceReceiptInput(
            CampaignId: "campaign-7",
            RunnerId: "runner-1",
            RulesetId: "sr5",
            Adoption: new CampaignAdoptionInput(
                CampaignId: "campaign-7",
                RulesetId: "sr5",
                AdoptedRunnerIds: ["runner-1", "runner-2"],
                MissingRunnerIds: [],
                ConflictTags: [],
                CrewContextTags: ["street", "trust"]),
            Reward: new RewardEventInput(
                RewardEventId: "reward-2048",
                RewardKind: "karma",
                Amount: 6m,
                Unit: "karma",
                GoalProgressDelta: 1.5m,
                RewardTags: ["primary-objective", "session-closeout"]),
            Downtime: new DowntimeAllocationInput(
                Days: 3,
                RecoveryRatePerDay: 2m,
                InitialBurden: 9m,
                ActivityTags: ["healing", "legwork"]),
            Goal: new RunnerGoalUpdateInput(
                GoalId: "goal-7",
                GoalTitle: "Clear the bounty",
                CurrentProgress: 2m,
                TargetProgress: 10m,
                RewardProgressDelta: 1.5m,
                DowntimeProgressDelta: 0.5m,
                GoalTags: ["heat", "survival"]),
            Consequence: new BlackLedgerConsequenceInput(
                ResolutionReportId: "rr-2048",
                RunId: "run-2048",
                FeedId: "shadowfeed-2048",
                FactionId: "redmond-watch",
                Hostility: 24m,
                Exposure: 32m,
                ConsequenceTags: ["intel", "security"],
                SpoilerTags: []));
    }
}
