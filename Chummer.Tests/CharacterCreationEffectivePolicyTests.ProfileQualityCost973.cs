using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready973 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using D973 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow("Ambidextrous", 2, false)]
    [DataRow("Simsense Vertigo", 3, true)]
    [DataRow("both", 1, true)]
    [DataRow("both", 3, false)]
    [DataRow("both", 0, true)]
    [DataRow("", 3, true)]
    [DataRow("", int.MaxValue, false)]
    public void ProfileQuality973_actual_costs_bind_exact_source_and_completed_candidate(string name, int multiplier, bool v4)
    {
        using var context = Ready973.Create(includeGearReview: true, wrapResolver: v4 ? V4Sources905 : null,
            amendSettings: profile =>
            {
                profile.Element("karmacost")!.SetElementValue("karmaquality", multiplier);
                profile.Element("karmacost")!.Add(new XElement("unknowncost973", 1));
            });
        var prepared = PrepareProfileSourcebooks935(context, v4, books: true);
        var request = CostRequest973(prepared.Inputs, name);
        string before = CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!);
        var observed = ObserveSources839(context, prepared, request);
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var invocation = observed.Capture.Invocations.Single();
        var identity = CostIdentity973(prepared.Inputs);
        Assert.IsTrue(ReadCost973(invocation, identity, out var actual, out var policy, out bool empty, out var digest));
        Assert.AreEqual(multiplier, policy!.KarmaMultiplier);
        Assert.AreEqual(name.Length == 0, empty);
        Assert.AreEqual(name is "Ambidextrous" or "both" ? 4 * multiplier : 0, actual!.PositiveQualityBudget.Used);
        Assert.AreEqual(name is "Simsense Vertigo" or "both" ? 5 * multiplier : 0, actual.NegativeQualityBudget.Used);
        Assert.AreEqual(25, actual.PositiveQualityBudget.Total);
        Assert.AreEqual(25, actual.NegativeQualityBudget.Total);
        Assert.IsFalse(actual.PositiveQualityBudget.MayExceed);
        Assert.IsFalse(actual.NegativeQualityBudget.MayExceed);
        CollectionAssert.AreEquivalent(name == "both" ? new[] { 4, -5 } : name == "Ambidextrous" ? new[] { 4 }
            : name == "Simsense Vertigo" ? new[] { -5 } : Array.Empty<int>(), actual.Selections.Select(row => row.KarmaCost).ToArray());
        Assert.AreEqual(prepared.Inputs.Lifestyles.Qualities.Binding.CreationKarmaTotal - actual.ProposedAttributeKarmaUsed
            - actual.PositiveQualityBudget.Used + actual.NegativeQualityBudget.Used, actual.KarmaRemaining);
        Assert.AreEqual(JsonSerializer.Serialize(observed.Review.UpstreamBudget!.Qualities.Qualities), JsonSerializer.Serialize(actual));
        AssertCostLeaf973(observed.Capture.Reports.Single(row => row.Scope == "profile"), identity, actual, policy, empty, digest!);
        var siblings = CostFields973(observed.Capture.Reports.Single(row => row.Scope == "profile"), identity.ProfileId, "unknowncost973");
        Assert.IsTrue(siblings.Length > 0);
        Assert.IsTrue(siblings.All(row => row.Disposition == D973.Unconsumed && row.ExecutionDigest is null && row.OperationResult718 is null));
        Assert.IsFalse(observed.Review.CompleteAssessment.SatisfiesComplete);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved, ErrataDomain788(observed.Review.CompleteAssessment).Coverage);
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    [DataRow("positive", CharacterCreationQualitiesBlockers.PositiveLimitExceeded)]
    [DataRow("negative", CharacterCreationQualitiesBlockers.NegativeLimitExceeded)]
    [DataRow("karma", CharacterCreationQualitiesBlockers.KarmaExceeded)]
    [DataRow("invalid", CharacterCreationQualitiesBlockers.InvalidSelection)]
    [DataRow("unsupported", "")]
    public void ProfileQuality973_failed_canonical_prices_and_selections_cannot_register(string kind, string blocker)
    {
        using var context = Ready973.Create(includeGearReview: true, amendSettings: profile =>
        {
            profile.Element("karmacost")!.SetElementValue("karmaquality", 3);
            if (kind is "positive" or "negative") profile.SetElementValue("qualitykarmalimit", 1);
            if (kind == "karma") profile.SetElementValue("buildpoints", 1);
        });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var request = CostRequest973(prepared.Inputs, kind == "negative" ? "Simsense Vertigo" : "Ambidextrous");
        if (kind == "invalid") request = request with { Selections = request.Selections with { QualityOptionIds = ["missing973"] } };
        if (kind == "unsupported")
        {
            var unsupported = prepared.Inputs.Lifestyles.Qualities.Authority.Options.Single(row => row.Name == "Bad Luck");
            Assert.IsFalse(unsupported.IsSelectable);
            Assert.IsFalse(string.IsNullOrWhiteSpace(unsupported.DisableReasonKey));
            blocker = unsupported.DisableReasonKey!;
            request = request with { Selections = request.Selections with { QualityOptionIds = [unsupported.OptionId] } };
        }
        var canonical = CharacterCreationQualitiesService.PreviewWithProposedAttributes(prepared.Inputs.Lifestyles.Qualities,
            request.Selections.AttributeAllocations, request.Selections.QualityOptionIds);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, canonical.Outcome);
        CollectionAssert.Contains(canonical.Blockers.ToArray(), blocker);
        var capture = RestoreObserved839(prepared);
        var reviewed = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request,
            ruleSources: new CharacterCreationRuleSourceExecutionContext(capture, context.Queries));
        Assert.IsTrue(reviewed.Value?.CompleteAssessment?.MechanicalSucceeded != true);
        foreach (var invocation in capture.Invocations)
            Assert.IsFalse(ReadCost973(invocation, CostIdentity973(prepared.Inputs), out _, out _, out _, out _));
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("container")]
    [DataRow("attribute")]
    [DataRow("nested")]
    [DataRow("negative")]
    [DataRow("overflow")]
    [DataRow("decimal")]
    [DataRow("missing")]
    [DataRow("comment")]
    [DataRow("cdata")]
    public void ProfileQuality973_invalid_or_absent_source_cannot_borrow_default_execution(string kind)
    {
        using var context = Ready973.Create(includeGearReview: true, prepareSource: _ => { });
        EditProfile800(Path.Combine(context.Directory, "source"), profile =>
        {
            var costs = profile.Element("karmacost")!;
            var leaf = costs.Element("karmaquality")!;
            switch (kind)
            {
                case "duplicate": costs.Add(new XElement(leaf)); break;
                case "container": profile.Add(new XElement(costs)); break;
                case "attribute": leaf.SetAttributeValue("unexecuted973", 1); break;
                case "nested": leaf.ReplaceNodes(new XElement("value", leaf.Value)); break;
                case "negative": leaf.Value = "-1"; break;
                case "overflow": leaf.Value = "2147483648"; break;
                case "decimal": leaf.Value = "1.0"; break;
                case "missing": leaf.Remove(); break;
                case "comment": leaf.Add(new XComment("nonplain973")); break;
                case "cdata": leaf.ReplaceNodes(new XCData(leaf.Value)); break;
            }
        });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithMovementSourceExecution(owners.Capture(), new(context.WorkspaceId),
            includeLoadout: true, includeSourcebooks: true);
        Assert.AreEqual(0, owners.ActiveLeases);
        if (loaded.Value is not { } prepared)
        {
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome);
            return;
        }
        var capture = RestoreObserved839(prepared);
        var review = CharacterCreationCompletionReviewService.Review(prepared.Inputs, SourcebookRequest800(prepared.Inputs),
            ruleSources: new CharacterCreationRuleSourceExecutionContext(capture, context.Queries));
        Assert.IsTrue(review.Value?.CompleteAssessment?.SatisfiesComplete != true);
        foreach (var report in capture.Reports.Where(row => row.Scope == "profile"))
            Assert.IsTrue(report.Operations.Where(row => HasField832(row, "karmaquality"))
                .All(row => row.Disposition == D973.Unconsumed && row.OperationResult718 is null));
    }

    [TestMethod]
    public void ProfileQuality973_copies_mutation_and_stale_source_refuse_and_diagnostics_detach()
    {
        using var context = Ready973.Create(includeGearReview: true, prepareSource: _ => { },
            amendSettings: profile => profile.Element("karmacost")!.SetElementValue("karmaquality", 3));
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var request = CostRequest973(prepared.Inputs, "both");
        var observed = ObserveSources839(context, prepared, request);
        var invocation = observed.Capture.Invocations.Single();
        var identity = CostIdentity973(prepared.Inputs);
        Assert.IsTrue(ReadCost973(invocation, identity, out var actual, out var policy, out _, out var digest));
        string expected = JsonSerializer.Serialize(actual);
        string other = CharacterCreationFinalizationDigest.Compute("foreign973");
        foreach (var changed in new[] { identity with { ProfileId = other }, identity with { ProfileInputsDigest = other },
            identity with { ProfileXmlDigest = other }, identity with { LeafXmlDigest = other } })
            Assert.IsFalse(ReadCost973(invocation, changed, out _, out _, out _, out _));
        var consumers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in prepared.Inputs.EffectivePolicy!.Sources)
            if (invocation.TryGetCompletedConsumer(source.SourceId, out var consumer)) consumers.TryAdd(source.SourceId, consumer);
        var copy = new CharacterCreationRuleSourceScopeInvocation(invocation.PolicyDigest, invocation.CaptureDigest,
            invocation.RequestDigest, invocation.GraphDigest, consumers, invocation.CompiledEffects);
        foreach (var property in typeof(CharacterCreationRuleSourceScopeInvocation).GetProperties().Where(row => row.CanWrite))
            property.SetValue(copy, property.GetValue(invocation));
        Assert.IsFalse(ReadCost973(copy, identity, out _, out _, out _, out _));
        Assert.AreEqual("{}", JsonSerializer.Serialize(invocation));
        AssertNotSupported957(() => JsonSerializer.Deserialize<CharacterCreationRuleSourceScopeInvocation>("{}"));
        var propertyToMutate = typeof(CharacterCreationRuleSourceScopeInvocation).GetProperty(nameof(invocation.Skills718))!;
        var skills = propertyToMutate.GetValue(invocation);
        propertyToMutate.SetValue(invocation, null);
        Assert.IsFalse(ReadCost973(invocation, identity, out _, out _, out _, out _));
        propertyToMutate.SetValue(invocation, skills);
        Assert.IsTrue(((IList<CharacterCreationQualitySelection>)actual!.Selections).IsReadOnly);
        typeof(CharacterCreationQualitySelection).GetProperty(nameof(CharacterCreationQualitySelection.SourceAnchorIds))!
            .SetValue(actual.Selections[0], new[] { "foreign973" });
        actual.PositiveQualityBudget.GetType().GetProperty("Total")!.SetValue(actual.PositiveQualityBudget, 999);
        actual.NegativeQualityBudget.GetType().GetProperty("Total")!.SetValue(actual.NegativeQualityBudget, 999);
        typeof(CharacterCreationQualityCostPolicy).GetProperty(nameof(CharacterCreationQualityCostPolicy.KarmaMultiplier))!
            .SetValue(policy, 999);
        Assert.IsTrue(ReadCost973(invocation, identity, out var reread, out var rereadPolicy, out _, out var rereadDigest));
        Assert.AreEqual(expected, JsonSerializer.Serialize(reread));
        Assert.AreEqual(3, rereadPolicy!.KarmaMultiplier);
        Assert.AreEqual(digest, rereadDigest);
        var cold = ObserveSources839(context, prepared, request);
        Assert.IsFalse(ReferenceEquals(invocation, cold.Capture.Invocations.Single()));
        Assert.IsTrue(ReadCost973(cold.Capture.Invocations.Single(), identity, out _, out _, out _, out var coldDigest));
        Assert.AreEqual(digest, coldDigest);
        Assert.AreEqual(JsonSerializer.Serialize(observed.Capture.Reports), JsonSerializer.Serialize(cold.Capture.Reports));
        string path = Path.Combine(context.Directory, "source", "data", CharacterCreationBootstrapProfiles.SettingsSourceFile(identity.ProfileId));
        File.AppendAllText(path, "\n<!-- source drift973 -->");
        var producer = (ICharacterCreationRuleSourceCapture)context.Resolver;
        Assert.IsTrue(producer.TryCaptureSourcebookRuleSources(context.Store.Get(context.WorkspaceId).Value!.Document.Content, out var changedSource));
        var resolution = changedSource!.CreateExecutionResolution()!;
        Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsTrue(resolution.EnableCandidateSourcebookSelection());
        Assert.AreEqual(0, resolution.ExecuteRemainingSourceScopes(invocation).Count);
    }

    [TestMethod]
    [DataRow("Ambidextrous", 2)]
    [DataRow("Simsense Vertigo", 3)]
    [DataRow("both", 3)]
    public void ProfileQuality973_selected_replay_preserves_real_prices_without_claiming_Complete(string name, int multiplier)
    {
        using var context = Ready973.Create(includeGearReview: true,
            amendSettings: profile => profile.Element("karmacost")!.SetElementValue("karmaquality", multiplier));
        var inputs = LoadConsumption(context);
        var request = CostRequest973(inputs, name, Request(inputs, CharacterCreationPolicyRequirement.None));
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capture.Outcome, string.Join(",", capture.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, capture.Value!, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var coldWitness));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, coldWitness, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(reviewed.Value!.UpstreamBudget!.Qualities.Qualities with
            { AuthorityDigest = replay.Value!.Qualities.AuthorityDigest }), JsonSerializer.Serialize(replay.Value.Qualities));
        Assert.AreEqual(CharacterCreationSettingConsumptionReason.Evaluated, reviewed.Value.PolicyConsumption!.Settings
            .Single(row => row.Key == CharacterCreationObservedSetting.QualityKarma).Reason);
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var observed = RestoreObserved839(prepared);
        var completeBootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var complete = CharacterCreationSelectedCompletionEvaluator.Capture(original, completeBootstrap, prepared.Inputs,
            CostRequest973(prepared.Inputs, name), ruleSources: new CharacterCreationRuleSourceExecutionContext(observed, context.Queries));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, complete.Outcome);
        Assert.IsNull(complete.Value);
        Assert.IsTrue(observed.Invocations.Count > 0);
        Assert.IsTrue(ReadCost973(observed.Invocations.Last(), CostIdentity973(prepared.Inputs), out _, out _, out _, out _));
    }

    private static CharacterCreationCompletionReviewRequest CostRequest973(CharacterCreationCompletionReviewInputs inputs,
        string name, CharacterCreationCompletionReviewRequest? basis = null)
    {
        var request = basis ?? SourcebookRequest800(inputs);
        var names = name == "both" ? new[] { "Ambidextrous", "Simsense Vertigo" } : name.Length == 0 ? [] : new[] { name };
        var ids = names.Select(value => inputs.Lifestyles.Qualities.Authority.Options.Single(row => row.Name == value && row.IsSelectable).OptionId).ToArray();
        return request with { Selections = request.Selections with { QualityOptionIds = ids } };
    }

    private static QualityIdentity947Value CostIdentity973(CharacterCreationCompletionReviewInputs inputs)
    {
        var identity = QualityIdentity947(inputs);
        var profile = XElement.Parse(inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        return identity with { LeafXmlDigest = CharacterCreationFinalizationDigest.Compute(
            profile.Element("karmacost")!.Element("karmaquality")!.ToString(SaveOptions.DisableFormatting)) };
    }

    private static bool ReadCost973(CharacterCreationRuleSourceScopeInvocation invocation, QualityIdentity947Value identity,
        out CharacterCreationQualitiesCompletionEvaluation? result, out CharacterCreationQualityCostPolicy? policy,
        out bool empty, out string? digest)
        => invocation.TryGetExecutedProfileQualityCost973(identity.ProfileId, identity.ProfileInputsDigest,
            identity.ProfileXmlDigest, identity.LeafXmlDigest, out result, out policy, out empty, out digest);

    private static CharacterCreationSourceOperation[] CostFields973(CharacterCreationRuleSourceScopeResult report, string profileId, string field)
        => report.Operations.Where(row => row.Path.StartsWith(SelectedProfilePath935(report, profileId) + "/karmacost[", StringComparison.Ordinal)
            && HasField832(row, field)).ToArray();

    private static void AssertCostLeaf973(CharacterCreationRuleSourceScopeResult report, QualityIdentity947Value identity,
        CharacterCreationQualitiesCompletionEvaluation result, CharacterCreationQualityCostPolicy policy, bool empty, string digest)
    {
        var rows = CostFields973(report, identity.ProfileId, "karmaquality");
        Assert.AreEqual(2, rows.Length);
        foreach (var row in rows)
        {
            Assert.AreEqual(D973.Compiled, row.Disposition);
            Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(row.ExecutionDigest));
            Assert.AreEqual(policy.KarmaMultiplier.ToString(CultureInfo.InvariantCulture), row.ConsumedValue718);
            using var payload = JsonDocument.Parse(row.OperationResult718!);
            Assert.AreEqual("canonical-quality-karma-cost", payload.RootElement.GetProperty("Operation").GetString());
            Assert.AreEqual(JsonSerializer.Serialize(policy), payload.RootElement.GetProperty("CostPolicy").GetRawText());
            Assert.AreEqual(JsonSerializer.Serialize(result), payload.RootElement.GetProperty("Result").GetRawText());
            Assert.AreEqual(empty, payload.RootElement.GetProperty("NoTarget").GetBoolean());
            Assert.AreEqual(digest, payload.RootElement.GetProperty("Execution").GetString());
        }
        Assert.IsFalse(report.IsComplete);
    }
}

