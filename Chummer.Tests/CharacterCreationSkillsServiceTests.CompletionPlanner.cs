using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationSkillsServiceTests
{
    [TestMethod]
    public void Skill_completion_balanced_fill_meets_floors_and_fills_independent_pools_without_breaking_groups()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var group = skills.Authority.SkillGroups.Single();
            var request = SkillCompletionRequest(attributes, skills,
            [
                .. ActiveIds.Select(sourceId => new CharacterCreationSkillCompletionTarget(CharacterCreationSkillKinds.Active, sourceId)),
                .. KnowledgeIds.Select(sourceId => new CharacterCreationSkillCompletionTarget(CharacterCreationSkillKinds.Knowledge, sourceId)),
                new(CharacterCreationSkillCompletionKinds.Group, group.GroupId)
            ]) with
            {
                NativeLanguageSourceId = LanguageId,
                ActivePointsToReserve = 8,
                Targets = [new(CharacterCreationSkillCompletionKinds.Group, group.GroupId, 1),
                    .. ActiveIds.Skip(2).Select(sourceId => new CharacterCreationSkillCompletionRatingTarget(
                        CharacterCreationSkillKinds.Active, sourceId, 2)),
                    .. KnowledgeIds.Select(sourceId => new CharacterCreationSkillCompletionRatingTarget(
                        CharacterCreationSkillKinds.Knowledge, sourceId, 1))],
                FillRemainingPoints = true
            };
            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            var plan = result.Value!;
            Assert.AreEqual(8m, plan.Preview.ActiveSkillPointBudget.Remaining);
            Assert.AreEqual(0m, plan.Preview.SkillGroupPointBudget.Remaining);
            Assert.AreEqual(0m, plan.Preview.KnowledgeSkillPointBudget.Remaining);
            Assert.AreEqual(2, plan.GroupAllocations.Single().Rating);
            Assert.IsFalse(plan.Allocations.Any(row => group.MemberSkillSourceIds.Contains(row.SourceSkillId)),
                "Group closure permissions must not buy independent member rows or break the group.");
            foreach (var sourceId in ActiveIds.Skip(2))
                Assert.AreEqual(5, plan.Allocations.Single(row => row.SourceSkillId == sourceId).Rating);
            foreach (var sourceId in KnowledgeIds)
                Assert.AreEqual(6, plan.Allocations.Single(row => row.SourceSkillId == sourceId).Rating);
            Assert.IsTrue(plan.Allocations.Single(row => row.SourceSkillId == LanguageId).IsNativeLanguage);
            Assert.IsNull(plan.Allocations.Single(row => row.SourceSkillId == LanguageId).Rating);
            Assert.IsTrue(plan.Evaluations <= CharacterCreationSkillCompletionPlanner.MaximumCandidateEvaluations);
            Assert.AreEqual(plan.PlanDigest, CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft.Allocations, request).Value!.PlanDigest);
            AssertCompletionProjectionMatches(plan.Preview,
                service.Preview(new(skills.Binding, plan.Allocations, plan.GroupAllocations)).Value!);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_balanced_fill_preserves_omitted_manual_zero_specialization_and_native_choices()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            CharacterCreationSkillAllocation[] chosen =
            [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true),
                new(ActiveIds[2], CharacterCreationSkillKinds.Active, 2, "spec-2", false),
                new(KnowledgeIds[0], CharacterCreationSkillKinds.Knowledge, 3, null, false)];
            // The ordinary skill contract represents zero by omission, not a
            // saved numeric-zero row. Withholding permission for that identity
            // must preserve the zero choice while other rows are completed.
            var explicitZero = service.Preview(new(Load(service, id).Binding,
                [.. chosen, new(ActiveIds[3], CharacterCreationSkillKinds.Active, 0, null, false)], [])).Value!;
            Assert.IsFalse(explicitZero.CanConfirm);
            CollectionAssert.Contains(explicitZero.Blockers.ToArray(), CharacterCreationSkillsBlockers.RatingInvalid);
            SaveCompletionSkills(service, id, chosen, []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            Assert.IsFalse(skills.PendingDraft!.Allocations.Any(row => row.SourceSkillId == ActiveIds[3]));
            var request = SkillCompletionRequest(attributes, skills,
                [new(CharacterCreationSkillKinds.Active, ActiveIds[4]), new(CharacterCreationSkillKinds.Active, ActiveIds[5]),
                    new(CharacterCreationSkillKinds.Knowledge, KnowledgeIds[1]),
                    new(CharacterCreationSkillKinds.Knowledge, LanguageId)]) with
            {
                ActivePointsToReserve = 19, GroupPointsToReserve = 2, KnowledgePointsToReserve = 3,
                Targets = [new(CharacterCreationSkillKinds.Active, ActiveIds[4], 1),
                    new(CharacterCreationSkillKinds.Active, ActiveIds[5], 1)], FillRemainingPoints = true
            };
            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            var plan = result.Value!;
            Assert.AreEqual(3, plan.Allocations.Single(row => row.SourceSkillId == ActiveIds[4]).Rating);
            Assert.AreEqual(3, plan.Allocations.Single(row => row.SourceSkillId == ActiveIds[5]).Rating);
            Assert.AreEqual(6, plan.Allocations.Single(row => row.SourceSkillId == KnowledgeIds[1]).Rating);
            Assert.IsFalse(plan.Allocations.Any(row => row.SourceSkillId == ActiveIds[3]));
            foreach (var original in skills.PendingDraft.Allocations)
                Assert.AreEqual(original, plan.Allocations.Single(row => row.Kind == original.Kind && row.SourceSkillId == original.SourceSkillId));
            Assert.AreEqual(19m, plan.Preview.ActiveSkillPointBudget.Remaining);
            Assert.AreEqual(2m, plan.Preview.SkillGroupPointBudget.Remaining);
            Assert.AreEqual(3m, plan.Preview.KnowledgeSkillPointBudget.Remaining);
            AssertCompletionProjectionMatches(plan.Preview,
                service.Preview(new(skills.Binding, plan.Allocations, plan.GroupAllocations)).Value!);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_balanced_fill_without_floors_retains_group_member_permission_and_source_gates()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var group = skills.Authority.SkillGroups.Single();
            var request = SkillCompletionRequest(attributes, skills,
                [new(CharacterCreationSkillKinds.Active, ActiveIds[0]),
                    new(CharacterCreationSkillKinds.Active, ActiveIds[1]),
                    new(CharacterCreationSkillCompletionKinds.Group, group.GroupId)]) with
            {
                NativeLanguageSourceId = LanguageId, ActivePointsToReserve = 28, KnowledgePointsToReserve = 12,
                FillRemainingPoints = true
            };
            foreach (var floors in new IReadOnlyList<CharacterCreationSkillCompletionRatingTarget>?[] { null, [] })
            {
                var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                    attributes.PendingDraft!.Allocations, request with { Targets = floors });
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
                Assert.AreEqual(2, result.Value!.GroupAllocations.Single().Rating);
                Assert.IsFalse(result.Value.Allocations.Any(row => row.Kind == CharacterCreationSkillKinds.Active));
            }
            var blocked = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request with
                {
                    PermittedTargetOrder = [new(CharacterCreationSkillCompletionKinds.Group, group.GroupId),
                        new(CharacterCreationSkillKinds.Active, ActiveIds[1])],
                    ActivePointsToReserve = 26, Targets = []
                });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, blocked.Outcome);
            Assert.AreEqual(0, blocked.Value!.GroupAllocations.Count);
            Assert.IsFalse(blocked.Value.Allocations.Any(row => row.Kind == CharacterCreationSkillKinds.Active),
                "Known fly-only skill permission must not bypass movement admission.");
            CollectionAssert.Contains(blocked.Blockers.ToArray(), CharacterCreationSkillCompletionBlockers.GroupMemberLocked);
            CollectionAssert.Contains(blocked.Blockers.ToArray(), CharacterCreationSkillCompletionBlockers.GroupPointsUnallocated);
            CollectionAssert.Contains(blocked.Blockers.ToArray(), CharacterCreationSkillCompletionBlockers.ActivePointsUnallocated);
            Assert.IsTrue(blocked.Value.Evaluations <= CharacterCreationSkillCompletionPlanner.MaximumCandidateEvaluations);
            Assert.Throws<OperationCanceledException>(() => CharacterCreationSkillCompletionPlanner.Plan(attributes,
                skills, attributes.PendingDraft.Allocations, request, new CancellationToken(true)));
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_balanced_fill_optional_locked_group_does_not_block_a_completed_pool()
    {
        WithRealTalentSkills("Mundane", (_, store, resolver, service, id) =>
        {
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var locked = skills.Authority.SkillGroups.Single(group => group.Name == "Athletics");
            var permitted = skills.Authority.SkillGroups.Single(group => group.Name == "Stealth");
            Assert.IsFalse(locked.MemberSkillSourceIds.Intersect(permitted.MemberSkillSourceIds).Any());
            var baseline = service.Preview(new(skills.Binding, [], [])).Value!;
            Assert.IsTrue(baseline.SkillGroupPointBudget.Remaining > 0);
            var request = SkillCompletionRequest(attributes, skills,
                [new(CharacterCreationSkillCompletionKinds.Group, locked.GroupId),
                    new(CharacterCreationSkillCompletionKinds.Group, permitted.GroupId),
                    .. permitted.MemberSkillSourceIds.Select(sourceId => new CharacterCreationSkillCompletionTarget(
                        CharacterCreationSkillKinds.Active, sourceId))]) with
            {
                NativeLanguageSourceId = skills.Authority.KnowledgeSkills.First(skill => skill.CanBeNativeLanguage).SourceSkillId,
                ActivePointsToReserve = (int)baseline.ActiveSkillPointBudget.Remaining,
                KnowledgePointsToReserve = (int)baseline.KnowledgeSkillPointBudget.Remaining,
                FillRemainingPoints = true
            };
            foreach (var floors in new IReadOnlyList<CharacterCreationSkillCompletionRatingTarget>?[]
                     { null, [], [new(CharacterCreationSkillCompletionKinds.Group, permitted.GroupId, 1)] })
            {
                var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                    attributes.PendingDraft!.Allocations, request with { Targets = floors });
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
                Assert.AreEqual(0m, result.Value!.Preview.SkillGroupPointBudget.Remaining);
                Assert.AreEqual(permitted.GroupId, result.Value.GroupAllocations.Single().GroupId);
                CollectionAssert.DoesNotContain(result.Blockers.ToArray(), CharacterCreationSkillCompletionBlockers.GroupMemberLocked);
            }
            var unmetFloor = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request with
                {
                    Targets = [new(CharacterCreationSkillCompletionKinds.Group, locked.GroupId, 1)]
                });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, unmetFloor.Outcome);
            Assert.AreEqual(0m, unmetFloor.Value!.Preview.SkillGroupPointBudget.Remaining);
            CollectionAssert.Contains(unmetFloor.Blockers.ToArray(), CharacterCreationSkillCompletionBlockers.TargetsUnfulfilled);
            CollectionAssert.Contains(unmetFloor.Blockers.ToArray(), CharacterCreationSkillCompletionBlockers.GroupMemberLocked);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_cannot_turn_a_saved_paid_language_into_a_free_native_language()
    {
        WithRealTalentSkills("Magician", (_, store, resolver, service, id) =>
        {
            var initial = Load(service, id);
            var languages = initial.Authority.KnowledgeSkills.Where(row => row.CanBeNativeLanguage).Take(2).ToArray();
            Assert.AreEqual(2, languages.Length);
            SaveCompletionSkills(service, id,
                [new(languages[0].SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true),
                    new(languages[1].SourceSkillId, CharacterCreationSkillKinds.Knowledge, 2, null, false)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var request = SkillCompletionRequest(attributes, skills, []) with
            {
                NativeLanguageSourceId = languages[1].SourceSkillId
            };
            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            Assert.IsNull(result.Value);
            CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationSkillCompletionBlockers.NativeLanguageChoiceConflict);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_repeating_the_existing_native_choice_preserves_the_actual_saved_selection()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            SaveCompletionSkills(service, id,
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true),
                    new(KnowledgeIds[0], CharacterCreationSkillKinds.Knowledge, 2, null, false)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var request = SkillCompletionRequest(attributes, skills, []) with
            {
                NativeLanguageSourceId = LanguageId,
                ActivePointsToReserve = 28, GroupPointsToReserve = 2, KnowledgePointsToReserve = 10
            };
            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            Assert.IsFalse(result.Value!.HasChanges);
            Assert.AreEqual(1, result.Value.Allocations.Count(row => row.IsNativeLanguage));
            Assert.AreEqual(2, result.Value.Allocations.Single(row => row.SourceSkillId == KnowledgeIds[0]).Rating);
            var ordinary = service.Preview(new(skills.Binding, result.Value.Allocations, result.Value.GroupAllocations)).Value!;
            CollectionAssert.AreEqual(new[] { CharacterCreationSkillsBlockers.DraftDuplicate }, ordinary.Blockers.ToArray());
            Assert.IsFalse(ordinary.CanConfirm);
            Assert.AreEqual(0, result.Value.Preview.Blockers.Count);
            // The ordinary save route rejects duplicate writes. A read-only
            // proposal retains the same rows/budgets without that write blocker.
            AssertCompletionProjectionMatches(result.Value.Preview, ordinary with { Blockers = [] });
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_preserves_chosen_and_omitted_values_and_obeys_preference_reserves()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var initial = Load(service, id);
            string groupId = initial.Authority.SkillGroups[0].GroupId;
            SaveCompletionSkills(service, id,
            [
                new(ActiveIds[2], CharacterCreationSkillKinds.Active, 2, "spec-2", false),
                new(ActiveIds[3], CharacterCreationSkillKinds.Active, 1, null, false),
                new(KnowledgeIds[0], CharacterCreationSkillKinds.Knowledge, 3, null, false),
                new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)
            ], [new(groupId, 1)]);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var request = SkillCompletionRequest(attributes, skills,
            [
                new(CharacterCreationSkillKinds.Active, ActiveIds[2]),
                new(CharacterCreationSkillKinds.Active, ActiveIds[4]),
                new(CharacterCreationSkillKinds.Knowledge, KnowledgeIds[1]),
                new(CharacterCreationSkillKinds.Knowledge, LanguageId)
            ]) with { ActivePointsToReserve = 18, GroupPointsToReserve = 1, KnowledgePointsToReserve = 3 };

            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            var plan = result.Value!;
            Assert.IsTrue(plan.HasChanges);
            Assert.AreEqual(6, plan.Allocations.Single(a => a.SourceSkillId == ActiveIds[2]).Rating);
            Assert.AreEqual("spec-2", plan.Allocations.Single(a => a.SourceSkillId == ActiveIds[2]).SpecializationOptionId);
            Assert.AreEqual(2, plan.Allocations.Single(a => a.SourceSkillId == ActiveIds[4]).Rating);
            Assert.AreEqual(1, plan.Allocations.Single(a => a.SourceSkillId == ActiveIds[3]).Rating);
            Assert.AreEqual(3, plan.Allocations.Single(a => a.SourceSkillId == KnowledgeIds[0]).Rating);
            Assert.AreEqual(6, plan.Allocations.Single(a => a.SourceSkillId == KnowledgeIds[1]).Rating);
            Assert.IsTrue(plan.Allocations.Single(a => a.SourceSkillId == LanguageId).IsNativeLanguage);
            Assert.IsNull(plan.Allocations.Single(a => a.SourceSkillId == LanguageId).Rating);
            Assert.AreEqual(1, plan.GroupAllocations.Single().Rating);
            Assert.IsFalse(plan.Allocations.Any(a => a.SourceSkillId == ActiveIds[0]
                || a.SourceSkillId == ActiveIds[1] || a.SourceSkillId == ActiveIds[5]));
            Assert.AreEqual(18m, plan.Preview.ActiveSkillPointBudget.Remaining);
            Assert.AreEqual(1m, plan.Preview.SkillGroupPointBudget.Remaining);
            Assert.AreEqual(3m, plan.Preview.KnowledgeSkillPointBudget.Remaining);
            Assert.IsTrue(plan.Evaluations is >= 1 and <= 257);
            Assert.IsTrue(CharacterCreationSkillsDigest.IsCanonical(plan.PlanDigest));
            Assert.AreEqual(plan.PlanDigest, CharacterCreationSkillCompletionPlanner.Plan(
                attributes, skills, attributes.PendingDraft.Allocations, request).Value!.PlanDigest);
            AssertCompletionProjectionMatches(plan.Preview,
                service.Preview(new(skills.Binding, plan.Allocations, plan.GroupAllocations)).Value!);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_group_permission_includes_disabled_canonical_members_without_materializing_them()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            SaveCompletionSkills(service, id,
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var group = skills.Authority.SkillGroups.Single();
            Assert.IsTrue(skills.Authority.ActiveSkills.Single(s => s.SourceSkillId == ActiveIds[1]).RequiresFlyMovement);
            Assert.IsFalse(skills.MovementCapability.Fly);
            var lockedRequest = SkillCompletionRequest(attributes, skills,
            [new(CharacterCreationSkillCompletionKinds.Group, group.GroupId),
                new(CharacterCreationSkillKinds.Active, ActiveIds[0])])
                with { ActivePointsToReserve = 28, KnowledgePointsToReserve = 12 };
            var locked = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, lockedRequest);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, locked.Outcome);
            Assert.IsNotNull(locked.Value);
            Assert.AreEqual(0, locked.Value.GroupAllocations.Count);
            CollectionAssert.Contains(locked.Blockers.ToList(), CharacterCreationSkillCompletionBlockers.GroupMemberLocked);

            var allowedRequest = lockedRequest with
            {
                PermittedTargetOrder = [.. lockedRequest.PermittedTargetOrder,
                    new(CharacterCreationSkillKinds.Active, ActiveIds[1])]
            };
            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft.Allocations, allowedRequest);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            var plan = result.Value!;
            Assert.AreEqual(2, plan.GroupAllocations.Single().Rating);
            Assert.IsFalse(plan.Allocations.Any(a => group.MemberSkillSourceIds.Contains(a.SourceSkillId)));
            CollectionAssert.AreEqual(group.MemberSkillSourceIds.ToArray(),
                plan.Preview.SkillGroups.Single().MemberSkillSourceIds.ToArray());
            Assert.AreEqual(0m, plan.Preview.ActiveSkillPointBudget.Used);
            AssertCompletionProjectionMatches(plan.Preview,
                service.Preview(new(skills.Binding, plan.Allocations, plan.GroupAllocations)).Value!);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_never_replaces_an_individual_or_specialization_to_buy_a_group()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            SaveCompletionSkills(service, id,
            [new(ActiveIds[0], CharacterCreationSkillKinds.Active, 1, "spec-0", false),
                new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var group = skills.Authority.SkillGroups.Single();
            var request = SkillCompletionRequest(attributes, skills,
            [new(CharacterCreationSkillCompletionKinds.Group, group.GroupId),
                .. group.MemberSkillSourceIds.Select(sourceId =>
                    new CharacterCreationSkillCompletionTarget(CharacterCreationSkillKinds.Active, sourceId))])
                with { ActivePointsToReserve = 26, KnowledgePointsToReserve = 12 };
            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            Assert.IsNotNull(result.Value);
            Assert.IsFalse(result.Value.HasChanges);
            Assert.AreEqual(0, result.Value.GroupAllocations.Count);
            var individual = result.Value.Allocations.Single(a => a.SourceSkillId == ActiveIds[0]);
            Assert.AreEqual(1, individual.Rating);
            Assert.AreEqual("spec-0", individual.SpecializationOptionId);
            CollectionAssert.Contains(result.Blockers.ToList(), CharacterCreationSkillCompletionBlockers.GroupPointsUnallocated);
            Assert.AreEqual(0, result.Value.Preview.Blockers.Count);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_knowledge_cannot_increase_overflow_and_proposed_attributes_release_active_points()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            SaveSkillsReviewAttributes(store, resolver, id, 0, 0);
            SaveCompletionSkills(service, id,
            [new(ActiveIds[2], CharacterCreationSkillKinds.Active, 2, null, false),
                new(KnowledgeIds[0], CharacterCreationSkillKinds.Knowledge, 6, null, false),
                new(KnowledgeIds[1], CharacterCreationSkillKinds.Knowledge, 1, null, false),
                new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var request = SkillCompletionRequest(attributes, skills,
                [new(CharacterCreationSkillKinds.Knowledge, KnowledgeIds[1]),
                    new(CharacterCreationSkillKinds.Active, ActiveIds[2])])
                with { ActivePointsToReserve = 23, GroupPointsToReserve = 2 };
            var unchangedAttributes = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, unchangedAttributes.Outcome,
                string.Join(",", unchangedAttributes.Blockers));
            var held = unchangedAttributes.Value!;
            Assert.AreEqual(1, held.Preview.KnowledgePointOverflowToActive);
            Assert.AreEqual(1, held.Allocations.Single(a => a.SourceSkillId == KnowledgeIds[1]).Rating);
            Assert.AreEqual(4, held.Allocations.Single(a => a.SourceSkillId == ActiveIds[2]).Rating);

            var changedAttributes = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                [new("INT", 2, 0), new("LOG", 1, 0)], request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, changedAttributes.Outcome,
                string.Join(",", changedAttributes.Blockers));
            var released = changedAttributes.Value!;
            Assert.AreEqual(12m, released.Preview.KnowledgeSkillPointBudget.Total);
            Assert.AreEqual(0, released.Preview.KnowledgePointOverflowToActive);
            Assert.AreEqual(6, released.Allocations.Single(a => a.SourceSkillId == KnowledgeIds[1]).Rating);
            Assert.AreEqual(5, released.Allocations.Single(a => a.SourceSkillId == ActiveIds[2]).Rating);
            Assert.AreEqual(23m, released.Preview.ActiveSkillPointBudget.Remaining);
            Assert.AreEqual(6, released.Allocations.Single(a => a.SourceSkillId == KnowledgeIds[0]).Rating);
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                skills.KnowledgePointContributions, released.Preview.KnowledgePointContributions));
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_missing_native_is_an_explicit_partial_not_an_invented_language()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            Assert.IsNull(skills.PendingDraft);
            var request = SkillCompletionRequest(attributes, skills,
                [new(CharacterCreationSkillKinds.Active, ActiveIds[1]),
                    new(CharacterCreationSkillKinds.Active, ActiveIds[2]),
                    new(CharacterCreationSkillKinds.Knowledge, KnowledgeIds[0])])
                with { ActivePointsToReserve = 26, GroupPointsToReserve = 2, KnowledgePointsToReserve = 10 };
            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            var plan = result.Value!;
            Assert.IsTrue(plan.HasChanges);
            Assert.AreEqual(2, plan.Allocations.Single(a => a.SourceSkillId == ActiveIds[2]).Rating);
            Assert.AreEqual(2, plan.Allocations.Single(a => a.SourceSkillId == KnowledgeIds[0]).Rating);
            Assert.IsFalse(plan.Allocations.Any(a => a.IsNativeLanguage || a.SourceSkillId == LanguageId));
            Assert.IsFalse(plan.Allocations.Any(a => a.SourceSkillId == ActiveIds[1]),
                "A known permission cannot bypass Core's movement restriction.");
            CollectionAssert.AreEquivalent(new[] { CharacterCreationSkillsBlockers.NativeLanguageRequired },
                plan.Blockers.ToArray());
            CollectionAssert.AreEquivalent(plan.Blockers.ToArray(), plan.Preview.Blockers.ToArray());
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    [DataRow("Magician")]
    [DataRow("Aspected Magician")]
    public void Skill_completion_real_source_grants_are_preserved_and_only_the_increment_is_charged(string talent)
    {
        WithRealTalentSkills(talent, (_, store, resolver, service, id) =>
        {
            var initial = Load(service, id);
            var native = initial.Authority.KnowledgeSkills.First(s => s.CanBeNativeLanguage);
            SaveCompletionSkills(service, id,
                [new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var grants = skills.PrerequisiteDraft!.TalentSelection!.GrantPlan!;
            CharacterCreationSkillCompletionTarget[] targets;
            bool activeGrant = grants.ActiveSkills.Count > 0;
            if (activeGrant)
                targets = [new(CharacterCreationSkillKinds.Active, grants.ActiveSkills[0].SourceId)];
            else
            {
                var group = skills.Authority.SkillGroups.Single(g => g.Name == grants.SkillGroups[0].CanonicalName);
                targets = [new(CharacterCreationSkillCompletionKinds.Group, group.GroupId),
                    .. group.MemberSkillSourceIds.Select(sourceId =>
                        new CharacterCreationSkillCompletionTarget(CharacterCreationSkillKinds.Active, sourceId))];
            }
            var request = SkillCompletionRequest(attributes, skills, targets) with
            {
                ActivePointsToReserve = (int)skills.ActiveSkillPointBudget.Total - (activeGrant ? 1 : 0),
                GroupPointsToReserve = (int)skills.SkillGroupPointBudget.Total - (activeGrant ? 0 : 1),
                KnowledgePointsToReserve = (int)skills.KnowledgeSkillPointBudget.Total
            };
            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            var plan = result.Value!;
            Assert.AreEqual(activeGrant ? 1m : 0m, plan.Preview.ActiveSkillPointBudget.Used);
            Assert.AreEqual(activeGrant ? 0m : 1m, plan.Preview.SkillGroupPointBudget.Used);
            foreach (var grant in grants.ActiveSkills)
            {
                var projected = plan.Preview.Skills.Single(s => s.SourceSkillId == grant.SourceId);
                Assert.AreEqual(grant.BaseRating, projected.GrantedRating);
                Assert.AreEqual(grant.BaseRating + (grant == grants.ActiveSkills[0] ? 1 : 0), projected.Rating);
                Assert.IsTrue(grant.SourceAnchorIds.All(projected.SourceAnchorIds.Contains));
            }
            foreach (var grant in grants.SkillGroups)
            {
                var projected = plan.Preview.SkillGroups.Single(g => g.Name == grant.CanonicalName);
                Assert.AreEqual(grant.BaseRating, projected.GrantedRating);
                Assert.AreEqual(grant.BaseRating + (grant == grants.SkillGroups[0] ? 1 : 0), projected.Rating);
                Assert.IsTrue(grant.SourceAnchorIds.All(projected.SourceAnchorIds.Contains));
                Assert.IsFalse(plan.Allocations.Any(a => projected.MemberSkillSourceIds.Contains(a.SourceSkillId)));
            }
            AssertCompletionProjectionMatches(plan.Preview,
                service.Preview(new(skills.Binding, plan.Allocations, plan.GroupAllocations)).Value!);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_rejects_invalid_requests_stale_snapshots_and_other_baseline_errors()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            SaveCompletionSkills(service, id,
            [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true),
                .. ActiveIds.Skip(2).Select(sourceId =>
                    new CharacterCreationSkillAllocation(sourceId, CharacterCreationSkillKinds.Active, 6, null, false)),
                .. KnowledgeIds.Select(sourceId =>
                    new CharacterCreationSkillAllocation(sourceId, CharacterCreationSkillKinds.Knowledge, 6, null, false))], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var request = SkillCompletionRequest(attributes, skills, []);
            CharacterCreationSkillCompletionRequest[] invalid =
            [
                request with { PermittedTargetOrder = [new("not-a-kind", ActiveIds[2])] },
                request with { PermittedTargetOrder = [new(CharacterCreationSkillKinds.Active, "unknown")] },
                request with { PermittedTargetOrder = [new(CharacterCreationSkillKinds.Active, ActiveIds[2]),
                    new(CharacterCreationSkillKinds.Active, ActiveIds[2])] },
                request with { PermittedTargetOrder = [null!] },
                request with { PermittedTargetOrder = Enumerable.Repeat(
                    new CharacterCreationSkillCompletionTarget(CharacterCreationSkillKinds.Active, ActiveIds[2]), 129).ToArray() },
                request with { ActivePointsToReserve = -1 },
                request with { GroupPointsToReserve = -1 },
                request with { KnowledgePointsToReserve = -1 }
            ];
            foreach (var candidate in invalid)
            {
                var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                    attributes.PendingDraft!.Allocations, candidate);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, result.Outcome);
                Assert.IsNull(result.Value);
                CollectionAssert.Contains(result.Blockers.ToList(), CharacterCreationSkillCompletionBlockers.RequestInvalid);
            }
            CharacterCreationSkillCompletionRequest[] stale =
            [
                request with { Binding = request.Binding with { WorkspaceId = new CharacterWorkspaceId("different-runner") } },
                request with { Binding = request.Binding with { ContentRevision = request.Binding.ContentRevision + 1 } },
                request with { Binding = request.Binding with { RawCharacterXmlDigest = Digest('f') } },
                request with { Binding = request.Binding with { AuxiliaryStateDigest = Digest('f') } },
                request with { Binding = request.Binding with { SkillsAuthorityDigest = Digest('f') } },
                request with { AttributesSnapshotDigest = Digest('f') },
                request with { SkillsSnapshotDigest = Digest('f') }
            ];
            foreach (var candidate in stale)
            {
                var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                    attributes.PendingDraft!.Allocations, candidate);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, result.Outcome);
                Assert.IsNull(result.Value);
                CollectionAssert.Contains(result.Blockers.ToList(), CharacterCreationSkillCompletionBlockers.SnapshotConflict);
            }
            var illegalBaseline = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                [new("INT", 0, 0), new("LOG", 0, 0)], request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, illegalBaseline.Outcome);
            Assert.IsNull(illegalBaseline.Value, "Only missing native language permits partial planning.");
            CollectionAssert.Contains(illegalBaseline.Blockers.ToList(), CharacterCreationSkillsBlockers.ActiveBudgetExceeded);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_cancellation_detachment_and_noop_cannot_change_saved_state()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            SaveCompletionSkills(service, id,
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var noOpRequest = SkillCompletionRequest(attributes, skills, [])
                with { ActivePointsToReserve = 28, GroupPointsToReserve = 2, KnowledgePointsToReserve = 12 };
            var noOp = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, noOpRequest);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, noOp.Outcome);
            Assert.IsFalse(noOp.Value!.HasChanges);
            Assert.AreEqual(1, noOp.Value.Evaluations);
            CollectionAssert.Contains(noOp.Value.Preview.Attributes.Blockers.ToList(), CharacterCreationAttributesBlockers.DraftDuplicate);
            var unspent = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft.Allocations, noOpRequest with
                { ActivePointsToReserve = 0, GroupPointsToReserve = 0, KnowledgePointsToReserve = 0 });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, unspent.Outcome);
            Assert.IsFalse(unspent.Value!.HasChanges);
            CollectionAssert.AreEquivalent(new[]
            {
                CharacterCreationSkillCompletionBlockers.ActivePointsUnallocated,
                CharacterCreationSkillCompletionBlockers.GroupPointsUnallocated,
                CharacterCreationSkillCompletionBlockers.KnowledgePointsUnallocated
            }, unspent.Value.Blockers.ToArray());

            CharacterCreationSkillCompletionTarget[] targets = [new(CharacterCreationSkillKinds.Active, ActiveIds[2])];
            var request = noOpRequest with { PermittedTargetOrder = targets, ActivePointsToReserve = 27 };
            var proposedAttributes = attributes.PendingDraft.Allocations.ToArray();
            bool canceled = false;
            try
            {
                CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                    proposedAttributes, request, new CancellationToken(true));
            }
            catch (OperationCanceledException) { canceled = true; }
            Assert.IsTrue(canceled);
            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills, proposedAttributes, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            var captured = CharacterCreationSkillsDigest.Compute(result.Value);
            targets[0] = new(CharacterCreationSkillKinds.Active, "mutated");
            proposedAttributes[0] = proposedAttributes[0] with { PriorityPoints = 100 };
            if (skills.Skills is CharacterCreationSkillProjection[] rows)
                rows[0] = rows[0] with { IsNativeLanguage = false, Rating = 100 };
            Assert.AreEqual(captured, CharacterCreationSkillsDigest.Compute(result.Value));
            Assert.IsNull(typeof(CharacterCreationSkillCompletionPlan).GetProperty("CanConfirm"));
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_explicit_targets_stop_at_requested_ratings_preserve_manual_values_and_detach_inputs()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            SaveCompletionSkills(service, id,
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true),
                    new(ActiveIds[2], CharacterCreationSkillKinds.Active, 4, "spec-2", false),
                    new(ActiveIds[3], CharacterCreationSkillKinds.Active, 1, null, false)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            CharacterCreationSkillCompletionTarget[] permissions =
            [new(CharacterCreationSkillKinds.Active, ActiveIds[2]),
                new(CharacterCreationSkillKinds.Active, ActiveIds[3]),
                new(CharacterCreationSkillKinds.Active, ActiveIds[4]),
                new(CharacterCreationSkillKinds.Knowledge, KnowledgeIds[0])];
            CharacterCreationSkillCompletionRatingTarget[] targets =
            [new(CharacterCreationSkillKinds.Active, ActiveIds[2], 2),
                new(CharacterCreationSkillKinds.Active, ActiveIds[4], 2),
                new(CharacterCreationSkillKinds.Knowledge, KnowledgeIds[0], 1)];
            var request = SkillCompletionRequest(attributes, skills, permissions) with { Targets = targets };
            var proposedAttributes = attributes.PendingDraft!.Allocations.ToArray();
            var inputDigest = CharacterCreationSkillsDigest.Compute(new { attributes, skills, request, proposedAttributes });

            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills, proposedAttributes, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            var plan = result.Value!;
            Assert.IsTrue(plan.HasChanges);
            Assert.AreEqual(4, plan.Allocations.Single(row => row.SourceSkillId == ActiveIds[2]).Rating);
            Assert.AreEqual("spec-2", plan.Allocations.Single(row => row.SourceSkillId == ActiveIds[2]).SpecializationOptionId);
            Assert.AreEqual(1, plan.Allocations.Single(row => row.SourceSkillId == ActiveIds[3]).Rating,
                "Permission alone must not add a numeric target.");
            Assert.AreEqual(2, plan.Allocations.Single(row => row.SourceSkillId == ActiveIds[4]).Rating);
            Assert.AreEqual(1, plan.Allocations.Single(row => row.SourceSkillId == KnowledgeIds[0]).Rating);
            Assert.IsTrue(plan.Allocations.Single(row => row.SourceSkillId == LanguageId).IsNativeLanguage);
            Assert.IsTrue(plan.Preview.ActiveSkillPointBudget.Remaining > 0);
            Assert.IsTrue(plan.Preview.SkillGroupPointBudget.Remaining > 0);
            Assert.IsTrue(plan.Preview.KnowledgeSkillPointBudget.Remaining > 0);
            Assert.AreEqual(0, plan.Blockers.Count, "Met explicit targets do not require spending every pool.");
            Assert.AreEqual(inputDigest, CharacterCreationSkillsDigest.Compute(new { attributes, skills, request, proposedAttributes }));
            AssertCompletionProjectionMatches(plan.Preview,
                service.Preview(new(skills.Binding, plan.Allocations, plan.GroupAllocations)).Value!);
            var captured = CharacterCreationSkillsDigest.Compute(plan);
            targets[0] = targets[0] with { TargetRating = int.MaxValue };
            permissions[0] = new(CharacterCreationSkillKinds.Active, "mutated");
            proposedAttributes[0] = proposedAttributes[0] with { PriorityPoints = 100 };
            Assert.AreEqual(captured, CharacterCreationSkillsDigest.Compute(plan));
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_explicit_target_order_not_permission_order_controls_a_limited_pool()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            SaveCompletionSkills(service, id,
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var request = SkillCompletionRequest(attributes, skills,
                [new(CharacterCreationSkillKinds.Active, ActiveIds[2]),
                    new(CharacterCreationSkillKinds.Active, ActiveIds[3])]) with
            {
                ActivePointsToReserve = 26,
                Targets = [new(CharacterCreationSkillKinds.Active, ActiveIds[3], 2),
                    new(CharacterCreationSkillKinds.Active, ActiveIds[2], 2)]
            };
            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            var plan = result.Value!;
            Assert.AreEqual(2, plan.Allocations.Single(row => row.SourceSkillId == ActiveIds[3]).Rating);
            Assert.IsFalse(plan.Allocations.Any(row => row.SourceSkillId == ActiveIds[2]));
            Assert.AreEqual(26m, plan.Preview.ActiveSkillPointBudget.Remaining);
            CollectionAssert.AreEquivalent(new[] { CharacterCreationSkillCompletionBlockers.TargetsUnfulfilled },
                plan.Blockers.ToArray());
            Assert.AreEqual(0, plan.Preview.Blockers.Count);
            Assert.AreEqual(plan.PlanDigest, CharacterCreationSkillCompletionPlanner.Plan(
                attributes, skills, attributes.PendingDraft.Allocations, request).Value!.PlanDigest);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_explicit_group_target_requires_all_member_permissions_without_buying_member_rows()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            SaveCompletionSkills(service, id,
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var group = skills.Authority.SkillGroups.Single();
            var request = SkillCompletionRequest(attributes, skills,
                [new(CharacterCreationSkillCompletionKinds.Group, group.GroupId),
                    new(CharacterCreationSkillKinds.Active, ActiveIds[0])]) with
            {
                Targets = [new(CharacterCreationSkillCompletionKinds.Group, group.GroupId, 1)]
            };
            var locked = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, locked.Outcome);
            Assert.IsNotNull(locked.Value);
            Assert.AreEqual(0, locked.Value.GroupAllocations.Count);
            CollectionAssert.Contains(locked.Blockers.ToArray(), CharacterCreationSkillCompletionBlockers.GroupMemberLocked);
            CollectionAssert.Contains(locked.Blockers.ToArray(), CharacterCreationSkillCompletionBlockers.TargetsUnfulfilled);

            request = request with
            {
                PermittedTargetOrder = [.. request.PermittedTargetOrder,
                    new(CharacterCreationSkillKinds.Active, ActiveIds[1])]
            };
            var allowed = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, allowed.Outcome, string.Join(",", allowed.Blockers));
            var plan = allowed.Value!;
            Assert.AreEqual(1, plan.GroupAllocations.Single().Rating);
            Assert.AreEqual(1m, plan.Preview.SkillGroupPointBudget.Remaining);
            Assert.AreEqual(0m, plan.Preview.ActiveSkillPointBudget.Used);
            Assert.IsFalse(plan.Allocations.Any(row => group.MemberSkillSourceIds.Contains(row.SourceSkillId)));
            AssertCompletionProjectionMatches(plan.Preview,
                service.Preview(new(skills.Binding, plan.Allocations, plan.GroupAllocations)).Value!);
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    [DataRow("Magician")]
    [DataRow("Aspected Magician")]
    public void Skill_completion_explicit_target_rating_includes_real_source_grants(string talent)
    {
        WithRealTalentSkills(talent, (_, store, resolver, service, id) =>
        {
            var initial = Load(service, id);
            var native = initial.Authority.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
            SaveCompletionSkills(service, id,
                [new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var grants = skills.PrerequisiteDraft!.TalentSelection!.GrantPlan!;
            bool activeGrant = grants.ActiveSkills.Count > 0;
            CharacterCreationSkillCompletionTarget[] permissions;
            CharacterCreationSkillCompletionRatingTarget target;
            if (activeGrant)
            {
                var grant = grants.ActiveSkills[0];
                permissions = [new(CharacterCreationSkillKinds.Active, grant.SourceId)];
                target = new(CharacterCreationSkillKinds.Active, grant.SourceId, grant.BaseRating + 1);
            }
            else
            {
                var grant = grants.SkillGroups[0];
                var group = skills.Authority.SkillGroups.Single(row => row.Name == grant.CanonicalName);
                permissions = [new(CharacterCreationSkillCompletionKinds.Group, group.GroupId),
                    .. group.MemberSkillSourceIds.Select(sourceId =>
                        new CharacterCreationSkillCompletionTarget(CharacterCreationSkillKinds.Active, sourceId))];
                target = new(CharacterCreationSkillCompletionKinds.Group, group.GroupId, grant.BaseRating + 1);
            }
            var request = SkillCompletionRequest(attributes, skills, permissions) with { Targets = [target] };
            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            var plan = result.Value!;
            Assert.AreEqual(activeGrant ? 1m : 0m, plan.Preview.ActiveSkillPointBudget.Used);
            Assert.AreEqual(activeGrant ? 0m : 1m, plan.Preview.SkillGroupPointBudget.Used);
            if (activeGrant)
            {
                var row = plan.Preview.Skills.Single(value => value.SourceSkillId == target.SourceId);
                Assert.AreEqual(target.TargetRating, row.Rating);
                Assert.AreEqual(target.TargetRating - 1, row.GrantedRating);
            }
            else
            {
                var row = plan.Preview.SkillGroups.Single(value => value.GroupId == target.SourceId);
                Assert.AreEqual(target.TargetRating, row.Rating);
                Assert.AreEqual(target.TargetRating - 1, row.GrantedRating);
                Assert.IsFalse(plan.Allocations.Any(value => row.MemberSkillSourceIds.Contains(value.SourceSkillId)));
            }
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_explicit_group_rating_satisfies_only_enabled_member_targets_without_individual_rows()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            SaveCompletionSkills(service, id,
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var group = skills.Authority.SkillGroups.Single();
            var request = SkillCompletionRequest(attributes, skills,
                [new(CharacterCreationSkillCompletionKinds.Group, group.GroupId),
                    .. group.MemberSkillSourceIds.Select(sourceId =>
                        new CharacterCreationSkillCompletionTarget(CharacterCreationSkillKinds.Active, sourceId))]) with
            {
                Targets = [new(CharacterCreationSkillCompletionKinds.Group, group.GroupId, 2),
                    new(CharacterCreationSkillKinds.Active, ActiveIds[0], 2)]
            };
            var allowed = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, allowed.Outcome, string.Join(",", allowed.Blockers));
            Assert.AreEqual(2, allowed.Value!.GroupAllocations.Single().Rating);
            Assert.AreEqual(0m, allowed.Value.Preview.ActiveSkillPointBudget.Used);
            Assert.IsFalse(allowed.Value.Allocations.Any(row => group.MemberSkillSourceIds.Contains(row.SourceSkillId)));

            var disabled = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft.Allocations, request with
                {
                    Targets = [.. request.Targets!, new(CharacterCreationSkillKinds.Active, ActiveIds[1], 2)]
                });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, disabled.Outcome);
            CollectionAssert.AreEquivalent(new[] { CharacterCreationSkillCompletionBlockers.TargetsUnfulfilled },
                disabled.Blockers.ToArray());
            Assert.AreEqual(2, disabled.Value!.GroupAllocations.Single().Rating);
            Assert.IsFalse(disabled.Value.Allocations.Any(row => group.MemberSkillSourceIds.Contains(row.SourceSkillId)));
            Assert.AreEqual(0, disabled.Value.Preview.Blockers.Count);
            var enabledTarget = CharacterCreationSkillCompletionPlanner.EvaluateTarget(
                disabled.Value.Preview, skills.Authority, skills.MovementCapability,
                new(CharacterCreationSkillKinds.Active, ActiveIds[0], 2));
            Assert.IsTrue(enabledTarget.IsKnown);
            Assert.AreEqual(2, enabledTarget.EffectiveRating);
            Assert.IsTrue(enabledTarget.IsSatisfied,
                "An unrelated unmet target does not undo this enabled member's group rating.");
            var disabledTarget = CharacterCreationSkillCompletionPlanner.EvaluateTarget(
                disabled.Value.Preview, skills.Authority, skills.MovementCapability,
                new(CharacterCreationSkillKinds.Active, ActiveIds[1], 2));
            Assert.IsTrue(disabledTarget.IsKnown);
            Assert.AreEqual(0, disabledTarget.EffectiveRating);
            Assert.IsFalse(disabledTarget.IsSatisfied,
                "Canonical group membership must not bypass the member's movement requirement.");
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_explicit_targets_reject_invalid_ids_permissions_duplicates_and_negative_ratings()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var request = SkillCompletionRequest(attributes, skills,
                [new(CharacterCreationSkillKinds.Active, ActiveIds[2])]);
            CharacterCreationSkillCompletionRatingTarget[][] invalid =
            [
                [new(CharacterCreationSkillKinds.Active, ActiveIds[2], -1)],
                [new(CharacterCreationSkillKinds.Active, "unknown", 1)],
                [new("not-a-kind", ActiveIds[2], 1)],
                [new(CharacterCreationSkillKinds.Active, ActiveIds[3], 1)],
                [new(CharacterCreationSkillKinds.Active, ActiveIds[2], 1),
                    new(CharacterCreationSkillKinds.Active, ActiveIds[2], 2)],
                [null!],
                Enumerable.Repeat(new CharacterCreationSkillCompletionRatingTarget(
                    CharacterCreationSkillKinds.Active, ActiveIds[2], 1), 129).ToArray()
            ];
            foreach (var targets in invalid)
            {
                var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                    attributes.PendingDraft!.Allocations, request with { Targets = targets });
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, result.Outcome);
                Assert.IsNull(result.Value);
                CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationSkillCompletionBlockers.RequestInvalid);
                var balanced = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                    attributes.PendingDraft.Allocations, request with { Targets = targets, FillRemainingPoints = true });
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, balanced.Outcome);
                Assert.IsNull(balanced.Value);
            }
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_explicit_unavailable_and_huge_targets_return_bounded_legal_partials()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            SaveCompletionSkills(service, id,
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            CharacterCreationSkillCompletionRatingTarget[] targets =
            [new(CharacterCreationSkillKinds.Active, ActiveIds[2], int.MaxValue),
                new(CharacterCreationSkillKinds.Knowledge, KnowledgeIds[0], int.MaxValue),
                new(CharacterCreationSkillKinds.Active, ActiveIds[1], 1)];
            foreach (var target in targets)
            {
                var request = SkillCompletionRequest(attributes, skills,
                    [new(target.Kind, target.SourceId)]) with { Targets = [target] };
                var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                    attributes.PendingDraft!.Allocations, request);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
                var plan = result.Value!;
                CollectionAssert.AreEquivalent(new[] { CharacterCreationSkillCompletionBlockers.TargetsUnfulfilled },
                    plan.Blockers.ToArray());
                Assert.IsTrue(plan.Evaluations is >= 1 and <= 16,
                    "An impossible target must stop at the actual source/budget boundary.");
                Assert.AreEqual(0, plan.Preview.Blockers.Count);
                if (target.SourceId == ActiveIds[1])
                    Assert.IsFalse(plan.Allocations.Any(row => row.SourceSkillId == target.SourceId));
                else
                    Assert.AreEqual(6, plan.Allocations.Single(row => row.SourceSkillId == target.SourceId).Rating);
            }
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_empty_targets_make_no_numeric_additions_and_preserve_explicit_native_language_behavior()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var request = SkillCompletionRequest(attributes, skills,
                [new(CharacterCreationSkillKinds.Active, ActiveIds[2]),
                    new(CharacterCreationSkillKinds.Knowledge, KnowledgeIds[0])]) with { Targets = [] };
            var missingNative = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, missingNative.Outcome);
            Assert.IsFalse(missingNative.Value!.HasChanges);
            CollectionAssert.AreEquivalent(new[] { CharacterCreationSkillsBlockers.NativeLanguageRequired },
                missingNative.Blockers.ToArray());

            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft.Allocations, request with { NativeLanguageSourceId = LanguageId });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            var plan = result.Value!;
            Assert.AreEqual(1, plan.Allocations.Count);
            Assert.AreEqual(LanguageId, plan.Allocations.Single().SourceSkillId);
            Assert.IsTrue(plan.Allocations.Single().IsNativeLanguage);
            Assert.IsNull(plan.Allocations.Single().Rating);
            Assert.AreEqual(0, plan.GroupAllocations.Count);
            Assert.AreEqual(0m, plan.Preview.ActiveSkillPointBudget.Used);
            Assert.AreEqual(0m, plan.Preview.SkillGroupPointBudget.Used);
            Assert.AreEqual(0m, plan.Preview.KnowledgeSkillPointBudget.Used);
            var zero = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft.Allocations, request with
                {
                    NativeLanguageSourceId = LanguageId,
                    Targets = [new(CharacterCreationSkillKinds.Active, ActiveIds[2], 0)]
                });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, zero.Outcome);
            Assert.AreEqual(CharacterCreationSkillsDigest.Compute(plan.Allocations),
                CharacterCreationSkillsDigest.Compute(zero.Value!.Allocations));
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_omitted_targets_preserve_legacy_json_digest_and_planning_semantics()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            SaveCompletionSkills(service, id,
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var request = SkillCompletionRequest(attributes, skills,
                [new(CharacterCreationSkillKinds.Active, ActiveIds[2])]) with
            {
                ActivePointsToReserve = 26, GroupPointsToReserve = 2, KnowledgePointsToReserve = 12
            };
            var legacy = new
            {
                request.Binding, request.AttributesSnapshotDigest, request.SkillsSnapshotDigest,
                request.PermittedTargetOrder, request.ActivePointsToReserve, request.GroupPointsToReserve,
                request.KnowledgePointsToReserve, request.NativeLanguageSourceId
            };
            var json = System.Text.Json.JsonSerializer.Serialize(request);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            Assert.IsFalse(document.RootElement.TryGetProperty("Targets", out _));
            Assert.IsFalse(document.RootElement.TryGetProperty("FillRemainingPoints", out _));
            Assert.AreEqual(json, System.Text.Json.JsonSerializer.Serialize(request with { FillRemainingPoints = false }));
            Assert.IsTrue(System.Text.Json.JsonSerializer.SerializeToElement(request with { FillRemainingPoints = true })
                .GetProperty("FillRemainingPoints").GetBoolean());
            Assert.AreEqual(CharacterCreationSkillsDigest.Compute(legacy), CharacterCreationSkillsDigest.Compute(request));
            var restored = System.Text.Json.JsonSerializer.Deserialize<CharacterCreationSkillCompletionRequest>(
                System.Text.Json.JsonSerializer.Serialize(legacy))!;
            Assert.IsNull(restored.Targets);
            var original = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            var replay = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft.Allocations, restored);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, original.Outcome);
            Assert.AreEqual(2, original.Value!.Allocations.Single(row => row.SourceSkillId == ActiveIds[2]).Rating);
            Assert.AreEqual(original.Value.PlanDigest, replay.Value!.PlanDigest);
            using var explicitEmpty = System.Text.Json.JsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(request with { Targets = [] }));
            Assert.AreEqual(0, explicitEmpty.RootElement.GetProperty("Targets").GetArrayLength());
            Assert.AreNotEqual(CharacterCreationSkillsDigest.Compute(request),
                CharacterCreationSkillsDigest.Compute(request with { Targets = [] }));
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    [DataRow("Magician")]
    [DataRow("Aspected Magician")]
    public void Skill_completion_target_evaluation_observes_saved_real_grants_independently_of_other_unmet_goals(string talent)
    {
        WithRealTalentSkills(talent, (_, store, resolver, service, id) =>
        {
            var initial = Load(service, id);
            var native = initial.Authority.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
            SaveCompletionSkills(service, id,
                [new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var grants = skills.PrerequisiteDraft!.TalentSelection!.GrantPlan!;
            CharacterCreationSkillCompletionRatingTarget target;
            if (grants.ActiveSkills.Count > 0)
            {
                var grant = grants.ActiveSkills[0];
                target = new(CharacterCreationSkillKinds.Active, grant.SourceId, grant.BaseRating);
            }
            else
            {
                var grant = grants.SkillGroups[0];
                var group = skills.Authority.SkillGroups.Single(row => row.Name == grant.CanonicalName);
                string memberId = group.MemberSkillSourceIds.First(sourceId =>
                    CharacterCreationSkillsAccessRules.IsSkillAvailable(skills.Authority, sourceId));
                Assert.IsFalse(skills.Skills.Any(row => row.SourceSkillId == memberId));
                target = new(CharacterCreationSkillKinds.Active, memberId, grant.BaseRating);
            }
            var unallocated = skills.Authority.ActiveSkills.First(row =>
                CharacterCreationSkillsAccessRules.IsSkillAvailable(skills.Authority, row.SourceSkillId)
                && !row.IsExotic && !row.RequiresGroundMovement && !row.RequiresSwimMovement && !row.RequiresFlyMovement
                && skills.Skills.All(saved => saved.SourceSkillId != row.SourceSkillId)
                && skills.SkillGroups.All(group => !group.MemberSkillSourceIds.Contains(row.SourceSkillId)));
            var request = SkillCompletionRequest(attributes, skills,
                [new(target.Kind, target.SourceId), new(CharacterCreationSkillKinds.Active, unallocated.SourceSkillId)]) with
            {
                ActivePointsToReserve = (int)skills.ActiveSkillPointBudget.Remaining,
                GroupPointsToReserve = (int)skills.SkillGroupPointBudget.Remaining,
                KnowledgePointsToReserve = (int)skills.KnowledgeSkillPointBudget.Remaining,
                Targets = [target, new(CharacterCreationSkillKinds.Active, unallocated.SourceSkillId, 1)]
            };
            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            var plan = result.Value!;
            CollectionAssert.Contains(plan.Blockers.ToArray(), CharacterCreationSkillCompletionBlockers.TargetsUnfulfilled);
            Assert.IsFalse(plan.HasChanges);
            var evaluated = CharacterCreationSkillCompletionPlanner.EvaluateTarget(
                plan.Preview, skills.Authority, skills.MovementCapability, target);
            Assert.AreEqual(target, evaluated.Target);
            Assert.IsTrue(evaluated.IsKnown);
            Assert.AreEqual(target.TargetRating, evaluated.EffectiveRating,
                "The reported rating includes the source grant without charging or adding it again.");
            Assert.IsTrue(evaluated.IsSatisfied);
            var unmet = CharacterCreationSkillCompletionPlanner.EvaluateTarget(
                plan.Preview, skills.Authority, skills.MovementCapability, request.Targets![1]);
            Assert.IsTrue(unmet.IsKnown);
            Assert.AreEqual(0, unmet.EffectiveRating);
            Assert.IsFalse(unmet.IsSatisfied);
            if (grants.SkillGroups.Count > 0)
            {
                Assert.IsFalse(plan.Preview.Skills.Any(row => row.SourceSkillId == target.SourceId));
                var locked = skills.Authority.ActiveSkills.First(row =>
                    !CharacterCreationSkillsAccessRules.IsSkillAvailable(skills.Authority, row.SourceSkillId));
                var unavailable = CharacterCreationSkillCompletionPlanner.EvaluateTarget(
                    plan.Preview, skills.Authority, skills.MovementCapability,
                    new(CharacterCreationSkillKinds.Active, locked.SourceSkillId, 1));
                Assert.IsTrue(unavailable.IsKnown);
                Assert.AreEqual(0, unavailable.EffectiveRating);
                Assert.IsFalse(unavailable.IsSatisfied);
            }
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    [TestMethod]
    public void Skill_completion_target_evaluation_distinguishes_absent_zero_native_and_unknown_source()
    {
        WithReadySkillsContext((store, service, id, _, _, resolver) =>
        {
            SaveCompletionSkills(service, id,
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)], []);
            var skills = Load(service, id);
            var attributes = new CharacterCreationAttributesService(store, resolver).Load(new(id)).Value!;
            var before = store.Get(id).Value!;
            var request = SkillCompletionRequest(attributes, skills, []) with { Targets = [] };
            var result = CharacterCreationSkillCompletionPlanner.Plan(attributes, skills,
                attributes.PendingDraft!.Allocations, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            var preview = result.Value!.Preview;
            CharacterCreationSkillCompletionRatingTarget[] absent =
            [new(CharacterCreationSkillKinds.Active, ActiveIds[2], 0),
                new(CharacterCreationSkillKinds.Knowledge, KnowledgeIds[0], 0),
                new(CharacterCreationSkillCompletionKinds.Group, skills.Authority.SkillGroups.Single().GroupId, 0)];
            foreach (var zeroTarget in absent)
            {
                var zero = CharacterCreationSkillCompletionPlanner.EvaluateTarget(
                    preview, skills.Authority, skills.MovementCapability, zeroTarget);
                Assert.IsTrue(zero.IsKnown);
                Assert.AreEqual(0, zero.EffectiveRating);
                Assert.IsTrue(zero.IsSatisfied);
                var positive = CharacterCreationSkillCompletionPlanner.EvaluateTarget(
                    preview, skills.Authority, skills.MovementCapability, zeroTarget with { TargetRating = 1 });
                Assert.IsTrue(positive.IsKnown);
                Assert.AreEqual(0, positive.EffectiveRating);
                Assert.IsFalse(positive.IsSatisfied);
            }
            foreach (int desired in new[] { 0, 1 })
            {
                var native = CharacterCreationSkillCompletionPlanner.EvaluateTarget(
                    preview, skills.Authority, skills.MovementCapability,
                    new(CharacterCreationSkillKinds.Knowledge, LanguageId, desired));
                Assert.IsTrue(native.IsKnown);
                Assert.IsNull(native.EffectiveRating);
                Assert.IsFalse(native.IsSatisfied, "A native language is not a numeric zero-rating choice.");
                var unknown = CharacterCreationSkillCompletionPlanner.EvaluateTarget(
                    preview, skills.Authority, skills.MovementCapability,
                    new(CharacterCreationSkillKinds.Active, "not-in-authority", desired));
                Assert.IsFalse(unknown.IsKnown);
                Assert.IsNull(unknown.EffectiveRating);
                Assert.IsFalse(unknown.IsSatisfied, "An unknown source cannot satisfy even a zero goal.");
            }
            var unknownKind = CharacterCreationSkillCompletionPlanner.EvaluateTarget(
                preview, skills.Authority, skills.MovementCapability,
                new("not-a-kind", ActiveIds[2], 0));
            Assert.IsFalse(unknownKind.IsKnown);
            Assert.IsNull(unknownKind.EffectiveRating);
            Assert.IsFalse(unknownKind.IsSatisfied);
            Assert.Throws<ArgumentException>(() => CharacterCreationSkillCompletionPlanner.EvaluateTarget(
                preview, skills.Authority, skills.MovementCapability,
                new(CharacterCreationSkillKinds.Active, ActiveIds[2], -1)));
            Assert.Throws<ArgumentException>(() => CharacterCreationSkillCompletionPlanner.EvaluateTarget(
                preview, skills.Authority, skills.MovementCapability,
                new(CharacterCreationSkillKinds.Active, " ", 0)));
            AssertCompletionWorkspaceUnchanged(before, store.Get(id).Value!);
        });
    }

    private static CharacterCreationSkillCompletionRequest SkillCompletionRequest(
        CharacterCreationAttributesState attributes,
        CharacterCreationSkillsState skills,
        IReadOnlyList<CharacterCreationSkillCompletionTarget> targets)
        => new(skills.Binding, attributes.SnapshotDigest, skills.SnapshotDigest, targets);

    private static void SaveCompletionSkills(
        ICharacterCreationSkillsService service, CharacterWorkspaceId id,
        IReadOnlyList<CharacterCreationSkillAllocation> allocations,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups)
    {
        var state = Load(service, id);
        var preview = service.Preview(new(state.Binding, allocations, groups)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        var saved = service.Confirm(new(state.Binding, allocations, groups, preview.PreviewDigest,
            "skill-completion-baseline", true));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, saved.Outcome, string.Join(",", saved.Blockers));
    }
}
