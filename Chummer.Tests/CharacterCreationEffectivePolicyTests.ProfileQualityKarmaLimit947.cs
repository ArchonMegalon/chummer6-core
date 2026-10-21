using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready947 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using D947 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow("", 0, false)]
    [DataRow("", 25, true)]
    [DataRow("", int.MaxValue, false)]
    [DataRow("Ambidextrous", 4, false)]
    [DataRow("Ambidextrous", 25, true)]
    [DataRow("Simsense Vertigo", 5, false)]
    [DataRow("Simsense Vertigo", 25, true)]
    [DataRow("both", 25, true)]
    public void ProfileQuality947_canonical_positive_negative_and_empty_enumeration_account_only_exact_leaf(
        string name, int limit, bool v4)
    {
        using var context = Ready947.Create(includeGearReview: true, includeNonEmptyPurchases: name.Length != 0,
            qualityName: name == "both" ? "Ambidextrous" : name, wrapResolver: v4 ? V4Sources905 : null,
            amendSettings: profile =>
            {
                profile.SetElementValue("qualitykarmalimit", limit);
                profile.Add(new XElement("unknown947", "unresolved"));
            });
        var prepared = PrepareProfileSourcebooks935(context, v4, books: true);
        var request = SourcebookRequest800(prepared.Inputs);
        if (name == "both")
        {
            var negative = prepared.Inputs.Lifestyles.Qualities.Authority.Options.Single(row => row.Name == "Simsense Vertigo" && row.IsSelectable);
            request = request with { Selections = request.Selections with
                { QualityOptionIds = request.Selections.QualityOptionIds.Append(negative.OptionId).ToArray() } };
        }
        string original = CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!);
        var observed = ObserveSources839(context, prepared, request);
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var invocation = observed.Capture.Invocations.Single();
        var identity = QualityIdentity947(prepared.Inputs);
        Assert.IsTrue(ReadQuality947(invocation, identity, out var actual, out bool noTarget, out var digest));
        Assert.AreEqual(limit, actual!.PositiveQualityBudget.Total);
        Assert.AreEqual(limit, actual.NegativeQualityBudget.Total);
        Assert.AreEqual(name.Length == 0, noTarget);
        Assert.AreEqual(name.Length == 0 ? 0 : name == "both" ? 2 : 1, actual.Selections.Count);
        Assert.AreEqual(name is "Ambidextrous" or "both" ? 4 : 0, actual.PositiveQualityBudget.Used);
        Assert.AreEqual(name is "Simsense Vertigo" or "both" ? 5 : 0, actual.NegativeQualityBudget.Used);
        Assert.AreEqual(JsonSerializer.Serialize(observed.Review.UpstreamBudget!.Qualities.Qualities), JsonSerializer.Serialize(actual));
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(digest));
        Assert.AreEqual("{}", JsonSerializer.Serialize(invocation));
        var report = observed.Capture.Reports.Single(row => row.Scope == "profile");
        AssertQualityLeaf947(report, identity.ProfileId, actual, noTarget, digest!);
