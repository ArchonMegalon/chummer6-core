using System.Xml.Linq;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationAttributesServiceTests
{
    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Completion_plan_spends_only_explicitly_unlocked_points_and_matches_Core(string method)
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var state = Load(service, id);
            var before = store.Get(id).Value!;
            var request = new CharacterCreationAttributeCompletionRequest(
                state.Binding, state.SnapshotDigest,
                ["BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL", "EDG"]);
            var result = CharacterCreationAttributeCompletionPlanner.Plan(state, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            Assert.IsNotNull(result.Value);
            var plan = result.Value;
            Assert.IsTrue(plan.HasChanges);
            Assert.AreEqual(0m, plan.Preview.NormalPointBudget.Remaining);
            Assert.AreEqual(0m, plan.Preview.SpecialPointBudget.Remaining);
            Assert.AreEqual(state.CreationKarmaBudget.Used, plan.Preview.CreationKarmaBudget.Used);
            Assert.IsTrue(plan.Preview.Attributes.Count(a =>
                a.Category == CharacterCreationAttributeCategories.Normal && a.Current == a.Maximum)
                <= state.MaxNumberMaxAttributesCreate);
            Assert.IsTrue(plan.Preview.Attributes.All(a => a.Current <= a.Maximum));
            Assert.IsTrue(plan.Allocations.All(a => a.KarmaLevels == 0));
            Assert.IsTrue(plan.Evaluations > 0 && plan.Evaluations <= 257);
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                service.Preview(new(state.Binding, plan.Allocations)).Value, plan.Preview));
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                plan, CharacterCreationAttributeCompletionPlanner.Plan(state, request).Value));
            Assert.AreEqual(before.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.SavedRevision, store.Get(id).Value!.SavedRevision);
            Assert.AreEqual(before.Document.Content, store.Get(id).Value!.Document.Content);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        }, method: method);
    }

    [TestMethod]
    public void Completion_plan_keeps_omitted_zero_allocations_locked_and_reports_partial_remainder()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var state = Load(service, id);
            var before = store.Get(id).Value!;
            var result = CharacterCreationAttributeCompletionPlanner.Plan(state,
                new(state.Binding, state.SnapshotDigest, ["BOD"], SpecialPointsToReserve: 1));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            Assert.IsNotNull(result.Value);
            var plan = result.Value;
            Assert.IsTrue(plan.HasChanges);
            Assert.IsTrue(plan.Preview.CanConfirm, "A partial proposal must remain an exact legal allocation.");
            CollectionAssert.Contains(plan.Blockers.ToList(), CharacterCreationAttributeCompletionBlockers.NormalPointsUnallocated);
            Assert.AreEqual(5, plan.Allocations.Single(a => a.AttributeId == "BOD").PriorityPoints);
            Assert.IsTrue(plan.Allocations.Where(a => a.AttributeId != "BOD")
                .All(a => a.PriorityPoints == 0 && a.KarmaLevels == 0),
                "An omitted zero is a user lock, not permission to fill a different attribute.");
            Assert.AreEqual(15m, plan.Preview.NormalPointBudget.Remaining);
            Assert.AreEqual(1m, plan.Preview.SpecialPointBudget.Remaining);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);

            var locked = CharacterCreationAttributeCompletionPlanner.Plan(state,
                new(state.Binding, state.SnapshotDigest, []));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, locked.Outcome);
            Assert.IsNotNull(locked.Value);
            Assert.IsFalse(locked.Value.HasChanges);
            Assert.AreEqual(state.NormalPointBudget.Remaining, locked.Value.Preview.NormalPointBudget.Remaining);
            CollectionAssert.Contains(locked.Value.Blockers.ToList(), CharacterCreationAttributeCompletionBlockers.NormalPointsUnallocated);
            CollectionAssert.Contains(locked.Value.Blockers.ToList(), CharacterCreationAttributeCompletionBlockers.SpecialPointsUnallocated);
        });
    }

    [TestMethod]
    public void Completion_plan_respects_preference_order_and_separate_reserves()
    {
        WithConfirmedPrerequisite((_, service, id, _) =>
        {
            var state = Load(service, id);
            var result = CharacterCreationAttributeCompletionPlanner.Plan(state,
                new(state.Binding, state.SnapshotDigest, ["AGI", "BOD", "EDG"],
                    NormalPointsToReserve: 17, SpecialPointsToReserve: 1));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            var plan = result.Value!;
            Assert.AreEqual(3, plan.Allocations.Single(a => a.AttributeId == "AGI").PriorityPoints);
            Assert.AreEqual(0, plan.Allocations.Single(a => a.AttributeId == "BOD").PriorityPoints);
            Assert.AreEqual(0, plan.Allocations.Single(a => a.AttributeId == "EDG").PriorityPoints);
            Assert.AreEqual(17m, plan.Preview.NormalPointBudget.Remaining);
            Assert.AreEqual(1m, plan.Preview.SpecialPointBudget.Remaining);

            var special = CharacterCreationAttributeCompletionPlanner.Plan(state,
                new(state.Binding, state.SnapshotDigest, ["BOD", "EDG"],
                    NormalPointsToReserve: 20));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, special.Outcome);
            Assert.AreEqual(0, special.Value!.Allocations.Single(a => a.AttributeId == "BOD").PriorityPoints);
            Assert.AreEqual(1, special.Value.Allocations.Single(a => a.AttributeId == "EDG").PriorityPoints);
        });
    }

    [TestMethod]
    public void Completion_plan_preserves_existing_allocations_and_does_not_reprice_Karma_upward()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var initial = Load(service, id);
            CharacterCreationAttributeAllocation[] baseline = [new("BOD", 2, 0), new("AGI", 0, 1)];
            var preview = service.Preview(new(initial.Binding, baseline)).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new(initial.Binding, baseline, preview.PreviewDigest, true)).Outcome);
            var state = Load(service, id);
            var before = store.Get(id).Value!;
            var result = CharacterCreationAttributeCompletionPlanner.Plan(state,
                new(state.Binding, state.SnapshotDigest,
                    ["AGI", "BOD", "REA", "STR", "CHA", "INT", "LOG", "WIL", "EDG"]));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            var plan = result.Value!;
            foreach (var original in state.PendingDraft!.Allocations)
            {
                var proposed = plan.Allocations.Single(a => a.AttributeId == original.AttributeId);
                Assert.IsTrue(proposed.PriorityPoints >= original.PriorityPoints);
                Assert.AreEqual(original.KarmaLevels, proposed.KarmaLevels);
            }
            Assert.AreEqual(0, plan.Allocations.Single(a => a.AttributeId == "AGI").PriorityPoints,
                "Raising AGI's priority base would raise the price of its existing Karma level.");
            Assert.AreEqual(10m, plan.Preview.CreationKarmaBudget.Used);
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                service.Preview(new(state.Binding, plan.Allocations)).Value, plan.Preview));

            var cannotReprice = CharacterCreationAttributeCompletionPlanner.Plan(state,
                new(state.Binding, state.SnapshotDigest, ["AGI"], SpecialPointsToReserve: 1));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, cannotReprice.Outcome);
            Assert.IsNotNull(cannotReprice.Value);
            Assert.IsFalse(cannotReprice.Value.HasChanges);
            Assert.AreEqual(10m, cannotReprice.Value.Preview.CreationKarmaBudget.Used);
            Assert.AreEqual(before.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        });
    }

    [TestMethod]
    public void Completion_plan_saved_baseline_noop_does_not_invent_an_apply_authority()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var initial = Load(service, id);
            CharacterCreationAttributeAllocation[] baseline = [new("BOD", 1, 0)];
            var preview = service.Preview(new(initial.Binding, baseline)).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new(initial.Binding, baseline, preview.PreviewDigest, true)).Outcome);
            var state = Load(service, id);
            var before = store.Get(id).Value!;
            var result = CharacterCreationAttributeCompletionPlanner.Plan(state,
                new(state.Binding, state.SnapshotDigest, [], NormalPointsToReserve: 19, SpecialPointsToReserve: 1));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            Assert.IsNotNull(result.Value);
            Assert.IsFalse(result.Value.HasChanges);
            Assert.IsFalse(result.Value.Preview.CanConfirm);
            CollectionAssert.Contains(result.Value.Preview.Blockers.ToList(), CharacterCreationAttributesBlockers.DraftDuplicate);
            Assert.AreEqual(0, result.Value.Blockers.Count);
            Assert.AreEqual(before.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        });
    }

    [TestMethod]
    public void Completion_plan_rejects_unknown_duplicate_disabled_or_invalid_reserve_requests()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var state = Load(service, id);
            var before = store.Get(id).Value!;
            CharacterCreationAttributeCompletionRequest[] invalid =
            [
                new(state.Binding, state.SnapshotDigest, ["UNKNOWN"]),
                new(state.Binding, state.SnapshotDigest, ["BOD", "BOD"]),
                new(state.Binding, state.SnapshotDigest, ["bod"]),
                new(state.Binding, state.SnapshotDigest, ["ESS"]),
                new(state.Binding, state.SnapshotDigest, ["MAG"]),
                new(state.Binding, state.SnapshotDigest, ["BOD"], NormalPointsToReserve: -1),
                new(state.Binding, state.SnapshotDigest, ["BOD"], SpecialPointsToReserve: -1),
                new(state.Binding, state.SnapshotDigest, ["BOD"], NormalPointsToReserve: 21),
                new(state.Binding, state.SnapshotDigest, ["BOD"], SpecialPointsToReserve: 2)
            ];
            foreach (var request in invalid)
            {
                var result = CharacterCreationAttributeCompletionPlanner.Plan(state, request);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, result.Outcome);
                Assert.IsNull(result.Value);
                CollectionAssert.Contains(result.Blockers.ToList(), CharacterCreationAttributeCompletionBlockers.RequestInvalid);
            }
            Assert.AreEqual(before.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        });
    }

    [TestMethod]
    public void Completion_plan_rejects_binding_snapshot_and_state_tampering_without_writes()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var state = Load(service, id);
            var before = store.Get(id).Value!;
            var request = new CharacterCreationAttributeCompletionRequest(state.Binding, state.SnapshotDigest, ["BOD"]);
            var stale = CharacterCreationAttributeCompletionPlanner.Plan(state,
                request with { Binding = state.Binding with { ContentRevision = state.Binding.ContentRevision + 1 } });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, stale.Outcome);
            Assert.IsNull(stale.Value);
            CollectionAssert.Contains(stale.Blockers.ToList(), CharacterCreationAttributeCompletionBlockers.SnapshotConflict);
            var wrongDigest = CharacterCreationAttributeCompletionPlanner.Plan(state,
                request with { SnapshotDigest = Digest('f') });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, wrongDigest.Outcome);
            Assert.IsNull(wrongDigest.Value);
            CollectionAssert.Contains(wrongDigest.Blockers.ToList(), CharacterCreationAttributeCompletionBlockers.SnapshotConflict);
            var changedState = CharacterCreationAttributeCompletionPlanner.Plan(state with { KarmaAttribute = 0 }, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, changedState.Outcome);
            Assert.IsNull(changedState.Value);
            CollectionAssert.Contains(changedState.Blockers.ToList(), CharacterCreationAttributesBlockers.AuthorityUnavailable);
            Assert.AreEqual(before.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        });
    }

    [TestMethod]
    public void Completion_plan_cancellation_and_detached_outputs_leave_the_workspace_unchanged()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var state = Load(service, id);
            var before = store.Get(id).Value!;
            string[] order = ["BOD", "AGI"];
            var request = new CharacterCreationAttributeCompletionRequest(state.Binding, state.SnapshotDigest, order);
            bool canceled = false;
            try { CharacterCreationAttributeCompletionPlanner.Plan(state, request, new CancellationToken(true)); }
            catch (OperationCanceledException) { canceled = true; }
            Assert.IsTrue(canceled);
            var plan = CharacterCreationAttributeCompletionPlanner.Plan(state, request).Value!;
            order[0] = "ESS";
            Assert.AreEqual("BOD", plan.Request.PermittedAttributeOrder[0]);
            Assert.IsFalse(ReferenceEquals(state.Attributes, plan.Preview.Attributes));
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                state, Load(service, id)), "Planning and returned arrays must not mutate the input snapshot.");
            Assert.AreEqual(before.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.SavedRevision, store.Get(id).Value!.SavedRevision);
            Assert.AreEqual(before.Document.Content, store.Get(id).Value!.Document.Content);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        });
    }

    [TestMethod]
    public void Completion_plan_stops_at_its_candidate_budget_with_an_honest_partial_plan()
    {
        // An intentionally large admitted test authority makes the bounded-work
        // path observable without relying on timing or changing real source rules.
        var authority = CharacterCreationPrerequisiteServiceTests.CreateAuthority(
            CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]);
        authority = authority with
        {
            Options = authority.Options.Select(option => option with
            {
                BaseNormalAttributePoints = option.CategoryId == CharacterCreationPriorityCategoryIds.Attributes
                    ? 300 : option.BaseNormalAttributePoints,
                HeritageOptions = option.HeritageOptions.Select(heritage => heritage with
                {
                    Attributes = heritage.Attributes.Select(attribute => attribute.AttributeId == "BOD"
                        ? attribute with { Maximum = 400, AugmentedMaximum = 400 } : attribute).ToArray()
                }).ToArray()
            }).ToArray(),
            AuthorityDigest = string.Empty
        };
        authority = authority with { AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority) };
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var state = Load(service, id);
            var before = store.Get(id).Value!;
            var result = CharacterCreationAttributeCompletionPlanner.Plan(state,
                new(state.Binding, state.SnapshotDigest, ["BOD"], SpecialPointsToReserve: 1));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            Assert.IsNotNull(result.Value);
            var plan = result.Value;
            Assert.AreEqual(CharacterCreationAttributeCompletionPlanner.MaximumCandidateEvaluations + 1, plan.Evaluations);
            Assert.AreEqual(CharacterCreationAttributeCompletionPlanner.MaximumCandidateEvaluations,
                plan.Allocations.Single(a => a.AttributeId == "BOD").PriorityPoints);
            Assert.AreEqual(44m, plan.Preview.NormalPointBudget.Remaining);
            Assert.IsTrue(plan.Preview.CanConfirm);
            CollectionAssert.Contains(plan.Blockers.ToList(), CharacterCreationAttributeCompletionBlockers.NormalPointsUnallocated);
            CollectionAssert.Contains(plan.Blockers.ToList(), CharacterCreationAttributeCompletionBlockers.WorkLimitReached);
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                service.Preview(new(state.Binding, plan.Allocations)).Value, plan.Preview));
            Assert.AreEqual(before.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);

            var targeted = CharacterCreationAttributeCompletionPlanner.Plan(state,
                new(state.Binding, state.SnapshotDigest, ["BOD"])
                { Targets = [new("BOD", 400)] });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, targeted.Outcome);
            Assert.IsNotNull(targeted.Value);
            Assert.AreEqual(CharacterCreationAttributeCompletionPlanner.MaximumCandidateEvaluations, targeted.Value.Evaluations);
            Assert.AreEqual(CharacterCreationAttributeCompletionPlanner.MaximumCandidateEvaluations - 1,
                targeted.Value.Allocations.Single(a => a.AttributeId == "BOD").PriorityPoints);
            CollectionAssert.Contains(targeted.Blockers.ToArray(), CharacterCreationAttributeCompletionBlockers.TargetsUnfulfilled);
            CollectionAssert.Contains(targeted.Blockers.ToArray(), CharacterCreationAttributeCompletionBlockers.WorkLimitReached);
            CollectionAssert.DoesNotContain(targeted.Blockers.ToArray(), CharacterCreationAttributeCompletionBlockers.NormalPointsUnallocated);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        }, authority);
    }

    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Completion_explicit_targets_stop_at_totals_and_leave_permitted_untargeted_attributes_unchanged(string method)
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var state = Load(service, id);
            var before = store.Get(id).Value!;
            CharacterCreationAttributeCompletionTarget[] targets = [new("AGI", 3), new("BOD", 2)];
            var request = new CharacterCreationAttributeCompletionRequest(state.Binding, state.SnapshotDigest, ["BOD", "AGI", "EDG"])
            { Targets = targets };
            var result = CharacterCreationAttributeCompletionPlanner.Plan(state, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            Assert.IsNotNull(result.Value);
            var plan = result.Value;
            Assert.AreEqual(3, plan.Preview.Attributes.Single(a => a.AttributeId == "AGI").UnaugmentedCurrent);
            Assert.AreEqual(2, plan.Preview.Attributes.Single(a => a.AttributeId == "BOD").UnaugmentedCurrent);
            Assert.AreEqual(0, plan.Allocations.Single(a => a.AttributeId == "EDG").PriorityPoints);
            Assert.IsTrue(plan.Allocations.Where(a => a.AttributeId is not ("AGI" or "BOD"))
                .All(a => a.PriorityPoints == 0 && a.KarmaLevels == 0));
            Assert.AreEqual(17m, plan.Preview.NormalPointBudget.Remaining);
            Assert.AreEqual(1m, plan.Preview.SpecialPointBudget.Remaining);
            Assert.AreEqual(0, plan.Blockers.Count);
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                service.Preview(new(state.Binding, plan.Allocations)).Value, plan.Preview));
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                plan, CharacterCreationAttributeCompletionPlanner.Plan(state, request).Value));
            targets[0] = new("ESS", 100);
            Assert.AreEqual("AGI", plan.Request.Targets![0].AttributeId);
            Assert.AreEqual(3, plan.Request.Targets[0].TargetUnaugmentedRating);
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(state, Load(service, id)));
            Assert.AreEqual(before.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.Document.Content, store.Get(id).Value!.Document.Content);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        }, method: method);
    }

    [TestMethod]
    public void Completion_explicit_target_order_and_reserves_report_unmet_goals_not_unused_pools()
    {
        WithConfirmedPrerequisite((_, service, id, _) =>
        {
            var state = Load(service, id);
            var result = CharacterCreationAttributeCompletionPlanner.Plan(state,
                new(state.Binding, state.SnapshotDigest, ["BOD", "AGI", "EDG"], NormalPointsToReserve: 19, SpecialPointsToReserve: 1)
                { Targets = [new("AGI", 3), new("BOD", 3), new("EDG", 3)] });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            Assert.IsNotNull(result.Value);
            Assert.AreEqual(1, result.Value.Allocations.Single(a => a.AttributeId == "AGI").PriorityPoints);
            Assert.AreEqual(0, result.Value.Allocations.Single(a => a.AttributeId == "BOD").PriorityPoints);
            Assert.AreEqual(0, result.Value.Allocations.Single(a => a.AttributeId == "EDG").PriorityPoints);
            Assert.AreEqual(19m, result.Value.Preview.NormalPointBudget.Remaining);
            Assert.AreEqual(1m, result.Value.Preview.SpecialPointBudget.Remaining);
            CollectionAssert.AreEqual(new[] { CharacterCreationAttributeCompletionBlockers.TargetsUnfulfilled }, result.Blockers.ToArray());
            Assert.IsTrue(result.Value.Preview.CanConfirm);
        });
    }

    [TestMethod]
    public void Completion_explicit_targets_preserve_higher_manual_values_and_never_reprice_existing_Karma()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var initial = Load(service, id);
            CharacterCreationAttributeAllocation[] baseline = [new("BOD", 2, 0), new("AGI", 0, 1)];
            var preview = service.Preview(new(initial.Binding, baseline)).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new(initial.Binding, baseline, preview.PreviewDigest, true)).Outcome);
            var state = Load(service, id);
            var before = store.Get(id).Value!;
            var request = new CharacterCreationAttributeCompletionRequest(state.Binding, state.SnapshotDigest, ["BOD", "AGI"])
            { Targets = [new("BOD", 2), new("AGI", 1)] };
            var preserved = CharacterCreationAttributeCompletionPlanner.Plan(state, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, preserved.Outcome);
            Assert.IsNotNull(preserved.Value);
            Assert.IsFalse(preserved.Value.HasChanges);
            Assert.AreEqual(3, preserved.Value.Preview.Attributes.Single(a => a.AttributeId == "BOD").Current);
            Assert.AreEqual(2, preserved.Value.Preview.Attributes.Single(a => a.AttributeId == "AGI").Current);
            Assert.AreEqual(10m, preserved.Value.Preview.CreationKarmaBudget.Used);
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                state.PendingDraft!.Allocations, preserved.Value.Allocations));

            var blocked = CharacterCreationAttributeCompletionPlanner.Plan(state,
                request with { Targets = [new("AGI", 3)] });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, blocked.Outcome);
            Assert.IsNotNull(blocked.Value);
            Assert.IsFalse(blocked.Value.HasChanges);
            Assert.AreEqual(10m, blocked.Value.Preview.CreationKarmaBudget.Used);
            CollectionAssert.AreEqual(new[] { CharacterCreationAttributeCompletionBlockers.TargetsUnfulfilled }, blocked.Blockers.ToArray());
            Assert.AreEqual(before.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        });
    }

    [TestMethod]
    public void Completion_explicit_magic_target_includes_the_source_grant_without_spending_it_again()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var state = Load(service, id);
            var before = store.Get(id).Value!;
            var request = new CharacterCreationAttributeCompletionRequest(state.Binding, state.SnapshotDigest, ["MAG", "EDG"])
            { Targets = [new("MAG", 3)] };
            var retained = CharacterCreationAttributeCompletionPlanner.Plan(state, request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, retained.Outcome);
            Assert.IsFalse(retained.Value!.HasChanges);
            Assert.AreEqual(0m, retained.Value.Preview.SpecialPointBudget.Used);

            var raised = CharacterCreationAttributeCompletionPlanner.Plan(state, request with { Targets = [new("MAG", 4)] });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, raised.Outcome);
            Assert.AreEqual(4, raised.Value!.Preview.Attributes.Single(a => a.AttributeId == "MAG").UnaugmentedCurrent);
            Assert.AreEqual(1, raised.Value.Allocations.Single(a => a.AttributeId == "MAG").PriorityPoints);
            Assert.AreEqual(0, raised.Value.Allocations.Single(a => a.AttributeId == "EDG").PriorityPoints);
            Assert.AreEqual(1m, raised.Value.Preview.SpecialPointBudget.Used);
            Assert.AreEqual(20m, raised.Value.Preview.NormalPointBudget.Remaining);
            Assert.AreEqual(0m, raised.Value.Preview.CreationKarmaBudget.Used);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        }, WithTalent("Magician", "MAG", 3), "awakened");
    }

    [TestMethod]
    public void Completion_explicit_targets_validate_shape_permissions_and_bound_unreachable_ratings()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var state = Load(service, id);
            var before = store.Get(id).Value!;
            var request = new CharacterCreationAttributeCompletionRequest(state.Binding, state.SnapshotDigest, ["BOD"]);
            IReadOnlyList<CharacterCreationAttributeCompletionTarget>[] invalid =
            [
                [new("BOD", -1)], [new("BOD", 2), new("BOD", 3)], [new("AGI", 2)],
                [new("UNKNOWN", 2)], [new("bod", 2)], [new("ESS", 2)], [new("", 2)], [null!],
                Enumerable.Range(0, 14).Select(_ => new CharacterCreationAttributeCompletionTarget("BOD", 2)).ToArray()
            ];
            foreach (var targets in invalid)
            {
                var result = CharacterCreationAttributeCompletionPlanner.Plan(state, request with { Targets = targets });
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, result.Outcome);
                Assert.IsNull(result.Value);
                CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationAttributeCompletionBlockers.RequestInvalid);
            }

            var capped = CharacterCreationAttributeCompletionPlanner.Plan(state,
                request with { Targets = [new("BOD", int.MaxValue)] });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, capped.Outcome);
            Assert.IsNotNull(capped.Value);
            Assert.AreEqual(6, capped.Value.Preview.Attributes.Single(a => a.AttributeId == "BOD").Current);
            Assert.IsTrue(capped.Value.Evaluations <= CharacterCreationAttributeCompletionPlanner.MaximumCandidateEvaluations);
            CollectionAssert.AreEqual(new[] { CharacterCreationAttributeCompletionBlockers.TargetsUnfulfilled }, capped.Blockers.ToArray());
            Assert.IsTrue(capped.Value.Preview.CanConfirm);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        });
    }

    [TestMethod]
    public void Completion_empty_and_zero_targets_do_not_allocate_and_null_targets_keep_legacy_serialization()
    {
        WithConfirmedPrerequisite((_, service, id, _) =>
        {
            var state = Load(service, id);
            var legacy = new CharacterCreationAttributeCompletionRequest(state.Binding, state.SnapshotDigest, ["BOD"]);
            Assert.IsFalse(JsonSerializer.SerializeToElement(legacy).TryGetProperty("Targets", out _));
            Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(new
            {
                legacy.Binding, legacy.SnapshotDigest, legacy.PermittedAttributeOrder,
                legacy.NormalPointsToReserve, legacy.SpecialPointsToReserve
            }), CharacterCreationFinalizationDigest.Compute(legacy));
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                CharacterCreationAttributeCompletionPlanner.Plan(state, legacy),
                CharacterCreationAttributeCompletionPlanner.Plan(state, legacy with { Targets = null })));
            foreach (var targets in new IReadOnlyList<CharacterCreationAttributeCompletionTarget>[] { [], [new("BOD", 0)] })
            {
                var result = CharacterCreationAttributeCompletionPlanner.Plan(state, legacy with { Targets = targets });
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
                Assert.IsNotNull(result.Value);
                Assert.IsFalse(result.Value.HasChanges);
                Assert.AreEqual(1, result.Value.Evaluations);
                Assert.AreEqual(state.NormalPointBudget.Remaining, result.Value.Preview.NormalPointBudget.Remaining);
                Assert.AreEqual(state.SpecialPointBudget.Remaining, result.Value.Preview.SpecialPointBudget.Remaining);
            }
            Assert.Throws<OperationCanceledException>(() => CharacterCreationAttributeCompletionPlanner.Plan(state,
                legacy with { Targets = [new("BOD", 2)] }, new CancellationToken(true)));
        });
    }

    [TestMethod]
    public void Player_choice_null_preserves_legacy_JSON_and_canonical_digests()
    {
        WithConfirmedPrerequisite((_, service, id, _) =>
        {
            var state = Load(service, id);
            var request = new CharacterCreationAttributesPreviewRequest(state.Binding, []);
            var preview = service.Preview(request).Value!;
            var confirmation = new CharacterCreationAttributesConfirmRequest(
                preview.Binding, [], preview.PreviewDigest, true);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, service.Confirm(confirmation).Outcome);
            var draft = Load(service, id).PendingDraft!;

            Assert.IsNull(request.PlayerChosenAttributeIds);
            Assert.IsNull(confirmation.PlayerChosenAttributeIds);
            Assert.IsNull(preview.PlayerChosenAttributeIds);
            Assert.IsNull(draft.PlayerChosenAttributeIds);
            AssertLegacyPlayerChoiceShape(request, new { request.Binding, request.Allocations });
            AssertLegacyPlayerChoiceShape(confirmation, new
            {
                confirmation.Binding, confirmation.Allocations, confirmation.PreviewDigest,
                confirmation.ExplicitlyConfirmed
            });
            AssertLegacyPlayerChoiceShape(preview, new
            {
                preview.Schema, preview.Binding, preview.Attributes, preview.NormalPointBudget,
                preview.SpecialPointBudget, preview.CreationKarmaBudget, preview.Blockers,
                preview.RequiresExplicitConfirmation, preview.CanConfirm, preview.PreviewDigest
            });
            AssertLegacyPlayerChoiceShape(draft, new
            {
                draft.Schema, draft.WorkspaceId, draft.DraftRevision, draft.BaseContentRevision,
                draft.BaseRawCharacterXmlDigest, draft.PrerequisiteDraftRevision, draft.PrerequisiteDraftDigest,
                draft.PrerequisiteAuthorityDigest, draft.MetatypeSourceId, draft.MetatypeSourceNodeDigest,
                draft.HalvesNormalAttributePoints, draft.NormalPointTotal, draft.NormalPointUsed,
                draft.SpecialPointTotal, draft.SpecialPointUsed, draft.CreationKarmaTotal, draft.CreationKarmaUsed,
                draft.Allocations, draft.Attributes, draft.SourceAnchorIds, draft.CharacterEffectsApplied,
                draft.DraftDigest
            });
            Assert.IsTrue(JsonSerializer.SerializeToElement(request with { PlayerChosenAttributeIds = [] })
                .TryGetProperty(nameof(request.PlayerChosenAttributeIds), out var empty));
            Assert.AreEqual(0, empty.GetArrayLength());
        });
    }

    [TestMethod]
    public void Player_choice_zero_is_a_durable_metadata_change_and_exact_duplicates_do_not_write()
    {
        string? stateDirectory = null;
        WithConfirmedPrerequisite((store, service, id, beforeXml) =>
        {
            var state = Load(service, id);
            var untouched = service.Preview(new(state.Binding, []) { PlayerChosenAttributeIds = [] }).Value!;
            var first = service.Confirm(new(untouched.Binding, [], untouched.PreviewDigest, true)
            {
                PlayerChosenAttributeIds = []
            });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, first.Outcome);

            var resolver = new StubSourceResolver(CharacterCreationPrerequisiteServiceTests.CreateAuthority(
                CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]));
            var coldStore = new FileWorkspaceStore(stateDirectory!);
            var coldService = new CharacterCreationAttributesService(coldStore, resolver);
            var reopened = Load(coldService, id);
            Assert.IsNotNull(reopened.PendingDraft!.PlayerChosenAttributeIds);
            Assert.AreEqual(0, reopened.PendingDraft.PlayerChosenAttributeIds.Count);
            var originalDraft = reopened.PendingDraft;
            var allocations = originalDraft.Allocations;
            Assert.IsTrue(allocations.All(item => item.PriorityPoints == 0 && item.KarmaLevels == 0));

            var same = coldService.Preview(new(reopened.Binding, allocations) { PlayerChosenAttributeIds = [] });
            CollectionAssert.Contains(same.Blockers.ToArray(), CharacterCreationAttributesBlockers.DraftDuplicate);
            var chosen = coldService.Preview(new(reopened.Binding, allocations)
            {
                PlayerChosenAttributeIds = ["BOD"]
            });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, chosen.Outcome);
            Assert.IsNotNull(chosen.Value);
            Assert.IsTrue(chosen.Value.CanConfirm);
            Assert.AreNotEqual(same.Value!.PreviewDigest, chosen.Value.PreviewDigest);
            var saved = coldService.Confirm(new(chosen.Value.Binding, allocations, chosen.Value.PreviewDigest, true)
            {
                PlayerChosenAttributeIds = ["BOD"]
            });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, saved.Outcome);
            Assert.AreEqual(first.Value!.ContentRevision + 1, saved.Value!.ContentRevision);
            Assert.AreEqual(originalDraft.DraftRevision + 1, saved.Value.DraftRevision);
            Assert.AreNotEqual(originalDraft.DraftDigest, saved.Value.DraftDigest);

            var secondColdService = new CharacterCreationAttributesService(new FileWorkspaceStore(stateDirectory!), resolver);
            var retained = Load(secondColdService, id);
            CollectionAssert.AreEqual(new[] { "BOD" }, retained.PendingDraft!.PlayerChosenAttributeIds!.ToArray());
            CollectionAssert.AreEqual(allocations.ToArray(), retained.PendingDraft.Allocations.ToArray());
            Assert.AreEqual(originalDraft.NormalPointUsed, retained.PendingDraft.NormalPointUsed);
            Assert.AreEqual(originalDraft.SpecialPointUsed, retained.PendingDraft.SpecialPointUsed);
            Assert.AreEqual(originalDraft.CreationKarmaUsed, retained.PendingDraft.CreationKarmaUsed);
            var beforeDuplicate = store.Get(id).Value!;
            Assert.AreEqual(beforeXml, beforeDuplicate.Document.Content);
            var duplicate = secondColdService.Preview(new(retained.Binding, allocations)
            {
                PlayerChosenAttributeIds = ["BOD"]
            });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, duplicate.Outcome);
            CollectionAssert.Contains(duplicate.Blockers.ToArray(), CharacterCreationAttributesBlockers.DraftDuplicate);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                secondColdService.Confirm(new(retained.Binding, allocations, duplicate.Value!.PreviewDigest, true)
                {
                    PlayerChosenAttributeIds = ["BOD"]
                }).Outcome);
            Assert.AreEqual(beforeDuplicate.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(beforeDuplicate.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        }, stateDirectoryReady: directory => stateDirectory = directory);
    }

    [TestMethod]
    public void Player_choices_are_monotonic_and_legacy_unknown_cannot_be_relabelled()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var state = Load(service, id);
            var preview = service.Preview(new(state.Binding, []) { PlayerChosenAttributeIds = ["BOD"] }).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new(preview.Binding, [], preview.PreviewDigest, true)
                {
                    PlayerChosenAttributeIds = ["BOD"]
                }).Outcome);
            state = Load(service, id);
            var before = store.Get(id).Value!;
            foreach (IReadOnlyList<string> removed in new IReadOnlyList<string>[] { [], ["AGI"] })
            {
                var rejected = service.Preview(new(state.Binding, []) { PlayerChosenAttributeIds = removed });
                CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationAttributesBlockers.PlayerChoiceIntentInvalid);
                Assert.IsFalse(rejected.Value?.CanConfirm ?? false);
            }
            var added = service.Preview(new(state.Binding, []) { PlayerChosenAttributeIds = ["BOD", "AGI"] });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, added.Outcome);
            CollectionAssert.AreEqual(new[] { "AGI", "BOD" }, added.Value!.PlayerChosenAttributeIds!.ToArray());
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);

            // An old caller may conservatively downgrade to unknown, never to known-empty.
            CharacterCreationAttributeAllocation[] changed = [new("BOD", 1, 0)];
            var legacy = service.Preview(new(state.Binding, changed));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, legacy.Outcome);
            Assert.IsNull(legacy.Value!.PlayerChosenAttributeIds);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new(state.Binding, changed, legacy.Value.PreviewDigest, true)).Outcome);
            state = Load(service, id);
            Assert.IsNull(state.PendingDraft!.PlayerChosenAttributeIds);
            var unknown = store.Get(id).Value!;
            foreach (IReadOnlyList<string> relabelled in new IReadOnlyList<string>[]
                     { [], ["BOD"], state.Attributes.Select(item => item.AttributeId).ToArray() })
            {
                var rejected = service.Preview(new(state.Binding, changed) { PlayerChosenAttributeIds = relabelled });
                CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationAttributesBlockers.PlayerChoiceIntentInvalid);
                Assert.IsFalse(rejected.Value?.CanConfirm ?? false);
            }
            Assert.AreEqual(unknown.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(unknown.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        });
    }

    [TestMethod]
    public void Player_choice_invalid_ids_and_unacknowledged_numeric_changes_do_not_write()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var state = Load(service, id);
            var before = store.Get(id).Value!;
            IReadOnlyList<string>[] invalid = [["UNKNOWN"], ["bod"], [""], [null!], ["BOD", "BOD"]];
            foreach (var ids in invalid)
            {
                var rejected = service.Preview(new(state.Binding, []) { PlayerChosenAttributeIds = ids });
                CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationAttributesBlockers.PlayerChoiceIntentInvalid);
                Assert.IsFalse(rejected.Value?.CanConfirm ?? false);
            }
            foreach (var allocation in new CharacterCreationAttributeAllocation[]
                     { new("BOD", 1, 0), new("BOD", 0, 1) })
            foreach (IReadOnlyList<string> ids in new IReadOnlyList<string>[] { [], ["AGI"] })
            {
                var rejected = service.Preview(new(state.Binding, [allocation]) { PlayerChosenAttributeIds = ids });
                CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationAttributesBlockers.PlayerChoiceIntentInvalid);
                Assert.IsFalse(rejected.Value?.CanConfirm ?? false);
            }
            Assert.AreEqual(before.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        });
    }

    [TestMethod]
    public void Player_choice_confirmation_cannot_substitute_intent_under_an_unchanged_preview_digest()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var state = Load(service, id);
            var before = store.Get(id).Value!;
            var preview = service.Preview(new(state.Binding, []) { PlayerChosenAttributeIds = [] }).Value!;
            var substituted = service.Confirm(new(preview.Binding, [], preview.PreviewDigest, true)
            {
                PlayerChosenAttributeIds = ["BOD"]
            });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, substituted.Outcome);
            CollectionAssert.Contains(substituted.Blockers.ToArray(), CharacterCreationAttributesBlockers.PreviewDigestMismatch);
            Assert.AreEqual(before.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new(preview.Binding, [], preview.PreviewDigest, true)
                {
                    PlayerChosenAttributeIds = []
                }).Outcome);
        });
    }

    [TestMethod]
    public void Player_choice_explicit_session_matches_live_while_speculation_preserves_original_intent()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var state = Load(service, id);
            var baseline = service.Preview(new(state.Binding, []) { PlayerChosenAttributeIds = [] }).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new(baseline.Binding, [], baseline.PreviewDigest, true)
                {
                    PlayerChosenAttributeIds = []
                }).Outcome);
            state = Load(service, id);
            var before = store.Get(id).Value!;
            var session = CharacterCreationAttributesService.CreateDraftSession(state);
            Assert.IsNotNull(session);
            CharacterCreationAttributeAllocation[] allocations = [new("BOD", 1, 0)];
            string[] ids = ["BOD"];
            var live = service.Preview(new(state.Binding, allocations) { PlayerChosenAttributeIds = ids });
            var explicitPreview = session.Preview(allocations, ids);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, explicitPreview.Outcome);
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(live, explicitPreview));
            ids[0] = "AGI";
            CollectionAssert.AreEqual(new[] { "BOD" }, explicitPreview.Value!.PlayerChosenAttributeIds!.ToArray());
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                service.Preview(new(state.Binding, allocations) { PlayerChosenAttributeIds = [] }),
                session.Preview(allocations, [])));

            var speculative = session.Preview(allocations);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, speculative.Outcome);
            Assert.IsNotNull(speculative.Value!.PlayerChosenAttributeIds);
            Assert.AreEqual(0, speculative.Value.PlayerChosenAttributeIds.Count);
            var plan = CharacterCreationAttributeCompletionPlanner.Plan(state,
                new(state.Binding, state.SnapshotDigest, ["BOD"]));
            Assert.IsNotNull(plan.Value);
            Assert.IsTrue(plan.Value.HasChanges);
            Assert.IsNotNull(plan.Value.Preview.PlayerChosenAttributeIds);
            Assert.AreEqual(0, plan.Value.Preview.PlayerChosenAttributeIds.Count);
            Assert.AreEqual(0, state.PendingDraft!.PlayerChosenAttributeIds!.Count);
            Assert.AreEqual(before.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        });
    }

    [TestMethod]
    public void Player_choice_rehashed_drafts_cannot_hide_nonzero_allocations_or_noncanonical_ids()
    {
        WithConfirmedPrerequisite((_, service, id, _) =>
        {
            var state = Load(service, id);
            CharacterCreationAttributeAllocation[] allocations = [new("BOD", 1, 0)];
            var preview = service.Preview(new(state.Binding, allocations)
            {
                PlayerChosenAttributeIds = ["BOD"]
            }).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new(preview.Binding, allocations, preview.PreviewDigest, true)
                {
                    PlayerChosenAttributeIds = ["BOD"]
                }).Outcome);
            state = Load(service, id);
            var original = state.PendingDraft!;
            Assert.IsTrue(CharacterCreationAttributesDraftIntegrity.IsStructurallyValidPending(original, id,
                state.Binding.ContentRevision, state.Binding.RawCharacterXmlDigest, state.PrerequisiteDraft!));
            IReadOnlyList<string>[] invalid = [[], ["UNKNOWN"], ["BOD", "BOD"], ["BOD", "AGI"], [null!]];
            foreach (var ids in invalid)
            {
                var forged = original with { PlayerChosenAttributeIds = ids };
                forged = forged with { DraftDigest = CharacterCreationAttributesDraftIntegrity.ComputeDigest(forged) };
                Assert.IsFalse(CharacterCreationAttributesDraftIntegrity.IsStructurallyValidPending(forged, id,
                    state.Binding.ContentRevision, state.Binding.RawCharacterXmlDigest, state.PrerequisiteDraft!));
            }
            foreach (var malformed in new[]
                     { original with { Attributes = [null!] }, original with { Allocations = [null!] } })
            {
                var rehashed = malformed with { DraftDigest = CharacterCreationAttributesDraftIntegrity.ComputeDigest(malformed) };
                Assert.IsFalse(CharacterCreationAttributesDraftIntegrity.IsStructurallyValidPending(rehashed, id,
                    state.Binding.ContentRevision, state.Binding.RawCharacterXmlDigest, state.PrerequisiteDraft!));
            }
        });
    }

    private static void AssertLegacyPlayerChoiceShape(object value, object legacy)
    {
        Assert.IsFalse(JsonSerializer.SerializeToElement(value).TryGetProperty("PlayerChosenAttributeIds", out _));
        CollectionAssert.AreEqual(JsonSerializer.SerializeToUtf8Bytes(legacy), JsonSerializer.SerializeToUtf8Bytes(value));
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(legacy), CharacterCreationFinalizationDigest.Compute(value));
    }

    [TestMethod]
    public void Draft_session_matches_fresh_preview_and_cannot_authorize_a_stale_save()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            var state = Load(service, id);
            var session = CharacterCreationAttributesService.CreateDraftSession(state);
            Assert.IsNotNull(session);
            Assert.IsNull(CharacterCreationAttributesService.CreateDraftSession(state with { KarmaAttribute = 0 }));
            CharacterCreationAttributeAllocation[][] choices =
            [[], [new("BOD", 1, 0)], [new("BOD", 5, 0), new("AGI", 5, 0)],
                [new("EDG", 1, 0)], [new("EDG", 2, 0)], [new("MAG", 1, 0)],
                [new("BOD", 0, 3)], [new("BOD", -1, 0)], [new("BOD", 6, 0)]];
            foreach (var choice in choices)
                Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    service.Preview(new(state.Binding, choice)), session.Preview(choice)));
            CharacterCreationAttributeAllocation[] accepted = [new("BOD", 1, 0)];
            var preview = session.Preview(accepted).Value!;
            var confirmed = service.Confirm(new(state.Binding, accepted, preview.PreviewDigest, true));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new(state.Binding, accepted, preview.PreviewDigest, true)).Outcome);
            var reopened = Load(service, id);
            var resumed = CharacterCreationAttributesService.CreateDraftSession(reopened)!;
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                service.Preview(new(reopened.Binding, accepted)), resumed.Preview(accepted)));
            Assert.AreEqual(confirmed.Value!.ContentRevision, store.Get(id).Value!.ContentRevision);
        });
    }

    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Owner_bound_attributes_use_only_the_admitted_partition_and_reject_old_stamps(string method)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"chummer-owner-attributes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new FileWorkspaceStore(directory);
            var owners = new AllocationOwners();
            var ownerA = new OwnerScope("attributes-owner-a");
            var ownerB = new OwnerScope("attributes-owner-b");
            owners.Set(ownerA);
            OwnerContextStamp original = owners.Capture();
            var id = new CharacterWorkspaceId("scoped-attributes");
            var document = new WorkspaceDocument(
                $"<character><name>Scoped</name><buildmethod>{method}</buildmethod><created>false</created><karma>25</karma></character>",
                RulesetDefaults.Sr5);
            Assert.IsTrue(store.CreateWorkspaceDocument(ownerA, id, document).Success);
            Assert.IsTrue(store.CreateWorkspaceDocument(ownerB, id, document).Success);
            var resolver = new StubSourceResolver(CharacterCreationPrerequisiteServiceTests.CreateAuthority(
                method, ["A", "B", "C", "D", "E"]));
            var prerequisites = new OwnerBoundCharacterCreationPrerequisiteService(store, owners, new StubCharacterQueries(), resolver);
            var prerequisite = prerequisites.Load(original, new(id)).Value!;
            var ranks = CharacterCreationPrerequisiteServiceTests.Assign("A", "E", "B", "C", "D");
            var proposed = prerequisites.Preview(original, new(prerequisite.Binding, ranks)
                { HeritageSelectionId = "human", TalentSelectionId = "mundane" }).Value!;
            var prerequisiteSaved = prerequisites.Confirm(original, new(proposed.Binding, ranks, proposed.PreviewDigest, true)
                { HeritageSelectionId = "human", TalentSelectionId = "mundane" });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, prerequisiteSaved.Outcome);
            var service = new OwnerBoundCharacterCreationAttributesService(store, owners, resolver);
            Assert.IsFalse(store.Get(id).Success, "No legacy copy may mask a missing owner partition.");
            WorkspaceStoredDocument before = store.Get(ownerA, id).Value!;
            WorkspaceStoredDocument other = store.Get(ownerB, id).Value!;
            var loaded = service.Load(original, new(id));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome);
            Assert.IsTrue(loaded.Value!.CanEdit);
            CharacterCreationAttributeAllocation[] allocations = [new("BOD", 1, 0)];
            var preview = service.Preview(original, new(loaded.Value.Binding, allocations)).Value!;
            Assert.IsTrue(preview.CanConfirm);
            var command = new CharacterCreationAttributesConfirmRequest(preview.Binding, allocations, preview.PreviewDigest, true);

            foreach (OwnerContextStamp denied in new[] { default(OwnerContextStamp),
                         original with { AuthorityInstanceId = "foreign" }, original with { Owner = ownerB } })
            {
                Assert.IsNull(service.Load(denied, new(id)).Value);
                Assert.IsNull(service.Preview(denied, new(preview.Binding, allocations)).Value);
                Assert.IsNull(service.Confirm(denied, command).Value);
            }
            Assert.IsNull(service.Load(original, new(new CharacterWorkspaceId("missing"))).Value);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(original, command with { ExplicitlyConfirmed = false }).Outcome);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(original, command with { PreviewDigest = Digest('f') }).Outcome);
            owners.Set(ownerB);
            Assert.IsNull(service.Confirm(original, command).Value);
            owners.Set(ownerA);
            Assert.IsNull(service.Load(original, new(id)).Value, "A→B→A must not revive old authority.");
            Assert.IsNull(service.Confirm(original, command).Value);
            Assert.AreEqual(before.Document.Content, store.Get(ownerA, id).Value!.Document.Content);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(ownerA, id).Value!.Document.AuxiliaryStateDigest);
            Assert.AreEqual(before.ContentRevision, store.Get(ownerA, id).Value!.ContentRevision);

            OwnerContextStamp fresh = owners.Capture();
            var freshState = service.Load(fresh, new(id)).Value!;
            var freshPreview = service.Preview(fresh, new(freshState.Binding, allocations)).Value!;
            var confirmed = service.Confirm(fresh, new(freshPreview.Binding, allocations, freshPreview.PreviewDigest, true));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome);
            var coldStore = new FileWorkspaceStore(directory);
            var after = coldStore.Get(ownerA, id).Value!;
            Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
            Assert.AreEqual(after.ContentRevision, after.SavedRevision);
            Assert.AreEqual(document.Content, after.Document.Content);
            Assert.AreEqual(before.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft!.DraftDigest,
                after.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft!.DraftDigest);
            var reopened = new OwnerBoundCharacterCreationAttributesService(coldStore, owners, resolver).Load(fresh, new(id)).Value!;
            Assert.AreEqual(2, reopened.Attributes.Single(item => item.AttributeId == "BOD").Current);
            Assert.IsNotNull(reopened.PendingDraft);
            Assert.AreEqual(other.Document.Content, coldStore.Get(ownerB, id).Value!.Document.Content);
            Assert.AreEqual(other.Document.AuxiliaryStateDigest, coldStore.Get(ownerB, id).Value!.Document.AuxiliaryStateDigest);
            Assert.AreEqual(other.ContentRevision, coldStore.Get(ownerB, id).Value!.ContentRevision);
            Assert.IsFalse(coldStore.Get(id).Success);
            Assert.AreEqual(0, owners.ActiveLeases);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    internal sealed class AllocationOwners : IOwnerContextLeaseAccessor
    {
        private readonly object _gate = new();
        private OwnerContextStamp _stamp = new(OwnerScope.LocalSingleUser, Guid.NewGuid().ToString("N"), 0);
        public int ActiveLeases { get; private set; }
        public OwnerScope Current => Capture().Owner;
        public OwnerContextStamp Capture() { lock (_gate) return _stamp; }
        public void Set(OwnerScope owner)
        {
            lock (_gate)
            {
                Assert.AreEqual(0, ActiveLeases);
                _stamp = _stamp with { Owner = owner, TransitionRevision = checked(_stamp.TransitionRevision + 1) };
            }
        }
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Monitor.Enter(_gate);
            lease = null;
            if (expected != _stamp || !expected.IsValid || ActiveLeases != 0)
            { Monitor.Exit(_gate); return false; }
            ActiveLeases++;
            lease = new Lease(this, expected);
            return true;
        }
        private sealed class Lease(AllocationOwners owner, OwnerContextStamp stamp) : IOwnerContextLease
        {
            private bool _disposed;
            public OwnerContextStamp Stamp { get { ObjectDisposedException.ThrowIf(_disposed, this); return stamp; } }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                owner.ActiveLeases--;
                Monitor.Exit(owner._gate);
            }
        }
    }

    [TestMethod]
    public void Preview_projects_exact_normal_special_and_global_karma_budgets()
    {
        WithConfirmedPrerequisite((store, service, id, beforeXml) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            Assert.AreEqual(20m, state.NormalPointBudget.Total);
            Assert.AreEqual(1m, state.SpecialPointBudget.Total);
            Assert.AreEqual(25m, state.CreationKarmaBudget.Total);
            Assert.AreEqual(1, state.MaxNumberMaxAttributesCreate);
            Assert.AreEqual(5, state.KarmaAttribute);
            Assert.AreEqual(1, state.Attributes.Single(item => item.AttributeId == "BOD").Current);
            Assert.AreEqual(2, state.Attributes.Single(item => item.AttributeId == "EDG").Current);
            Assert.IsFalse(state.Attributes.Single(item => item.AttributeId == "MAG").IsEnabled);
            CharacterCreationAttributeProjection essence = state.Attributes.Single(item =>
                item.AttributeId == "ESS");
            Assert.IsFalse(essence.IsEnabled);
            Assert.AreEqual(0, essence.Minimum);
            Assert.AreEqual(6, essence.Maximum);
            Assert.AreEqual(6, essence.AugmentedMaximum);
            Assert.AreEqual(6, essence.Current);
            CollectionAssert.Contains(
                essence.DisableReasons.ToList(),
                CharacterCreationAttributesBlockers.EssenceNotSpendable);

            CharacterCreationFoundationResult<CharacterCreationAttributesPreview> result =
                service.Preview(new CharacterCreationAttributesPreviewRequest(
                    state.Binding,
                    [
                        new CharacterCreationAttributeAllocation("BOD", 5, 0),
                        new CharacterCreationAttributeAllocation("AGI", 0, 1),
                        new CharacterCreationAttributeAllocation("EDG", 1, 0)
                    ]));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            CharacterCreationAttributesPreview preview = result.Value!;
            Assert.IsTrue(preview.CanConfirm);
            Assert.AreEqual(5m, preview.NormalPointBudget.Used);
            Assert.AreEqual(15m, preview.NormalPointBudget.Remaining);
            Assert.AreEqual(1m, preview.SpecialPointBudget.Used);
            Assert.AreEqual(10m, preview.CreationKarmaBudget.Used);
            Assert.AreEqual(6, preview.Attributes.Single(item => item.AttributeId == "BOD").Current);
            Assert.AreEqual(2, preview.Attributes.Single(item => item.AttributeId == "AGI").Current);
            Assert.AreEqual(10, preview.Attributes.Single(item => item.AttributeId == "AGI").KarmaCost);
            Assert.AreEqual(beforeXml, store.Get(id).Value!.Document.Content);
            Assert.IsNull(store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationAttributesDraft);
        });
    }

    [TestMethod]
    public void Confirm_is_atomic_reopens_and_preserves_the_prerequisite_sibling()
    {
        WithConfirmedPrerequisite((store, service, id, beforeXml) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            CharacterCreationPrerequisiteDraft prerequisite = state.PrerequisiteDraft!;
            CharacterCreationAttributeAllocation[] allocations =
            [
                new("BOD", 4, 0),
                new("EDG", 1, 0)
            ];
            CharacterCreationAttributesPreview preview = service.Preview(
                new CharacterCreationAttributesPreviewRequest(state.Binding, allocations)).Value!;
            CharacterCreationFoundationResult<CharacterCreationAttributesReceipt> confirmed =
                service.Confirm(new CharacterCreationAttributesConfirmRequest(
                    preview.Binding,
                    allocations,
                    preview.PreviewDigest,
                    ExplicitlyConfirmed: true));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome);
            Assert.IsFalse(confirmed.Value!.CharacterDocumentChanged);
            WorkspaceStoredDocument after = store.Get(id).Value!;
            Assert.AreEqual(beforeXml, after.Document.Content);
            Assert.AreEqual(prerequisite.DraftDigest,
                after.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft!.DraftDigest);
            Assert.IsNotNull(after.Document.AuxiliaryState.CharacterCreationAttributesDraft);
            CharacterCreationAttributesState reopened = Load(service, id);
            Assert.AreEqual(5,
                reopened.Attributes.Single(item => item.AttributeId == "BOD").Current);
            Assert.AreEqual(1L, reopened.PendingDraft!.DraftRevision);
        });
    }

    [TestMethod]
    public void Halve_attribute_points_uses_exact_legacy_integer_division()
    {
        CharacterCreationPrerequisiteAuthority authority = WithHalvedHuman(
            CharacterCreationPrerequisiteServiceTests.CreateAuthority(
                CharacterCreationBuildMethods.Priority,
                ["A", "B", "C", "D", "E"]));
        WithConfirmedPrerequisite((_, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            Assert.AreEqual(10m, state.NormalPointBudget.Total);
            Assert.IsTrue(state.PrerequisiteDraft!.HeritageSelection!.HalvesNormalAttributePoints);
        }, authority);
    }

    [TestMethod]
    public void Disabled_duplicate_over_budget_and_maximum_count_allocations_fail_closed()
    {
        WithConfirmedPrerequisite((_, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            CharacterCreationFoundationResult<CharacterCreationAttributesPreview> invalid =
                service.Preview(new CharacterCreationAttributesPreviewRequest(
                    state.Binding,
                    [
                        new CharacterCreationAttributeAllocation("BOD", 5, 0),
                        new CharacterCreationAttributeAllocation("BOD", 1, 0),
                        new CharacterCreationAttributeAllocation("AGI", 5, 0),
                        new CharacterCreationAttributeAllocation("MAG", 1, 0),
                        new CharacterCreationAttributeAllocation("EDG", 6, 0)
                    ]));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, invalid.Outcome);
            CollectionAssert.Contains(invalid.Blockers.ToList(),
                CharacterCreationAttributesBlockers.AllocationDuplicate);
            CollectionAssert.Contains(invalid.Blockers.ToList(),
                CharacterCreationAttributesBlockers.AttributeDisabled);
            CollectionAssert.Contains(invalid.Blockers.ToList(),
                CharacterCreationAttributesBlockers.AllocationInvalid);
            CollectionAssert.Contains(invalid.Blockers.ToList(),
                CharacterCreationAttributesBlockers.MaximumAttributeCountExceeded);
        });
    }

    [TestMethod]
    public void Stale_binding_preview_tamper_and_missing_explicit_confirmation_do_not_write()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            CharacterCreationAttributeAllocation[] allocations = [new("BOD", 1, 0)];
            CharacterCreationAttributesPreview preview = service.Preview(
                new CharacterCreationAttributesPreviewRequest(state.Binding, allocations)).Value!;
            CharacterCreationAttributesBinding stale = state.Binding with
            {
                PrerequisiteDraftDigest = Digest('9')
            };
            CharacterCreationFoundationResult<CharacterCreationAttributesPreview> staleResult =
                service.Preview(new CharacterCreationAttributesPreviewRequest(stale, allocations));
            CharacterCreationFoundationResult<CharacterCreationAttributesReceipt> noConsent =
                service.Confirm(new CharacterCreationAttributesConfirmRequest(
                    preview.Binding,
                    allocations,
                    preview.PreviewDigest,
                    ExplicitlyConfirmed: false));
            CharacterCreationFoundationResult<CharacterCreationAttributesReceipt> tampered =
                service.Confirm(new CharacterCreationAttributesConfirmRequest(
                    preview.Binding,
                    allocations,
                    Digest('8'),
                    ExplicitlyConfirmed: true));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, staleResult.Outcome);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, noConsent.Outcome);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, tampered.Outcome);
            Assert.IsNull(store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationAttributesDraft);
        });
    }

    [TestMethod]
    public void Recomputed_draft_digest_cannot_hide_a_tampered_attribute_projection()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            CharacterCreationAttributeAllocation[] allocations = [new("BOD", 1, 0)];
            CharacterCreationAttributesPreview preview = service.Preview(
                new CharacterCreationAttributesPreviewRequest(state.Binding, allocations)).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new CharacterCreationAttributesConfirmRequest(
                    preview.Binding,
                    allocations,
                    preview.PreviewDigest,
                    ExplicitlyConfirmed: true)).Outcome);

            WorkspaceStoredDocument persisted = store.Get(id).Value!;
            CharacterCreationAttributesDraft current = persisted.Document.AuxiliaryState
                .CharacterCreationAttributesDraft!;
            CharacterCreationAttributeProjection[] forgedAttributes = current.Attributes
                .Select(item => item.AttributeId == "BOD"
                    ? item with { Current = item.Current + 1 }
                    : item)
                .ToArray();
            CharacterCreationAttributesDraft forged = current with
            {
                DraftRevision = current.DraftRevision + 1,
                BaseContentRevision = persisted.ContentRevision,
                Attributes = forgedAttributes,
                DraftDigest = string.Empty
            };
            forged = forged with
            {
                DraftDigest = CharacterCreationAttributesDraftIntegrity.ComputeDigest(forged)
            };
            WorkspaceDocument replacement = persisted.Document with
            {
                State = persisted.Document.State with
                {
                    AuxiliaryState = persisted.Document.AuxiliaryState with
                    {
                        CharacterCreationAttributesDraft = forged
                    }
                }
            };
            WorkspaceStoreMutationResult mutation = store
                .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    persisted.ContentRevision,
                    persisted.Document.AuxiliaryStateDigest,
                    replacement);
            Assert.IsTrue(mutation.Success, mutation.Error);

            CharacterCreationAttributesState rejected = Load(service, id);
            Assert.IsNull(rejected.PendingDraft);
            CollectionAssert.Contains(rejected.Blockers.ToList(),
                CharacterCreationAttributesBlockers.DraftInvalid);
        });
    }

    [TestMethod]
    [DataRow("Magician", "MAG", 3)]
    [DataRow("Adept", "MAG", 2)]
    [DataRow("Mystic Adept", "MAG", 3)]
    [DataRow("Aspected Magician", "MAG", 2)]
    [DataRow("Technomancer", "RES", 3)]
    public void Awakened_priority_grants_are_starting_values_with_separate_point_and_karma_costs(
        string kind, string attributeId, int startingValue)
    {
        CharacterCreationPrerequisiteAuthority authority = WithTalent(kind, attributeId, startingValue);
        WithConfirmedPrerequisite((store, service, id, xml) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            CharacterCreationAttributeProjection initial = state.Attributes.Single(a => a.AttributeId == attributeId);
            Assert.IsTrue(initial.IsEnabled);
            Assert.AreEqual(startingValue, initial.Minimum);
            Assert.AreEqual(startingValue, initial.Current);
            Assert.AreEqual(6, initial.Maximum);
            Assert.AreEqual(0m, state.SpecialPointBudget.Used);
            Assert.IsFalse(state.Attributes.Single(a => a.AttributeId == (attributeId == "MAG" ? "RES" : "MAG")).IsEnabled);
            Assert.IsFalse(state.Attributes.Single(a => a.AttributeId == "DEP").IsEnabled);
            CollectionAssert.Contains(initial.SourceAnchorIds.ToArray(), "priorities.xml#test:awakened");

            CharacterCreationAttributeAllocation[] allocations = [new(attributeId, 1, 1)];
            CharacterCreationAttributesPreview preview = service.Preview(new(state.Binding, allocations)).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
            CharacterCreationAttributeProjection raised = preview.Attributes.Single(a => a.AttributeId == attributeId);
            Assert.AreEqual(startingValue + 2, raised.Current);
            Assert.AreEqual((startingValue + 2) * 5, raised.KarmaCost);
            Assert.AreEqual(1m, preview.SpecialPointBudget.Used);
            Assert.AreEqual(0m, preview.NormalPointBudget.Used);
            Assert.AreEqual((decimal)raised.KarmaCost, preview.CreationKarmaBudget.Used);
            Assert.AreEqual(xml, store.Get(id).Value!.Document.Content);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new(preview.Binding, allocations, preview.PreviewDigest, true)).Outcome);
            Assert.AreEqual(xml, store.Get(id).Value!.Document.Content);
            CharacterCreationAttributesState reopened = Load(service, id);
            Assert.IsTrue(reopened.CanEdit, string.Join(",", reopened.Blockers));
            Assert.AreEqual(raised, reopened.Attributes.Single(a => a.AttributeId == attributeId)
                with { SourceAnchorIds = raised.SourceAnchorIds });
            Assert.AreEqual(1m, reopened.SpecialPointBudget.Used);
        }, authority, "awakened");
    }

    [TestMethod]
    [DataRow("Magician", "MAG")]
    [DataRow("Technomancer", "RES")]
    public void Awakened_allocations_share_edge_budget_and_reject_caps_overflow_and_disabled_attributes(
        string kind, string attributeId)
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            WorkspaceStoredDocument before = store.Get(id).Value!;
            (CharacterCreationAttributeAllocation[] Allocations, string Blocker)[] cases =
            [
                ([new(attributeId, 1, 0), new("EDG", 1, 0)], CharacterCreationAttributesBlockers.SpecialPointsExceeded),
                ([new(attributeId, 0, 2)], CharacterCreationAttributesBlockers.GlobalKarmaExceeded),
                ([new(attributeId, 0, 4)], CharacterCreationAttributesBlockers.AllocationInvalid),
                ([new(attributeId, int.MaxValue, int.MaxValue)], CharacterCreationAttributesBlockers.AllocationInvalid),
                ([new(attributeId == "MAG" ? "RES" : "MAG", 1, 0)], CharacterCreationAttributesBlockers.AttributeDisabled),
                ([new("ESS", 1, 0)], CharacterCreationAttributesBlockers.AttributeDisabled),
                ([new("DEP", 1, 0)], CharacterCreationAttributesBlockers.AttributeDisabled)
            ];
            foreach (var test in cases)
            {
                CharacterCreationAttributesPreview preview = service.Preview(new(state.Binding, test.Allocations)).Value!;
                Assert.IsFalse(preview.CanConfirm);
                CollectionAssert.Contains(preview.Blockers.ToList(), test.Blocker);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
                    service.Confirm(new(preview.Binding, test.Allocations, preview.PreviewDigest, true)).Outcome);
            }
            Assert.AreEqual(before.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        }, WithTalent(kind, attributeId, 3), "awakened");
    }

    [TestMethod]
    public void Source_grant_sets_magic_minimum_and_can_raise_its_metatype_maximum_without_free_spending()
    {
        WithConfirmedPrerequisite((_, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            CharacterCreationAttributeProjection magic = state.Attributes.Single(a => a.AttributeId == "MAG");
            Assert.AreEqual(7, magic.Minimum);
            Assert.AreEqual(7, magic.Maximum);
            Assert.AreEqual(7, magic.AugmentedMaximum);
            Assert.AreEqual(7, magic.Current);
            Assert.AreEqual(0m, state.SpecialPointBudget.Used);
            CharacterCreationAttributesPreview overCap = service.Preview(new(state.Binding, [new("MAG", 1, 0)])).Value!;
            Assert.IsFalse(overCap.CanConfirm);
            CollectionAssert.Contains(overCap.Blockers.ToList(), CharacterCreationAttributesBlockers.AllocationInvalid);
        }, WithTalent("Magician", "MAG", 7), "awakened");
    }

    private static CharacterCreationPrerequisiteAuthority WithTalent(
        string kind, string attributeId, int grant, string? additionalQuality = null)
    {
        CharacterCreationPrerequisiteAuthority authority = CharacterCreationPrerequisiteServiceTests.CreateAuthority(
            CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]);
        string[] qualities = additionalQuality is null ? [kind] : [kind, additionalQuality];
        string raw = new XElement("talent",
            new XElement("name", kind), new XElement("value", kind),
            new XElement("qualities", qualities.Select(quality => new XElement("quality", quality))),
            new XElement(attributeId == "MAG" ? "magic" : "resonance", grant)).ToString(SaveOptions.DisableFormatting);
        authority = authority with
        {
            Options = authority.Options.Select(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent && option.Rank == "E"
                ? option with
                {
                    TalentOptions = [new CharacterCreationPriorityTalentOptionProjection(
                        "awakened", kind, kind, 0, attributeId == "MAG" ? grant : null,
                        attributeId == "RES" ? grant : null, null, qualities,
                        CharacterCreationTalentGrantAuthorityDigest.ComputeRawTalentNode(raw), true, [],
                        ["priorities.xml#test:awakened"]) { RawTalentNode = raw }]
                }
                : option).ToArray(),
            AuthorityDigest = string.Empty
        };
        return authority with { AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority) };
    }

    [TestMethod]
    public void An_extra_talent_quality_cannot_silently_change_special_attribute_authority()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            Assert.IsFalse(state.CanEdit);
            CollectionAssert.Contains(state.Blockers.ToList(), CharacterCreationAttributesBlockers.SpecialAttributeAuthorityIncomplete);
            long revision = store.Get(id).Value!.ContentRevision;
            CharacterCreationAttributesPreview preview = service.Preview(new(state.Binding, [new("MAG", 1, 0)])).Value!;
            Assert.IsFalse(preview.CanConfirm);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
                service.Confirm(new(preview.Binding, [new("MAG", 1, 0)], preview.PreviewDigest, true)).Outcome);
            Assert.AreEqual(revision, store.Get(id).Value!.ContentRevision);
        }, WithTalent("Magician", "MAG", 3, "Unknown attribute effect"), "awakened");
    }

    [TestMethod]
    [DataRow("Magician", "MAG", 0)]
    [DataRow("Magician", "MAG", -1)]
    [DataRow("Magician", "RES", 3)]
    [DataRow("Technomancer", "MAG", 3)]
    [DataRow("Mundane", "MAG", 3)]
    [DataRow("Unresolved talent", "MAG", 3)]
    public void Inconsistent_or_unresolved_talent_grants_do_not_enable_attribute_mutation(
        string kind, string attributeId, int grant)
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            Assert.IsFalse(state.CanEdit);
            CollectionAssert.Contains(state.Blockers.ToList(), CharacterCreationAttributesBlockers.SpecialAttributeAuthorityIncomplete);
            Assert.IsFalse(state.Attributes.Single(a => a.AttributeId == "MAG").IsEnabled);
            Assert.IsFalse(state.Attributes.Single(a => a.AttributeId == "RES").IsEnabled);
            CharacterCreationAttributesPreview preview = service.Preview(new(state.Binding, [])).Value!;
            Assert.IsFalse(preview.CanConfirm);
            long revision = store.Get(id).Value!.ContentRevision;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
                service.Confirm(new(preview.Binding, [], preview.PreviewDigest, true)).Outcome);
            Assert.AreEqual(revision, store.Get(id).Value!.ContentRevision);
        }, WithTalent(kind, attributeId, grant), "awakened");
    }

    private static CharacterCreationAttributesState Load(
        ICharacterCreationAttributesService service,
        CharacterWorkspaceId id)
    {
        CharacterCreationFoundationResult<CharacterCreationAttributesState> result =
            service.Load(new CharacterCreationAttributesLoadRequest(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static void WithConfirmedPrerequisite(
        Action<FileWorkspaceStore, ICharacterCreationAttributesService, CharacterWorkspaceId, string> action,
        CharacterCreationPrerequisiteAuthority? suppliedAuthority = null,
        string talentSelectionId = "mundane",
        string method = CharacterCreationBuildMethods.Priority,
        Action<string>? stateDirectoryReady = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"chummer-attributes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            CharacterCreationPrerequisiteAuthority authority = suppliedAuthority
                ?? CharacterCreationPrerequisiteServiceTests.CreateAuthority(
                    method,
                    ["A", "B", "C", "D", "E"]);
            FileWorkspaceStore store = new(directory);
            CharacterWorkspaceId id = new("attributes-runner");
            string xml = "<character><name>Attributes Runner</name><alias>Priority</alias>"
                         + $"<buildmethod>{method}</buildmethod><created>false</created>"
                         + "<karma>25</karma><nuyen>0</nuyen></character>";
            Assert.IsTrue(store.CreateWorkspaceDocument(
                id,
                new WorkspaceDocument(xml, RulesetDefaults.Sr5)).Success);
            var sourceResolver = new StubSourceResolver(authority);
            var prerequisiteService = new CharacterCreationPrerequisiteService(
                store,
                new StubCharacterQueries(),
                sourceResolver);
            CharacterCreationPrerequisiteState prerequisiteState = prerequisiteService.Load(
                new CharacterCreationPrerequisiteLoadRequest(id)).Value!;
            IReadOnlyDictionary<string, string> ranks = CharacterCreationPrerequisiteServiceTests.Assign(
                "A", "E", "B", "C", "D");
            CharacterCreationPrerequisitePreview prerequisitePreview = prerequisiteService.Preview(
                new CharacterCreationPrerequisitePreviewRequest(prerequisiteState.Binding, ranks)
                {
                    HeritageSelectionId = "human",
                    TalentSelectionId = talentSelectionId
                }).Value!;
            CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> prerequisiteReceipt =
                prerequisiteService.Confirm(new CharacterCreationPrerequisiteConfirmRequest(
                    prerequisitePreview.Binding,
                    ranks,
                    prerequisitePreview.PreviewDigest,
                    ExplicitlyConfirmed: true)
                {
                    HeritageSelectionId = "human",
                    TalentSelectionId = talentSelectionId
                });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, prerequisiteReceipt.Outcome);

            var service = new CharacterCreationAttributesService(store, sourceResolver);
            stateDirectoryReady?.Invoke(directory);
            action(store, service, id, xml);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CharacterCreationPrerequisiteAuthority WithHalvedHuman(
        CharacterCreationPrerequisiteAuthority authority)
    {
        CharacterCreationPriorityOptionProjection[] options = authority.Options.Select(option =>
        {
            if (option.CategoryId != CharacterCreationPriorityCategoryIds.Heritage)
                return option;
            return option with
            {
                HeritageOptions = option.HeritageOptions.Select(child => child with
                {
                    HalvesNormalAttributePoints = true
                }).ToArray()
            };
        }).ToArray();
        authority = authority with { Options = options, AuthorityDigest = string.Empty };
        return authority with
        {
            AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority)
        };
    }

    private static string Digest(char value) => "sha256:" + new string(value, 64);

    internal sealed class StubSourceResolver : ICharacterSourceDataResolver
    {
        private readonly CharacterCreationPrerequisiteAuthority _authority;
        private readonly CharacterCreationSkillsAuthority? _skillsAuthority;
        private readonly CharacterCreationMagicResonanceAuthority? _magicResonanceAuthority;

        public StubSourceResolver(
            CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationSkillsAuthority? skillsAuthority = null,
            CharacterCreationMagicResonanceAuthority? magicResonanceAuthority = null)
        {
            _authority = authority;
            _skillsAuthority = skillsAuthority;
            _magicResonanceAuthority = magicResonanceAuthority;
        }

        public ICharacterSourceDataContext TryCreateContext(string characterXml) =>
            new StubSourceContext(_authority, _skillsAuthority, _magicResonanceAuthority);
    }

    internal sealed class StubSourceContext : ICharacterSourceDataContext
    {
        private readonly CharacterCreationPrerequisiteAuthority _authority;
        private readonly CharacterCreationSkillsAuthority? _skillsAuthority;
        private readonly CharacterCreationMagicResonanceAuthority? _magicResonanceAuthority;

        public StubSourceContext(
            CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationSkillsAuthority? skillsAuthority = null,
            CharacterCreationMagicResonanceAuthority? magicResonanceAuthority = null)
        {
            _authority = authority;
            _skillsAuthority = skillsAuthority;
            _magicResonanceAuthority = magicResonanceAuthority;
        }

        public bool TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority)
        {
            authority = _authority;
            return true;
        }

        public bool TryResolveCreationSkillsAuthority(out CharacterCreationSkillsAuthority authority)
        {
            authority = _skillsAuthority ?? CharacterCreationSkillsAuthority.Unavailable;
            return _skillsAuthority is not null;
        }

        public bool TryResolveCreationMagicResonanceAuthority(
            out CharacterCreationMagicResonanceAuthority authority)
        {
            authority = _magicResonanceAuthority ?? CharacterCreationMagicResonanceAuthority.Unavailable;
            return _magicResonanceAuthority is not null;
        }

        public bool TryResolveCyberwareGradeDeviceRating(
            string gradeName,
            string improvementSource,
            out int deviceRating)
        {
            deviceRating = 0;
            return false;
        }

        public bool TryResolveVehicleModBonuses(
            string sourceId,
            string name,
            out CharacterVehicleModSourceBonuses bonuses)
        {
            bonuses = CharacterVehicleModSourceBonuses.Empty;
            return false;
        }
    }

    internal sealed class StubCharacterQueries : ICharacterFileQueries
    {
        public CharacterFileSummary ParseSummary(CharacterDocument document)
        {
            XElement root = XDocument.Parse(document.Content).Root!;
            return new CharacterFileSummary(
                root.Element("name")?.Value ?? string.Empty,
                root.Element("alias")?.Value ?? string.Empty,
                string.Empty,
                root.Element("buildmethod")?.Value ?? string.Empty,
                string.Empty,
                string.Empty,
                25,
                0,
                false);
        }

        public CharacterValidationResult Validate(CharacterDocument document) => new(true, []);
    }
}
