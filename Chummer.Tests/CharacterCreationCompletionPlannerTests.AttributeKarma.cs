using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationCompletionPlannerTests
{
    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Whole_attribute_karma_spends_only_to_useful_targets_and_rebinds_the_complete_review(string method)
    {
        using var original = ReadyContext.Create(false, buildMethod: method, playerChosenAttributeIds: []);
        using var context = original.Restart();
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var before = JsonSerializer.Serialize(inputs);
        var files = Snapshot(context);
        var request = KarmaRequest(inputs, "BOD", 3, 25);
        var result = CharacterCreationCompletionPlanner.Plan(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var plan = result.Value!;
        Assert.AreEqual(2, plan.Selections.AttributeAllocations.Single(row => row.AttributeId == "BOD").KarmaLevels);
        Assert.AreEqual(25m, plan.Attributes!.Preview.CreationKarmaBudget.Used);
        Assert.AreEqual((int?)0, plan.Review.Balances.RemainingCreationKarma);
        Assert.IsTrue(plan.Attributes.HasChanges);
        Assert.IsFalse(plan.Skills!.HasChanges);
        Assert.IsFalse(plan.Magic!.HasChanges);
        Assert.IsFalse(plan.Purchases!.HasChanges);
        Assert.AreEqual(0, plan.Attributes.Preview.PlayerChosenAttributeIds!.Count);
        foreach (var row in inputs.Skills.Attributes.Attributes)
        {
            var after = plan.Selections.AttributeAllocations.Single(item => item.AttributeId == row.AttributeId);
            Assert.AreEqual(row.PriorityPointsSpent, after.PriorityPoints);
            if (row.AttributeId != "BOD") Assert.AreEqual(row.KarmaLevels, after.KarmaLevels);
        }
        Assert.AreEqual(plan.Attributes.Preview.PreviewDigest, plan.Skills.Preview.Attributes.PreviewDigest);
        Assert.AreEqual(plan.Attributes.Preview.PreviewDigest, plan.Magic.Preview.Attributes.PreviewDigest);
        Assert.AreEqual(plan.Attributes.Preview.PreviewDigest, plan.Purchases.Preview.Qualities.Attributes.PreviewDigest);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(plan.Selections.AttributeAllocations,
            plan.Purchases.Request.AttributeAllocations));
        Assert.AreEqual(CharacterCreationSkillsDigest.Compute(plan.Skills with { PlanDigest = string.Empty }), plan.Skills.PlanDigest);
        Assert.AreEqual(CharacterCreationMagicCompletionRules.ComputePlanDigest(plan.Magic), plan.Magic.PlanDigest);
        Assert.AreEqual(CharacterCreationPurchaseCompletionRules.ComputePlanDigest(plan.Purchases), plan.Purchases.PlanDigest);
        Assert.AreEqual(plan.Review.ReviewDigest, CharacterCreationCompletionReviewService.Review(inputs,
            new(inputs.SnapshotDigest, plan.Selections)).Value!.ReviewDigest);
        Assert.AreEqual(plan.PlanDigest, CharacterCreationCompletionPlanner.Plan(inputs, request).Value!.PlanDigest);
        Assert.AreEqual(before, JsonSerializer.Serialize(inputs));
        Assert.AreEqual(inputs.SnapshotDigest, CharacterCreationCompletionReviewServiceTests.LoadInputs(context).SnapshotDigest);
        AssertUnchanged(context, files);

        // Satisfied targets retain real remaining funds; an opt-in is not a
        // command to exhaust the wallet or to increase other permitted IDs.
        var modest = CharacterCreationCompletionPlanner.Plan(inputs, request with
        {
            Attributes = request.Attributes with { PermittedAttributeOrder = ["BOD", "AGI"], KarmaTargets = [new("BOD", 2)] }
        });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, modest.Outcome, string.Join(",", modest.Blockers));
        Assert.AreEqual((int?)15, modest.Value!.Review.Balances.RemainingCreationKarma);
        Assert.AreEqual(0, modest.Value.Selections.AttributeAllocations.Single(row => row.AttributeId == "AGI").KarmaLevels);
    }

    [TestMethod]
    [DataRow(25, 15, 1)]
    [DataRow(25, 16, 0)]
    [DataRow(10, 0, 1)]
    [DataRow(9, 0, 0)]
    [DataRow(0, 0, 0)]
    public void Whole_attribute_karma_respects_shared_reserves_and_the_source_priced_increment_cap(int cap, int reserve, int levels)
    {
        using var context = ReadyContext.Create(false, playerChosenAttributeIds: []);
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var request = KarmaRequest(inputs, "BOD", 3, cap) with { KarmaToReserve = reserve };
        var result = CharacterCreationCompletionPlanner.Plan(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        var plan = result.Value!;
        Assert.AreEqual(levels, plan.Selections.AttributeAllocations.Single(row => row.AttributeId == "BOD").KarmaLevels);
        Assert.AreEqual((int?)(levels == 0 ? 25 : 15), plan.Review.Balances.RemainingCreationKarma);
        Assert.AreEqual(0, plan.Review.Blockers.Count, "An unmet spending wish must retain a legal proposal.");
        CollectionAssert.Contains(plan.Blockers.ToArray(), CharacterCreationAttributeCompletionBlockers.KarmaTargetsUnfulfilled);
        CollectionAssert.Contains(plan.Blockers.ToArray(), reserve > 0
            ? CharacterCreationCompletionPlanBlockers.KarmaReserveRequired : CharacterCreationAttributeCompletionBlockers.KarmaSpendLimit);
        Assert.IsTrue(plan.Attributes!.Evaluations <= 1 + CharacterCreationCompletionPlanner.MaximumAttributeKarmaCandidateEvaluations);
    }

    [TestMethod]
    public void Whole_attribute_karma_is_off_for_legacy_and_attribute_only_callers_and_omitted_from_legacy_identity()
    {
        using var context = ReadyContext.Create(false, playerChosenAttributeIds: []);
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var legacy = RetainingRequest(inputs);
        Assert.IsFalse(JsonSerializer.SerializeToElement(legacy).TryGetProperty("AllocateRemainingAttributeKarma", out _));
        var legacyAttributes = JsonSerializer.SerializeToElement(legacy.Attributes);
        Assert.IsFalse(legacyAttributes.TryGetProperty("KarmaTargets", out _));
        Assert.IsFalse(legacyAttributes.TryGetProperty("MaximumAdditionalKarmaSpend", out _));
        Assert.AreEqual(JsonSerializer.Serialize(legacy), JsonSerializer.Serialize(legacy with
        {
            AllocateRemainingAttributeKarma = false,
            Attributes = legacy.Attributes with { KarmaTargets = null, MaximumAdditionalKarmaSpend = 0 }
        }));
        var disabled = KarmaRequest(inputs, "BOD", 3, 25) with { AllocateRemainingAttributeKarma = false };
        var whole = CharacterCreationCompletionPlanner.Plan(inputs, disabled);
        var attributes = CharacterCreationAttributeCompletionPlanner.Plan(inputs.Skills.Attributes, disabled.Attributes);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, whole.Outcome, string.Join(",", whole.Blockers));
        Assert.IsFalse(whole.Value!.Attributes!.HasChanges);
        Assert.AreEqual((int?)25, whole.Value.Review.Balances.RemainingCreationKarma);
        Assert.IsFalse(attributes.Value!.HasChanges);
        var baseline = CharacterCreationCompletionPlanner.Plan(inputs, legacy).Value!;
        var roundTrip = JsonSerializer.Deserialize<CharacterCreationCompletionPlanRequest>(JsonSerializer.Serialize(legacy))!;
        Assert.AreEqual(baseline.PlanDigest, CharacterCreationCompletionPlanner.Plan(inputs, roundTrip).Value!.PlanDigest);
        foreach (var goals in new IReadOnlyList<CharacterCreationAttributeCompletionTarget>?[] { null, [] })
        {
            var missing = CharacterCreationCompletionPlanner.Plan(inputs, disabled with
            { AllocateRemainingAttributeKarma = true, Attributes = disabled.Attributes with { KarmaTargets = goals } });
            Assert.AreEqual((int?)25, missing.Value!.Review.Balances.RemainingCreationKarma);
            CollectionAssert.Contains(missing.Blockers.ToArray(), CharacterCreationAttributeCompletionBlockers.KarmaTargetsRequired);
        }
    }

    [TestMethod]
    public void Whole_attribute_karma_preserves_original_manual_priority_karma_and_explicit_zero_choices()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var service = new CharacterCreationAttributesService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        CharacterCreationAttributeAllocation[] manual = [new("BOD", 2, 0), new("AGI", 0, 1), new("STR", 0, 0)];
        var preview = service.Preview(new(state.Binding, manual) { PlayerChosenAttributeIds = ["AGI", "BOD", "STR"] });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, preview.Outcome, string.Join(",", preview.Blockers));
        var saved = service.Confirm(new(state.Binding, manual, preview.Value!.PreviewDigest, true)
        { PlayerChosenAttributeIds = ["AGI", "BOD", "STR"] });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, saved.Outcome, string.Join(",", saved.Blockers));
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var files = Snapshot(context);
        foreach (string id in new[] { "BOD", "AGI", "STR" })
        {
            var denied = CharacterCreationCompletionPlanner.Plan(inputs, KarmaRequest(inputs, id, 4, 25));
            Assert.IsNull(denied.Value, "Explicit permission cannot override a recorded player choice in automatic mode.");
            CollectionAssert.Contains(denied.Blockers.ToArray(), CharacterCreationAttributeCompletionBlockers.AutomaticPermissionUnavailable);
        }
        var request = KarmaRequest(inputs, "REA", 2, 10);
        request = request with
        {
            Skills = request.Skills with
            { NativeLanguageSourceId = inputs.Skills.Authority.KnowledgeSkills.First(row => row.CanBeNativeLanguage).SourceSkillId },
            Magic = RankedMagic(inputs, MagicResonanceCompletionTests.CompleteSelections(inputs.Magic,
                inputs.Skills.Attributes.Attributes.Single(row => row.AttributeId == "MAG").UnaugmentedCurrent))
        };
        var allowed = CharacterCreationCompletionPlanner.Plan(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, allowed.Outcome, string.Join(",", allowed.Blockers));
        foreach (var original in manual)
            Assert.AreEqual(original, allowed.Value!.Selections.AttributeAllocations.Single(row => row.AttributeId == original.AttributeId));
        Assert.AreEqual((int?)5, allowed.Value!.Review.Balances.RemainingCreationKarma);
        CollectionAssert.AreEqual(new[] { "AGI", "BOD", "STR" }, allowed.Value.Attributes!.Preview.PlayerChosenAttributeIds!.ToArray());
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Whole_attribute_karma_rejects_unknown_intent_and_unpermitted_or_unavailable_targets()
    {
        using var unknown = ReadyContext.Create(false);
        var unknownInputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(unknown);
        var denied = CharacterCreationCompletionPlanner.Plan(unknownInputs, KarmaRequest(unknownInputs, "BOD", 2, 10));
        Assert.IsNull(denied.Value);
        CollectionAssert.Contains(denied.Blockers.ToArray(), CharacterCreationAttributeCompletionBlockers.AutomaticPermissionUnavailable);
        using var context = ReadyContext.Create(false, playerChosenAttributeIds: []);
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var request = KarmaRequest(inputs, "BOD", 2, 25);
        foreach (var invalid in new[]
        {
            request.Attributes with { KarmaTargets = [new("AGI", 2)] },
            request.Attributes with { KarmaTargets = [new("BOD", 2), new("BOD", 3)] },
            request.Attributes with { KarmaTargets = [new("BOD", -1)] },
            request.Attributes with { MaximumAdditionalKarmaSpend = -1 },
            request.Attributes with { KarmaTargets = Enumerable.Repeat(new CharacterCreationAttributeCompletionTarget("BOD", 2), 14).ToArray() }
        })
        {
            var result = CharacterCreationCompletionPlanner.Plan(inputs, request with { Attributes = invalid });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, result.Outcome);
            Assert.IsNull(result.Value);
        }
        foreach (string id in new[] { "RES", "unknown-source-id" })
        {
            var result = CharacterCreationCompletionPlanner.Plan(inputs, KarmaRequest(inputs, id, 2, 25));
            Assert.IsNull(result.Value);
            CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.InvalidChangePermissions);
        }
        var capped = CharacterCreationCompletionPlanner.Plan(inputs, request with
        {
            Attributes = request.Attributes with { NormalPointsToReserve = request.Attributes.NormalPointsToReserve - 5,
                Targets = [new("BOD", 6)], KarmaTargets = [new("BOD", 7)] }
        });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, capped.Outcome);
        Assert.AreEqual(6, capped.Value!.Attributes!.Preview.Attributes.Single(row => row.AttributeId == "BOD").UnaugmentedCurrent);
        Assert.AreEqual(0, capped.Value.Selections.AttributeAllocations.Single(row => row.AttributeId == "BOD").KarmaLevels);
        Assert.AreEqual((int?)25, capped.Value.Review.Balances.RemainingCreationKarma);
        CollectionAssert.Contains(capped.Blockers.ToArray(), CharacterCreationAttributeCompletionBlockers.KarmaTargetsUnfulfilled);
    }

    [TestMethod]
    public void Whole_attribute_karma_prices_proposed_quality_power_points_contacts_and_investment_once()
    {
        using var context = ReadyContext.Create(false, talentValue: "Mystic Adept", mysticPowerPoints: 0, playerChosenAttributeIds: []);
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var request = KarmaRequest(inputs, "BOD", 2, 25);
        var guts = inputs.Lifestyles.Qualities.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("0d0cb0fc-c79d-418e-9216-8a197369e52d") && row.Rating == 1);
        var medic = new CharacterCreationKarmaContactSelection(Guid.Parse("38cb0c5b-eb71-4596-b1fa-97f6f513d529"),
            new CharacterCreationContactIdentity("Medic", "Professional", "Seattle", "", "", "Human", "Other", "Adult",
                "Professional", "Nuyen", "", "", ""), 1, 1);
        var magic = MagicResonanceCompletionTests.CompleteSelections(inputs.Magic,
            inputs.Skills.Attributes.Attributes.Single(row => row.AttributeId == "MAG").UnaugmentedCurrent, mysticPowerPoints: 1);
        request = request with { QualityOptionIds = [guts.OptionId], Contacts = [medic],
            Magic = RankedMagic(inputs, magic) with { DesiredTotalMysticAdeptPowerPoints = 1,
                MaximumAdditionalPowerPointKarmaSpend = 5 } };
        var withoutKarma = CharacterCreationCompletionPlanner.Plan(inputs, request with { AllocateRemainingAttributeKarma = false });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, withoutKarma.Outcome, string.Join(",", withoutKarma.Blockers));
        var exact = CharacterCreationCompletionPlanner.Plan(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, exact.Outcome, string.Join(",", exact.Blockers));
        Assert.AreEqual(1, exact.Value!.Selections.AttributeAllocations.Single(row => row.AttributeId == "BOD").KarmaLevels);
        Assert.AreEqual((int?)5, exact.Value.Review.Balances.MysticAdeptPowerPointKarmaCost);
        Assert.AreEqual((int?)0, exact.Value.Review.Balances.ContactsKarmaCost);
        Assert.AreEqual((int?)0, exact.Value.Review.Balances.RemainingCreationKarma);
        Assert.IsTrue(exact.Value.Magic!.HasChanges, "The shared cost includes newly proposed PP and powers.");
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            withoutKarma.Value!.Selections.MagicSelections, exact.Value.Selections.MagicSelections));

        var investment = CharacterCreationCompletionPlanner.Plan(inputs, request with { TotalKarmaInvestment = 3 });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, investment.Outcome);
        Assert.AreEqual(0, investment.Value!.Selections.AttributeAllocations.Single(row => row.AttributeId == "BOD").KarmaLevels);
        Assert.AreEqual((int?)3, investment.Value.Review.Balances.ResourcesKarmaInvestment);
        Assert.AreEqual((int?)7, investment.Value.Review.Balances.RemainingCreationKarma);
        Assert.AreEqual(0, investment.Value.Review.Blockers.Count);
        var unpriced = CharacterCreationCompletionPlanner.Plan(inputs, request with { Contacts = [medic with { IsGroup = true }] });
        Assert.AreEqual(0, unpriced.Value!.Selections.AttributeAllocations.Single(row => row.AttributeId == "BOD").KarmaLevels);
        Assert.IsNull(unpriced.Value.Review.Balances.RemainingCreationKarma);
        CollectionAssert.Contains(unpriced.Blockers.ToArray(), CharacterCreationContactsCompletionSchemas.KarmaContributionUnavailable);

        // A source-valid attribute increase is still inadmissible while a
        // dependent contact budget is illegal. Never silently drop the contact.
        var contactOverdraw = CharacterCreationCompletionPlanner.Plan(inputs, request with { Contacts = [medic with { Connection = 2, Loyalty = 2 }] });
        Assert.AreEqual(0, contactOverdraw.Value!.Selections.AttributeAllocations.Single(row => row.AttributeId == "BOD").KarmaLevels);
        CollectionAssert.Contains(contactOverdraw.Blockers.ToArray(), CharacterCreationContactsBlockers.BudgetExceeded);
    }

    [TestMethod]
    public void Whole_attribute_karma_rejects_a_new_magic_gap_without_reselecting_powers()
    {
        using var context = ReadyContext.Create(false, talentValue: "Adept", talentRank: "D", playerChosenAttributeIds: []);
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var magic = inputs.Skills.Attributes.Attributes.Single(row => row.AttributeId == "MAG");
        var request = KarmaRequest(inputs, "MAG", magic.UnaugmentedCurrent + 1, 25);
        var result = CharacterCreationCompletionPlanner.Plan(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.AreEqual(0, result.Value!.Selections.AttributeAllocations.Single(row => row.AttributeId == "MAG").KarmaLevels);
        Assert.AreEqual((int?)25, result.Value.Review.Balances.RemainingCreationKarma);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.PowerBudgetIncomplete);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(inputs.Magic.Selections, result.Value.Selections.MagicSelections));
        Assert.AreEqual(0, result.Value.Review.Blockers.Count);

        var partial = CharacterCreationCompletionPlanner.Plan(inputs, request with { Attributes = request.Attributes with
        { PermittedAttributeOrder = ["MAG", "BOD"], KarmaTargets = [new("MAG", magic.UnaugmentedCurrent + 1), new("BOD", 2)] } });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, partial.Outcome);
        Assert.AreEqual(0, partial.Value!.Selections.AttributeAllocations.Single(row => row.AttributeId == "MAG").KarmaLevels);
        Assert.AreEqual(1, partial.Value.Selections.AttributeAllocations.Single(row => row.AttributeId == "BOD").KarmaLevels);
        Assert.AreEqual((int?)15, partial.Value.Review.Balances.RemainingCreationKarma);
        Assert.AreEqual(0, partial.Value.Review.Blockers.Count);
        CollectionAssert.Contains(partial.Blockers.ToArray(), CharacterCreationAttributeCompletionBlockers.KarmaTargetsUnfulfilled);
    }

    [TestMethod]
    public void Whole_attribute_karma_reports_new_knowledge_points_without_replanning_skills()
    {
        using var context = ReadyContext.Create(false, playerChosenAttributeIds: []);
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var result = CharacterCreationCompletionPlanner.Plan(inputs, KarmaRequest(inputs, "INT", 2, 10));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.AreEqual(1, result.Value!.Selections.AttributeAllocations.Single(row => row.AttributeId == "INT").KarmaLevels);
        Assert.AreEqual((int?)15, result.Value.Review.Balances.RemainingCreationKarma);
        Assert.AreEqual(0, result.Value.Review.Blockers.Count);
        Assert.IsFalse(result.Value.Skills!.HasChanges);
        CollectionAssert.Contains(result.Value.Skills.Blockers.ToArray(), CharacterCreationSkillCompletionBlockers.KnowledgePointsUnallocated);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationSkillCompletionBlockers.KnowledgePointsUnallocated);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(inputs.Skills.Allocations,
            result.Value.Selections.SkillAllocations));
    }

    [TestMethod]
    public void Whole_attribute_karma_rejects_mixed_observations_stale_sources_and_cancellation_without_writes()
    {
        using var context = ReadyContext.Create(false, playerChosenAttributeIds: []);
        using var other = ReadyContext.Create(false, playerChosenAttributeIds: []);
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var foreign = CharacterCreationCompletionReviewServiceTests.LoadInputs(other);
        var request = KarmaRequest(inputs, "BOD", 2, 10);
        var files = Snapshot(context);
        Assert.Throws<OperationCanceledException>(() => CharacterCreationCompletionPlanner.Plan(inputs, request, new CancellationToken(true)));
        var stale = CharacterCreationCompletionPlanner.Plan(inputs, request with
        { Attributes = request.Attributes with { SnapshotDigest = new string('0', 64) } });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, stale.Outcome);
        Assert.IsNull(stale.Value);
        var mixed = inputs with { Contacts = foreign.Contacts };
        mixed = mixed with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(mixed) };
        var denied = CharacterCreationCompletionPlanner.Plan(mixed, request with { InputsSnapshotDigest = mixed.SnapshotDigest });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, denied.Outcome);
        Assert.IsNull(denied.Value);
        Assert.AreEqual(inputs.SnapshotDigest, CharacterCreationCompletionReviewServiceTests.LoadInputs(context).SnapshotDigest);
        AssertUnchanged(context, files);
    }

    internal static CharacterCreationCompletionPlanRequest KarmaRequest(CharacterCreationCompletionReviewInputs inputs,
        string id, int target, int cap)
    {
        var request = RetainingRequest(inputs);
        return request with { AllocateRemainingAttributeKarma = true, Attributes = request.Attributes with
        { PermittedAttributeOrder = [id], Targets = [], KarmaTargets = [new(id, target)], MaximumAdditionalKarmaSpend = cap } };
    }
}