var costIdentity973 = CostIdentity973(prepared.Inputs);
Assert.IsTrue(ReadCost973(invocation, costIdentity973, out var priced973, out var policy973, out bool empty973, out var costDigest973));
Assert.AreEqual(JsonSerializer.Serialize(actual), JsonSerializer.Serialize(priced973));
Assert.AreEqual(noTarget, empty973);
Assert.AreEqual(prepared.Inputs.EffectivePolicy!.Consumers!.Qualities.CostPolicy, policy973);
AssertCostLeaf973(report, costIdentity973, priced973!, policy973!, empty973, costDigest973!);
        AssertMetadataFromInputs956(report, prepared.Inputs, invocation);
        foreach (string field in new[] { "unknown947" })
            AssertQualityUnconsumed947(report, identity.ProfileId, field);
        Assert.IsFalse(report.IsComplete);
        AssertSource696(prepared.Inputs, observed.Review.CompleteAssessment, "profile", CharacterCreationCompleteCoverage.Unresolved);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved, ErrataDomain788(observed.Review.CompleteAssessment).Coverage);
        Assert.IsFalse(observed.Review.CompleteAssessment.SatisfiesComplete);
        Assert.AreEqual(original, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    [DataRow("Ambidextrous", CharacterCreationQualitiesBlockers.PositiveLimitExceeded)]
    [DataRow("Simsense Vertigo", CharacterCreationQualitiesBlockers.NegativeLimitExceeded)]
    public void ProfileQuality947_exceeded_canonical_budget_cannot_register_operation(string name, string blocker)
    {
        using var context = Ready947.Create(includeGearReview: true,
            amendSettings: profile => profile.SetElementValue("qualitykarmalimit", 1));
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var input = prepared.Inputs.Lifestyles.Qualities;
        var option = input.Authority.Options.Single(row => row.Name == name && row.IsSelectable);
        var request = SourcebookRequest800(prepared.Inputs);
        request = request with { Selections = request.Selections with { QualityOptionIds = new[] { option.OptionId } } };
        var canonical = CharacterCreationQualitiesService.PreviewWithProposedAttributes(input,
            request.Selections.AttributeAllocations, request.Selections.QualityOptionIds);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, canonical.Outcome);
        Assert.IsTrue(canonical.Blockers.Contains(blocker));
        var capture = RestoreObserved839(prepared);
        var review = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request,
            ruleSources: new CharacterCreationRuleSourceExecutionContext(capture, context.Queries));
        Assert.IsTrue(review.Value?.CompleteAssessment?.MechanicalSucceeded != true);
        foreach (var invocation in capture.Invocations)
            Assert.IsFalse(ReadQuality947(invocation, QualityIdentity947(prepared.Inputs), out _, out _, out _));
    }

    [TestMethod]
    [DataRow("comment")]
    [DataRow("cdata")]
    [DataRow("mixed-cdata")]
    public void ProfileQuality947_completed_evaluation_refuses_nonplain_leaf(string nodeKind)
    {
        using var context = Ready947.Create(includeGearReview: true, includeNonEmptyPurchases: true, qualityName: "Ambidextrous",
            amendSettings: profile =>
            {
                var leaf = profile.Element("qualitykarmalimit")!;
                if (nodeKind == "cdata") leaf.ReplaceNodes(new XCData(leaf.Value));
                else if (nodeKind == "mixed-cdata") leaf.ReplaceNodes(new XText("2"), new XCData("5"));
                else leaf.Add(new XComment("nonplain947"));
            });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var observed = ObserveSources839(context, prepared, SourcebookRequest800(prepared.Inputs));
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        Assert.IsFalse(ReadQuality947(observed.Capture.Invocations.Single(), QualityIdentity947(prepared.Inputs), out _, out _, out _));
        AssertQualityUnconsumed947(observed.Capture.Reports.Single(row => row.Scope == "profile"),
            prepared.Inputs.EffectivePolicy!.SettingsProfileId, "qualitykarmalimit");
    }

    [TestMethod]
    public void ProfileQuality963_processing_instruction_is_refused_before_profile_execution()
    {
        using var context = Ready947.Create(includeGearReview: true);
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var source = prepared.Inputs.Magic.Authority.MysticAdeptPowerPointPolicy!;
        Assert.IsTrue(CharacterCreationMysticAdeptPowerPointRules.TryCreatePolicy(source.SettingsProfileId,
            source.SettingsInputsDigest, source.CanonicalSourceXml, out _));
        var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        profile.Element("qualitykarmalimit")!.Add(new XProcessingInstruction("quality953", "nonplain"));
        Assert.IsFalse(CharacterCreationMysticAdeptPowerPointRules.TryCreatePolicy(source.SettingsProfileId,
            source.SettingsInputsDigest, profile.ToString(SaveOptions.DisableFormatting), out _),
            "The canonical source validator rejects processing instructions before a completed quality operation can exist.");
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("attribute")]
    [DataRow("nested")]
    [DataRow("negative")]
    [DataRow("overflow")]
    [DataRow("decimal")]
    [DataRow("malformed")]
    [DataRow("empty")]
    [DataRow("missing")]
    public void ProfileQuality947_malformed_or_absent_source_never_borrows_operation(string kind)
    {
        using var context = Ready947.Create(includeGearReview: true, prepareSource: _ => { });
        var first = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        EditProfile800(Path.Combine(context.Directory, "source"), profile =>
        {
            var leaf = profile.Element("qualitykarmalimit")!;
            switch (kind)
            {
                case "duplicate": profile.Add(new XElement(leaf)); break;
                case "attribute": leaf.SetAttributeValue("unexecuted947", 1); break;
                case "nested": leaf.ReplaceNodes(new XElement("value", leaf.Value)); break;
                case "negative": leaf.Value = "-1"; break;
                case "overflow": leaf.Value = "2147483648"; break;
                case "decimal": leaf.Value = "25.0"; break;
                case "malformed": leaf.Value = "not-a-number"; break;
                case "empty": leaf.Value = ""; break;
                case "missing": leaf.Remove(); break;
            }
        });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithMovementSourceExecution(owners.Capture(), new(context.WorkspaceId),
            includeLoadout: true, includeSourcebooks: true);
        Assert.AreEqual(0, owners.ActiveLeases);
        if (loaded.Value is { } changed)
        {
            var capture = RestoreObserved839(changed);
            var result = CharacterCreationCompletionReviewService.Review(changed.Inputs, SourcebookRequest800(changed.Inputs),
                ruleSources: new CharacterCreationRuleSourceExecutionContext(capture, context.Queries));
            Assert.IsTrue(result.Value?.CompleteAssessment?.SatisfiesComplete != true);
            foreach (var report in capture.Reports.Where(row => row.Scope == "profile"))
                Assert.IsTrue(report.Operations.Where(row => HasField832(row, "qualitykarmalimit"))
                    .All(row => row.Disposition != D947.Compiled));
        }
        else Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(original),
            CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void ProfileQuality947_copied_mutated_deserialized_and_mismatched_packets_have_no_capability()
    {
        using var context = Ready947.Create(includeGearReview: true, includeNonEmptyPurchases: true, qualityName: "Simsense Vertigo");
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var observed = ObserveSources839(context, prepared, SourcebookRequest800(prepared.Inputs));
        var invocation = observed.Capture.Invocations.Single();
        var identity = QualityIdentity947(prepared.Inputs);
        Assert.IsTrue(ReadQuality947(invocation, identity, out var evaluation, out _, out _));
        string different = CharacterCreationFinalizationDigest.Compute("foreign947");
        foreach (var changed in new[]
        {
            identity with { ProfileId = Guid.NewGuid().ToString("D") }, identity with { ProfileInputsDigest = different },
            identity with { ProfileXmlDigest = different }, identity with { LeafXmlDigest = different }
        }) Assert.IsFalse(ReadQuality947(invocation, changed, out _, out _, out _));
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(prepared.Execution.ExportPrivateInputs(),
            prepared.Inputs.RuleSourceCaptureDigest!, out var retained));
        var resolution = retained!.CreateExecutionResolution(prepared.Inputs.RuleSourceExecutionSchema)!;
        Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsTrue(resolution.EnableCandidateSourcebookSelection());
        foreach (string changed in new[] { "identical", "policy", "capture", "request", "graph", "consumer", "workspace", "skills", "draft" })
        {
            var consumers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var source in prepared.Inputs.EffectivePolicy!.Sources)
                if (invocation.TryGetCompletedConsumer(source.SourceId, out var digest)) consumers.TryAdd(source.SourceId, digest);
            if (changed == "consumer") consumers["profile"] = different;
            var copy = new CharacterCreationRuleSourceScopeInvocation(
                changed == "policy" ? different : invocation.PolicyDigest, changed == "capture" ? different : invocation.CaptureDigest,
                changed == "request" ? different : invocation.RequestDigest, changed == "graph" ? different : invocation.GraphDigest,
                consumers, invocation.CompiledEffects)
            {
                CreationDraft718 = changed == "draft" ? null : invocation.CreationDraft718,
                Skills718 = changed == "skills" ? null : invocation.Skills718,
                WorkspaceId718 = changed == "workspace" ? null : invocation.WorkspaceId718,
                Attributes718 = invocation.Attributes718, AttributePolicy718 = invocation.AttributePolicy718,
                ResourcesExecution718 = invocation.ResourcesExecution718, LifestyleAuthority718 = invocation.LifestyleAuthority718,
                Lifestyles718 = invocation.Lifestyles718, StartingLifestyleId718 = invocation.StartingLifestyleId718,
                SourceExecution832 = invocation.SourceExecution832, CompiledMovement832 = invocation.CompiledMovement832,
                PhysicalAuthority886 = invocation.PhysicalAuthority886, PhysicalInitiative886 = invocation.PhysicalInitiative886
            };
            Assert.IsFalse(ReadQuality947(copy, identity, out _, out _, out _), changed);
            foreach (var report in resolution.ExecuteRemainingSourceScopes(copy).Where(row => row.Scope == "profile"))
                Assert.IsTrue(report.Operations.Where(row => HasField832(row, "qualitykarmalimit"))
                    .All(row => row.Disposition != D947.Compiled), changed);
        }
        var deserialized = JsonSerializer.Deserialize<CharacterCreationQualitiesCompletionEvaluation>(JsonSerializer.Serialize(evaluation))!;
        Assert.AreEqual(evaluation!.PositiveQualityBudget.Total, deserialized.PositiveQualityBudget.Total);
        Assert.AreEqual("{}", JsonSerializer.Serialize(invocation));
        AssertNotSupported957(() => JsonSerializer.Deserialize<CharacterCreationRuleSourceScopeInvocation>("{}"));
        foreach (string field in new[] { nameof(invocation.Skills718), nameof(invocation.CreationDraft718), nameof(invocation.WorkspaceId718) })
        {
            // Deliberately mutate the original registered object, not just a copy.
            var property = typeof(CharacterCreationRuleSourceScopeInvocation).GetProperty(field)!;
            object? before = property.GetValue(invocation);
            property.SetValue(invocation, null);
            Assert.IsFalse(ReadQuality947(invocation, identity, out _, out _, out _), field);
            property.SetValue(invocation, before);
        }
        var projections = (IList<CharacterCreationSkillProjection>)invocation.Skills718!.Skills;
        Assert.IsTrue(projections.Count > 0);
        var originalProjection = projections[0];
        try
        {
            projections[0] = originalProjection with { GroupRating = originalProjection.GroupRating + 1 };
            Assert.IsFalse(ReadQuality947(invocation, identity, out var rejected, out bool rejectedNoTarget, out var rejectedDigest));
            Assert.IsNull(rejected);
            Assert.IsFalse(rejectedNoTarget);
            Assert.IsNull(rejectedDigest);
        }
        finally { projections[0] = originalProjection; }
        Assert.IsTrue(ReadQuality947(invocation, identity, out _, out _, out _));
        AssertQualityLeaf947(resolution.ExecuteRemainingSourceScopes(invocation).Single(row => row.Scope == "profile"),
            identity.ProfileId, evaluation, false, ReadDigest947(invocation, identity));
    }

    [TestMethod]
    [DataRow("Ambidextrous")]
    [DataRow("Simsense Vertigo")]
    public void ProfileQuality947_retained_reconstruction_reexecutes_budget_and_rejects_source_drift(string name)
    {
        using var context = Ready947.Create(includeGearReview: true, includeNonEmptyPurchases: true, qualityName: name,
            wrapResolver: V4Sources905, prepareSource: _ => { });
        var prepared = PrepareProfileSourcebooks935(context, v4: true, books: true);
        var request = SourcebookRequest800(prepared.Inputs);
        byte[] bytes = prepared.Execution.ExportPrivateInputs();
        var first = ObserveSources839(context, prepared, request);
        var cold = ObserveSources839(context, prepared, request);
        var identity = QualityIdentity947(prepared.Inputs);
        Assert.AreEqual(true, first.Review.CompleteAssessment!.MechanicalSucceeded);
        Assert.AreEqual(true, cold.Review.CompleteAssessment!.MechanicalSucceeded);
        Assert.IsFalse(ReferenceEquals(first.Capture.Invocations.Single(), cold.Capture.Invocations.Single()));
        Assert.AreEqual(ReadDigest947(first.Capture.Invocations.Single(), identity), ReadDigest947(cold.Capture.Invocations.Single(), identity));
        Assert.AreEqual(JsonSerializer.Serialize(first.Review.CompleteAssessment), JsonSerializer.Serialize(cold.Review.CompleteAssessment));
        Assert.AreEqual(JsonSerializer.Serialize(first.Capture.Reports), JsonSerializer.Serialize(cold.Capture.Reports));
        Assert.AreEqual(string.Join("\n", first.Capture.Reports.SelectMany(row => row.Operations).Select(row => row.OperationResult718)),
            string.Join("\n", cold.Capture.Reports.SelectMany(row => row.Operations).Select(row => row.OperationResult718)));
        CollectionAssert.AreEqual(bytes, prepared.Execution.ExportPrivateInputs());
        string path = Path.Combine(context.Directory, "source", "data", CharacterCreationBootstrapProfiles.SettingsSourceFile(identity.ProfileId));
        File.AppendAllText(path, "\n<!-- source drift947 -->");
        var producer = (ICharacterCreationRuleSourceCapture)context.Resolver;
        Assert.IsTrue(producer.TryCaptureSourcebookRuleSources(context.Store.Get(context.WorkspaceId).Value!.Document.Content, out var changed));
        var resolution = changed!.CreateExecutionResolution()!;
        Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsTrue(resolution.EnableCandidateSourcebookSelection());
        Assert.AreEqual(0, resolution.ExecuteRemainingSourceScopes(first.Capture.Invocations.Single()).Count);
    }

    [TestMethod]
    public void ProfileQuality947_unresolved_siblings_refuse_selected_Complete_witness()
    {
        using var context = Ready947.Create(includeGearReview: true, includeNonEmptyPurchases: true, qualityName: "Ambidextrous");
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var capture = RestoreObserved839(prepared);
        var result = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, prepared.Inputs,
            SourcebookRequest800(prepared.Inputs), ruleSources: new CharacterCreationRuleSourceExecutionContext(capture, context.Queries));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.IsNull(result.Value);
        Assert.IsTrue(capture.Invocations.Count > 0);
        Assert.IsTrue(ReadQuality947(capture.Invocations.Last(), QualityIdentity947(prepared.Inputs), out _, out _, out _));
