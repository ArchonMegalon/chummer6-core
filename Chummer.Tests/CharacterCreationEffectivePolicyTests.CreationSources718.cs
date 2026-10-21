using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready718 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using D718 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, false)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, false)]
    [DataRow(CharacterCreationBuildMethods.Priority, true)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, true)]
    public void CreationSources718_Canonical_selection_uses_default_and_changed_numeric_source(string method, bool changed)
    {
        using var context = Ready718.Create(includeGearReview: true, buildMethod: method,
            amendSettings: settings =>
            {
                if (changed)
                {
                    settings.SetElementValue("buildpoints", 37);
                    settings.SetElementValue("priorityarray", string.Empty);
                }
            }, prepareSource: root =>
            {
                if (!changed) return;
                string path = Path.Combine(root, "data", "priorities.xml");
                var document = XDocument.Load(path);
                AttributePriority718(document).SetElementValue("attributes", 22);
                document.Save(path);
            });
        var prepared = PrepareSources696(context);
        var request = SourceRequest696(prepared.Inputs);
        string before = CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!);
        var observed = Observe718(context, prepared, request);
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, observed.Review.Request.PolicyRequirement);
        var invocation = observed.Capture.Invocations.Last(i => i.CreationDraft718 is not null);
        Assert.AreEqual(changed ? 22 : 20, invocation.CreationDraft718!.EffectiveNormalAttributePoints);
        Assert.AreEqual(9, invocation.CreationDraft718.TotalSpecialAttributePoints);
        Assert.AreEqual(changed ? 37 : 25, invocation.CreationDraft718.CreationKarmaTotal);
        Assert.AreEqual(method, invocation.CreationDraft718.BuildMethod);
        Assert.AreEqual(prepared.Inputs.EffectivePolicy!.PolicyDigest, invocation.PolicyDigest);
        Assert.AreEqual(prepared.Inputs.RuleSourceCaptureDigest, invocation.CaptureDigest);
        Assert.AreEqual("{}", JsonSerializer.Serialize(invocation));
        var priorities = observed.Capture.Reports.Single(r => r.Scope == "priorities");
        Assert.IsTrue(priorities.IsComplete, string.Join(",", priorities.Operations
            .Where(o => o.Disposition == D718.Unconsumed).Select(o => o.Path)));
        AssertSource696(prepared.Inputs, observed.Review.CompleteAssessment, "priorities",
            CharacterCreationCompleteCoverage.Evaluated);
        Assert.IsTrue(priorities.Operations.Any(o => o.Path.Contains("/attributes[", StringComparison.Ordinal)
            && o.ConsumedValue718 == (changed ? "22" : "20") && o.Disposition == D718.Compiled
            && o.OperationResult718!.Contains("ResolveAttributePointPools", StringComparison.Ordinal)));
        Assert.IsTrue(priorities.Operations.Any(o => o.Path.Contains("/resources[", StringComparison.Ordinal)
            && o.ConsumedValue718 == "50000" && o.OperationResult718!.Contains("PriceSelectedCompletion", StringComparison.Ordinal)));
        var profile = observed.Capture.Reports.Single(r => r.Scope == "profile");
        Assert.IsTrue(profile.Operations.Any(o => o.Path.Contains("/buildpoints[", StringComparison.Ordinal)
            && o.ConsumedValue718 == (changed ? "37" : "25") && o.Disposition == D718.Compiled));
        // Partial consumers retain the rest of the selected source verbatim as
        // unresolved obligations. This is not a replacement strict Finish gate.
        Assert.IsTrue(profile.Operations.Any(o => o.Disposition == D718.Unconsumed));
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    [DataRow(false, 20)]
    [DataRow(true, 6)]
    public void CreationSources718_Profile_house_rules_reach_paid_attribute_cost(bool alternate, int expectedCost)
    {
        using var context = Ready718.Create(includeGearReview: true, amendSettings: settings =>
        {
            settings.Element("karmacost")!.SetElementValue("karmaattribute", alternate ? 3 : 5);
            settings.SetElementValue("alternatemetatypeattributekarma", alternate);
            settings.SetElementValue("reverseattributepriorityorder", alternate);
        });
        var prepared = PrepareSources696(context);
        var request = SourceRequest696(prepared.Inputs);
        request = request with { Selections = request.Selections with
            { AttributeAllocations = request.Selections.AttributeAllocations.Where(row => row.AttributeId != "EDG")
            .Append(new CharacterCreationAttributeAllocation("EDG", 1, 1)).ToArray() } };
        string before = System.Text.Json.JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var locked = Observe718(context, prepared, request).Review;
        Assert.IsTrue(locked.Blockers.Contains(CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict),
            Diagnostic772(locked));
        Assert.AreEqual(false, locked.CompleteAssessment!.MechanicalSucceeded, Diagnostic772(locked));
        AssertNoMechanics695(locked.CompleteAssessment);
        request = request with { Selections = request.Selections with
        { ChangeableAttributeIds = request.Selections.ChangeableAttributeIds.Append("EDG").Distinct(StringComparer.Ordinal).ToArray() } };
        var observed = Observe718(context, prepared, request);
        AssertCompleteMechanics772(observed.Review);
        Assert.AreEqual(before, System.Text.Json.JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        var edge = observed.Review.Skills!.Attributes.Attributes.Single(a => a.AttributeId == "EDG");
        Assert.AreEqual(4, edge.Current);
        Assert.AreEqual(expectedCost, edge.KarmaCost);
        var profile = observed.Capture.Reports.Single(r => r.Scope == "profile");
        foreach (string field in new[] { "karmaattribute", "alternatemetatypeattributekarma", "reverseattributepriorityorder" })
            Assert.IsTrue(profile.Operations.Any(o => o.Path.Contains("/" + field + "[", StringComparison.Ordinal)
                && o.Disposition == D718.Compiled && o.OperationResult718!.Contains("TryCalculateAllocation", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CreationSources718_Empty_purchases_execute_Street_terms_and_real_explicit_cash(bool changed)
    {
        using var context = Ready718.Create(includeGearReview: true, prepareSource: root =>
        {
            if (!changed) return;
            string path = Path.Combine(root, "data", "lifestyles.xml");
            var document = XDocument.Load(path);
            var street = document.Root!.Element("lifestyles")!.Elements("lifestyle")
                .Single(n => n.Element("name")!.Value == "Street");
            street.SetElementValue("dice", 2);
            street.SetElementValue("multiplier", 35);
            document.Save(path);
        });
        var prepared = PrepareSources696(context);
        var observed = Observe718(context, prepared, SourceRequest696(prepared.Inputs));
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var invocation = observed.Capture.Invocations.Last(i => i.Lifestyles718 is not null);
        Assert.AreEqual(0, invocation.Lifestyles718!.Count);
        var report = observed.Capture.Reports.Single(r => r.Scope == "lifestyles");
        Assert.IsTrue(report.Operations.Any(o => o.Path.Contains("/dice[", StringComparison.Ordinal)
            && o.Disposition == D718.Compiled && o.ConsumedValue718 == (changed ? "2" : "1")));
        Assert.IsTrue(report.Operations.Any(o => o.Path.Contains("/multiplier[", StringComparison.Ordinal)
            && o.Disposition == D718.Compiled && o.ConsumedValue718 == (changed ? "35" : "20")));
        Assert.IsTrue(report.Operations.Any(o => o.OperationResult718?.Contains("\"CashChoiceExecuted\":false", StringComparison.Ordinal) == true));
        // Freegrid/supporting-source operations are still present. Empty
        // purchases are never allowed to make the source look inactive.
        Assert.IsFalse(report.IsComplete);
        AssertSource696(prepared.Inputs, observed.Review.CompleteAssessment, "lifestyles",
            CharacterCreationCompleteCoverage.Unresolved);

        var current = context.Resolver.TryCreateContext(context.Store.Get(context.WorkspaceId).Value!.Document.Content)!;
        Assert.IsTrue(current.TryResolveCreationDefaultStartingNuyen(out var source));
        var authority = invocation.LifestyleAuthority718!;
        // This is an explicit test choice submitted to the actual Finish cash
        // consumer. It is not a choice fabricated by the source accounting.
        int roll = changed ? 7 : 4;
        Assert.IsTrue(CharacterCreationFinalizationStartingCashRules.TryPrepare(source, authority,
            new CharacterCreationStartingCashChoice(source!.AuthorityDigest, roll), out var cash));
        Assert.IsTrue(CharacterCreationFinalizationStartingCashRules.TryProject(context.WorkspaceId, cash,
            out decimal amount, out var saved));
        Assert.AreEqual(changed ? 245m : 80m, amount);
        Assert.AreEqual("0", saved!.Element("cost")!.Value);
        Assert.AreEqual(changed ? "2" : "1", saved.Element("dice")!.Value);
        Assert.AreEqual(changed ? "35" : "20", saved.Element("multiplier")!.Value);
        Assert.IsFalse(CharacterCreationFinalizationStartingCashRules.TryPrepare(source, authority,
            new CharacterCreationStartingCashChoice(source.AuthorityDigest, source.Dice * 6 + 1), out _));
    }

    [TestMethod]
    [DataRow("profile", false)]
    [DataRow("priorities", false)]
    [DataRow("priorities", true)]
    public void CreationSources718_Unknown_influential_leaf_remains_in_ledger(string scope, bool amendment)
    {
        using var context = Ready718.Create(includeGearReview: true,
            amendSettings: settings =>
            {
                if (scope == "profile") settings.Add(new XElement("unknown718", 0));
            }, prepareSource: root =>
            {
                if (scope != "priorities") return;
                string path = Path.Combine(root, "data", "priorities.xml");
                var doc = XDocument.Load(path);
                var unknown = new XElement("unknown718", 0);
                if (amendment) unknown.SetAttributeValue("amendoperation", "unsupported718");
                AttributePriority718(doc).Add(unknown);
                doc.Save(path);
            });
        var prepared = PrepareSources696(context);
        var observed = Observe718(context, prepared, SourceRequest696(prepared.Inputs));
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var report = observed.Capture.Reports.Single(r => r.Scope == scope);
        Assert.IsTrue(report.Operations.Any(o => o.Path.Contains("/unknown718[", StringComparison.Ordinal)
            && o.Disposition == D718.Unconsumed && o.ExecutionDigest is null));
        AssertSource696(prepared.Inputs, observed.Review.CompleteAssessment, scope, CharacterCreationCompleteCoverage.Unresolved);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CreationSources718_Live_source_change_or_malformed_root_cannot_reuse_execution(bool malformed)
    {
        using var context = Ready718.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareSources696(context);
        string path = Path.Combine(context.Directory, "source", "data", "priorities.xml");
        var document = XDocument.Load(path);
        if (malformed) document.Root!.Name = "wrong-root718";
        else AttributePriority718(document).SetElementValue("attributes", 23);
        document.Save(path);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var fresh = reader.LoadCompletionInputsWithPurchaseSourceExecution(owners.Capture(), new(context.WorkspaceId), includeLoadout: true);
        if (fresh.Value is { } value)
        {
            var review = CharacterCreationCompletionReviewService.Review(value.Inputs, SourceRequest696(value.Inputs), ruleSources: value.Execution);
            Assert.IsTrue(review.Value?.CompleteAssessment is null
                || FindSource696(value.Inputs, review.Value.CompleteAssessment, "priorities").Coverage != CharacterCreationCompleteCoverage.Evaluated);
        }
        else Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, fresh.Outcome);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void CreationSources718_Retained_cold_source_runs_same_consumers_without_live_files()
    {
        using var context = Ready718.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareSources696(context);
        var request = SourceRequest696(prepared.Inputs);
        var before = Observe718(context, prepared, request);
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var after = Observe718(context, prepared, request);
        Assert.AreEqual(JsonSerializer.Serialize(before.Review.CompleteAssessment),
            JsonSerializer.Serialize(after.Review.CompleteAssessment));
        Assert.AreEqual(JsonSerializer.Serialize(before.Capture.Reports), JsonSerializer.Serialize(after.Capture.Reports));
        Assert.AreEqual(string.Join("\n", before.Capture.Reports.SelectMany(r => r.Operations).Select(o => o.OperationResult718)),
            string.Join("\n", after.Capture.Reports.SelectMany(r => r.Operations).Select(o => o.OperationResult718)));
    }

    private static XElement AttributePriority718(XDocument document) => document.Root!.Element("priorities")!
        .Elements("priority").Single(n => n.Element("category")?.Value == "Attributes"
            && n.Element("value")?.Value == "B" && n.Element("prioritytable") is null);

    private static (CharacterCreationCompletionReview Review, ObservedCapture718 Capture) Observe718(
        Ready718 context, CharacterCreationRuleSourcePreparation prepared, CharacterCreationCompletionReviewRequest request)
    {
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            prepared.Execution.ExportPrivateInputs(), prepared.Inputs.RuleSourceCaptureDigest!, out var retained));
        var capture = new ObservedCapture718(retained!);
        var review = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request,
            ruleSources: new CharacterCreationRuleSourceExecutionContext(capture, context.Queries));
        Assert.IsNotNull(review.Value, string.Join(",", review.Blockers));
        return (review.Value!, capture);
    }

    // Test-only observation of the real retained resolution. No method changes
    // a source, request, arithmetic result, assessment or history byte.
    private sealed class ObservedCapture718(CharacterCreationRuleSourceCapture inner) : CharacterCreationRuleSourceCapture
    {
        internal List<CharacterCreationRuleSourceScopeResult> Reports { get; } = [];
        internal List<CharacterCreationRuleSourceScopeInvocation> Invocations { get; } = [];
        public override string IntegrityDigest => inner.IntegrityDigest;
        public override string EffectivePolicyDigest => inner.EffectivePolicyDigest;
        public override byte[] ExportPrivateInputs() => inner.ExportPrivateInputs();
        public override ICharacterSourceDataResolver CreateReplayResolver() => inner.CreateReplayResolver();
        public override bool MatchesOriginalCharacter(string xml) => inner.MatchesOriginalCharacter(xml);
        public override CharacterCreationRuleSourceResolution? CreateExecutionResolution()
            => inner.CreateExecutionResolution() is { } resolution ? new ObservedResolution718(resolution, this) : null;
    }

    private sealed class ObservedResolution718(CharacterCreationRuleSourceResolution inner, ObservedCapture718 observer)
        : CharacterCreationRuleSourceResolution
    {
        public override ICharacterSourceDataResolver Resolver => inner.Resolver;
        public override bool EnablePurchaseSourceExecution() => inner.EnablePurchaseSourceExecution();
        public override bool EnableCompleteSkillCapSourceExecution() => inner.EnableCompleteSkillCapSourceExecution();
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
}
