using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready940 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using D940 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, false, true)]
    [DataRow(true, true, true)]
    public void ProfileBrokenGroup940_canonical_boolean_and_final_group_scope_account_only_exact_leaf(
        bool enabled, bool operativeGroup, bool v4)
    {
        using var context = Ready940.Create(includeGearReview: true, includeSkillPurchase: operativeGroup,
            wrapResolver: v4 ? V4Sources905 : null, amendSettings: profile =>
            {
                profile.SetElementValue("usepointsonbrokengroups", enabled);
                profile.Add(new XElement("unknown940", "still-unconsumed"));
            });
        var prepared = PrepareProfileSourcebooks935(context, v4, books: true);
        var request = GroupRequest940(prepared.Inputs);
        AssertGroupCandidate952(context, prepared, request, operativeGroup);
        string before = CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!);
        var observed = ObserveSources839(context, prepared, request);
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var invocation = observed.Capture.Invocations.Single();
        Assert.AreEqual("{}", JsonSerializer.Serialize(invocation));
        var identity = GroupIdentity940(prepared.Inputs);
        Assert.IsTrue(ReadGroup940(invocation, identity, out bool actual, out bool noTarget, out var checks, out var digest));
        Assert.AreEqual(enabled, actual);
        Assert.AreEqual(!operativeGroup, noTarget);
        Assert.AreEqual(operativeGroup, checks.Any(check => check.GroupRating > 0));
        AssertFinalGroup952(prepared.Inputs, request, invocation, checks, operativeGroup);
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(digest));
        var report = observed.Capture.Reports.Single(row => row.Scope == "profile");
        AssertExecutedGroup940(report, identity.ProfileId, enabled, !operativeGroup, digest!);
        var qualityIdentity947 = QualityIdentity947(prepared.Inputs);
        Assert.IsTrue(ReadQuality947(invocation, qualityIdentity947, out var quality947, out bool qualityNoTarget947, out var qualityDigest947));
        AssertQualityLeaf947(report, identity.ProfileId, quality947!, qualityNoTarget947, qualityDigest947!);
        AssertMetadataFromInputs956(report, prepared.Inputs, invocation);
        foreach (string sibling in new[] { "unknown940" })
            AssertUnexecutedGroupField940(report, identity.ProfileId, sibling);
        Assert.IsFalse(report.IsComplete);
        AssertSource696(prepared.Inputs, observed.Review.CompleteAssessment, "profile", CharacterCreationCompleteCoverage.Unresolved);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved, ErrataDomain788(observed.Review.CompleteAssessment).Coverage);
        Assert.IsFalse(observed.Review.CompleteAssessment.SatisfiesComplete);
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void ProfileBrokenGroup952_explicit_member_preserves_exact_final_group_targets(bool enabled, bool v4)
    {
        using var context = Ready940.Create(includeGearReview: true, includeSkillPurchase: true,
            wrapResolver: v4 ? V4Sources905 : null,
            amendSettings: profile => profile.SetElementValue("usepointsonbrokengroups", enabled));
        var prepared = PrepareProfileSourcebooks935(context, v4, books: true);
        var group = prepared.Inputs.Skills.GroupAllocations.Single();
        var groupSource = prepared.Inputs.Skills.Authority.SkillGroups.Single(row => row.GroupId == group.GroupId);
        string member = CharacterCreationCompletionPlannerTests.EnabledGroupMembers(prepared.Inputs, groupSource)
            .First().SourceSkillId;
        var request = GroupRequest940(prepared.Inputs);
        Assert.IsFalse(request.Selections.SkillAllocations.Any(row => row.SourceSkillId == member));
        request = request with { Selections = request.Selections with
        {
            SkillAllocations = [.. request.Selections.SkillAllocations,
                new(member, CharacterCreationSkillKinds.Active, group.Rating, null, false)]
        } };
        AssertGroupCandidate952(context, prepared, request, operativeGroup: true);
        string before = CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!);
        var observed = ObserveSources839(context, prepared, request);
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var invocation = observed.Capture.Invocations.Single();
        var identity = GroupIdentity940(prepared.Inputs);
        Assert.IsTrue(ReadGroup940(invocation, identity, out bool actual, out bool noTarget, out var checks, out var digest));
        Assert.AreEqual(enabled, actual);
        Assert.IsFalse(noTarget);
        AssertFinalGroup952(prepared.Inputs, request, invocation, checks, operativeGroup: true);
        Assert.AreEqual(group.Rating, invocation.Skills718!.Skills.Single(row => row.SourceSkillId == member).GroupRating);
        AssertExecutedGroup940(observed.Capture.Reports.Single(row => row.Scope == "profile"),
            identity.ProfileId, enabled, noTarget: false, digest!);
        Assert.IsFalse(observed.Review.CompleteAssessment.SatisfiesComplete);
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void ProfileBrokenGroup940_settings_equality_without_completed_profile_execution_is_not_authority()
    {
        using var context = Ready940.Create(includeGearReview: true, includeSkillPurchase: true);
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        // Historical request: the actual profile-settings consumer is not opted in.
        var observed = ObserveSources839(context, prepared, SourcebookRequest800(prepared.Inputs));
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var identity = GroupIdentity940(prepared.Inputs);
        Assert.IsFalse(ReadGroup940(observed.Capture.Invocations.Single(), identity, out _, out _, out _, out _));
        AssertUnexecutedGroupField940(observed.Capture.Reports.Single(row => row.Scope == "profile"),
            identity.ProfileId, "usepointsonbrokengroups");
    }

    [TestMethod]
    [DataRow(false, "comment")]
    [DataRow(true, "comment")]
    [DataRow(false, "cdata")]
    [DataRow(true, "cdata")]
    public void ProfileBrokenGroup940_completed_canonical_group_still_refuses_nonplain_boolean_leaf(bool enabled, string nodeKind)
    {
        using var context = Ready940.Create(includeGearReview: true, includeSkillPurchase: true,
            amendSettings: profile =>
            {
                profile.SetElementValue("usepointsonbrokengroups", enabled);
                var leaf = profile.Element("usepointsonbrokengroups")!;
            if (nodeKind == "cdata") leaf.ReplaceNodes(new XCData(leaf.Value));
            else leaf.Add(new XComment("nonplain940"));
            });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var observed = ObserveSources839(context, prepared, GroupRequest940(prepared.Inputs));
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var identity = GroupIdentity940(prepared.Inputs);
        Assert.IsFalse(ReadGroup940(observed.Capture.Invocations.Single(), identity, out _, out _, out _, out _));
        AssertUnexecutedGroupField940(observed.Capture.Reports.Single(row => row.Scope == "profile"),
            identity.ProfileId, "usepointsonbrokengroups");
        Assert.IsFalse(observed.Review.CompleteAssessment.SatisfiesComplete);
    }

    [TestMethod]
    public void ProfileBrokenGroup940_borrowed_invocation_and_exact_identity_mismatches_refuse()
    {
        using var context = Ready940.Create(includeGearReview: true, includeSkillPurchase: true);
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var observed = ObserveSources839(context, prepared, GroupRequest940(prepared.Inputs));
        var invocation = observed.Capture.Invocations.Single();
        var identity = GroupIdentity940(prepared.Inputs);
        Assert.IsTrue(ReadGroup940(invocation, identity, out _, out _, out _, out _));
        string other = CharacterCreationFinalizationDigest.Compute("foreign940");
        foreach (var changed in new[]
        {
            identity with { ProfileId = Guid.NewGuid().ToString("D") },
            identity with { ProfileInputsDigest = other }, identity with { ProfileXmlDigest = other },
            identity with { LeafXmlDigest = other }
        }) Assert.IsFalse(ReadGroup940(invocation, changed, out _, out _, out _, out _));

        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            prepared.Execution.ExportPrivateInputs(), prepared.Inputs.RuleSourceCaptureDigest!, out var retained));
        var resolution = retained!.CreateExecutionResolution(prepared.Inputs.RuleSourceExecutionSchema)!;
        Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsTrue(resolution.EnableCandidateSourcebookSelection());
        foreach (string changed in new[] { "none", "request", "policy", "capture", "graph", "consumer", "skills", "draft", "workspace" })
        {
            invocation.TryGetCompletedConsumer("profile", out string consumer);
            var consumers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var source in prepared.Inputs.EffectivePolicy!.Sources)
                if (invocation.TryGetCompletedConsumer(source.SourceId, out var completed))
                    consumers.TryAdd(source.SourceId, completed);
            consumers["profile"] = changed == "consumer" ? other : consumer;
            var copy = new CharacterCreationRuleSourceScopeInvocation(
                changed == "policy" ? other : invocation.PolicyDigest,
                changed == "capture" ? other : invocation.CaptureDigest,
                changed == "request" ? other : invocation.RequestDigest,
                changed == "graph" ? other : invocation.GraphDigest,
                consumers,
                invocation.CompiledEffects)
            {
                CreationDraft718 = changed == "draft" ? null : invocation.CreationDraft718,
                Attributes718 = invocation.Attributes718, AttributePolicy718 = invocation.AttributePolicy718,
                Skills718 = changed == "skills" ? null : invocation.Skills718,
                WorkspaceId718 = changed == "workspace" ? null : invocation.WorkspaceId718,
                ResourcesExecution718 = invocation.ResourcesExecution718,
                SourceExecution832 = invocation.SourceExecution832, CompiledMovement832 = invocation.CompiledMovement832,
                PhysicalAuthority886 = invocation.PhysicalAuthority886, PhysicalInitiative886 = invocation.PhysicalInitiative886,
                LifestyleAuthority718 = invocation.LifestyleAuthority718, Lifestyles718 = invocation.Lifestyles718,
                StartingLifestyleId718 = invocation.StartingLifestyleId718
            };
            Assert.IsFalse(ReadGroup940(copy, identity, out _, out _, out _, out _), changed);
            var reports = resolution.ExecuteRemainingSourceScopes(copy);
            if (changed == "none")
                AssertUnexecutedGroupField940(reports.Single(row => row.Scope == "profile"),
                    identity.ProfileId, "usepointsonbrokengroups");
            foreach (var report in reports.Where(row => row.Scope == "profile"))
                Assert.IsTrue(report.Operations.Where(row => HasField832(row, "usepointsonbrokengroups"))
                    .All(row => row.Disposition != D940.Compiled), changed);
        }
        Assert.IsTrue(ReadGroup940(invocation, identity, out _, out _, out _, out _));
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("attribute")]
    [DataRow("nested")]
    [DataRow("numeric")]
    [DataRow("empty")]
    [DataRow("unsupported")]
    [DataRow("whitespace")]
    [DataRow("comment")]
    public void ProfileBrokenGroup940_malformed_selected_leaf_never_gains_operation(string malformed)
    {
        using var context = Ready940.Create(includeGearReview: true, prepareSource: _ => { });
        var initial = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        EditProfile800(Path.Combine(context.Directory, "source"), profile =>
        {
            var leaf = profile.Element("usepointsonbrokengroups")!;
            switch (malformed)
            {
                case "duplicate": profile.Add(new XElement(leaf)); break;
                case "attribute": leaf.SetAttributeValue("unknown940", "yes"); break;
                case "nested": leaf.ReplaceNodes(new XElement("nested940", "true")); break;
                case "numeric": leaf.Value = "1"; break;
                case "empty": leaf.Value = ""; break;
                case "unsupported": leaf.Value = "yes"; break;
                case "whitespace": leaf.Value = " true "; break;
                case "comment": leaf.Add(new XComment("unsupported scalar node")); break;
            }
        });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        IOwnerBoundCharacterCreationCompletionReader reader = new OwnerBoundCharacterCreationCompletionReader(
            context.Store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithMovementSourceExecution(owners.Capture(),
            new(context.WorkspaceId), includeLoadout: true, includeSourcebooks: true);
        Assert.AreEqual(0, owners.ActiveLeases);
        if (loaded.Value is { } changed)
        {
            var observed = ObserveSources839(context, changed, GroupRequest940(changed.Inputs));
            Assert.IsFalse(observed.Review.CompleteAssessment!.SatisfiesComplete);
            foreach (var invocation in observed.Capture.Invocations)
                Assert.IsFalse(ReadGroup940(invocation, GroupIdentity940(changed.Inputs), out _, out _, out _, out _));
            foreach (var report in observed.Capture.Reports.Where(row => row.Scope == "profile"))
                AssertUnexecutedGroupField940(report, initial.Inputs.EffectivePolicy!.SettingsProfileId, "usepointsonbrokengroups");
        }
        else Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(original),
            CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void ProfileBrokenGroup940_fresh_retained_reconstruction_reexecutes_identical_result_and_rejects_source_drift()
    {
        using var context = Ready940.Create(includeGearReview: true, includeSkillPurchase: true,
            wrapResolver: V4Sources905, prepareSource: _ => { });
        var prepared = PrepareProfileSourcebooks935(context, v4: true, books: true);
        var request = GroupRequest940(prepared.Inputs);
        byte[] bytes = prepared.Execution.ExportPrivateInputs();
        var first = ObserveSources839(context, prepared, request);
        // ObserveSources839 restores a new capture/resolution from retained bytes,
        // then runs canonical consumers again; it does not reuse a completion receipt.
        var cold = ObserveSources839(context, prepared, request);
        var identity = GroupIdentity940(prepared.Inputs);
        Assert.IsFalse(ReferenceEquals(first.Capture.Invocations.Single(), cold.Capture.Invocations.Single()));
        Assert.IsTrue(ReadGroup940(first.Capture.Invocations.Single(), identity, out _, out _, out _, out var before));
        Assert.IsTrue(ReadGroup940(cold.Capture.Invocations.Single(), identity, out _, out _, out _, out var after));
        Assert.AreEqual(before, after);
        Assert.AreEqual(JsonSerializer.Serialize(first.Capture.Reports), JsonSerializer.Serialize(cold.Capture.Reports));
        Assert.AreEqual(string.Join("\n", first.Capture.Reports.SelectMany(row => row.Operations).Select(row => row.OperationResult718)),
            string.Join("\n", cold.Capture.Reports.SelectMany(row => row.Operations).Select(row => row.OperationResult718)));
        CollectionAssert.AreEqual(bytes, prepared.Execution.ExportPrivateInputs());
        string path = Path.Combine(context.Directory, "source", "data",
            CharacterCreationBootstrapProfiles.SettingsSourceFile(identity.ProfileId));
        File.AppendAllText(path, "\n<!-- raw source drift940 -->");
        var producer = (ICharacterCreationRuleSourceCapture)context.Resolver;
        Assert.IsTrue(producer.TryCaptureSourcebookRuleSources(context.Store.Get(context.WorkspaceId).Value!.Document.Content, out var changed));
        var resolution = changed!.CreateExecutionResolution()!;
        Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsTrue(resolution.EnableCandidateSourcebookSelection());
        Assert.AreEqual(0, resolution.ExecuteRemainingSourceScopes(first.Capture.Invocations.Single()).Count);
    }

    [TestMethod]
    public void ProfileBrokenGroup946_detached_checks_and_mutated_original_projection_fail_closed()
    {
        using var context = Ready940.Create(includeGearReview: true, includeSkillPurchase: true);
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var observed = ObserveSources839(context, prepared, GroupRequest940(prepared.Inputs));
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var invocation = observed.Capture.Invocations.Single();
        var identity = GroupIdentity940(prepared.Inputs);
        Assert.IsTrue(ReadGroup940(invocation, identity, out _, out _, out var checks, out var digest));
        Assert.IsTrue(checks.Count > 0);
        var originalCheck = checks[0];
        var writableChecks = (IList<(string TargetId, int GroupRating, bool PointSpecialization)>)checks;
        Assert.IsTrue(writableChecks.IsReadOnly);
        bool refused = false;
        try { writableChecks[0] = ("foreign946", originalCheck.GroupRating + 1, originalCheck.PointSpecialization); }
        catch (NotSupportedException) { refused = true; }
        Assert.IsTrue(refused);
        Assert.IsTrue(ReadGroup940(invocation, identity, out _, out _, out var reread, out var sameDigest));
        Assert.IsFalse(ReferenceEquals(checks, reread));
        CollectionAssert.AreEqual(checks.ToArray(), reread.ToArray());
        Assert.AreEqual(digest, sameDigest);

        // Mutate an exposed canonical projection on the registered object itself.
        // A copied invocation would test only weak-table identity, not the binding.
        var projections = (IList<CharacterCreationSkillProjection>)invocation.Skills718!.Skills;
        Assert.IsTrue(projections.Count > 0);
        var originalProjection = projections[0];
        try
        {
            projections[0] = originalProjection with { GroupRating = originalProjection.GroupRating + 1 };
            Assert.IsFalse(ReadGroup940(invocation, identity, out bool enabled, out bool noTarget,
                out var rejectedChecks, out var rejectedDigest));
            Assert.IsFalse(enabled);
            Assert.IsFalse(noTarget);
            Assert.AreEqual(0, rejectedChecks.Count);
            Assert.IsNull(rejectedDigest);
            Assert.AreEqual(originalCheck, checks[0]);
        }
        finally { projections[0] = originalProjection; }
        Assert.IsTrue(ReadGroup940(invocation, identity, out _, out _, out var restored, out var restoredDigest));
        CollectionAssert.AreEqual(checks.ToArray(), restored.ToArray());
        Assert.AreEqual(digest, restoredDigest);
    }

    [TestMethod]
    public void ProfileBrokenGroup946_serialized_invocation_cannot_reconstitute_capability()
    {
        using var context = Ready940.Create(includeGearReview: true, includeSkillPurchase: true);
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var observed = ObserveSources839(context, prepared, GroupRequest940(prepared.Inputs));
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var invocation = observed.Capture.Invocations.Single();
        var identity = GroupIdentity940(prepared.Inputs);
        Assert.IsTrue(ReadGroup940(invocation, identity, out _, out _, out _, out var digest));
        string serialized = JsonSerializer.Serialize(invocation);
        Assert.AreEqual("{}", serialized);
        bool refused = false;
        try { _ = JsonSerializer.Deserialize<CharacterCreationRuleSourceScopeInvocation>(serialized); }
        catch (NotSupportedException) { refused = true; }
        Assert.IsTrue(refused, "The invocation has no public deserialization constructor.");
        Assert.IsTrue(ReadGroup940(invocation, identity, out _, out _, out _, out var unchangedDigest));
        Assert.AreEqual(digest, unchangedDigest);
    }


    private static CharacterCreationCompletionReviewRequest GroupRequest940(CharacterCreationCompletionReviewInputs inputs)
        => SourcebookRequest800(inputs) with
        { ProfileSettingsExecutionSchema = CharacterCreationCompletionReviewSchemas.ProfileSettingsExecutionV1 };

    private static void AssertGroupCandidate952(Ready940 context, CharacterCreationRuleSourcePreparation prepared,
        CharacterCreationCompletionReviewRequest request, bool operativeGroup)
    {
        var saved = new CharacterCreationSkillsService(context.Store, context.Resolver).Load(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, saved.Outcome, string.Join(",", saved.Blockers));
        Assert.IsNotNull(saved.Value!.PendingDraft);
        CollectionAssert.AreEqual(saved.Value.PendingDraft!.GroupAllocations.ToArray(),
            prepared.Inputs.Skills.GroupAllocations.ToArray(), "Preparation must retain the real saved group allocation.");
        CollectionAssert.AreEqual(prepared.Inputs.Skills.GroupAllocations.ToArray(),
            request.Selections.GroupAllocations.ToArray(), "The observed candidate must retain the prepared group allocation.");
        Assert.AreEqual(prepared.Inputs.SnapshotDigest, request.InputsSnapshotDigest);
        Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, request.PolicyRequirement);
        Assert.AreEqual(operativeGroup ? 1 : 0, request.Selections.GroupAllocations.Count);
        if (operativeGroup)
        {
            var athletics = saved.Value.Authority.SkillGroups.Single(row => row.Name == "Athletics");
            var candidate = request.Selections.GroupAllocations.Single();
            Assert.AreEqual(athletics.GroupId, candidate.GroupId);
            Assert.AreEqual(1, candidate.Rating);
            Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(athletics),
                CharacterCreationFinalizationDigest.Compute(prepared.Inputs.Skills.Authority.SkillGroups
                    .Single(row => row.GroupId == candidate.GroupId)));
        }
    }

    private static void AssertFinalGroup952(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request, CharacterCreationRuleSourceScopeInvocation invocation,
        IReadOnlyList<(string TargetId, int GroupRating, bool PointSpecialization)> checks, bool operativeGroup)
    {
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(request), invocation.RequestDigest);
        Assert.AreEqual(inputs.EffectivePolicy!.PolicyDigest, invocation.PolicyDigest);
        Assert.AreEqual(inputs.RuleSourceCaptureDigest, invocation.CaptureDigest);
        Assert.IsNotNull(invocation.Skills718);
        Assert.AreEqual(operativeGroup ? 1 : 0, invocation.Skills718!.SkillGroups.Count);
        var positive = checks.Where(row => row.GroupRating > 0).ToArray();
        if (!operativeGroup)
        {
            Assert.IsTrue(checks.Count > 0, "The no-group candidate still enumerates its real saved skills.");
            Assert.AreEqual(0, positive.Length);
            return;
        }
        var candidate = request.Selections.GroupAllocations.Single();
        var source = inputs.Skills.Authority.SkillGroups.Single(row => row.GroupId == candidate.GroupId);
        var final = invocation.Skills718.SkillGroups.Single();
        Assert.AreEqual(candidate.GroupId, final.GroupId);
        Assert.AreEqual(candidate.Rating, final.Rating);
        Assert.AreEqual("Athletics", final.Name);
        Assert.IsTrue(final.IsEnabled);
        Assert.AreEqual(0, final.Blockers.Count);
        Assert.IsTrue(final.SourceAnchorIds.Count > 0);
        CollectionAssert.AreEqual(source.MemberSkillSourceIds.ToArray(), final.MemberSkillSourceIds.ToArray());
        Assert.IsTrue(final.MemberSkillSourceIds.Count > 0);
        // The catalog retains all members, including Flight for a runner without
        // flight movement. Only admitted members can receive the group's rating.
        var eligible = CharacterCreationCompletionPlannerTests.EnabledGroupMembers(inputs, source)
            .Select(row => row.SourceSkillId).ToArray();
        Assert.IsTrue(eligible.Length > 0);
        var unavailable = source.MemberSkillSourceIds.Except(eligible, StringComparer.Ordinal).ToArray();
        Assert.IsTrue(unavailable.Length > 0, "The actual Athletics fixture must distinguish catalog from eligible membership.");
        Assert.IsFalse(positive.Any(row => unavailable.Contains(row.TargetId, StringComparer.Ordinal)),
            "Unavailable catalog members must not acquire an executed group-policy target.");
        CollectionAssert.AreEquivalent(eligible, positive.Select(row => row.TargetId).ToArray(),
            "Each eligible Athletics member must appear once under its skill identity, including implicit members.");
        Assert.IsTrue(positive.All(row => row.GroupRating == final.Rating && !row.PointSpecialization));
    }

    [TestMethod]
    public void ProfileBrokenGroup940_unresolved_siblings_do_not_mint_selected_Complete_witness()
    {
        using var context = Ready940.Create(includeGearReview: true, includeSkillPurchase: true);
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var files = Files962(context);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot
            .Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var capture = RestoreObserved839(prepared);
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, prepared.Inputs,
            GroupRequest940(prepared.Inputs), ruleSources: new CharacterCreationRuleSourceExecutionContext(capture, context.Queries));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, captured.Outcome);
        Assert.IsNull(captured.Value);
        // This is a refusal assertion, not evidence of genuine selected Complete.
        Assert.IsTrue(capture.Invocations.Count > 0);
        var identity = GroupIdentity940(prepared.Inputs);
        Assert.IsTrue(ReadGroup940(capture.Invocations.Last(), identity, out _, out _, out _, out _));
        Assert.IsTrue(ReadQuality947(capture.Invocations.Last(), QualityIdentity947(prepared.Inputs), out _, out _, out _));
        var invocation = capture.Invocations.Last();
        Assert.IsTrue(ReadContacts962(invocation, prepared.Inputs, out var budget, out var digest));
        Assert.IsNotNull(budget);
        Assert.IsTrue(budget!.IsExact);
        Assert.AreEqual(0, budget.Overspend);
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(digest));
        var profile = capture.Reports.Last(row => row.Scope == "profile");
        var contacts = ContactOperations962(profile, prepared.Inputs);
        Assert.AreEqual(2, contacts.Length, "The canonical contact operation accounts for the leaf and its text.");
        Assert.IsTrue(contacts.All(row => row.Disposition == CharacterCreationSourceOperationDisposition.Compiled
            && CharacterCreationFinalizationDigest.IsCanonical(row.ExecutionDigest)
            && row.OperationResult718!.Contains(digest!, StringComparison.Ordinal)));
        Assert.IsFalse(profile.IsComplete);
        Assert.IsTrue(ReadKnowledge971(invocation, prepared.Inputs, out var knowledgeBudget,
            out int knowledgeOverflow, out var knowledgeDigest));
        Assert.IsNotNull(knowledgeBudget);
        Assert.IsTrue(knowledgeBudget!.IsExact);
        Assert.AreEqual(0, knowledgeBudget.Blockers.Count);
        Assert.AreEqual(JsonSerializer.Serialize(invocation.Skills718!.KnowledgeSkillPointBudget),
            JsonSerializer.Serialize(knowledgeBudget));
        Assert.AreEqual(invocation.Skills718.KnowledgePointOverflowToActive, knowledgeOverflow);
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(knowledgeDigest));
        var knowledge = KnowledgeOperations971(profile, prepared.Inputs);
        Assert.AreEqual(2, knowledge.Length, "The canonical knowledge operation accounts for the leaf and its text.");
        Assert.IsTrue(knowledge.All(row => row.Disposition == CharacterCreationSourceOperationDisposition.Compiled
            && CharacterCreationFinalizationDigest.IsCanonical(row.ExecutionDigest)
            && row.OperationResult718!.Contains(knowledgeDigest!, StringComparison.Ordinal)));
        Assert.IsTrue(profile.Operations.Any(row => row.Disposition == D940.Unconsumed));
        AssertFiles962(context, files);
    }

    [TestMethod]
    [DataRow(ContentOverlayModes.ReplaceFile)]
    [DataRow(ContentOverlayModes.MergeCatalog)]
    public void ProfileBrokenGroup940_unsupported_profile_overlay_refuses_complete(string mode)
    {
        using var context = Ready940.Create(includeGearReview: true, prepareSource: _ => { });
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var initial = PrepareProfileSourcebooks935(context, v4: false, books: true);
        string root = Path.Combine(context.Directory, "source");
        string overlay = Path.Combine(context.Directory, "overlay940");
        Directory.CreateDirectory(overlay);
        string file = CharacterCreationBootstrapProfiles.SettingsSourceFile(initial.Inputs.EffectivePolicy!.SettingsProfileId);
        File.Copy(Path.Combine(root, "data", file), Path.Combine(overlay, file));
        var resolver = new FileSystemCharacterSourceDataResolver(new GroupOverlayCatalog940(root, overlay, mode));
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        IOwnerBoundCharacterCreationCompletionReader reader = new OwnerBoundCharacterCreationCompletionReader(
            context.Store, owners, resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithMovementSourceExecution(owners.Capture(),
            new(context.WorkspaceId), includeLoadout: true, includeSourcebooks: true);
        Assert.AreEqual(0, owners.ActiveLeases);
        if (loaded.Value is { } prepared)
        {
            var observed = ObserveSources839(context, prepared, GroupRequest940(prepared.Inputs));
            Assert.IsFalse(observed.Review.CompleteAssessment!.SatisfiesComplete);
            foreach (var report in observed.Capture.Reports.Where(row => row.Scope == "profile"))
                Assert.IsTrue(report.Operations.Where(row => HasField832(row, "usepointsonbrokengroups"))
                    .All(row => row.Disposition == D940.Unconsumed && row.ExecutionDigest is null));
        }
        else Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(original),
            CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    // Fixture for the existing overlay port, not an alternate consumer or ledger.
    private sealed class GroupOverlayCatalog940(string root, string overlay, string mode) : IContentOverlayCatalogService
    {
        public ContentOverlayCatalog GetCatalog() => new(Path.Combine(root, "data"), Path.Combine(root, "lang"),
            [new("group940", "Group940", overlay, overlay, overlay, 1, true, mode, "Profile overlay refusal")]);
        public IReadOnlyList<string> GetDataDirectories() => [Path.Combine(root, "data"), overlay];
        public IReadOnlyList<string> GetLanguageDirectories() => [Path.Combine(root, "lang")];
        public string ResolveDataFile(string fileName) => File.Exists(Path.Combine(overlay, fileName))
            ? Path.Combine(overlay, fileName) : Path.Combine(root, "data", fileName);
    }

    private sealed record GroupSourceIdentity940(string ProfileId, string ProfileInputsDigest, string ProfileXmlDigest, string LeafXmlDigest);
    private static GroupSourceIdentity940 GroupIdentity940(CharacterCreationCompletionReviewInputs inputs)
    {
        var policy = inputs.EffectivePolicy!;
        var profile = XElement.Parse(inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        // First is intentional for malformed duplicate-source refusal cases.
        var leaf = profile.Elements("usepointsonbrokengroups").First();
        return new(policy.SettingsProfileId, policy.ProfileInputsDigest,
            CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
            CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)));
    }

    private static bool ReadGroup940(CharacterCreationRuleSourceScopeInvocation invocation, GroupSourceIdentity940 identity,
        out bool enabled, out bool noTarget, out IReadOnlyList<(string TargetId, int GroupRating, bool PointSpecialization)> checks,
        out string? digest) => invocation.TryGetExecutedProfileBrokenGroup940(identity.ProfileId, identity.ProfileInputsDigest,
            identity.ProfileXmlDigest, identity.LeafXmlDigest, out enabled, out noTarget, out checks, out digest);

    private static CharacterCreationSourceOperation[] GroupField940(CharacterCreationRuleSourceScopeResult report, string id, string field)
        => report.Operations.Where(row => row.Path.StartsWith(SelectedProfilePath935(report, id) + "/" + field + "[", StringComparison.Ordinal)).ToArray();

    private static void AssertUnexecutedGroupField940(CharacterCreationRuleSourceScopeResult report, string id, string field)
    {
        var operations = GroupField940(report, id, field);
        Assert.IsTrue(operations.Length > 0, field);
        Assert.IsTrue(operations.All(row => row.Disposition == D940.Unconsumed && row.ExecutionDigest is null
            && row.OperationResult718 is null && row.ConsumedValue718 is null), field);
    }

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(true, false, true)]
    [DataRow(true, true, true)]
    public void ProfileAttributes993_canonical_paid_and_zero_targets_reexecute_full_and_selected(
        bool paid, bool alternate, bool reverse)
    {
        using var context = Ready940.Create(true, metatypeName: "Ork", amendSettings: profile =>
            AttributeProfile993(profile, alternate, reverse));
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var request = AttributeRequest993(prepared.Inputs, paid);
        var files = Files962(context);
        if (paid)
        {
            var locked = request.Selections with
            { ChangeableAttributeIds = request.Selections.ChangeableAttributeIds.Where(id => id != "BOD").ToArray() };
            var blockers = CharacterCreationCompletionPreservationRules.Check(prepared.Inputs, locked);
            Assert.IsTrue(blockers.Contains(CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict),
                "A paid BOD increase must still require explicit scalar permission: " + string.Join(",", blockers));
        }
        var preservation = CharacterCreationCompletionPreservationRules.Check(prepared.Inputs, request.Selections);
        Assert.AreEqual(0, preservation.Count, string.Join(",", preservation));
        var full = ObserveSources839(context, prepared, request);
        Assert.AreEqual(true, full.Review.CompleteAssessment!.MechanicalSucceeded,
            $"paid={paid}, alternate={alternate}, reverse={reverse}; blockers={string.Join(",", full.Review.Blockers)}; "
            + $"balances={JsonSerializer.Serialize(full.Review.Balances)}");
        var invocation = full.Capture.Invocations.Single();
        var expected = CharacterCreationAttributesService.CreateDraftSession(prepared.Inputs.Skills.Attributes)!
            .Preview(request.Selections.AttributeAllocations);
        Assert.IsNotNull(expected.Value);
        Assert.IsTrue(expected.Blockers.All(blocker => blocker == CharacterCreationAttributesBlockers.DraftDuplicate));
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(expected.Value),
            CharacterCreationFinalizationDigest.Compute(invocation.Attributes718));
        Assert.IsTrue(invocation.Attributes718!.Attributes.Count > 0);
        var body = invocation.Attributes718.Attributes.Single(row => row.AttributeId == "BOD");
        Assert.IsTrue(body.Minimum > 1, "The metatype minimum exercises alternate attribute Karma.");
        Assert.AreEqual(paid ? 1 : 0, body.KarmaLevels);
        Assert.AreEqual(paid ? 2 : 0, body.PriorityPointsSpent);
        Assert.AreEqual(body.Minimum + (paid ? 3 : 0), body.Current);
        Assert.AreEqual(paid, body.KarmaCost > 0);
        Assert.IsTrue(CharacterCreationAttributeCostRules.TryCalculateAllocation(body.Minimum,
            body.PriorityPointsSpent, body.KarmaLevels, prepared.Inputs.AttributePolicy!.KarmaAttribute,
            alternate, reverse, out int expectedRating, out int expectedCost));
        Assert.AreEqual(expectedRating, body.Current);
        Assert.AreEqual(expectedCost, body.KarmaCost);
        AssertAttributes993(prepared.Inputs, invocation, full.Capture.Reports.Single(row => row.Scope == "profile"), paid);

        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot
            .Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var publicResult = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, prepared.Inputs,
            request, ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, publicResult.Outcome);
        Assert.IsNull(publicResult.Value);
        // This existing private boundary genuinely reexecutes selected consumers;
        // unresolved source siblings still prohibit a successful public witness.
        var selectedCapture = RestoreObserved839(prepared);
        var method = typeof(CharacterCreationSelectedCompletionEvaluator).GetMethod("CaptureReviewed",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var selected = (CharacterCreationFoundationResult<CharacterCreationSelectedCompletionWitness>)method.Invoke(null,
            [original, bootstrap, prepared.Inputs, request, full.Review, CancellationToken.None,
                new CharacterCreationRuleSourceExecutionContext(selectedCapture, context.Queries), null])!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, selected.Outcome);
        Assert.IsNull(selected.Value);
        var selectedInvocation = selectedCapture.Invocations.Single();
        Assert.IsFalse(ReferenceEquals(invocation, selectedInvocation));
        AssertAttributes993(prepared.Inputs, selectedInvocation, selectedCapture.Reports.Single(row => row.Scope == "profile"), paid);
        foreach (string field in AttributeFields993)
        {
            Assert.IsTrue(ReadAttribute993(invocation, prepared.Inputs, field, out _, out _, out var actual, out var digest));
            Assert.IsTrue(ReadAttribute993(selectedInvocation, prepared.Inputs, field, out _, out _, out var replay, out var replayDigest));
            Assert.AreEqual(digest, replayDigest);
            Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(actual), CharacterCreationFinalizationDigest.Compute(replay));
        }
        Assert.IsFalse(full.Review.CompleteAssessment.SatisfiesComplete);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved, ErrataDomain788(full.Review.CompleteAssessment).Coverage);
        AssertFiles962(context, files);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ProfileAttributes993_missing_profile_execution_cannot_account_evaluated_attributes(bool paid)
    {
        using var context = Ready940.Create(true, metatypeName: "Ork", amendSettings: profile => AttributeProfile993(profile, true, true));
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var request = AttributeRequest993(prepared.Inputs, paid) with { ProfileSettingsExecutionSchema = null };
        var observed = ObserveSources839(context, prepared, request);
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded,
            $"paid={paid}; blockers={string.Join(",", observed.Review.Blockers)}; "
            + $"balances={JsonSerializer.Serialize(observed.Review.Balances)}");
        foreach (string field in AttributeFields993)
        {
            Assert.IsFalse(ReadAttribute993(observed.Capture.Invocations.Single(), prepared.Inputs, field, out _, out _, out _, out _));
            AssertUnexecutedGroupField940(observed.Capture.Reports.Single(row => row.Scope == "profile"),
                prepared.Inputs.EffectivePolicy!.SettingsProfileId, field);
        }
        Assert.IsFalse(observed.Review.CompleteAssessment.SatisfiesComplete);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ProfileAttributes993_copied_crossed_mutated_and_stale_execution_refuses(bool paid)
    {
        using var context = Ready940.Create(true, metatypeName: "Ork", amendSettings: profile => AttributeProfile993(profile, true, true));
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var request = AttributeRequest993(prepared.Inputs, paid);
        var observed = ObserveSources839(context, prepared, request);
        var invocation = observed.Capture.Invocations.Single();
        var inputs = prepared.Inputs;
        var profile = Profile962(inputs);
        string foreign = CharacterCreationFinalizationDigest.Compute("foreign-attribute993");
        Assert.AreEqual("{}", JsonSerializer.Serialize(invocation));
        bool refusedSerialization = false;
        try { JsonSerializer.Deserialize<CharacterCreationRuleSourceScopeInvocation>(JsonSerializer.Serialize(invocation)); }
        catch (NotSupportedException) { refusedSerialization = true; }
        Assert.IsTrue(refusedSerialization, "Serialized diagnostics cannot reconstruct the private invocation.");
        var workspaceProperty = typeof(CharacterCreationRuleSourceScopeInvocation).GetProperty(nameof(invocation.WorkspaceId718))!;
        var workspace = invocation.WorkspaceId718;
        try
        {
            workspaceProperty.SetValue(invocation, new Chummer.Contracts.Workspaces.CharacterWorkspaceId(Guid.NewGuid().ToString("N")));
            foreach (string field in AttributeFields993)
                Assert.IsFalse(ReadAttribute993(invocation, inputs, field, out _, out _, out _, out _));
        }
        finally { workspaceProperty.SetValue(invocation, workspace); }
        Assert.IsTrue(ReadAttribute993(invocation, inputs, AttributeFields993[0], out _, out _, out _, out _));
        foreach (string field in AttributeFields993)
        {
            Assert.IsTrue(ReadAttribute993(invocation, inputs, field, out _, out _, out _, out _));
            string[] identity = [inputs.EffectivePolicy!.SettingsProfileId, inputs.EffectivePolicy.ProfileInputsDigest,
                Xml962(profile), field, Xml962(profile.Element(field)!)];
            for (int index = 0; index < identity.Length; index++)
            {
                var wrong = identity.ToArray(); wrong[index] = foreign;
                Assert.IsFalse(invocation.TryGetExecutedAttributeHouseRule993(wrong[0], wrong[1], wrong[2], wrong[3], wrong[4],
                    out _, out _, out _, out _));
            }
            // Equal boolean values in the other leaf do not make its XML interchangeable.
            string sibling = AttributeFields993.Single(value => value != field);
            Assert.IsFalse(invocation.TryGetExecutedAttributeHouseRule993(identity[0], identity[1], identity[2], field,
                Xml962(profile.Element(sibling)!), out _, out _, out _, out _));
        }
        var resolution = observed.Capture.CreateExecutionResolution(inputs.RuleSourceExecutionSchema)!;
        Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsTrue(resolution.EnableCandidateSourcebookSelection());
        var consumers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in inputs.EffectivePolicy!.Sources)
            if (invocation.TryGetCompletedConsumer(source.SourceId, out var consumer)) consumers.TryAdd(source.SourceId, consumer);
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
        var copiedReport = resolution.ExecuteRemainingSourceScopes(copy).Single(row => row.Scope == "profile");
        foreach (string field in AttributeFields993)
        {
            Assert.IsFalse(ReadAttribute993(copy, inputs, field, out _, out _, out _, out _));
            AssertUnexecutedGroupField940(copiedReport, inputs.EffectivePolicy.SettingsProfileId, field);
        }
        // Real, separately executed candidates also cannot be crossed after binding.
        var crossed = ObserveSources839(context, prepared, AttributeRequest993(inputs, !paid)).Capture.Invocations.Single();
        var property = typeof(CharacterCreationRuleSourceScopeInvocation).GetProperty(nameof(invocation.Attributes718))!;
        var originalAttributes = invocation.Attributes718;
        try
        {
            property.SetValue(invocation, crossed.Attributes718);
            foreach (string field in AttributeFields993)
                Assert.IsFalse(ReadAttribute993(invocation, inputs, field, out _, out _, out _, out _));
        }
        finally { property.SetValue(invocation, originalAttributes); }
        Assert.IsTrue(ReadAttribute993(invocation, inputs, AttributeFields993[0], out _, out _, out var frozen, out var digest));
        Assert.IsTrue(((IList<CharacterCreationAttributeProjection>)frozen!.Attributes).IsReadOnly);
        Assert.IsTrue(((IList<string>)frozen.Attributes[0].SourceAnchorIds).IsReadOnly);
        Assert.IsTrue(((IList<string>)frozen.CreationKarmaBudget.Blockers).IsReadOnly);
        Assert.IsTrue(((IList<string>)frozen.Blockers).IsReadOnly);
        var projections = (IList<CharacterCreationAttributeProjection>)invocation.Attributes718!.Attributes;
        var saved = projections[0];
        string frozenDigest = CharacterCreationFinalizationDigest.Compute(frozen);
        try
        {
            projections[0] = saved with { Current = saved.Current + 1 };
            Assert.AreEqual(frozenDigest, CharacterCreationFinalizationDigest.Compute(frozen));
            foreach (string field in AttributeFields993)
                Assert.IsFalse(ReadAttribute993(invocation, inputs, field, out _, out _, out _, out _));
        }
        finally { projections[0] = saved; }
        Assert.IsTrue(ReadAttribute993(invocation, inputs, AttributeFields993[0], out _, out _, out _, out var restored));
        Assert.AreEqual(digest, restored);
        EditProfile800(Path.Combine(context.Directory, "source"), row => row.SetElementValue(AttributeFields993[0], false));
        Assert.IsTrue(((ICharacterCreationRuleSourceCapture)context.Resolver).TryCaptureSourcebookRuleSources(
            context.Store.Get(context.WorkspaceId).Value!.Document.Content, out var changed));
        Assert.AreNotEqual(inputs.RuleSourceCaptureDigest, changed!.IntegrityDigest);
        var stale = changed.CreateExecutionResolution()!;
        Assert.IsTrue(stale.EnablePurchaseSourceExecution());
        Assert.IsTrue(stale.EnableCompleteSkillCapSourceExecution());
        Assert.IsTrue(stale.EnableCandidateSourcebookSelection());
        Assert.AreEqual(0, stale.ExecuteRemainingSourceScopes(invocation).Count);
    }

    [TestMethod]
    [DataRow("alternatemetatypeattributekarma", "duplicate")]
    [DataRow("reverseattributepriorityorder", "duplicate")]
    [DataRow("alternatemetatypeattributekarma", "namespace")]
    [DataRow("reverseattributepriorityorder", "namespace")]
    [DataRow("alternatemetatypeattributekarma", "attribute")]
    [DataRow("reverseattributepriorityorder", "attribute")]
    [DataRow("alternatemetatypeattributekarma", "nested")]
    [DataRow("reverseattributepriorityorder", "nested")]
    [DataRow("alternatemetatypeattributekarma", "numeric")]
    [DataRow("reverseattributepriorityorder", "numeric")]
    [DataRow("alternatemetatypeattributekarma", "empty")]
    [DataRow("reverseattributepriorityorder", "empty")]
    [DataRow("alternatemetatypeattributekarma", "comment")]
    [DataRow("reverseattributepriorityorder", "comment")]
    [DataRow("alternatemetatypeattributekarma", "cdata")]
    [DataRow("reverseattributepriorityorder", "cdata")]
    public void ProfileAttributes993_malformed_or_duplicate_leaf_never_gains_execution(string field, string malformed)
    {
        using var context = Ready940.Create(true, amendSettings: profile => AttributeProfile993(profile, false, false));
        var initial = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var files = Files962(context);
        EditProfile800(Path.Combine(context.Directory, "source"), profile =>
        {
            var leaf = profile.Element(field)!;
            switch (malformed)
            {
                case "duplicate": profile.Add(new XElement(leaf)); break;
                case "namespace": profile.Add(new XElement(XName.Get(field, "urn:unknown993"), leaf.Value)); break;
                case "attribute": leaf.SetAttributeValue("unknown993", "true"); break;
                case "nested": leaf.ReplaceNodes(new XElement("unknown993", "false")); break;
                case "numeric": leaf.Value = "0"; break;
                case "empty": leaf.Value = ""; break;
                case "comment": leaf.Add(new XComment("nonplain993")); break;
                case "cdata": leaf.ReplaceNodes(new XCData(leaf.Value)); break;
            }
        });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        IOwnerBoundCharacterCreationCompletionReader reader = new OwnerBoundCharacterCreationCompletionReader(
            context.Store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithMovementSourceExecution(owners.Capture(),
            new(context.WorkspaceId), includeLoadout: true, includeSourcebooks: true);
        Assert.AreEqual(0, owners.ActiveLeases);
        if (loaded.Value is { } prepared)
        {
            var observed = ObserveSources839(context, prepared, AttributeRequest993(prepared.Inputs, false));
            Assert.IsFalse(observed.Review.CompleteAssessment!.SatisfiesComplete);
            foreach (var invocation in observed.Capture.Invocations)
                Assert.IsFalse(ReadAttribute993(invocation, prepared.Inputs, field, out _, out _, out _, out _));
            foreach (var report in observed.Capture.Reports.Where(row => row.Scope == "profile"))
                AssertUnexecutedGroupField940(report, initial.Inputs.EffectivePolicy!.SettingsProfileId, field);
        }
        else Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome);
        AssertFiles962(context, files);
    }

    [TestMethod]
    [DataRow("alternatemetatypeattributekarma", false)]
    [DataRow("alternatemetatypeattributekarma", true)]
    [DataRow("reverseattributepriorityorder", false)]
    [DataRow("reverseattributepriorityorder", true)]
    public void ProfileAttributes1009_fresh_nonplain_leaf_refuses_after_full_and_private_selected_execution(string field, bool cdata)
    {
        // Change the source before bootstrap. A stale bootstrap digest cannot
        // substitute for the exact leaf-shape check under review.
        using var context = Ready940.Create(true, metatypeName: "Ork", amendSettings: profile =>
        {
            AttributeProfile993(profile, true, true);
            var leaf = profile.Element(field)!;
            if (cdata) leaf.ReplaceNodes(new XCData(leaf.Value));
            else leaf.Add(new XComment("nonplain1009"));
        });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var request = AttributeRequest993(prepared.Inputs, true);
        var files = Files962(context);
        var full = ObserveSources839(context, prepared, request);
        Assert.AreEqual(true, full.Review.CompleteAssessment!.MechanicalSucceeded);
        Assert.IsFalse(full.Review.CompleteAssessment.SatisfiesComplete);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot
            .Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var selectedCapture = RestoreObserved839(prepared);
        var method = typeof(CharacterCreationSelectedCompletionEvaluator).GetMethod("CaptureReviewed",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var selected = (CharacterCreationFoundationResult<CharacterCreationSelectedCompletionWitness>)method.Invoke(null,
            [original, bootstrap, prepared.Inputs, request, full.Review, CancellationToken.None,
                new CharacterCreationRuleSourceExecutionContext(selectedCapture, context.Queries), null])!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, selected.Outcome);
        Assert.IsNull(selected.Value);
        foreach (var capture in new[] { full.Capture, selectedCapture })
        {
            var invocation = capture.Invocations.Single();
            Assert.IsTrue(invocation.Attributes718!.Attributes.Any(row => row.KarmaLevels > 0));
            Assert.IsFalse(ReadAttribute993(invocation, prepared.Inputs, field, out _, out _, out _, out _));
            var report = capture.Reports.Single(row => row.Scope == "profile");
            AssertUnexecutedGroupField940(report, prepared.Inputs.EffectivePolicy!.SettingsProfileId, field);
            Assert.IsFalse(report.IsComplete);
        }
        Assert.AreEqual(JsonSerializer.Serialize(full.Capture.Reports), JsonSerializer.Serialize(selectedCapture.Reports));
        AssertFiles962(context, files);
    }

    private static readonly string[] AttributeFields993 = ["alternatemetatypeattributekarma", "reverseattributepriorityorder"];
    private static void AttributeProfile993(XElement profile, bool alternate, bool reverse)
    {
        profile.SetElementValue("buildpoints", 200);
        profile.SetElementValue(AttributeFields993[0], alternate);
        profile.SetElementValue(AttributeFields993[1], reverse);
        profile.Add(new XElement("unknownattribute993", "true"));
    }
    private static CharacterCreationCompletionReviewRequest AttributeRequest993(CharacterCreationCompletionReviewInputs inputs, bool paid)
    {
        var request = GroupRequest940(inputs);
        if (!paid) return request;
        var body = inputs.Skills.Attributes.Attributes.Single(row => row.AttributeId == "BOD");
        // Finish preserves saved scalar choices unless their increase is explicitly permitted.
        return request with { Selections = request.Selections with
        {
            AttributeAllocations = [.. request.Selections.AttributeAllocations.Where(row => row.AttributeId != "BOD"),
                new("BOD", body.PriorityPointsSpent + 2, body.KarmaLevels + 1)],
            ChangeableAttributeIds = [.. request.Selections.ChangeableAttributeIds.Where(id => id != "BOD"), "BOD"]
        } };
    }
    private static bool ReadAttribute993(CharacterCreationRuleSourceScopeInvocation invocation,
        CharacterCreationCompletionReviewInputs inputs, string field, out bool value, out bool noPaidTargets,
        out CharacterCreationAttributesPreview? result, out string? digest)
    {
        var profile = Profile962(inputs);
        return invocation.TryGetExecutedAttributeHouseRule993(inputs.EffectivePolicy!.SettingsProfileId,
            inputs.EffectivePolicy.ProfileInputsDigest, Xml962(profile), field, Xml962(profile.Element(field)!),
            out value, out noPaidTargets, out result, out digest);
    }
    private static void AssertAttributes993(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationRuleSourceScopeInvocation invocation, CharacterCreationRuleSourceScopeResult report, bool paid)
    {
        foreach (string field in AttributeFields993)
        {
            Assert.IsTrue(ReadAttribute993(invocation, inputs, field, out bool value, out bool noPaidTargets, out var result, out var digest));
            Assert.AreEqual(bool.Parse(Profile962(inputs).Element(field)!.Value), value);
            Assert.AreEqual(!paid, noPaidTargets);
            Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(invocation.Attributes718), CharacterCreationFinalizationDigest.Compute(result));
            Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(digest));
            var operations = GroupField940(report, inputs.EffectivePolicy!.SettingsProfileId, field);
            Assert.AreEqual(2, operations.Length, "Exactly the selected element and its text gain coverage.");
            foreach (var operation in operations)
            {
                Assert.AreEqual(D940.Compiled, operation.Disposition);
                Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(operation.ExecutionDigest));
                using var details = JsonDocument.Parse(operation.OperationResult718!);
                Assert.AreEqual(paid ? "TryCalculateAllocation-paid-base" : "EvaluateAllocations-no-paid-attributes",
                    details.RootElement.GetProperty("Operation").GetString());
                Assert.AreEqual(!paid, details.RootElement.GetProperty("NoPaidTargets").GetBoolean());
                Assert.AreEqual(digest, details.RootElement.GetProperty("Execution").GetString());
                var evaluated = details.RootElement.GetProperty("Result").Deserialize<CharacterCreationAttributesPreview>()!;
                Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(result), CharacterCreationFinalizationDigest.Compute(evaluated));
                Assert.AreEqual(value, bool.Parse(operation.ConsumedValue718!));
            }
        }
        AssertUnexecutedGroupField940(report, inputs.EffectivePolicy!.SettingsProfileId, "unknownattribute993");
        Assert.IsFalse(report.IsComplete);
    }

    private static void AssertExecutedGroup940(CharacterCreationRuleSourceScopeResult report, string id, bool enabled, bool noTarget, string digest)
    {
        var operations = GroupField940(report, id, "usepointsonbrokengroups");
        Assert.AreEqual(2, operations.Length, "Exactly one element and one text operation.");
        foreach (var operation in operations)
        {
            Assert.AreEqual(D940.Compiled, operation.Disposition);
            Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(operation.ExecutionDigest));
            using var result = JsonDocument.Parse(operation.OperationResult718!);
            Assert.AreEqual("canonical-broken-group-policy", result.RootElement.GetProperty("Operation").GetString());
            Assert.AreEqual(enabled, result.RootElement.GetProperty("Result").GetBoolean());
            Assert.AreEqual(noTarget, result.RootElement.GetProperty("NoTarget").GetBoolean());
            Assert.AreEqual(digest, result.RootElement.GetProperty("CanonicalExecution").GetString());
            Assert.AreEqual(enabled, bool.Parse(operation.ConsumedValue718!));
        }
    }
}
