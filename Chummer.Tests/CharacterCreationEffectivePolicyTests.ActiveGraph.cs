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

// Graph352 reconciliation of Core342; execution is recorded in the controller's retained test packets.
public sealed partial class CharacterCreationEffectivePolicyTests
{
    private static ReadyContext Context342() => ReadyContext.Create(false, includeNonEmptyPurchases: true, includeSkillPurchase: true,
        qualityName: "Paraplegic", amendSettings: settings =>
        {
            SetLimits340(settings, 8, 7, -1, -1);
            settings.Elements("allowfreegrids").Remove();
            settings.Add(new XElement("unclassified-graph352", "opaque"));
            var books = settings.Element("books")!;
            if (!books.Elements("book").Any(book => book.Value == "RF")) books.Add(new XElement("book", "RF"));
        }, prepareSource: root =>
        {
            // Synthetic source amendment through the real compiler, never a copied result.
            string path = Path.Combine(root, "data", "qualities.xml");
            var source = XDocument.Load(path);
            var quality = source.Root!.Element("qualities")!.Elements("quality")
                .Single(row => row.Element("name")!.Value == "Ambidextrous");
            quality.Add(XElement.Parse("<firstlevelbonus><activeskillkarmacostmultiplier><name>Pistols</name><val>50</val></activeskillkarmacostmultiplier></firstlevelbonus>"));
            source.Save(path);
        });

