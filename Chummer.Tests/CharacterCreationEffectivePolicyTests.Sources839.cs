using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready839 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    // Deliberately scoped graph tests. Complete-only ReviewSources696 is unchanged.
    private static CharacterCreationCompletionReview ReviewScopedSources839(
        CharacterCreationRuleSourcePreparation prepared, CharacterCreationCompletionReviewRequest request)
    {
        Assert.AreEqual(CharacterCreationPolicyRequirement.None, request.PolicyRequirement);
        var result = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request, ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value);
        Assert.AreEqual(CharacterCreationPolicyRequirement.None, result.Value!.Request.PolicyRequirement);
        Assert.AreEqual(0, result.Blockers.Count);
        return result.Value;
    }

    private static (CharacterCreationCompletionReview Review, ObservedCapture839 Capture) ObserveSources839(
        Ready839 context, CharacterCreationRuleSourcePreparation prepared, CharacterCreationCompletionReviewRequest request)
    {
        var capture = RestoreObserved839(prepared);
        var result = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request,
            ruleSources: new CharacterCreationRuleSourceExecutionContext(capture, context.Queries));
        Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
        Assert.AreEqual(request.PolicyRequirement, result.Value!.Request.PolicyRequirement);
        return (result.Value, capture);
    }

    private static ObservedCapture839 RestoreObserved839(CharacterCreationRuleSourcePreparation prepared)
    {
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            prepared.Execution.ExportPrivateInputs(), prepared.Inputs.RuleSourceCaptureDigest!, out var retained));
        return new(retained!);
    }

    // Observe the real resolver; never invent, suppress or change a source result.
    private sealed class ObservedCapture839(CharacterCreationRuleSourceCapture inner) : CharacterCreationRuleSourceCapture
    {
        internal List<CharacterCreationRuleSourceScopeResult> Reports { get; } = [];
        internal List<CharacterCreationRuleSourceScopeInvocation> Invocations { get; } = [];
        public override string IntegrityDigest => inner.IntegrityDigest;
        public override string EffectivePolicyDigest => inner.EffectivePolicyDigest;
        public override CharacterCreationSourcebookSelectionAuthority? SourcebookSelection => inner.SourcebookSelection;
        public override byte[] ExportPrivateInputs() => inner.ExportPrivateInputs();
        public override ICharacterSourceDataResolver CreateReplayResolver() => inner.CreateReplayResolver();
        public override bool MatchesOriginalCharacter(string xml) => inner.MatchesOriginalCharacter(xml);
        public override CharacterCreationRuleSourceResolution? CreateExecutionResolution(string? schema)
            => inner.CreateExecutionResolution(schema) is { } resolution ? new ObservedResolution839(resolution, this) : null;
        public override CharacterCreationRuleSourceResolution? CreateExecutionResolution()
            => inner.CreateExecutionResolution() is { } resolution ? new ObservedResolution839(resolution, this) : null;
    }

    private sealed class ObservedResolution839(CharacterCreationRuleSourceResolution inner, ObservedCapture839 observer)
        : CharacterCreationRuleSourceResolution
    {
        public override ICharacterSourceDataResolver Resolver => inner.Resolver;
        public override bool EnablePurchaseSourceExecution() => inner.EnablePurchaseSourceExecution();
        public override bool EnableCompleteSkillCapSourceExecution() => inner.EnableCompleteSkillCapSourceExecution();
        public override bool EnableCandidateSourcebookSelection() => inner.EnableCandidateSourcebookSelection();
        public override string? ExecuteAmendmentAbsence954() => inner.ExecuteAmendmentAbsence954();
        public override bool HasExecutedSourceScope955(CharacterCreationRuleSourceScopeInvocation invocation,
            CharacterCreationRuleSourceScopeResult report) => inner.HasExecutedSourceScope955(invocation, report);
        public override CharacterCreationSourcebookSelectionProjection.Result? ConsumeSourcebookSelection(
            CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request)
            => inner.ConsumeSourcebookSelection(inputs, request);
        public override IReadOnlyDictionary<string, string> ConsumeSkillLimits(IReadOnlyDictionary<string, int> consumed)
            => inner.ConsumeSkillLimits(consumed);
        public override IReadOnlyDictionary<string, string> ConsumeGearAndQualitySources(
            CharacterCreationGearAuthority gear, CharacterCreationQualitiesAuthority qualities,
            IReadOnlyList<CharacterCreationGearLine> lines, CharacterCreationGearBudget budget,
            CharacterCreationQualitiesCompletionEvaluation evaluatedQualities, IReadOnlyList<CharacterCreationTalentGearSource> grantedGear)
            => inner.ConsumeGearAndQualitySources(gear, qualities, lines, budget, evaluatedQualities, grantedGear);
        public override IReadOnlyDictionary<string, string> ConsumeSkillsSource(CharacterCreationSkillsAuthority skills,
            CharacterCreationCompletionSelections selections, CharacterCreationAttributeSkillsPreview result)
            => inner.ConsumeSkillsSource(skills, selections, result);
        public override IReadOnlyList<CharacterCreationRuleSourceScopeResult> ExecuteRemainingSourceScopes(
            CharacterCreationRuleSourceScopeInvocation invocation)
        {
            observer.Invocations.Add(invocation);
            var result = inner.ExecuteRemainingSourceScopes(invocation);
            observer.Reports.AddRange(result);
            return result;
        }
    }

    // This ID is captured while the injected XML-to-operation mapping still exists.
    // Later assertions use the exact scope/order/path/content identity, not aggregate gaps.
    private sealed record SourceOperationId839(string ObligationId, int Order, string Path, string SourceDigest);

    private static string SourcePath839(XElement element)
        => element.Parent is null ? "/chummer[0]"
            : SourcePath839(element.Parent) + "/" + element.Name + "["
                + element.Parent.Nodes().TakeWhile(node => !ReferenceEquals(node, element)).Count() + "]";

    private static SourceOperationId839 IdentifyOperation839(CharacterCreationRuleSourceScopeResult report,
        string path, string sourceDigest)
    {
        var matches = report.Operations.Where(row => row.Path == path && row.SourceDigest == sourceDigest).ToArray();
        string leaf = path[(path.LastIndexOf('/') + 1)..];
        Assert.AreEqual(1, matches.Length, $"Expected exact source operation {path} ({sourceDigest}); candidates: "
            + string.Join(", ", report.Operations.Where(row => row.SourceDigest == sourceDigest
                || row.Path.EndsWith("/" + leaf, StringComparison.Ordinal)).Take(8)
                .Select(row => $"{row.Path} ({row.SourceDigest})")));
        var operation = matches[0];
        return new(report.ObligationId, operation.Order, operation.Path, operation.SourceDigest);
    }

    private static void AssertUnknown839(ObservedCapture839 capture, SourceOperationId839 id)
    {
        var report = capture.Reports.Single(row => row.ObligationId == id.ObligationId);
        var operation = report.Operations.Single(row => row.Order == id.Order);
        Assert.AreEqual(id.Path, operation.Path);
        Assert.AreEqual(id.SourceDigest, operation.SourceDigest);
        Assert.AreEqual(CharacterCreationSourceOperationDisposition.Unconsumed, operation.Disposition);
        Assert.IsNull(operation.ExecutionDigest);
        Assert.IsNull(operation.ConsumedValue718);
        Assert.IsNull(operation.OperationResult718);
        Assert.IsFalse(report.IsComplete);
        foreach (string field in new[] { "walk", "run", "sprint" })
            Assert.IsTrue(report.Operations.Any(row => HasField832(row, field)
                && row.Disposition == CharacterCreationSourceOperationDisposition.Compiled
                && CharacterCreationFinalizationDigest.IsCanonical(row.ExecutionDigest)));
        Assert.IsTrue(capture.Invocations.Any(row => row.SourceExecution832 && row.CompiledMovement832 is not null));
    }

    // Direct selected consumer/assessment evaluation with an actual Complete request.
    // The legitimate None witness supplies selected arithmetic only. It is never
    // relabeled, resealed, persisted or claimed as a successful Complete replay.
    private static (CharacterCreationCompleteAssessment Assessment, ObservedCapture839 Capture) AssessSelectedSources839(
        Ready839 context, CharacterCreationRuleSourcePreparation prepared, CharacterCreationCompletionReviewRequest complete,
        Chummer.Application.Workspaces.WorkspaceStoredDocument original,
        CharacterCreationCompletionBootstrapSnapshot bootstrap, CharacterCreationSelectedCompletionWitness witness)
    {
        Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, complete.PolicyRequirement);
        Assert.AreEqual(CharacterCreationPolicyRequirement.None, witness.PolicyRequirement);
        var scoped = complete with { PolicyRequirement = CharacterCreationPolicyRequirement.None };
        var capture = RestoreObserved839(prepared);
        var sources = new CharacterCreationRuleSourceExecutionContext(capture, context.Queries);
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, witness, scoped, ruleSources: sources);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.IsNotNull(replay.Value);
        Assert.AreEqual(0, capture.Reports.Count, "None replay does not execute Complete source scopes.");
        Assert.IsTrue(CharacterCreationSelectedCompletionEvaluator.TryReadOriginal(original, bootstrap, witness,
            out var baseline, out var effects));
        // Reconstruct original custody from the selected route's admitted history.
        Assert.IsTrue(CharacterCreationOriginalCharacterSource.TryFromSelected(original, bootstrap, witness,
            scoped, out var originalSource));
        var authority = CharacterCreationCompleteAssessmentService.FromSelectedAuthority(witness, complete, baseline!, effects!);
        var lifestyleExecution = ObserveSelectedLifestyle923(witness, complete, replay.Value!, authority);
        var execution = sources.Resolve(original, complete, default);
        Assert.IsNotNull(execution);
        var consumers = CharacterCreationCompleteConsumerExecution.FromSelected(witness, complete, replay.Value!,
            baseline!, effects!, authority, lifestyles: lifestyleExecution,
            originalLoadoutInstances: original.Document.AuxiliaryState.CharacterCreationLoadoutDraft?.Instances,
            originalCharacter: originalSource, ruleSourceExecution: execution);
        Assert.IsNotNull(consumers);
        var consumption = CharacterCreationPolicyConsumptionProjection.FromSelected(witness, complete, replay.Value!, baseline!, effects!);
        var assessment = CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, consumption, true, consumers: consumers);
        Assert.IsFalse(assessment.SatisfiesComplete);
        return (assessment, capture);
    }

    // None replay intentionally has no Complete execution callback. Re-execute
    // the canonical selected lifestyle consumer under this Complete authority,
    // using only selected inputs/results, as production Replay does. Neither an
    // empty basket nor full-path evidence may stand in for this execution.
    private static CharacterCreationLifestylesService.CompleteGraphExecution? ObserveSelectedLifestyle923(
        CharacterCreationSelectedCompletionWitness witness, CharacterCreationCompletionReviewRequest request,
        CharacterCreationSelectedCompletionEvaluation result, CharacterCreationCompleteAssessment authority)
    {
        if (authority.Schema is not (CharacterCreationCompleteAssessment.SchemaV3 or CharacterCreationCompleteAssessment.SchemaV4))
            return null;
        var choices = request.Selections;
        IReadOnlyList<string> anchors = result.Loadout is null ? result.GearSourceAnchorIds : result.GearSourceAnchorIds
            .Concat(result.Loadout.Lines.SelectMany(row => row.SourceAnchorIds))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        decimal afterLoadout = checked(result.GearBudget.RemainingNuyen - (result.Loadout?.PurchaseCost ?? 0m));
        Assert.IsTrue(afterLoadout >= 0m, "Selected lifestyle funding must have succeeded.");
        CharacterCreationLifestylesService.CompleteGraphExecution? execution = null;
        var priced = CharacterCreationLifestylesService.CompleteGraphExecution.Price(new(witness.Lifestyles,
            witness.Skills.PrerequisiteDraft, witness.Magic.Talent.GrantedQualitySources ?? [], result.Qualities.Selections,
            witness.ProposedQualityProjectionProvenanceSeed, choices.Lifestyles, choices.StartingLifestyleId,
            afterLoadout, anchors, []), default, observed => execution = observed, authority);
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Available, priced.Outcome, string.Join(",", priced.Blockers));
        Assert.IsNotNull(priced.Value);
        Assert.AreEqual(0, priced.Blockers.Count);
        Assert.IsNotNull(execution, "Complete source observation requires a real selected lifestyle execution.");
        Assert.IsTrue(execution!.MatchesSelected(witness, request, result, authority),
            "Selected lifestyle execution must match its own replay, request and authority.");
        return execution;
    }

    // Independent of V4 preparation and initiative arithmetic: these readers
    // exist on the pre-initiative graph. Run this matrix there as well.
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void SourceReports923_historical_routes_retain_all_consumer_reports(bool movement, bool purchases)
    {
        using var context = Ready839.Create(includeGearReview: true, includeNonEmptyPurchases: purchases,
            qualityName: "Ambidextrous", prepareSource: _ => { });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var loaded = movement
            ? reader.LoadCompletionInputsWithMovementSourceExecution(owners.Capture(), new(context.WorkspaceId), includeLoadout: true)
            : reader.LoadCompletionInputsWithPurchaseSourceExecution(owners.Capture(), new(context.WorkspaceId), includeLoadout: true);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        Assert.IsNotNull(loaded.Value);
        Assert.AreEqual(0, owners.ActiveLeases);
        var prepared = loaded.Value!;
        Assert.AreEqual(movement ? CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3
            : CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2, prepared.Inputs.RuleSourceExecutionSchema);
        var complete = SourceRequest696(prepared.Inputs);
        if (purchases)
        {
            var low = prepared.Inputs.Lifestyles.Authority.LifestyleOptions.Single(option => option.Name == "Low");
            Guid id = Guid.Parse("90c9cd36-01fb-42a0-9ec0-95a1a01a5923");
            var home = new CharacterCreationLifestyleConfiguration(id, low.OptionId, "Report parity home",
                CharacterCreationLifestyleStyleIds.Standard, low.DefaultIncrementId, 1, 100m, 0,
                false, false, 0, 0, 0, 0, "Seattle", "Redmond", "", []);
            complete = complete with { Selections = complete.Selections with
                { Lifestyles = [home], StartingLifestyleId = id } };
        }
        Assert.AreEqual(purchases ? 1 : 0, complete.Selections.Lifestyles.Count);
        var full = ObserveSources839(context, prepared, complete);
        Assert.AreEqual(true, full.Review.CompleteAssessment!.MechanicalSucceeded);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot
            .Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var witness = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, prepared.Inputs,
            complete with { PolicyRequirement = CharacterCreationPolicyRequirement.None }, ruleSources: prepared.Execution);
        Assert.IsNotNull(witness.Value, string.Join(",", witness.Blockers));
        var selected = AssessSelectedSources839(context, prepared, complete, original, bootstrap, witness.Value!);
        var left = full.Capture.Invocations.Single();
        var right = selected.Capture.Invocations.Single();
        Assert.AreEqual(left.GraphDigest, right.GraphDigest);
        Assert.IsTrue(left.TryGetCompletedConsumer("lifestyles", out var fullLifestyle));
        Assert.IsTrue(right.TryGetCompletedConsumer("lifestyles", out var selectedLifestyle));
        Assert.AreEqual(fullLifestyle, selectedLifestyle);
        var source = prepared.Inputs.EffectivePolicy!.Sources.Single(row =>
            row.Kind == CharacterCreationPolicySourceKind.Rules && row.SourceId == "lifestyles");
        string obligation = $"source:{source.Kind}:{source.Order}";
        foreach (var capture in new[] { full.Capture, selected.Capture })
        {
            var report = capture.Reports.Single(row => row.ObligationId == obligation);
            Assert.AreEqual(source.SourceId, report.Scope);
            Assert.AreEqual(source.ContentDigest, report.SourceDigest);
            Assert.AreEqual(fullLifestyle, report.ConsumerDigest);
            Assert.IsTrue(report.Operations.Count > 0);
        }
        // Preserve all report fields, order and operations, including unknowns.
        // Keep failure text bounded; no source report payload belongs in output.
        Assert.IsTrue(string.Equals(JsonSerializer.Serialize(full.Capture.Reports),
            JsonSerializer.Serialize(selected.Capture.Reports), StringComparison.Ordinal),
            "Full/selected source reports differ after independently executing every admitted consumer.");
        Assert.AreEqual(JsonSerializer.Serialize(FindSource696(prepared.Inputs, full.Review.CompleteAssessment!, "lifestyles")),
            JsonSerializer.Serialize(FindSource696(prepared.Inputs, selected.Assessment, "lifestyles")));
        Assert.IsFalse(full.Review.CompleteAssessment!.SatisfiesComplete);
        Assert.IsFalse(selected.Assessment.SatisfiesComplete);
    }
}