public sealed partial class CharacterCreationEffectivePolicyTests
{
    private static readonly string[] QualityPolicyFields1004 =
    ["exceedpositivequalities", "exceedpositivequalitiescostdoubled", "exceednegativequalities", "exceednegativequalitiesnobonus"];

    [TestMethod]
    [DataRow("both", 1, 25, false, false, false, false, 4, 5)]
    [DataRow("both", 2, 25, false, true, false, true, 8, 10)]
    [DataRow("both", 0, 0, false, true, false, true, 0, 0)]
    [DataRow("", 3, 0, true, true, true, true, 0, 0)]
    public void ProfileQuality1004_full_and_actual_selected_costs_account_exact_flags(string name, int multiplier,
        int limit, bool positive, bool doubled, bool negative, bool noBonus, int positiveCost, int negativeCost)
        => AssertQualityPolicyCandidate1004(name, multiplier, limit, positive, doubled, negative, noBonus, positiveCost, negativeCost);

    [TestMethod]
    [DataRow("both", 2, 3, true, false, true, false, 8, 10)]
    [DataRow("both", 2, 3, true, true, true, false, 13, 10)]
    [DataRow("both", 2, 3, true, false, true, true, 8, 3)]
    [DataRow("both", 2, 3, true, true, true, true, 13, 3)]
    [DataRow("Ambidextrous", 3, 3, true, true, false, false, 21, 0)]
    [DataRow("Simsense Vertigo", 3, 3, false, false, true, true, 0, 3)]
    [DataRow("Simsense Vertigo", 3, 3, false, false, false, true, 0, 3)]
    public void ProfileQuality1004_excess_full_and_actual_selected_costs_account_exact_flags(string name, int multiplier,
        int limit, bool positive, bool doubled, bool negative, bool noBonus, int positiveCost, int negativeCost)
        => AssertQualityPolicyCandidate1004(name, multiplier, limit, positive, doubled, negative, noBonus, positiveCost, negativeCost);

