using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationCompletionPlannerTests
{
    [TestMethod]
    public void Completion_skill_karma_knowledge_purchase_leaves_knowledge_points_and_native_language_unchanged()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var inputs = SkillKarmaInputs(context);
        var source = inputs.Skills.Authority.KnowledgeSkills.First(row => !row.CanBeNativeLanguage);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var before = CharacterCreationSkillsService.PreviewWithProposedAttributes(inputs.Skills,
            choices.AttributeAllocations, choices.SkillAllocations, choices.GroupAllocations).Value!;
        var quote = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills,
            choices.AttributeAllocations, choices.SkillAllocations, choices.GroupAllocations, [Purchase(source, 2)]);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, quote.Outcome, string.Join(",", quote.Blockers));
        var purchased = quote.Value!.Skills.Single(row => row.SourceSkillId == source.SourceSkillId);
        Assert.AreEqual(0, purchased.PointCost);
        Assert.AreEqual(2, purchased.KarmaLevels);
        Assert.AreEqual(3, purchased.KarmaCost);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(before.KnowledgeSkillPointBudget,
            quote.Value.KnowledgeSkillPointBudget));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(before.Skills.Single(row => row.IsNativeLanguage),
            quote.Value.Skills.Single(row => row.IsNativeLanguage)));
    }

    [TestMethod]
    public void Completion_skill_karma_shares_the_wallet_with_attributes_qualities_contacts_and_magic()
    {
        using var context = ReadyContext.Create(false, talentValue: "Mystic Adept", mysticPowerPoints: 1,
            qualityName: "Guts", includeSkillPurchase: true);
        var inputs = SkillKarmaInputs(context);
        var perception = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Perception");
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
        {
            // Match the source-backed contact completion fixture: CHA 3 funds
            // nine contact points; ordinary contacts do not buy overflow with Karma.
            AttributeAllocations = [new("CHA", 2, 0)], ChangeableAttributeIds = ["CHA"],
            QualityOptionIds = [inputs.Lifestyles.Qualities.Authority.Options.Single(row => row.Name == "Guts" && row.IsSelectable).OptionId],
            SkillKarmaPurchases = [Purchase(perception, 1)], TotalKarmaInvestment = 1,
            Contacts = [new(Guid.Parse("c8d69971-1c3c-47e8-972d-9b6a070c8f8d"),
                new CharacterCreationContactIdentity("Medic", "Professional", "Seattle", "", "", "Human",
                    "Other", "Adult", "Professional", "Nuyen", "", "", ""), 2, 2)]
        };
        var review = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        Assert.AreEqual(9, review.Value!.Contacts!.ContactBudget.Total);
        Assert.AreEqual(4, review.Value.Contacts.ContactBudget.Used);
        Assert.AreEqual(5, review.Value.Contacts.ContactBudget.Remaining);
        Assert.AreEqual((int?)0, review.Value.Contacts.AdditionalKarmaCost);
        Assert.AreEqual((int?)15, review.Value.Balances.KarmaAfterAttributesAndQualities);
        Assert.AreEqual((int?)5, review.Value.Balances.MysticAdeptPowerPointKarmaCost);
        Assert.AreEqual((int?)1, review.Value.Balances.ResourcesKarmaInvestment);
        Assert.AreEqual((int?)2, review.Value.Balances.SkillsKarmaCost);
        Assert.AreEqual((int?)7, review.Value.Balances.RemainingCreationKarma);
        Assert.AreEqual((int?)(review.Value.Lifestyles!.Gear.Resources.RemainingCreationKarma
            - review.Value.Contacts.AdditionalKarmaCost!.Value - 2), review.Value.Balances.RemainingCreationKarma);
        var contactOverspent = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest,
            choices with { AttributeAllocations = [] }));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, contactOverspent.Outcome,
            string.Join(",", contactOverspent.Blockers));
        CollectionAssert.Contains(contactOverspent.Blockers.ToArray(), CharacterCreationContactsBlockers.BudgetExceeded,
            string.Join(",", contactOverspent.Blockers));
        Assert.AreEqual(3, contactOverspent.Value!.Contacts!.ContactBudget.Total);
        Assert.AreEqual(4, contactOverspent.Value.Contacts.ContactBudget.Used);
        var attributes = choices.AttributeAllocations.Where(row => row.AttributeId != "BOD")
            .Append(new CharacterCreationAttributeAllocation("BOD", 0, 1)).ToArray();
        var overspent = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest,
            choices with { AttributeAllocations = attributes, ChangeableAttributeIds = ["CHA", "BOD"] }));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, overspent.Outcome);
        CollectionAssert.Contains(overspent.Blockers.ToArray(), CharacterCreationFinalizationBlockers.GlobalKarmaExceeded);
        Assert.AreEqual((int?)(-3), overspent.Value!.Balances.RemainingCreationKarma);
    }

    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Completion_skill_karma_exact_22_wallet_preserves_points_groups_and_shared_reserves(string method)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, buildMethod: method);
        var inputs = SkillKarmaInputs(context);
        var original = JsonSerializer.Serialize(inputs);
        var files = Snapshot(context);
        var request = RetainingRequest(inputs) with { SkillKarmaPurchases = Purchases22(inputs), TotalKarmaInvestment = 3 };
        var result = CharacterCreationCompletionPlanner.Plan(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var plan = result.Value!;
        Assert.AreEqual((int?)22, plan.Review.Balances.SkillsKarmaCost);
        Assert.AreEqual((int?)3, plan.Review.Balances.ResourcesKarmaInvestment);
        Assert.AreEqual((int?)0, plan.Review.Balances.RemainingCreationKarma);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(inputs.Skills.Allocations, plan.Selections.SkillAllocations));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(inputs.Skills.GroupAllocations, plan.Selections.GroupAllocations));
        var pistols = plan.Review.Skills!.Skills.Single(row => row.Name == "Pistols");
        Assert.AreEqual(4, pistols.Rating);
        Assert.AreEqual(2, pistols.PointCost);
        Assert.AreEqual(2, pistols.KarmaLevels);
        Assert.AreEqual(14, pistols.KarmaCost);
        Assert.AreEqual(22, plan.Review.Skills.Skills.Sum(row => row.KarmaCost));
        Assert.AreEqual(plan.Review.ReviewDigest, CharacterCreationCompletionReviewService.Review(inputs,
            new(inputs.SnapshotDigest, plan.Selections)).Value!.ReviewDigest);
        Assert.AreEqual(plan.PlanDigest, CharacterCreationCompletionPlanner.Plan(inputs, request).Value!.PlanDigest);
        var reserved = CharacterCreationCompletionPlanner.Plan(inputs, request with { TotalKarmaInvestment = 0, KarmaToReserve = 3 });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reserved.Outcome, string.Join(",", reserved.Blockers));
        Assert.AreEqual((int?)3, reserved.Value!.Review.Balances.RemainingCreationKarma);
        var shortReserve = CharacterCreationCompletionPlanner.Plan(inputs, request with { TotalKarmaInvestment = 0, KarmaToReserve = 4 });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, shortReserve.Outcome);
        CollectionAssert.Contains(shortReserve.Blockers.ToArray(), CharacterCreationCompletionPlanBlockers.KarmaReserveRequired);
        Assert.AreEqual(4, shortReserve.Value!.Selections.KarmaToReserve);
        var reserveReview = CharacterCreationCompletionReviewService.Review(inputs,
            new(inputs.SnapshotDigest, shortReserve.Value.Selections));
        CollectionAssert.Contains(reserveReview.Blockers.ToArray(), CharacterCreationCompletionPlanBlockers.KarmaReserveRequired);
        var overspent = CharacterCreationCompletionReviewService.Review(inputs,
            new(inputs.SnapshotDigest, plan.Selections with { TotalKarmaInvestment = 4 }));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, overspent.Outcome);
        Assert.AreEqual((int?)(-1), overspent.Value!.Balances.RemainingCreationKarma);
        CollectionAssert.Contains(overspent.Blockers.ToArray(), CharacterCreationFinalizationBlockers.GlobalKarmaExceeded);
        Assert.AreEqual(original, JsonSerializer.Serialize(inputs));
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Completion_skill_karma_specialization_is_source_priced_without_repaying_ratings_or_points()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var inputs = SkillKarmaInputs(context);
        var source = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var request = RetainingRequest(inputs) with
        { SkillKarmaPurchases = [new(source.SourceSkillId, source.Kind, source.SourceNodeDigest, 2, source.Specializations[0].OptionId)] };
        var result = CharacterCreationCompletionPlanner.Plan(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var row = result.Value!.Review.Skills!.Skills.Single(row => row.SourceSkillId == source.SourceSkillId);
        Assert.AreEqual(2, row.Rating);
        Assert.AreEqual(2, row.PointCost);
        Assert.AreEqual(0, row.KarmaLevels);
        Assert.IsTrue(row.KarmaSpecialization);
        Assert.AreEqual(inputs.Skills.KarmaPurchasePolicy!.KarmaSpecialization, row.KarmaCost);
        CollectionAssert.Contains(row.SourceAnchorIds.ToArray(), source.Specializations[0].SourceAnchorId);

        // A point-funded manual specialization remains point-funded when only
        // ratings are bought; it cannot be overwritten by a second purchase.
        var choices = result.Value.Selections;
        var manual = choices.SkillAllocations.Select(item => item.SourceSkillId == source.SourceSkillId
            ? item with { SpecializationOptionId = source.Specializations[0].OptionId } : item).ToArray();
        var baseline = CharacterCreationSkillsService.PreviewWithProposedAttributes(inputs.Skills,
            choices.AttributeAllocations, manual, choices.GroupAllocations).Value!;
        var quote = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills,
            choices.AttributeAllocations, manual, choices.GroupAllocations,
            [new(source.SourceSkillId, source.Kind, source.SourceNodeDigest, 3)]);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, quote.Outcome, string.Join(",", quote.Blockers));
        var increased = quote.Value!.Skills.Single(item => item.SourceSkillId == source.SourceSkillId);
        Assert.AreEqual(3, increased.PointCost);
        Assert.IsFalse(increased.KarmaSpecialization);
        Assert.AreEqual(baseline.Skills.Single(item => item.SourceSkillId == source.SourceSkillId).SpecializationOptionId,
            increased.SpecializationOptionId);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(baseline.SkillGroups, quote.Value.SkillGroups));
    }

    [TestMethod]
    public void Completion_skill_karma_rejects_source_exotic_talent_cap_group_and_duplicate_or_lowering_requests()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var inputs = SkillKarmaInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with { SkillKarmaPurchases = [] };
        var pistols = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var group = inputs.Skills.Authority.SkillGroups.Single(row => row.Name == "Athletics");
        var grouped = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Running"
            && group.MemberSkillSourceIds.Contains(row.SourceSkillId));
        var magical = inputs.Skills.Authority.ActiveSkills.First(row => row.Category == "Magical Active" && !row.IsExotic);
        var exotic = inputs.Skills.Authority.ActiveSkills.First(row => row.IsExotic);
        var invalid = new (string Case, CharacterCreationSkillKarmaPurchase Purchase, string Blocker)[]
        {
            ("lowering rating", Purchase(pistols, 1), CharacterCreationSkillKarmaPurchaseBlockers.PurchaseInvalid),
            ("rating cap", Purchase(pistols, inputs.Skills.Authority.MaxActiveSkillRatingCreate + 1), CharacterCreationSkillsBlockers.RatingInvalid),
            ("stale source", Purchase(pistols, 3) with { SourceNodeDigest = CharacterCreationSkillsDigest.ComputeUtf8("stale-source") }, CharacterCreationSkillKarmaPurchaseBlockers.SourceConflict),
            ("missing source", Purchase(pistols, 3) with { SourceSkillId = "disabled-or-missing-source" }, CharacterCreationSkillKarmaPurchaseBlockers.SourceConflict),
            ("exotic skill", Purchase(exotic, 1), CharacterCreationSkillsBlockers.ExoticSkillUnsupported),
            ("talent access", Purchase(magical, 1), CharacterCreationSkillsBlockers.TalentAccessRequired),
            ("broken group", Purchase(grouped, 2), CharacterCreationSkillsBlockers.GroupBroken),
            ("missing specialization", Purchase(pistols, 2) with { SpecializationOptionId = "missing-specialization" }, CharacterCreationSkillsBlockers.SpecializationInvalid)
        };
        foreach (var test in invalid)
        {
            var denied = CharacterCreationCompletionReviewService.Review(inputs,
                new(inputs.SnapshotDigest, choices with { SkillKarmaPurchases = [test.Purchase] }));
            string diagnostic = $"Case: {test.Case}; purchase: {JsonSerializer.Serialize(test.Purchase)}; "
                + $"expected blocker: {test.Blocker}; outcome: {denied.Outcome}; returned blockers: {string.Join(",", denied.Blockers)}";
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, denied.Outcome, diagnostic);
            CollectionAssert.Contains(denied.Blockers.ToArray(), test.Blocker, diagnostic);
            Assert.IsNull(denied.Value?.Balances.SkillsKarmaCost,
                $"{diagnostic}. A blocked purchase must not masquerade as an exact zero-cost quote.");
        }
        var duplicate = CharacterCreationCompletionReviewService.Review(inputs,
            new(inputs.SnapshotDigest, choices with { SkillKarmaPurchases = [Purchase(pistols, 3), Purchase(pistols, 3)] }));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, duplicate.Outcome,
            $"Case: duplicate purchase; purchase: {JsonSerializer.Serialize(Purchase(pistols, 3))}; "
            + $"outcome: {duplicate.Outcome}; returned blockers: {string.Join(",", duplicate.Blockers)}");
    }

    [TestMethod]
    public void Completion_skill_karma_preserves_talent_grants_and_legacy_zero_purchase_identity()
    {
        using var context = ReadyContext.Create(false, talentValue: "Magician", includeSkillPurchase: true);
        var inputs = SkillKarmaInputs(context);
        var request = RetainingRequest(inputs) with { SkillKarmaPurchases = Purchases22(inputs) };
        var result = CharacterCreationCompletionPlanner.Plan(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var original = CharacterCreationSkillsService.PreviewWithProposedAttributes(inputs.Skills,
            result.Value!.Selections.AttributeAllocations, inputs.Skills.Allocations, inputs.Skills.GroupAllocations).Value!;
        Assert.IsTrue(original.Skills.Any(row => row.GrantedRating > 0));
        foreach (var grant in original.Skills.Where(row => row.GrantedRating > 0))
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(grant,
                result.Value.Review.Skills!.Skills.Single(row => row.SourceSkillId == grant.SourceSkillId)));
        var legacy = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        Assert.IsNull(legacy.Skills.KarmaPurchasePolicy);
        Assert.IsFalse(JsonSerializer.SerializeToElement(legacy.Skills).TryGetProperty("KarmaPurchasePolicy", out _));
        var oldRequest = RetainingRequest(legacy);
        var oldPlan = CharacterCreationCompletionPlanner.Plan(legacy, oldRequest).Value!;
        Assert.IsFalse(JsonSerializer.SerializeToElement(oldRequest).TryGetProperty("SkillKarmaPurchases", out _));
        Assert.IsFalse(JsonSerializer.SerializeToElement(oldPlan.Selections).TryGetProperty("SkillKarmaPurchases", out _));
        Assert.AreEqual(oldPlan.PlanDigest, CharacterCreationCompletionPlanner.Plan(legacy,
            oldRequest with { SkillKarmaPurchases = null }).Value!.PlanDigest);
        foreach (var row in oldPlan.Review.Skills!.Skills)
        {
            var json = JsonSerializer.SerializeToElement(row);
            Assert.IsFalse(json.TryGetProperty("KarmaCost", out _));
            Assert.IsFalse(json.TryGetProperty("KarmaLevels", out _));
            Assert.IsFalse(json.TryGetProperty("KarmaSpecialization", out _));
        }
    }

    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Completion_skill_karma_knowledge_specialization_uses_source_price_and_preserves_point_provenance(string method)
    {
        string? selectedProfileId = null;
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, buildMethod: method, amendSettings: profile =>
        {
            selectedProfileId = profile.Element("id")!.Value;
            // Costs belong to the selected profile's karmacost container. Root-level
            // fields leave the actual source prices unchanged.
            profile.Element("karmacost")!.SetElementValue("karmaspecialization", "9");
            profile.Element("karmacost")!.SetElementValue("karmaknospecialization", "3");
        });
        var inputs = SkillKarmaInputs(context);
        var files = Snapshot(context);
        var source = inputs.Skills.Authority.KnowledgeSkills.Single(row => row.Name == "Administration");
        var specialization = source.Specializations.Single(row => row.Name == "Corporate");
        var activeSource = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var activeSpecialization = activeSource.Specializations[0];
        var policy = inputs.Skills.KarmaPurchasePolicy!;
        Assert.AreEqual(selectedProfileId, policy.SettingsProfileId);
        Assert.AreEqual(inputs.Skills.Authority.SettingsProfileId, policy.SettingsProfileId);
        CollectionAssert.Contains(policy.SourceAnchorIds.ToArray(),
            CharacterCreationBootstrapProfiles.SettingsSourceAnchor(selectedProfileId!));
        Assert.AreEqual(9, policy.KarmaSpecialization);
        Assert.AreEqual(3, policy.KarmaKnowledgeSpecialization);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var allocations = choices.SkillAllocations.Append(new CharacterCreationSkillAllocation(
            source.SourceSkillId, source.Kind, 2, null, false)).ToArray();
        var baseline = CharacterCreationSkillsService.PreviewWithProposedAttributes(inputs.Skills,
            choices.AttributeAllocations, allocations, choices.GroupAllocations);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, baseline.Outcome, string.Join(",", baseline.Blockers));
        var quote = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills,
            choices.AttributeAllocations, allocations, choices.GroupAllocations,
            [Purchase(source, 2) with { SpecializationOptionId = specialization.OptionId },
                Purchase(activeSource, 2) with { SpecializationOptionId = activeSpecialization.OptionId }]);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, quote.Outcome, string.Join(",", quote.Blockers));
        var purchased = quote.Value!.Skills.Single(row => row.SourceSkillId == source.SourceSkillId);
        Assert.AreEqual(2, purchased.Rating);
        Assert.AreEqual(2, purchased.PointCost);
        Assert.AreEqual(0, purchased.KarmaLevels);
        Assert.IsTrue(purchased.KarmaSpecialization);
        Assert.AreEqual(policy.KarmaKnowledgeSpecialization, purchased.KarmaCost);
        Assert.AreEqual(specialization.OptionId, purchased.SpecializationOptionId);
        Assert.AreEqual(specialization.Name, purchased.SpecializationName);
        CollectionAssert.Contains(purchased.SourceAnchorIds.ToArray(), specialization.SourceAnchorId);
        foreach (string anchor in source.SourceAnchorIds.Concat(policy.SourceAnchorIds))
            CollectionAssert.Contains(purchased.SourceAnchorIds.ToArray(), anchor);
        var activePurchased = quote.Value.Skills.Single(row => row.SourceSkillId == activeSource.SourceSkillId);
        Assert.AreEqual(2, activePurchased.Rating);
        Assert.AreEqual(2, activePurchased.PointCost);
        Assert.AreEqual(0, activePurchased.KarmaLevels);
        Assert.IsTrue(activePurchased.KarmaSpecialization);
        Assert.AreEqual(9, activePurchased.KarmaCost);
        Assert.AreEqual(activeSpecialization.OptionId, activePurchased.SpecializationOptionId);
        CollectionAssert.Contains(activePurchased.SourceAnchorIds.ToArray(), activeSpecialization.SourceAnchorId);
        Assert.AreEqual(12, quote.Value.Skills.Sum(row => row.KarmaCost));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            baseline.Value!.ActiveSkillPointBudget, quote.Value.ActiveSkillPointBudget));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            baseline.Value.KnowledgeSkillPointBudget, quote.Value.KnowledgeSkillPointBudget));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            baseline.Value.Skills.Single(row => row.IsNativeLanguage), quote.Value.Skills.Single(row => row.IsNativeLanguage)));
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Completion_skill_karma_above_a_talent_grant_charges_only_the_new_interval()
    {
        using var context = ReadyContext.Create(false, talentValue: "Magician", includeSkillPurchase: true);
        var inputs = SkillKarmaInputs(context);
        var files = Snapshot(context);
        var request = RetainingRequest(inputs);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var baseline = CharacterCreationSkillsService.PreviewWithProposedAttributes(inputs.Skills,
            choices.AttributeAllocations, choices.SkillAllocations, choices.GroupAllocations);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, baseline.Outcome, string.Join(",", baseline.Blockers));
        // Select an actual admitted free grant, not an arbitrary catalog skill.
        var grant = baseline.Value!.Skills.Where(row => row.Kind == CharacterCreationSkillKinds.Active
                && row.GrantedRating > 0 && row.PointCost == 0)
            .OrderBy(row => row.SourceSkillId, StringComparer.Ordinal).First();
        Assert.AreEqual(grant.GrantedRating, grant.Rating);
        var source = inputs.Skills.Authority.ActiveSkills.Single(row => row.SourceSkillId == grant.SourceSkillId);
        int target = grant.GrantedRating + 1;
        Assert.IsTrue(target <= inputs.Skills.Authority.MaxActiveSkillRatingCreate);
        var result = CharacterCreationCompletionPlanner.Plan(inputs,
            request with { SkillKarmaPurchases = [Purchase(source, target)] });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var skills = result.Value!.Review.Skills!;
        var purchased = skills.Skills.Single(row => row.SourceSkillId == grant.SourceSkillId);
        Assert.AreEqual(target, purchased.Rating);
        Assert.AreEqual(target, purchased.EffectiveRating);
        Assert.AreEqual(grant.GrantedRating, purchased.GrantedRating);
        Assert.AreEqual(0, purchased.PointCost);
        Assert.AreEqual(1, purchased.KarmaLevels);
        Assert.IsFalse(purchased.KarmaSpecialization);
        int expectedCost = target * inputs.Skills.KarmaPurchasePolicy!.KarmaImproveActiveSkill;
        Assert.AreEqual(expectedCost, purchased.KarmaCost);
        Assert.AreEqual((int?)expectedCost, result.Value.Review.Balances.SkillsKarmaCost);
        foreach (string anchor in grant.SourceAnchorIds)
            CollectionAssert.Contains(purchased.SourceAnchorIds.ToArray(), anchor);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            baseline.Value.ActiveSkillPointBudget, skills.ActiveSkillPointBudget));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            baseline.Value.SkillGroups, skills.SkillGroups));
        foreach (var untouched in baseline.Value.Skills.Where(row => row.SourceSkillId != grant.SourceSkillId))
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(untouched,
                skills.Skills.Single(row => row.SourceSkillId == untouched.SourceSkillId)), untouched.Name);
        AssertUnchanged(context, files);
    }

    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Completion_skill_karma_compensation_allows_unfunded_groups_and_rejects_funded_groups(string method)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, buildMethod: method,
            amendSettings: profile => profile.SetElementValue("compensateskillgroupkarmadifference", "True"));
        var inputs = SkillKarmaInputs(context);
        var original = JsonSerializer.Serialize(inputs);
        var files = Snapshot(context);
        Assert.IsTrue(inputs.Skills.KarmaPurchasePolicy!.CompensateSkillGroupKarmaDifference);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs)
            with { SkillKarmaPurchases = [] };
        var baseline = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, baseline.Outcome, string.Join(",", baseline.Blockers));
        var baselineSkills = baseline.Value!.Skills!;

        var unfunded = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Negotiation");
        Assert.IsNotNull(unfunded.SkillGroup);
        var unfundedGroup = inputs.Skills.Authority.SkillGroups.Single(group =>
            group.MemberSkillSourceIds.Contains(unfunded.SourceSkillId));
        Assert.IsFalse(baselineSkills.SkillGroups.Any(group => group.GroupId == unfundedGroup.GroupId));
        var allowed = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest,
            choices with { SkillKarmaPurchases = [Purchase(unfunded, 2)] }));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, allowed.Outcome, string.Join(",", allowed.Blockers));
        var purchased = allowed.Value!.Skills!.Skills.Single(row => row.SourceSkillId == unfunded.SourceSkillId);
        Assert.AreEqual(2, purchased.Rating);
        Assert.AreEqual(2, purchased.KarmaLevels);
        Assert.AreEqual(0, purchased.PointCost);
        Assert.AreEqual(6, purchased.KarmaCost);
        Assert.AreEqual((int?)6, allowed.Value.Balances.SkillsKarmaCost);
        Assert.AreEqual(baseline.Value.Balances.RemainingCreationKarma - 6,
            allowed.Value.Balances.RemainingCreationKarma);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            baselineSkills.ActiveSkillPointBudget, allowed.Value.Skills.ActiveSkillPointBudget));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            baselineSkills.SkillGroupPointBudget, allowed.Value.Skills.SkillGroupPointBudget));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            baselineSkills.SkillGroups, allowed.Value.Skills.SkillGroups));

        var funded = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Running");
        var fundedGroup = baselineSkills.SkillGroups.Single(group =>
            group.MemberSkillSourceIds.Contains(funded.SourceSkillId));
        Assert.AreEqual(1, fundedGroup.PointCost);
        var rejected = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest,
            choices with { SkillKarmaPurchases = [Purchase(funded, 2)] }));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, rejected.Outcome);
        CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationSkillKarmaPurchaseBlockers.GroupCompensationUnsupported);
        Assert.IsNull(rejected.Value?.Skills);
        Assert.IsNull(rejected.Value?.Balances.SkillsKarmaCost, "A funded group must not become a zero-cost purchase or refund.");

        var stale = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest,
            choices with { SkillKarmaPurchases = [Purchase(unfunded, 2) with
                { SourceNodeDigest = CharacterCreationSkillsDigest.ComputeUtf8("stale-unfunded-source") }] }));
        CollectionAssert.Contains(stale.Blockers.ToArray(), CharacterCreationSkillKarmaPurchaseBlockers.SourceConflict);
        Assert.AreEqual(original, JsonSerializer.Serialize(inputs));
        AssertUnchanged(context, files);
    }

    internal static CharacterCreationCompletionReviewInputs SkillKarmaInputs(ReadyContext context)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var result = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries)
            .LoadCompletionInputs(owners.Capture(), new(context.WorkspaceId) { IncludeKarmaPurchasePolicy = true });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        return result.Value!;
    }

    internal static CharacterCreationSkillKarmaPurchase[] Purchases22(CharacterCreationCompletionReviewInputs inputs)
        => [Purchase(inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols"), 4),
            Purchase(inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Perception"), 2),
            Purchase(inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Negotiation"), 1)];

    private static CharacterCreationSkillKarmaPurchase Purchase(CharacterCreationSkillCatalogEntry source, int rating)
        => new(source.SourceSkillId, source.Kind, source.SourceNodeDigest, rating);
}
