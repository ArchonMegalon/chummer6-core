using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready696 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using Coverage696 = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    // These assert positive source execution on real domain-created characters.
    // They do not replace the strict PrivateWire674 Complete acceptance gate.
    [TestMethod]
    public void SourceScopes696_Ordinary_character_executes_optional_source_selectors_and_cold_replays()
    {
        using var context = Ready696.Create(includeGearReview: true, includeNonEmptyPurchases: true,
            qualityName: "Ambidextrous", prepareSource: _ => { });
        string before = CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!);
        var prepared = PrepareSources696(context);
        var request = SourceRequest696(prepared.Inputs);
        var reviewed = ReviewSources696(prepared, request);
        Assert.AreEqual(true, reviewed.CompleteAssessment!.MechanicalSucceeded);
        foreach (string scope in new[] { "traditions", "streams", "powers", "spells", "complexforms", "weapons", "armor" })
            AssertSource696(prepared.Inputs, reviewed.CompleteAssessment, scope, Coverage696.Evaluated);

        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            prepared.Execution.ExportPrivateInputs(), prepared.Inputs.RuleSourceCaptureDigest!, out var retained));
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var cold = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request,
            ruleSources: new CharacterCreationRuleSourceExecutionContext(retained!, context.Queries));
        Assert.IsNotNull(cold.Value);
        Assert.AreEqual(JsonSerializer.Serialize(reviewed.CompleteAssessment),
            JsonSerializer.Serialize(cold.Value!.CompleteAssessment));
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void SourceScopes696_Selected_tradition_executes_changed_source_effect_and_retains_exact_compilation()
    {
        const string changedDrain = "{WIL} + {INT}";
        using var context = Ready696.Create(includeGearReview: true,
            talentValue: CharacterCreationMagicResonanceKinds.Magician, prepareSource: root =>
            {
                string path = Path.Combine(root, "data", "traditions.xml");
                var document = XDocument.Load(path);
                var hermetic = document.Root!.Element("traditions")!.Elements("tradition")
                    .Single(row => row.Element("name")!.Value == "Hermetic");
                hermetic.SetElementValue("drain", changedDrain);
                document.Save(path);
            });
        var prepared = PrepareSources696(context);
        var request = SourceRequest696(prepared.Inputs);
        var preview = CharacterCreationMagicResonanceService.PreviewCompletion(prepared.Inputs.Magic,
            request.Selections.AttributeAllocations, request.Selections.MagicSelections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, preview.Outcome, string.Join(",", preview.Blockers));
        Assert.IsNotNull(request.Selections.MagicSelections.Tradition);
        Assert.IsTrue(CharacterCreationMagicResonanceFinalizationRules.TryProjectOption(
            prepared.Inputs.Magic.Authority.Traditions, request.Selections.MagicSelections.Tradition!, 1, out var source));
        var graph = new XElement("character");
        var deltas = new List<CharacterCreationFinalizationDelta>();
        int order = 0;
        using (var trace = CharacterCreationCompiledSourceTrace.Begin())
        {
            CharacterCreationAwakenedLegacyProjector.ApplyTradition(graph, source!,
                CharacterCreationFinalizationDigest.ComputeUtf8("source-scope-696"), deltas, ref order);
            var consumed = trace.Finish();
            Assert.AreEqual(1, consumed.Count);
            Assert.AreEqual(changedDrain, graph.Element("tradition")!.Element("drain")!.Value);
            Assert.AreEqual(graph.Element("tradition")!.ToString(SaveOptions.DisableFormatting), consumed[0].EffectXml);
            Assert.AreEqual("{}", JsonSerializer.Serialize(consumed[0]), "Private source text must not serialize.");
        }
        var reviewed = ReviewSources696(prepared, request);
        Assert.AreEqual(true, reviewed.CompleteAssessment!.MechanicalSucceeded);
        AssertSource696(prepared.Inputs, reviewed.CompleteAssessment, "traditions", Coverage696.Evaluated);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            prepared.Execution.ExportPrivateInputs(), prepared.Inputs.RuleSourceCaptureDigest!, out var retained));
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var cold = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request,
            ruleSources: new CharacterCreationRuleSourceExecutionContext(retained!, context.Queries));
        Assert.IsNotNull(cold.Value);
        Assert.AreEqual(JsonSerializer.Serialize(reviewed.CompleteAssessment),
            JsonSerializer.Serialize(cold.Value!.CompleteAssessment));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SourceScopes696_Unknown_root_content_or_operation_remains_unconsumed(bool operation)
    {
        using var context = Ready696.Create(includeGearReview: true, includeNonEmptyPurchases: true,
            qualityName: "Ambidextrous", prepareSource: root =>
            {
                string path = Path.Combine(root, "data", "complexforms.xml");
                var document = XDocument.Load(path);
                var unsupported = new XElement("unexecuted696", "0");
                if (operation) unsupported.SetAttributeValue("amendoperation", "UNKNOWN696");
                document.Root!.Add(unsupported);
                document.Save(path);
            });
        var prepared = PrepareSources696(context);
        var reviewed = ReviewSources696(prepared, SourceRequest696(prepared.Inputs));
        Assert.AreEqual(true, reviewed.CompleteAssessment!.MechanicalSucceeded,
            "An unrelated no-op-looking source node must not be hidden by unchanged mechanical numbers.");
        AssertSource696(prepared.Inputs, reviewed.CompleteAssessment, "complexforms", Coverage696.Unresolved);
        var errata = reviewed.CompleteAssessment.Obligations.Single(row =>
            row.Kind == CharacterCreationCompleteObligationKind.ActiveGraph
            && row.Operation == CharacterCreationConsumerPolicyOperation.Errata);
        Assert.AreEqual(Coverage696.Unresolved, errata.Coverage);
        Assert.IsNull(errata.EvaluationDigest);
    }

    [TestMethod]
    public void SourceScopes696_Unknown_active_tradition_payload_is_rejected_by_the_real_compiler()
    {
        using var context = Ready696.Create(includeGearReview: true,
            talentValue: CharacterCreationMagicResonanceKinds.Magician, prepareSource: _ => { });
        var prepared = PrepareSources696(context);
        var request = SourceRequest696(prepared.Inputs);
        // Change live source after legitimate confirmations. Fresh input
        // admission must reject drift or the real compiler must refuse it;
        // no existing receipt, budget or private archive is rewritten.
        string path = Path.Combine(context.Directory, "source", "data", "traditions.xml");
        var document = XDocument.Load(path);
        var selected = document.Root!.Element("traditions")!.Elements("tradition")
            .Single(row => row.Element("id")!.Value == request.Selections.MagicSelections.Tradition!.SourceId);
        selected.Add(new XElement("unknown-effect696", "0"));
        document.Save(path);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var fresh = reader.LoadCompletionInputsWithPurchaseSourceExecution(owners.Capture(), new(context.WorkspaceId));
        if (fresh.Value is { } changed)
        {
            var review = CharacterCreationCompletionReviewService.Review(changed.Inputs,
                SourceRequest696(changed.Inputs), ruleSources: changed.Execution);
            Assert.IsTrue(review.Value?.CompleteAssessment is null
                || FindSource696(changed.Inputs, review.Value.CompleteAssessment, "traditions").Coverage != Coverage696.Evaluated);
        }
        else Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, fresh.Outcome);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void SourceScopes696_Missing_private_execution_and_wrong_capture_cannot_borrow_source_coverage()
    {
        using var context = Ready696.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareSources696(context);
        var request = SourceRequest696(prepared.Inputs);
        string before = CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!);
        var missing = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request);
        Assert.IsTrue(missing.Value?.CompleteAssessment is null
            || FindSource696(prepared.Inputs, missing.Value.CompleteAssessment, "powers").Coverage != Coverage696.Evaluated);
        var wrong = CharacterCreationCompletionReviewService.Review(prepared.Inputs,
            request with { RuleSourceCaptureDigest = CharacterCreationFinalizationDigest.ComputeUtf8("wrong-source696") },
            ruleSources: prepared.Execution);
        Assert.IsTrue(wrong.Value?.CompleteAssessment is null
            || FindSource696(prepared.Inputs, wrong.Value.CompleteAssessment, "powers").Coverage != Coverage696.Evaluated);
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void SourceScopes696_V1_keeps_source_scopes_unresolved_and_stable_after_V2_execution()
    {
        using var context = Ready696.Create(includeGearReview: true, prepareSource: _ => { });
        var historical = PrepareSources696(context, legacy: true);
        var legacyRequest = SourceRequest696(historical.Inputs);
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1, legacyRequest.RuleSourceExecutionSchema);
        var before = ReviewSources696(historical, legacyRequest);
        foreach (string scope in new[] { "traditions", "streams", "powers", "spells", "complexforms" })
            AssertSource696(historical.Inputs, before.CompleteAssessment!, scope, Coverage696.Unresolved);
        var current = PrepareSources696(context);
        var positive = ReviewSources696(current, SourceRequest696(current.Inputs));
        AssertSource696(current.Inputs, positive.CompleteAssessment!, "complexforms", Coverage696.Evaluated);
        Assert.AreEqual(JsonSerializer.Serialize(before),
            JsonSerializer.Serialize(ReviewSources696(historical, legacyRequest)));
    }

    private static CharacterCreationRuleSourcePreparation PrepareSources696(Ready696 context, bool legacy = false)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var loaded = legacy
            ? reader.LoadCompletionInputsWithRuleSourceExecution(owners.Capture(), new(context.WorkspaceId), includeLoadout: true)
            : reader.LoadCompletionInputsWithPurchaseSourceExecution(owners.Capture(), new(context.WorkspaceId), includeLoadout: true);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        Assert.AreEqual(0, owners.ActiveLeases);
        return loaded.Value!;
    }

    private static CharacterCreationCompletionReviewRequest SourceRequest696(CharacterCreationCompletionReviewInputs inputs)
        => Request(inputs, CharacterCreationPolicyRequirement.Complete) with
        {
            CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4,
            OriginalCharacterExecutionSchema = CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1,
            RuleSourceExecutionSchema = inputs.RuleSourceExecutionSchema,
            RuleSourceCaptureDigest = inputs.RuleSourceCaptureDigest,
            AdditionalConsumerExecutionSchema = CharacterCreationCompletionReviewSchemas.AdditionalConsumerExecutionV1,
            MaximumAttributesDefaultExecutionSchema = CharacterCreationCompletionReviewSchemas.MaximumAttributesDefaultExecutionV1
        };

    private static CharacterCreationCompletionReview ReviewSources696(CharacterCreationRuleSourcePreparation prepared,
        CharacterCreationCompletionReviewRequest request)
    {
        var reviewed = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request, ruleSources: prepared.Execution);
        Assert.IsNotNull(reviewed.Value, string.Join(",", reviewed.Blockers));
        Assert.IsNotNull(reviewed.Value!.CompleteAssessment);
        Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, reviewed.Value.Request.PolicyRequirement);
        return reviewed.Value;
    }

    private static CharacterCreationCompleteObligation FindSource696(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompleteAssessment assessment, string scope)
    {
        var source = inputs.EffectivePolicy!.Sources.Single(row =>
            row.Kind == CharacterCreationPolicySourceKind.Rules && row.SourceId == scope);
        return assessment.Obligations.Single(row => row.Id == $"source:{source.Kind}:{source.Order}");
    }

    private static void AssertSource696(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompleteAssessment assessment, string scope, Coverage696 expected)
    {
        var row = FindSource696(inputs, assessment, scope);
        Assert.AreEqual(expected, row.Coverage, scope + ": " + row.Reason);
        if (expected == Coverage696.Evaluated)
            Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(row.EvaluationDigest), scope);
        else Assert.IsNull(row.EvaluationDigest, scope);
    }
}
