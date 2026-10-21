using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using K = Chummer.Contracts.Characters.CharacterCreationCompleteObligationKind;
using C = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;
using O = Chummer.Contracts.Characters.CharacterCreationConsumerPolicyOperation;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Consumers520_Mystic_exchange_uses_actual_magic_execution_and_cold_replay(bool exchange)
    {
        using var context = MagicContext384("Mystic Adept", exchange: exchange);
        var inputs = LoadConsumption(context);
        var historical = GrantRequest368(inputs);
        string oldBytes = JsonSerializer.Serialize(historical);
        Assert.IsFalse(oldBytes.Contains("AdditionalConsumerExecutionSchema", StringComparison.Ordinal));
        var old = CharacterCreationCompletionReviewService.Review(inputs, historical);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, old.Outcome, string.Join(",", old.Blockers));
        var request = historical with
        {
            AdditionalConsumerExecutionSchema = CharacterCreationCompletionReviewSchemas.AdditionalConsumerExecutionV1
        };
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.AreEqual(C.Unresolved, Operation332(old.Value!.CompleteAssessment!, O.MysticAdeptExchange).Coverage);
        Assert.AreEqual(C.Evaluated, Operation332(full.Value!.CompleteAssessment!, O.MysticAdeptExchange).Coverage);
        Assert.IsNotNull(full.Value.UpstreamBudget!.Magic.MysticAdeptPowerPoints);
        if (exchange) Assert.IsTrue(full.Value.UpstreamBudget.Magic.MysticAdeptPowerPoints!.ExchangedSpellSlots > 0);
        Assert.AreEqual(JsonSerializer.Serialize(old.Value.UpstreamBudget), JsonSerializer.Serialize(full.Value.UpstreamBudget));
        Assert.AreEqual(JsonSerializer.Serialize(full.Value.CompleteAssessment),
            JsonSerializer.Serialize(Cold342(context, inputs, request).CompleteAssessment));
        Assert.AreEqual(oldBytes, JsonSerializer.Serialize(request with { AdditionalConsumerExecutionSchema = null }));
        Assert.IsFalse(full.Value.CompleteAssessment!.SatisfiesComplete);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Consumers520_Loadout_and_lifestyles_require_real_cost_execution(bool retainLoadout)
    {
        using var context = ReadyContext.Create(true, includeNonEmptyPurchases: true);
        if (retainLoadout) RetainLoadout387(context);
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var inputs = LoadEconomic387(context);
        var request = EconomicRequest387(inputs) with
        {
            AdditionalConsumerExecutionSchema = CharacterCreationCompletionReviewSchemas.AdditionalConsumerExecutionV1
        };
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.IsTrue(full.Value!.Loadout!.PurchaseCost > 0m);
        Assert.IsTrue(full.Value.Lifestyles!.Budget.Used > 0m);
        foreach (var operation in new[] { O.Loadout, O.LifestylesAndFreeGrid, O.LifestyleMetatypeCost })
            Assert.AreEqual(C.Evaluated, Operation332(full.Value.CompleteAssessment!, operation).Coverage, operation.ToString());
        Assert.AreEqual(JsonSerializer.Serialize(full.Value.CompleteAssessment),
            JsonSerializer.Serialize(Cold342(context, inputs, request).CompleteAssessment));
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        Assert.IsFalse(full.Value.CompleteAssessment!.SatisfiesComplete);
    }

    [TestMethod]
    [DataRow(false, 24)]
    [DataRow(true, 17)]
    public void Consumers520_Purchase_execution_accounts_for_category_and_group_rules(bool compensate, int groupCost)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: settings =>
        {
            ConfigureSkillExecution(settings);
            settings.SetElementValue("compensateskillgroupkarmadifference", compensate.ToString());
        });
        var inputs = LoadConsumption(context);
        var historical = GrantRequest368(inputs) with
        {
            Selections = SkillExecutionRequest(inputs).Selections
        };
        var knowledge = inputs.Skills.Authority.KnowledgeSkills.First(row => !row.CanBeNativeLanguage);
        historical = historical with { Selections = historical.Selections with
        {
            SkillKarmaPurchases = [.. historical.Selections.SkillKarmaPurchases!,
                new(knowledge.SourceSkillId, knowledge.Kind, knowledge.SourceNodeDigest, 1)]
        } };
        var old = CharacterCreationCompletionReviewService.Review(inputs, historical);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, old.Outcome, string.Join(",", old.Blockers));
        var request = historical with
        {
            AdditionalConsumerExecutionSchema = CharacterCreationCompletionReviewSchemas.AdditionalConsumerExecutionV1
        };
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.AreEqual(groupCost + inputs.Skills.KarmaPurchasePolicy!.KarmaNewKnowledgeSkill,
            full.Value!.Balances.SkillsKarmaCost);
        Assert.AreEqual(JsonSerializer.Serialize(old.Value!.Skills), JsonSerializer.Serialize(full.Value.Skills),
            "Coverage must not alter the actual canonical prices.");
        foreach (var operation in new[] { O.SkillGroupKarmaCompensation, O.KnowledgeCategoryPricing })
        {
            Assert.AreEqual(C.Unresolved, Operation332(old.Value.CompleteAssessment!, operation).Coverage);
            Assert.AreEqual(C.Evaluated, Operation332(full.Value.CompleteAssessment!, operation).Coverage, operation.ToString());
            var unexecuted = CharacterCreationCompleteAssessmentService.AfterEvaluation(
                CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request), full.Value.PolicyConsumption, true);
            Assert.AreEqual(C.Unresolved, Operation332(unexecuted, operation).Coverage,
                "The new request pin and valid inputs alone are not execution.");
        }
        Assert.AreEqual(JsonSerializer.Serialize(full.Value.CompleteAssessment),
            JsonSerializer.Serialize(Cold342(context, inputs, request).CompleteAssessment));
        var noPurchases = CharacterCreationCompletionReviewService.Review(inputs, request with
        {
            Selections = request.Selections with { SkillKarmaPurchases = null }
        });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, noPurchases.Outcome);
        Assert.IsNull(noPurchases.Value, "V5 must reject an absent purchase scope, not manufacture execution.");
        CollectionAssert.Contains(noPurchases.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.RequestInvalid);
    }

    [TestMethod]
    public void Consumers520_Request_pin_and_copied_results_cannot_supply_execution()
    {
        using var context = MagicContext384("Mystic Adept", exchange: true);
        var inputs = LoadConsumption(context);
        var request = GrantRequest368(inputs) with
        {
            AdditionalConsumerExecutionSchema = CharacterCreationCompletionReviewSchemas.AdditionalConsumerExecutionV1
        };
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        Assert.IsNotNull(review);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var noExecution = CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, review.PolicyConsumption, true);
        Assert.AreEqual(C.Unresolved, Operation332(noExecution, O.MysticAdeptExchange).Coverage);
        var forged = review with { UpstreamBudget = review.UpstreamBudget! with
        {
            Magic = review.UpstreamBudget.Magic with
            {
                EffectiveAttributes = review.UpstreamBudget.Magic.EffectiveAttributes with { Magic = int.MaxValue }
            }
        } };
        var badExecution = CharacterCreationCompleteConsumerExecution.FromFull(inputs, forged, authority);
        Assert.AreEqual(C.Unresolved, Operation332(CharacterCreationCompleteAssessmentService.AfterEvaluation(
            authority, review.PolicyConsumption, true, consumers: badExecution), O.MysticAdeptExchange).Coverage);
        foreach (var bad in new[]
        {
            request with { AdditionalConsumerExecutionSchema = "unknown" },
            request with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV3 },
            request with { PolicyConsumptionSchema = null }
        })
        {
            Assert.IsFalse(CharacterCreationCompletionReviewRules.HasValidAdditionalConsumerExecution(bad));
            Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs, bad).Value);
        }
        var execution = CharacterCreationCompleteConsumerExecution.FromFull(inputs, review, authority)!;
        Assert.IsNotNull(execution);
        Assert.AreEqual(0, execution.EvaluationsFor(authority with
            { RequestDigest = CharacterCreationFinalizationDigest.Compute(request with { AdditionalConsumerExecutionSchema = null }) }).Count);
    }

    [TestMethod]
    [DataRow(false, false, 18, CharacterCreationBuildMethods.Priority)]
    [DataRow(true, false, 9, CharacterCreationBuildMethods.Priority)]
    [DataRow(false, true, 15, CharacterCreationBuildMethods.Priority)]
    [DataRow(true, true, 6, CharacterCreationBuildMethods.Priority)]
    [DataRow(true, true, 6, CharacterCreationBuildMethods.SumToTen)]
    public void Complete382_Attribute_and_purchase_consumers_use_actual_house_rules_and_cold_replay(
        bool alternate, bool reverse, int attributeCost, string method)
    {
        int skillCost = alternate ? 5 : 4;
        using var context = ReadyContext.Create(false, metatypeName: "Ork", buildMethod: method,
            includeSkillPurchase: true, playerChosenAttributeIds: [], amendSettings: settings =>
            {
                settings.SetElementValue("alternatemetatypeattributekarma", alternate);
                settings.SetElementValue("reverseattributepriorityorder", reverse);
                settings.Element("karmacost")!.SetElementValue("karmaattribute", 3);
                settings.Element("karmacost")!.SetElementValue("karmanewactiveskill", skillCost);
            });
        string original = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!);
        var inputs = LoadConsumption(context);
        string inputBytes = JsonSerializer.Serialize(inputs);
        var firstAid = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "First Aid");
        var baseline = Request(inputs, CharacterCreationPolicyRequirement.None);
        var request = baseline with
        {
            CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4,
            Selections = baseline.Selections with
            {
                AttributeAllocations = [new("BOD", 1, 1)], ChangeableAttributeIds = ["BOD"],
                SkillKarmaPurchases = [new(firstAid.SourceSkillId, firstAid.Kind, firstAid.SourceNodeDigest, 1)]
            }
        };
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.AreEqual(attributeCost, full.Value!.Skills!.Attributes.Attributes.Single(row => row.AttributeId == "BOD").KarmaCost);
        Assert.AreEqual((int?)skillCost, full.Value.Balances.SkillsKarmaCost);
        foreach (var operation in new[] { O.AttributeSpending, O.SkillPurchases })
        {
            var row = Operation332(full.Value.CompleteAssessment!, operation);
            Assert.AreEqual(C.Evaluated, row.Coverage, operation.ToString());
            Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(row.EvaluationDigest));
        }
        Assert.AreEqual(alternate || reverse ? C.Evaluated : C.Unresolved,
            Operation332(full.Value.CompleteAssessment!, O.AttributeHouseRules).Coverage);
        Assert.IsFalse(full.Value.CompleteAssessment!.SatisfiesComplete);
        var cold = Cold342(context, inputs, request);
        Assert.AreEqual(JsonSerializer.Serialize(full.Value.CompleteAssessment), JsonSerializer.Serialize(cold.CompleteAssessment));
        Assert.AreEqual(attributeCost, cold.Skills.Attributes.Attributes.Single(row => row.AttributeId == "BOD").KarmaCost);
        Assert.AreEqual(inputBytes, JsonSerializer.Serialize(inputs));
        Assert.AreEqual(original, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void Complete382_Empty_purchase_scope_requires_actual_purchase_execution_not_point_success()
    {
        using var context = ReadyContext.Create(false);
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None) with
            { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4 };
        Assert.IsNotNull(request.Selections.SkillKarmaPurchases);
        Assert.AreEqual(0, request.Selections.SkillKarmaPurchases!.Count);
        var trace = new CharacterCreationSkillsService.CompleteSkillTrace();
        var points = CharacterCreationSkillsService.PreviewCompletionPurchasesCore(inputs.Skills,
            request.Selections.AttributeAllocations, request.Selections.SkillAllocations,
            request.Selections.GroupAllocations, null, null, default, completeTrace: trace);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, points.Outcome, string.Join(",", points.Blockers));
        Assert.IsTrue(trace.Completed);
        Assert.IsNull(trace.PurchasesDigest);
        var purchases = CharacterCreationSkillsService.PreviewCompletionPurchasesCore(inputs.Skills,
            request.Selections.AttributeAllocations, request.Selections.SkillAllocations,
            request.Selections.GroupAllocations, [], null, default, completeTrace: trace);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, purchases.Outcome, string.Join(",", purchases.Blockers));
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(trace.PurchasesDigest));
        // Reusing the same in-process observer for a point-only pass must retire
        // the prior purchase execution, even if every numeric outcome is equal.
        points = CharacterCreationSkillsService.PreviewCompletionPurchasesCore(inputs.Skills,
            request.Selections.AttributeAllocations, request.Selections.SkillAllocations,
            request.Selections.GroupAllocations, null, null, default, completeTrace: trace);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, points.Outcome);
        Assert.IsNull(trace.PurchasesDigest);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.AreEqual(C.Evaluated, Operation332(full.Value!.CompleteAssessment!, O.SkillPurchases).Coverage);
        Assert.AreEqual(JsonSerializer.Serialize(full.Value.CompleteAssessment),
            JsonSerializer.Serialize(Cold342(context, inputs, request).CompleteAssessment));
    }

    [TestMethod]
    public void Complete382_Attribute_and_purchase_results_cannot_recreate_or_borrow_execution()
    {
        using var context = ReadyContext.Create(false,
            amendSettings: settings => settings.SetElementValue("reverseattributepriorityorder", true));
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None) with
            { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4 };
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        Assert.IsNotNull(review);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var executed = CharacterCreationCompleteConsumerExecution.FromFull(inputs, review, authority);
        Assert.IsNotNull(executed);
        var changedSkills = review.Skills! with
        {
            Attributes = review.Skills!.Attributes with
            {
                CreationKarmaBudget = review.Skills.Attributes.CreationKarmaBudget with
                    { Used = review.Skills.Attributes.CreationKarmaBudget.Used + 1m }
            }
        };
        var changed = CharacterCreationCompleteConsumerExecution.FromFull(inputs, review with { Skills = changedSkills }, authority);
        foreach (var proof in new[]
        {
            CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, review.PolicyConsumption, true),
            CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, review.PolicyConsumption, false, consumers: executed),
            CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, review.PolicyConsumption, true, consumers: changed)
        })
            foreach (var operation in new[] { O.AttributeSpending, O.AttributeHouseRules, O.SkillPurchases })
                Assert.AreEqual(C.Unresolved, Operation332(proof, operation).Coverage, operation.ToString());
        foreach (var borrowed in new[]
        {
            authority with { PolicyDigest = CharacterCreationFinalizationDigest.Compute("other policy") },
            authority with { SelectionsDigest = CharacterCreationFinalizationDigest.Compute("other choices") },
            authority with { RawCharacterXmlDigest = CharacterCreationFinalizationDigest.Compute("other original") }
        }) Assert.AreEqual(0, executed!.EvaluationsFor(borrowed).Count);
        foreach (var allocations in new CharacterCreationAttributeAllocation[][]
        {
            [new("BOD", 99, 0)], [new("BOD", 0, 99)], [new("MAG", 1, 0)]
        })
        {
            var invalid = request with { Selections = request.Selections with
                { AttributeAllocations = allocations, ChangeableAttributeIds = allocations.Select(row => row.AttributeId).ToArray() } };
            var refused = CharacterCreationCompletionReviewService.Review(inputs, invalid);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, refused.Outcome);
            foreach (var operation in new[] { O.AttributeSpending, O.AttributeHouseRules, O.SkillPurchases })
                Assert.AreEqual(C.Unresolved, Operation332(refused.Value!.CompleteAssessment!, operation).Coverage);
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            CharacterCreationCompleteConsumerExecution.FromFull(inputs, review, authority, cancellation.Token));
    }

    private static ReadyContext Context332() => ReadyContext.Create(false, includeSkillPurchase: true,
        amendSettings: settings =>
        {
            settings.SetElementValue("maxskillratingcreate", 6);
            settings.SetElementValue("maxknowledgeskillratingcreate", 6);
            settings.Element("allowfreegrids")?.Remove();
            settings.Add(new XElement("unclassified-core332", "opaque"));
        });

    private static CharacterCreationCompletionReviewRequest Request332(CharacterCreationCompletionReviewInputs inputs)
    {
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        var knowledge = inputs.Skills.Authority.KnowledgeSkills.First(row => !row.CanBeNativeLanguage);
        var pistols = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var quality = inputs.Lifestyles.Qualities.Authority.Options.Single(row => row.Name == "Ambidextrous" && row.IsSelectable);
        return request with
        {
            CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV2,
            Selections = request.Selections with
            {
                SkillAllocations = [.. request.Selections.SkillAllocations, new(knowledge.SourceSkillId,
                    CharacterCreationSkillKinds.Knowledge, 1, null, false)],
                SkillKarmaPurchases = [new(pistols.SourceSkillId, pistols.Kind, pistols.SourceNodeDigest, 3)],
                QualityOptionIds = [.. request.Selections.QualityOptionIds, quality.OptionId]
            }
        };
    }

    private static CharacterCreationCompleteObligation Limit332(CharacterCreationCompleteAssessment assessment, string id)
        => assessment.Obligations.Single(row => row.Kind == K.SkillLimit && row.Id == "limit:" + id);

    private static CharacterCreationCompleteObligation Operation332(CharacterCreationCompleteAssessment assessment, O operation)
        => assessment.Obligations.Single(row => row.Kind == K.ConsumerOperation && row.Operation == operation);

    [TestMethod]
    public void Complete332_Full_and_cold_selected_execute_supported_consumers_and_keep_real_gaps()
    {
        using var context = Context332();
        var inputs = LoadConsumption(context);
        var request = Request332(inputs);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        var assessment = full.Value!.CompleteAssessment!;
        foreach (string id in new[] { CharacterCreationEffectivePolicyContext.ActiveLimit, CharacterCreationEffectivePolicyContext.KnowledgeLimit })
            Assert.AreEqual(C.Evaluated, Limit332(assessment, id).Coverage, id);
        CollectionAssert.AreEqual(new[] { O.QualityCostsAndLimits, O.SkillPointRules },
            assessment.Obligations.Where(row => row.Kind == K.ConsumerOperation && row.Coverage == C.Evaluated)
                .Select(row => row.Operation!.Value).ToArray());
        Assert.IsTrue(full.Value.Skills!.Skills.Any(row => row.KarmaLevels > 0));
        Assert.IsTrue(full.Value.UpstreamBudget!.Qualities.Qualities.Selections.Count > 0);
        // The resolver still supplies a consumer default for the group cap.
        Assert.AreEqual(C.Unresolved, Limit332(assessment, CharacterCreationEffectivePolicyContext.GroupLimit).Coverage);
        Assert.AreEqual(CharacterCreationCompleteReason.AbsentSourceDefaultUnproven,
            Limit332(assessment, CharacterCreationEffectivePolicyContext.GroupLimit).Reason);
        Assert.AreEqual(C.Unresolved, assessment.Obligations.Single(row => row.Kind == K.PolicyDomain
            && row.Domain == CharacterCreationPolicyDomain.SkillLimits).Coverage);
        Assert.IsTrue(assessment.Obligations.Where(row => row.Kind is K.Source or K.Sourcebook or K.ActiveGraph or K.SelectedGrants)
            .All(row => row.Coverage == C.Unresolved));
        Assert.AreEqual(8, assessment.Obligations.Count(row => row.Kind == K.ActiveGraph));
        Assert.IsTrue(assessment.Obligations.Any(row => row.Operation == O.Campaign && row.Coverage == C.Unresolved));
        Assert.IsTrue(assessment.Obligations.Any(row => row.Reason == CharacterCreationCompleteReason.OpaqueSource && row.Coverage == C.Unresolved));
        Assert.IsTrue(assessment.Obligations.Any(row => row.Setting?.Key == CharacterCreationObservedSetting.FreeGrids
            && row.Reason == CharacterCreationCompleteReason.AbsentSourceDefaultUnproven && row.Coverage == C.Unresolved));
        Assert.IsFalse(assessment.SatisfiesComplete);

        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capture.Outcome, string.Join(",", capture.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, capture.Value!, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var cold));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, cold, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(assessment), JsonSerializer.Serialize(replay.Value!.CompleteAssessment));

        var complete = request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete };
        var blocked = CharacterCreationCompletionReviewService.Review(inputs, complete);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, blocked.Outcome);
        Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, blocked.Value!.Request.PolicyRequirement);
        Assert.AreEqual(true, blocked.Value.CompleteAssessment!.MechanicalSucceeded);
        Assert.AreEqual(C.Evaluated, Operation332(blocked.Value.CompleteAssessment, O.QualityCostsAndLimits).Coverage);
        var claim = capture.Value! with
        {
            PolicyRequirement = CharacterCreationPolicyRequirement.Complete,
            ProposalDigest = CharacterCreationFinalizationDigest.Compute(complete),
            ProvenanceReviewDigest = blocked.Value.ReviewDigest,
            CompleteAssessment = blocked.Value.CompleteAssessment
        };
        claim = claim with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(claim) };
        var refused = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, claim, complete);
        Assert.IsNull(refused.Value);
        CollectionAssert.AreEqual(new[] { CharacterCreationEffectivePolicyContext.CoverageUnavailable }, refused.Blockers.ToArray());
    }

    [TestMethod]
    public void Complete332_Native_language_is_not_a_rating_check_and_untyped_inactivity_stays_unresolved()
    {
        using var context = Context332();
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None) with
            { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV2 };
        var result = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var proof = result.Value!.CompleteAssessment!;
        Assert.AreEqual(C.Evaluated, Limit332(proof, CharacterCreationEffectivePolicyContext.ActiveLimit).Coverage);
        Assert.AreEqual(C.Unresolved, Limit332(proof, CharacterCreationEffectivePolicyContext.KnowledgeLimit).Coverage);
        Assert.IsTrue(proof.Obligations.Where(row => row.Reason == CharacterCreationCompleteReason.UntypedInactivity)
            .All(row => row.Coverage == C.Unresolved));
        // All policy inputs exist; the empty purchase list has not executed a purchase.
        Assert.AreEqual(C.Unresolved, Operation332(proof, O.SkillPurchases).Coverage);
        Assert.AreEqual(C.Unresolved, Operation332(proof, O.MysticAdeptExchange).Coverage);
    }

    [TestMethod]
    public void Complete332_Failed_canonical_evaluation_cannot_discharge_consumers()
    {
        using var context = Context332();
        var inputs = LoadConsumption(context);
        var request = Request332(inputs);
        request = request with { Selections = request.Selections with
        {
            SkillKarmaPurchases = request.Selections.SkillKarmaPurchases!.Select(row => row with { Rating = 99 }).ToArray()
        } };
        var result = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.IsNotNull(result.Value);
        var assessment = result.Value!.CompleteAssessment!;
        Assert.AreEqual(false, assessment.MechanicalSucceeded);
        Assert.IsTrue(assessment.Obligations.Where(row => row.Kind is K.SkillLimit or K.ConsumerOperation)
            .All(row => row.Coverage == C.Unresolved));
        Assert.IsNull(CharacterCreationCompleteConsumerExecution.FromFull(inputs, result.Value,
            CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request)));
    }

    [TestMethod]
    public void Complete332_Consumption_hashes_and_success_boolean_do_not_replace_execution_or_binding()
    {
        using var context = Context332();
        var inputs = LoadConsumption(context);
        var request = Request332(inputs);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var execution = CharacterCreationCompleteConsumerExecution.FromFull(inputs, review, authority);
        Assert.IsNotNull(execution);
        Assert.IsTrue(execution!.EvaluationsFor(authority).Count > 0);
        var receiptOnly = CharacterCreationCompleteAssessmentService.AfterEvaluation(authority,
            JsonSerializer.Deserialize<CharacterCreationPolicyConsumption>(JsonSerializer.Serialize(review.PolicyConsumption)), true);
        Assert.IsTrue(receiptOnly.Obligations.Where(row => row.Kind is K.SkillLimit or K.ConsumerOperation)
            .All(row => row.Coverage == C.Unresolved));
        string drift = CharacterCreationFinalizationDigest.Compute("different-authority");
        foreach (var changed in new[]
        {
            authority with { InputsSnapshotDigest = drift }, authority with { RawCharacterXmlDigest = drift },
            authority with { SelectedInputsDigest = drift }, authority with { PolicyDigest = drift },
            authority with { RequestDigest = drift }, authority with { SelectionsDigest = drift },
            authority with { SettingsObservationDigest = drift },
            authority with { Obligations = authority.Obligations.Select(row => row with { AuthorityDigest = drift }).ToArray() }
        })
            Assert.AreEqual(0, execution.EvaluationsFor(changed).Count);
    }

    [TestMethod]
    public void Complete332_Rehashed_forged_coverage_and_changed_selections_fail_independent_replay()
    {
        using var context = Context332();
        var inputs = LoadConsumption(context);
        var request = Request332(inputs);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        foreach (var rows in new[]
        {
            witness.CompleteAssessment!.Obligations.Select(row => row.Kind == K.ConsumerOperation && row.Operation == O.SkillPointRules
                ? row with { EvaluationDigest = CharacterCreationFinalizationDigest.Compute("rehash") } : row).ToArray(),
            witness.CompleteAssessment.Obligations.Select(row => row.Kind == K.ConsumerOperation && row.Operation == O.Campaign
                ? row with { Coverage = C.Evaluated, EvaluationDigest = CharacterCreationFinalizationDigest.Compute("forged-campaign") } : row).ToArray(),
            witness.CompleteAssessment.Obligations.Select(row => row.Kind == K.SkillLimit
                ? row with { Coverage = C.ProvenInactive } : row).ToArray(),
            witness.CompleteAssessment.Obligations.Where(row => row.Kind != K.ActiveGraph).ToArray()
        })
        {
            var forged = witness with { CompleteAssessment = witness.CompleteAssessment with { Obligations = rows } };
            forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
            var rejected = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, forged, request);
            Assert.IsNull(rejected.Value);
            CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationCompleteAssessment.Invalid);
        }
        // Even equal downstream totals would not authorize a changed choice or request.
        var changed = request with { Selections = request.Selections with { TotalKarmaInvestment = 1 } };
        var copied = witness with { ProposalDigest = CharacterCreationFinalizationDigest.Compute(changed),
            CompleteAssessment = witness.CompleteAssessment with
            {
                RequestDigest = CharacterCreationFinalizationDigest.Compute(changed),
                SelectionsDigest = CharacterCreationFinalizationDigest.Compute(changed.Selections)
            } };
        copied = copied with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(copied) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, copied, changed).Value);
    }

    [TestMethod]
    public void Complete332_Changed_typed_inputs_source_policy_and_duplicate_observations_cannot_reuse_execution()
    {
        using var context = Context332();
        var inputs = LoadConsumption(context);
        var request = Request332(inputs);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        var policy = inputs.EffectivePolicy!;
        var observation = policy.SettingObservations!;
        var present = observation.Settings.First(row => row.State == CharacterCreationSettingObservationState.Present);
        var changedSkillInputs = inputs with { Skills = inputs.Skills with
            { SelectedActiveSkillPoints = inputs.Skills.SelectedActiveSkillPoints + 1 } };
        foreach (var changed in new[]
        {
            changedSkillInputs with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(changedSkillInputs) },
            WithPolicy(inputs, policy with { Sources = policy.Sources.Select(row => row.SourceId == "skills"
                ? row with { ContentDigest = CharacterCreationFinalizationDigest.Compute("changed-skills-source") } : row).ToArray() }),
            WithPolicy(inputs, policy with { Consumers = policy.Consumers! with
                { Qualities = policy.Consumers!.Qualities with { QualityKarmaLimit = policy.Consumers.Qualities.QualityKarmaLimit + 1 } } }),
            WithPolicy(inputs, policy with { SettingObservations = observation with
                { Settings = observation.Settings.Select(row => row == present
                    ? row with { State = CharacterCreationSettingObservationState.Duplicate, ValueDigest = null } : row).ToArray() } })
        })
        {
            var moved = Request332(changed);
            var refused = CharacterCreationCompletionReviewService.Review(changed, moved);
            Assert.IsNull(refused.Value);
            Assert.IsNull(CharacterCreationCompleteConsumerExecution.FromFull(changed, review,
                CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request)));
        }
    }

    [TestMethod]
    public void Complete332_V1_and_omitted_versions_keep_their_coverage_bytes_and_cannot_be_resealed_as_V2()
    {
        using var context = Context332();
        var inputs = LoadConsumption(context);
        var request = Request332(inputs) with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV1 };
        var before = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, before.Outcome);
        Assert.IsTrue(before.Value!.CompleteAssessment!.Obligations.Where(row => row.Kind is K.SkillLimit or K.ConsumerOperation)
            .All(row => row.Coverage == C.Unresolved));
        string bytes = JsonSerializer.Serialize(before.Value);
        var omitted = request with { CompleteAssessmentSchema = null };
        var old = CharacterCreationCompletionReviewService.Review(inputs, omitted);
        Assert.IsFalse(JsonSerializer.Serialize(old.Value).Contains("CompleteAssessment", StringComparison.Ordinal));
        var v2 = CharacterCreationCompletionReviewService.Review(inputs,
            request with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV2 });
        Assert.AreEqual(C.Evaluated, Operation332(v2.Value!.CompleteAssessment!, O.SkillPointRules).Coverage);
        Assert.AreEqual(bytes, JsonSerializer.Serialize(CharacterCreationCompletionReviewService.Review(inputs, request).Value));

        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request).Value!;
        var upgraded = request with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV2 };
        var relabeled = captured with
        {
            ProposalDigest = CharacterCreationFinalizationDigest.Compute(upgraded),
            CompleteAssessment = captured.CompleteAssessment! with
            {
                Schema = CharacterCreationCompleteAssessment.SchemaV2,
                RequestDigest = CharacterCreationFinalizationDigest.Compute(upgraded)
            }
        };
        relabeled = relabeled with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(relabeled) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, relabeled, upgraded).Value);
    }
}
