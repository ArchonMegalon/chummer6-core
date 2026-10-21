using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationCompletionPlannerTests
{
    [TestMethod]
    public void Skill_karma_modifier_compiler_denies_source_drift_unknown_companions_and_ambiguous_names()
    {
        using var context = ReadyContext.Create(false);
        var inputs = KnowledgeInputs(context);
        var choices = KnowledgeChoices(inputs);
        var selected = CharacterCreationQualitiesService.PreviewWithProposedAttributes(inputs.Lifestyles.Qualities,
            choices.AttributeAllocations, choices.QualityOptionIds).Value!.Qualities.Selections
            .Single(row => row.SourceId == CollegeEducationId);
        var node = XElement.Parse(selected.SourceNodeXml);
        node.Element("bonus")!.ReplaceNodes(XElement.Parse(
            "<activeskillkarmacostmultiplier><name>Perception</name><val>50</val></activeskillkarmacostmultiplier>"));
        string xml = node.ToString(SaveOptions.DisableFormatting);
        var literal = selected with { SourceNodeXml = xml, SourceNodeDigest = CharacterCreationQualitiesRules.ComputeSourceNodeDigest(xml) };
        Assert.IsTrue(CharacterCreationLegacySourceProjector.TrySkillKarmaModifiers([literal], inputs.Skills.Authority, out var effects));
        var target = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Perception");
        Assert.AreEqual(new CharacterCreationSkillKarmaTarget(target.Kind, target.SourceSkillId, target.SourceNodeDigest), effects.Single().Targets.Single());
        Assert.IsFalse(CharacterCreationLegacySourceProjector.TrySkillKarmaModifiers(
            [literal with { SourceNodeDigest = selected.SourceNodeDigest }], inputs.Skills.Authority, out _));
        node.Element("bonus")!.Add(new XElement("skilldisable", "Perception"));
        xml = node.ToString(SaveOptions.DisableFormatting);
        Assert.IsFalse(CharacterCreationLegacySourceProjector.TrySkillKarmaModifiers(
            [literal with { SourceNodeXml = xml, SourceNodeDigest = CharacterCreationQualitiesRules.ComputeSourceNodeDigest(xml) }],
            inputs.Skills.Authority, out _));
        var ambiguous = inputs.Skills.Authority with { ActiveSkills = inputs.Skills.Authority.ActiveSkills
            .Append(target with { SourceSkillId = Guid.NewGuid().ToString("D") }).ToArray() };
        Assert.IsFalse(CharacterCreationLegacySourceProjector.TrySkillKarmaModifiers([literal], ambiguous, out _));
        var denied = CharacterCreationSkillsService.PreviewCompletionKnowledgePurchases(inputs.Skills, choices,
            [literal with { SourceNodeDigest = selected.SourceNodeDigest }], [], default);
        Assert.IsNull(denied.Value);
        CollectionAssert.Contains(denied.Blockers.ToArray(), CharacterCreationSkillKarmaPurchaseBlockers.ModifiersUnsupported);
    }

    [TestMethod]
    public void Skill_karma_first_level_occurrence_does_not_change_knowledge_point_or_aggregate_pricing()
    {
        using var context = ReadyContext.Create(false, prepareSource: root =>
        {
            string path = Path.Combine(root, "data", "qualities.xml");
            var xml = XDocument.Load(path);
            var quality = xml.Root!.Element("qualities")!.Elements("quality")
                .Single(row => row.Element("id")!.Value == CollegeEducationId.ToString("D"));
            quality.SetElementValue("limit", 2);
            quality.Add(XElement.Parse("<firstlevelbonus><activeskillkarmacostmultiplier><name>Perception</name><val>50</val></activeskillkarmacostmultiplier></firstlevelbonus>"));
            xml.Save(path);
        });
        var inputs = KnowledgeInputs(context);
        var perception = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Perception");
        var choices = KnowledgeChoices(inputs) with { QualityOptionIds = [inputs.Lifestyles.Qualities.Authority.Options
            .Single(row => row.SourceId == CollegeEducationId && row.Rating == 2).OptionId] };
        choices = choices with { SkillKarmaPurchases = choices.SkillKarmaPurchases!.Append(Purchase(perception, 1)).ToArray() };
        var result = CharacterCreationCompletionReviewService.Review(inputs, KnowledgeRequest(inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var knowledge = result.Value!.Skills!.Skills.Single(row => row.Category == "Academic");
        Assert.AreEqual(3, knowledge.KarmaCost);
        Assert.AreEqual(1, knowledge.PointCost);
        Assert.AreEqual(8, knowledge.KnowledgeCategoryPrice!.Effects.Count);
        var active = result.Value.Skills.Skills.Single(row => row.SourceSkillId == perception.SourceSkillId);
        Assert.AreEqual(1, active.KarmaCost);
        Assert.AreEqual(1, active.SkillKarmaPrice!.Effects.Count);
        Assert.AreEqual("firstlevelbonus", active.SkillKarmaPrice.Effects[0].Container);
        Assert.AreEqual(1, active.SkillKarmaPrice.Effects[0].Level);
        var withoutActive = CharacterCreationCompletionReviewService.Review(inputs, KnowledgeRequest(inputs,
            choices with { SkillKarmaPurchases = choices.SkillKarmaPurchases.Where(row => row.Kind == CharacterCreationSkillKinds.Knowledge).ToArray() }));
        Assert.AreEqual(JsonSerializer.Serialize(result.Value.Skills.KnowledgeSkillPointBudget),
            JsonSerializer.Serialize(withoutActive.Value!.Skills!.KnowledgeSkillPointBudget));
        Assert.AreEqual(result.Value.Skills.KnowledgePointOverflowToActive, withoutActive.Value.Skills.KnowledgePointOverflowToActive);
    }

    internal static readonly Guid CollegeEducationId = Guid.Parse("604aea10-3f13-4f28-a87b-25b8bf677276");

    internal static CharacterCreationCompletionReviewInputs KnowledgeInputs(ReadyContext context)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var loaded = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries)
            .LoadCompletionInputsWithKnowledgeModifiers(owners.Capture(), new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        return loaded.Value!;
    }

    internal static CharacterCreationCompletionSelections KnowledgeChoices(CharacterCreationCompletionReviewInputs inputs,
        int pointRating = 1, int finalRating = 3, bool specialization = true, Guid? qualityId = null)
    {
        var source = inputs.Skills.Authority.KnowledgeSkills.First(row => row.Category == "Academic" && row.Specializations.Count > 0);
        var quality = inputs.Lifestyles.Qualities.Authority.Options.Single(row => row.SourceId == (qualityId ?? CollegeEducationId) && row.Rating == 1);
        Assert.IsTrue(quality.IsSelectable);
        var baseline = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        return baseline with
        {
            QualityOptionIds = baseline.QualityOptionIds.Append(quality.OptionId).ToArray(),
            SkillAllocations = baseline.SkillAllocations.Append(
                new CharacterCreationSkillAllocation(source.SourceSkillId, source.Kind, pointRating, null, false)).ToArray(),
            SkillKarmaPurchases = finalRating == pointRating && !specialization ? [] :
                [new(source.SourceSkillId, source.Kind, source.SourceNodeDigest, finalRating,
                    specialization ? source.Specializations[0].OptionId : null)]
        };
    }

    internal static CharacterCreationCompletionReviewRequest KnowledgeRequest(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionSelections choices) => new(inputs.SnapshotDigest, choices)
        { EffectivePolicyDigest = inputs.EffectivePolicy!.PolicyDigest };

    [TestMethod]
    public void Knowledge_modifiers_real_source_aggregate_wallet_and_point_prices()
    {
        using var context = ReadyContext.Create(false);
        var files = Snapshot(context);
        var inputs = KnowledgeInputs(context);
        var choices = KnowledgeChoices(inputs);
        var review = CharacterCreationCompletionReviewService.Review(inputs, KnowledgeRequest(inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var row = review.Value!.Skills!.Skills.Single(skill => skill.Category == "Academic");
        Assert.AreEqual(6, row.KarmaCost); // ceil(5 * .5 + 7 * .5), not ceil(2.5) + ceil(3.5).
        Assert.AreEqual(1, row.PointCost);
        Assert.AreEqual((int?)6, review.Value.Balances.SkillsKarmaCost);
        Assert.AreEqual((int?)15, review.Value.Balances.RemainingCreationKarma); // 25 - quality 4 - skill 6.
        var price = row.KnowledgeCategoryPrice!;
        Assert.AreEqual(5m, price.RawInterval);
        Assert.AreEqual(7m, price.RawSpecializations);
        Assert.AreEqual(0.5m, price.IntervalMultiplier);
        Assert.AreEqual(0m, price.IntervalAddition);
        Assert.AreEqual(4, price.Effects.Count);
        Assert.IsTrue(price.Effects.All(effect => effect.SourceId == CollegeEducationId));
        Assert.AreEqual("/character/created", price.Effects[0].Condition);
        Assert.AreEqual(3, price.Effects[0].Minimum);
        var points = CharacterCreationCompletionReviewService.Review(inputs,
            KnowledgeRequest(inputs, KnowledgeChoices(inputs, 3, 3, false)));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, points.Outcome, string.Join(",", points.Blockers));
        Assert.AreEqual(2, points.Value!.Skills!.Skills.Single(skill => skill.Category == "Academic").PointCost);
        Assert.AreEqual(2m, points.Value.Skills.KnowledgeSkillPointBudget.Used);
        Assert.AreEqual(CharacterCreationCompletionConsumerPolicy.SchemaV5, inputs.EffectivePolicy!.Consumers!.Schema);
        Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Unsupported, inputs.EffectivePolicy.Consumers.Coverage
            .Single(item => item.Operation == CharacterCreationConsumerPolicyOperation.SkillKarmaModifiers).Disposition);
        Assert.AreEqual(CharacterCreationConsumerPolicyDisposition.Admitted, inputs.EffectivePolicy.Consumers.Coverage
            .Single(item => item.Operation == CharacterCreationConsumerPolicyOperation.KnowledgeCategoryPricing).Disposition);
        AssertUnchanged(context, files);
    }

    [TestMethod]
    [DataRow(50, 1, 1, 7, 6)]
    [DataRow(25, 3, 2, 9, 5)]
    [DataRow(75, 1, 3, 5, 15)]
    public void Knowledge_modifiers_literal_and_profile_rates_are_generic(int percent, int newRate, int improveRate,
        int specializationRate, int expected)
    {
        using var context = ReadyContext.Create(false, amendSettings: settings =>
        {
            var costs = settings.Element("karmacost")!;
            costs.SetElementValue("karmanewknowledgeskill", newRate);
            costs.SetElementValue("karmaimproveknowledgeskill", improveRate);
            costs.SetElementValue("karmaknospecialization", specializationRate);
        }, prepareSource: root =>
        {
            string path = Path.Combine(root, "data", "qualities.xml");
            var xml = XDocument.Load(path);
            var quality = xml.Root!.Element("qualities")!.Elements("quality")
                .Single(row => row.Element("id")!.Value == CollegeEducationId.ToString("D"));
            quality.SetElementValue("name", "Renamed source fixture");
            foreach (var effect in quality.Element("bonus")!.Elements().Where(node => node.Name.LocalName.EndsWith("multiplier")))
                effect.SetElementValue("val", percent);
            xml.Save(path);
        });
        var inputs = KnowledgeInputs(context);
        var result = CharacterCreationCompletionReviewService.Review(inputs, KnowledgeRequest(inputs, KnowledgeChoices(inputs)));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.AreEqual(expected, result.Value!.Skills!.Skills.Single(row => row.Category == "Academic").KarmaCost);
        var pointResult = CharacterCreationCompletionReviewService.Review(inputs,
            KnowledgeRequest(inputs, KnowledgeChoices(inputs, 3, 3, false)));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, pointResult.Outcome, string.Join(",", pointResult.Blockers));
        Assert.AreEqual((int)decimal.Ceiling(3m * percent / 100m),
            pointResult.Value!.Skills!.Skills.Single(row => row.Category == "Academic").PointCost);
        // Same source bundle does not alter a different knowledge category.
        var other = inputs.Skills.Authority.KnowledgeSkills.First(row => row.Category != "Academic" && !row.CanBeNativeLanguage);
        var choices = KnowledgeChoices(inputs, 1, 1, false) with
        {
            SkillKarmaPurchases = [new(other.SourceSkillId, other.Kind, other.SourceNodeDigest, 2)]
        };
        var control = CharacterCreationCompletionReviewService.Review(inputs, KnowledgeRequest(inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, control.Outcome, string.Join(",", control.Blockers));
        Assert.AreEqual(newRate + 2 * improveRate, control.Value!.Skills!.Skills.Single(row => row.SourceSkillId == other.SourceSkillId).KarmaCost);
    }

    [TestMethod]
    [DataRow("companion")]
    [DataRow("duplicate")]
    [DataRow("expression")]
    [DataRow("condition")]
    [DataRow("bounds")]
    [DataRow("namespace")]
    public void Knowledge_modifiers_unknown_or_malformed_companions_stay_unavailable(string fault)
    {
        using var context = ReadyContext.Create(false, prepareSource: root =>
        {
            string path = Path.Combine(root, "data", "qualities.xml");
            var xml = XDocument.Load(path);
            var quality = xml.Root!.Element("qualities")!.Elements("quality")
                .Single(row => row.Element("id")!.Value == CollegeEducationId.ToString("D"));
            var bonus = quality.Element("bonus")!;
            var effect = bonus.Element("skillcategorykarmacostmultiplier")!;
            switch (fault)
            {
                case "companion": bonus.Add(new XElement("skillcategorydisable", "Academic")); break;
                case "duplicate": effect.Add(new XElement("val", 50)); break;
                case "expression": effect.SetElementValue("val", "Rating * 50"); break;
                case "condition": effect.SetElementValue("condition", "/character/karma > 2"); break;
                case "bounds": effect.SetElementValue("min", 4); effect.SetElementValue("max", 2); break;
                case "namespace": effect.Add(new XElement(XName.Get("min", "foreign"), 1)); break;
            }
            xml.Save(path);
        });
        var inputs = KnowledgeInputs(context);
        var quality = inputs.Lifestyles.Qualities.Authority.Options.Single(row => row.SourceId == CollegeEducationId);
        Assert.IsFalse(quality.IsSelectable);
        Assert.IsFalse(CharacterCreationLegacySourceProjector.IsQualitySourceProjectable(quality.SourceNodeXml));
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
            { QualityOptionIds = [quality.OptionId], SkillKarmaPurchases = [] };
        var result = CharacterCreationCompletionReviewService.Review(inputs, KnowledgeRequest(inputs, choices));
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationQualitiesBlockers.EffectsNotProjectable);
    }

    [TestMethod]
    public void Knowledge_modifiers_native_bounds_conditions_multiplicity_and_first_level_once()
    {
        using var context = ReadyContext.Create(false);
        var inputs = KnowledgeInputs(context);
        var result = CharacterCreationCompletionReviewService.Review(inputs, KnowledgeRequest(inputs, KnowledgeChoices(inputs)));
        var selection = result.Value!.UpstreamBudget.Qualities.Qualities.Selections.Single(row => row.SourceId == CollegeEducationId);
        var source = XElement.Parse(selection.SourceNodeXml);
        // A structural variant of the real source proves order/level semantics.
        source.SetElementValue("limit", 2);
        source.Element("bonus")!.Elements().First().SetElementValue("max", 5);
        source.Add(new XElement("firstlevelbonus", new XElement(source.Element("bonus")!.Elements().First())));
        string xml = source.ToString(SaveOptions.DisableFormatting);
        selection = selection with { Rating = 2, KarmaCost = 8, SourceNodeXml = xml,
            SourceNodeDigest = CharacterCreationQualitiesRules.ComputeSourceNodeDigest(xml) };
        Assert.IsTrue(CharacterCreationLegacySourceProjector.TryBuildQualityGraph(selection,
            CharacterCreationSkillsDigest.Compute("knowledge-graph"), out var qualities, out var improvements));
        Assert.AreEqual(2, qualities.Length);
        Assert.AreEqual(9, improvements.Length);
        CollectionAssert.AreEqual(new[] { "3", "0", "0", "0", "3", "3", "0", "0", "0" },
            improvements.Select(row => row.Element("min")!.Value).ToArray());
        Assert.IsTrue(improvements.Take(5).All(row => row.Element("sourcename")!.Value == qualities[0].Element("guid")!.Value));
        Assert.IsTrue(improvements.Skip(5).All(row => row.Element("sourcename")!.Value == qualities[1].Element("guid")!.Value));
        Assert.AreEqual("5", improvements[0].Element("max")!.Value);
        Assert.AreEqual("career", improvements[0].Element("condition")!.Value);
        Assert.AreEqual("create", improvements[1].Element("condition")!.Value);
        Assert.IsTrue(CharacterCreationLegacySourceProjector.TryKnowledgeModifiers([selection], inputs.Skills.Authority, out var effects));
        Assert.AreEqual(9, effects.Count);
        Assert.IsTrue(CharacterCreationKnowledgeCategoryRules.TryPrice("Academic", 1, 3, 1, 0, 1,
            1, 1, 7, effects, out var stacked));
        Assert.AreEqual(3, stacked!.KarmaCost);
        var bound = effects[1] with { Minimum = 4, Maximum = 4 };
        Assert.IsTrue(CharacterCreationKnowledgeCategoryRules.TryPrice("Academic", 1, 3, 1, 0, 1,
            1, 1, 7, [bound], out var below));
        Assert.AreEqual(12, below!.KarmaCost);
        Assert.IsFalse(CharacterCreationKnowledgeCategoryRules.TryPrice("Academic", 1, 7, 1, 0, 0,
            1, 1, 7, effects, out _));
    }

    [TestMethod]
    public void Knowledge_modifiers_ordinary_catalog_and_historical_inputs_keep_bytes()
    {
        using var context = ReadyContext.Create(false);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var old = reader.LoadCompletionInputsWithConsumerPolicy(owners.Capture(),
            new(context.WorkspaceId) { IncludeKarmaPurchasePolicy = true }).Value!;
        var bytes = JsonSerializer.Serialize(old);
        Assert.IsFalse(old.Lifestyles.Qualities.Authority.Options.Single(row => row.SourceId == CollegeEducationId).IsSelectable);
        Assert.AreEqual(CharacterCreationQualitiesBlockers.EffectsNotProjectable,
            old.Lifestyles.Qualities.Authority.Options.Single(row => row.SourceId == CollegeEducationId).DisableReasonKey);
        var current = KnowledgeInputs(context);
        Assert.AreNotEqual(old.SnapshotDigest, current.SnapshotDigest);
        var again = reader.LoadCompletionInputsWithConsumerPolicy(owners.Capture(),
            new(context.WorkspaceId) { IncludeKarmaPurchasePolicy = true }).Value!;
        Assert.AreEqual(bytes, JsonSerializer.Serialize(again));
        Assert.AreEqual(old.SnapshotDigest, again.SnapshotDigest);
        var baseline = CharacterCreationCompletionReviewServiceTests.BaselineSelections(old)
            with { SkillKarmaPurchases = [] };
        var review = CharacterCreationCompletionReviewService.Review(old,
            new(old.SnapshotDigest, baseline) { EffectivePolicyDigest = old.EffectivePolicy!.PolicyDigest });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        Assert.IsFalse(JsonSerializer.Serialize(review.Value!.Skills).Contains("KnowledgeCategoryPrice"));
    }
    [TestMethod]
    public void Knowledge_modifiers_target_without_a_source_consumer_is_rejected()
    {
        using var context = ReadyContext.Create(false, prepareSource: root =>
        {
            string path = Path.Combine(root, "data", "qualities.xml");
            var xml = XDocument.Load(path);
            var quality = xml.Root!.Element("qualities")!.Elements("quality")
                .Single(row => row.Element("id")!.Value == CollegeEducationId.ToString("D"));
            foreach (var effect in quality.Element("bonus")!.Elements())
                effect.SetElementValue("name", "No matching knowledge category");
            xml.Save(path);
        });
        var inputs = KnowledgeInputs(context);
        var files = Snapshot(context);
        var result = CharacterCreationCompletionReviewService.Review(inputs,
            KnowledgeRequest(inputs, KnowledgeChoices(inputs)));
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationSkillKarmaPurchaseBlockers.ModifiersUnsupported);
        AssertUnchanged(context, files);
    }


    internal static readonly Guid BoundedKnowledgeQualityId = Guid.Parse("cf54fa67-dc40-4f1a-a437-17339048efe2");

    internal static ReadyContext BoundedKnowledgeContext(int qualityCost = 4)
        => ReadyContext.Create(false, prepareSource: root =>
        {
            string path = Path.Combine(root, "data", "qualities.xml");
            var xml = XDocument.Load(path);
            var container = xml.Root!.Element("qualities")!;
            // Separate synthetic source in the private fixture copy. The real RF source is unchanged.
            var quality = new XElement(container.Elements("quality")
                .Single(row => row.Element("id")!.Value == CollegeEducationId.ToString("D")));
            quality.SetElementValue("id", BoundedKnowledgeQualityId.ToString("D"));
            quality.SetElementValue("name", "Bounded knowledge additive fixture");
            quality.SetElementValue("karma", qualityCost);
            quality.Element("bonus")!.ReplaceNodes(new XElement("skillcategorykarmacost",
                new XElement("name", "Academic"), new XElement("val", -1),
                new XElement("min", 1), new XElement("max", 1)));
            container.Add(quality);
            xml.Save(path);
        });

    [TestMethod]
    [DataRow(false, 4, 2, 19)]
    [DataRow(false, 23, 2, 0)]
    [DataRow(false, 24, 2, -1)]
    [DataRow(true, 4, 6, 15)]
    [DataRow(true, 19, 6, 0)]
    [DataRow(true, 20, 6, -1)]
    public void Knowledge_modifiers_point_only_addition_and_final_purchase_use_one_shared_wallet(
        bool purchase, int qualityCost, int expectedCost, int expectedRemaining)
    {
        using var context = BoundedKnowledgeContext(qualityCost);
        var inputs = KnowledgeInputs(context);
        var choices = KnowledgeChoices(inputs, 3, purchase ? 4 : 3, false, BoundedKnowledgeQualityId);
        var files = Snapshot(context);
        var result = CharacterCreationCompletionReviewService.Review(inputs, KnowledgeRequest(inputs, choices));
        Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
        var row = result.Value.Skills!.Skills.Single(skill => skill.Category == "Academic");
        Assert.AreEqual(expectedCost, row.KarmaCost);
        Assert.AreEqual(3, row.PointCost);
        Assert.AreEqual(purchase ? 1 : 0, row.KarmaLevels);
        Assert.AreEqual(2m, row.KnowledgeCategoryPrice!.IntervalAddition); // -1 * (1 - 3)
        Assert.AreEqual(expectedCost, row.KnowledgeCategoryPrice.KarmaCost);
        Assert.AreEqual(purchase ? 4m : 0m, row.KnowledgeCategoryPrice.RawInterval);
        Assert.AreEqual((int?)expectedCost, result.Value.Balances.SkillsKarmaCost);
        Assert.AreEqual((int?)expectedRemaining, result.Value.Balances.RemainingCreationKarma);
        if (expectedRemaining < 0)
        {
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationFinalizationBlockers.GlobalKarmaExceeded);
        }
        else
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        AssertUnchanged(context, files);
    }

    [TestMethod]
    [DataRow("source", "creation-qualities-source-disabled")]
    [DataRow("requirement", "creation-qualities-requirement-projection-pending")]
    public void Knowledge_modifiers_completion_extension_preserves_earlier_catalog_blockers(string fault, string expected)
    {
        using var context = ReadyContext.Create(false, prepareSource: root =>
        {
            string path = Path.Combine(root, "data", "qualities.xml");
            var xml = XDocument.Load(path);
            var quality = xml.Root!.Element("qualities")!.Elements("quality")
                .Single(row => row.Element("id")!.Value == CollegeEducationId.ToString("D"));
            if (fault == "source") quality.SetElementValue("source", "CORE42-disabled-source");
            else quality.Add(new XElement("required", new XElement("oneof", new XElement("quality", "Guts"))));
            xml.Save(path);
        });
        var inputs = KnowledgeInputs(context);
        var quality = inputs.Lifestyles.Qualities.Authority.Options.Single(row => row.SourceId == CollegeEducationId);
        Assert.IsFalse(quality.IsSelectable);
        Assert.AreEqual(expected, quality.DisableReasonKey);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
            { QualityOptionIds = [quality.OptionId], SkillKarmaPurchases = [] };
        var result = CharacterCreationCompletionReviewService.Review(inputs, KnowledgeRequest(inputs, choices));
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
        CollectionAssert.Contains(result.Blockers.ToArray(), expected);
    }

    [TestMethod]
    [DataRow("", "")]
    [DataRow("create", "create")]
    [DataRow("career", "career")]
    [DataRow("/character/created = false", "create")]
    [DataRow("/character/created", "career")]
    [DataRow("Create", null)]
    [DataRow(" create", null)]
    [DataRow("/character/created = false()", null)]
    [DataRow("/character/created=true", null)]
    [DataRow("/character/karma > 2", null)]
    public void Knowledge_modifiers_lifecycle_lowering_is_exact_and_keeps_source_provenance(string condition, string? native)
    {
        using var context = ReadyContext.Create(false);
        var inputs = KnowledgeInputs(context);
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, KnowledgeRequest(inputs, KnowledgeChoices(inputs)));
        var selection = reviewed.Value!.UpstreamBudget.Qualities.Qualities.Selections.Single(row => row.SourceId == CollegeEducationId);
        var source = XElement.Parse(selection.SourceNodeXml);
        var effect = new XElement(source.Element("bonus")!.Element("skillcategorykarmacostmultiplier")!);
        effect.SetElementValue("condition", condition);
        source.Element("bonus")!.ReplaceNodes(effect);
        string xml = source.ToString(SaveOptions.DisableFormatting);
        selection = selection with { SourceNodeXml = xml, SourceNodeDigest = CharacterCreationQualitiesRules.ComputeSourceNodeDigest(xml) };
        bool built = CharacterCreationLegacySourceProjector.TryBuildQualityGraph(selection,
            CharacterCreationSkillsDigest.Compute("exact-lifecycle"), out _, out var improvements);
        Assert.AreEqual(native is not null, built);
        Assert.AreEqual(native is not null,
            CharacterCreationLegacySourceProjector.TryKnowledgeModifiers([selection], inputs.Skills.Authority, out var effects));
        if (native is null) return;
        Assert.AreEqual(native, improvements.Single().Element("condition")!.Value);
        var retained = effects.Single();
        Assert.AreEqual(condition, retained.Condition);
        Assert.AreEqual(selection.SourceNodeDigest, retained.SourceNodeDigest);
        Assert.AreEqual(selection.SourceId, retained.SourceId);
        Assert.AreEqual(selection.OptionId, retained.OptionId);
        Assert.AreEqual("bonus", retained.Container);
        Assert.AreEqual(0, retained.Position);
        Assert.AreEqual(1, retained.Level);
        Assert.IsTrue(CharacterCreationKnowledgeCategoryRules.TryPrice("Academic", 1, 3, 1, 0, 0,
            1, 1, 7, effects, out var price));
        Assert.AreEqual(native == "career" ? 5 : 3, price!.KarmaCost);
    }

    [TestMethod]
    public void Knowledge_modifiers_discounted_knowledge_overflow_respects_exhausted_active_pool()
    {
        using var context = ReadyContext.Create(false);
        var inputs = KnowledgeInputs(context);
        var choices = KnowledgeChoices(inputs, 3, 3, false);
        var initial = CharacterCreationCompletionReviewService.Review(inputs, KnowledgeRequest(inputs, choices)).Value!.Skills!;
        Assert.AreEqual(2, initial.Skills.Single(row => row.Category == "Academic").PointCost);
        var allocations = choices.SkillAllocations.ToList();
        int activeRemaining = (int)initial.ActiveSkillPointBudget.Remaining;
        foreach (var source in inputs.Skills.Authority.ActiveSkills.Where(row => !row.IsExotic
                     && !row.RequiresGroundMovement && !row.RequiresSwimMovement && !row.RequiresFlyMovement
                     && CharacterCreationSkillsAccessRules.IsSkillAvailable(inputs.Skills.Authority, row.SourceSkillId)
                     && !allocations.Any(allocation => allocation.SourceSkillId == row.SourceSkillId)
                     && !initial.SkillGroups.Any(group => group.MemberSkillSourceIds.Contains(row.SourceSkillId))))
        {
            if (activeRemaining == 0) break;
            int rating = Math.Min(activeRemaining, inputs.Skills.Authority.MaxActiveSkillRatingCreate);
            allocations.Add(new(source.SourceSkillId, source.Kind, rating, null, false));
            activeRemaining -= rating;
        }
        Assert.AreEqual(0, activeRemaining, "Fixture must actually exhaust the active pool.");
        int knowledgeRemaining = (int)initial.KnowledgeSkillPointBudget.Remaining;
        string? lastKnowledge = null;
        foreach (var source in inputs.Skills.Authority.KnowledgeSkills.Where(row => row.Category != "Academic"
                     && !row.CanBeNativeLanguage && !row.IsExotic
                     && !allocations.Any(allocation => allocation.SourceSkillId == row.SourceSkillId)))
        {
            if (knowledgeRemaining == 0) break;
            // Leave a legal extra rank available for the one-point overflow boundary.
            int rating = Math.Min(knowledgeRemaining, inputs.Skills.Authority.MaxKnowledgeSkillRatingCreate - 1);
            allocations.Add(new(source.SourceSkillId, source.Kind, rating, null, false));
            lastKnowledge = source.SourceSkillId;
            knowledgeRemaining -= rating;
        }
        Assert.AreEqual(0, knowledgeRemaining);
        Assert.IsNotNull(lastKnowledge);
        choices = choices with { SkillAllocations = allocations.ToArray() };
        var exact = CharacterCreationCompletionReviewService.Review(inputs, KnowledgeRequest(inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, exact.Outcome, string.Join(",", exact.Blockers));
        Assert.AreEqual(0m, exact.Value!.Skills!.ActiveSkillPointBudget.Remaining);
        Assert.AreEqual(0m, exact.Value.Skills.KnowledgeSkillPointBudget.Remaining);
        Assert.AreEqual(0, exact.Value.Skills.KnowledgePointOverflowToActive);
        var overflow = choices with { SkillAllocations = choices.SkillAllocations.Select(row =>
            row.SourceSkillId == lastKnowledge ? row with { Rating = row.Rating + 1 } : row).ToArray() };
        var refused = CharacterCreationCompletionReviewService.Review(inputs, KnowledgeRequest(inputs, overflow));
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, refused.Outcome);
        CollectionAssert.Contains(refused.Blockers.ToArray(), CharacterCreationSkillsBlockers.ActiveBudgetExceeded);
        Assert.AreEqual(1, refused.Value!.Skills!.KnowledgePointOverflowToActive);
        // Returning one active point makes exactly that same discounted overflow legal.
        var active = overflow.SkillAllocations.First(row => row.Kind == CharacterCreationSkillKinds.Active && row.Rating > 1);
        var funded = overflow with { SkillAllocations = overflow.SkillAllocations.Select(row =>
            row.SourceSkillId == active.SourceSkillId ? row with { Rating = row.Rating - 1 } : row).ToArray() };
        var accepted = CharacterCreationCompletionReviewService.Review(inputs, KnowledgeRequest(inputs, funded));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, accepted.Outcome, string.Join(",", accepted.Blockers));
        Assert.AreEqual(1, accepted.Value!.Skills!.KnowledgePointOverflowToActive);
        Assert.AreEqual(0m, accepted.Value.Skills.ActiveSkillPointBudget.Remaining);
    }
}
