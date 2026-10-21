using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using K = Chummer.Contracts.Characters.CharacterCreationCompleteObligationKind;
using C = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Complete375_Selected_effects_compile_full_and_cold_without_saving(bool savedChoices)
    {
        using var context = ReadyContext.Create(savedChoices, includeNonEmptyPurchases: true,
            includeSkillPurchase: true, amendSettings: settings => SetLimits340(settings, 6, 6, 12, 12));
        string original = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!);
        var inputs = LoadConsumption(context);
        string originalInputs = JsonSerializer.Serialize(inputs);
        var policyCoverage = inputs.EffectivePolicy!.Consumers!.Coverage.Single(row =>
            row.Operation == CharacterCreationConsumerPolicyOperation.SelectedEffects);
        Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Missing, policyCoverage.Disposition);
        Assert.AreEqual(CharacterCreationConsumerPolicyReason.SelectedEffectsRequireEvaluation, policyCoverage.Reason);
        var request = GrantRequest368(inputs);
        var gear = inputs.Lifestyles.Gear.State.Authority.Options.Where(row => row.IsSelectable && row.PackageCost > 0m)
            .OrderBy(row => row.PackageCost).First();
        request = request with { Selections = request.Selections with
        {
            Gear = request.Selections.Gear.Count == 0 ? [new(gear.OptionId, 1)] : request.Selections.Gear,
            Contacts = [.. request.Selections.Contacts, new(Guid.Parse("648f1178-d5e2-4464-9c48-8612b8f6eb4c"),
                new("Proposed medic", "", "Seattle", "", "", "", "", "", "", "", "", "", GroupName: string.Empty), 1, 1)]
        } };
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        var effects = Graph342(full.Value!.CompleteAssessment!, CharacterCreationConsumerPolicyOperation.SelectedEffects);
        Assert.AreEqual(C.Evaluated, effects.Coverage);
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(effects.EvaluationDigest));
        AssertSelectedEffects380(full.Value.CompleteAssessment!, C.Evaluated);
        Assert.IsTrue(full.Value.Lifestyles!.Gear.Lines.Count > 0);
        Assert.IsTrue(full.Value.Contacts!.Contacts.Count > 0);
        Assert.IsTrue(full.Value.Lifestyles.Lifestyles.Count > 0);
        var cold = Cold342(context, inputs, request);
        Assert.AreEqual(JsonSerializer.Serialize(full.Value.CompleteAssessment), JsonSerializer.Serialize(cold.CompleteAssessment));
        foreach (var operation in new[] { CharacterCreationConsumerPolicyOperation.Amendments,
            CharacterCreationConsumerPolicyOperation.Errata, CharacterCreationConsumerPolicyOperation.CharacterMechanics,
            CharacterCreationConsumerPolicyOperation.Campaign })
            Assert.AreEqual(C.Unresolved, Graph342(cold.CompleteAssessment!, operation).Coverage, operation.ToString());
        Assert.IsFalse(cold.CompleteAssessment!.SatisfiesComplete);
        var complete = CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, complete.Outcome);
        Assert.AreEqual(C.Evaluated,
            Graph342(complete.Value!.CompleteAssessment!, CharacterCreationConsumerPolicyOperation.SelectedEffects).Coverage);
        AssertSelectedEffects380(complete.Value.CompleteAssessment!, C.Evaluated);
        Assert.AreEqual(originalInputs, JsonSerializer.Serialize(inputs), "Execution must not rewrite the frozen source-policy inventory.");
        Assert.AreEqual(original, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void Complete375_Selected_effects_require_real_bound_successful_execution()
    {
        using var context = ReadyContext.Create(false);
        var inputs = LoadConsumption(context);
        var request = GrantRequest368(inputs);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        Assert.IsNotNull(review);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var operation = authority.Obligations.Single(row => row.Kind == K.ConsumerOperation
            && row.Operation == CharacterCreationConsumerPolicyOperation.SelectedEffects);
        Assert.AreEqual(CharacterCreationCompleteEvaluability.CandidateDependent, operation.Evaluability);
        Assert.AreEqual(CharacterCreationCompleteReason.CandidateEvaluationRequired, operation.Reason);
        AssertSelectedEffects380(authority, C.Pending);
        var execution = CharacterCreationSelectedEffectsConsumer.FromFull(inputs, review, authority);
        Assert.IsNotNull(execution);
        var admitted = CharacterCreationCompleteConsumerExecution.FromFull(inputs, review, authority);
        Assert.IsNotNull(admitted);
        Assert.AreEqual(execution!.DigestFor(authority), admitted!.EvaluationsFor(authority)[operation.Id]);
        AssertSelectedEffects380(CharacterCreationCompleteAssessmentService.AfterEvaluation(
            authority, review.PolicyConsumption, true, consumers: admitted), C.Evaluated);
        Assert.AreEqual(Graph342(review.CompleteAssessment!, CharacterCreationConsumerPolicyOperation.SelectedEffects).EvaluationDigest,
            execution!.DigestFor(authority));
        foreach (var borrowed in new[]
        {
            authority with { PolicyDigest = CharacterCreationFinalizationDigest.Compute("other policy") },
            authority with { RawCharacterXmlDigest = CharacterCreationFinalizationDigest.Compute("other character") },
            authority with { RequestDigest = CharacterCreationFinalizationDigest.Compute("other request") }
        })
        {
            Assert.IsNull(execution.DigestFor(borrowed));
            Assert.AreEqual(0, admitted.EvaluationsFor(borrowed).Count);
        }
        // Serialized/equal evaluation diagnostics cannot reconstruct either
        // obligation's private in-process compiler execution.
        AssertSelectedEffects380(CharacterCreationCompleteAssessmentService.AfterEvaluation(
            authority, review.PolicyConsumption, true), C.Unresolved);
        // A malformed source-bound graph must not obtain coverage merely because
        // numeric budgets and the other canonical consumers still succeed.
        var invalidLoadout = new CharacterCreationLoadoutCompletionProjection(new([], []), [], 0m, [], "invalid");
        Assert.IsNull(CharacterCreationSelectedEffectsConsumer.FromFull(inputs, review with { Loadout = invalidLoadout }, authority));
        var consumers = CharacterCreationCompleteConsumerExecution.FromFull(inputs, review with { Loadout = invalidLoadout }, authority);
        AssertSelectedEffects380(CharacterCreationCompleteAssessmentService.AfterEvaluation(
            authority, review.PolicyConsumption, true, consumers: consumers), C.Unresolved);
        Assert.IsNull(CharacterCreationSelectedEffectsConsumer.FromFull(inputs,
            review with { Request = request with { InputsSnapshotDigest = CharacterCreationFinalizationDigest.Compute("other snapshot") } }, authority));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            CharacterCreationSelectedEffectsConsumer.FromFull(inputs, review, authority, cancellation.Token));
    }

    [TestMethod]
    public void Complete377_Selected_effects_preserve_manual_loadout_and_mystic_group_contact_rules()
    {
        using var context = CreateGroupContext(2, true);
        var manual = new CharacterCreationLoadoutService(context.Store, context.Resolver);
        var prepared = manual.Prepare(context.WorkspaceId);
        Assert.IsNotNull(prepared.Value, string.Join(",", prepared.Blockers));
        var state = prepared.Value!;
        var armor = state.Inputs.Authority.Options.Where(row => row.Kind == CharacterCreationLoadoutKinds.Armor
            && row.IsSelectable && row.UnitCost > 0m).OrderBy(row => row.UnitCost).First();
        var quote = manual.Preview(new(state.Inputs.Binding, state.Inputs.SnapshotDigest, new([], [new(armor.OptionId)])));
        Assert.IsNotNull(quote.Value, string.Join(",", quote.Blockers));
        var confirmed = manual.Confirm(new(quote.Value!.Request, quote.Value.PreviewDigest, "selected-effects-armor", true));
        Assert.IsNotNull(confirmed.Value, string.Join(",", confirmed.Blockers));
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        IOwnerBoundCharacterCreationCompletionReader reader =
            new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithPolicyConsumption(owners.Capture(), new(context.WorkspaceId), true);
        Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
        var inputs = loaded.Value!;
        var weapon = inputs.Loadout!.Authority.Options.Where(row => row.Kind == CharacterCreationLoadoutKinds.Weapon
            && row.IsSelectable && row.UnitCost > 0m).OrderBy(row => row.UnitCost).First();
        var baseline = AddGroups(Request(inputs, CharacterCreationPolicyRequirement.None), 1);
        var request = baseline with
        {
            CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4,
            Selections = baseline.Selections with
            { Loadout = new([new(weapon.OptionId)], baseline.Selections.Loadout!.Armor) }
        };
        string original = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.AreEqual(C.Evaluated,
            Graph342(full.Value!.CompleteAssessment!, CharacterCreationConsumerPolicyOperation.SelectedEffects).Coverage);
        AssertSelectedEffects380(full.Value.CompleteAssessment!, C.Evaluated);
        Assert.IsTrue(full.Value.Request.Selections.MagicSelections.Spells.Count > 0);
        Assert.IsTrue(full.Value.Request.Selections.MagicSelections.AdeptPowers.Count > 0);
        Assert.IsNotNull(full.Value.Contacts!.GroupKarma);
        Assert.IsTrue(full.Value.Contacts.GroupKarma!.AdditionalKarmaCost > 0);
        Assert.IsTrue(full.Value.Loadout!.RetainedInstances!.Count > 0);
        var cold = Cold342(context, inputs, request, out var witness);
        Assert.AreEqual(JsonSerializer.Serialize(full.Value.CompleteAssessment), JsonSerializer.Serialize(cold.CompleteAssessment));
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var projected = full.Value.Loadout with { RetainedInstances = [] };
        projected = projected with { ProjectionDigest = CharacterCreationLoadoutCompletionRules.ComputeProjectionDigest(projected) };
        Assert.IsNull(CharacterCreationSelectedEffectsConsumer.FromFull(inputs, full.Value with { Loadout = projected }, authority),
            "A correctly rehashed graph cannot replace the admitted original instance identities.");
        Assert.IsNull(CharacterCreationSelectedEffectsConsumer.FromSelected(witness, request, cold, authority, []),
            "Cold replay must obtain original instance identities from its real archive, not the proposed graph.");
        Assert.AreEqual(original, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    [DataRow(CharacterCreationCompleteAssessment.SchemaV1)]
    [DataRow(CharacterCreationCompleteAssessment.SchemaV2)]
    [DataRow(CharacterCreationCompleteAssessment.SchemaV3)]
    public void Complete375_Historical_selected_effects_coverage_is_not_reinterpreted(string schema)
    {
        using var context = ReadyContext.Create(false);
        var inputs = LoadConsumption(context);
        var request = GrantRequest368(inputs) with { CompleteAssessmentSchema = schema };
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.AreEqual(C.Unresolved, Graph342(full.Value!.CompleteAssessment!, CharacterCreationConsumerPolicyOperation.SelectedEffects).Coverage);
        AssertSelectedEffects380(full.Value.CompleteAssessment!, C.Unresolved);
        Assert.AreEqual(CharacterCreationCompleteEvaluability.AuthorityUnavailable,
            full.Value.CompleteAssessment!.Obligations.Single(row => row.Kind == K.ConsumerOperation
                && row.Operation == CharacterCreationConsumerPolicyOperation.SelectedEffects).Evaluability);
        Assert.IsNull(CharacterCreationSelectedEffectsConsumer.FromFull(inputs, full.Value,
            CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request)));
        AssertCold340(context, inputs, request, full.Value);
    }

    private static void AssertSelectedEffects380(CharacterCreationCompleteAssessment assessment,
        CharacterCreationCompleteCoverage expected)
    {
        var rows = assessment.Obligations.Where(row => row.Operation == CharacterCreationConsumerPolicyOperation.SelectedEffects
            && row.Kind is K.ConsumerOperation or K.ActiveGraph).ToArray();
        Assert.AreEqual(2, rows.Length);
        foreach (var row in rows) Assert.AreEqual(expected, row.Coverage, row.Id);
        if (expected == C.Evaluated)
        {
            Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(rows[0].EvaluationDigest));
            Assert.AreEqual(rows[0].EvaluationDigest, rows[1].EvaluationDigest,
                "Both obligations must bind the same actual compiler execution, not independent passing flags.");
        }
    }

    [TestMethod]
    public void Complete371_Empty_numeric_skill_scopes_execute_and_cold_replay_without_rating_native_language()
    {
        using var context = ReadyContext.Create(false);
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!);
        var inputs = LoadConsumption(context);
        var request = Request342(inputs, Request(inputs, CharacterCreationPolicyRequirement.None)) with
        { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4 };
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.AreEqual(0, full.Value!.Skills!.SkillGroups.Count);
        Assert.IsTrue(full.Value.Skills.Skills.Count > 0);
        Assert.IsTrue(full.Value.Skills.Skills.All(row => row.IsNativeLanguage && row.Rating is null));
        AssertLimits340(full.Value.CompleteAssessment!, C.Evaluated);
        Assert.IsFalse(full.Value.CompleteAssessment!.SatisfiesComplete);
        AssertCold340(context, inputs, request, full.Value);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    [DataRow(CharacterCreationCompleteAssessment.SchemaV1)]
    [DataRow(CharacterCreationCompleteAssessment.SchemaV2)]
    [DataRow(CharacterCreationCompleteAssessment.SchemaV3)]
    public void Complete371_Historical_empty_scope_coverage_is_not_reinterpreted(string schema)
    {
        using var context = ReadyContext.Create(false);
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None) with { CompleteAssessmentSchema = schema };
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        AssertLimits340(full.Value!.CompleteAssessment!, C.Unresolved);
    }

    [TestMethod]
    [DataRow("B", "Conjuring", true)]
    [DataRow("D", "Conjuring", false)]
    [DataRow("D", "Enchanting", false)]
    [DataRow("D", "Sorcery", false)]
    public void Complete371_Canonical_scope_includes_automatic_grants_but_not_zero_rating_prompts(
        string rank, string aspect, bool grantsGroup)
    {
        using var context = ReadyContext.Create(false, talentValue: "Aspected Magician", talentRank: rank,
            talentGroupName: aspect);
        var inputs = LoadConsumption(context);
        var request = Request342(inputs, Request(inputs, CharacterCreationPolicyRequirement.None)) with
        { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4 };
        var prepared = CharacterCreationSkillsService.PrepareCompletionPreview(inputs.Skills,
            request.Selections.AttributeAllocations, CancellationToken.None);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, prepared.Outcome);
        var trace = new CharacterCreationSkillsService.CompleteSkillTrace();
        // Intentionally omit group rows: the canonical consumer restores genuine
        // talent grants, but must not fabricate levels from selection-only prompts.
        var preview = prepared.Value!.Preview(request.Selections.SkillAllocations, [], CancellationToken.None,
            completeTrace: trace);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, preview.Outcome, string.Join(",", preview.Blockers));
        Assert.IsTrue(trace.Completed);
        var scope = trace.Scopes.Single(row => row.SettingId == CharacterCreationEffectivePolicyContext.GroupLimit);
        Assert.AreEqual(grantsGroup ? 1 : 0, scope.Checks.Count);
        Assert.AreEqual(preview.Value!.SkillGroups.Count, scope.Checks.Count);
        foreach (var group in preview.Value.SkillGroups)
        {
            Assert.IsTrue(group.GrantedRating > 0);
            Assert.IsTrue(scope.Checks.Any(check => check.TargetId == group.GroupId && check.Rating == group.Rating));
        }
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        AssertLimits340(full.Value!.CompleteAssessment!, C.Evaluated);
        AssertCold340(context, inputs, request, full.Value);
    }

    [TestMethod]
    public void Complete371_Failed_final_allocation_cannot_keep_prior_completed_scope()
    {
        using var context = ReadyContext.Create(false);
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        var prepared = CharacterCreationSkillsService.PrepareCompletionPreview(inputs.Skills,
            request.Selections.AttributeAllocations, CancellationToken.None);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, prepared.Outcome);
        var trace = new CharacterCreationSkillsService.CompleteSkillTrace();
        var valid = prepared.Value!.Preview(request.Selections.SkillAllocations, [], CancellationToken.None,
            completeTrace: trace);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, valid.Outcome);
        Assert.IsTrue(trace.Completed);
        Assert.AreEqual(3, trace.Scopes.Count);
        var group = inputs.Skills.Authority.SkillGroups.First();
        var invalid = prepared.Value.Preview(request.Selections.SkillAllocations,
            [new(group.GroupId, inputs.Skills.Authority.MaxSkillGroupRatingCreate + 1)], CancellationToken.None,
            completeTrace: trace);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, invalid.Outcome);
        Assert.IsFalse(trace.Completed);
        Assert.AreEqual(0, trace.Scopes.Count);
        var again = prepared.Value.Preview(request.Selections.SkillAllocations, [], CancellationToken.None,
            completeTrace: trace);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, again.Outcome);
        Assert.IsTrue(trace.Completed);
        Assert.AreEqual(3, trace.Scopes.Count);
        Assert.IsTrue(trace.Scopes.All(scope => scope.Checks.Count == 0));
    }

    private static CharacterCreationCompletionReviewRequest GrantRequest368(CharacterCreationCompletionReviewInputs inputs)
        => Request342(inputs, Request(inputs, CharacterCreationPolicyRequirement.None) with
        {
            Selections = CharacterCreationCompletionPlannerTests.KnowledgeChoices(inputs)
        }) with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4 };

    private static void AssertGrants368(CharacterCreationCompleteAssessment assessment, C coverage)
    {
        foreach (var kind in new[] { K.SelectedGrants, K.SelectedPrerequisites })
        {
            var row = assessment.Obligations.Single(item => item.Kind == kind);
            Assert.AreEqual(coverage, row.Coverage, kind.ToString());
            Assert.AreEqual(coverage == C.Evaluated, CharacterCreationFinalizationDigest.IsCanonical(row.EvaluationDigest));
        }
    }

    [TestMethod]
    [DataRow(null, "Human")]
    [DataRow("Adept", "Human")]
    [DataRow("Magician", "Human")]
    [DataRow("Technomancer", "Human")]
    [DataRow("Magician", "Ork")]
    public void Complete368_Actual_racial_talent_graph_matches_full_and_cold_selected_replay(string? talent, string metatype)
    {
        using var context = ReadyContext.Create(false, talentValue: talent, metatypeName: metatype,
            rankAssignments: metatype == "Ork" ? CharacterCreationSelectedPrerequisiteTests.OrkRanks() : null,
            includeSkillPurchase: metatype != "Ork", amendSettings: settings => SetLimits340(settings, 6, 6, 12, 12));
        var original = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!);
        var inputs = LoadConsumption(context);
        var request = GrantRequest368(inputs);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        Assert.AreEqual(CharacterCreationCompleteEvaluability.CandidateDependent,
            authority.Obligations.Single(row => row.Kind == K.SelectedGrants).Evaluability);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.AreEqual(true, full.Value!.CompleteAssessment!.MechanicalSucceeded);
        AssertGrants368(full.Value.CompleteAssessment, C.Evaluated);
        AssertLimits340(full.Value.CompleteAssessment, C.Evaluated);
        Assert.AreEqual(C.Evaluated,
            Graph342(full.Value.CompleteAssessment, CharacterCreationConsumerPolicyOperation.SelectedEffects).Coverage);
        if (talent is null)
            Assert.AreEqual(0, full.Value.SelectedPrerequisites!.Checks.Count,
                "Empty grants are proven by compiler execution, not by a nonempty predicate list.");
        else
            Assert.IsTrue(full.Value.SelectedPrerequisites!.Checks.Any(row => row.Origin == "talent"));
        if (metatype == "Ork")
            Assert.IsTrue(full.Value.SelectedPrerequisites!.Checks.Any(row => row.Origin == "racial"));
        var cold = Cold342(context, inputs, request);
        Assert.AreEqual(JsonSerializer.Serialize(full.Value.CompleteAssessment), JsonSerializer.Serialize(cold.CompleteAssessment));
        AssertGrants368(cold.CompleteAssessment!, C.Evaluated);
        Assert.IsFalse(cold.CompleteAssessment!.MayRequestCompleteCandidate);
        Assert.IsFalse(cold.CompleteAssessment.SatisfiesComplete);
        Assert.IsTrue(cold.CompleteAssessment.Obligations.Where(row => row.Kind is K.Source or K.Sourcebook)
            .All(row => row.Coverage == C.Unresolved));
        var complete = CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, complete.Outcome);
        AssertGrants368(complete.Value!.CompleteAssessment!, C.Evaluated);
        Assert.AreEqual(original, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void Complete368_Diagnostic_or_borrowed_execution_cannot_discharge_grants()
    {
        using var context = ReadyContext.Create(false, talentValue: "Adept", includeSkillPurchase: true,
            amendSettings: settings => SetLimits340(settings, 6, 6, 12, 12));
        var inputs = LoadConsumption(context);
        var request = GrantRequest368(inputs);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        Assert.IsNotNull(review);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var upstream = review.UpstreamBudget!;
        var executed = CharacterCreationSelectedPrerequisiteConsumer.EvaluateFull(inputs.Magic, upstream.Magic,
            review.Skills!, upstream.Qualities.Qualities, inputs.SnapshotDigest,
            CharacterCreationFinalizationDigest.Compute(request.Selections), authority);
        Assert.IsNotNull(executed);
        var consumption = review.PolicyConsumption!;
        AssertGrants368(CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, consumption, true, executed), C.Evaluated);
        // Equal/deserialized public predicate evidence has no private compilation trace.
        var diagnosticOnly = new CharacterCreationSelectedPrerequisiteConsumer.Execution(
            JsonSerializer.Deserialize<CharacterCreationSelectedPrerequisiteEvaluation>(JsonSerializer.Serialize(executed!.Evaluation))!);
        AssertGrants368(CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, consumption, true, diagnosticOnly), C.Unresolved);
        AssertGrants368(CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, consumption, true), C.Unresolved);
        AssertGrants368(CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, consumption, false, executed), C.Unresolved);
        AssertGrants368(CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, null, true, executed), C.Unresolved);
        foreach (var borrowed in new[]
        {
            authority with { PolicyDigest = CharacterCreationFinalizationDigest.Compute("other policy") },
            authority with { RequestDigest = CharacterCreationFinalizationDigest.Compute("other request") },
            authority with { RawCharacterXmlDigest = CharacterCreationFinalizationDigest.Compute("other character") },
            authority with { Obligations = authority.Obligations.Where(row => row.Kind != K.Sourcebook).ToArray() }
        })
            AssertGrants368(CharacterCreationCompleteAssessmentService.AfterEvaluation(borrowed, consumption, true, executed), C.Unresolved);
    }

    [TestMethod]
    public void Complete368_Cold_replay_rejects_rehashed_grant_coverage_and_evidence_forgery()
    {
        using var context = ReadyContext.Create(false, talentValue: "Adept", includeSkillPurchase: true,
            amendSettings: settings => SetLimits340(settings, 6, 6, 12, 12));
        var inputs = LoadConsumption(context);
        var request = GrantRequest368(inputs);
        _ = Cold342(context, inputs, request, out var witness);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var rows = witness.CompleteAssessment!.Obligations;
        foreach (var changed in new[]
        {
            rows.Select(row => row.Kind == K.SelectedGrants
                ? row with { EvaluationDigest = CharacterCreationFinalizationDigest.Compute("other compiled graph") } : row).ToArray(),
            rows.Select(row => row.Kind == K.SelectedGrants ? row with { Coverage = C.ProvenInactive } : row).ToArray(),
            rows.Where(row => row.Kind != K.SelectedGrants).ToArray(),
            rows.Select(row => row.Kind == K.SelectedPrerequisites ? row with { Coverage = C.Unresolved, EvaluationDigest = null } : row).ToArray()
        })
        {
            var forged = witness with { CompleteAssessment = witness.CompleteAssessment with { Obligations = changed } };
            forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
            var refused = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, forged, request);
            Assert.IsNull(refused.Value);
            CollectionAssert.Contains(refused.Blockers.ToArray(), CharacterCreationCompleteAssessment.Invalid);
        }
        var altered = witness with { SelectedPrerequisites = witness.SelectedPrerequisites! with { Checks = [] } };
        altered = altered with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(altered) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, altered, request).Value);
    }
    [TestMethod]
    [DataRow("<specificskill><name>Computer</name><bonus>2</bonus><condition>Matrix Perception</condition></specificskill>", false)]
    [DataRow("<specificskill><name>Computer</name><bonus>-2</bonus><applytorating>false</applytorating></specificskill>", false)]
    [DataRow("<specificskill><name>Computer</name><bonus>2</bonus><applytorating>true</applytorating></specificskill>", true)]
    [DataRow("<specificskill><name>Computer</name><bonus>2</bonus><applytorating>unknown</applytorating></specificskill>", true)]
    [DataRow("<specificskill><name>Computer</name><bonus>2</bonus><unknown /></specificskill>", true)]
    [DataRow("<specificskill xmlns='urn:unknown'><name>Computer</name><bonus>2</bonus></specificskill>", true)]
    [DataRow("<specificskill><name>Computer</name><name>Pistols</name><bonus>2</bonus></specificskill>", true)]
    [DataRow("<specificskill><name>Computer</name><bonus>2147483648</bonus></specificskill>", true)]
    [DataRow("<activeskillkarmacost><name>Computer</name><val>2</val></activeskillkarmacost>", true)]
    public void Complete368_Only_canonical_dice_only_grants_are_independent_of_purchase_cost(string effect, bool unsupported)
    {
        string source = "<quality><bonus>" + effect + "</bonus></quality>";
        Assert.IsTrue(CharacterCreationSkillsService.HasUnsupportedCompletionSkillModifiers([source]),
            "Historical callers keep their original fail-closed boundary.");
        Assert.AreEqual(unsupported,
            CharacterCreationSkillsService.HasUnsupportedCompletionSkillModifiers([source], allowDiceOnlyGrants: true));
        Assert.AreEqual(!unsupported, CharacterCreationAwakenedLegacyProjector.IsDiceOnlySkillGrant(XElement.Parse(effect)));
    }

    [TestMethod]
    public void Complete368_Technomancer_dice_bonus_does_not_silently_change_historical_assessments()
    {
        using var context = ReadyContext.Create(false, talentValue: "Technomancer", includeSkillPurchase: true,
            amendSettings: settings => SetLimits340(settings, 6, 6, 12, 12));
        var inputs = LoadConsumption(context);
        var request = GrantRequest368(inputs);
        foreach (var schema in new[] { CharacterCreationCompleteAssessment.SchemaV1,
            CharacterCreationCompleteAssessment.SchemaV2, CharacterCreationCompleteAssessment.SchemaV3 })
        {
            var old = CharacterCreationCompletionReviewService.Review(inputs, request with { CompleteAssessmentSchema = schema });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, old.Outcome);
            CollectionAssert.Contains(old.Blockers.ToArray(), CharacterCreationSkillKarmaPurchaseBlockers.ModifiersUnsupported);
            Assert.AreEqual(C.Unresolved, old.Value!.CompleteAssessment!.Obligations.Single(row => row.Kind == K.SelectedGrants).Coverage);
        }
    }

    [TestMethod]
    public void Complete368_Real_racial_conflict_cannot_acquire_grant_execution_coverage()
    {
        using var context = ReadyContext.Create(false, talentValue: "Magician", metatypeName: "Ork",
            rankAssignments: CharacterCreationSelectedPrerequisiteTests.OrkRanks(),
            prepareSource: root => CharacterCreationSelectedPrerequisiteTests.RestrictLowLight(root, "magenabled", ""),
            amendSettings: settings => SetLimits340(settings, 6, 6, 12, 12));
        var before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!);
        var inputs = LoadConsumption(context);
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, GrantRequest368(inputs));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, reviewed.Outcome);
        CollectionAssert.Contains(reviewed.Blockers.ToArray(), CharacterCreationSelectedPrerequisiteConsumer.Blocker);
        AssertGrants368(reviewed.Value!.CompleteAssessment!, C.Unresolved);
        Assert.IsNull(reviewed.Value.SelectedPrerequisites);
        Assert.AreEqual(false, reviewed.Value.CompleteAssessment!.MechanicalSucceeded);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!));
    }

}
