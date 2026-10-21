using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready695 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using Owners695 = Chummer.Tests.CharacterCreationAttributesServiceTests.AllocationOwners;
using C695 = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;
using K695 = Chummer.Contracts.Characters.CharacterCreationCompleteObligationKind;
using O695 = Chummer.Contracts.Characters.CharacterCreationConsumerPolicyOperation;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    // Authored, UNEXECUTED. This tests the mechanics slice. PrivateWire674 remains
    // the unchanged strict Complete acceptance gate; this is not a Career proof.
    [TestMethod]
    public void Mechanics695_Strict674_ordinary_character_executes_mechanics_and_retained_sources()
    {
        using var context = Ready695.Create(includeGearReview: true, includeNonEmptyPurchases: true,
            qualityName: "Ambidextrous", prepareSource: _ => { });
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var owners = new Owners695();
        var original = store.Get(context.WorkspaceId).Value!;
        string before = CharacterCreationFinalizationDigest.Compute(original);
        var prepared = Prepare695(context, store, owners);
        var request = Request695(prepared.Inputs);
        Assert.IsTrue(request.Selections.SkillAllocations.Count > 0);
        Assert.IsTrue(request.Selections.QualityOptionIds.Count > 0);
        Assert.IsTrue(request.Selections.Gear.Count > 0);
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(prepared.Inputs.Skills.Authority.AuthorityDigest));
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(prepared.Inputs.RuleSourceCaptureDigest));
        var full = Review695(prepared, request);
        AssertMechanics695(full.CompleteAssessment!);

        // Exercise the actual source-compiled presence effect. Ambidextrous is a
        // zero-valued improvement whose mechanical output is a changed modifier.
        Assert.IsTrue(CharacterCreationLegacySourceProjector.TryProjectCompletionQualityEffects(
            full.UpstreamBudget!.Qualities.Qualities.Selections, out var effects));
        var hands = effects.Where(row => row.ImprovementType == "Ambidextrous").ToArray();
        Assert.AreEqual(1, hands.Length);
        Assert.AreEqual(0m, hands[0].Value);
        Assert.IsTrue(CharacterCreationActionPoolRules695.TryEvaluateHandedness("Right",
            hands.Select(_ => true).ToArray(), out var ambidextrous));
        Assert.IsTrue(CharacterCreationActionPoolRules695.TryEvaluateHandedness("Right", [], out var ordinary));
        Assert.AreEqual(-2, ordinary!.LeftHandModifier);
        Assert.AreEqual(0, ambidextrous!.LeftHandModifier);
        Assert.IsTrue(ambidextrous.Ambidextrous);
        Assert.IsTrue(full.Skills!.Skills.Any(row => row.IsNativeLanguage));

        byte[] privateInputs = prepared.Execution.ExportPrivateInputs();
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            privateInputs, prepared.Inputs.RuleSourceCaptureDigest!, out var restored));
        System.IO.Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        Assert.IsNull(context.Resolver.TryCreateContext(original.Document.Content));
        var cold = Review695(new(prepared.Inputs, new(restored!, context.Queries)), request);
        AssertMechanics695(cold.CompleteAssessment!);
        CollectionAssert.AreEqual(MechanicsRows695(full.CompleteAssessment!).Select(row => row.EvaluationDigest).ToArray(),
            MechanicsRows695(cold.CompleteAssessment!).Select(row => row.EvaluationDigest).ToArray());
        Assert.AreEqual(JsonSerializer.Serialize(full.Skills), JsonSerializer.Serialize(cold.Skills));
        Assert.AreEqual(JsonSerializer.Serialize(full.Balances), JsonSerializer.Serialize(cold.Balances));
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(
            FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries).Get(context.WorkspaceId).Value!));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Mechanics695_Legal_candidate_edit_reexecutes_real_attributes_and_skill_ratings()
    {
        using var context = Ready695.Create(includeGearReview: true, includeNonEmptyPurchases: true,
            qualityName: "Ambidextrous", prepareSource: _ => { });
        var prepared = Prepare695(context, context.Store, new Owners695());
        var original = context.Store.Get(context.WorkspaceId).Value!;
        string before = CharacterCreationFinalizationDigest.Compute(original);
        var request = Request695(prepared.Inputs);
        var baseline = Review695(prepared, request);
        AssertMechanics695(baseline.CompleteAssessment!);
        var pistols = prepared.Inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var changed = request with
        {
            Selections = request.Selections with
            {
                AttributeAllocations = request.Selections.AttributeAllocations.Where(row => row.AttributeId != "AGI")
                    .Append(new CharacterCreationAttributeAllocation("AGI", 1, 0)).ToArray(),
                SkillAllocations = request.Selections.SkillAllocations.Append(
                    new CharacterCreationSkillAllocation(pistols.SourceSkillId, CharacterCreationSkillKinds.Active, 2, null, false)).ToArray()
            }
        };
        var evaluated = Review695(prepared, changed);
        AssertMechanics695(evaluated.CompleteAssessment!);
        Assert.AreEqual(2, evaluated.Skills!.Skills.Single(row => row.SourceSkillId == pistols.SourceSkillId).EffectiveRating);
        Assert.AreEqual(2, evaluated.Skills.Attributes.Attributes.Single(row => row.AttributeId == "AGI").Current);
        Assert.AreEqual(baseline.Skills!.ActiveSkillPointBudget.Used + 2, evaluated.Skills.ActiveSkillPointBudget.Used);
        Assert.AreNotEqual(MechanicsRows695(baseline.CompleteAssessment!).First().EvaluationDigest,
            MechanicsRows695(evaluated.CompleteAssessment!).First().EvaluationDigest);
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void Mechanics695_Missing_execution_wrong_source_owner_and_changed_candidate_cannot_borrow_coverage()
    {
        using var context = Ready695.Create(includeGearReview: true, includeNonEmptyPurchases: true,
            qualityName: "Ambidextrous", prepareSource: _ => { });
        var owners = new Owners695();
        var prepared = Prepare695(context, context.Store, owners);
        var request = Request695(prepared.Inputs);
        AssertMechanics695(Review695(prepared, request).CompleteAssessment!);
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(prepared.Inputs, request).Value);
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        owners.Set(stamp.Owner);
        Assert.IsNull(reader.LoadCompletionInputsWithPurchaseSourceExecution(stamp, new(context.WorkspaceId),
            includeLoadout: true).Value);
        Assert.AreEqual(0, owners.ActiveLeases);

        var altered = request with { Selections = request.Selections with
            { AttributeAllocations = [new("BOD", int.MaxValue, 0)] } };
        AssertNoMechanics695(CharacterCreationCompletionReviewService.Review(prepared.Inputs, altered,
            ruleSources: prepared.Execution).Value?.CompleteAssessment);
        var mismatchedCapture = request with { RuleSourceCaptureDigest = CharacterCreationFinalizationDigest.ComputeUtf8("wrong-695") };
        AssertNoMechanics695(CharacterCreationCompletionReviewService.Review(prepared.Inputs, mismatchedCapture,
            ruleSources: prepared.Execution).Value?.CompleteAssessment);
        byte[] bytes = prepared.Execution.ExportPrivateInputs();
        bytes[^1] ^= 1;
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            bytes, prepared.Inputs.RuleSourceCaptureDigest!, out _));

        var original = context.Store.Get(context.WorkspaceId).Value!;
        string path = Path.Combine(context.Directory, "source", "data", "qualities.xml");
        var source = XDocument.Load(path);
        source.Root!.Element("qualities")!.Elements("quality")
            .Single(row => row.Element("name")?.Value == "Ambidextrous").SetElementValue("karma", "5");
        source.Save(path);
        string root = Path.Combine(context.Directory, "source");
        var changedResolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
        Assert.IsTrue(changedResolver.TryCaptureRuleSources(original.Document.Content, out var changedCapture));
        Assert.AreNotEqual(prepared.Inputs.RuleSourceCaptureDigest, changedCapture!.IntegrityDigest);
        AssertNoMechanics695(CharacterCreationCompletionReviewService.Review(prepared.Inputs, request,
            ruleSources: new(changedCapture, context.Queries)).Value?.CompleteAssessment);
    }

    [TestMethod]
    public void Mechanics695_Unexecuted_active_original_effect_stays_unresolved()
    {
        // Negative fixture only: authority binds this original before any drafts.
        // The strict ordinary positive above is never simplified or edited.
        using var context = Ready695.Create(includeGearReview: true, includeNonEmptyPurchases: true,
            qualityName: "Ambidextrous", beforeDrafts: root => root.Element("improvements")!.Add(
                CharacterCreationAwakenedLegacyProjector.Improvement("UnexecutedMechanic695", "", "", "Custom", 7)),
            prepareSource: _ => { });
        var prepared = Prepare695(context, context.Store, new Owners695());
        var request = Request695(prepared.Inputs);
        var evaluated = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request, ruleSources: prepared.Execution);
        AssertNoMechanics695(evaluated.Value?.CompleteAssessment);
        Assert.IsFalse(evaluated.Value?.CompleteAssessment?.SatisfiesComplete ?? false);
    }

    [TestMethod]
    public void Mechanics695_Action_rule_executes_rated_default_native_and_overflow_branches()
    {
        Assert.IsTrue(CharacterCreationActionPoolRules695.TryEvaluate(new(3, 2, true, false, true, true, 1), out var rated));
        Assert.AreEqual(6, rated!.DicePool);
        Assert.AreEqual(8, rated.SpecializedDicePool);
        Assert.IsTrue(CharacterCreationActionPoolRules695.TryEvaluate(new(3, 0, true, false, true, false, 0), out var defaults));
        Assert.AreEqual(2, defaults!.DicePool);
        Assert.AreEqual(-1, defaults.DefaultPenalty);
        Assert.IsTrue(CharacterCreationActionPoolRules695.TryEvaluate(new(3, 0, true, false, false, false, 0), out var unavailable));
        Assert.IsFalse(unavailable!.Available);
        Assert.AreEqual(0, unavailable.DicePool);
        Assert.IsTrue(CharacterCreationActionPoolRules695.TryEvaluate(new(3, 0, true, true, false, false, 0), out var native));
        Assert.IsTrue(native!.NativeLanguage);
        Assert.IsNull(native.DicePool);
        Assert.IsFalse(CharacterCreationActionPoolRules695.TryEvaluate(new(3, 2, true, false, true, false, int.MaxValue), out _));
        Assert.IsTrue(CharacterCreationActionPoolRules695.TryEvaluateResistance(3, 4, [2, 1], out var resistance));
        Assert.AreEqual(10, resistance!.DicePool);
    }

    private static CharacterCreationRuleSourcePreparation Prepare695(Ready695 context,
        Chummer.Application.Workspaces.IWorkspaceStore store, Owners695 owners)
    {
        var reader = new OwnerBoundCharacterCreationCompletionReader(store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithPurchaseSourceExecution(owners.Capture(), new(context.WorkspaceId), includeLoadout: true);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        Assert.IsNotNull(loaded.Value);
        return loaded.Value!;
    }
    private static CharacterCreationCompletionReviewRequest Request695(CharacterCreationCompletionReviewInputs inputs)
        => Request(inputs, CharacterCreationPolicyRequirement.Complete) with
        {
            CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4,
            OriginalCharacterExecutionSchema = CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1,
            RuleSourceExecutionSchema = CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2,
            RuleSourceCaptureDigest = inputs.RuleSourceCaptureDigest,
            AdditionalConsumerExecutionSchema = CharacterCreationCompletionReviewSchemas.AdditionalConsumerExecutionV1,
            MaximumAttributesDefaultExecutionSchema = CharacterCreationCompletionReviewSchemas.MaximumAttributesDefaultExecutionV1
        };
    private static CharacterCreationCompletionReview Review695(CharacterCreationRuleSourcePreparation prepared,
        CharacterCreationCompletionReviewRequest request)
    {
        var result = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request, ruleSources: prepared.Execution);
        Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
        Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, result.Value!.Request.PolicyRequirement);
        Assert.AreEqual(true, result.Value.CompleteAssessment!.MechanicalSucceeded, string.Join(",", result.Blockers));
        return result.Value;
    }
    private static CharacterCreationCompleteObligation[] MechanicsRows695(CharacterCreationCompleteAssessment assessment)
        => assessment.Obligations.Where(row => row.Domain == CharacterCreationPolicyDomain.CharacterMechanics
            || row.Operation == O695.CharacterMechanics).ToArray();
    private static void AssertMechanics695(CharacterCreationCompleteAssessment assessment)
    {
        var rows = MechanicsRows695(assessment);
        Assert.IsTrue(rows.Any(row => row.Kind == K695.PolicyDomain));
        Assert.IsTrue(rows.Any(row => row.Kind == K695.ActiveGraph));
        Assert.IsTrue(rows.All(row => row.Coverage == C695.Evaluated
            && CharacterCreationFinalizationDigest.IsCanonical(row.EvaluationDigest)));
    }
    private static void AssertNoMechanics695(CharacterCreationCompleteAssessment? assessment)
        => Assert.IsFalse(assessment is not null && MechanicsRows695(assessment).Any(row => row.Coverage == C695.Evaluated));
}
