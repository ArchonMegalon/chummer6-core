using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

// Uses the existing fixture/reader, actual canonical evaluations and archive
// replay; no approved Complete fixture.
public sealed partial class CharacterCreationEffectivePolicyTests
{
    private static CharacterCreationCompletionReviewRequest Assessment308(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationPolicyRequirement requirement)
        => Request(inputs, requirement) with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV1 };

    [TestMethod]
    public void Complete308_Complete_runs_canonical_mechanics_without_downgrade()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var inputs = LoadConsumption(context);
        var request = Assessment308(inputs, CharacterCreationPolicyRequirement.Complete);
        request = request with { Selections = request.Selections with { TotalKarmaInvestment = 2 } };
        var preflight = CharacterCreationCompleteAssessmentService.AssessAuthority(inputs, request);
        Assert.IsNotNull(preflight.Value);
        Assert.AreEqual(CharacterCreationCompletePhase.Authority, preflight.Value!.Phase);
        Assert.IsTrue(preflight.Value.Obligations.Any(row => row.Kind == CharacterCreationCompleteObligationKind.Setting
            && row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent));
        Assert.IsNull(preflight.Value.MechanicalSucceeded);
        Assert.IsNull(preflight.Value.ConsumptionDigest);

        var result = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.IsNotNull(result.Value);
        var review = result.Value!;
        Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, review.Request.PolicyRequirement);
        Assert.IsNotNull(review.Skills);
        Assert.IsNotNull(review.Lifestyles);
        Assert.IsNotNull(review.PolicyConsumption);
        Assert.AreEqual(true, review.CompleteAssessment!.MechanicalSucceeded);
        Assert.AreEqual(CharacterCreationCompletePhase.Coverage, review.CompleteAssessment.Phase);
        Assert.IsTrue(review.PolicyConsumption!.Settings.Any(row => row.Reason == CharacterCreationSettingConsumptionReason.Evaluated));
        Assert.IsTrue(review.CompleteAssessment.Obligations.Any(row => row.Kind == CharacterCreationCompleteObligationKind.Setting
            && row.Coverage == CharacterCreationCompleteCoverage.Evaluated));
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);

        // Control uses the same real canonical arithmetic; production never changes
        // the caller's Complete requirement to obtain these diagnostic results.
        var control = CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.None, CompleteAssessmentSchema = null });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, control.Outcome);
        Assert.AreEqual(JsonSerializer.Serialize(control.Value!.Skills), JsonSerializer.Serialize(review.Skills));
        Assert.AreEqual(JsonSerializer.Serialize(control.Value.PolicyConsumption), JsonSerializer.Serialize(review.PolicyConsumption));
    }

    [TestMethod]
    public void Complete308_Fixed_authority_blocks_prepare_and_retains_eight_graph_obligations()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var inputs = LoadConsumption(context);
        var result = CharacterCreationCompleteAssessmentService.AssessAuthority(inputs,
            Assessment308(inputs, CharacterCreationPolicyRequirement.Complete));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        var assessment = result.Value!;
        Assert.IsFalse(assessment.MayRequestCompleteCandidate);
        Assert.IsFalse(assessment.SatisfiesComplete);
        var graph = assessment.Obligations.Where(row => row.Kind == CharacterCreationCompleteObligationKind.ActiveGraph).ToArray();
        Assert.AreEqual(8, graph.Length);
        CollectionAssert.AreEqual(CharacterCreationPolicyConsumptionProjection.ActiveGraphObligations.ToArray(),
            graph.Select(row => row.Operation!.Value).ToArray());
        Assert.AreEqual(CharacterCreationCompleteEvaluability.AuthorityUnavailable,
            graph.Single(row => row.Operation == CharacterCreationConsumerPolicyOperation.Campaign).Evaluability);
        Assert.IsTrue(graph.All(row => row.Coverage == CharacterCreationCompleteCoverage.Pending));
    }

    [TestMethod]
    public void Complete308_Unfinished_obligations_block_capture_review_and_apply()
    {
        using var context = ReadyContext.Create(true, includeSkillPurchase: true);
        var inputs = LoadConsumption(context);
        var complete = Assessment308(inputs, CharacterCreationPolicyRequirement.Complete);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        string before = JsonSerializer.Serialize(original);
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, complete);
        Assert.IsNull(capture.Value);
        CollectionAssert.Contains(capture.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);

        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var optional = Request(inputs, CharacterCreationPolicyRequirement.None);
        var cash = service.LoadStartingCash(owners.Capture(), context.WorkspaceId, optional);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cash.Outcome, string.Join(",", cash.Blockers));
        var choice = new CharacterCreationStartingCashChoice(cash.Value!.AuthorityDigest, cash.Value.Dice * 3);
        var accepted = service.Review(owners.Capture(), context.WorkspaceId, optional, choice);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, accepted.Outcome, string.Join(",", accepted.Blockers));
        var refused = service.Review(owners.Capture(), context.WorkspaceId, complete, choice);
        Assert.IsNull(refused.Value);
        CollectionAssert.Contains(refused.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);
        // An existing optional acceptance cannot be upgraded to Complete at Apply.
        var applied = service.Apply(owners.Capture(), Command(accepted.Value!) with { Proposal = complete });
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome);
        Assert.IsNull(applied.Value);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Complete308_Independent_replay_rejects_rehashed_omitted_forged_and_inactive_obligations()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var inputs = LoadConsumption(context);
        var request = Assessment308(inputs, CharacterCreationPolicyRequirement.None);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capture.Outcome, string.Join(",", capture.Blockers));
        var witness = capture.Value!;
        var proof = witness.CompleteAssessment!;
        var full = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        Assert.AreEqual(JsonSerializer.Serialize(full.CompleteAssessment), JsonSerializer.Serialize(proof));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, witness, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var cold));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, cold, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(proof), JsonSerializer.Serialize(replay.Value!.CompleteAssessment));
        foreach (var forged in new[]
        {
            proof with { Obligations = [] },
            proof with { Obligations = proof.Obligations.Skip(1).ToArray() },
            proof with { Obligations = proof.Obligations.Append(proof.Obligations[0]).ToArray() },
            proof with { Obligations = proof.Obligations.Select(row => row with
                { Coverage = CharacterCreationCompleteCoverage.Evaluated,
                  EvaluationDigest = CharacterCreationFinalizationDigest.Compute("fabricated") }).ToArray() },
            proof with { Obligations = proof.Obligations.Select(row => row with
                { Coverage = CharacterCreationCompleteCoverage.ProvenInactive }).ToArray() }
        })
        {
            var changed = witness with { CompleteAssessment = forged };
            changed = changed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changed) };
            var rejected = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, changed, request);
            Assert.IsNull(rejected.Value);
            CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationCompleteAssessment.Invalid);
        }
        var omitted = witness with { EffectivePolicy = witness.EffectivePolicy! with
            { SelectedConsumption = witness.EffectivePolicy!.SelectedConsumption! with { RemainingOperations = [] } } };
        omitted = omitted with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(omitted) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, omitted, request).Value);
    }

    [TestMethod]
    public void Complete308_Independent_complete_replay_reaches_final_coverage_failure()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var inputs = LoadConsumption(context);
        var optional = Assessment308(inputs, CharacterCreationPolicyRequirement.None);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, optional);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var complete = optional with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete };
        var full = CharacterCreationCompletionReviewService.Review(inputs, complete);
        Assert.AreEqual(true, full.Value!.CompleteAssessment!.MechanicalSucceeded);
        // Construct an untrusted historical claim using the real blocked full
        // evaluation. No fabricated positive coverage or production capture bypass.
        var witness = captured.Value! with
        {
            PolicyRequirement = CharacterCreationPolicyRequirement.Complete,
            ProposalDigest = CharacterCreationFinalizationDigest.Compute(complete),
            ProvenanceReviewDigest = full.Value.ReviewDigest,
            CompleteAssessment = full.Value.CompleteAssessment
        };
        witness = witness with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(witness) };
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, witness, complete);
        Assert.IsNull(replay.Value);
        // This blocker is emitted after independent arithmetic + exact assessment comparison.
        CollectionAssert.AreEqual(new[] { CharacterCreationEffectivePolicyContext.CoverageUnavailable }, replay.Blockers.ToArray());
    }

    [TestMethod]
    public void Complete308_Source_choice_and_permission_drift_invalidate_assessment()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var inputs = LoadConsumption(context);
        var request = Assessment308(inputs, CharacterCreationPolicyRequirement.None);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        foreach (var changedRequest in new[]
        {
            request with { Selections = request.Selections with { TotalKarmaInvestment = 1 } },
            request with { Selections = request.Selections with { ChangeableAttributeIds = ["BOD"] } }
        })
        {
            var witness = captured.Value! with { ProposalDigest = CharacterCreationFinalizationDigest.Compute(changedRequest) };
            witness = witness with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(witness) };
            Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, witness, changedRequest).Value);
        }
        var observation = inputs.EffectivePolicy!.SettingObservations!;
        var present = observation.Settings.First(row => row.State == CharacterCreationSettingObservationState.Present);
        var changed = WithPolicy(inputs, inputs.EffectivePolicy with { SettingObservations = observation with
        {
            Settings = observation.Settings.Select(row => row == present
                ? row with { SourceValueDigest = CharacterCreationFinalizationDigest.Compute("changed-source") } : row).ToArray()
        } });
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(changed,
            Assessment308(changed, CharacterCreationPolicyRequirement.Complete)).Value);
    }

    [TestMethod]
    public void Complete308_None_skill_limits_and_missing_defaults_retain_historical_behavior()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            amendSettings: settings => settings.Element("allowfreegrids")?.Remove());
        var inputs = LoadConsumption(context);
        foreach (var requirement in new[] { CharacterCreationPolicyRequirement.None, CharacterCreationPolicyRequirement.SkillLimits })
        {
            var request = Request(inputs, requirement);
            string requestBytes = JsonSerializer.Serialize(request);
            var before = CharacterCreationCompletionReviewService.Review(inputs, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, before.Outcome, string.Join(",", before.Blockers));
            string reviewBytes = JsonSerializer.Serialize(before.Value);
            Assert.IsFalse(requestBytes.Contains("CompleteAssessment", StringComparison.Ordinal));
            Assert.IsFalse(reviewBytes.Contains("CompleteAssessment", StringComparison.Ordinal));
            var assessed = CharacterCreationCompletionReviewService.Review(inputs,
                request with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV1 });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, assessed.Outcome, string.Join(",", assessed.Blockers));
            Assert.IsFalse(assessed.Value!.CompleteAssessment!.SatisfiesComplete);
            var absent = assessed.Value.CompleteAssessment.Obligations.Single(row => row.Setting?.Key == CharacterCreationObservedSetting.FreeGrids);
            Assert.AreEqual(CharacterCreationCompleteReason.AbsentSourceDefaultUnproven, absent.Reason);
            Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved, absent.Coverage);
            Assert.AreEqual(reviewBytes, JsonSerializer.Serialize(CharacterCreationCompletionReviewService.Review(inputs, request).Value));
            Assert.AreEqual(requestBytes, JsonSerializer.Serialize(request));
        }
        // Historical Complete requests still fail at their original early check.
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs, Request(inputs, CharacterCreationPolicyRequirement.Complete)).Value);
    }

    [TestMethod]
    public void Complete308_Unknown_unpinned_and_downgraded_assessments_fail_closed()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var inputs = LoadConsumption(context);
        var request = Assessment308(inputs, CharacterCreationPolicyRequirement.None);
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs,
            request with { CompleteAssessmentSchema = "chummer.creation-complete-assessment.v0" }).Value);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        foreach (string? schema in new string?[] { null, "chummer.creation-complete-assessment.v0" })
        {
            var changedRequest = request with { CompleteAssessmentSchema = schema };
            var witness = captured.Value! with { ProposalDigest = CharacterCreationFinalizationDigest.Compute(changedRequest) };
            witness = witness with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(witness) };
            Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, witness, changedRequest).Value);
        }
        var stripped = captured.Value! with { CompleteAssessment = null };
        stripped = stripped with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(stripped) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, stripped, request).Value);
    }
}
