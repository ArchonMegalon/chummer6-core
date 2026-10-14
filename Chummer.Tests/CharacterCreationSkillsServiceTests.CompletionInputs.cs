using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationSkillsServiceTests
{
    [TestMethod]
    public void Completion_inputs_respect_store_rejection_of_an_empty_persisted_skill_receipt_ledger()
    {
        var observer = new CompletionInputWorkspacePathObserver();
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var reader = new CharacterCreationSkillsService(store, resolver);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reader.LoadCompletionInputs(new(id)).Outcome);
            var before = store.Get(id).Value!;
            Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationAttributesDraft);
            Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationSkillsDraft);
            Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationSkillsReceipts);
            Assert.IsNotNull(observer.TargetPath);
            string path = observer.TargetPath!;
            Assert.AreEqual(id.Value + ".json", Path.GetFileName(path));

            // Inject only this malformed persisted field, as the existing
            // history-corruption tests do. The actual FileWorkspaceStore and
            // all normal source/history validators remain in use.
            var record = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            record["AuxiliaryState"]!.AsObject()["CharacterCreationSkillsReceipts"] =
                new System.Text.Json.Nodes.JsonArray();
            File.WriteAllText(path, record.ToJsonString());
            byte[] corrupted = File.ReadAllBytes(path);
            // The real store rejects malformed auxiliary history before either
            // service receives a snapshot; do not invent a readable document or
            // mistake this storage-boundary check for isolated service coverage.
            var retained = store.Get(id);
            Assert.AreEqual(WorkspaceOperationOutcome.Corrupt, retained.Outcome);
            Assert.IsNull(retained.Value);
            var ordinary = service.Load(new(id));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, ordinary.Outcome);
            Assert.IsNull(ordinary.Value);
            CollectionAssert.Contains(ordinary.Blockers.ToList(), CharacterCreationSkillsBlockers.WorkspaceUnavailable);

            var result = reader.LoadCompletionInputs(new(id));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, result.Outcome);
            Assert.IsNull(result.Value, "An empty ledger is not the same as no historical ledger.");
            CollectionAssert.Contains(result.Blockers.ToList(), CharacterCreationSkillsBlockers.WorkspaceUnavailable);
            Assert.AreEqual(WorkspaceOperationOutcome.Corrupt, store.Get(id).Outcome);
            CollectionAssert.AreEqual(corrupted, File.ReadAllBytes(path));
        }, faultInjector: observer, saveAttributes: false);
    }

    [TestMethod]
    public void Completion_inputs_before_first_attribute_save_match_ordinary_Core_without_inventing_a_draft()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var before = store.Get(id).Value!;
            var ordinaryBefore = Load(service, id);
            Assert.IsFalse(ordinaryBefore.CanEdit);
            CollectionAssert.Contains(ordinaryBefore.Blockers.ToList(), CharacterCreationSkillsBlockers.AttributesDraftRequired);
            var reader = new CharacterCreationSkillsService(store, resolver);
            var loaded = reader.LoadCompletionInputs(new(id));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
            var inputs = loaded.Value!;
            Assert.IsNull(inputs.Attributes.PendingDraft);
            Assert.AreEqual(0L, inputs.Binding.AttributesDraftRevision);
            Assert.AreEqual(string.Empty, inputs.Binding.AttributesDraftDigest);
            Assert.AreEqual(before.ContentRevision, inputs.Binding.ContentRevision);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, inputs.Binding.AuxiliaryStateDigest);
            Assert.AreEqual(0, inputs.Allocations.Count);
            Assert.AreEqual(0, inputs.GroupAllocations.Count);
            Assert.IsNull(typeof(CharacterCreationSkillsCompletionInputs).GetProperty("CanConfirm"));
            Assert.AreNotEqual(ordinaryBefore.Schema, inputs.Schema);
            Assert.IsTrue(CharacterCreationSkillsDigest.IsCanonical(inputs.SnapshotDigest));

            CharacterCreationAttributeAllocation[] attributes = [new("INT", 2, 0), new("LOG", 1, 0)];
            CharacterCreationSkillAllocation[] choices =
            [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true),
                new(ActiveIds[2], CharacterCreationSkillKinds.Active, 3, "spec-2", false),
                .. KnowledgeIds.Select(sourceId => new CharacterCreationSkillAllocation(
                    sourceId, CharacterCreationSkillKinds.Knowledge, 6, null, false))];
            CharacterCreationSkillGroupAllocation[] groups = [new(inputs.Authority.SkillGroups.Single().GroupId, 1)];
            var proposed = CharacterCreationSkillsService.PreviewWithProposedAttributes(inputs, attributes, choices, groups);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, proposed.Outcome, string.Join(",", proposed.Blockers));
            Assert.AreEqual(12m, proposed.Value!.KnowledgeSkillPointBudget.Total);
            Assert.AreEqual(0, proposed.Value.KnowledgePointOverflowToActive);
            Assert.AreEqual(4m, proposed.Value.ActiveSkillPointBudget.Used);
            Assert.AreEqual(inputs.Binding, proposed.Value.Binding);
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                inputs.KnowledgePointContributions, proposed.Value.KnowledgePointContributions));
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
            Assert.IsNull(store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationAttributesDraft);
            Assert.IsNull(store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationSkillsDraft);
            Assert.IsFalse(Load(service, id).CanEdit, "A hypothetical preview cannot unlock ordinary Skills confirmation.");

            // Only this normal explicit confirmation creates the first draft.
            var attributesService = new CharacterCreationAttributesService(store, resolver);
            var actualAttributes = attributesService.Preview(new(inputs.Attributes.Binding, attributes)).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                attributesService.Confirm(new(actualAttributes.Binding, attributes, actualAttributes.PreviewDigest, true)).Outcome);
            var ordinaryState = Load(service, id);
            Assert.IsTrue(ordinaryState.CanEdit);
            AssertCompletionProjectionMatches(proposed.Value,
                service.Preview(new(ordinaryState.Binding, choices, groups)).Value!);
            Assert.AreEqual(before.ContentRevision + 1, store.Get(id).Value!.ContentRevision);
            Assert.IsNull(store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationSkillsDraft);
            Assert.AreEqual(before.Document.Content, store.Get(id).Value!.Document.Content);
        }, saveAttributes: false);
    }

    [TestMethod]
    public void Completion_inputs_plan_before_attributes_retains_missing_native_and_exact_proposed_budgets()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var before = store.Get(id).Value!;
            var inputs = new CharacterCreationSkillsService(store, resolver).LoadCompletionInputs(new(id)).Value!;
            CharacterCreationAttributeAllocation[] attributes = [new("INT", 2, 0), new("LOG", 1, 0)];
            var request = InitialSkillCompletionRequest(inputs,
                [new(CharacterCreationSkillKinds.Active, ActiveIds[2]),
                    new(CharacterCreationSkillKinds.Knowledge, KnowledgeIds[0])])
                with { ActivePointsToReserve = 26, GroupPointsToReserve = 2, KnowledgePointsToReserve = 10 };
            var result = CharacterCreationSkillCompletionPlanner.Plan(inputs, attributes, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            var plan = result.Value!;
            Assert.IsTrue(plan.HasChanges);
            Assert.AreEqual(2, plan.Allocations.Single(a => a.SourceSkillId == ActiveIds[2]).Rating);
            Assert.AreEqual(2, plan.Allocations.Single(a => a.SourceSkillId == KnowledgeIds[0]).Rating);
            Assert.IsFalse(plan.Allocations.Any(a => a.IsNativeLanguage || a.SpecializationOptionId is not null));
            Assert.AreEqual(12m, plan.Preview.KnowledgeSkillPointBudget.Total);
            CollectionAssert.AreEquivalent(new[] { CharacterCreationSkillsBlockers.NativeLanguageRequired },
                plan.Blockers.ToArray());
            Assert.IsTrue(plan.Evaluations is >= 1 and <= 257);
            Assert.IsTrue(CharacterCreationSkillsDigest.IsCanonical(plan.PlanDigest));
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(attributes, plan.AttributeAllocations));
            Assert.AreEqual(plan.PlanDigest,
                CharacterCreationSkillCompletionPlanner.Plan(inputs, attributes, request).Value!.PlanDigest);
            Assert.IsFalse(Load(service, id).CanEdit);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        }, saveAttributes: false);
    }

    [TestMethod]
    public void Completion_inputs_require_admitted_prerequisite_and_unchanged_source_authority()
    {
        WithReadySkillsContext((store, _, id, authority, prerequisite, resolver) =>
        {
            var before = store.Get(id).Value!;
            var readers = new[]
            {
                new CharacterCreationSkillsService(store,
                    new CharacterCreationAttributesServiceTests.StubSourceResolver(prerequisite)),
                new CharacterCreationSkillsService(store, CreateResolver(prerequisite,
                    Reseal(authority with { RuntimeDigest = Digest('f') }))),
                new CharacterCreationSkillsService(store, CreateResolver(prerequisite,
                    Reseal(authority with { RawProfileInputsDigest = Digest('f') }))),
                new CharacterCreationSkillsService(store, CreateResolver(
                    prerequisite with { AuthorityDigest = Digest('f') }, authority)),
                new CharacterCreationSkillsService(store, CreateResolver(prerequisite,
                    CreateSkillsAuthority(prerequisite, ReadyXml.Replace("Skills Runner", "Different source runner"))))
            };
            foreach (var reader in readers)
            {
                var result = reader.LoadCompletionInputs(new(id));
                Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
                Assert.IsNull(result.Value);
                Assert.IsTrue(result.Blockers.Count > 0);
                AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
            }
            var unseeded = new CharacterWorkspaceId("completion-without-prerequisite");
            Assert.IsTrue(store.CreateWorkspaceDocument(unseeded, new WorkspaceDocument(ReadyXml, RulesetDefaults.Sr5)).Success);
            var unseededBefore = store.Get(unseeded).Value!;
            var missingPrerequisite = new CharacterCreationSkillsService(store, resolver).LoadCompletionInputs(new(unseeded));
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, missingPrerequisite.Outcome);
            Assert.IsNull(missingPrerequisite.Value);
            AssertCompletionWorkspaceUnchanged(unseededBefore, store.Get(unseeded).Value!);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        }, saveAttributes: false);
    }

    [TestMethod]
    public void Completion_inputs_fresh_reload_rejects_old_planner_binding_without_claiming_offline_freshness()
    {
        WithReadySkillsContext((store, _, id, _, _, resolver) =>
        {
            var reader = new CharacterCreationSkillsService(store, resolver);
            var initial = reader.LoadCompletionInputs(new(id)).Value!;
            var request = InitialSkillCompletionRequest(initial, []);
            CharacterCreationAttributeAllocation[] attributes = [new("INT", 2, 0), new("LOG", 1, 0)];
            CharacterCreationSkillAllocation[] language = [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            var detached = CharacterCreationSkillsService.PreviewWithProposedAttributes(initial, attributes, language, []).Value!;
            var attributesService = new CharacterCreationAttributesService(store, resolver);
            var actual = attributesService.Preview(new(initial.Attributes.Binding, attributes)).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                attributesService.Confirm(new(actual.Binding, attributes, actual.PreviewDigest, true)).Outcome);
            var afterSave = store.Get(id).Value!;
            var fresh = reader.LoadCompletionInputs(new(id));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, fresh.Outcome, string.Join(",", fresh.Blockers));
            Assert.AreNotEqual(initial.SnapshotDigest, fresh.Value!.SnapshotDigest);
            Assert.IsNotNull(fresh.Value.Attributes.PendingDraft);
            Assert.IsTrue(fresh.Value.Binding.AttributesDraftRevision > 0);
            var stale = CharacterCreationSkillCompletionPlanner.Plan(fresh.Value, attributes, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, stale.Outcome);
            Assert.IsNull(stale.Value);
            CollectionAssert.Contains(stale.Blockers.ToList(), CharacterCreationSkillCompletionBlockers.SnapshotConflict);
            Assert.AreEqual(detached.ProposalDigest,
                CharacterCreationSkillsService.PreviewWithProposedAttributes(initial, attributes, language, []).Value!.ProposalDigest,
                "A detached projection remains historical data; it does not query the store or authorize applying it.");
            AssertCompletionWorkspaceUnchanged(afterSave, store.Get(id).Value!);
        }, saveAttributes: false);
    }

    [TestMethod]
    public void Completion_inputs_preserve_saved_skill_choices_and_cannot_bypass_required_history_review()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            string groupId = Load(service, id).Authority.SkillGroups.Single().GroupId;
            SaveCompletionSkills(service, id,
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true),
                    new(ActiveIds[2], CharacterCreationSkillKinds.Active, 2, "spec-2", false)], [new(groupId, 1)]);
            var before = store.Get(id).Value!;
            var reader = new CharacterCreationSkillsService(store, resolver);
            var inputsResult = reader.LoadCompletionInputs(new(id));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, inputsResult.Outcome,
                string.Join(",", inputsResult.Blockers));
            var inputs = inputsResult.Value!;
            var saved = Load(service, id).PendingDraft!;
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.Allocations, inputs.Allocations));
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.GroupAllocations, inputs.GroupAllocations));
            var request = InitialSkillCompletionRequest(inputs, []) with
                { ActivePointsToReserve = 25, GroupPointsToReserve = 1, KnowledgePointsToReserve = 12 };
            var noOp = CharacterCreationSkillCompletionPlanner.Plan(inputs, inputs.Attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, noOp.Outcome, string.Join(",", noOp.Blockers));
            Assert.IsFalse(noOp.Value!.HasChanges);
            Assert.AreEqual("spec-2", noOp.Value.Allocations.Single(a => a.SourceSkillId == ActiveIds[2]).SpecializationOptionId);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);

            SaveSkillsReviewAttributes(store, resolver, id, 3, 1);
            var beforeReview = store.Get(id).Value!;
            Assert.IsFalse(Load(service, id).CanEdit);
            var needsReview = reader.LoadCompletionInputs(new(id));
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, needsReview.Outcome);
            Assert.IsNull(needsReview.Value, "Existing stale Skills history cannot be silently replaced by initial choices.");
            Assert.AreEqual(saved.DraftDigest,
                beforeReview.Document.AuxiliaryState.CharacterCreationSkillsDraft!.DraftDigest);
            AssertCompletionWorkspaceUnchanged(beforeReview, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Completion_inputs_invalid_snapshot_proposals_and_cancellation_remain_read_only_and_detached()
    {
        WithReadySkillsContext((store, _, id, _, _, resolver) =>
        {
            var before = store.Get(id).Value!;
            var inputs = new CharacterCreationSkillsService(store, resolver).LoadCompletionInputs(new(id)).Value!;
            CharacterCreationAttributeAllocation[] attributes = [new("INT", 2, 0), new("LOG", 1, 0)];
            CharacterCreationSkillAllocation[] choices = [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            var badSnapshot = CharacterCreationSkillsService.PreviewWithProposedAttributes(
                inputs with { SnapshotDigest = Digest('f') }, attributes, choices, []);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, badSnapshot.Outcome);
            Assert.IsNull(badSnapshot.Value);
            var badAttributes = CharacterCreationSkillsService.PreviewWithProposedAttributes(inputs,
                [new("INT", 100, 0)], choices, []);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, badAttributes.Outcome);
            Assert.IsNull(badAttributes.Value);
            var invalidSkill = CharacterCreationSkillsService.PreviewWithProposedAttributes(inputs, attributes,
                [.. choices, new(ActiveIds[2], CharacterCreationSkillKinds.Active, 7, null, false)], []);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, invalidSkill.Outcome);
            Assert.IsNotNull(invalidSkill.Value);
            CollectionAssert.Contains(invalidSkill.Blockers.ToList(), CharacterCreationSkillsBlockers.RatingInvalid);
            bool previewCanceled = false;
            try
            {
                CharacterCreationSkillsService.PreviewWithProposedAttributes(inputs,
                    attributes, choices, [], new CancellationToken(true));
            }
            catch (OperationCanceledException) { previewCanceled = true; }
            Assert.IsTrue(previewCanceled);
            bool plannerCanceled = false;
            try
            {
                CharacterCreationSkillCompletionPlanner.Plan(inputs, attributes,
                    InitialSkillCompletionRequest(inputs, []), new CancellationToken(true));
            }
            catch (OperationCanceledException) { plannerCanceled = true; }
            Assert.IsTrue(plannerCanceled);
            var result = CharacterCreationSkillsService.PreviewWithProposedAttributes(inputs, attributes, choices, []);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            var captured = CharacterCreationSkillsDigest.Compute(result.Value);
            attributes[0] = new("INT", 100, 0);
            choices[0] = new(ActiveIds[2], CharacterCreationSkillKinds.Active, 100, null, false);
            if (inputs.KnowledgePointContributions is CharacterCreationKnowledgePointContribution[] contributions)
                contributions[0] = contributions[0] with { Points = 9999 };
            Assert.AreEqual(captured, CharacterCreationSkillsDigest.Compute(result.Value));
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        }, saveAttributes: false);
    }

    private static CharacterCreationSkillCompletionRequest InitialSkillCompletionRequest(
        CharacterCreationSkillsCompletionInputs inputs, IReadOnlyList<CharacterCreationSkillCompletionTarget> targets)
        => new(inputs.Binding, inputs.Attributes.SnapshotDigest, inputs.SnapshotDigest, targets);

    private sealed class CompletionInputWorkspacePathObserver : IFileWorkspaceStoreFaultInjector
    {
        public string? TargetPath { get; private set; }

        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            _ = tempPath;
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
                TargetPath = targetPath;
        }
    }
}
