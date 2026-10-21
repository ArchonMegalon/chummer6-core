using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready971 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using D971 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(false, "INT")]
    [DataRow(false, "LOG")]
    [DataRow(true, "INT")]
    [DataRow(true, "LOG")]
    public void ProfileKnowledge971_default_nonempty_budget_uses_proposed_attributes_and_cold_execution(bool v4, string attribute)
    {
        using var context = Ready971.Create(includeGearReview: true, playerChosenAttributeIds: [],
            wrapResolver: v4 ? V4Sources905 : null,
            amendSettings: profile => profile.Add(new XElement("unknown971", 1)));
        var prepared = PrepareProfileSourcebooks935(context, v4, books: true);
        var inputs = prepared.Inputs;
        Assert.AreEqual("settings-all-sources.xml", CharacterCreationBootstrapProfiles.SettingsSourceFile(inputs.EffectivePolicy!.SettingsProfileId));
        Assert.IsTrue(Profile962(inputs).Elements().Count() >= 100);
        var files = Files962(context);
        string? previous = null;
        foreach (int levels in new[] { 0, 1 })
        {
            var request = KnowledgeRequest971(inputs, attribute, levels, 3);
            var full = ObserveSources839(context, prepared, request);
            Assert.AreEqual(levels + 1, full.Review.Skills!.Attributes.Attributes.Single(row => row.AttributeId == attribute).Current);
            AssertKnowledge971(inputs, request, full.Review, full.Capture.Invocations.Single(),
                full.Capture.Reports.Single(row => row.Scope == "profile"), 4 + levels * 2, 3, 0);
            Assert.IsTrue(ReadKnowledge971(full.Capture.Invocations.Single(), inputs, out _, out _, out var digest));
            if (previous is not null) Assert.AreNotEqual(previous, digest);
            previous = digest;
            var cold = ObserveSources839(context, prepared, request);
            Assert.IsFalse(ReferenceEquals(full.Capture.Invocations.Single(), cold.Capture.Invocations.Single()));
            AssertKnowledge971(inputs, request, cold.Review, cold.Capture.Invocations.Single(),
                cold.Capture.Reports.Single(row => row.Scope == "profile"), 4 + levels * 2, 3, 0);
            Assert.IsTrue(ReadKnowledge971(cold.Capture.Invocations.Single(), inputs, out _, out _, out var coldDigest));
            Assert.AreEqual(digest, coldDigest);
        }
        AssertFiles962(context, files);
    }

    [TestMethod]
    [DataRow("({INTUnaug} + {LOGUnaug}) * 3", "INT", 1, 9, 6, 0, false)]
    [DataRow("({INTUnaug} + {LOGUnaug}) * 3", "LOG", 1, 9, 6, 0, true)]
    [DataRow("({INTUnaug} + {LOGUnaug}) div 2 + 0.1", "INT", 1, 2, 2, 4, false)]
    [DataRow("({INTUnaug} + {LOGUnaug}) div 2 + 0.1", "INT", 2, 3, 3, 3, false)]
    [DataRow("({INT} + {LOG}) div 2 + 0.1", "LOG", 1, 2, 2, 4, true)]
    [DataRow("{CHAUnaug} + {INT} + {LOGUnaug}", "CHA", 2, 5, 5, 1, false)]
    public void ProfileKnowledge982_supported_house_expressions_use_proposed_attributes_and_canonical_accounting(
        string expression, string attribute, int levels, int total, int used, int overflow, bool v4)
    {
        using var context = Ready971.Create(includeGearReview: true, playerChosenAttributeIds: [],
            wrapResolver: v4 ? V4Sources905 : null,
            amendSettings: profile => profile.SetElementValue("knowledgepointsexpression", expression));
        var prepared = PrepareProfileSourcebooks935(context, v4, books: true);
        var inputs = prepared.Inputs;
        var files = Files962(context);
        string original = JsonSerializer.Serialize(inputs.Skills);
        Assert.AreEqual(expression, inputs.Skills.Authority.KnowledgePointsExpression);
        var request = KnowledgeRequest971(inputs, attribute, levels, 6);
        var observed = ObserveSources839(context, prepared, request);
        Assert.AreEqual(levels + 1, observed.Review.Skills!.Attributes.Attributes.Single(row => row.AttributeId == attribute).UnaugmentedCurrent);
        AssertKnowledge971(inputs, request, observed.Review, observed.Capture.Invocations.Single(),
            observed.Capture.Reports.Single(row => row.Scope == "profile"), total, used, overflow);
        Assert.AreEqual((decimal)overflow, observed.Review.Skills.ActiveSkillPointBudget.Used);
        Assert.IsTrue(ReadKnowledge971(observed.Capture.Invocations.Single(), inputs, out _, out _, out var digest));
        var cold = ObserveSources839(context, prepared, request);
        AssertKnowledge971(inputs, request, cold.Review, cold.Capture.Invocations.Single(),
            cold.Capture.Reports.Single(row => row.Scope == "profile"), total, used, overflow);
        Assert.IsTrue(ReadKnowledge971(cold.Capture.Invocations.Single(), inputs, out _, out _, out var replayDigest));
        Assert.AreEqual(digest, replayDigest);
        Assert.AreEqual(original, JsonSerializer.Serialize(inputs.Skills), "Proposed attributes must not rewrite the original budget or inputs.");
        AssertFiles962(context, files);
    }

    [TestMethod]
    [DataRow("{INTUnaug} * 1000000000", "INT", 2)]
    [DataRow("1 div (2 - {LOGUnaug})", "LOG", 1)]
    public void ProfileKnowledge982_proposed_attribute_overflow_or_division_by_zero_cannot_register_execution(
        string expression, string attribute, int levels)
    {
        using var context = Ready971.Create(includeGearReview: true, playerChosenAttributeIds: [],
            amendSettings: profile => profile.SetElementValue("knowledgepointsexpression", expression));
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var inputs = prepared.Inputs;
        var files = Files962(context);
        string original = JsonSerializer.Serialize(inputs.Skills);
        var request = KnowledgeRequest971(inputs, attribute, levels, 3);
        var actual = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills,
            request.Selections.AttributeAllocations, request.Selections.SkillAllocations,
            request.Selections.GroupAllocations, request.Selections.SkillKarmaPurchases);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, actual.Outcome);
        Assert.IsTrue(actual.Blockers.Contains(CharacterCreationSkillsBlockers.AuthorityUnavailable));
        var observed = ObserveSources839(context, prepared, request);
        Assert.IsTrue(observed.Review.CompleteAssessment?.SatisfiesComplete != true);
        foreach (var invocation in observed.Capture.Invocations)
            Assert.IsFalse(ReadKnowledge971(invocation, inputs, out _, out _, out _));
        foreach (var report in observed.Capture.Reports.Where(row => row.Scope == "profile"))
            Assert.IsTrue(KnowledgeOperations971(report, inputs).All(row => row.Disposition == D971.Unconsumed));
        Assert.AreEqual(original, JsonSerializer.Serialize(inputs.Skills));
        AssertFiles962(context, files);
    }

    [TestMethod]
    [DataRow(0, 0, 0)]
    [DataRow(6, 4, 2)]
    public void ProfileKnowledge971_inactive_and_overflow_allocations_use_real_canonical_budgets(int rating, int used, int overflow)
    {
        using var context = Ready971.Create(includeGearReview: true, playerChosenAttributeIds: []);
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var request = KnowledgeRequest971(prepared.Inputs, "INT", 0, rating);
        var full = ObserveSources839(context, prepared, request);
        AssertKnowledge971(prepared.Inputs, request, full.Review, full.Capture.Invocations.Single(),
            full.Capture.Reports.Single(row => row.Scope == "profile"), 4, used, overflow);
        Assert.AreEqual((decimal)overflow, full.Review.Skills!.ActiveSkillPointBudget.Used);
    }

    [TestMethod]
    public void ProfileKnowledge971_combined_pool_overspend_cannot_register_execution()
    {
        using var context = Ready971.Create(includeGearReview: true, playerChosenAttributeIds: []);
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var inputs = prepared.Inputs;
        var request = KnowledgeRequest971(inputs, "INT", 0, 0);
        // Knowledge overflow consumes active points. Exceed that real combined
        // allowance with ordinary catalog skills, rather than inventing a cap.
        int cap = inputs.Skills.Authority.MaxKnowledgeSkillRatingCreate;
        int count = (inputs.Skills.SelectedActiveSkillPoints + 4) / cap + 1;
        var knowledge = inputs.Skills.Authority.KnowledgeSkills.Where(row => !row.CanBeNativeLanguage).Take(count).ToArray();
        Assert.AreEqual(count, knowledge.Length);
        request = request with { Selections = request.Selections with
        { SkillAllocations = [.. request.Selections.SkillAllocations,
            .. knowledge.Select(row => new CharacterCreationSkillAllocation(row.SourceSkillId, CharacterCreationSkillKinds.Knowledge, cap, null, false))] } };
        var actual = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills,
            request.Selections.AttributeAllocations, request.Selections.SkillAllocations,
            request.Selections.GroupAllocations, request.Selections.SkillKarmaPurchases);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, actual.Outcome);
        Assert.IsTrue(actual.Blockers.Contains(CharacterCreationSkillsBlockers.ActiveBudgetExceeded));
        var observed = ObserveSources839(context, prepared, request);
        Assert.IsTrue(observed.Review.CompleteAssessment?.SatisfiesComplete != true);
        foreach (var invocation in observed.Capture.Invocations)
            Assert.IsFalse(ReadKnowledge971(invocation, inputs, out _, out _, out _));
        foreach (var report in observed.Capture.Reports.Where(row => row.Scope == "profile"))
            Assert.IsTrue(KnowledgeOperations971(report, inputs).All(row => row.Disposition == D971.Unconsumed));
    }

    [TestMethod]
    [DataRow("{Unknown971} * 2")]
    [DataRow("({INTUnaug} +")]
    public void ProfileKnowledge971_unknown_and_malformed_expressions_do_not_reuse_default_execution(string expression)
    {
        // A default-expression execution cannot be reused for changed source.
        // Direct canonical rejection is covered by the Knowledge982 skills tests.
        using var context = Ready971.Create(includeGearReview: true, playerChosenAttributeIds: [], prepareSource: _ => { });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var full = ObserveSources839(context, prepared, KnowledgeRequest971(prepared.Inputs, "INT", 0, 3));
        var files = Files962(context);
        string root = Path.Combine(context.Directory, "source");
        EditProfile800(root, profile => profile.SetElementValue("knowledgepointsexpression", expression));
        var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
        using var scope = resolver.CreateOperationScope();
        var current = scope.TryCreateContext(context.Store.Get(context.WorkspaceId).Value!.Document.Content)!;
        Assert.IsTrue(current.TryResolveCreationSkillsAuthority(out var authority));
        Assert.AreEqual(expression, authority.KnowledgePointsExpression);
        // Structural source resolution does not claim the expression was evaluated.
        var changed = Profile962(prepared.Inputs);
        changed.SetElementValue("knowledgepointsexpression", expression);
        var invocation = full.Capture.Invocations.Single();
        Assert.IsFalse(invocation.TryGetExecutedKnowledgePoints971(prepared.Inputs.EffectivePolicy!.SettingsProfileId,
            prepared.Inputs.EffectivePolicy.ProfileInputsDigest, expression, Xml962(changed),
            Xml962(changed.Element("knowledgepointsexpression")!), out _, out _, out _));
        AssertFiles962(context, files);
    }

    [TestMethod]
    [DataRow("comment")]
    [DataRow("attribute")]
    [DataRow("duplicate")]
    [DataRow("sibling")]
    public void ProfileKnowledge971_current_source_recapture_rejects_stale_or_nonplain_profile(string change)
    {
        using var context = Ready971.Create(includeGearReview: true, playerChosenAttributeIds: [], prepareSource: _ => { });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var full = ObserveSources839(context, prepared, KnowledgeRequest971(prepared.Inputs, "LOG", 1, 3));
        var invocation = full.Capture.Invocations.Single();
        Assert.IsTrue(ReadKnowledge971(invocation, prepared.Inputs, out _, out _, out _));
        var files = Files962(context);
        EditProfile800(Path.Combine(context.Directory, "source"), profile =>
        {
            var leaf = profile.Element("knowledgepointsexpression")!;
            switch (change)
            {
                case "comment": leaf.Add(new XComment("unexecuted971")); break;
                case "attribute": leaf.SetAttributeValue("unknown971", 1); break;
                case "duplicate": profile.Add(new XElement(leaf)); break;
                case "sibling": profile.Add(new XElement("unknown971", 1)); break;
            }
        });
        var producer = (ICharacterCreationRuleSourceCapture)context.Resolver;
        bool captured = producer.TryCaptureSourcebookRuleSources(context.Store.Get(context.WorkspaceId).Value!.Document.Content, out var changed);
        if (change == "sibling") Assert.IsTrue(captured);
        if (captured)
        {
            Assert.AreNotEqual(prepared.Inputs.RuleSourceCaptureDigest, changed!.IntegrityDigest);
            var resolution = changed.CreateExecutionResolution()!;
            Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
            Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
            Assert.IsTrue(resolution.EnableCandidateSourcebookSelection());
            Assert.AreEqual(0, resolution.ExecuteRemainingSourceScopes(invocation).Count);
        }
        AssertFiles962(context, files);
    }

    [TestMethod]
    public void ProfileKnowledge971_copies_deserialized_results_and_mutated_identity_do_not_transfer_execution()
    {
        using var context = Ready971.Create(includeGearReview: true, playerChosenAttributeIds: []);
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var inputs = prepared.Inputs;
        var request = KnowledgeRequest971(inputs, "INT", 1, 3);
        var observed = ObserveSources839(context, prepared, request);
        var invocation = observed.Capture.Invocations.Single();
        Assert.IsTrue(ReadKnowledge971(invocation, inputs, out var budget, out _, out var digest));
        Assert.IsTrue(ReadKnowledge971(invocation, inputs, out var detached, out _, out _));
        Assert.IsFalse(ReferenceEquals(budget, detached));
        Assert.AreEqual("{}", JsonSerializer.Serialize(invocation));
        var consumers = new Dictionary<string, string>();
        foreach (var source in inputs.EffectivePolicy!.Sources)
            if (invocation.TryGetCompletedConsumer(source.SourceId, out var value)) consumers.TryAdd(source.SourceId, value);
        var copy = new CharacterCreationRuleSourceScopeInvocation(invocation.PolicyDigest, invocation.CaptureDigest,
            invocation.RequestDigest, invocation.GraphDigest, consumers, invocation.CompiledEffects)
        {
            CreationDraft718 = invocation.CreationDraft718, Attributes718 = invocation.Attributes718,
            AttributePolicy718 = invocation.AttributePolicy718,
            Skills718 = JsonSerializer.Deserialize<CharacterCreationAttributeSkillsPreview>(JsonSerializer.Serialize(invocation.Skills718)),
            WorkspaceId718 = invocation.WorkspaceId718, ResourcesExecution718 = invocation.ResourcesExecution718,
            LifestyleAuthority718 = invocation.LifestyleAuthority718, Lifestyles718 = invocation.Lifestyles718,
            StartingLifestyleId718 = invocation.StartingLifestyleId718, SourceExecution832 = invocation.SourceExecution832,
            CompiledMovement832 = invocation.CompiledMovement832, PhysicalAuthority886 = invocation.PhysicalAuthority886,
            PhysicalInitiative886 = invocation.PhysicalInitiative886
        };
        Assert.IsFalse(ReadKnowledge971(copy, inputs, out _, out _, out _));
        var resolution = observed.Capture.CreateExecutionResolution(inputs.RuleSourceExecutionSchema)!;
        Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsTrue(resolution.EnableCandidateSourcebookSelection());
        Assert.IsTrue(KnowledgeOperations971(resolution.ExecuteRemainingSourceScopes(copy).Single(row => row.Scope == "profile"), inputs)
            .All(row => row.Disposition == D971.Unconsumed));
        foreach (string propertyName in new[] { nameof(invocation.WorkspaceId718), nameof(invocation.Attributes718),
            nameof(invocation.Skills718), nameof(invocation.CreationDraft718) })
        {
            var property = typeof(CharacterCreationRuleSourceScopeInvocation).GetProperty(propertyName)!;
            object? saved = property.GetValue(invocation);
            try
            {
                property.SetValue(invocation, null);
                Assert.IsFalse(ReadKnowledge971(invocation, inputs, out _, out _, out _), propertyName);
            }
            finally { property.SetValue(invocation, saved); }
        }
        var resultProperty = typeof(CharacterCreationRuleSourceScopeInvocation).GetProperty(nameof(invocation.Skills718))!;
        var savedResult = invocation.Skills718!;
        try
        {
            resultProperty.SetValue(invocation, savedResult with
            { KnowledgeSkillPointBudget = savedResult.KnowledgeSkillPointBudget with { Remaining = budget!.Remaining + 1 } });
            Assert.IsFalse(ReadKnowledge971(invocation, inputs, out _, out _, out _));
        }
        finally { resultProperty.SetValue(invocation, savedResult); }
        var profile = Profile962(inputs);
        string foreign = CharacterCreationFinalizationDigest.Compute("foreign971");
        string[] identities = [inputs.EffectivePolicy.SettingsProfileId, inputs.EffectivePolicy.ProfileInputsDigest,
            inputs.Skills.Authority.KnowledgePointsExpression, Xml962(profile), Xml962(profile.Element("knowledgepointsexpression")!)];
        for (int i = 0; i < identities.Length; i++)
        {
            var changed = identities.ToArray(); changed[i] = foreign;
            Assert.IsFalse(invocation.TryGetExecutedKnowledgePoints971(changed[0], changed[1], changed[2], changed[3], changed[4],
                out _, out _, out _));
        }
        Assert.IsTrue(ReadKnowledge971(invocation, inputs, out _, out _, out var unchanged));
        Assert.AreEqual(digest, unchanged);
        foreach (var historical in new[]
        {
            request with { ProfileSettingsExecutionSchema = null },
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.None },
            request with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV3 }
        })
        {
            var capture = RestoreObserved839(prepared);
            CharacterCreationCompletionReviewService.Review(inputs, historical,
                ruleSources: new CharacterCreationRuleSourceExecutionContext(capture, context.Queries));
            foreach (var old in capture.Invocations) Assert.IsFalse(ReadKnowledge971(old, inputs, out _, out _, out _));
        }
    }

    [TestMethod]
    public void ProfileKnowledge971_selected_canonical_reexecution_accounts_only_expression_and_refuses_Complete_witness()
    {
        using var context = Ready971.Create(includeGearReview: true, playerChosenAttributeIds: []);
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var request = KnowledgeRequest971(prepared.Inputs, "LOG", 1, 3);
        var full = ObserveSources839(context, prepared, request);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var publicCapture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, prepared.Inputs, request,
            ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, publicCapture.Outcome);
        Assert.IsNull(publicCapture.Value);
        // Exercise the same private selected boundary used by existing source
        // tests. The real Complete request and partial full review stay intact.
        var observed = RestoreObserved839(prepared);
        var method = typeof(CharacterCreationSelectedCompletionEvaluator).GetMethod("CaptureReviewed", BindingFlags.Static | BindingFlags.NonPublic)!;
        var selected = (CharacterCreationFoundationResult<CharacterCreationSelectedCompletionWitness>)method.Invoke(null,
            [original, bootstrap, prepared.Inputs, request, full.Review, CancellationToken.None,
                new CharacterCreationRuleSourceExecutionContext(observed, context.Queries), null])!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, selected.Outcome);
        Assert.IsNull(selected.Value);
        Assert.IsTrue(selected.Blockers.Contains(CharacterCreationEffectivePolicyContext.CoverageUnavailable));
        AssertKnowledge971(prepared.Inputs, request, full.Review, observed.Invocations.Single(),
            observed.Reports.Single(row => row.Scope == "profile"), 6, 3, 0);
        Assert.IsTrue(ReadKnowledge971(full.Capture.Invocations.Single(), prepared.Inputs, out _, out _, out var fullDigest));
        Assert.IsTrue(ReadKnowledge971(observed.Invocations.Single(), prepared.Inputs, out _, out _, out var selectedDigest));
        Assert.AreEqual(fullDigest, selectedDigest);
    }

    private static CharacterCreationCompletionReviewRequest KnowledgeRequest971(CharacterCreationCompletionReviewInputs inputs,
        string attribute, int levels, int rating)
    {
        var request = GroupRequest940(inputs);
        var skill = inputs.Skills.Authority.KnowledgeSkills.First(row => !row.CanBeNativeLanguage);
        return request with { Selections = request.Selections with
        {
            AttributeAllocations = [.. request.Selections.AttributeAllocations.Where(row => row.AttributeId != attribute), new(attribute, levels, 0)],
            ChangeableAttributeIds = [attribute],
            SkillAllocations = rating == 0 ? request.Selections.SkillAllocations
                : [.. request.Selections.SkillAllocations, new(skill.SourceSkillId, CharacterCreationSkillKinds.Knowledge, rating, null, false)]
        } };
    }

    private static bool ReadKnowledge971(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompletionReviewInputs inputs, out CharacterCreationBudgetState? budget, out int overflow, out string? digest)
    {
        var profile = Profile962(inputs);
        return invocation.TryGetExecutedKnowledgePoints971(inputs.EffectivePolicy!.SettingsProfileId, inputs.EffectivePolicy.ProfileInputsDigest,
            inputs.Skills.Authority.KnowledgePointsExpression, Xml962(profile), Xml962(profile.Element("knowledgepointsexpression")!),
            out budget, out overflow, out digest);
    }

    private static CharacterCreationSourceOperation[] KnowledgeOperations971(CharacterCreationRuleSourceScopeResult report,
        CharacterCreationCompletionReviewInputs inputs)
    {
        string prefix = SelectedProfilePath935(report, inputs.EffectivePolicy!.SettingsProfileId) + "/knowledgepointsexpression[";
        return report.Operations.Where(row => row.Path.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
    }

    private static void AssertKnowledge971(CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        CharacterCreationCompletionReview review, CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationRuleSourceScopeResult report, int total, int used, int overflow)
    {
        Assert.AreEqual(true, review.CompleteAssessment!.MechanicalSucceeded);
        Assert.IsTrue(ReadKnowledge971(invocation, inputs, out var budget, out int actualOverflow, out var digest));
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(digest));
        Assert.IsTrue(budget!.IsExact);
        Assert.AreEqual((decimal)total, budget.Total);
        Assert.AreEqual((decimal)used, budget.Used);
        Assert.AreEqual((decimal)(total - used), budget.Remaining);
        Assert.AreEqual(overflow, actualOverflow);
        Assert.AreEqual(0, budget.Blockers.Count);
        Assert.AreEqual(JsonSerializer.Serialize(review.Skills!.KnowledgeSkillPointBudget), JsonSerializer.Serialize(budget));
        Assert.AreEqual(overflow, review.Skills.KnowledgePointOverflowToActive);
        Assert.AreEqual(inputs.EffectivePolicy!.PolicyDigest, invocation.PolicyDigest);
        Assert.AreEqual(inputs.RuleSourceCaptureDigest, invocation.CaptureDigest);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(request), invocation.RequestDigest);
        Assert.AreEqual(inputs.OriginalCharacter!.WorkspaceId, invocation.WorkspaceId718);
        var operations = KnowledgeOperations971(report, inputs);
        Assert.AreEqual(2, operations.Length, "Account only for the exact leaf and text.");
        Assert.IsTrue(operations.All(row => row.Disposition == D971.Compiled
            && CharacterCreationFinalizationDigest.IsCanonical(row.ExecutionDigest)
            && row.OperationResult718!.Contains(digest!, StringComparison.Ordinal)));
        Assert.IsFalse(report.IsComplete);
        Assert.IsTrue(report.Operations.Any(row => row.Disposition == D971.Unconsumed));
        Assert.IsTrue(report.Operations.Where(row => HasField832(row, "unknown971"))
            .All(row => row.Disposition == D971.Unconsumed && row.ExecutionDigest is null));
        Assert.IsFalse(review.CompleteAssessment.SatisfiesComplete);
        AssertSource696(inputs, review.CompleteAssessment, "profile", CharacterCreationCompleteCoverage.Unresolved);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved, ErrataDomain788(review.CompleteAssessment).Coverage);
        Assert.IsTrue(review.CompleteAssessment.Obligations.Any(row => row.Domain == CharacterCreationPolicyDomain.OtherSettings
            && row.Coverage == CharacterCreationCompleteCoverage.Unresolved));
        Assert.AreEqual(CharacterCreationCompleteCoverage.Evaluated, review.CompleteAssessment.Obligations.Single(row =>
            row.Kind == CharacterCreationCompleteObligationKind.PolicyDomain && row.Domain == CharacterCreationPolicyDomain.Amendments).Coverage);
    }
}