var costIdentity973 = CostIdentity973(prepared.Inputs);
Assert.IsTrue(ReadCost973(capture.Invocations.Last(), costIdentity973, out var priced973, out var policy973, out bool empty973, out var costDigest973));
AssertCostLeaf973(capture.Reports.Last(row => row.Scope == "profile"), costIdentity973,
    priced973!, policy973!, empty973, costDigest973!);
    }

    [TestMethod]
    [DataRow("Ambidextrous")]
    [DataRow("Simsense Vertigo")]
    public void ProfileQuality953_diagnostic_results_detach_nested_rows_and_budgets(string name)
    {
        using var context = Ready947.Create(includeGearReview: true, includeNonEmptyPurchases: true, qualityName: name);
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var observed = ObserveSources839(context, prepared, SourcebookRequest800(prepared.Inputs));
        var invocation = observed.Capture.Invocations.Single();
        var identity = QualityIdentity947(prepared.Inputs);
        Assert.IsTrue(ReadQuality947(invocation, identity, out var first, out bool noTarget, out var digest));
        Assert.IsFalse(noTarget);
        Assert.AreEqual(1, first!.Selections.Count);
        string expected = JsonSerializer.Serialize(first);
        Assert.IsTrue(ReadQuality947(invocation, identity, out var second, out _, out var secondDigest));
        Assert.AreEqual(expected, JsonSerializer.Serialize(second));
        Assert.AreEqual(digest, secondDigest);
        Assert.IsFalse(ReferenceEquals(first, second));
        Assert.IsFalse(ReferenceEquals(first.Selections, second!.Selections));
        Assert.IsFalse(ReferenceEquals(first.Selections[0], second.Selections[0]));
        Assert.IsFalse(ReferenceEquals(first.Selections[0].SourceAnchorIds, second.Selections[0].SourceAnchorIds));
        // Empty read-only collections may safely share the runtime's singleton.
        if (first.GrantedQualities.Count != 0)
            Assert.IsFalse(ReferenceEquals(first.GrantedQualities, second.GrantedQualities));
        Assert.IsFalse(ReferenceEquals(first.PositiveQualityBudget, second.PositiveQualityBudget));
        Assert.IsFalse(ReferenceEquals(first.NegativeQualityBudget, second.NegativeQualityBudget));
        Assert.IsTrue(((IList<CharacterCreationQualitySelection>)first.Selections).IsReadOnly);
        Assert.IsTrue(((IList<CharacterCreationGrantedQuality>)first.GrantedQualities).IsReadOnly);
        AssertNotSupported957(() => ((IList<CharacterCreationGrantedQuality>)first.GrantedQualities).Clear());
        Assert.IsTrue(((IList<string>)first.Selections[0].SourceAnchorIds).IsReadOnly);
        Assert.IsTrue(((IList<string>)first.SourceAnchorIds).IsReadOnly);
        Assert.IsTrue(((IList<string>)first.Blockers).IsReadOnly);
        Assert.IsTrue(((IList<string>)first.PositiveQualityBudget.Blockers).IsReadOnly);
        Assert.IsTrue(((IList<string>)first.NegativeQualityBudget.Blockers).IsReadOnly);
        AssertNotSupported957(() =>
            ((IList<CharacterCreationQualitySelection>)first.Selections)[0] = second.Selections[0]);
        typeof(CharacterCreationQualitySelection).GetProperty(nameof(CharacterCreationQualitySelection.SourceAnchorIds))!
            .SetValue(first.Selections[0], new[] { "foreign953" });
        first.PositiveQualityBudget.GetType().GetProperty("Total")!.SetValue(first.PositiveQualityBudget, 999);
        first.NegativeQualityBudget.GetType().GetProperty("Total")!.SetValue(first.NegativeQualityBudget, 999);
        Assert.IsTrue(ReadQuality947(invocation, identity, out var reread, out bool rereadNoTarget, out var rereadDigest));
        Assert.AreEqual(expected, JsonSerializer.Serialize(reread));
        Assert.AreEqual(expected, JsonSerializer.Serialize(second));
        Assert.AreEqual(noTarget, rereadNoTarget);
        Assert.AreEqual(digest, rereadDigest);
        AssertQualityLeaf947(observed.Capture.Reports.Single(row => row.Scope == "profile"),
            identity.ProfileId, reread!, noTarget, digest!);
    }

    private static void AssertNotSupported957(Action action)
    {
        bool refused = false;
        try { action(); }
        catch (NotSupportedException) { refused = true; }
        Assert.IsTrue(refused, "The operation must throw NotSupportedException.");
    }

    private sealed record QualityIdentity947Value(string ProfileId, string ProfileInputsDigest, string ProfileXmlDigest, string LeafXmlDigest);
    private static QualityIdentity947Value QualityIdentity947(CharacterCreationCompletionReviewInputs inputs)
    {
        var policy = inputs.EffectivePolicy!;
        var profile = XElement.Parse(inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        return new(policy.SettingsProfileId, policy.ProfileInputsDigest,
            CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
            CharacterCreationFinalizationDigest.Compute(profile.Elements("qualitykarmalimit").First().ToString(SaveOptions.DisableFormatting)));
    }

    private static bool ReadQuality947(CharacterCreationRuleSourceScopeInvocation invocation, QualityIdentity947Value identity,
        out CharacterCreationQualitiesCompletionEvaluation? result, out bool noTarget, out string? digest)
        => invocation.TryGetExecutedProfileQualityKarmaLimit947(identity.ProfileId, identity.ProfileInputsDigest,
            identity.ProfileXmlDigest, identity.LeafXmlDigest, out result, out noTarget, out digest);

    private static string ReadDigest947(CharacterCreationRuleSourceScopeInvocation invocation, QualityIdentity947Value identity)
    {
        Assert.IsTrue(ReadQuality947(invocation, identity, out _, out _, out var digest));
        return digest!;
    }

    private static CharacterCreationSourceOperation[] QualityField947(CharacterCreationRuleSourceScopeResult report, string profileId, string field)
        => report.Operations.Where(row => row.Path.StartsWith(SelectedProfilePath935(report, profileId) + "/" + field + "[", StringComparison.Ordinal)).ToArray();

    private static void AssertQualityUnconsumed947(CharacterCreationRuleSourceScopeResult report, string profileId, string field)
    {
        var operations = QualityField947(report, profileId, field);
        Assert.IsTrue(operations.Length > 0, field);
        Assert.IsTrue(operations.All(row => row.Disposition == D947.Unconsumed && row.ExecutionDigest is null
            && row.OperationResult718 is null && row.ConsumedValue718 is null), field);
    }

    private static void AssertQualityLeaf947(CharacterCreationRuleSourceScopeResult report, string profileId,
        CharacterCreationQualitiesCompletionEvaluation result, bool noTarget, string digest)
    {
        var operations = QualityField947(report, profileId, "qualitykarmalimit");
        Assert.AreEqual(2, operations.Length, "Exactly the scalar element and its text occurrence.");
        foreach (var operation in operations)
        {
            Assert.AreEqual(D947.Compiled, operation.Disposition);
            Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(operation.ExecutionDigest));
            using var payload = JsonDocument.Parse(operation.OperationResult718!);
            Assert.AreEqual("canonical-quality-karma-limit", payload.RootElement.GetProperty("Operation").GetString());
            Assert.AreEqual(result.PositiveQualityBudget.Total, payload.RootElement.GetProperty("Limit").GetInt32());
            Assert.AreEqual(noTarget, payload.RootElement.GetProperty("NoTarget").GetBoolean());
            Assert.AreEqual(digest, payload.RootElement.GetProperty("Execution").GetString());
            Assert.AreEqual(JsonSerializer.Serialize(result), payload.RootElement.GetProperty("Result").GetRawText());
        }
    }
}
