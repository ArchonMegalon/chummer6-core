using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationSkillsServiceTests
{
    [TestMethod]
    public void Completion_preview_preserves_explicit_saved_choices_without_rebinding_or_writing()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var initial = Load(service, id);
            CharacterCreationSkillAllocation[] choices =
            [
                new(ActiveIds[2], CharacterCreationSkillKinds.Active, 2, "spec-2", false),
                new(KnowledgeIds[0], CharacterCreationSkillKinds.Knowledge, 3, null, false),
                new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)
            ];
            CharacterCreationSkillGroupAllocation[] groups = [new(initial.Authority.SkillGroups[0].GroupId, 1)];
            var first = service.Preview(new(initial.Binding, choices, groups)).Value!;
            Assert.IsTrue(first.CanConfirm, string.Join(",", first.Blockers));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new(initial.Binding, choices, groups, first.PreviewDigest, "completion-baseline", true)).Outcome);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var proposedAttributes = attributes.PendingDraft!.Allocations.Select(a => a.AttributeId == "LOG"
                ? a with { PriorityPoints = a.PriorityPoints + 1 } : a).ToArray();

            var result = CharacterCreationSkillsService.PreviewWithProposedAttributes(attributes, skills,
                proposedAttributes, skills.PendingDraft!.Allocations, skills.PendingDraft.GroupAllocations);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            var proposed = result.Value!;
            Assert.AreEqual(attributes.SnapshotDigest, proposed.AttributesSnapshotDigest);
            Assert.AreEqual(skills.SnapshotDigest, proposed.SkillsSnapshotDigest);
            Assert.AreEqual(skills.Binding, proposed.Binding, "The binding remains the original saved state, not a pretend commit.");
            Assert.AreEqual(3, proposed.Attributes.Attributes.Single(a => a.AttributeId == "INT").Current);
            Assert.AreEqual(3, proposed.Attributes.Attributes.Single(a => a.AttributeId == "LOG").Current);
            Assert.AreEqual(14m, proposed.KnowledgeSkillPointBudget.Total);
            foreach (var original in choices)
            {
                var projected = proposed.Skills.Single(s => s.SourceSkillId == original.SourceSkillId && s.Kind == original.Kind);
                Assert.AreEqual(original.Rating, projected.Rating);
                Assert.AreEqual(original.SpecializationOptionId, projected.SpecializationOptionId);
                Assert.AreEqual(original.IsNativeLanguage, projected.IsNativeLanguage);
            }
            Assert.AreEqual(groups[0].Rating, proposed.SkillGroups.Single(g => g.GroupId == groups[0].GroupId).Rating);
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                skills.KnowledgePointContributions, proposed.KnowledgePointContributions));
            Assert.IsTrue(CharacterCreationSkillsDigest.IsCanonical(proposed.ProposalDigest));
            Assert.AreEqual(proposed.ProposalDigest,
                CharacterCreationSkillsService.PreviewWithProposedAttributes(attributes, skills,
                    proposedAttributes, skills.PendingDraft.Allocations, skills.PendingDraft.GroupAllocations).Value!.ProposalDigest);
            Assert.IsNull(typeof(CharacterCreationAttributeSkillsPreview).GetProperty("CanConfirm"));
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Completion_preview_knowledge_overflow_matches_fresh_Core_after_equivalent_attribute_save(bool saveSkillsFirst)
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            SaveSkillsReviewAttributes(store, resolver, id, 0, 0);
            var initial = Load(service, id);
            CharacterCreationSkillAllocation[] choices =
            [
                new(ActiveIds[2], CharacterCreationSkillKinds.Active, 3, null, false),
                new(KnowledgeIds[0], CharacterCreationSkillKinds.Knowledge, 6, null, false),
                new(KnowledgeIds[1], CharacterCreationSkillKinds.Knowledge, 6, null, false),
                new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)
            ];
            var oldPreview = service.Preview(new(initial.Binding, choices, [])).Value!;
            Assert.IsTrue(oldPreview.CanConfirm, string.Join(",", oldPreview.Blockers));
            Assert.AreEqual(6m, oldPreview.KnowledgeSkillPointBudget.Total);
            Assert.AreEqual(6, oldPreview.KnowledgePointOverflowToActive);
            Assert.AreEqual(9m, oldPreview.ActiveSkillPointBudget.Used);
            if (saveSkillsFirst)
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                    service.Confirm(new(initial.Binding, choices, [], oldPreview.PreviewDigest,
                        "completion-overflow-baseline", true)).Outcome);

            var attributesService = new CharacterCreationAttributesService(store, resolver);
            var attributes = attributesService.Load(new(id)).Value!;
            var skills = Load(service, id);
            var before = store.Get(id).Value!;
            CharacterCreationAttributeAllocation[] proposedAttributes = [new("INT", 2, 0), new("LOG", 1, 0)];
            var result = CharacterCreationSkillsService.PreviewWithProposedAttributes(attributes, skills,
                proposedAttributes, choices, []);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            var proposed = result.Value!;
            Assert.AreEqual(12m, proposed.KnowledgeSkillPointBudget.Total);
            Assert.AreEqual(0, proposed.KnowledgePointOverflowToActive);
            Assert.AreEqual(3m, proposed.ActiveSkillPointBudget.Used);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);

            // Only this explicit ordinary confirmation writes. The hypothetical
            // result itself is not a fresh SkillsPreview or an apply command.
            var freshAttributePreview = attributesService.Preview(new(attributes.Binding, proposedAttributes)).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                attributesService.Confirm(new(attributes.Binding, proposedAttributes,
                    freshAttributePreview.PreviewDigest, true)).Outcome);
            CharacterCreationSkillsPreview ordinary;
            if (saveSkillsFirst)
            {
                Assert.IsFalse(Load(service, id).CanEdit);
                var review = new CharacterCreationSkillsService(store, resolver).LoadReReview(new(id));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
                ordinary = review.Value!.InitialPreview.CurrentPreview;
            }
            else
            {
                var freshSkills = Load(service, id);
                ordinary = service.Preview(new(freshSkills.Binding, choices, [])).Value!;
            }
            AssertCompletionProjectionMatches(proposed, ordinary);
            Assert.AreEqual(before.ContentRevision + 1, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.Document.Content, store.Get(id).Value!.Document.Content);
            Assert.AreEqual(before.Document.AuxiliaryState.CharacterCreationSkillsDraft?.DraftDigest,
                store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationSkillsDraft?.DraftDigest);
        });
    }

    [TestMethod]
    public void Completion_preview_unchanged_attributes_are_an_honest_noop_not_a_new_draft()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            CharacterCreationSkillAllocation[] choices =
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            var result = CharacterCreationSkillsService.PreviewWithProposedAttributes(attributes, skills,
                attributes.PendingDraft!.Allocations, choices, []);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            var proposed = result.Value!;
            Assert.IsFalse(proposed.Attributes.CanConfirm);
            CollectionAssert.Contains(proposed.Attributes.Blockers.ToList(), CharacterCreationAttributesBlockers.DraftDuplicate);
            Assert.AreEqual(0, proposed.Blockers.Count);
            AssertCompletionProjectionMatches(proposed, service.Preview(new(skills.Binding, choices, [])).Value!);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Completion_preview_rejects_mixed_original_workspace_revision_raw_aux_and_source_bindings()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var original = attributes.Binding;
            CharacterCreationAttributesState[] mixed =
            [
                attributes with { Binding = original with { WorkspaceId = new CharacterWorkspaceId("another-runner") } },
                attributes with { Binding = original with
                    { ContentRevision = original.ContentRevision + 1, SavedRevision = original.SavedRevision + 1 } },
                attributes with { Binding = original with { RawCharacterXmlDigest = Digest('f') } },
                attributes with { Binding = original with { AuxiliaryStateDigest = Digest('f') } },
                attributes with
                {
                    Binding = original with { PrerequisiteAuthorityDigest = Digest('f') },
                    PrerequisiteDraft = attributes.PrerequisiteDraft! with { AuthorityDigest = Digest('f') }
                },
                attributes with { PendingDraft = attributes.PendingDraft! with { DraftDigest = Digest('f') } },
                attributes with { PendingDraft = null }
            ];
            foreach (var changed in mixed)
            {
                var resealed = changed with { SnapshotDigest = string.Empty };
                resealed = resealed with
                {
                    SnapshotDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(resealed)
                };
                Assert.IsNotNull(CharacterCreationAttributesService.CreateDraftSession(resealed),
                    "The cross-snapshot check must reject combinations even when the individual snapshot guard passes.");
                var result = CharacterCreationSkillsService.PreviewWithProposedAttributes(resealed, skills,
                    attributes.PendingDraft!.Allocations,
                    [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)], []);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, result.Outcome);
                Assert.IsNull(result.Value);
                CollectionAssert.Contains(result.Blockers.ToList(), CharacterCreationAttributeSkillsPreviewSchemas.SnapshotConflict);
            }
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Completion_preview_validates_both_original_snapshot_guards_before_using_proposals()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            CharacterCreationSkillAllocation[] choices =
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            var invalidAttributes = CharacterCreationSkillsService.PreviewWithProposedAttributes(
                attributes with { SnapshotDigest = Digest('f') }, skills, attributes.PendingDraft!.Allocations, choices, []);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, invalidAttributes.Outcome);
            Assert.IsNull(invalidAttributes.Value);
            CollectionAssert.Contains(invalidAttributes.Blockers.ToList(), CharacterCreationAttributesBlockers.AuthorityUnavailable);
            CharacterCreationSkillsState[] invalidSkills =
            [
                skills with { SnapshotDigest = Digest('f') },
                skills with { Authority = skills.Authority with { KnowledgePointsExpression = "9999" } },
                skills with { AttributesDraft = null }
            ];
            foreach (var invalid in invalidSkills)
            {
                var result = CharacterCreationSkillsService.PreviewWithProposedAttributes(
                    attributes, invalid, attributes.PendingDraft.Allocations, choices, []);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
                Assert.IsNull(result.Value);
                CollectionAssert.Contains(result.Blockers.ToList(), CharacterCreationSkillsBlockers.AuthorityUnavailable);
            }
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Completion_preview_invalid_attribute_choices_stop_but_illegal_skills_keep_a_blocked_projection()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            CharacterCreationSkillAllocation[] choices =
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            var invalidAttributes = CharacterCreationSkillsService.PreviewWithProposedAttributes(
                attributes, skills, [new("INT", 100, 0)], choices, []);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, invalidAttributes.Outcome);
            Assert.IsNull(invalidAttributes.Value);
            CollectionAssert.Contains(invalidAttributes.Blockers.ToList(), CharacterCreationAttributesBlockers.AllocationInvalid);
            var invalidSkills = CharacterCreationSkillsService.PreviewWithProposedAttributes(
                attributes, skills, attributes.PendingDraft!.Allocations,
                [.. choices, new(ActiveIds[2], CharacterCreationSkillKinds.Active, 7, null, false)], []);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, invalidSkills.Outcome);
            Assert.IsNotNull(invalidSkills.Value);
            CollectionAssert.Contains(invalidSkills.Value.Blockers.ToList(), CharacterCreationSkillsBlockers.RatingInvalid);
            Assert.AreEqual(skills.Binding, invalidSkills.Value.Binding);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Completion_preview_cancellation_and_detached_data_have_no_persisted_effect()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            CharacterCreationAttributeAllocation[] proposedAttributes = [new("INT", 3, 0), new("LOG", 1, 0)];
            CharacterCreationSkillAllocation[] choices =
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            bool canceled = false;
            try
            {
                CharacterCreationSkillsService.PreviewWithProposedAttributes(attributes, skills,
                    proposedAttributes, choices, [], new CancellationToken(true));
            }
            catch (OperationCanceledException) { canceled = true; }
            Assert.IsTrue(canceled);
            var result = CharacterCreationSkillsService.PreviewWithProposedAttributes(attributes, skills,
                proposedAttributes, choices, []);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            var captured = CharacterCreationSkillsDigest.Compute(result.Value);
            proposedAttributes[0] = new("INT", 100, 0);
            choices[0] = new(ActiveIds[2], CharacterCreationSkillKinds.Active, 100, null, false);
            if (skills.KnowledgePointContributions is CharacterCreationKnowledgePointContribution[] contributions)
                contributions[0] = contributions[0] with { Points = 9999 };
            Assert.AreEqual(captured, CharacterCreationSkillsDigest.Compute(result.Value));
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    private static void AssertCompletionProjectionMatches(
        CharacterCreationAttributeSkillsPreview proposed, CharacterCreationSkillsPreview ordinary)
    {
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(proposed.Skills, ordinary.Skills));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(proposed.SkillGroups, ordinary.SkillGroups));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            proposed.KnowledgePointContributions, ordinary.KnowledgePointContributions));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            proposed.ActiveSkillPointBudget, ordinary.ActiveSkillPointBudget));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            proposed.SkillGroupPointBudget, ordinary.SkillGroupPointBudget));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            proposed.KnowledgeSkillPointBudget, ordinary.KnowledgeSkillPointBudget));
        Assert.AreEqual(proposed.KnowledgePointOverflowToActive, ordinary.KnowledgePointOverflowToActive);
        CollectionAssert.AreEqual(proposed.Blockers.ToArray(), ordinary.Blockers.ToArray());
    }

    private static void AssertCompletionWorkspaceUnchanged(WorkspaceStoredDocument before, WorkspaceStoredDocument after)
    {
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
    }
}