    private static CharacterCreationCompletionReviewRequest Request342(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest? baseline = null)
    {
        var request = (baseline ?? Request332(inputs)) with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV3 };
        var option = inputs.Lifestyles.Authority.LifestyleOptions.Single(row => row.Name == "Low" && row.IsSelectable);
        var home = new CharacterCreationLifestyleConfiguration(Guid.Parse("59396d22-1265-4ddd-bf27-11c7a316b342"),
            option.OptionId, "Manual baseline apartment", CharacterCreationLifestyleStyleIds.Standard,
            option.DefaultIncrementId, 1, 100m, 0, false, false, 0, 0, 0, 0, "Seattle", "", "", []);
        return request with { Selections = request.Selections with
        {
            Lifestyles = [.. request.Selections.Lifestyles, home], StartingLifestyleId = home.LifestyleId,
            TotalKarmaInvestment = Math.Max(2, request.Selections.TotalKarmaInvestment)
        } };
    }

    private static CharacterCreationCompleteObligation Graph342(CharacterCreationCompleteAssessment proof, O operation)
        => proof.Obligations.Single(row => row.Kind == K.ActiveGraph && row.Operation == operation);

    private static CharacterCreationSelectedCompletionEvaluation Cold342(ReadyContext context,
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request)
        => Cold342(context, inputs, request, out _);

    private static CharacterCreationSelectedCompletionEvaluation Cold342(ReadyContext context,
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        out CharacterCreationSelectedCompletionWitness retainedWitness)
    {
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, captured.Value!, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var cold, out var witness));
        retainedWitness = witness;
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, cold, witness, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        return replay.Value!;
    }

    private static CharacterCreationLifestylesService.CompleteGraphExecution ExecuteLifestyle342(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request)
    {
        var choices = request.Selections;
        CharacterCreationLifestylesService.CompleteGraphExecution? execution = null;
        int observed = 0;
        var preview = CharacterCreationLifestylesService.PreviewCompletion(inputs.Lifestyles, inputs.Magic,
            choices.AttributeAllocations, choices.MagicSelections, choices.QualityOptionIds, choices.TotalKarmaInvestment,
            choices.Gear, choices.Lifestyles, choices.StartingLifestyleId, null, default,
            value => { execution = value; observed++; },
            CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request));
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Available, preview.Outcome, string.Join(",", preview.Blockers));
        Assert.AreEqual(1, observed);
        Assert.IsNotNull(execution);
        return execution!;
    }

    [TestMethod]
    public void Complete352_Combined_nonempty_V3_caps_lifestyle_and_skill_modifier_agree_with_cold_replay()
    {
        using var context = Context342();
        string original = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!);
        var inputs = LoadConsumption(context);
        Assert.AreEqual(CharacterCreationCompletionConsumerPolicy.SchemaV5, inputs.EffectivePolicy!.Consumers!.Schema);
        var request = Request342(inputs);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        var review = full.Value!;
        Assert.AreEqual(CharacterCreationCompleteAssessment.SchemaV3, request.CompleteAssessmentSchema);
        Assert.AreEqual(8, inputs.Skills.Authority.MaxActiveSkillRatingCreate);
        Assert.AreEqual(7, inputs.Skills.Authority.MaxKnowledgeSkillRatingCreate);
        Assert.AreEqual(8, inputs.Skills.Authority.MaxSkillGroupRatingCreate);
        AssertLimits340(review.CompleteAssessment!, C.Evaluated);
        var pistols = review.Skills!.Skills.Single(row => row.Name == "Pistols");
        Assert.AreEqual(3, pistols.Rating);
        Assert.AreEqual(3, pistols.KarmaCost); // (rating 3 * rate 2) * 50%.
        Assert.AreEqual(1, pistols.SkillKarmaPrice!.Effects.Count);
        Assert.AreEqual(1, review.Skills.SkillGroups.Single().Rating);
        Assert.IsTrue(review.Skills.Skills.Any(row => row.Kind == CharacterCreationSkillKinds.Knowledge
            && !row.IsNativeLanguage && row.Rating == 1));
        Assert.AreEqual(2200m, review.Lifestyles!.Budget.Used);
        Assert.AreEqual(1, review.Lifestyles.Authority.PurchasedQualityCosts!.Occurrences.Count);
        Assert.IsTrue(review.UpstreamBudget!.Qualities.Qualities.Selections.Any(row => row.Name == "Paraplegic"));
        Assert.IsTrue(review.Skills!.Skills.Any(row => row.KarmaLevels > 0));
        foreach (var operation in new[] { O.LifestyleGrantEffects, O.LifestyleCostEffects, O.SkillKarmaModifiers })
        {
            var row = Graph342(review.CompleteAssessment!, operation);
            Assert.AreEqual(C.Evaluated, row.Coverage);
            Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(row.EvaluationDigest));
        }
        Assert.AreEqual(8, review.CompleteAssessment!.Obligations.Count(row => row.Kind == K.ActiveGraph));
        foreach (var operation in new[] { O.SelectedEffects, O.Amendments, O.Errata, O.CharacterMechanics, O.Campaign })
            Assert.AreEqual(C.Unresolved, Graph342(review.CompleteAssessment, operation).Coverage);
        Assert.IsTrue(review.CompleteAssessment.Obligations.Where(row => row.Kind is K.Source or K.Sourcebook or K.SelectedGrants)
            .All(row => row.Coverage == C.Unresolved));
        AssertGraph352(review.CompleteAssessment);
        Assert.IsTrue(review.CompleteAssessment.Obligations.Any(row => row.Kind == K.Setting
            && row.Reason == CharacterCreationCompleteReason.OpaqueSource && row.Coverage == C.Unresolved));
        Assert.IsTrue(review.CompleteAssessment.Obligations.Any(row => row.Setting?.Key == CharacterCreationObservedSetting.FreeGrids
            && row.Coverage == C.Unresolved));
        var baseline = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        foreach (var allocation in baseline.SkillAllocations)
            Assert.IsTrue(request.Selections.SkillAllocations.Contains(allocation));
        foreach (var allocation in baseline.GroupAllocations)
            Assert.IsTrue(request.Selections.GroupAllocations.Contains(allocation));
        foreach (string quality in baseline.QualityOptionIds)
            CollectionAssert.Contains(request.Selections.QualityOptionIds.ToArray(), quality);
        Assert.AreEqual(JsonSerializer.Serialize(baseline.AttributeAllocations), JsonSerializer.Serialize(request.Selections.AttributeAllocations));
        Assert.AreEqual(JsonSerializer.Serialize(baseline.MagicSelections), JsonSerializer.Serialize(request.Selections.MagicSelections));
        Assert.AreEqual(JsonSerializer.Serialize(baseline.Contacts), JsonSerializer.Serialize(request.Selections.Contacts));
        Assert.AreEqual(JsonSerializer.Serialize(baseline.Gear), JsonSerializer.Serialize(request.Selections.Gear));
        var cold = Cold342(context, inputs, request, out var retainedWitness);
        Assert.AreEqual(JsonSerializer.Serialize(review.CompleteAssessment), JsonSerializer.Serialize(cold.CompleteAssessment));
        Assert.AreEqual(review.Lifestyles.Budget.Used, cold.Lifestyles.FullLifestyleCost);
        Assert.AreEqual(JsonSerializer.Serialize(review.Skills), JsonSerializer.Serialize(cold.Skills));
        // Full evaluation binds the complete catalogue; replay binds the reduced, authenticated witness.
        // Verify both identities independently before comparing every mechanical result field.
        var fullQualities = review.UpstreamBudget!.Qualities.Qualities;
        Assert.AreEqual(inputs.Lifestyles.Qualities.Authority.AuthorityDigest, fullQualities.AuthorityDigest);
        Assert.AreEqual(fullQualities.AuthorityDigest, retainedWitness.Qualities.ProvenanceAuthorityDigest);
        Assert.IsTrue(CharacterCreationQualitiesRules.IsValidSelectedAuthority(retainedWitness.Qualities));
        Assert.AreEqual(retainedWitness.Qualities.WitnessDigest, cold.Qualities.AuthorityDigest);
        Assert.AreEqual(JsonSerializer.Serialize(fullQualities with { AuthorityDigest = cold.Qualities.AuthorityDigest }),
            JsonSerializer.Serialize(cold.Qualities));
        Assert.AreEqual(JsonSerializer.Serialize(review.Lifestyles.Lifestyles), JsonSerializer.Serialize(cold.Lifestyles.Lifestyles));
        Assert.AreEqual(review.Lifestyles.StartingLifestyleId, cold.Lifestyles.StartingLifestyleId);
        AssertLimits340(cold.CompleteAssessment!, C.Evaluated);
        AssertGraph352(cold.CompleteAssessment!);
        Assert.AreEqual(original, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!));

        var complete = CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, complete.Outcome);
        Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, complete.Value!.Request.PolicyRequirement);
        Assert.AreEqual(true, complete.Value.CompleteAssessment!.MechanicalSucceeded);
        Assert.AreEqual(C.Evaluated, Graph342(complete.Value.CompleteAssessment, O.LifestyleCostEffects).Coverage);
        Assert.IsFalse(complete.Value.CompleteAssessment.SatisfiesComplete);
    }

    [TestMethod]
    public void Complete342_Nonzero_skill_modifier_is_executed_and_independently_replayed()
    {
        using var context = ReadyContext.Create(false, prepareSource: root =>
        {
            string path = Path.Combine(root, "data", "qualities.xml");
            var source = XDocument.Load(path);
            var quality = source.Root!.Element("qualities")!.Elements("quality")
                .Single(row => row.Element("id")!.Value == "604aea10-3f13-4f28-a87b-25b8bf677276");
            quality.Add(XElement.Parse("<firstlevelbonus><activeskillkarmacostmultiplier><name>Perception</name><val>50</val></activeskillkarmacostmultiplier></firstlevelbonus>"));
            source.Save(path);
        });
        var inputs = LoadConsumption(context);
        var choices = CharacterCreationCompletionPlannerTests.KnowledgeChoices(inputs);
        var target = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Perception");
        choices = choices with { SkillKarmaPurchases = [.. choices.SkillKarmaPurchases!,
            new(target.SourceSkillId, target.Kind, target.SourceNodeDigest, 1)] };
        var request = Request(inputs, CharacterCreationPolicyRequirement.None) with
        { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV3, Selections = choices };
        var result = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var actual = result.Value!.Skills!.Skills.Single(row => row.SourceSkillId == target.SourceSkillId);
        Assert.AreEqual(1, actual.KarmaCost);
        Assert.AreEqual(1, actual.SkillKarmaPrice!.Effects.Count);
        Assert.AreEqual(C.Evaluated, Graph342(result.Value.CompleteAssessment!, O.SkillKarmaModifiers).Coverage);
        Assert.AreEqual(JsonSerializer.Serialize(result.Value.CompleteAssessment),
            JsonSerializer.Serialize(Cold342(context, inputs, request).CompleteAssessment));
    }

    [TestMethod]
    public void Complete342_Receipts_booleans_and_typed_results_without_execution_do_not_discharge_graph()
    {
        using var context = Context342();
        var inputs = LoadConsumption(context);
        var request = Request342(inputs);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var copied = JsonSerializer.Deserialize<CharacterCreationCompletionReview>(JsonSerializer.Serialize(review))!;
        var noExecution = CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, copied.PolicyConsumption, true);
        Assert.IsTrue(noExecution.Obligations.Where(row => row.Kind == K.ActiveGraph).All(row => row.Coverage == C.Unresolved));
        var onlySkills = CharacterCreationCompleteConsumerExecution.FromFull(inputs, copied, authority)!;
        Assert.IsFalse(onlySkills.EvaluationsFor(authority).ContainsKey("graph:" + O.LifestyleCostEffects));
        Assert.IsFalse(onlySkills.EvaluationsFor(authority).ContainsKey("graph:" + O.LifestyleGrantEffects));

        var executed = ExecuteLifestyle342(inputs, request);
        Assert.IsTrue(executed.MatchesFull(inputs, review, authority));
        var consumers = CharacterCreationCompleteConsumerExecution.FromFull(inputs, review, authority, default, executed)!;
        Assert.IsTrue(consumers.EvaluationsFor(authority).ContainsKey("graph:" + O.LifestyleCostEffects));
        var falseSuccess = CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, review.PolicyConsumption, false,
            consumers: consumers);
        Assert.IsTrue(falseSuccess.Obligations.Where(row => row.Kind == K.ActiveGraph).All(row => row.Coverage == C.Unresolved));
        Assert.IsFalse(executed.MatchesFull(inputs, review with
        { Lifestyles = review.Lifestyles! with { Budget = review.Lifestyles.Budget with { Used = 1m } } }, authority));
    }

    [TestMethod]
    public void Complete342_Execution_is_bound_to_source_policy_request_and_saved_baseline()
    {
        using var context = Context342();
        var inputs = LoadConsumption(context);
        var request = Request342(inputs);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var executed = ExecuteLifestyle342(inputs, request);
        string changed = CharacterCreationFinalizationDigest.Compute("changed admitted evaluation");
        foreach (var moved in new[]
        {
            authority with { InputsSnapshotDigest = changed }, authority with { RawCharacterXmlDigest = changed },
            authority with { SelectedInputsDigest = changed }, authority with { PolicyDigest = changed },
            authority with { RequestDigest = changed }, authority with { SelectionsDigest = changed },
            authority with { SettingsObservationDigest = changed }
        }) Assert.IsFalse(executed.MatchesFull(inputs, review, moved));

        var next = request with { Selections = request.Selections with { TotalKarmaInvestment = request.Selections.TotalKarmaInvestment + 1 } };
        var nextAuthority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, next);
        Assert.IsFalse(executed.MatchesFull(inputs, review with { Request = next }, nextAuthority));
        Assert.IsFalse(executed.MatchesFull(inputs with
        { Lifestyles = inputs.Lifestyles with { Authority = inputs.Lifestyles.Authority with { TrustFundLevel = 4 } } }, review, authority));
    }

    private static SelectedCompletionLifestylesPricingInput Pricing342(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReview review)
        => new(inputs.Lifestyles.Authority, inputs.Lifestyles.Qualities.PrerequisiteDraft,
            inputs.Magic.SelectedTalent.GrantedQualitySources ?? [], review.UpstreamBudget!.Qualities.Qualities.Selections,
            review.UpstreamBudget.Qualities.ProposalDigest, review.Request.Selections.Lifestyles,
            review.Request.Selections.StartingLifestyleId, review.Lifestyles!.Gear.Budget.RemainingNuyen,
            review.Lifestyles.Gear.SourceAnchorIds, []);

    [TestMethod]
    public void Complete342_Empty_baskets_require_actual_grant_and_cost_source_validation()
    {
        using var context = Context332();
        var inputs = LoadConsumption(context);
        var request = Request332(inputs) with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV3, Selections = Request332(inputs).Selections with { Lifestyles = [], StartingLifestyleId = null } };
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var pricing = Pricing342(inputs, review);
        int observed = 0;
        var valid = CharacterCreationLifestylesService.CompleteGraphExecution.Price(pricing, default, _ => observed++, authority);
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Available, valid.Outcome);
        Assert.AreEqual(1, observed);
        Assert.AreEqual(C.Evaluated, Graph342(review.CompleteAssessment!, O.LifestyleGrantEffects).Coverage);
        Assert.AreEqual(C.Evaluated, Graph342(review.CompleteAssessment, O.LifestyleCostEffects).Coverage);

        var unknown = pricing.QualitySelections[0];
        var node = XElement.Parse(unknown.SourceNodeXml);
        node.Element("bonus")?.Remove();
        node.Add(XElement.Parse("<bonus><lifestylecost condition='unimplemented'>-10</lifestylecost></bonus>"));
        string xml = node.ToString(SaveOptions.DisableFormatting);
        foreach (var unsupported in new[]
        {
            pricing with { TalentQualitySources = [], Prerequisite = pricing.Prerequisite with
            { TalentSelection = pricing.Prerequisite.TalentSelection! with { GrantedQualities = ["unresolved grant"] } } },
            pricing with { Prerequisite = pricing.Prerequisite with
            { HeritageSelection = pricing.Prerequisite.HeritageSelection! with { MetatypeName = "Elf", RacialQualitySources = null } } },
            pricing with { QualitySelections = [unknown with { SourceNodeXml = xml,
                SourceNodeDigest = CharacterCreationQualitiesRules.ComputeSourceNodeDigest(xml) }] }
        })
        {
            observed = 0;
            var refused = CharacterCreationLifestylesService.CompleteGraphExecution.Price(unsupported, default, _ => observed++, authority);
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Blocked, refused.Outcome);
            Assert.AreEqual(0, observed);
        }
    }

    [TestMethod]
    public void Complete342_Later_mechanical_failure_and_discarded_manual_quality_cannot_use_earlier_execution()
    {
        using var context = Context342();
        var inputs = LoadConsumption(context);
        var request = Request342(inputs);
        string saved = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!);
        foreach (var choices in new[]
        {
            request.Selections with { SkillKarmaPurchases = request.Selections.SkillKarmaPurchases!
                .Select(row => row with { Rating = 99 }).ToArray() },
            request.Selections with { QualityOptionIds = [] }
        })
        {
            var result = CharacterCreationCompletionReviewService.Review(inputs, request with { Selections = choices });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            if (result.Value?.CompleteAssessment is { } proof)
                Assert.IsTrue(proof.Obligations.Where(row => row.Kind == K.ActiveGraph).All(row => row.Coverage == C.Unresolved));
        }
        Assert.AreEqual(saved, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void Complete342_Cold_replay_rejects_rehashed_forged_graph_digests_status_and_inventory()
    {
        using var context = Context342();
        var inputs = LoadConsumption(context);
        var request = Request342(inputs);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, captured.Value!, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var cold, out var witness));
        var rows = witness.CompleteAssessment!.Obligations;
        foreach (var forgedRows in new[]
        {
            rows.Select(row => row.Kind == K.ActiveGraph && row.Operation == O.LifestyleCostEffects
                ? row with { EvaluationDigest = CharacterCreationFinalizationDigest.Compute("borrowed result") } : row).ToArray(),
            rows.Select(row => row.Kind == K.ActiveGraph && row.Operation == O.LifestyleGrantEffects
                ? row with { Coverage = C.ProvenInactive } : row).ToArray(),
            rows.Select(row => row.Kind == K.ActiveGraph && row.Operation == O.SkillKarmaModifiers
                ? row with { Coverage = C.Unresolved, EvaluationDigest = null } : row).ToArray(),
            rows.Select(row => row.Kind == K.ActiveGraph && row.Operation == O.CharacterMechanics
                ? row with { Coverage = C.Evaluated, EvaluationDigest = CharacterCreationFinalizationDigest.Compute("green mechanics") } : row).ToArray(),
            rows.Where(row => row.Kind != K.ActiveGraph || row.Operation != O.Campaign).ToArray()
        })
        {
            var forged = witness with { CompleteAssessment = witness.CompleteAssessment with { Obligations = forgedRows } };
            forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
            var refused = CharacterCreationSelectedCompletionEvaluator.Replay(original, cold, forged, request);
            Assert.IsNull(refused.Value);
            CollectionAssert.Contains(refused.Blockers.ToArray(), CharacterCreationCompleteAssessment.Invalid);
        }
    }

    [TestMethod]
    public void Complete342_Unknown_skill_modifier_companion_cannot_borrow_a_green_skill_projection()
    {
        using var context = Context332();
        var inputs = LoadConsumption(context);
        var request = Request332(inputs) with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV3 };
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var upstream = review.UpstreamBudget!;
        var selections = upstream.Qualities.Qualities.Selections.ToArray();
        var source = XElement.Parse(selections[0].SourceNodeXml);
        source.Element("bonus")?.Remove();
        source.Add(XElement.Parse("<bonus><activeskillkarmacostmultiplier><name>Pistols</name><val>50</val></activeskillkarmacostmultiplier><skilldisable>Pistols</skilldisable></bonus>"));
        string xml = source.ToString(SaveOptions.DisableFormatting);
        selections[0] = selections[0] with { SourceNodeXml = xml,
            SourceNodeDigest = CharacterCreationQualitiesRules.ComputeSourceNodeDigest(xml) };
        var forged = review with { UpstreamBudget = upstream with
        { Qualities = upstream.Qualities with { Qualities = upstream.Qualities.Qualities with { Selections = selections } } } };
        var execution = CharacterCreationCompleteConsumerExecution.FromFull(inputs, forged, authority);
        Assert.IsNotNull(execution);
        Assert.IsFalse(execution!.EvaluationsFor(authority).ContainsKey("graph:" + O.SkillKarmaModifiers));
    }

    [TestMethod]
    public void Complete352_V1_V2_and_omitted_schemas_do_not_opt_into_graph_execution_evidence()
    {
        using var context = Context342();
        var inputs = LoadConsumption(context);
        var v3 = Request342(inputs);
        var v2 = v3 with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV2 };
        var v1 = v2 with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV1 };
        var omitted = v2 with { CompleteAssessmentSchema = null };
        var before = CharacterCreationCompletionReviewService.Review(inputs, v1).Value!;
        var legacy = CharacterCreationCompletionReviewService.Review(inputs, omitted).Value!;
        string v1Bytes = JsonSerializer.Serialize(before), omittedBytes = JsonSerializer.Serialize(legacy);
        Assert.IsFalse(omittedBytes.Contains("CompleteAssessment", StringComparison.Ordinal));
        Assert.IsTrue(before.CompleteAssessment!.Obligations.Where(row => row.Kind == K.ActiveGraph)
            .All(row => row.Coverage == C.Unresolved));
        int observed = 0;
        CharacterCreationLifestylesService.CompleteGraphExecution.Price(Pricing342(inputs, before), default,
            _ => observed++, CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, v1));
        Assert.AreEqual(0, observed);
        var old = CharacterCreationCompletionReviewService.Review(inputs, v2).Value!;
        string v2Bytes = JsonSerializer.Serialize(old);
        Assert.IsTrue(old.CompleteAssessment!.Obligations.Where(row => row.Kind == K.ActiveGraph)
            .All(row => row.Coverage == C.Unresolved && row.EvaluationDigest is null));
        CharacterCreationLifestylesService.CompleteGraphExecution.Price(Pricing342(inputs, old), default,
            _ => observed++, CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, v2));
        Assert.AreEqual(0, observed);
        var current = CharacterCreationCompletionReviewService.Review(inputs, v3).Value!;
        Assert.AreEqual(C.Evaluated, Graph342(current.CompleteAssessment!, O.LifestyleCostEffects).Coverage);
        Assert.AreEqual(v2Bytes, JsonSerializer.Serialize(CharacterCreationCompletionReviewService.Review(inputs, v2).Value));
        Assert.AreEqual(JsonSerializer.Serialize(old.CompleteAssessment), JsonSerializer.Serialize(Cold342(context, inputs, v2).CompleteAssessment));
        Assert.AreEqual(v1Bytes, JsonSerializer.Serialize(CharacterCreationCompletionReviewService.Review(inputs, v1).Value));
        Assert.AreEqual(omittedBytes, JsonSerializer.Serialize(CharacterCreationCompletionReviewService.Review(inputs, omitted).Value));
    }
}
