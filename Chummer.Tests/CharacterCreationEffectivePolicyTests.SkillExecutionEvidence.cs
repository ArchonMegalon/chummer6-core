using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    public void Skill_execution_evidence_records_first_and_later_group_terms()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: ConfigureSkillExecution);
        var inputs = LoadConsumption(context);
        var source = SkillExecutionMembers(inputs)[0];
        var policy = inputs.Skills.KarmaPurchasePolicy!;
        Assert.IsTrue(CharacterCreationKarmaSkillsRules.TryPriceActive(source, 0, 1, policy, true, 1, 3, 0, [],
            out var first, captureExecution: true));
        var firstBranch = first!.Execution!.GroupCompensation!;
        Assert.AreEqual(CharacterCreationSkillKarmaOperation.ActiveSkillPurchase, first.Execution.Operation);
        Assert.AreEqual(CharacterCreationLegacySourceProjector.Target(source), first.Execution.Payer);
        Assert.AreEqual(1L, firstBranch.NewGroup!.RatingWeight);
        Assert.AreEqual(policy.KarmaNewSkillGroup, firstBranch.NewGroup.Multiplier);
        Assert.AreEqual(0L, firstBranch.ImproveGroup.RatingWeight);
        Assert.AreEqual(0L, firstBranch.ImproveGroup.Charge);
        Assert.AreEqual(first.KarmaCost, first.Execution.EvaluatedCharge);
        AssertSkillExecutionMatchesCanonicalQuote(first, source, 0, 1, policy, true, 1, 3, 0, []);

        Assert.IsTrue(CharacterCreationKarmaSkillsRules.TryPriceActive(source, 0, 2, policy, true, 2, 3, 0, [],
            out var both, captureExecution: true));
        Assert.AreEqual(policy.KarmaNewSkillGroup, both!.Execution!.GroupCompensation!.NewGroup!.Multiplier);
        Assert.AreEqual(1L, both.Execution.GroupCompensation.ImproveGroup.RatingWeight);
        Assert.AreEqual(policy.KarmaImproveSkillGroup, both.Execution.GroupCompensation.ImproveGroup.Multiplier);
        AssertSkillExecutionMatchesCanonicalQuote(both, source, 0, 2, policy, true, 2, 3, 0, []);

        Assert.IsTrue(CharacterCreationKarmaSkillsRules.TryPriceActive(source, 1, 2, policy, true, 2, 3, 0, [],
            out var later, captureExecution: true));
        Assert.IsNull(later!.Execution!.GroupCompensation!.NewGroup);
        Assert.AreEqual(2L, later.Execution.GroupCompensation.ImproveGroup.RatingWeight);
        AssertSkillExecutionMatchesCanonicalQuote(later, source, 1, 2, policy, true, 2, 3, 0, []);

        var target = CharacterCreationLegacySourceProjector.Target(source);
        CharacterCreationSkillKarmaEffect Effect(string type, int position) => new(type, 50, 0, 0, "create",
            new Guid("1356c4d3-85dc-4c45-9b11-903c6f42cc21"), CharacterCreationSkillsDigest.Compute("execution-quality"),
            "execution-quality", 1, "bonus", position, [target], ["source:execution-quality"]);
        var effects = new[] { Effect("SkillCategoryKarmaCostMultiplier", 0),
            Effect("SkillCategorySpecializationKarmaCostMultiplier", 1) };
        Assert.IsTrue(CharacterCreationKarmaSkillsRules.TryPriceActive(source, 0, 2, policy, true, 2, 3, 1, effects,
            out var modified, captureExecution: true));
        Assert.AreEqual(modified!.KarmaCost, modified.Execution!.EvaluatedCharge);
        Assert.AreEqual(both.Execution.GroupCompensation, modified.Execution.GroupCompensation);
        AssertSkillExecutionMatchesCanonicalQuote(modified, source, 0, 2, policy, true, 2, 3, 1, effects);
    }

    [TestMethod]
    public void Skill_execution_evidence_distinguishes_individual_and_zero_width_intervals()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: ConfigureSkillExecution);
        var inputs = LoadConsumption(context);
        var source = SkillExecutionMembers(inputs)[0];
        var policy = inputs.Skills.KarmaPurchasePolicy!;
        Assert.IsTrue(CharacterCreationKarmaSkillsRules.TryPriceActive(source, 0, 2, policy, false, 2, 3, 0, [],
            out var individual, captureExecution: true));
        AssertSkillExecutionMatchesCanonicalQuote(individual!, source, 0, 2, policy, false, 2, 3, 0, []);
        Assert.IsFalse(individual!.Execution!.CompensationRequested);
        Assert.IsNull(individual.Execution.GroupCompensation);
        Assert.IsTrue(CharacterCreationKarmaSkillsRules.TryPriceActive(source, 2, 2, policy, true, 3, 3, 0, [],
            out var zero, captureExecution: true));
        Assert.AreEqual(0, zero!.KarmaCost);
        Assert.AreEqual(0, zero.RawInterval);
        Assert.IsNull(zero.Execution!.GroupCompensation);
        Assert.IsTrue(CharacterCreationKarmaSkillsRules.IsActiveExecutionValid(zero, source, 2, 2, policy, true, 3, 3, 0, []));
    }

    [TestMethod]
    public void Skill_execution_evidence_is_opt_in_and_preserves_historical_price_bytes()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: ConfigureSkillExecution);
        var inputs = LoadConsumption(context);
        var source = SkillExecutionMembers(inputs)[0];
        var policy = inputs.Skills.KarmaPurchasePolicy!;
        Assert.IsTrue(CharacterCreationKarmaSkillsRules.TryPriceActive(source, 0, 2, policy, false, 2, 3, 0, [], out var historical));
        Assert.IsNull(historical!.Execution);
        var originalShape = new
        {
            historical.Target, historical.Lower, historical.Upper, historical.RawInterval, historical.CompensationFloor,
            historical.IntervalMultiplier, historical.IntervalAddition, historical.SpecializationMultiplier,
            historical.SpecializationAddition, historical.IntervalCost, historical.SpecializationCost,
            historical.KarmaCost, historical.Effects
        };
        Assert.AreEqual(JsonSerializer.Serialize(originalShape), JsonSerializer.Serialize(historical));
        Assert.AreEqual(CharacterCreationSkillsDigest.Compute(originalShape), CharacterCreationSkillsDigest.Compute(historical));
        Assert.IsTrue(CharacterCreationKarmaSkillsRules.TryPriceActive(source, 0, 2, policy, false, 2, 3, 0, [],
            out var captured, captureExecution: true));
        Assert.AreEqual(JsonSerializer.Serialize(historical), JsonSerializer.Serialize(captured! with { Execution = null }));
        Assert.IsFalse(CharacterCreationKarmaSkillsRules.IsActiveExecutionValid(historical, source, 0, 2, policy, false, 2, 3, 0, []));
    }

    [TestMethod]
    public void Skill_execution_evidence_full_and_independent_selected_replay_agree()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: ConfigureSkillExecution);
        var inputs = LoadConsumption(context);
        Assert.AreEqual(CharacterCreationSettingsObservation.SchemaV2, inputs.EffectivePolicy!.SettingObservations!.Schema);
        var request = SkillExecutionRequest(inputs);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        var members = SkillExecutionMembers(inputs);
        string payer = inputs.Skills.KarmaCompensationSourceOrder!.First(id => members.Any(row => row.SourceSkillId == id));
        foreach (var source in members)
        {
            var row = full.Value!.Skills!.Skills.Single(item => item.SourceSkillId == source.SourceSkillId);
            Assert.IsNotNull(row.SkillKarmaPrice);
            Assert.IsNotNull(row.SkillKarmaPrice!.Execution);
            Assert.AreEqual(row.KarmaCost, row.SkillKarmaPrice.Execution!.EvaluatedCharge);
            Assert.AreEqual(source.SourceSkillId == payer, row.SkillKarmaPrice.Execution.GroupCompensation is not null);
        }
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capture.Outcome, string.Join(",", capture.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, capture.Value!, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var coldWitness));
        var selected = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, coldWitness, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, selected.Outcome, string.Join(",", selected.Blockers));
        Assert.IsTrue(CharacterCreationSelectedCompletionEvaluator.MatchesFullReview(selected.Value!, full.Value!));
        Assert.AreEqual(JsonSerializer.Serialize(full.Value!.Skills), JsonSerializer.Serialize(selected.Value!.Skills));
        Assert.AreEqual(JsonSerializer.Serialize(full.Value.PolicyConsumption),
            JsonSerializer.Serialize(coldWitness.EffectivePolicy!.SelectedConsumption));
        AssertSkillGroupConsumption(full.Value.PolicyConsumption!, newGroup: true, improveGroup: true);
        var closed = CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, closed.Outcome);
        CollectionAssert.Contains(closed.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);
    }

    [TestMethod]
    public void Skill_execution_evidence_unfunded_quote_does_not_claim_admission()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: settings =>
        {
            ConfigureSkillExecution(settings);
            settings.Element("karmacost")!.SetElementValue("karmanewskillgroup", 1000);
            settings.Element("karmacost")!.SetElementValue("karmaimproveskillgroup", 1000);
        });
        var inputs = LoadConsumption(context);
        var request = SkillExecutionRequest(inputs);
        var before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, full.Outcome);
        CollectionAssert.Contains(full.Blockers.ToArray(), CharacterCreationFinalizationBlockers.GlobalKarmaExceeded);
        var price = full.Value!.Skills!.Skills.Single(row => row.SkillKarmaPrice?.Execution?.GroupCompensation is not null).SkillKarmaPrice!;
        Assert.AreEqual(price.KarmaCost, price.Execution!.EvaluatedCharge);
        Assert.IsTrue(full.Value.Balances.RemainingCreationKarma < 0);
        foreach (var row in full.Value.PolicyConsumption!.Settings.Where(row => row.Key is
            CharacterCreationObservedSetting.NewSkillGroup or CharacterCreationObservedSetting.ImproveSkillGroup))
        {
            Assert.AreEqual(CharacterCreationSettingConsumptionReason.EvaluationUnavailable, row.Reason);
            Assert.IsNull(row.ConsumerInputsDigest);
            Assert.IsNull(row.EvaluationDigest);
        }
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request).Value);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Skill_execution_evidence_v1_observation_replays_original_result_and_witness_bytes()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: ConfigureSkillExecution);
        var current = LoadConsumption(context);
        Assert.IsTrue(CharacterCreationSettingsObservation.TryCapture(current.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml,
            current.EffectivePolicy!.ProfileInputsDigest, CharacterCreationSettingsObservation.SchemaV1, out var observation));
        var inputs = WithPolicy(current, current.EffectivePolicy with { SettingObservations = observation });
        var request = SkillExecutionRequest(inputs);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.IsTrue(full.Value!.Skills!.Skills.All(row => row.SkillKarmaPrice is null));
        AssertSkillGroupConsumption(full.Value.PolicyConsumption!, false, false);
        var choices = request.Selections;
        var legacy = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills, choices.AttributeAllocations,
            choices.SkillAllocations, choices.GroupAllocations, choices.SkillKarmaPurchases);
        Assert.AreEqual(JsonSerializer.Serialize(legacy.Value), JsonSerializer.Serialize(full.Value.Skills));
        Assert.AreEqual(legacy.Value!.ProposalDigest, full.Value.Skills.ProposalDigest);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capture.Outcome, string.Join(",", capture.Blockers));
        string witnessBytes = JsonSerializer.Serialize(capture.Value);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, capture.Value!, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var coldWitness));
        Assert.AreEqual(witnessBytes, JsonSerializer.Serialize(coldWitness));
        Assert.AreEqual(capture.Value!.WitnessDigest, coldWitness.WitnessDigest);
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, coldWitness, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.IsTrue(CharacterCreationSelectedCompletionEvaluator.MatchesFullReview(replay.Value!, full.Value));
        Assert.IsTrue(replay.Value!.Skills.Skills.All(row => row.SkillKarmaPrice is null));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(coldBootstrap, coldWitness, out var repeated));
        Assert.AreEqual(JsonSerializer.Serialize(archive), JsonSerializer.Serialize(repeated));
    }

    [TestMethod]
    public void Skill_execution_evidence_rejects_missing_inconsistent_and_resealed_facts()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: ConfigureSkillExecution);
        var inputs = LoadConsumption(context);
        var request = SkillExecutionRequest(inputs);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        var row = full.Value!.Skills!.Skills.Single(item => item.SkillKarmaPrice?.Execution?.GroupCompensation is not null);
        var price = row.SkillKarmaPrice!;
        var execution = price.Execution!;
        var branch = execution.GroupCompensation!;
        var source = inputs.Skills.Authority.ActiveSkills.Single(item => item.SourceSkillId == row.SourceSkillId);
        var policy = inputs.Skills.KarmaPurchasePolicy!;
        Assert.IsTrue(CharacterCreationKarmaSkillsRules.IsActiveExecutionValid(price, source, 0, 2, policy, true, 2, 3, 0, []));
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capture.Outcome, string.Join(",", capture.Blockers));
        var selected = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, capture.Value!, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, selected.Outcome, string.Join(",", selected.Blockers));
        Assert.IsTrue(CharacterCreationKarmaSkillsRules.TryPriceActive(source, 0, 2, policy, false, 2, 3, 0, [],
            out var otherDecision, captureExecution: true));
        foreach (var forged in new[]
        {
            price with { Execution = null },
            price with { Execution = execution with { GroupCompensation = null } },
            price with { Execution = execution with { GroupCompensation = branch with { NewGroup = null } } },
            price with { Execution = execution with { GroupCompensation = branch with { ImproveGroup = null! } } },
            price with { Execution = execution with { GroupCompensation = branch with
                { NewGroup = branch.NewGroup! with { Multiplier = 99, Charge = 99 } } } },
            price with { Execution = execution with { GroupCompensation = branch with
                { ImproveGroup = branch.ImproveGroup with { RatingWeight = 0, Charge = 0 } } } },
            price with { Execution = execution with { Payer = execution.Payer with { SourceId = "another-payer" } } },
            price with { Execution = execution with { CanonicalInputDigest = CharacterCreationSkillsDigest.Compute("forged-input") } },
            price with { Execution = execution with { EvaluatedCharge = execution.EvaluatedCharge + 1 }, KarmaCost = price.KarmaCost + 1 },
            otherDecision! // Fully canonical evidence for the wrong payer decision is still a forgery here.
        })
        {
            Assert.IsFalse(CharacterCreationKarmaSkillsRules.IsActiveExecutionValid(forged, source, 0, 2, policy, true, 2, 3, 0, []));
            var fakeSkills = full.Value.Skills with { Skills = full.Value.Skills.Skills.Select(item =>
                item.SourceSkillId == row.SourceSkillId ? item with { SkillKarmaPrice = forged, KarmaCost = forged.KarmaCost } : item).ToArray() };
            fakeSkills = fakeSkills with { ProposalDigest = CharacterCreationSkillsDigest.Compute(fakeSkills with { ProposalDigest = string.Empty }) };
            var fakeFull = full.Value with { Skills = fakeSkills };
            fakeFull = fakeFull with { ReviewDigest = CharacterCreationCompletionReviewRules.ComputeReviewDigest(fakeFull) };
            Assert.IsFalse(CharacterCreationSelectedCompletionEvaluator.MatchesFullReview(selected.Value!, fakeFull));
            var fakeSelected = selected.Value! with { Skills = fakeSkills };
            fakeSelected = fakeSelected with { ReplayDigest = CharacterCreationSelectedCompletionEvaluator.ComputeReplayDigest(fakeSelected) };
            Assert.IsFalse(CharacterCreationSelectedCompletionEvaluator.MatchesFullReview(fakeSelected, full.Value));
            var rejectedProof = CharacterCreationPolicyConsumptionProjection.FromFull(inputs, fakeFull)!;
            AssertSkillGroupConsumption(rejectedProof, false, false);
            Assert.IsNull(CharacterCreationSelectedPolicyProjection.SkillGroupExecutions(capture.Value!.EffectivePolicy!,
                capture.Value.Skills, request.Selections, fakeSkills, selected.Value.Qualities,
                (capture.Value.Magic.Talent.GrantedQualitySources ?? []).Select(item => item.CanonicalSourceXml)));
            var fakePolicy = capture.Value.EffectivePolicy! with { SelectedConsumption = rejectedProof };
            fakePolicy = fakePolicy with { PolicyDigest = CharacterCreationEffectivePolicyContext.ComputeDigest(fakePolicy) };
            var fakeWitness = capture.Value with { EffectivePolicy = fakePolicy };
            fakeWitness = fakeWitness with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(fakeWitness) };
            Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, fakeWitness, out var forgedArchive));
            Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(forgedArchive, out var forgedBootstrap, out var forgedWitness));
            Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, forgedBootstrap, forgedWitness, request).Value);
        }
        // Even internally consistent facts cannot be moved to a different canonical interval or payer decision.
        Assert.IsFalse(CharacterCreationKarmaSkillsRules.IsActiveExecutionValid(price, source, 1, 2, policy, true, 2, 3, 0, []));
        Assert.IsFalse(CharacterCreationKarmaSkillsRules.IsActiveExecutionValid(price, source, 0, 2, policy, false, 2, 3, 0, []));
    }

    [TestMethod]
    public void Skill_execution_evidence_group_consumption_tracks_first_later_and_zero_weight_terms()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: ConfigureSkillExecution);
        var inputs = LoadConsumption(context);
        foreach (var interval in new[] { (Lower: 0, Upper: 1), (Lower: 0, Upper: 2), (Lower: 1, Upper: 2) })
        {
            var request = SkillExecutionRequest(inputs, interval.Lower, interval.Upper);
            var full = AssertSkillGroupConsumptionReplay(context, inputs, request,
                newGroup: interval.Lower == 0, improveGroup: interval.Upper > 1);
            var payer = full.Skills!.Skills.Single(row => row.SkillKarmaPrice?.Execution?.GroupCompensation is not null);
            Assert.AreEqual(interval.Lower, payer.SkillKarmaPrice!.Execution!.Lower);
            Assert.AreEqual(interval.Upper, payer.SkillKarmaPrice.Execution.Upper);
            Assert.AreEqual(interval.Upper - interval.Lower, payer.KarmaLevels);
        }
    }

    [TestMethod]
    public void Skill_execution_evidence_group_consumption_preserves_zero_multiplier_execution()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: settings =>
        {
            ConfigureSkillExecution(settings);
            settings.Element("karmacost")!.SetElementValue("karmanewskillgroup", 0);
            settings.Element("karmacost")!.SetElementValue("karmaimproveskillgroup", 0);
        });
        var inputs = LoadConsumption(context);
        var full = AssertSkillGroupConsumptionReplay(context, inputs, SkillExecutionRequest(inputs), true, true);
        var price = full.Skills!.Skills.Single(row => row.SkillKarmaPrice?.Execution?.GroupCompensation is not null).SkillKarmaPrice!;
        var branch = price.Execution!.GroupCompensation!;
        Assert.IsTrue(branch.NewGroup!.RatingWeight > 0);
        Assert.IsTrue(branch.ImproveGroup.RatingWeight > 0);
        Assert.AreEqual(0, branch.NewGroup.Multiplier);
        Assert.AreEqual(0, branch.ImproveGroup.Multiplier);
        Assert.AreEqual(0L, branch.NewGroup.Charge);
        Assert.AreEqual(0L, branch.ImproveGroup.Charge);
        Assert.AreEqual(0, price.KarmaCost);
    }

    [TestMethod]
    public void Skill_execution_evidence_group_consumption_requires_an_executed_increase()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: ConfigureSkillExecution);
        var inputs = LoadConsumption(context);
        var all = SkillExecutionRequest(inputs);
        var individual = all with { Selections = all.Selections with
            { SkillKarmaPurchases = all.Selections.SkillKarmaPurchases!.Take(1).ToArray() } };
        var full = AssertSkillGroupConsumptionReplay(context, inputs, individual, false, false);
        Assert.IsTrue(full.Skills!.Skills.Where(row => row.SkillKarmaPrice?.Execution is not null)
            .All(row => row.SkillKarmaPrice!.Execution!.GroupCompensation is null));
        var zeroWidth = SkillExecutionRequest(inputs, lower: 2, upper: 2);
        // A no-op purchase without a specialization is rejected by the canonical
        // purchase validator; the direct zero-width quote is covered separately.
        var unchanged = CharacterCreationCompletionReviewService.Review(inputs, zeroWidth);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, unchanged.Outcome);
        CollectionAssert.Contains(unchanged.Blockers.ToArray(), CharacterCreationSkillKarmaPurchaseBlockers.PurchaseInvalid);
        Assert.IsNull(unchanged.Value!.Skills);
        foreach (var row in unchanged.Value.PolicyConsumption!.Settings.Where(row => row.Key is
            CharacterCreationObservedSetting.NewSkillGroup or CharacterCreationObservedSetting.ImproveSkillGroup))
        {
            Assert.AreEqual(CharacterCreationSettingConsumptionReason.EvaluationUnavailable, row.Reason);
            Assert.IsNull(row.ConsumerInputsDigest);
            Assert.IsNull(row.EvaluationDigest);
        }
    }

    private static void AssertSkillExecutionMatchesCanonicalQuote(CharacterCreationSkillKarmaPrice price,
        CharacterCreationSkillCatalogEntry source, int lower, int upper, CharacterCreationKarmaSkillsPolicy policy,
        bool compensate, int? otherMinimum, int memberCount, int specializationCount,
        IReadOnlyList<CharacterCreationSkillKarmaEffect> effects)
    {
        Assert.IsTrue(CharacterCreationKarmaSkillsRules.TryPriceActive(source, lower, upper, policy, compensate,
            otherMinimum, memberCount, specializationCount, effects, out var historical));
        Assert.AreEqual(JsonSerializer.Serialize(historical), JsonSerializer.Serialize(price with { Execution = null }));
        Assert.IsTrue(CharacterCreationKarmaSkillsRules.IsActiveExecutionValid(price, source, lower, upper, policy,
            compensate, otherMinimum, memberCount, specializationCount, effects));
    }

    private static void AssertSkillGroupConsumption(CharacterCreationPolicyConsumption consumption,
        bool newGroup, bool improveGroup)
    {
        foreach (var expected in new[] { (Key: CharacterCreationObservedSetting.NewSkillGroup, Used: newGroup),
            (Key: CharacterCreationObservedSetting.ImproveSkillGroup, Used: improveGroup) })
        {
            var row = consumption.Settings.Single(row => row.Key == expected.Key);
            Assert.AreEqual(CharacterCreationSettingConsumer.Skills, row.Consumer);
            Assert.AreEqual(expected.Used ? CharacterCreationSettingConsumptionReason.Evaluated
                : CharacterCreationSettingConsumptionReason.EvaluatedUseUnavailable, row.Reason, expected.Key.ToString());
            Assert.AreEqual(expected.Used, row.ConsumerInputsDigest is not null);
            Assert.AreEqual(expected.Used, row.EvaluationDigest is not null);
        }
    }

    private static CharacterCreationCompletionReview AssertSkillGroupConsumptionReplay(ReadyContext context,
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        bool newGroup, bool improveGroup)
    {
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        AssertSkillGroupConsumption(full.Value!.PolicyConsumption!, newGroup, improveGroup);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, captured.Value!, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var coldWitness));
        var selected = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, coldWitness, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, selected.Outcome, string.Join(",", selected.Blockers));
        Assert.IsTrue(CharacterCreationSelectedCompletionEvaluator.MatchesFullReview(selected.Value!, full.Value));
        Assert.AreEqual(JsonSerializer.Serialize(full.Value.PolicyConsumption),
            JsonSerializer.Serialize(coldWitness.EffectivePolicy!.SelectedConsumption));
        return full.Value;
    }

    private static void ConfigureSkillExecution(XElement settings)
    {
        settings.SetElementValue("compensateskillgroupkarmadifference", "True");
        var costs = settings.Element("karmacost")!;
        costs.SetElementValue("karmanewactiveskill", 2);
        costs.SetElementValue("karmaimproveactiveskill", 3);
        costs.SetElementValue("karmanewskillgroup", 4);
        costs.SetElementValue("karmaimproveskillgroup", 4);
        costs.SetElementValue("karmaspecialization", 9);
    }

    private static CharacterCreationSkillCatalogEntry[] SkillExecutionMembers(CharacterCreationCompletionReviewInputs inputs)
    {
        var group = inputs.Skills.Authority.SkillGroups.Single(row => row.Name == "Influence");
        var members = CharacterCreationCompletionPlannerTests.EnabledGroupMembers(inputs, group);
        Assert.AreEqual(3, members.Length);
        return members;
    }

    private static CharacterCreationCompletionReviewRequest SkillExecutionRequest(CharacterCreationCompletionReviewInputs inputs,
        int lower = 0, int upper = 2)
    {
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        var members = SkillExecutionMembers(inputs);
        return request with { Selections = request.Selections with
        {
            SkillAllocations = lower == 0 ? request.Selections.SkillAllocations : request.Selections.SkillAllocations
                .Where(row => !members.Any(member => member.SourceSkillId == row.SourceSkillId))
                .Concat(members.Select(row => new CharacterCreationSkillAllocation(row.SourceSkillId, row.Kind, lower, null, false))).ToArray(),
            SkillKarmaPurchases = members.Select(row => new CharacterCreationSkillKarmaPurchase(
                row.SourceSkillId, row.Kind, row.SourceNodeDigest, upper)).ToArray()
        } };
    }
}
