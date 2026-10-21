using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationCompletionPlannerTests
{
    [TestMethod]
    public void Skill_karma_group_purchase_modifiers_do_not_modify_active_compensation_rates()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: ConfigureCompensation);
        var inputs = SkillKarmaInputs(context);
        var group = inputs.Skills.Authority.SkillGroups.Single(row => row.Name == "Influence");
        var members = CompensationMembers(inputs);
        var target = new CharacterCreationSkillKarmaTarget("group", group.GroupId, group.GroupDigest);
        CharacterCreationSkillKarmaEffect Effect(string type, decimal value, int position,
            IReadOnlyList<CharacterCreationSkillKarmaTarget> targets) => new(type, value, 0, 0, "create",
            CollegeEducationId, CharacterCreationSkillsDigest.Compute("group-quality"), "group-option", 1,
            "bonus", position, targets, ["source:group-quality"]);
        var effects = new[] { Effect("SkillGroupKarmaCostMultiplier", 50, 0, [target]),
            Effect("SkillGroupCategoryKarmaCost", -1, 1, members.Select(CharacterCreationLegacySourceProjector.Target).ToArray()) };
        Assert.IsTrue(CharacterCreationSkillCostRules.TryGroup(1, 3, 4, 4, out int raw));
        Assert.IsTrue(CharacterCreationKarmaSkillsRules.TryPriceGroup(group, members, 1, 3, raw, effects, out int groupCost));
        Assert.AreEqual(8, groupCost); // 20 * .5 - 2
        Assert.IsTrue(CharacterCreationKarmaSkillsRules.TryPriceActive(members[0], 0, 2,
            inputs.Skills.KarmaPurchasePolicy!, true, 2, 3, 0, effects, out var active));
        Assert.AreEqual(-7, active!.CompensationFloor);
        Assert.AreEqual(1, active.KarmaCost); // Unmodified group base rates still determine compensation.
        Assert.IsTrue(CharacterCreationKarmaSkillsRules.TryPriceGroup(group, members, 1, 3, raw,
            effects.Select(effect => effect with { Minimum = 2 }).ToArray(), out groupCost));
        Assert.AreEqual(20, groupCost); // Group threshold uses the lower rating.
    }

    [TestMethod]
    public void Skill_karma_modifiers_preserve_native_minimum_rounding_and_compensation_floor_order()
    {
        var active = new CharacterCreationSkillKarmaTarget("active", "source-active", CharacterCreationSkillsDigest.Compute("active"));
        var knowledge = new CharacterCreationSkillKarmaTarget("knowledge", "source-knowledge", CharacterCreationSkillsDigest.Compute("knowledge"));
        CharacterCreationSkillKarmaEffect Effect(string type, decimal value, int position, int minimum = 0) => new(
            type, value, minimum, 0, "create", CollegeEducationId, CharacterCreationSkillsDigest.Compute("quality"),
            "quality-option", 1, "bonus", position, [active, knowledge], ["source:quality"]);
        var effects = new[] { Effect("SkillCategoryKarmaCostMultiplier", 50, 0),
            Effect("SkillCategorySpecializationKarmaCostMultiplier", 50, 1) };
        Assert.IsTrue(CharacterCreationSkillKarmaModifierRules.TryPrice(active, 1, 3, 5, 0, 1, 7, effects, out var a));
        Assert.IsTrue(CharacterCreationSkillKarmaModifierRules.TryPrice(knowledge, 1, 3, 5, 0, 1, 7, effects, out var k));
        Assert.AreEqual(7, a!.KarmaCost); // ceil(2.5) + ceil(3.5)
        Assert.AreEqual(6, k!.KarmaCost); // ceil(2.5 + 3.5)
        Assert.IsFalse(CharacterCreationSkillKarmaModifierRules.TryPrice(active with
            { SourceNodeDigest = CharacterCreationSkillsDigest.Compute("changed-active") }, 1, 3, 5, 0, 1, 7, effects, out _));
        effects[0] = effects[0] with { Minimum = 2 };
        Assert.IsTrue(CharacterCreationSkillKarmaModifierRules.TryPrice(active, 1, 3, 5, 0, 1, 7, effects, out a));
        Assert.IsTrue(CharacterCreationSkillKarmaModifierRules.TryPrice(knowledge, 1, 3, 5, 0, 1, 7, effects, out k));
        Assert.AreEqual(9, a!.KarmaCost);
        Assert.AreEqual(6, k!.KarmaCost);
        Assert.IsTrue(CharacterCreationSkillKarmaModifierRules.TryPrice(active, 1, 2, 5, 0, 0, 7,
            [Effect("SkillCategoryKarmaCost", -0.2m, 0)], out a));
        Assert.AreEqual(4, a!.KarmaCost); // Integral subtotal + floor(-.2), not ceil(4.8).
        Assert.IsTrue(CharacterCreationSkillKarmaModifierRules.TryPrice(active, 1, 2, -4, -10, 1, 20,
            [Effect("SkillCategoryKarmaCostMultiplier", 300, 0)], out a));
        Assert.AreEqual(-10, a!.IntervalCost);
        Assert.AreEqual(10, a.KarmaCost); // Interval floor first, specialization second, final clamp last.
    }

    [TestMethod]
    public void Skill_karma_modifiers_compensation_manual_consumer_completion_and_cold_replay_agree()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: ConfigureCompensation,
            prepareSource: root =>
            {
                string path = Path.Combine(root, "data", "qualities.xml");
                var xml = System.Xml.Linq.XDocument.Load(path);
                var quality = xml.Root!.Element("qualities")!.Elements("quality")
                    .Single(row => row.Element("id")!.Value == CollegeEducationId.ToString("D"));
                quality.Element("bonus")!.ReplaceNodes(
                    System.Xml.Linq.XElement.Parse("<skillcategorykarmacostmultiplier><name>Social Active</name><val>50</val></skillcategorykarmacostmultiplier>"),
                    System.Xml.Linq.XElement.Parse("<skillcategoryspecializationkarmacostmultiplier><name>Social Active</name><val>50</val></skillcategoryspecializationkarmacostmultiplier>"));
                xml.Save(path);
            });
        var inputs = KnowledgeInputs(context);
        var members = CompensationMembers(inputs);
        var payer = members.Single(row => row.SourceSkillId == inputs.Skills.KarmaCompensationSourceOrder!
            .First(id => members.Any(member => member.SourceSkillId == id)));
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
        {
            QualityOptionIds = [inputs.Lifestyles.Qualities.Authority.Options.Single(row => row.SourceId == CollegeEducationId).OptionId],
            SkillKarmaPurchases = members.Select(row => Purchase(row, 2) with
                { SpecializationOptionId = row == payer ? row.Specializations[0].OptionId : null }).ToArray()
        };
        var request = KnowledgeRequest(inputs, choices);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var qualities = CharacterCreationQualitiesService.PreviewWithProposedAttributes(inputs.Lifestyles.Qualities,
            choices.AttributeAllocations, choices.QualityOptionIds).Value!.Qualities.Selections;
        Assert.IsTrue(CharacterCreationLegacySourceProjector.TrySkillKarmaModifiers(qualities, inputs.Skills.Authority, out var effects));
        foreach (var source in members)
        {
            Assert.IsTrue(CharacterCreationKarmaSkillsRules.TryPriceActive(source, 0, 2, inputs.Skills.KarmaPurchasePolicy!,
                source == payer, 2, 3, source == payer ? 1 : 0, effects, out var manual));
            var row = review.Value!.Skills!.Skills.Single(skill => skill.SourceSkillId == source.SourceSkillId);
            Assert.AreEqual(source == payer ? 6 : 4, row.KarmaCost);
            Assert.AreEqual(manual!.KarmaCost, row.KarmaCost);
            Assert.AreEqual(source == payer ? -7 : 0, row.SkillKarmaPrice!.CompensationFloor);
        }
        Assert.AreEqual((int?)14, review.Value!.Balances.SkillsKarmaCost);
        Assert.AreEqual((int?)(review.Value.Lifestyles!.Gear.Resources.RemainingCreationKarma
            - review.Value.Contacts!.AdditionalKarmaCost!.Value - 14), review.Value.Balances.RemainingCreationKarma);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        var cash = service.LoadStartingCash(stamp, context.WorkspaceId, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cash.Outcome, string.Join(",", cash.Blockers));
        var final = service.Review(stamp, context.WorkspaceId, request, new(cash.Value!.AuthorityDigest, cash.Value.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, final.Outcome, string.Join(",", final.Blockers));
        Assert.AreEqual(review.Value.Balances.RemainingCreationKarma, (int?)final.Value!.Projection.KarmaBeforeCarryover);
        Assert.AreEqual(checked((int)Math.Min(final.Value.Projection.KarmaBeforeCarryover,
            final.Value.Projection.CarryoverPolicy.MaximumKarma)), final.Value.Projection.KarmaRemaining);
        var command = new CharacterCreationCompletionApplyRequest(final.Value!.Projection.Binding, final.Value.Proposal,
            final.Value.Projection.ReviewDigest, final.Value.Projection.StartingCash.Choice,
            final.Value.Projection.ProjectionDigest, "skill-modifier-compensation", true);
        var applied = service.Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        var cold = new Chummer.Infrastructure.Workspaces.FileWorkspaceStore(context.Directory);
        var archive = cold.Get(context.WorkspaceId).Value!.Document.AuxiliaryState.CharacterCreationFinalizationArchive!;
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive.CompletionAuthority!.SelectedReplay,
            out var bootstrap, out var witness));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(before, bootstrap, witness, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(review.Value!.Balances), JsonSerializer.Serialize(replay.Value!.Balances));
    }

    [TestMethod]
    public void Skill_karma_completion_refuses_unknown_category_without_an_exact_wallet()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: ConfigureCompensation,
            prepareSource: root =>
            {
                var path = Path.Combine(root, "data", "qualities.xml");
                var xml = System.Xml.Linq.XDocument.Load(path);
                var quality = xml.Root!.Element("qualities")!.Elements("quality")
                    .Single(row => row.Element("id")!.Value == CollegeEducationId.ToString("D"));
                quality.Element("bonus")!.ReplaceNodes(System.Xml.Linq.XElement.Parse(
                    "<skillcategorykarmacostmultiplier><name>Social</name><val>50</val></skillcategorykarmacostmultiplier>"));
                xml.Save(path);
            });
        var inputs = KnowledgeInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
        {
            QualityOptionIds = [inputs.Lifestyles.Qualities.Authority.Options.Single(row => row.SourceId == CollegeEducationId).OptionId],
            SkillKarmaPurchases = CompensationMembers(inputs).Select(row => Purchase(row, 2)).ToArray()
        };
        var review = CharacterCreationCompletionReviewService.Review(inputs, KnowledgeRequest(inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, review.Outcome);
        CollectionAssert.Contains(review.Blockers.ToArray(), CharacterCreationSkillKarmaPurchaseBlockers.ModifiersUnsupported);
        Assert.IsNotNull(review.Value!.UpstreamBudget);
        Assert.AreEqual(0, review.Value.UpstreamBudget.Qualities.Blockers.Count);
        Assert.IsNull(review.Value.Skills);
        Assert.IsNull(review.Value.Balances.SkillsKarmaCost);
        Assert.IsNull(review.Value.Balances.RemainingCreationKarma);
    }

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
            ("invalid group member rating", Purchase(grouped, 0), CharacterCreationCompletionReviewSchemas.RequestInvalid),
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
        Assert.IsNull(legacy.Skills.KarmaCompensationSourceOrder);
        Assert.IsFalse(JsonSerializer.SerializeToElement(legacy.Skills).TryGetProperty("KarmaCompensationSourceOrder", out _));
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
    public void Completion_skill_karma_compensation_preserves_funded_group_base_and_prices_personal_ranks(string method)
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
        var fundedReview = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest,
            choices with { SkillKarmaPurchases = [Purchase(funded, 2)] }));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, fundedReview.Outcome, string.Join(",", fundedReview.Blockers));
        var raised = fundedReview.Value!.Skills!.Skills.Single(row => row.SourceSkillId == funded.SourceSkillId);
        Assert.AreEqual(1, raised.GroupRating);
        Assert.AreEqual(1, raised.KarmaLevels);
        Assert.AreEqual(0, raised.PointCost);
        Assert.AreEqual(4, raised.KarmaCost);
        var retained = fundedReview.Value.Skills.SkillGroups.Single(row => row.GroupId == fundedGroup.GroupId);
        Assert.AreEqual(fundedGroup.Rating, retained.Rating);
        Assert.AreEqual(fundedGroup.PointCost, retained.PointCost);
        Assert.AreEqual(true, retained.IsBroken);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            baselineSkills.SkillGroupPointBudget, fundedReview.Value.Skills.SkillGroupPointBudget));

        var stale = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest,
            choices with { SkillKarmaPurchases = [Purchase(unfunded, 2) with
                { SourceNodeDigest = CharacterCreationSkillsDigest.ComputeUtf8("stale-unfunded-source") }] }));
        CollectionAssert.Contains(stale.Blockers.ToArray(), CharacterCreationSkillKarmaPurchaseBlockers.SourceConflict);
        Assert.AreEqual(original, JsonSerializer.Serialize(inputs));
        AssertUnchanged(context, files);
    }

    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Completion_skill_karma_compensation_uses_configured_rates_once_in_review_Apply_and_cold_replay(string method)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, buildMethod: method,
            amendSettings: ConfigureCompensation);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var loaded = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries)
            .LoadCompletionInputsWithConsumerPolicy(owners.Capture(), new(context.WorkspaceId) { IncludeKarmaPurchasePolicy = true });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        var inputs = loaded.Value!;
        Assert.AreEqual(CharacterCreationCompletionConsumerPolicy.SchemaV4, inputs.EffectivePolicy!.Consumers!.Schema);
        var coverage = inputs.EffectivePolicy.Consumers.Coverage;
        var compensation = coverage.Single(row => row.Operation == CharacterCreationConsumerPolicyOperation.SkillGroupKarmaCompensation);
        Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Admitted, compensation.Disposition);
        Assert.AreEqual(CharacterCreationConsumerPolicyReason.MatchedConsumerInputs, compensation.Reason);
        foreach (var operation in new[] { CharacterCreationConsumerPolicyOperation.SkillKarmaModifiers,
            CharacterCreationConsumerPolicyOperation.MysticAdeptSeparateMagic, CharacterCreationConsumerPolicyOperation.LifestyleCostEffects })
            Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Unsupported, coverage.Single(row => row.Operation == operation).Disposition);
        var members = CompensationMembers(inputs);
        var source = context.Resolver.TryCreateContext(context.Store.Get(context.WorkspaceId).Value!.Document.Content)!;
        Assert.IsTrue(source.TryResolveCreationSkillsCatalog(out var catalog));
        CollectionAssert.AreEqual(catalog!.ActiveSkillSourceOrder.ToArray(), inputs.Skills.KarmaCompensationSourceOrder!.ToArray());
        Assert.AreEqual(3, inputs.Skills.KarmaPurchasePolicy!.KarmaImproveActiveSkill);
        Assert.AreEqual(4, inputs.Skills.KarmaPurchasePolicy.KarmaNewSkillGroup);
        Assert.AreEqual(4, inputs.Skills.KarmaPurchasePolicy.KarmaImproveSkillGroup);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
        { SkillKarmaPurchases = members.Select(row => Purchase(row, 2)).ToArray() };
        var request = new CharacterCreationCompletionReviewRequest(inputs.SnapshotDigest, choices)
        { EffectivePolicyDigest = inputs.EffectivePolicy!.PolicyDigest };
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        // RangeCost's lower=0 branch uses (ceiling-1)*ceiling/2 for
        // compensation: each skill costs 8; the first gets 8 - 3*5 = -7.
        Assert.AreEqual((int?)17, reviewed.Value!.Balances.SkillsKarmaCost);
        var withoutPurchases = CharacterCreationCompletionReviewService.Review(inputs, request with
        { Selections = choices with { SkillKarmaPurchases = [] } });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, withoutPurchases.Outcome, string.Join(",", withoutPurchases.Blockers));
        Assert.AreEqual(withoutPurchases.Value!.Balances.RemainingCreationKarma - 17, reviewed.Value.Balances.RemainingCreationKarma);
        var payer = inputs.Skills.KarmaCompensationSourceOrder.First(id => members.Any(row => row.SourceSkillId == id));
        foreach (var member in members)
        {
            var row = reviewed.Value.Skills!.Skills.Single(item => item.SourceSkillId == member.SourceSkillId);
            Assert.AreEqual(member.SourceSkillId == payer ? 1 : 8, row.KarmaCost);
            Assert.AreEqual(2, row.KarmaLevels);
            Assert.AreEqual(0, row.PointCost);
            foreach (string anchor in member.SourceAnchorIds.Concat(inputs.Skills.KarmaPurchasePolicy.SourceAnchorIds))
                CollectionAssert.Contains(row.SourceAnchorIds.ToArray(), anchor);
        }
        var baseline = CharacterCreationSkillsService.PreviewWithProposedAttributes(inputs.Skills,
            choices.AttributeAllocations, choices.SkillAllocations, choices.GroupAllocations).Value!;
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            withoutPurchases.Value.Skills!.SkillGroups, reviewed.Value.Skills!.SkillGroups));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(baseline.ActiveSkillPointBudget, reviewed.Value.Skills.ActiveSkillPointBudget));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(baseline.SkillGroupPointBudget, reviewed.Value.Skills.SkillGroupPointBudget));
        foreach (var row in baseline.Skills)
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(row,
                reviewed.Value.Skills.Skills.Single(item => item.Kind == row.Kind && item.SourceSkillId == row.SourceSkillId)));
        var reversed = CharacterCreationCompletionReviewService.Review(inputs, request with
        { Selections = choices with { SkillKarmaPurchases = choices.SkillKarmaPurchases!.Reverse().ToArray() } });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reversed.Outcome, string.Join(",", reversed.Blockers));
        Assert.AreEqual(reviewed.Value.Skills.ProposalDigest, reversed.Value!.Skills!.ProposalDigest);
        var complete = CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        CollectionAssert.Contains(complete.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);

        var before = context.Store.Get(context.WorkspaceId).Value!;
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        var cash = service.LoadStartingCash(stamp, context.WorkspaceId, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cash.Outcome, string.Join(",", cash.Blockers));
        var final = service.Review(stamp, context.WorkspaceId, request, new(cash.Value!.AuthorityDigest, cash.Value.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, final.Outcome, string.Join(",", final.Blockers));
        var command = new CharacterCreationCompletionApplyRequest(final.Value!.Projection.Binding, final.Value.Proposal,
            final.Value.Projection.ReviewDigest, final.Value.Projection.StartingCash.Choice,
            final.Value.Projection.ProjectionDigest, "skill-group-compensation", true);
        var applied = service.Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        var cold = new Chummer.Infrastructure.Workspaces.FileWorkspaceStore(context.Directory);
        var opened = cold.Get(context.WorkspaceId);
        Assert.IsTrue(opened.Success, opened.Error);
        var after = opened.Value!;
        Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
        Assert.AreEqual(final.Value.Projection.CharacterXml, after.Document.Content);
        var xml = System.Xml.Linq.XDocument.Parse(after.Document.Content).Root!;
        Assert.AreEqual(final.Value.Projection.KarmaRemaining,
            int.Parse(xml.Element("karma")!.Value, System.Globalization.CultureInfo.InvariantCulture));
        foreach (var member in members)
        {
            var saved = xml.Element("newskills")!.Element("skills")!.Elements("skill")
                .Single(row => row.Element("suid")!.Value == member.SourceSkillId);
            Assert.AreEqual("0", saved.Element("base")!.Value);
            Assert.AreEqual("2", saved.Element("karma")!.Value);
        }
        var archive = after.Document.AuxiliaryState.CharacterCreationFinalizationArchive!;
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidArchive(context.WorkspaceId, archive, applied.Value!));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive.CompletionAuthority!.SelectedReplay,
            out var bootstrap, out var witness));
        Assert.AreEqual(CharacterCreationSelectedCompletionWitness.SchemaV5, witness.Schema);
        Assert.AreEqual(CharacterCreationSelectedPolicyInputs.SchemaV2, witness.SelectedPolicyInputs!.Schema);
        Assert.AreEqual(JsonSerializer.Serialize(coverage), JsonSerializer.Serialize(witness.SelectedPolicyInputs.Policy.Coverage));
        CollectionAssert.AreEqual(inputs.Skills.KarmaCompensationSourceOrder.ToArray(), witness.Skills.KarmaCompensationSourceOrder!.ToArray());
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(before, bootstrap, witness, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(reviewed.Value.Balances), JsonSerializer.Serialize(replay.Value!.Balances));
        var repeated = new OwnerBoundCharacterCreationCompletionService(cold, owners, context.Resolver, context.Queries).Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, repeated.Outcome, string.Join(",", repeated.Blockers));
        Assert.AreEqual(applied.Value!.ReceiptDigest, repeated.Value!.ReceiptDigest);
        Assert.AreEqual(1, cold.Get(context.WorkspaceId).Value!.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Count);
        Assert.AreEqual(JsonSerializer.Serialize(after), JsonSerializer.Serialize(cold.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    [DataRow(false, 12)]
    [DataRow(true, 17)]
    public void Completion_skill_karma_compensation_preserves_point_floors_and_clamps_after_specialization(bool specialize, int expected)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: ConfigureCompensation);
        var inputs = SkillKarmaInputs(context);
        var members = CompensationMembers(inputs);
        var payer = members.Single(row => row.SourceSkillId == inputs.Skills.KarmaCompensationSourceOrder!
            .First(id => members.Any(item => item.SourceSkillId == id)));
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var allocations = choices.SkillAllocations.Concat(members.Select(row =>
            new CharacterCreationSkillAllocation(row.SourceSkillId, row.Kind, 1, null, false))).ToArray();
        var purchases = members.Select(row => Purchase(row, 2) with
        { SpecializationOptionId = specialize && row.SourceSkillId == payer.SourceSkillId ? payer.Specializations[0].OptionId : null }).ToArray();
        var quoted = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills,
            choices.AttributeAllocations, allocations, choices.GroupAllocations, purchases);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, quoted.Outcome, string.Join(",", quoted.Blockers));
        Assert.AreEqual(expected, quoted.Value!.Skills.Sum(row => row.KarmaCost));
        foreach (var member in members)
        {
            var row = quoted.Value.Skills.Single(item => item.SourceSkillId == member.SourceSkillId);
            Assert.AreEqual(1, row.PointCost);
            Assert.AreEqual(1, row.KarmaLevels);
            Assert.AreEqual(member.SourceSkillId == payer.SourceSkillId ? specialize ? 5 : 0 : 6, row.KarmaCost);
        }
        // A missing source-order witness must not silently charge the old full
        // individual price for this coupled interval. Ordinary history still works.
        var historical = inputs.Skills with { KarmaCompensationSourceOrder = null, SnapshotDigest = string.Empty };
        historical = historical with { SnapshotDigest = CharacterCreationSkillsDigest.Compute(historical) };
        var refused = CharacterCreationSkillsService.PreviewCompletionPurchases(historical,
            choices.AttributeAllocations, allocations, choices.GroupAllocations, purchases);
        CollectionAssert.Contains(refused.Blockers.ToArray(), CharacterCreationSkillKarmaPurchaseBlockers.GroupCompensationUnsupported);
        var ordinary = CharacterCreationSkillsService.PreviewCompletionPurchases(historical,
            choices.AttributeAllocations, choices.SkillAllocations, choices.GroupAllocations, [Purchase(members[0], 2)]);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, ordinary.Outcome, string.Join(",", ordinary.Blockers));
        Assert.AreEqual(8, ordinary.Value!.Skills.Single(row => row.SourceSkillId == members[0].SourceSkillId).KarmaCost);
    }

    [TestMethod]
    [DataRow("karmanewskillgroup")]
    [DataRow("karmaimproveskillgroup")]
    [DataRow("karmaimproveactiveskill")]
    public void Completion_skill_karma_compensation_rejects_overflow_without_a_quote(string field)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: profile =>
        {
            ConfigureCompensation(profile);
            profile.Element("karmacost")!.SetElementValue(field, int.MaxValue);
        });
        var inputs = SkillKarmaInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var denied = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills,
            choices.AttributeAllocations, choices.SkillAllocations, choices.GroupAllocations,
            CompensationMembers(inputs).Select(row => Purchase(row, 3)).ToArray());
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, denied.Outcome);
        Assert.IsNull(denied.Value);
        CollectionAssert.Contains(denied.Blockers.ToArray(), CharacterCreationSkillKarmaPurchaseBlockers.PurchaseInvalid);
    }

    [TestMethod]
    public void Completion_skill_karma_compensation_source_changes_revoke_live_Apply_but_preserve_historical_replay()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: ConfigureCompensation);
        var inputs = SkillKarmaInputs(context);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
        { SkillKarmaPurchases = CompensationMembers(inputs).Select(row => Purchase(row, 2)).ToArray() };
        var request = new CharacterCreationCompletionReviewRequest(inputs.SnapshotDigest, choices);
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        var cash = service.LoadStartingCash(stamp, context.WorkspaceId, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cash.Outcome, string.Join(",", cash.Blockers));
        var review = service.Review(stamp, context.WorkspaceId, request, new(cash.Value!.AuthorityDigest, cash.Value.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var command = new CharacterCreationCompletionApplyRequest(review.Value!.Projection.Binding, review.Value.Proposal,
            review.Value.Projection.ReviewDigest, review.Value.Projection.StartingCash.Choice,
            review.Value.Projection.ProjectionDigest, "compensation-source-change", true);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(before.Document.Content)!, before.Document.Content, out var bootstrap));
        var witness = CharacterCreationSelectedCompletionEvaluator.Capture(before, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, witness.Outcome, string.Join(",", witness.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, witness.Value!, out var archive));
        string profileId = inputs.Skills.KarmaPurchasePolicy!.SettingsProfileId;
        string path = Path.Combine(context.Directory, "source", "data", CharacterCreationBootstrapProfiles.SettingsSourceFile(profileId));
        var document = System.Xml.Linq.XDocument.Load(path);
        document.Root!.Element("settings")!.Elements("setting").Single(row => row.Element("id")!.Value == profileId)
            .Element("karmacost")!.SetElementValue("karmaimproveskillgroup", 5);
        document.Save(path);
        Assert.IsNull(service.Apply(stamp, command).Value);
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var coldWitness));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(before, coldBootstrap, coldWitness, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual((int?)17, replay.Value!.Balances.SkillsKarmaCost);
        Assert.AreEqual(0, owners.ActiveLeases);
    }


    [TestMethod]
    [DataRow(0, 4)]
    [DataRow(4, 4)]
    [DataRow(10, 10)]
    public void Completion_skill_karma_compensation_keeps_canonical_clamp_and_configured_surcharges(int groupCost, int expected)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: profile =>
        {
            ConfigureCompensation(profile);
            profile.Element("karmacost")!.SetElementValue("karmanewskillgroup", groupCost);
        });
        var inputs = SkillKarmaInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var quote = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills,
            choices.AttributeAllocations, choices.SkillAllocations, choices.GroupAllocations,
            CompensationMembers(inputs).Select(row => Purchase(row, 1)).ToArray());
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, quote.Outcome, string.Join(",", quote.Blockers));
        Assert.AreEqual(expected, quote.Value!.Skills.Sum(row => row.KarmaCost));
        Assert.IsTrue(quote.Value.Skills.All(row => row.KarmaCost >= 0));
    }

    [TestMethod]
    public void Completion_skill_karma_compensation_does_not_transfer_the_first_members_discount()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: ConfigureCompensation);
        var inputs = SkillKarmaInputs(context);
        var members = CompensationMembers(inputs);
        string first = inputs.Skills.KarmaCompensationSourceOrder!.First(id => members.Any(row => row.SourceSkillId == id));
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var allocations = choices.SkillAllocations.Append(new CharacterCreationSkillAllocation(
            first, CharacterCreationSkillKinds.Active, 2, null, false)).ToArray();
        var quote = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills,
            choices.AttributeAllocations, allocations, choices.GroupAllocations,
            members.Where(row => row.SourceSkillId != first).Select(row => Purchase(row, 2)).ToArray());
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, quote.Outcome, string.Join(",", quote.Blockers));
        Assert.AreEqual(16, quote.Value!.Skills.Sum(row => row.KarmaCost));
        var preserved = quote.Value.Skills.Single(row => row.SourceSkillId == first);
        Assert.AreEqual(2, preserved.PointCost);
        Assert.AreEqual(0, preserved.KarmaLevels);
        Assert.AreEqual(0, preserved.KarmaCost);
    }


    [TestMethod]
    [DataRow("missing-order")]
    [DataRow("duplicate-order")]
    [DataRow("missing-policy")]
    [DataRow("missing-regroup")]
    [DataRow("incomplete-members")]
    [DataRow("ambiguous-identity")]
    public void Compensation_coverage_requires_complete_typed_inputs_and_source_order(string fault)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: ConfigureCompensation);
        var inputs = SkillKarmaInputs(context);
        var skills = inputs.Skills;
        var authority = skills.Authority;
        var policy = skills.KarmaPurchasePolicy!;
        var group = authority.SkillGroups.First(row => row.MemberSkillSourceIds.Count > 2);
        switch (fault)
        {
            case "missing-order": skills = skills with { KarmaCompensationSourceOrder = null }; break;
            case "duplicate-order":
                var order = skills.KarmaCompensationSourceOrder!.ToArray();
                order[0] = order[1];
                skills = skills with { KarmaCompensationSourceOrder = order };
                break;
            case "missing-policy": skills = skills with { KarmaPurchasePolicy = null }; break;
            case "missing-regroup":
                policy = policy with { AllowSkillRegrouping = null };
                policy = policy with { AuthorityDigest = CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(policy) };
                skills = skills with { KarmaPurchasePolicy = policy };
                break;
            case "incomplete-members":
                group = group with { MemberSkillSourceIds = group.MemberSkillSourceIds.Skip(1).ToArray() };
                string digest = CharacterCreationSkillsDigest.Compute(new
                {
                    Schema = "chummer.sr5.creation-skill-group-source.v1", group.Name,
                    MemberSkillSourceIds = group.MemberSkillSourceIds.ToArray(),
                    authority.EffectiveSkillsInputsDigest
                });
                group = group with { GroupId = digest, GroupDigest = digest };
                authority = authority with { SkillGroups = authority.SkillGroups.Select(row => row.Name == group.Name ? group : row).ToArray() };
                break;
            case "ambiguous-identity":
                authority = authority with { ActiveSkills = authority.ActiveSkills.Append(authority.ActiveSkills[0]).ToArray() };
                break;
        }
        // These are deliberately invalid inputs, rehashed before evaluation so a
        // stale public digest cannot be the only reason a coverage claim is refused.
        authority = authority with { AuthorityDigest = string.Empty };
        authority = authority with { AuthorityDigest = CharacterCreationSkillsDigest.Compute(authority) };
        skills = skills with { Authority = authority, Binding = skills.Binding with { SkillsAuthorityDigest = authority.AuthorityDigest },
            SnapshotDigest = string.Empty };
        skills = skills with { SnapshotDigest = CharacterCreationSkillsDigest.Compute(skills) };
        var coverage = CharacterCreationCompletionDomainPolicyProjection.DescribeCoverage(inputs with { Skills = skills },
            CharacterCreationCompletionConsumerPolicyProjection.DescribeCoverage(true), compensation: true);
        var row = coverage.Single(item => item.Operation == CharacterCreationConsumerPolicyOperation.SkillGroupKarmaCompensation);
        bool missing = fault is "missing-order" or "missing-policy" or "missing-regroup";
        Assert.AreEqual(missing ? CharacterCreationConsumerPolicyDisposition.Missing : CharacterCreationConsumerPolicyDisposition.Unsupported, row.Disposition);
        Assert.AreEqual(missing ? CharacterCreationConsumerPolicyReason.OriginalInputUnavailable : CharacterCreationConsumerPolicyReason.ConsumerOperationUnavailable, row.Reason);
        Assert.IsFalse(CharacterCreationSkillsService.HasCompletionCompensationInputs(skills));
        if (fault == "incomplete-members")
            Assert.IsTrue(CharacterCreationSkillsDraftIntegrity.IsValidAuthority(authority),
                "A self-consistent truncated group must fail the complete closure check, not merely its public hash.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Compensation_coverage_does_not_admit_selected_skill_modifiers(bool legacyCatalog)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            legacyQualityCatalog: legacyCatalog, amendSettings: ConfigureCompensation, prepareSource: root =>
            {
                string path = Path.Combine(root, "data", "qualities.xml");
                var xml = System.Xml.Linq.XDocument.Load(path);
                var quality = xml.Root!.Element("qualities")!.Elements("quality").Single(row => row.Element("name")?.Value == "Guts");
                var bonus = quality.Element("bonus");
                if (bonus is null) { bonus = new System.Xml.Linq.XElement("bonus"); quality.Add(bonus); }
                bonus.Add(new System.Xml.Linq.XElement("skillkarmacost", 1));
                xml.Save(path);
            });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var loaded = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries)
            .LoadCompletionInputsWithConsumerPolicy(owners.Capture(), new(context.WorkspaceId) { IncludeKarmaPurchasePolicy = true });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        var inputs = loaded.Value!;
        var qualityOption = inputs.Lifestyles.Qualities.Authority.Options.Single(row => row.Name == "Guts");
        var coverage = inputs.EffectivePolicy!.Consumers!.Coverage;
        var compensation = coverage.Single(row => row.Operation == CharacterCreationConsumerPolicyOperation.SkillGroupKarmaCompensation);
        Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Admitted, compensation.Disposition);
        Assert.AreEqual(CharacterCreationConsumerPolicyReason.MatchedConsumerInputs, compensation.Reason);
        Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Unsupported,
            coverage.Single(row => row.Operation == CharacterCreationConsumerPolicyOperation.SkillKarmaModifiers).Disposition);
        // Both catalog entry points reject this effect before quality pricing.
        Assert.IsFalse(qualityOption.IsSelectable);
        Assert.AreEqual(CharacterCreationQualitiesBlockers.EffectsNotProjectable, qualityOption.DisableReasonKey);
        // Exercise the real modifier detector independently of the closed catalog boundary.
        Assert.IsTrue(CharacterCreationSkillsService.HasUnsupportedCompletionSkillModifiers([qualityOption.SourceNodeXml]));
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
        {
            QualityOptionIds = [qualityOption.OptionId],
            SkillKarmaPurchases = CompensationMembers(inputs).Select(row => Purchase(row, 2)).ToArray()
        };
        var result = CharacterCreationCompletionReviewService.Review(inputs,
            new(inputs.SnapshotDigest, choices) { EffectivePolicyDigest = inputs.EffectivePolicy!.PolicyDigest });
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationQualitiesBlockers.EffectsNotProjectable);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
    }

    private static void ConfigureCompensation(System.Xml.Linq.XElement profile)
    {
        profile.SetElementValue("compensateskillgroupkarmadifference", "True");
        var costs = profile.Element("karmacost")!;
        costs.SetElementValue("karmanewactiveskill", 2);
        costs.SetElementValue("karmaimproveactiveskill", 3);
        costs.SetElementValue("karmanewskillgroup", 4);
        costs.SetElementValue("karmaimproveskillgroup", 4);
        costs.SetElementValue("karmaspecialization", 9);
    }

    [TestMethod]
    public void Completion_funded_group_payer_follows_enabled_source_order_not_display_or_purchase_order()
    {
        string expectedPayer = string.Empty;
        string[] canonical = [];
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            amendSettings: settings => settings.SetElementValue("compensateskillgroupkarmadifference", "True"),
            prepareSource: root =>
            {
                string path = Path.Combine(root, "data", "skills.xml");
                var xml = System.Xml.Linq.XDocument.Load(path);
                var active = xml.Root!.Element("skills")!;
                var members = active.Elements("skill").Where(row => row.Element("skillgroup")?.Value == "Athletics").ToArray();
                canonical = members.Select(row => row.Element("id")!.Value.ToLowerInvariant()).Order(StringComparer.Ordinal).ToArray();
                var disabled = members.Where(row => bool.TryParse(row.Element("requiresflymovement")?.Value, out bool fly) && fly).ToArray();
                Assert.IsTrue(disabled.Any(row => row.Element("name")?.Value == "Flight"));
                var eligible = members.Except(disabled).ToArray();
                string sortedFirst = members.Select(row => row.Element("id")!.Value.ToLowerInvariant()).Min(StringComparer.Ordinal)!;
                var displayFirst = eligible.OrderBy(row => row.Element("name")!.Value, StringComparer.Ordinal).First();
                var first = eligible.First(row => row != displayFirst && row.Element("id")!.Value.ToLowerInvariant() != sortedFirst);
                var enabled = new[] { first }.Concat(eligible.Where(row => row != first).Reverse()).ToArray();
                expectedPayer = enabled[0].Element("id")!.Value.ToLowerInvariant();
                foreach (var row in members) row.Remove();
                active.AddFirst(disabled.Concat(enabled));
                xml.Save(path);
            });
        var inputs = SkillKarmaInputs(context);
        var group = inputs.Skills.Authority.SkillGroups.Single(row => row.Name == "Athletics");
        CollectionAssert.AreEqual(canonical, group.MemberSkillSourceIds.Order(StringComparer.Ordinal).ToArray());
        var enabledMembers = EnabledGroupMembers(inputs, group);
        string payer = inputs.Skills.KarmaCompensationSourceOrder!.First(id => enabledMembers.Any(row => row.SourceSkillId == id));
        Assert.AreEqual(expectedPayer, payer);
        Assert.AreNotEqual(group.MemberSkillSourceIds[0], payer, "The fixture must distinguish sorted membership from native order.");
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
        { SkillKarmaPurchases = enabledMembers.OrderBy(row => row.Name, StringComparer.Ordinal).Select(row => Purchase(row, 2)).ToArray() };
        var quote = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, quote.Outcome, string.Join(",", quote.Blockers));
        foreach (var member in enabledMembers)
            Assert.AreEqual(member.SourceSkillId == payer ? 2 : 4,
                quote.Value!.Skills!.Skills.Single(row => row.SourceSkillId == member.SourceSkillId).KarmaCost);
        var native = AssertCompensationApplyAndColdRecovery(context, inputs, choices, "source-order-payer");
        Assert.IsFalse(native.Element("newskills")!.Element("skills")!.Elements("skill")
            .Any(row => row.Element("suid")?.Value == inputs.Skills.Authority.ActiveSkills.Single(skill => skill.Name == "Flight").SourceSkillId));
    }


    internal static CharacterCreationSkillCatalogEntry[] EnabledGroupMembers(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationSkillGroupCatalogEntry group)
    {
        var movement = inputs.Skills.MovementCapability;
        return inputs.Skills.Authority.ActiveSkills.Where(row => group.MemberSkillSourceIds.Contains(row.SourceSkillId)
            && CharacterCreationSkillsAccessRules.IsSkillAvailable(inputs.Skills.Authority, row.SourceSkillId)
            && (!row.RequiresGroundMovement || movement.Ground)
            && (!row.RequiresSwimMovement || movement.Swim)
            && (!row.RequiresFlyMovement || movement.Fly)).ToArray();
    }

    private static CharacterCreationSkillCatalogEntry[] CompensationMembers(CharacterCreationCompletionReviewInputs inputs)
    {
        var group = inputs.Skills.Authority.SkillGroups.Single(row => row.Name == "Influence");
        var members = EnabledGroupMembers(inputs, group);
        Assert.AreEqual(3, members.Length);
        Assert.IsTrue(members.All(row => CharacterCreationSkillsAccessRules.IsSkillAvailable(inputs.Skills.Authority, row.SourceSkillId)));
        Assert.IsFalse(inputs.Skills.GroupAllocations.Any(row => row.GroupId == group.GroupId));
        return members;
    }


    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, false)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, true)]
    [DataRow(CharacterCreationBuildMethods.Priority, null)]
    public void Completion_funded_group_compensation_persists_once_and_cold_replay_keeps_exact_provenance(string method, bool? regroup)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, buildMethod: method,
            amendSettings: settings =>
            {
                settings.SetElementValue("compensateskillgroupkarmadifference", "True");
                if (regroup.HasValue) settings.SetElementValue("allowskillregrouping", regroup.Value.ToString());
                else foreach (var field in settings.Elements("allowskillregrouping").ToArray()) field.Remove();
            });
        var inputs = SkillKarmaInputs(context);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var group = inputs.Skills.Authority.SkillGroups.Single(row => row.Name == "Athletics");
        var members = EnabledGroupMembers(inputs, group);
        var flight = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Flight");
        CollectionAssert.Contains(group.MemberSkillSourceIds.ToArray(), flight.SourceSkillId);
        Assert.IsFalse(members.Any(row => row.SourceSkillId == flight.SourceSkillId));
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
        { SkillKarmaPurchases = members.Select(row => Purchase(row, 2)).ToArray() };
        var request = new CharacterCreationCompletionReviewRequest(inputs.SnapshotDigest, choices);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        Assert.AreEqual((int?)10, review.Value!.Balances.SkillsKarmaCost);
        string payer = inputs.Skills.KarmaCompensationSourceOrder!.First(id => EnabledGroupMembers(inputs, group).Any(row => row.SourceSkillId == id));
        foreach (var member in members)
        {
            var row = review.Value.Skills!.Skills.Single(item => item.SourceSkillId == member.SourceSkillId);
            Assert.AreEqual(1, row.GroupRating);
            Assert.AreEqual(1, row.KarmaLevels);
            Assert.AreEqual(0, row.PointCost);
            Assert.AreEqual(member.SourceSkillId == payer ? 2 : 4, row.KarmaCost);
        }
        var projectedGroup = review.Value.Skills!.SkillGroups.Single(row => row.GroupId == group.GroupId);
        Assert.AreEqual(1, projectedGroup.PointCost);
        Assert.AreEqual(0, projectedGroup.GrantedRating);
        Assert.AreEqual(false, projectedGroup.IsBroken);
        Assert.AreEqual(regroup ?? true, projectedGroup.AllowSkillRegrouping);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        var cash = service.LoadStartingCash(stamp, context.WorkspaceId, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cash.Outcome, string.Join(",", cash.Blockers));
        var final = service.Review(stamp, context.WorkspaceId, request, new(cash.Value!.AuthorityDigest, cash.Value.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, final.Outcome, string.Join(",", final.Blockers));
        Assert.AreEqual(10m, final.Value!.Projection.OrderedDeltas.Where(row => row.Kind == CharacterCreationFinalizationDeltaKinds.Skill).Sum(row => row.KarmaCost));
        var xml = System.Xml.Linq.XDocument.Parse(final.Value.Projection.CharacterXml).Root!;
        var savedGroup = xml.Element("newskills")!.Element("groups")!.Elements("group").Single(row => row.Element("name")!.Value == group.Name);
        Assert.AreEqual("1", savedGroup.Element("base")!.Value);
        Assert.AreEqual("0", savedGroup.Element("karma")!.Value);
        Assert.AreEqual("False", savedGroup.Element("isbroken")!.Value);
        foreach (var member in members)
        {
            var saved = xml.Element("newskills")!.Element("skills")!.Elements("skill").Single(row => row.Element("suid")!.Value == member.SourceSkillId);
            Assert.AreEqual("0", saved.Element("base")!.Value, "Group rank must not be duplicated in personal base.");
            Assert.AreEqual("1", saved.Element("karma")!.Value);
        }
        var command = new CharacterCreationCompletionApplyRequest(final.Value.Projection.Binding, final.Value.Proposal,
            final.Value.Projection.ReviewDigest, final.Value.Projection.StartingCash.Choice,
            final.Value.Projection.ProjectionDigest, "funded-compensation-once", true);
        var applied = service.Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        var cold = new Chummer.Infrastructure.Workspaces.FileWorkspaceStore(context.Directory);
        var reopened = cold.Get(context.WorkspaceId);
        Assert.IsTrue(reopened.Success, reopened.Error);
        Assert.AreEqual(final.Value.Projection.CharacterXml, reopened.Value!.Document.Content);
        Assert.AreEqual(before.ContentRevision + 1, reopened.Value.ContentRevision);
        var archive = reopened.Value.Document.AuxiliaryState.CharacterCreationFinalizationArchive!;
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidArchive(context.WorkspaceId, archive, applied.Value!));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive.CompletionAuthority!.SelectedReplay, out var bootstrap, out var witness));
        Assert.AreEqual(regroup ?? true, witness.Skills.KarmaPurchasePolicy!.AllowSkillRegrouping);
        CollectionAssert.AreEqual(inputs.Skills.KarmaCompensationSourceOrder.ToArray(), witness.Skills.KarmaCompensationSourceOrder!.ToArray());
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(before, bootstrap, witness, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(review.Value.Skills), JsonSerializer.Serialize(replay.Value!.Skills));
        Assert.AreEqual(JsonSerializer.Serialize(review.Value.Balances), JsonSerializer.Serialize(replay.Value.Balances));
        var again = new OwnerBoundCharacterCreationCompletionService(cold, owners, context.Resolver, context.Queries).Apply(stamp, command);
        Assert.AreEqual(applied.Value!.ReceiptDigest, again.Value!.ReceiptDigest);
        Assert.AreEqual(JsonSerializer.Serialize(reopened.Value), JsonSerializer.Serialize(cold.Get(context.WorkspaceId).Value));
        var complete = CharacterCreationCompletionReviewService.Review(inputs, request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, complete.Outcome);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void Completion_funded_group_breaks_and_regroups_from_final_creation_choices(bool regroup, bool specializationsBreak)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: settings =>
        {
            settings.SetElementValue("compensateskillgroupkarmadifference", "True");
            settings.SetElementValue("allowskillregrouping", regroup.ToString());
            settings.SetElementValue("specializationsbreakskillgroups", specializationsBreak.ToString());
        });
        var inputs = SkillKarmaInputs(context);
        var group = inputs.Skills.Authority.SkillGroups.Single(row => row.Name == "Athletics");
        var members = EnabledGroupMembers(inputs, group);
        var first = members.First(row => row.Specializations.Count > 0);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        CharacterCreationAttributeSkillsPreview Quote(params CharacterCreationSkillKarmaPurchase[] purchases)
        {
            var result = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills,
                choices.AttributeAllocations, choices.SkillAllocations, choices.GroupAllocations, purchases);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            return result.Value!;
        }
        Assert.AreEqual(true, Quote(Purchase(first, 2)).SkillGroups.Single(row => row.GroupId == group.GroupId).IsBroken);
        var equal = members.Select(row => Purchase(row, 2)).ToArray();
        var reunited = Quote(equal);
        Assert.AreEqual(false, reunited.SkillGroups.Single(row => row.GroupId == group.GroupId).IsBroken,
            "Legacy does not latch career IsBroken during character creation, including when career regrouping is off.");
        Assert.AreEqual(reunited.ProposalDigest, Quote(equal.Reverse().ToArray()).ProposalDigest);
        var specialized = Quote(equal.Select(row => row.SourceSkillId == first.SourceSkillId
            ? row with { SpecializationOptionId = first.Specializations[0].OptionId } : row).ToArray());
        Assert.AreEqual(specializationsBreak, specialized.SkillGroups.Single(row => row.GroupId == group.GroupId).IsBroken);
        Assert.AreEqual(1, specialized.SkillGroups.Single(row => row.GroupId == group.GroupId).PointCost);
    }

    [TestMethod]
    public void Completion_group_compensation_preserves_manual_points_and_point_specialization()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: settings =>
        {
            settings.SetElementValue("compensateskillgroupkarmadifference", "True");
            settings.SetElementValue("usepointsonbrokengroups", "True");
            settings.SetElementValue("specializationsbreakskillgroups", "False");
        });
        var inputs = SkillKarmaInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var group = inputs.Skills.Authority.SkillGroups.Single(row => row.Name == "Athletics");
        string payer = inputs.Skills.KarmaCompensationSourceOrder!.First(id => EnabledGroupMembers(inputs, group).Any(row => row.SourceSkillId == id));
        var member = inputs.Skills.Authority.ActiveSkills.Single(row => row.SourceSkillId == payer);
        choices = choices with
        {
            SkillAllocations = [.. choices.SkillAllocations, new(member.SourceSkillId, member.Kind, 2, member.Specializations[0].OptionId, false)],
            SkillKarmaPurchases = EnabledGroupMembers(inputs, group).Select(row => Purchase(row, 3)).ToArray()
        };
        var result = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var row = result.Value!.Skills!.Skills.Single(row => row.SourceSkillId == payer);
        Assert.AreEqual(2, row.PointCost);
        Assert.AreEqual(1, row.GroupRating);
        Assert.AreEqual(1, row.KarmaLevels);
        Assert.IsFalse(row.KarmaSpecialization);
        Assert.AreEqual(member.Specializations[0].OptionId, row.SpecializationOptionId);
        Assert.AreEqual((int?)23, result.Value.Balances.SkillsKarmaCost);
        var deltas = new List<CharacterCreationFinalizationDelta>(); int order = 0;
        var xml = CharacterCreationFinalizationProjector.BuildSkills(result.Value.Skills.Skills, result.Value.Skills.SkillGroups,
            inputs.Skills.SelectedActiveSkillPoints, inputs.Skills.SelectedSkillGroupPoints, "manual-provenance", deltas, ref order);
        var native = xml.Element("skills")!.Elements("skill").Single(item => item.Element("suid")!.Value == payer);
        Assert.AreEqual("1", native.Element("base")!.Value);
        Assert.AreEqual("1", native.Element("karma")!.Value);
        Assert.IsNull(native.Element("buywithkarma"));
        Assert.AreEqual(member.Specializations[0].Name, native.Element("specs")!.Element("spec")!.Element("name")!.Value);
        AssertCompensationApplyAndColdRecovery(context, inputs, choices, "manual-group-provenance");
    }

    [TestMethod]
    public void Completion_group_grants_and_purchased_group_base_remain_separate_from_personal_Karma()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, talentValue: "Aspected Magician",
            talentRank: "B", talentGroupName: "Sorcery",
            amendSettings: settings => settings.SetElementValue("compensateskillgroupkarmadifference", "True"));
        var inputs = SkillKarmaInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var grant = inputs.Skills.SavedSkillsState!.SkillGroups.Single(row => row.GrantedRating > 0);
        var grantGroup = inputs.Skills.Authority.SkillGroups.Single(row => row.GroupId == grant.GroupId);
        var source = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Spellcasting");
        int floor = grant.GrantedRating + 1;
        choices = choices with
        {
            GroupAllocations = choices.GroupAllocations.Select(row => row.GroupId == grant.GroupId ? row with { Rating = floor } : row).ToArray(),
            SkillKarmaPurchases = [Purchase(source, floor + 1)]
        };
        CollectionAssert.Contains(CharacterCreationCompletionPreservationRules.Check(inputs, choices).ToArray(),
            CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict);
        // Raising the saved group requires permission for the group and every member.
        choices = choices with
        {
            ChangeableSkillTargets = [new(CharacterCreationSkillCompletionKinds.Group, grantGroup.GroupId),
                .. grantGroup.MemberSkillSourceIds.Select(id => new CharacterCreationSkillCompletionTarget(
                    CharacterCreationSkillKinds.Active, id))]
        };
        var review = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var group = review.Value!.Skills!.SkillGroups.Single(row => row.GroupId == grant.GroupId);
        Assert.AreEqual(grant.GrantedRating, group.GrantedRating);
        Assert.AreEqual(1, group.PointCost);
        Assert.AreEqual(true, group.IsBroken);
        var skill = review.Value.Skills.Skills.Single(row => row.SourceSkillId == source.SourceSkillId);
        Assert.AreEqual(floor, skill.GroupRating);
        Assert.AreEqual(1, skill.KarmaLevels);
        Assert.AreEqual(0, skill.PointCost);
        Assert.AreEqual((floor + 1) * 2, skill.KarmaCost);
        var deltas = new List<CharacterCreationFinalizationDelta>(); int order = 0;
        var xml = CharacterCreationFinalizationProjector.BuildSkills(review.Value.Skills.Skills, review.Value.Skills.SkillGroups,
            inputs.Skills.SelectedActiveSkillPoints, inputs.Skills.SelectedSkillGroupPoints, "grant-provenance", deltas, ref order);
        var nativeGroup = xml.Element("groups")!.Elements("group").Single(row => row.Element("name")!.Value == group.Name);
        Assert.AreEqual("1", nativeGroup.Element("base")!.Value);
        Assert.AreEqual("0", nativeGroup.Element("karma")!.Value);
        Assert.AreEqual("True", nativeGroup.Element("isbroken")!.Value);
        Assert.AreEqual("broken", deltas.Single(row => row.DeltaId == $"skill-group-state:{group.GroupId}").AfterValue);
        var nativeSkill = xml.Element("skills")!.Elements("skill").Single(row => row.Element("suid")!.Value == source.SourceSkillId);
        Assert.AreEqual("0", nativeSkill.Element("base")!.Value);
        Assert.AreEqual("1", nativeSkill.Element("karma")!.Value);
        var committed = AssertCompensationApplyAndColdRecovery(context, inputs, choices, "granted-group-provenance");
        var improvements = committed.Element("improvements")!.Elements("improvement").Where(row =>
            row.Element("improvementttype")?.Value == "SkillGroupBase" && row.Element("improvedname")?.Value == group.Name).ToArray();
        Assert.HasCount(1, improvements);
        Assert.AreEqual(grant.GrantedRating, (int?)improvements[0].Element("val"));
    }

    [TestMethod]
    public void Completion_personal_talent_grants_group_points_and_personal_Karma_reach_native_grant_consumer()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, talentValue: "Magician", amendSettings: settings =>
        {
            settings.SetElementValue("compensateskillgroupkarmadifference", "True");
            settings.SetElementValue("usepointsonbrokengroups", "True");
        });
        var inputs = SkillKarmaInputs(context);
        var grant = inputs.Skills.SavedSkillsState!.Skills.First(row => row.GrantedRating > 0 && row.SkillGroup is not null);
        var group = inputs.Skills.Authority.SkillGroups.Single(row => row.MemberSkillSourceIds.Contains(grant.SourceSkillId));
        var source = inputs.Skills.Authority.ActiveSkills.Single(row => row.SourceSkillId == grant.SourceSkillId);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        choices = choices with
        {
            // Balance personal floors first, as required by native BaseUnbroken
            // when talent free ranks already exist before group points are placed.
            SkillAllocations = [.. choices.SkillAllocations.Where(row => row.Kind != CharacterCreationSkillKinds.Active
                    || !group.MemberSkillSourceIds.Contains(row.SourceSkillId)),
                .. group.MemberSkillSourceIds.Select(id => new CharacterCreationSkillAllocation(id,
                    CharacterCreationSkillKinds.Active, grant.GrantedRating + 1, null, false))],
            GroupAllocations = [.. choices.GroupAllocations, new(group.GroupId, 1)],
            SkillKarmaPurchases = [Purchase(source, grant.GrantedRating + 2)]
        };
        CollectionAssert.Contains(CharacterCreationCompletionPreservationRules.Check(inputs, choices).ToArray(),
            CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict);
        // The new group also changes its saved talent-granted members.
        choices = choices with
        {
            ChangeableSkillTargets = [new(CharacterCreationSkillCompletionKinds.Group, group.GroupId),
                .. group.MemberSkillSourceIds.Select(id => new CharacterCreationSkillCompletionTarget(
                    CharacterCreationSkillKinds.Active, id))]
        };
        var result = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var skill = result.Value!.Skills!.Skills.Single(row => row.SourceSkillId == grant.SourceSkillId);
        Assert.AreEqual(grant.GrantedRating, skill.GrantedRating);
        Assert.AreEqual(1, skill.GroupRating);
        Assert.AreEqual(1, skill.KarmaLevels);
        Assert.AreEqual(0, skill.PointCost);
        var native = AssertCompensationApplyAndColdRecovery(context, inputs, choices, "personal-free-group-karma");
        var saved = native.Element("newskills")!.Element("skills")!.Elements("skill").Single(row => row.Element("suid")!.Value == grant.SourceSkillId);
        Assert.AreEqual("0", saved.Element("base")!.Value);
        Assert.AreEqual("1", saved.Element("karma")!.Value);
        var freeBase = native.Element("improvements")!.Elements("improvement").Where(row =>
            row.Element("improvementttype")?.Value == "SkillBase" && row.Element("improvedname")?.Value == grant.Name).ToArray();
        Assert.HasCount(1, freeBase);
        Assert.AreEqual(grant.GrantedRating, (int?)freeBase[0].Element("val"));
    }

    [TestMethod]
    [DataRow("usepointsonbrokengroups")]
    [DataRow("breakskillgroupsincreatemode")]
    public void Completion_group_legality_uses_selected_policy_without_refunding_points(string setting)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: settings =>
        {
            settings.SetElementValue("compensateskillgroupkarmadifference", "True");
            settings.SetElementValue(setting, setting == "usepointsonbrokengroups" ? "False" : "True");
        });
        var inputs = SkillKarmaInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var running = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Running");
        choices = setting == "usepointsonbrokengroups"
            ? choices with { SkillAllocations = [.. choices.SkillAllocations, new(running.SourceSkillId, running.Kind, 2, null, false)], SkillKarmaPurchases = [] }
            : choices with { SkillKarmaPurchases = [Purchase(running, 2)] };
        var result = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome, string.Join(",", result.Blockers));
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationSkillsBlockers.GroupBroken);
        if (choices.SkillKarmaPurchases!.Count == 0)
        {
            // No interval was requested: the blocked point preview still has a known zero Karma cost.
            Assert.IsNotNull(result.Value);
            Assert.AreEqual((int?)0, result.Value.Balances.SkillsKarmaCost);
            var skills = result.Value.Skills!;
            Assert.AreEqual(1, skills.Skills.Single(row => row.SourceSkillId == running.SourceSkillId).PointCost);
            Assert.AreEqual(1, skills.SkillGroups.Single(row =>
                row.MemberSkillSourceIds.Contains(running.SourceSkillId)).PointCost);
            Assert.AreEqual(1m, skills.SkillGroupPointBudget.Used);
        }
        else
        {
            // A refused paid interval must not expose an exact purchase quote.
            Assert.IsNull(result.Value?.Skills);
            Assert.IsNull(result.Value?.Balances.SkillsKarmaCost);
        }
    }

    [TestMethod]
    [DataRow(0, false, 8)]
    [DataRow(0, true, 8)]
    [DataRow(5, false, 10)]
    [DataRow(9, false, 18)]
    [DataRow(int.MaxValue, false, -1)]
    public void Completion_funded_compensation_uses_configured_intervals_clamp_and_overflow(int rate, bool specialize, int expected)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: settings =>
        {
            settings.SetElementValue("compensateskillgroupkarmadifference", "True");
            settings.Element("karmacost")!.SetElementValue("karmaimproveskillgroup", rate);
        });
        var inputs = SkillKarmaInputs(context);
        var group = inputs.Skills.Authority.SkillGroups.Single(row => row.Name == "Athletics");
        string payer = inputs.Skills.KarmaCompensationSourceOrder!.First(id => EnabledGroupMembers(inputs, group).Any(row => row.SourceSkillId == id));
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var purchases = EnabledGroupMembers(inputs, group).Select(skill =>
        {
            string id = skill.SourceSkillId;
            return Purchase(skill, 2) with { SpecializationOptionId = specialize && id == payer ? skill.Specializations[0].OptionId : null };
        }).ToArray();
        var result = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills,
            choices.AttributeAllocations, choices.SkillAllocations, choices.GroupAllocations, purchases);
        if (expected < 0)
        {
            Assert.IsNull(result.Value);
            CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationSkillKarmaPurchaseBlockers.PurchaseInvalid);
        }
        else
        {
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            Assert.AreEqual(expected, result.Value!.Skills.Sum(row => row.KarmaCost));
            Assert.AreEqual(1m, result.Value.SkillGroupPointBudget.Used);
        }
    }

    [TestMethod]
    [DataRow(1, 10)]
    [DataRow(2, 11)]
    public void Completion_compensation_uses_first_enabled_native_member_and_single_member_does_not_break(int disabled, int expected)
    {
        int canonicalCount = 0, originallyEnabled = 0;
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            amendSettings: settings => settings.SetElementValue("compensateskillgroupkarmadifference", "True"),
            prepareSource: root =>
            {
                string path = Path.Combine(root, "data", "skills.xml");
                var xml = System.Xml.Linq.XDocument.Load(path);
                var members = xml.Root!.Element("skills")!.Elements("skill")
                    .Where(row => row.Element("skillgroup")?.Value == "Athletics").ToArray();
                canonicalCount = members.Length;
                Assert.IsTrue(members.Any(row => row.Element("name")?.Value == "Flight"));
                var eligible = members.Where(row => !bool.TryParse(row.Element("requiresflymovement")?.Value, out bool fly) || !fly).ToArray();
                originallyEnabled = eligible.Length;
                Assert.IsTrue(originallyEnabled > disabled);
                foreach (var member in eligible.Take(disabled)) member.SetElementValue("requiresflymovement", "True");
                xml.Save(path);
            });
        var inputs = SkillKarmaInputs(context);
        var group = inputs.Skills.Authority.SkillGroups.Single(row => row.Name == "Athletics");
        Assert.HasCount(canonicalCount, group.MemberSkillSourceIds);
        var enabled = EnabledGroupMembers(inputs, group);
        Assert.HasCount(originallyEnabled - disabled, enabled);
        string payer = inputs.Skills.KarmaCompensationSourceOrder!.First(id => enabled.Any(row => row.SourceSkillId == id));
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var purchases = enabled.Reverse().Select(row => Purchase(row, 2) with
        { SpecializationOptionId = disabled == 2 ? row.Specializations[0].OptionId : null }).ToArray();
        var result = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills, choices.AttributeAllocations,
            choices.SkillAllocations, choices.GroupAllocations, purchases);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.AreEqual(expected, result.Value!.Skills.Sum(row => row.KarmaCost));
        Assert.AreEqual(disabled == 1 ? 6 : 11, result.Value.Skills.Single(row => row.SourceSkillId == payer).KarmaCost);
        Assert.AreEqual(false, result.Value.SkillGroups.Single(row => row.GroupId == group.GroupId).IsBroken);
        CollectionAssert.AreEqual(group.MemberSkillSourceIds.ToArray(), result.Value.SkillGroups.Single(row => row.GroupId == group.GroupId).MemberSkillSourceIds.ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Completion_without_compensation_keeps_ordinary_historical_prices_and_requires_new_policy_for_group_persistence(bool omitSetting)
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: settings =>
        {
            if (omitSetting) foreach (var field in settings.Elements("compensateskillgroupkarmadifference").ToArray()) field.Remove();
            else settings.SetElementValue("compensateskillgroupkarmadifference", "False");
        });
        var inputs = SkillKarmaInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var policy = inputs.Skills.KarmaPurchasePolicy! with { AllowSkillRegrouping = null };
        policy = policy with { AuthorityDigest = CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(policy) };
        var historical = inputs.Skills with { KarmaPurchasePolicy = policy, SnapshotDigest = string.Empty };
        historical = historical with { SnapshotDigest = CharacterCreationSkillsDigest.Compute(historical) };
        Assert.IsFalse(JsonSerializer.SerializeToElement(policy).TryGetProperty("AllowSkillRegrouping", out _));
        var current = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills, choices.AttributeAllocations,
            choices.SkillAllocations, choices.GroupAllocations, Purchases22(inputs));
        var old = CharacterCreationSkillsService.PreviewCompletionPurchases(historical, choices.AttributeAllocations,
            choices.SkillAllocations, choices.GroupAllocations, Purchases22(inputs));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, old.Outcome, string.Join(",", old.Blockers));
        Assert.AreEqual(22, old.Value!.Skills.Sum(row => row.KarmaCost));
        Assert.AreEqual(JsonSerializer.Serialize(current.Value!.Skills), JsonSerializer.Serialize(old.Value.Skills));
        Assert.AreEqual(JsonSerializer.Serialize(current.Value.SkillGroups), JsonSerializer.Serialize(old.Value.SkillGroups));
        var group = inputs.Skills.Authority.SkillGroups.Single(row => row.Name == "Athletics");
        var purchases = EnabledGroupMembers(inputs, group).Select(row => Purchase(row, 2)).ToArray();
        var funded = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills, choices.AttributeAllocations,
            choices.SkillAllocations, choices.GroupAllocations, purchases);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, funded.Outcome, string.Join(",", funded.Blockers));
        Assert.AreEqual(12, funded.Value!.Skills.Sum(row => row.KarmaCost));
        Assert.IsFalse(inputs.Skills.KarmaPurchasePolicy!.CompensateSkillGroupKarmaDifference);
        Assert.IsNull(inputs.Skills.KarmaCompensationSourceOrder);
        AssertCompensationApplyAndColdRecovery(context, inputs, choices with { SkillKarmaPurchases = purchases }, "ordinary-funded-group");
        var refusedHistory = CharacterCreationSkillsService.PreviewCompletionPurchases(historical, choices.AttributeAllocations,
            choices.SkillAllocations, choices.GroupAllocations, purchases);
        Assert.IsNull(refusedHistory.Value);
        CollectionAssert.Contains(refusedHistory.Blockers.ToArray(), CharacterCreationSkillsBlockers.GroupBroken);
    }

    private static System.Xml.Linq.XElement AssertCompensationApplyAndColdRecovery(ReadyContext context,
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionSelections choices, string key)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var request = new CharacterCreationCompletionReviewRequest(inputs.SnapshotDigest, choices);
        var stamp = owners.Capture();
        var cash = service.LoadStartingCash(stamp, context.WorkspaceId, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cash.Outcome, string.Join(",", cash.Blockers));
        var review = service.Review(stamp, context.WorkspaceId, request, new(cash.Value!.AuthorityDigest, cash.Value.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var command = new CharacterCreationCompletionApplyRequest(review.Value!.Projection.Binding, review.Value.Proposal,
            review.Value.Projection.ReviewDigest, review.Value.Projection.StartingCash.Choice,
            review.Value.Projection.ProjectionDigest, key, true);
        var applied = service.Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        var cold = new Chummer.Infrastructure.Workspaces.FileWorkspaceStore(context.Directory);
        var opened = cold.Get(context.WorkspaceId);
        Assert.IsTrue(opened.Success, opened.Error);
        Assert.AreEqual(review.Value.Projection.CharacterXml, opened.Value!.Document.Content);
        Assert.IsTrue(CharacterCreationCompletionTransaction.IsValidArchive(context.WorkspaceId,
            opened.Value.Document.AuxiliaryState.CharacterCreationFinalizationArchive!, applied.Value!));
        Assert.AreEqual(applied.Value!.ReceiptDigest,
            new OwnerBoundCharacterCreationCompletionService(cold, owners, context.Resolver, context.Queries).Apply(stamp, command).Value!.ReceiptDigest);
        Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(opened.Value), System.Text.Json.JsonSerializer.Serialize(cold.Get(context.WorkspaceId).Value));
        return System.Xml.Linq.XElement.Parse(opened.Value.Document.Content);
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
