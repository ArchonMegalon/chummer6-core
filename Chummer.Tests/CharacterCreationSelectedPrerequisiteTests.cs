using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationSelectedPrerequisiteTests
{
    [TestMethod]
    [DataRow("Magician", "quality", "Magician")]
    [DataRow("Magician", "magenabled", "")]
    [DataRow("Technomancer", "resenabled", "")]
    public void Selected_prerequisite_racial_conflict_appears_only_after_canonical_talent_execution(
        string talent, string predicate, string value)
    {
        using var context = ReadyContext.Create(false, talentValue: talent, metatypeName: "Ork", rankAssignments: OrkRanks(),
            prepareSource: path => RestrictLowLight(path, predicate, value));
        var before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var p = Prepare(context);
        var root = new XElement("character", new XElement("qualities"), new XElement("gears"),
            new XElement("metatype", "Ork"), new XElement("magenabled", "False"),
            new XElement("resenabled", "False"), new XElement("depenabled", "False"));
        var deltas = new List<CharacterCreationFinalizationDelta>();
        int order = 0;
        var prerequisite = p.Inputs.Skills.PrerequisiteDraft;
        Assert.IsTrue(CharacterCreationPriorityRacialQualities.TryApply(root, prerequisite.HeritageSelection!,
            prerequisite.DraftDigest, deltas, ref order), "The earlier racial check admits this graph.");
        Assert.IsTrue(CharacterCreationAwakenedLegacyProjector.TryApply(root, prerequisite,
            p.Skills.Attributes.Attributes, p.Skills.Skills, p.Skills.SkillGroups, p.Inputs.Magic,
            p.Budget.Magic, p.Inputs.SnapshotDigest, deltas, ref order));
        Assert.IsFalse(CharacterCreationSelectedPrerequisiteConsumer.TryCheckFinalGraph(root, prerequisite,
            p.Magic.Talent.GrantedQualitySources!, p.Skills.Attributes.Attributes, out _));
        Assert.IsNull(Full(p));
        Assert.IsNull(Selected(p));
        var review = CharacterCreationCompletionReviewService.Review(p.Inputs, p.Request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, review.Outcome);
        CollectionAssert.Contains(review.Blockers.ToArray(), CharacterCreationSelectedPrerequisiteConsumer.Blocker);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Selected_prerequisite_lawful_final_graph_matches_independent_execution_and_preserves_history()
    {
        using var context = ReadyContext.Create(true, talentValue: "Magician", metatypeName: "Ork", rankAssignments: OrkRanks(),
            prepareSource: path => RestrictLowLight(path, "resenabled", ""));
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var p = Prepare(context);
        var full = Full(p);
        var selected = Selected(p);
        Assert.IsNotNull(full);
        Assert.IsNotNull(selected);
        Assert.AreEqual(JsonSerializer.Serialize(full!.Evaluation), JsonSerializer.Serialize(selected!.Evaluation));
        Assert.IsTrue(CharacterCreationSelectedPrerequisiteConsumer.MatchesEvidence(selected, full.Evaluation));
        Assert.IsFalse(CharacterCreationSelectedPrerequisiteConsumer.MatchesEvidence(selected, null));
        foreach (var forgedEvidence in new[]
        {
            full.Evaluation with { Checks = [] },
            full.Evaluation with { EnabledFlags = [] },
            full.Evaluation with { Checks = full.Evaluation.Checks.Select(row => row with
                { SourceDigest = CharacterCreationFinalizationDigest.Compute("rehashed replacement") }).ToArray() }
        })
            Assert.IsFalse(CharacterCreationSelectedPrerequisiteConsumer.MatchesEvidence(selected, forgedEvidence));
        Assert.AreEqual((p.Inputs.Skills.PrerequisiteDraft.HeritageSelection!.RacialQualitySources?.Count ?? 0)
            + p.Magic.Talent.GrantedQualitySources!.Count, full.Evaluation.Checks.Count);
        CollectionAssert.AreEqual(new[] { "magenabled" }, full.Evaluation.EnabledFlags.ToArray());
        var review = CharacterCreationCompletionReviewService.Review(p.Inputs, p.Request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        Assert.IsNull(review.Value!.SelectedPrerequisites, "Historical request shapes retain nullable omission.");
        var source = context.Resolver.TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(source, original.Document.Content, out var bootstrap));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, p.Inputs, p.Request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capture.Outcome, string.Join(",", capture.Blockers));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, capture.Value!, p.Request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.IsTrue(CharacterCreationSelectedCompletionEvaluator.MatchesFullReview(replay.Value!, review.Value));
        Assert.IsTrue(source.TryResolveCreationDefaultStartingNuyen(out var cash));
        var cashChoice = new CharacterCreationStartingCashChoice(cash!.AuthorityDigest, cash.Dice * 3);
        var fullProjection = CharacterCreationCompletionFinalizationProjector.Project(original, bootstrap, p.Inputs,
            review.Value, cash, cashChoice);
        var selectedProjection = CharacterCreationCompletionTransaction.ProjectSelected(original, bootstrap,
            capture.Value!, p.Request, cash, cashChoice);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, fullProjection.Outcome, string.Join(",", fullProjection.Blockers));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, selectedProjection.Outcome, string.Join(",", selectedProjection.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(fullProjection.Value), JsonSerializer.Serialize(selectedProjection.Value),
            "The complete XML, deltas and native IDs must match, not just the budgets.");
        var projected = XElement.Parse(fullProjection.Value!.CharacterXml);
        foreach (var grant in p.Magic.Talent.GrantedQualitySources!)
        {
            string expected = CharacterCreationFinalizationProjector.StableGuid(
                $"heritage-quality:{grant.SourceId}:{grant.ForcedSelection}:{grant.SourceNodeDigest}:{review.Value.ReviewDigest}").ToString("D");
            Assert.AreEqual(expected, projected.Element("qualities")!.Elements("quality")
                .Single(row => row.Element("sourceid")?.Value == grant.SourceId).Element("guid")!.Value);
        }
        // Even a real execution result cannot be injected into an older witness.
        var forged = capture.Value! with { SelectedPrerequisites = full.Evaluation };
        forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, forged, p.Request).Value);
        Assert.AreEqual(JsonSerializer.Serialize(original), JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    [DataRow("<forbidden><oneof><unknown /></oneof></forbidden>")]
    [DataRow("<forbidden><oneof><magenabled xmlns='urn:unknown' /></oneof></forbidden>")]
    [DataRow("<forbidden><oneof><resenabled /><resenabled /></oneof></forbidden>")]
    [DataRow("<forbidden><oneof><resenabled /></oneof></forbidden><forbidden><oneof><depenabled /></oneof></forbidden>")]
    [DataRow("<forbidden xmlns='urn:unknown'><oneof><resenabled /></oneof></forbidden>")]
    [DataRow("<required><oneof><quality>Magician</quality></oneof></required>")]
    public void Selected_prerequisite_strict_grammar_refuses_unexecuted_clauses(string restriction)
    {
        using var context = ReadyContext.Create(false, talentValue: "Magician");
        var p = Prepare(context);
        var definition = XElement.Parse(p.Magic.Talent.GrantedQualitySources![0].CanonicalSourceXml);
        definition.Elements("required").Remove();
        definition.Elements("forbidden").Remove();
        definition.Add(XElement.Parse("<clauses>" + restriction + "</clauses>").Elements());
        var root = new XElement("character", new XElement("qualities", new XElement("quality", new XElement("name", "Magician"))));
        Assert.ThrowsExactly<InvalidDataException>(() => CharacterCreationAwakenedLegacyProjector.CheckRestrictions(
            definition, root, [], new HashSet<string>(StringComparer.Ordinal)));
    }

    [TestMethod]
    public void Selected_prerequisite_replay_rejects_rehashed_source_forced_choice_and_original_attribute_drift()
    {
        using var context = ReadyContext.Create(false, talentValue: "Magician");
        var p = Prepare(context);
        Assert.IsNotNull(Selected(p));
        var source = p.Magic.Talent.GrantedQualitySources![0];
        foreach (var changed in new[] { source with { CanonicalSourceXml = "<quality />" }, source with { ForcedSelection = "unadmitted" } })
        {
            var magic = p.Magic with
            {
                Talent = p.Magic.Talent with { GrantedQualitySources = new[] { changed }.Concat(p.Magic.Talent.GrantedQualitySources.Skip(1)).ToArray() }
            };
            magic = magic with { PayloadDigest = CharacterCreationSelectedMagicRules.ComputePayloadDigest(magic) };
            Assert.IsNull(Selected(p with { Magic = magic }));
        }
        var changedAttributes = p.Inputs.Magic.Attributes.Attributes.Select(row => row.AttributeId == "MAG"
            ? row with { Current = row.Current + 1 } : row).ToArray();
        Assert.IsNull(CharacterCreationSelectedPrerequisiteConsumer.EvaluateSelected(p.Inputs.Skills.PrerequisiteDraft,
            changedAttributes, p.Magic, p.Skills, p.Budget.Qualities.Qualities, p.Inputs.SnapshotDigest,
            CharacterCreationFinalizationDigest.Compute(p.Request.Selections)));
        var proposed = p.Skills with { Attributes = p.Skills.Attributes with { Attributes = changedAttributes } };
        Assert.IsNull(Selected(p with { Skills = proposed }));
    }

    internal static IReadOnlyDictionary<string, string> OrkRanks() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // The generic awakened fixture uses Heritage E, which cannot select Ork.
        [CharacterCreationPriorityCategoryIds.Attributes] = "A",
        [CharacterCreationPriorityCategoryIds.Talent] = "B",
        [CharacterCreationPriorityCategoryIds.Heritage] = "C",
        [CharacterCreationPriorityCategoryIds.Skills] = "D",
        [CharacterCreationPriorityCategoryIds.Resources] = "E"
    };

    internal static void RestrictLowLight(string path, string predicate, string value)
    {
        string file = Path.Combine(path, "data", "qualities.xml");
        var source = XDocument.Load(file);
        var quality = source.Root!.Element("qualities")!.Elements("quality")
            .Single(row => row.Element("name")?.Value == "Low-Light Vision");
        quality.Elements("forbidden").Remove();
        quality.Add(new XElement("forbidden", new XElement("oneof", new XElement(predicate, value))));
        source.Save(file);
    }

    private sealed record Proposal(CharacterCreationCompletionReviewInputs Inputs,
        CharacterCreationCompletionReviewRequest Request, CharacterCreationAttributeSkillsPreview Skills,
        CharacterCreationCompletionBudgetPreview Budget, CharacterCreationSelectedMagicAuthority Magic);

    private static Proposal Prepare(ReadyContext context)
    {
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var skills = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills, choices.AttributeAllocations,
            choices.SkillAllocations, choices.GroupAllocations, choices.SkillKarmaPurchases);
        var budget = CharacterCreationCompletionBudgetService.Preview(inputs.Magic, inputs.Lifestyles.Qualities,
            choices.AttributeAllocations, choices.MagicSelections, choices.QualityOptionIds);
        var magic = CharacterCreationMagicResonanceService.CaptureSelectedMagicAuthority(inputs.Magic, choices.MagicSelections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, skills.Outcome, string.Join(",", skills.Blockers));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, budget.Outcome, string.Join(",", budget.Blockers));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, magic.Outcome, string.Join(",", magic.Blockers));
        return new(inputs, new(inputs.SnapshotDigest, choices), skills.Value!, budget.Value!, magic.Value!);
    }

    private static CharacterCreationSelectedPrerequisiteConsumer.Execution? Full(Proposal p)
        => CharacterCreationSelectedPrerequisiteConsumer.EvaluateFull(p.Inputs.Magic, p.Budget.Magic, p.Skills,
            p.Budget.Qualities.Qualities, p.Inputs.SnapshotDigest, CharacterCreationFinalizationDigest.Compute(p.Request.Selections));

    private static CharacterCreationSelectedPrerequisiteConsumer.Execution? Selected(Proposal p)
        => CharacterCreationSelectedPrerequisiteConsumer.EvaluateSelected(p.Inputs.Skills.PrerequisiteDraft,
            p.Inputs.Magic.Attributes.Attributes, p.Magic, p.Skills, p.Budget.Qualities.Qualities,
            p.Inputs.SnapshotDigest, CharacterCreationFinalizationDigest.Compute(p.Request.Selections));
}

