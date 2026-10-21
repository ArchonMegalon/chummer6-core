using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready962 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using D962 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ProfileContacts962_default_expression_uses_proposed_charisma_and_exact_nonempty_budget(bool v4)
    {
        using var context = Ready962.Create(includeGearReview: true, playerChosenAttributeIds: [],
            wrapResolver: v4 ? V4Sources905 : null,
            amendSettings: profile => profile.Add(new XElement("unknown962", 1)));
        var prepared = PrepareProfileSourcebooks935(context, v4, books: true);
        var inputs = prepared.Inputs;
        Assert.AreEqual("settings-all-sources.xml", CharacterCreationBootstrapProfiles.SettingsSourceFile(inputs.EffectivePolicy!.SettingsProfileId));
        Assert.IsTrue(Profile962(inputs).Elements().Count() >= 100, "Keep the real default profile intact.");
        Assert.AreEqual("{CHAUnaug} * 3", inputs.Contacts.Policy.ContactPointsExpression);
        var files = Files962(context);
        string? previous = null;
        foreach (int levels in new[] { 0, 1 })
        {
            // CHA 1 -> 2 is an actual request change. Both baskets are nonempty
            // and consume exactly the new allowance (3 -> 6), without Karma.
            var request = ContactsRequest962(inputs, levels, levels + 1);
            var observed = ObserveSources839(context, prepared, request);
            Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
            Assert.AreEqual(levels + 1, observed.Review.Skills!.Attributes.Attributes.Single(row => row.AttributeId == "CHA").Current);
            var budget = observed.Review.Contacts!.ContactBudget;
            AssertBudget962(budget, (levels + 1) * 3, (levels + 1) * 3, 0);
            Assert.AreEqual((int?)0, observed.Review.Contacts.AdditionalKarmaCost);
            var invocation = observed.Capture.Invocations.Single();
            AssertContactExecution962(inputs, request, observed.Review, invocation,
                observed.Capture.Reports.Single(row => row.Scope == "profile"));
            Assert.IsTrue(ReadContacts962(invocation, inputs, out _, out var digest));
            if (previous is not null) Assert.AreNotEqual(previous, digest);
            previous = digest;
            var cold = ObserveSources839(context, prepared, request);
            Assert.IsFalse(ReferenceEquals(invocation, cold.Capture.Invocations.Single()));
            Assert.IsTrue(ReadContacts962(cold.Capture.Invocations.Single(), inputs, out var replayed, out var replayDigest));
            Assert.AreEqual(digest, replayDigest);
            Assert.AreEqual(JsonSerializer.Serialize(budget), JsonSerializer.Serialize(replayed));
            Assert.AreEqual(JsonSerializer.Serialize(observed.Review.CompleteAssessment), JsonSerializer.Serialize(cold.Review.CompleteAssessment));
        }
        AssertFiles962(context, files);
    }

    [TestMethod]
    [DataRow("{CHAUnaug} * 4", 1, 8, false)]
    [DataRow("{CHAUnaug} * 4", 1, 8, true)]
    [DataRow("{CHAUnaug} div 2 + 3", 0, 4, false)]
    [DataRow("{CHAUnaug} div 2 + 3", 0, 4, true)]
    public void ProfileContacts968_supported_house_rule_expression_uses_canonical_budget(
        string expression, int levels, int expected, bool v4)
    {
        using var context = Ready962.Create(includeGearReview: true, playerChosenAttributeIds: [],
            wrapResolver: v4 ? V4Sources905 : null,
            amendSettings: profile => profile.SetElementValue("contactpointsexpression", expression));
        var prepared = PrepareProfileSourcebooks935(context, v4, books: true);
        var files = Files962(context);
        var request = ContactsRequest962(prepared.Inputs, levels, 1);
        var observed = ObserveSources839(context, prepared, request);
        Assert.AreEqual(expression, prepared.Inputs.Contacts.Policy.ContactPointsExpression);
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        AssertBudget962(observed.Review.Contacts!.ContactBudget, expected, 3, 0);
        var invocation = observed.Capture.Invocations.Single();
        AssertContactExecution962(prepared.Inputs, request, observed.Review, invocation,
            observed.Capture.Reports.Single(row => row.Scope == "profile"));
        Assert.IsTrue(ReadContacts962(invocation, prepared.Inputs, out var budget, out var digest));
        AssertBudget962(budget!, expected, 3, 0);
        var cold = ObserveSources839(context, prepared, request);
        Assert.IsTrue(ReadContacts962(cold.Capture.Invocations.Single(), prepared.Inputs, out var replayed, out var replayDigest));
        Assert.AreEqual(digest, replayDigest);
        Assert.AreEqual(JsonSerializer.Serialize(budget), JsonSerializer.Serialize(replayed));
        AssertFiles962(context, files);
    }

    [TestMethod]
    [DataRow(0, 2, 2)]
    [DataRow(1, 3, 4)]
    public void ProfileContacts962_one_point_overspend_is_exact_and_cannot_register(int levels, int connection, int loyalty)
    {
        using var context = Ready962.Create(includeGearReview: true, playerChosenAttributeIds: [], prepareSource: _ => { });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var request = ContactsRequest962(prepared.Inputs, levels, 1);
        request = request with { Selections = request.Selections with
            { Contacts = [request.Selections.Contacts.Single() with { Connection = connection, Loyalty = loyalty }] } };
        var files = Files962(context);
        var actual = CharacterCreationContactsService.PreviewCompletion(prepared.Inputs.Contacts,
            request.Selections.AttributeAllocations, request.Selections.QualityOptionIds, request.Selections.Contacts);
        Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, actual.Outcome);
        Assert.IsTrue(actual.Blockers.Contains(CharacterCreationContactsBlockers.BudgetExceeded));
        AssertBudget962(actual.Value!.ContactBudget, (levels + 1) * 3, connection + loyalty, 1);
        Assert.AreEqual((int?)0, actual.Value.AdditionalKarmaCost, "Overspend is not converted into a new Karma allowance.");
        var observed = ObserveSources839(context, prepared, request);
        Assert.IsTrue(observed.Review.CompleteAssessment?.MechanicalSucceeded != true);
        foreach (var invocation in observed.Capture.Invocations)
            Assert.IsFalse(ReadContacts962(invocation, prepared.Inputs, out _, out _));
        AssertFiles962(context, files);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("unknown")]
    [DataRow("malformed")]
    [DataRow("comment")]
    [DataRow("duplicate")]
    public void ProfileContacts962_absent_unknown_or_malformed_expression_has_no_source_execution(string kind)
    {
        using var context = Ready962.Create(includeGearReview: true, playerChosenAttributeIds: [], amendSettings: profile =>
        {
            var leaf = profile.Element("contactpointsexpression")!;
            switch (kind)
            {
                case "missing": leaf.Remove(); break;
                case "unknown": leaf.Value = "{Unknown962} * 4"; break;
                case "malformed": leaf.Value = "{CHAUnaug} * ("; break;
                case "comment": leaf.Add(new XComment("unexecuted962")); break;
                case "duplicate": profile.Add(new XElement(leaf)); break;
            }
        });
        var files = Files962(context);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithMovementSourceExecution(owners.Capture(), new(context.WorkspaceId),
            includeLoadout: true, includeSourcebooks: true);
        Assert.AreEqual(0, owners.ActiveLeases);
        if (loaded.Value is { } prepared)
        {
            var observed = ObserveSources839(context, prepared, ContactsRequest962(prepared.Inputs, 1, 1));
            Assert.IsTrue(observed.Review.CompleteAssessment?.SatisfiesComplete != true);
            foreach (var invocation in observed.Capture.Invocations)
                Assert.IsFalse(ReadContacts962(invocation, prepared.Inputs, out _, out _));
            foreach (var report in observed.Capture.Reports.Where(row => row.Scope == "profile"))
                Assert.IsTrue(ContactOperations962(report, prepared.Inputs).All(row => row.Disposition == D962.Unconsumed));
        }
        else Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome);
        AssertFiles962(context, files);
    }

    [TestMethod]
    public void ProfileContacts962_copies_public_budgets_and_changed_invocation_inputs_do_not_mint_execution()
    {
        using var context = Ready962.Create(includeGearReview: true, playerChosenAttributeIds: [], prepareSource: _ => { });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var inputs = prepared.Inputs;
        var request = ContactsRequest962(inputs, 1, 2);
        var files = Files962(context);
        var observed = ObserveSources839(context, prepared, request);
        var invocation = observed.Capture.Invocations.Single();
        Assert.IsTrue(ReadContacts962(invocation, inputs, out var budget, out var digest));
        var publicCopy = JsonSerializer.Deserialize<CharacterCreationContactBudget>(JsonSerializer.Serialize(budget))!;
        Assert.AreEqual(JsonSerializer.Serialize(budget), JsonSerializer.Serialize(publicCopy));
        Assert.IsTrue(ReadContacts962(invocation, inputs, out var detached, out _));
        Assert.IsFalse(ReferenceEquals(budget, detached));
        Assert.AreEqual("{}", JsonSerializer.Serialize(invocation));
        var consumers = new Dictionary<string, string>();
        foreach (var source in inputs.EffectivePolicy!.Sources)
            if (invocation.TryGetCompletedConsumer(source.SourceId, out var value)) consumers.TryAdd(source.SourceId, value);
        var copy = new CharacterCreationRuleSourceScopeInvocation(invocation.PolicyDigest, invocation.CaptureDigest,
            invocation.RequestDigest, invocation.GraphDigest, consumers, invocation.CompiledEffects)
        {
            CreationDraft718 = invocation.CreationDraft718, Attributes718 = invocation.Attributes718,
            AttributePolicy718 = invocation.AttributePolicy718, Skills718 = invocation.Skills718,
            WorkspaceId718 = invocation.WorkspaceId718, ResourcesExecution718 = invocation.ResourcesExecution718,
            LifestyleAuthority718 = invocation.LifestyleAuthority718, Lifestyles718 = invocation.Lifestyles718,
            StartingLifestyleId718 = invocation.StartingLifestyleId718, SourceExecution832 = invocation.SourceExecution832,
            CompiledMovement832 = invocation.CompiledMovement832, PhysicalAuthority886 = invocation.PhysicalAuthority886,
            PhysicalInitiative886 = invocation.PhysicalInitiative886
        };
        Assert.IsFalse(ReadContacts962(copy, inputs, out _, out _));
        var resolution = observed.Capture.CreateExecutionResolution(inputs.RuleSourceExecutionSchema)!;
        Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsTrue(resolution.EnableCandidateSourcebookSelection());
        Assert.IsTrue(ContactOperations962(resolution.ExecuteRemainingSourceScopes(copy).Single(row => row.Scope == "profile"), inputs)
            .All(row => row.Disposition == D962.Unconsumed));
        string foreign = CharacterCreationFinalizationDigest.Compute("foreign962");
        var profile = Profile962(inputs);
        Assert.IsFalse(invocation.TryGetExecutedContactPoints962(inputs.Contacts.Policy with { RawProfileInputsDigest = foreign },
            Xml962(profile), Xml962(profile.Element("contactpointsexpression")!), out _, out _));
        Assert.IsFalse(invocation.TryGetExecutedContactPoints962(inputs.Contacts.Policy, foreign,
            Xml962(profile.Element("contactpointsexpression")!), out _, out _));
        foreach (string field in new[] { nameof(invocation.WorkspaceId718), nameof(invocation.Attributes718), nameof(invocation.Skills718) })
        {
            var property = typeof(CharacterCreationRuleSourceScopeInvocation).GetProperty(field)!;
            object? saved = property.GetValue(invocation);
            try
            {
                property.SetValue(invocation, null);
                Assert.IsFalse(ReadContacts962(invocation, inputs, out _, out _), field);
            }
            finally { property.SetValue(invocation, saved); }
        }
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        foreach (var changed in new[]
        {
            authority with { PolicyDigest = foreign }, authority with { RequestDigest = foreign },
            authority with { InputsSnapshotDigest = foreign }, authority with { RawCharacterXmlDigest = foreign }
        })
        {
            Assert.IsNull(CharacterCreationCompleteContactsExecution.FromFull(inputs, observed.Review, changed));
            Assert.IsFalse(ReadContacts962(copy, inputs, out _, out _));
        }
        Assert.IsTrue(ReadContacts962(invocation, inputs, out _, out var unchanged));
        Assert.AreEqual(digest, unchanged);
        AssertFiles962(context, files);
    }

    [TestMethod]
    public void ProfileContacts962_source_drift_and_historical_requests_cannot_borrow_execution()
    {
        using var context = Ready962.Create(includeGearReview: true, playerChosenAttributeIds: [], prepareSource: _ => { });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var request = ContactsRequest962(prepared.Inputs, 1, 1);
        var observed = ObserveSources839(context, prepared, request);
        var invocation = observed.Capture.Invocations.Single();
        var files = Files962(context);
        foreach (var historical in new[]
        {
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.None },
            request with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV3 },
            request with { RuleSourceExecutionSchema = CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1 }
        })
        {
            var capture = RestoreObserved839(prepared);
            CharacterCreationCompletionReviewService.Review(prepared.Inputs, historical,
                ruleSources: new CharacterCreationRuleSourceExecutionContext(capture, context.Queries));
            foreach (var old in capture.Invocations) Assert.IsFalse(ReadContacts962(old, prepared.Inputs, out _, out _));
        }
        // Explicit expressions bypass this legacy shim, but even an otherwise
        // inactive flag changes current profile/capture identity. Never reuse it.
        EditProfile800(Path.Combine(context.Directory, "source"), profile =>
            profile.SetElementValue("freecontactsmultiplierenabled", true));
        var producer = (ICharacterCreationRuleSourceCapture)context.Resolver;
        Assert.IsTrue(producer.TryCaptureSourcebookRuleSources(context.Store.Get(context.WorkspaceId).Value!.Document.Content, out var changed));
        Assert.AreNotEqual(prepared.Inputs.RuleSourceCaptureDigest, changed!.IntegrityDigest);
        var resolution = changed.CreateExecutionResolution()!;
        Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsTrue(resolution.EnableCandidateSourcebookSelection());
        Assert.AreEqual(0, resolution.ExecuteRemainingSourceScopes(invocation).Count);
        AssertFiles962(context, files);
    }

    [TestMethod]
    [DataRow("{CHAUnaug} * 3")]
    [DataRow("{CHAUnaug} * 4")]
    public void ProfileContacts962_selected_canonical_reexecution_accounts_expression_but_refuses_Complete_witness(string expression)
    {
        using var context = Ready962.Create(includeGearReview: true, playerChosenAttributeIds: [], prepareSource: _ => { },
            amendSettings: profile => profile.SetElementValue("contactpointsexpression", expression));
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var request = ContactsRequest962(prepared.Inputs, 1, 2);
        var files = Files962(context);
        var full = ObserveSources839(context, prepared, request);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var publicCapture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, prepared.Inputs, request,
            ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, publicCapture.Outcome);
        Assert.IsNull(publicCapture.Value);
        // Exercise the existing private selected computation boundary using the
        // actual partial full review, unchanged Complete request and source capture.
        // Public admission above remains blocked. No None-policy witness, forged
        // assessment or reduced profile is used to manufacture a successful replay.
        var observed = RestoreObserved839(prepared);
        var method = typeof(CharacterCreationSelectedCompletionEvaluator).GetMethod("CaptureReviewed",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (CharacterCreationFoundationResult<CharacterCreationSelectedCompletionWitness>)method.Invoke(null,
            [original, bootstrap, prepared.Inputs, request, full.Review, CancellationToken.None,
                new CharacterCreationRuleSourceExecutionContext(observed, context.Queries), null])!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.IsNull(result.Value);
        Assert.IsTrue(result.Blockers.Contains(CharacterCreationEffectivePolicyContext.CoverageUnavailable),
            "Selected replay blockers: " + string.Join(", ", result.Blockers)
                + "; source invocations: " + observed.Invocations.Count
                + "; full review blockers: " + string.Join(", ", full.Review.Blockers));
        AssertContactExecution962(prepared.Inputs, request, full.Review, observed.Invocations.Single(),
            observed.Reports.Single(row => row.Scope == "profile"));
        Assert.IsTrue(ReadContacts962(full.Capture.Invocations.Single(), prepared.Inputs, out _, out var fullDigest));
        Assert.IsTrue(ReadContacts962(observed.Invocations.Single(), prepared.Inputs, out _, out var selectedDigest));
        Assert.AreEqual(fullDigest, selectedDigest);
        AssertFiles962(context, files);
    }

    private static CharacterCreationCompletionReviewRequest ContactsRequest962(CharacterCreationCompletionReviewInputs inputs,
        int levels, int count)
    {
        var request = SourcebookRequest800(inputs);
        return request with { Selections = request.Selections with
        {
            AttributeAllocations = [.. request.Selections.AttributeAllocations.Where(row => row.AttributeId != "CHA"), new("CHA", levels, 0)],
            ChangeableAttributeIds = ["CHA"],
            Contacts = Enumerable.Range(0, count).Select(index => new CharacterCreationKarmaContactSelection(Guid.NewGuid(),
                new CharacterCreationContactIdentity("Contact " + index, "Professional", "Seattle", "", "", "Human", "Other",
                    "Adult", "Professional", "Nuyen", "", "", ""), 2, 1)).ToArray()
        } };
    }

    private static XElement Profile962(CharacterCreationCompletionReviewInputs inputs)
        => XElement.Parse(inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
    private static string Xml962(XElement node) => CharacterCreationFinalizationDigest.Compute(node.ToString(SaveOptions.DisableFormatting));
    private static bool ReadContacts962(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompletionReviewInputs inputs, out CharacterCreationContactBudget? budget, out string? digest)
    {
        var profile = Profile962(inputs);
        return invocation.TryGetExecutedContactPoints962(inputs.Contacts.Policy, Xml962(profile),
            profile.Element("contactpointsexpression") is { } leaf ? Xml962(leaf) : "", out budget, out digest);
    }
    private static CharacterCreationSourceOperation[] ContactOperations962(CharacterCreationRuleSourceScopeResult report,
        CharacterCreationCompletionReviewInputs inputs)
    {
        string prefix = SelectedProfilePath935(report, inputs.EffectivePolicy!.SettingsProfileId) + "/contactpointsexpression[";
        return report.Operations.Where(row => row.Path.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
    }
    private static void AssertBudget962(CharacterCreationContactBudget budget, int total, int used, int overspend)
    {
        Assert.IsTrue(budget.IsExact);
        Assert.AreEqual(total, budget.Total);
        Assert.AreEqual(used, budget.Used);
        Assert.AreEqual(Math.Max(0, total - used), budget.Remaining);
        Assert.AreEqual(overspend, budget.Overspend);
    }
    private static void AssertContactExecution962(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request, CharacterCreationCompletionReview review,
        CharacterCreationRuleSourceScopeInvocation invocation, CharacterCreationRuleSourceScopeResult report)
    {
        Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, request.PolicyRequirement);
        Assert.IsTrue(ReadContacts962(invocation, inputs, out var budget, out var digest));
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(digest));
        Assert.AreEqual(JsonSerializer.Serialize(review.Contacts!.ContactBudget), JsonSerializer.Serialize(budget));
        Assert.AreEqual(inputs.EffectivePolicy!.PolicyDigest, invocation.PolicyDigest);
        Assert.AreEqual(inputs.RuleSourceCaptureDigest, invocation.CaptureDigest);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(request), invocation.RequestDigest);
        Assert.AreEqual(inputs.OriginalCharacter!.WorkspaceId, invocation.WorkspaceId718);
        var operations = ContactOperations962(report, inputs);
        Assert.AreEqual(2, operations.Length, "Only the explicit leaf and its text are accounted.");
        Assert.IsTrue(operations.All(row => row.Disposition == D962.Compiled
            && CharacterCreationFinalizationDigest.IsCanonical(row.ExecutionDigest)
            && row.OperationResult718!.Contains(digest!, StringComparison.Ordinal)));
        Assert.IsFalse(report.IsComplete);
        Assert.IsTrue(report.Operations.Any(row => row.Disposition == D962.Unconsumed));
        if (Profile962(inputs).Element("unknown962") is not null)
            Assert.IsTrue(report.Operations.Where(row => HasField832(row, "unknown962"))
                .All(row => row.Disposition == D962.Unconsumed && row.ExecutionDigest is null));
        Assert.IsFalse(review.CompleteAssessment!.SatisfiesComplete);
        AssertSource696(inputs, review.CompleteAssessment, "profile", CharacterCreationCompleteCoverage.Unresolved);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved, ErrataDomain788(review.CompleteAssessment).Coverage);
        Assert.IsTrue(review.CompleteAssessment.Obligations.Any(row => row.Domain == CharacterCreationPolicyDomain.OtherSettings
            && row.Coverage == CharacterCreationCompleteCoverage.Unresolved));
        var amendments = review.CompleteAssessment.Obligations.Single(row =>
            row.Kind == CharacterCreationCompleteObligationKind.PolicyDomain
            && row.Domain == CharacterCreationPolicyDomain.Amendments);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Evaluated, amendments.Coverage);
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(amendments.EvaluationDigest));
    }
    private static Dictionary<string, byte[]> Files962(Ready962 context)
        => Directory.GetFiles(context.Directory, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(context.Directory, path).StartsWith("source" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToDictionary(path => Path.GetRelativePath(context.Directory, path), File.ReadAllBytes, StringComparer.Ordinal);
    private static void AssertFiles962(Ready962 context, Dictionary<string, byte[]> before)
    {
        var after = Files962(context);
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var row in before) CollectionAssert.AreEqual(row.Value, after[row.Key], row.Key);
    }
}