    private static void AssertQualityPolicyCandidate1004(string name, int multiplier,
        int limit, bool positive, bool doubled, bool negative, bool noBonus, int positiveCost, int negativeCost)
    {
        using var context = Ready973.Create(includeGearReview: true, amendSettings: profile =>
            ConfigureQualityPolicy1004(profile, multiplier, limit, positive, doubled, negative, noBonus));
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var request = CostRequest973(prepared.Inputs, name);
        string originalDigest = CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!);
        var observed = ObserveSources839(context, prepared, request);
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var actual = observed.Review.UpstreamBudget!.Qualities.Qualities;
        Assert.AreEqual(positiveCost, actual.PositiveQualityBudget.Used);
        Assert.AreEqual(negativeCost, actual.NegativeQualityBudget.Used);
        Assert.AreEqual(positive, actual.PositiveQualityBudget.MayExceed);
        Assert.AreEqual(negative, actual.NegativeQualityBudget.MayExceed);
        Assert.AreEqual(prepared.Inputs.Lifestyles.Qualities.Binding.CreationKarmaTotal - actual.ProposedAttributeKarmaUsed
            - positiveCost + negativeCost, actual.KarmaRemaining);
        Assert.AreEqual(name.Length == 0 ? 0 : name == "both" ? 2 : 1, actual.Selections.Count);
        CollectionAssert.AreEquivalent(name == "both" ? new[] { 4, -5 } : name == "Ambidextrous" ? new[] { 4 }
            : name == "Simsense Vertigo" ? new[] { -5 } : Array.Empty<int>(), actual.Selections.Select(row => row.KarmaCost).ToArray());
        AssertQualityPolicy1004(observed.Capture.Invocations.Single(), observed.Capture.Reports.Single(row => row.Scope == "profile"),
            prepared.Inputs, actual, name.Length == 0);
        AssertQualityUnconsumed947(observed.Capture.Reports.Single(row => row.Scope == "profile"),
            prepared.Inputs.EffectivePolicy!.SettingsProfileId, "unknownquality1004");
        AssertQualityUnconsumed947(observed.Capture.Reports.Single(row => row.Scope == "profile"),
            prepared.Inputs.EffectivePolicy.SettingsProfileId, "exceednegativequalitieslimit");
        Assert.IsFalse(observed.Review.CompleteAssessment.SatisfiesComplete);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved, ErrataDomain788(observed.Review.CompleteAssessment).Coverage);

        // Actually run the selected evaluator and archive round trip. This historical
        // None witness stays None throughout; it supplies no Complete capability.
        var inputs = LoadConsumption(context);
        var historicalRequest = CostRequest973(inputs, name, Request(inputs, CharacterCreationPolicyRequirement.None));
        var full = CharacterCreationCompletionReviewService.Review(inputs, historicalRequest);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, historicalRequest);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        Assert.AreEqual(CharacterCreationPolicyRequirement.None, captured.Value!.PolicyRequirement);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, captured.Value, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var coldWitness));
        Assert.AreEqual(CharacterCreationPolicyRequirement.None, coldWitness!.PolicyRequirement);
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, coldWitness, historicalRequest);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(positiveCost, replay.Value!.Qualities.PositiveQualityBudget.Used);
        Assert.AreEqual(negativeCost, replay.Value.Qualities.NegativeQualityBudget.Used);
        Assert.AreEqual(JsonSerializer.Serialize(full.Value!.UpstreamBudget!.Qualities.Qualities with
            { AuthorityDigest = replay.Value.Qualities.AuthorityDigest }), JsonSerializer.Serialize(replay.Value.Qualities));
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(prepared.Inputs, request);
        Assert.IsNull(CharacterCreationCompleteProfileSettingsExecution.QualityKarmaLimitExecution947.ExecuteSelected(
            coldWitness, historicalRequest, replay.Value, authority, out var independentlyEvaluated, default));
        Assert.IsNotNull(independentlyEvaluated);
        Assert.AreEqual(JsonSerializer.Serialize(replay.Value.Qualities), JsonSerializer.Serialize(independentlyEvaluated));
        Assert.AreEqual(originalDigest, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    [DataRow("both")]
    [DataRow("")]
    public void ProfileQuality1004_public_selected_Complete_remains_blocked(string name)
    {
        using var context = Ready973.Create(includeGearReview: true, wrapResolver: V4Sources905,
            amendSettings: profile => ConfigureQualityPolicy1004(profile, 2, 3, true, true, true, true));
        var prepared = PrepareProfileSourcebooks935(context, v4: true, books: true);
        var capture = RestoreObserved839(prepared);
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var result = CharacterCreationSelectedCompletionEvaluator.Capture(context.Store.Get(context.WorkspaceId).Value!, bootstrap,
            prepared.Inputs, CostRequest973(prepared.Inputs, name),
            ruleSources: new CharacterCreationRuleSourceExecutionContext(capture, context.Queries));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.IsNull(result.Value);
        var invocation = capture.Invocations.Last();
        Assert.IsTrue(ReadPolicy1004(invocation, PolicyIdentity1004(prepared.Inputs, QualityPolicyFields1004[0]),
            QualityPolicyFields1004[0], out var evaluation, out _, out _, out _, out _));
        AssertQualityPolicy1004(invocation, capture.Reports.Last(row => row.Scope == "profile"), prepared.Inputs,
            evaluation!, name.Length == 0);
        // Public Capture stops after the full review's independent Complete
        // blockers. Verify the separately executed selected capability too.
        var request = CostRequest973(prepared.Inputs, name);
        var full = ObserveSources839(context, prepared, request);
        Assert.AreEqual(true, full.Review.CompleteAssessment!.MechanicalSucceeded);
        var selected = RestoreObserved839(prepared);
        var method = typeof(CharacterCreationSelectedCompletionEvaluator).GetMethod("CaptureReviewed",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var selectedResult = (CharacterCreationFoundationResult<CharacterCreationSelectedCompletionWitness>)method.Invoke(null,
            [context.Store.Get(context.WorkspaceId).Value!, bootstrap, prepared.Inputs, request, full.Review, CancellationToken.None,
                new CharacterCreationRuleSourceExecutionContext(selected, context.Queries), null])!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, selectedResult.Outcome);
        Assert.IsNull(selectedResult.Value);
        Assert.AreEqual(1, selected.Invocations.Count, string.Join(',', selectedResult.Blockers));
        AssertQualityPolicy1004(selected.Invocations.Single(), selected.Reports.Single(row => row.Scope == "profile"),
            prepared.Inputs, full.Review.UpstreamBudget!.Qualities.Qualities, name.Length == 0);
    }

    [TestMethod]
    [DataRow("positive", CharacterCreationQualitiesBlockers.PositiveLimitExceeded)]
    [DataRow("negative", CharacterCreationQualitiesBlockers.NegativeLimitExceeded)]
    [DataRow("karma", CharacterCreationQualitiesBlockers.KarmaExceeded)]
    [DataRow("invalid", CharacterCreationQualitiesBlockers.InvalidSelection)]
    public void ProfileQuality1004_failed_mechanics_cannot_account_flags(string kind, string blocker)
    {
        using var context = Ready973.Create(includeGearReview: true, amendSettings: profile =>
        {
            ConfigureQualityPolicy1004(profile, 2, 3, kind != "positive", true, kind != "negative", false);
            if (kind == "karma") profile.SetElementValue("buildpoints", 1);
        });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var request = CostRequest973(prepared.Inputs, kind == "negative" ? "Simsense Vertigo" : "Ambidextrous");
        if (kind == "invalid") request = request with { Selections = request.Selections with { QualityOptionIds = ["missing1004"] } };
        var canonical = CharacterCreationQualitiesService.PreviewWithProposedAttributes(prepared.Inputs.Lifestyles.Qualities,
            request.Selections.AttributeAllocations, request.Selections.QualityOptionIds);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, canonical.Outcome);
        CollectionAssert.Contains(canonical.Blockers.ToArray(), blocker);
        var capture = RestoreObserved839(prepared);
        var reviewed = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request,
            ruleSources: new CharacterCreationRuleSourceExecutionContext(capture, context.Queries));
        Assert.IsTrue(reviewed.Value?.CompleteAssessment?.MechanicalSucceeded != true);
        foreach (var invocation in capture.Invocations)
            foreach (string field in QualityPolicyFields1004)
                Assert.IsFalse(ReadPolicy1004(invocation, PolicyIdentity1004(prepared.Inputs, field), field,
                    out _, out _, out _, out _, out _));
    }

    [TestMethod]
    [DataRow("exceedpositivequalities")]
    [DataRow("exceedpositivequalitiescostdoubled")]
    [DataRow("exceednegativequalities")]
    [DataRow("exceednegativequalitiesnobonus")]
    public void ProfileQuality1004_malformed_duplicate_namespaced_or_absent_flag_never_accounts(string field)
    {
        foreach (string kind in new[] { "duplicate", "attribute", "nested", "invalid", "numeric", "missing",
            "comment", "cdata", "namespaced", "namespaced-sibling", "processing-instruction" })
        {
            using var context = Ready973.Create(includeGearReview: true, prepareSource: _ => { });
            EditProfile800(Path.Combine(context.Directory, "source"), profile =>
            {
                ConfigureQualityPolicy1004(profile, 1, 25, false, false, false, false);
                var leaf = profile.Element(field)!;
                switch (kind)
                {
                    case "duplicate": profile.Add(new XElement(leaf)); break;
                    case "attribute": leaf.SetAttributeValue("extra1004", 1); break;
                    case "nested": leaf.ReplaceNodes(new XElement("value", "false")); break;
                    case "invalid": leaf.Value = "not-bool"; break;
                    case "numeric": leaf.Value = "1"; break;
                    case "missing": leaf.Remove(); break;
                    case "comment": leaf.Add(new XComment("extra1004")); break;
                    case "cdata": leaf.ReplaceNodes(new XCData("false")); break;
                    case "namespaced": leaf.Name = XName.Get(field, "urn:quality1004"); break;
                    case "namespaced-sibling": profile.Add(new XElement(XName.Get(field, "urn:quality1004"), "false")); break;
                    case "processing-instruction": leaf.Add(new XProcessingInstruction("quality1004", "extra")); break;
                }
            });
            var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
            var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
            var loaded = reader.LoadCompletionInputsWithMovementSourceExecution(owners.Capture(), new(context.WorkspaceId),
                includeLoadout: true, includeSourcebooks: true);
            Assert.AreEqual(0, owners.ActiveLeases);
            if (loaded.Value is not { } prepared)
            {
                Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, field + ":" + kind);
                continue;
            }
            var capture = RestoreObserved839(prepared);
            var review = CharacterCreationCompletionReviewService.Review(prepared.Inputs, SourcebookRequest800(prepared.Inputs),
                ruleSources: new CharacterCreationRuleSourceExecutionContext(capture, context.Queries));
            Assert.IsTrue(review.Value?.CompleteAssessment?.SatisfiesComplete != true, field + ":" + kind);
            foreach (var report in capture.Reports.Where(row => row.Scope == "profile"))
                Assert.IsTrue(report.Operations.Where(row => HasField832(row, field)).All(row =>
                    row.Disposition == D973.Unconsumed && row.ExecutionDigest is null && row.OperationResult718 is null), field + ":" + kind);
            var source = XElement.Parse(prepared.Inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml);
            var malformed = source.Elements().FirstOrDefault(row => row.Name.LocalName == field);
            var identity = QualityIdentity947(prepared.Inputs) with { LeafXmlDigest = CharacterCreationFinalizationDigest.Compute(
                malformed?.ToString(SaveOptions.DisableFormatting) ?? "absent1004") };
            foreach (var invocation in capture.Invocations)
                Assert.IsFalse(ReadPolicy1004(invocation, identity, field, out _, out _, out _, out _, out _), field + ":" + kind);
        }
    }

    [TestMethod]
    public void ProfileQuality1004_exact_binding_copies_mutated_results_and_source_drift_refuse()
    {
        using var context = Ready973.Create(includeGearReview: true, prepareSource: _ => { },
            amendSettings: profile => ConfigureQualityPolicy1004(profile, 2, 3, true, true, true, true));
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var request = CostRequest973(prepared.Inputs, "both");
        var observed = ObserveSources839(context, prepared, request);
        var invocation = observed.Capture.Invocations.Single();
        string foreign = CharacterCreationFinalizationDigest.Compute("foreign-quality1004");
        var consumers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in prepared.Inputs.EffectivePolicy!.Sources)
            if (invocation.TryGetCompletedConsumer(source.SourceId, out var consumer)) consumers.TryAdd(source.SourceId, consumer);
        var copy = new CharacterCreationRuleSourceScopeInvocation(invocation.PolicyDigest, invocation.CaptureDigest,
            invocation.RequestDigest, invocation.GraphDigest, consumers, invocation.CompiledEffects);
        foreach (var property in typeof(CharacterCreationRuleSourceScopeInvocation).GetProperties().Where(row => row.CanWrite))
            property.SetValue(copy, property.GetValue(invocation));
        Assert.AreEqual("{}", JsonSerializer.Serialize(invocation));
        AssertNotSupported957(() => JsonSerializer.Deserialize<CharacterCreationRuleSourceScopeInvocation>("{}"));
        foreach (string field in QualityPolicyFields1004)
        {
            var identity = PolicyIdentity1004(prepared.Inputs, field);
            Assert.IsTrue(ReadPolicy1004(invocation, identity, field, out var actual, out var cost, out _, out _, out var digest));
            string expected = JsonSerializer.Serialize(actual);
            foreach (var changed in new[] { identity with { ProfileId = foreign }, identity with { ProfileInputsDigest = foreign },
                identity with { ProfileXmlDigest = foreign }, identity with { LeafXmlDigest = foreign } })
                Assert.IsFalse(ReadPolicy1004(invocation, changed, field, out _, out _, out _, out _, out _));
            Assert.IsFalse(ReadPolicy1004(invocation, identity, "unknownquality1004", out _, out _, out _, out _, out _));
            Assert.IsFalse(ReadPolicy1004(copy, identity, field, out _, out _, out _, out _, out _));
            foreach (string propertyName in new[] { nameof(invocation.Skills718), nameof(invocation.CreationDraft718), nameof(invocation.WorkspaceId718) })
            {
                var property = typeof(CharacterCreationRuleSourceScopeInvocation).GetProperty(propertyName)!;
                object? before = property.GetValue(invocation);
                try
                {
                    property.SetValue(invocation, null);
                    Assert.IsFalse(ReadPolicy1004(invocation, identity, field, out var refused, out var refusedCost,
                        out bool refusedValue, out bool refusedEmpty, out var refusedDigest));
                    Assert.IsNull(refused); Assert.IsNull(refusedCost); Assert.IsNull(refusedDigest);
                    Assert.IsFalse(refusedValue); Assert.IsFalse(refusedEmpty);
                }
                finally { property.SetValue(invocation, before); }
            }
            Assert.IsTrue(((IList<CharacterCreationQualitySelection>)actual!.Selections).IsReadOnly);
            typeof(CharacterCreationQualitySelection).GetProperty(nameof(CharacterCreationQualitySelection.SourceAnchorIds))!
                .SetValue(actual.Selections[0], new[] { "foreign1004" });
            actual.PositiveQualityBudget.GetType().GetProperty("Used")!.SetValue(actual.PositiveQualityBudget, 999);
            typeof(CharacterCreationQualityCostPolicy).GetProperty(nameof(CharacterCreationQualityCostPolicy.DoublePositiveExcess))!
                .SetValue(cost, false);
            Assert.IsTrue(ReadPolicy1004(invocation, identity, field, out var reread, out var rereadCost, out _, out _, out var rereadDigest));
            Assert.AreEqual(expected, JsonSerializer.Serialize(reread)); Assert.AreEqual(digest, rereadDigest);
            Assert.IsTrue(rereadCost!.DoublePositiveExcess);
        }
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(prepared.Inputs, request);
        var budget = observed.Review.UpstreamBudget!;
        var tampered = observed.Review with { UpstreamBudget = budget with { Qualities = budget.Qualities with
            { Qualities = budget.Qualities.Qualities with { Selections = [], KarmaRemaining = 999 } } } };
        Assert.IsNull(CharacterCreationCompleteProfileSettingsExecution.QualityKarmaLimitExecution947.ExecuteFull(
            prepared.Inputs, tampered, authority, out _, default));
        var cold = ObserveSources839(context, prepared, request);
        Assert.AreEqual(JsonSerializer.Serialize(observed.Capture.Reports), JsonSerializer.Serialize(cold.Capture.Reports));
        string path = Path.Combine(context.Directory, "source", "data",
            CharacterCreationBootstrapProfiles.SettingsSourceFile(prepared.Inputs.EffectivePolicy.SettingsProfileId));
        File.AppendAllText(path, "\n<!-- source drift1004 -->");
        Assert.IsTrue(((ICharacterCreationRuleSourceCapture)context.Resolver).TryCaptureSourcebookRuleSources(
            context.Store.Get(context.WorkspaceId).Value!.Document.Content, out var changedSource));
        var resolution = changedSource!.CreateExecutionResolution()!;
        Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsTrue(resolution.EnableCandidateSourcebookSelection());
        Assert.AreEqual(0, resolution.ExecuteRemainingSourceScopes(invocation).Count);
    }

    private static void ConfigureQualityPolicy1004(XElement profile, int multiplier, int limit,
        bool positive, bool doubled, bool negative, bool noBonus)
    {
        profile.Element("karmacost")!.SetElementValue("karmaquality", multiplier);
        profile.SetElementValue("qualitykarmalimit", limit);
        foreach (var pair in QualityPolicyFields1004.Zip(new[] { positive, doubled, negative, noBonus }))
            profile.SetElementValue(pair.First, pair.Second);
        // The current spelling wins over a conflicting legacy fallback, which this
        // packet deliberately does not account as an executed current-profile leaf.
        profile.SetElementValue("exceednegativequalitieslimit", !noBonus);
        profile.SetElementValue("unknownquality1004", "retained");
    }

    private static QualityIdentity947Value PolicyIdentity1004(CharacterCreationCompletionReviewInputs inputs, string field)
        => QualityIdentity947(inputs) with { LeafXmlDigest = CharacterCreationFinalizationDigest.Compute(
            XElement.Parse(inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml, LoadOptions.PreserveWhitespace)
                .Element(field)!.ToString(SaveOptions.DisableFormatting)) };

    private static bool ReadPolicy1004(CharacterCreationRuleSourceScopeInvocation invocation, QualityIdentity947Value identity,
        string field, out CharacterCreationQualitiesCompletionEvaluation? result, out CharacterCreationQualityCostPolicy? cost,
        out bool value, out bool empty, out string? digest)
        => invocation.TryGetExecutedProfileQualityPolicy1004(identity.ProfileId, identity.ProfileInputsDigest,
            identity.ProfileXmlDigest, field, identity.LeafXmlDigest, out result, out cost, out value, out empty, out digest);

    private static void AssertQualityPolicy1004(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationRuleSourceScopeResult report, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationQualitiesCompletionEvaluation expected, bool empty)
    {
        var source = XElement.Parse(inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml);
        foreach (string field in QualityPolicyFields1004)
        {
            var identity = PolicyIdentity1004(inputs, field);
            Assert.IsTrue(ReadPolicy1004(invocation, identity, field, out var actual, out var cost, out bool value,
                out bool noTarget, out var digest), field);
            Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
            Assert.AreEqual(inputs.EffectivePolicy!.Consumers!.Qualities.CostPolicy, cost);
            Assert.AreEqual(bool.Parse(source.Element(field)!.Value), value);
            Assert.AreEqual(empty, noTarget);
            Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(digest));
            var operations = QualityField947(report, identity.ProfileId, field);
            Assert.AreEqual(2, operations.Length, field);
            foreach (var row in operations)
            {
                Assert.AreEqual(D973.Compiled, row.Disposition);
                Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(row.ExecutionDigest));
                Assert.AreEqual(value, bool.Parse(row.ConsumedValue718!));
                using var payload = JsonDocument.Parse(row.OperationResult718!);
                Assert.AreEqual("canonical-quality-limit-policy", payload.RootElement.GetProperty("Operation").GetString());
                Assert.AreEqual(field, payload.RootElement.GetProperty("Field").GetString());
                Assert.AreEqual(value, payload.RootElement.GetProperty("Value").GetBoolean());
                Assert.AreEqual(JsonSerializer.Serialize(cost), payload.RootElement.GetProperty("CostPolicy").GetRawText());
                Assert.AreEqual(JsonSerializer.Serialize(actual), payload.RootElement.GetProperty("Result").GetRawText());
                Assert.AreEqual(empty, payload.RootElement.GetProperty("NoTarget").GetBoolean());
                Assert.AreEqual(digest, payload.RootElement.GetProperty("Execution").GetString());
            }
        }
        Assert.IsFalse(report.IsComplete);
    }
}