public sealed partial class CharacterCreationCompletionApplyTests
{
    [TestMethod]
    public void Selected_prerequisite_source_conflict_blocks_review_and_apply_without_runner_writes()
    {
        using var context = ReadyContext.Create(true, talentValue: "Magician", metatypeName: "Ork",
            rankAssignments: CharacterCreationSelectedPrerequisiteTests.OrkRanks(),
            prepareSource: path => CharacterCreationSelectedPrerequisiteTests.RestrictLowLight(path, "resenabled", ""));
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = Service(context, context.Store, owners);
        var inputs = Inputs(context, context.Store, owners);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var request = new CharacterCreationCompletionReviewRequest(inputs.SnapshotDigest, choices);
        var cash = CashChoice(context, inputs, choices);
        var admitted = service.Review(owners.Capture(), context.WorkspaceId, request, cash);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, admitted.Outcome, string.Join(",", admitted.Blockers));
        var command = Command(admitted.Value!, "selected-prerequisite-source-drift");
        CharacterCreationSelectedPrerequisiteTests.RestrictLowLight(Path.Combine(context.Directory, "source"), "magenabled", "");
        var before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var files = Snapshot(context);
        Assert.IsNull(service.Review(owners.Capture(), context.WorkspaceId, request, cash).Value);
        Assert.IsNull(service.Apply(owners.Capture(), command).Value);
        AssertUnchanged(context, files);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
    }
}

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    public void Selected_prerequisite_assessed_history_replays_and_rejects_missing_forged_and_rehashed_checks()
    {
        using var context = ReadyContext.Create(false, talentValue: "Magician");
        var inputs = LoadConsumption(context);
        // This is an actual optional assessed history, never positive Complete authority.
        // Both routes receive the identical request and independently execute it.
        var request = Assessment308(inputs, CharacterCreationPolicyRequirement.None);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capture.Outcome, string.Join(",", capture.Blockers));
        var witness = capture.Value!;
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, witness, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var cold));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, cold, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(full.Value!.SelectedPrerequisites), JsonSerializer.Serialize(replay.Value!.SelectedPrerequisites));
        Assert.AreEqual(JsonSerializer.Serialize(full.Value.CompleteAssessment), JsonSerializer.Serialize(replay.Value.CompleteAssessment));
        var actual = witness.SelectedPrerequisites!;
        foreach (var forged in new CharacterCreationSelectedPrerequisiteEvaluation?[]
        {
            null,
            actual with { Checks = [] },
            actual with { Checks = actual.Checks.Concat(actual.Checks).ToArray() },
            actual with { EnabledFlags = [] },
            actual with { ProposedAttributesDigest = CharacterCreationFinalizationDigest.Compute("forged attributes") },
            actual with { Checks = actual.Checks.Select(row => row with
                { SourceDigest = CharacterCreationFinalizationDigest.Compute("rehashed source") }).ToArray() }
        })
        {
            var changed = witness with { SelectedPrerequisites = forged };
            // Rehash even the coverage claim. Independent execution must still win.
            changed = changed with { CompleteAssessment = witness.CompleteAssessment! with
            {
                Obligations = witness.CompleteAssessment.Obligations.Select(row =>
                    row.Kind == CharacterCreationCompleteObligationKind.SelectedPrerequisites
                        ? row with { EvaluationDigest = CharacterCreationFinalizationDigest.Compute(forged) } : row).ToArray()
            } };
            changed = changed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changed) };
            Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, changed, request).Value);
        }
        Assert.AreEqual(JsonSerializer.Serialize(original), JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Selected_prerequisite_Complete_executes_before_coverage_without_closing_other_obligations(bool conflict)
    {
        using var context = ReadyContext.Create(false, talentValue: "Magician", metatypeName: "Ork",
            rankAssignments: CharacterCreationSelectedPrerequisiteTests.OrkRanks(),
            prepareSource: path => CharacterCreationSelectedPrerequisiteTests.RestrictLowLight(path,
                conflict ? "magenabled" : "resenabled", ""));
        var inputs = LoadConsumption(context);
        var request = Assessment308(inputs, CharacterCreationPolicyRequirement.Complete);
        var before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var result = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        var review = result.Value!;
        Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, review.Request.PolicyRequirement);
        if (conflict)
        {
            Assert.IsNull(review.SelectedPrerequisites);
            Assert.AreEqual(false, review.CompleteAssessment!.MechanicalSucceeded);
            Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved, review.CompleteAssessment.Obligations
                .Single(item => item.Kind == CharacterCreationCompleteObligationKind.SelectedPrerequisites).Coverage);
            CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationSelectedPrerequisiteConsumer.Blocker);
            Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
            return;
        }
        Assert.IsNotNull(review.SelectedPrerequisites);
        Assert.IsTrue(review.SelectedPrerequisites!.Checks.Count > 0);
        var assessment = review.CompleteAssessment!;
        Assert.AreEqual(true, assessment.MechanicalSucceeded);
        Assert.IsFalse(assessment.SatisfiesComplete);
        var row = assessment.Obligations.Single(item => item.Kind == CharacterCreationCompleteObligationKind.SelectedPrerequisites);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Evaluated, row.Coverage);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(review.SelectedPrerequisites), row.EvaluationDigest);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved,
            assessment.Obligations.Single(item => item.Kind == CharacterCreationCompleteObligationKind.SelectedGrants).Coverage);
        var graph = assessment.Obligations.Where(item => item.Kind == CharacterCreationCompleteObligationKind.ActiveGraph).ToArray();
        Assert.AreEqual(8, graph.Length);
        Assert.IsTrue(graph.All(item => item.Coverage == CharacterCreationCompleteCoverage.Unresolved));
        Assert.IsFalse(CharacterCreationCompleteAssessmentService.AssessAuthority(inputs, request).Value!.MayRequestCompleteCandidate);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Selected_prerequisite_empty_scope_and_missing_execution_do_not_discharge_coverage()
    {
        using var context = ReadyContext.Create(false);
        var inputs = LoadConsumption(context);
        var request = Assessment308(inputs, CharacterCreationPolicyRequirement.Complete);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        Assert.IsNotNull(review.SelectedPrerequisites);
        Assert.AreEqual(0, review.SelectedPrerequisites!.Checks.Count);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved, review.CompleteAssessment!.Obligations
            .Single(item => item.Kind == CharacterCreationCompleteObligationKind.SelectedPrerequisites).Coverage);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var absent = CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, review.PolicyConsumption, true);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved, absent.Obligations
            .Single(item => item.Kind == CharacterCreationCompleteObligationKind.SelectedPrerequisites).Coverage);
    }
}
