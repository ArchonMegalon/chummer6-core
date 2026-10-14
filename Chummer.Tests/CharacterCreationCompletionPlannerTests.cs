using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationCompletionPlannerTests
{
    [TestMethod]
    public void Whole_plan_retains_cold_saved_choices_and_uses_one_final_review_without_writing()
    {
        using var original = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        using var context = original.Restart();
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var request = RetainingRequest(inputs);
        var files = Snapshot(context);
        var result = CharacterCreationCompletionPlanner.Plan(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var plan = result.Value!;
        Assert.IsFalse(plan.Attributes!.HasChanges);
        Assert.IsFalse(plan.Skills!.HasChanges);
        Assert.IsFalse(plan.Magic!.HasChanges);
        Assert.IsFalse(plan.Purchases!.HasChanges);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            inputs.Magic.Selections, plan.Selections.MagicSelections));
        CollectionAssert.AreEquivalent(inputs.Skills.Allocations.ToArray(), plan.Selections.SkillAllocations.ToArray());
        CollectionAssert.AreEquivalent(inputs.Skills.GroupAllocations.ToArray(), plan.Selections.GroupAllocations.ToArray());
        CollectionAssert.AreEquivalent(inputs.Lifestyles.Qualities.SelectedOptionIds.ToArray(), plan.Selections.QualityOptionIds.ToArray());
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(inputs.Contacts.Contacts, plan.Selections.Contacts));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(inputs.Lifestyles.Lifestyles, plan.Selections.Lifestyles));
        CollectionAssert.AreEquivalent(inputs.Lifestyles.Gear.State.PendingDraft!.Lines
            .Select(row => new CharacterCreationGearSelection(row.OptionId, row.Quantity)).ToArray(), plan.Selections.Gear.ToArray());
        Assert.AreEqual(3, plan.Selections.TotalKarmaInvestment);
        Assert.AreEqual((int?)7, plan.Review.Balances.RemainingCreationKarma);
        Assert.AreEqual(3, plan.Magic.Request.KarmaToReserve);
        Assert.AreEqual(plan.PlanDigest, CharacterCreationCompletionPlanRules.ComputePlanDigest(plan));
        Assert.AreEqual(plan.PlanDigest, CharacterCreationCompletionPlanner.Plan(inputs, request).Value!.PlanDigest);
        var independent = CharacterCreationCompletionReviewService.Review(inputs,
            new(inputs.SnapshotDigest, plan.Selections));
        Assert.AreEqual(independent.Value!.ReviewDigest, plan.Review.ReviewDigest);
        // Passing this bounded proposal never asserts final document identity,
        // new implants/specializations, equipment dependencies or Career entry.
        Assert.IsNull(typeof(CharacterCreationCompletionPlan).GetProperty("CanApply"));
        Assert.IsNull(typeof(CharacterCreationCompletionPlan).GetProperty("CanConfirm"));
        Assert.IsNull(typeof(CharacterCreationCompletionPlan).GetProperty("IsComplete"));
        AssertUnchanged(context, files);
        Assert.AreEqual(inputs.SnapshotDigest, CharacterCreationCompletionReviewServiceTests.LoadInputs(context).SnapshotDigest);
    }

    [TestMethod]
    public void Whole_plan_composes_actual_unsaved_domains_and_prices_explicit_quality_resources_contacts_and_purchases_once()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        Assert.IsNull(inputs.Skills.Attributes.PendingDraft);
        Assert.IsNull(inputs.Lifestyles.Gear.Resources.PendingDraft);
        var request = RetainingRequest(inputs);
        var pistols = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var native = inputs.Skills.Authority.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        var completeMagic = MagicResonanceCompletionTests.CompleteSelections(inputs.Magic,
            inputs.Magic.SelectedTalent.Magic + 1, mysticPowerPoints: 1);
        var guts = inputs.Lifestyles.Qualities.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("0d0cb0fc-c79d-418e-9216-8a197369e52d") && row.Rating == 1);
        var supplies = inputs.Lifestyles.Gear.State.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("76aca45d-0d4f-45a3-a532-c47ac0b26b89") && row.IsSelectable);
        var home = CharacterCreationCompletionReviewServiceTests.LowLifestyle(inputs.Lifestyles.Authority);
        var contacts = new CharacterCreationKarmaContactSelection[]
        {
            new(Guid.Parse("f5010867-d1e8-4fca-9a2f-06f42e7c4581"),
                new CharacterCreationContactIdentity("Street Medic", "Professional", "Seattle", "", "",
                    "Human", "Other", "Adult", "Professional", "Nuyen", "", "", ""), 1, 1)
        };
        request = request with
        {
            Attributes = request.Attributes with { PermittedAttributeOrder = ["MAG"],
                SpecialPointsToReserve = request.Attributes.SpecialPointsToReserve - 1 },
            Skills = request.Skills with { PermittedTargetOrder = [new(CharacterCreationSkillKinds.Active, pistols.SourceSkillId)],
                ActivePointsToReserve = request.Skills.ActivePointsToReserve - 2, NativeLanguageSourceId = native.SourceSkillId },
            Magic = RankedMagic(inputs, completeMagic) with { DesiredTotalMysticAdeptPowerPoints = 1,
                MaximumAdditionalPowerPointKarmaSpend = 5 },
            QualityOptionIds = [guts.OptionId], Contacts = contacts, TotalKarmaInvestment = 3,
            Purchases = [new(Gear: new(supplies.OptionId, 1)), new(Lifestyle: home)],
            StartingLifestyleId = home.LifestyleId, MaximumAdditionalPurchaseSpend = 2100m
        };
        var files = Snapshot(context);
        var result = CharacterCreationCompletionPlanner.Plan(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var plan = result.Value!;
        Assert.IsTrue(plan.Attributes!.HasChanges);
        Assert.IsTrue(plan.Skills!.HasChanges);
        Assert.IsTrue(plan.Magic!.HasChanges);
        Assert.IsTrue(plan.Purchases!.HasChanges);
        Assert.AreEqual(1, plan.Selections.AttributeAllocations.Single(row => row.AttributeId == "MAG").PriorityPoints);
        Assert.AreEqual(2, plan.Selections.SkillAllocations.Single(row => row.SourceSkillId == pistols.SourceSkillId).Rating);
        Assert.IsTrue(plan.Selections.SkillAllocations.Single(row => row.SourceSkillId == native.SourceSkillId).IsNativeLanguage);
        Assert.IsTrue(inputs.Skills.Allocations.All(plan.Selections.SkillAllocations.Contains), "Retain actual Priority-granted skill choices.");
        Assert.AreEqual(1, plan.Selections.MagicSelections.MysticAdeptPowerPoints);
        Assert.AreEqual(0m, plan.Magic.Preview.AdeptPowerPointBudget.Remaining);
        Assert.AreEqual((int?)15, plan.Review.Balances.KarmaAfterAttributesAndQualities);
        Assert.AreEqual((int?)5, plan.Review.Balances.MysticAdeptPowerPointKarmaCost);
        Assert.AreEqual((int?)3, plan.Review.Balances.ResourcesKarmaInvestment);
        Assert.AreEqual((int?)0, plan.Review.Balances.ContactsKarmaCost);
        Assert.AreEqual((int?)7, plan.Review.Balances.RemainingCreationKarma);
        Assert.AreEqual((decimal?)53900m, plan.Review.Balances.RemainingNuyen);
        Assert.AreEqual(2100m, plan.Purchases.AdditionalSpend);
        Assert.AreEqual(inputs.Lifestyles.SnapshotDigest, plan.Purchases.Request.LifestylesSnapshotDigest);
        Assert.AreEqual(inputs.Magic.SnapshotDigest, plan.Purchases.Request.MagicSnapshotDigest);
        CollectionAssert.AreEqual(request.Attributes.PermittedAttributeOrder.ToArray(), plan.Selections.ChangeableAttributeIds.ToArray());
        CollectionAssert.AreEqual(request.Skills.PermittedTargetOrder.ToArray(), plan.Selections.ChangeableSkillTargets.ToArray());
        var digest = CharacterCreationFinalizationDigest.Compute(plan);
        contacts[0] = contacts[0] with { Connection = 99 };
        Assert.AreEqual(digest, CharacterCreationFinalizationDigest.Compute(plan), "The result must not retain caller-owned arrays.");
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Whole_plan_reserves_real_resource_investment_before_additional_power_points_and_never_prices_unknown_contacts_as_zero()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var request = RetainingRequest(inputs);
        var files = Snapshot(context);
        request = request with { KarmaToReserve = 3, Magic = request.Magic with
            { DesiredTotalMysticAdeptPowerPoints = 2, MaximumAdditionalPowerPointKarmaSpend = 5 } };
        var denied = CharacterCreationCompletionPlanner.Plan(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, denied.Outcome);
        Assert.AreEqual(6, denied.Value!.Magic!.Request.KarmaToReserve);
        Assert.AreEqual(1, denied.Value.Selections.MagicSelections.MysticAdeptPowerPoints);
        Assert.AreEqual((int?)7, denied.Value.Review.Balances.RemainingCreationKarma);
        CollectionAssert.Contains(denied.Blockers.ToArray(), CharacterCreationMagicCompletionBlockers.PowerPointSpendNotAllowed);

        // Two remaining Karma are enough only after the full original investment
        // is reserved. An unfilled added Power Point is reported separately;
        // this test does not pretend that a missing power candidate is complete.
        var allowed = CharacterCreationCompletionPlanner.Plan(inputs, request with { KarmaToReserve = 2 });
        Assert.IsNotNull(allowed.Value);
        Assert.AreEqual(5, allowed.Value!.Magic!.Request.KarmaToReserve);
        Assert.AreEqual(2, allowed.Value.Selections.MagicSelections.MysticAdeptPowerPoints);
        Assert.AreEqual((int?)2, allowed.Value.Review.Balances.RemainingCreationKarma);
        Assert.IsFalse(allowed.Value.Magic.IsMagicComplete);
        CollectionAssert.DoesNotContain(allowed.Blockers.ToArray(), CharacterCreationMagicCompletionBlockers.PowerPointSpendNotAllowed);

        var group = inputs.Contacts.Contacts.Single() with
        {
            ContactId = Guid.Parse("a974a52d-b37f-4acb-a52d-d41e9b92250a"), IsGroup = true,
            Identity = inputs.Contacts.Contacts.Single().Identity with { Name = "Explicit proposed group" }
        };
        var unpriced = CharacterCreationCompletionPlanner.Plan(inputs,
            RetainingRequest(inputs) with { Contacts = [.. inputs.Contacts.Contacts, group] });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, unpriced.Outcome);
        Assert.IsNotNull(unpriced.Value);
        Assert.IsNotNull(unpriced.Value!.Attributes);
        Assert.IsNotNull(unpriced.Value.Skills);
        Assert.IsNull(unpriced.Value.Magic);
        Assert.IsNull(unpriced.Value.Purchases);
        Assert.IsNull(unpriced.Value.Review.Balances.RemainingCreationKarma);
        CollectionAssert.Contains(unpriced.Blockers.ToArray(), CharacterCreationCompletionPlanBlockers.KarmaReserveUnavailable);
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Whole_plan_rejects_stale_nested_authority_manual_replacement_and_implicit_negative_qualities()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var request = RetainingRequest(inputs);
        var files = Snapshot(context);
        var stale = CharacterCreationCompletionPlanner.Plan(inputs,
            request with { Magic = request.Magic with { MagicSnapshotDigest = new string('0', 64) } });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, stale.Outcome);
        Assert.IsNull(stale.Value);
        CollectionAssert.Contains(stale.Blockers.ToArray(), CharacterCreationCompletionPlanBlockers.SnapshotConflict);
        var badEnvelope = CharacterCreationCompletionPlanner.Plan(inputs,
            request with { InputsSnapshotDigest = new string('0', 64) });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, badEnvelope.Outcome);
        Assert.IsNull(badEnvelope.Value);
        var laundering = CharacterCreationCompletionPlanner.Plan(inputs, request with
        {
            Magic = request.Magic with { ManualSelections = request.Magic.ManualSelections with { MysticAdeptPowerPoints = 2 } }
        });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, laundering.Outcome);
        Assert.IsNull(laundering.Value);
        CollectionAssert.Contains(laundering.Blockers.ToArray(), CharacterCreationCompletionPlanBlockers.ManualMagicMismatch);
        foreach (var removal in new[] { request with { QualityOptionIds = [] }, request with { Contacts = [] },
                     request with { TotalKarmaInvestment = 0 } })
        {
            var result = CharacterCreationCompletionPlanner.Plan(inputs, removal);
            Assert.IsNull(result.Value);
            CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict);
        }
        var negative = inputs.Lifestyles.Qualities.Authority.Options.First(row =>
            row.Type == CharacterCreationQualityType.Negative && row.IsSelectable
            && !inputs.Lifestyles.Qualities.SelectedOptionIds.Contains(row.OptionId));
        var negativeRequest = request with { QualityOptionIds = [.. request.QualityOptionIds, negative.OptionId] };
        var implicitNegative = CharacterCreationCompletionPlanner.Plan(inputs, negativeRequest);
        Assert.IsNull(implicitNegative.Value);
        CollectionAssert.Contains(implicitNegative.Blockers.ToArray(), CharacterCreationCompletionPlanBlockers.NegativeQualityPermissionRequired);
        var explicitNegative = CharacterCreationCompletionPlanner.Plan(inputs,
            negativeRequest with { PermitNegativeQualityAdditions = true });
        CollectionAssert.DoesNotContain(explicitNegative.Blockers.ToArray(), CharacterCreationCompletionPlanBlockers.NegativeQualityPermissionRequired);
        bool canceled = false;
        try { CharacterCreationCompletionPlanner.Plan(inputs, request, new CancellationToken(true)); }
        catch (OperationCanceledException) { canceled = true; }
        Assert.IsTrue(canceled);
        AssertUnchanged(context, files);
    }

    private static CharacterCreationCompletionPlanRequest RetainingRequest(CharacterCreationCompletionReviewInputs inputs)
    {
        var attributes = inputs.Skills.Attributes.Attributes.Select(row => new CharacterCreationAttributeAllocation(
            row.AttributeId, row.PriorityPointsSpent, row.KarmaLevels)).ToArray();
        var skills = CharacterCreationSkillCompletionPlanner.Plan(inputs.Skills, attributes,
            new(inputs.Skills.Binding, inputs.Skills.Attributes.SnapshotDigest, inputs.Skills.SnapshotDigest, []));
        Assert.IsNotNull(skills.Value, string.Join(",", skills.Blockers));
        var preview = skills.Value!.Preview;
        return new(inputs.SnapshotDigest,
            new(inputs.Skills.Attributes.Binding, inputs.Skills.Attributes.SnapshotDigest, [],
                (int)inputs.Skills.Attributes.NormalPointBudget.Remaining, (int)inputs.Skills.Attributes.SpecialPointBudget.Remaining),
            new(inputs.Skills.Binding, inputs.Skills.Attributes.SnapshotDigest, inputs.Skills.SnapshotDigest, [],
                (int)preview.ActiveSkillPointBudget.Remaining, (int)preview.SkillGroupPointBudget.Remaining,
                (int)preview.KnowledgeSkillPointBudget.Remaining),
            RankedMagic(inputs, inputs.Magic.Selections), inputs.Lifestyles.Qualities.SelectedOptionIds,
            inputs.Contacts.Contacts, inputs.Lifestyles.Gear.Resources.PendingDraft?.KarmaInvestment ?? 0, [],
            inputs.Lifestyles.Lifestyles.FirstOrDefault()?.LifestyleId);
    }

    private static CharacterCreationMagicCompletionRequest RankedMagic(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationMagicResonanceSelections desired)
        => new(inputs.Magic.SnapshotDigest, inputs.Lifestyles.Qualities.SnapshotDigest, inputs.Magic.Selections,
            desired.Tradition is null ? [] : [desired.Tradition], desired.Stream is null ? [] : [desired.Stream],
            desired.Spells, desired.ComplexForms, desired.AdeptPowers.Select(row => row.Identity).ToArray());

    private static Dictionary<string, byte[]> Snapshot(ReadyContext context)
        => Directory.EnumerateFiles(context.Directory, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(context.Directory, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertUnchanged(ReadyContext context, Dictionary<string, byte[]> before)
    {
        var after = Snapshot(context);
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var entry in before) CollectionAssert.AreEqual(entry.Value, after[entry.Key], entry.Key);
    }
}
