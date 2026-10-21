using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    public void Historical_reader_cannot_claim_source_setting_consumption_through_default_interface()
    {
        IOwnerBoundCharacterCreationCompletionReader reader = new HistoricalOnlyCompletionReader();
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var result = reader.LoadCompletionInputsWithPolicyConsumption(
            owners.Capture(), new(new CharacterWorkspaceId("unread-workspace")));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.IsNull(result.Value);
        CollectionAssert.AreEqual(new[] { CharacterCreationEffectivePolicyContext.Unavailable }, result.Blockers.ToArray());
    }

    [TestMethod]
    public void Consumed_skill_prices_and_both_purchased_lifestyle_effect_scopes_replay_as_occurrences()
    {
        Guid qualityId = CharacterCreationCompletionPlannerTests.CollegeEducationId;
        using var context = ReadyContext.Create(false, includeNonEmptyPurchases: true, includeSkillPurchase: true,
            amendSettings: settings => settings.Element("karmacost")!.SetElementValue("karmaimproveactiveskill", 3),
            prepareSource: root =>
            {
                string path = Path.Combine(root, "data", "qualities.xml");
                var document = XDocument.Load(path);
                var quality = document.Root!.Element("qualities")!.Elements("quality")
                    .Single(row => row.Element("id")?.Value == qualityId.ToString("D"));
                quality.Element("bonus")!.ReplaceNodes(
                    XElement.Parse("<skillcategorykarmacostmultiplier><name>Combat Active</name><val>50</val></skillcategorykarmacostmultiplier>"),
                    new XElement("lifestylecost", "-10"), new XElement("basiclifestylecost", "-5"));
                document.Save(path);
            });
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        var pistols = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var home = CharacterCreationCompletionReviewServiceTests.LowLifestyle(inputs.Lifestyles.Authority);
        home = CharacterCreationLifestylesRules.AddBuiltInQualities(home,
            inputs.Lifestyles.Authority.LifestyleOptions.Single(row => row.OptionId == home.BaseLifestyleOptionId));
        request = request with { Selections = request.Selections with
        {
            QualityOptionIds = [.. request.Selections.QualityOptionIds,
                inputs.Lifestyles.Qualities.Authority.Options.Single(row => row.SourceId == qualityId && row.Rating == 1).OptionId],
            SkillKarmaPurchases = [new(pistols.SourceSkillId, pistols.Kind, pistols.SourceNodeDigest, 3)],
            Lifestyles = [.. request.Selections.Lifestyles, home],
            StartingLifestyleId = request.Selections.StartingLifestyleId ?? home.LifestyleId
        } };
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var evidence = reviewed.Value!.PolicyConsumption!;
        Assert.AreEqual(CharacterCreationSettingConsumptionReason.Evaluated,
            evidence.Settings.Single(row => row.Key == CharacterCreationObservedSetting.ImproveActiveSkill).Reason);
        Assert.IsTrue(evidence.Effects.Any(row => row.Operation == CharacterCreationConsumerPolicyOperation.SkillKarmaModifiers
            && row.Reason == CharacterCreationSettingConsumptionReason.Evaluated));
        Assert.AreEqual(2, evidence.Effects.Where(row => row.Operation == CharacterCreationConsumerPolicyOperation.LifestyleCostEffects
            && row.Reason == CharacterCreationSettingConsumptionReason.Evaluated).Select(row => row.SourceOccurrenceDigest).Distinct().Count());
        AssertEffectReceiptTamperingRefused(context, inputs, request);
        AssertV4Commit(context, inputs, request, reviewed.Value,
            expectedWitnessSchema: CharacterCreationSelectedCompletionWitness.SchemaV6);
    }

    [TestMethod]
    public void Separate_adept_magic_requires_actual_attribute_and_magic_evaluation()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            talentValue: "Mystic Adept",
            amendSettings: settings => settings.SetElementValue("mysadeptsecondmagattribute", "True"));
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var rows = reviewed.Value!.PolicyConsumption!.Settings;
        Assert.AreEqual(CharacterCreationSettingConsumptionReason.Evaluated,
            rows.Single(row => row.Key == CharacterCreationObservedSetting.SeparateAdeptMagic).Reason);
        Assert.AreNotEqual(CharacterCreationSettingConsumptionReason.Evaluated,
            rows.Single(row => row.Key == CharacterCreationObservedSetting.MysticAdeptKarma).Reason);
        AssertV4Commit(context, inputs, request, reviewed.Value,
            expectedWitnessSchema: CharacterCreationSelectedCompletionWitness.SchemaV6);
    }

    [TestMethod]
    public void Consumed_settings_bind_nondefault_source_values_actual_evaluations_and_cold_receipts()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: settings =>
        {
            settings.SetElementValue("maxnumbermaxattributescreate", "2");
            settings.SetElementValue("contactpointsexpression", "{CHAUnaug} * 4");
            settings.Add(new XElement("private-setting-name", "private-text /private/path"));
        });
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        request = request with { Selections = request.Selections with { TotalKarmaInvestment = 2 } };
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var proof = reviewed.Value!.PolicyConsumption!;
        Assert.IsNotNull(proof);
        foreach (var key in new[] { CharacterCreationObservedSetting.MaximumAttributes, CharacterCreationObservedSetting.KnowledgePoints,
                     CharacterCreationObservedSetting.ContactPoints, CharacterCreationObservedSetting.ResourceKarmaMaximum })
        {
            var row = proof.Settings.Single(row => row.Key == key);
            Assert.AreEqual(CharacterCreationSettingConsumptionReason.Evaluated, row.Reason, key.ToString());
            Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(row.ConsumerInputsDigest));
            Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(row.EvaluationDigest));
            Assert.AreEqual(inputs.EffectivePolicy!.SettingObservations!.Settings.Single(item => item.Key == key).SourceValueDigest, row.SourceValueDigest);
        }
        Assert.IsTrue(proof.Settings.Any(row => row.Reason == CharacterCreationSettingConsumptionReason.OpaqueSource));
        string evidence = JsonSerializer.Serialize(proof);
        Assert.IsFalse(evidence.Contains("private-setting-name", StringComparison.Ordinal));
        Assert.IsFalse(evidence.Contains("private-text", StringComparison.Ordinal));
        Assert.IsFalse(evidence.Contains("/private/path", StringComparison.Ordinal));
        Assert.IsTrue(proof.RemainingOperations.Contains(CharacterCreationConsumerPolicyOperation.Campaign));
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete }).Value);
        AssertV4Commit(context, inputs, request, reviewed.Value,
            expectedWitnessSchema: CharacterCreationSelectedCompletionWitness.SchemaV6);
    }

    [TestMethod]
    public void Consumed_settings_do_not_turn_false_flags_zero_purchases_or_missing_defaults_into_evidence()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            amendSettings: settings => settings.Element("allowfreegrids")?.Remove());
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var proof = reviewed.Value!.PolicyConsumption!;
        Assert.AreEqual(CharacterCreationSettingConsumptionReason.AbsentSourceDefaultUnproven,
            proof.Settings.Single(row => row.Key == CharacterCreationObservedSetting.FreeGrids).Reason);
        foreach (var key in new[] { CharacterCreationObservedSetting.NewActiveSkill, CharacterCreationObservedSetting.NewKnowledgeSkill,
                     CharacterCreationObservedSetting.SeparateAdeptMagic, CharacterCreationObservedSetting.ExceedPositiveQualities })
            Assert.AreNotEqual(CharacterCreationSettingConsumptionReason.Evaluated, proof.Settings.Single(row => row.Key == key).Reason);
        Assert.IsTrue(proof.Settings.Where(row => row.Reason != CharacterCreationSettingConsumptionReason.Evaluated)
            .All(row => row.EvaluationDigest is null && row.ConsumerInputsDigest is null));
    }

    [TestMethod]
    public void Unselected_specializations_do_not_claim_group_break_policy_consumption()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true,
            amendSettings: settings => settings.SetElementValue("specializationsbreakskillgroups", "False"));
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        Assert.IsTrue(request.Selections.GroupAllocations.Any(row => row.Rating > 0));
        Assert.IsTrue(request.Selections.SkillAllocations.All(row => row.SpecializationOptionId is null));
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var row = reviewed.Value!.PolicyConsumption!.Settings.Single(item =>
            item.Key == CharacterCreationObservedSetting.SpecializationsBreakGroups);
        Assert.AreEqual(CharacterCreationSettingConsumptionReason.EvaluatedUseUnavailable, row.Reason);
        Assert.IsNull(row.EvaluationDigest);
        Assert.IsNull(row.ConsumerInputsDigest);
    }

    [TestMethod]
    public void Selected_group_specialization_accounts_actual_policy_use_and_replays()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true, amendSettings: settings =>
        {
            settings.SetElementValue("breakskillgroupsincreatemode", "False");
            settings.SetElementValue("specializationsbreakskillgroups", "False");
        });
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        var allocation = request.Selections.GroupAllocations.Single();
        var group = inputs.Skills.Authority.SkillGroups.Single(item => item.GroupId == allocation.GroupId);
        var member = inputs.Skills.Authority.ActiveSkills.Single(item =>
            group.MemberSkillSourceIds.Contains(item.SourceSkillId) && item.Specializations.Count > 0
                && string.Equals(item.Name, "Gymnastics", StringComparison.Ordinal));
        request = request with { Selections = request.Selections with
        {
            SkillAllocations = [.. request.Selections.SkillAllocations,
                new(member.SourceSkillId, member.Kind, allocation.Rating, member.Specializations[0].OptionId, false)]
        } };
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var row = reviewed.Value!.PolicyConsumption!.Settings.Single(item =>
            item.Key == CharacterCreationObservedSetting.SpecializationsBreakGroups);
        Assert.AreEqual(CharacterCreationSettingConsumptionReason.Evaluated, row.Reason);
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(row.EvaluationDigest));
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(row.ConsumerInputsDigest));
        AssertV4Commit(context, inputs, request, reviewed.Value,
            expectedWitnessSchema: CharacterCreationSelectedCompletionWitness.SchemaV6);
    }

    [TestMethod]
    public void Rehashed_source_observations_cannot_omit_add_reclassify_or_downgrade_obligations()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var inputs = LoadConsumption(context);
        var policy = inputs.EffectivePolicy!;
        var observation = policy.SettingObservations!;
        var row = observation.Settings.First(item => item.State == CharacterCreationSettingObservationState.Present);
        foreach (var forged in new[]
                 {
                     observation with { Settings = observation.Settings.Skip(1).ToArray() },
                     observation with { Settings = observation.Settings.Append(row).ToArray() },
                     observation with { Settings = observation.Settings.Select(item => item == row
                         ? item with { ValueDigest = CharacterCreationFinalizationDigest.Compute("different value") } : item).ToArray() },
                     observation with { Settings = observation.Settings.Select(item => item == row
                         ? item with { SourceValueDigest = CharacterCreationFinalizationDigest.Compute("different source") } : item).ToArray() },
                     observation with { Schema = "chummer.creation-settings-observation.v0" }
                 })
        {
            var changed = WithPolicy(inputs, policy with { SettingObservations = forged });
            var refused = CharacterCreationCompletionReviewService.Review(changed, Request(changed, CharacterCreationPolicyRequirement.None));
            Assert.IsNull(refused.Value);
            CollectionAssert.Contains(refused.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.Invalid);
        }
    }

    [TestMethod]
    public void Independent_replay_rejects_rehashed_evaluation_claims_missing_extra_duplicate_and_downgraded_receipts()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        var proof = witness.EffectivePolicy!.SelectedConsumption!;
        foreach (string? schema in new string?[] { null, "chummer.creation-policy-consumption.v0" })
        {
            var downgradedRequest = request with { PolicyConsumptionSchema = schema };
            Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs, downgradedRequest).Value);
            var downgraded = witness with { ProposalDigest = CharacterCreationFinalizationDigest.Compute(downgradedRequest) };
            downgraded = downgraded with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(downgraded) };
            Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, downgraded, downgradedRequest).Value);
        }
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, witness, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var coldWitness));
        Assert.IsNotNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, coldWitness, request).Value);
        var evaluated = proof.Settings.First(row => row.Reason == CharacterCreationSettingConsumptionReason.Evaluated);
        foreach (var forged in new CharacterCreationPolicyConsumption?[]
                 {
                     null,
                     proof with { Settings = proof.Settings.Skip(1).ToArray() },
                     proof with { Settings = proof.Settings.Append(proof.Settings[0]).ToArray() },
                     proof with { RemainingOperations = [] },
                     proof with { RemainingOperations = proof.RemainingOperations.Append(CharacterCreationConsumerPolicyOperation.Campaign).ToArray() },
                     proof with { Settings = proof.Settings.Select(row => row == evaluated
                         ? row with { EvaluationDigest = CharacterCreationFinalizationDigest.Compute("forged result") } : row).ToArray() },
                     proof with { Schema = "chummer.creation-policy-consumption.v0" }
                 })
        {
            var changed = witness with { EffectivePolicy = witness.EffectivePolicy with { SelectedConsumption = forged } };
            changed = changed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changed) };
            Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, changed, request).Value);
        }
        // Selected-only evidence can never be supplied as a fresh input authority.
        var injected = WithPolicy(inputs, witness.EffectivePolicy);
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(injected, Request(injected, CharacterCreationPolicyRequirement.None)).Value);
    }

    [TestMethod]
    public void Historical_policy_reads_keep_omitted_accounting_fields_and_original_digests()
    {
        using var context = ReadyContext.Create(false, includeSkillPurchase: true);
        var before = LoadConsumers(context, includeSkillPurchases: true);
        string bytes = JsonSerializer.Serialize(before);
        _ = LoadConsumption(context);
        var after = LoadConsumers(context, includeSkillPurchases: true);
        Assert.AreEqual(bytes, JsonSerializer.Serialize(after));
        Assert.IsFalse(bytes.Contains("SettingObservations", StringComparison.Ordinal));
        Assert.IsFalse(bytes.Contains("SelectedConsumption", StringComparison.Ordinal));
        var reviewed = CharacterCreationCompletionReviewService.Review(after, Request(after, CharacterCreationPolicyRequirement.None));
        Assert.IsNotNull(reviewed.Value);
        Assert.IsFalse(JsonSerializer.SerializeToElement(reviewed.Value).TryGetProperty("PolicyConsumption", out _));
    }

    private static CharacterCreationCompletionReviewInputs LoadConsumption(ReadyContext context)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        IOwnerBoundCharacterCreationCompletionReader reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var result = reader.LoadCompletionInputsWithPolicyConsumption(owners.Capture(), new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value!.EffectivePolicy!.SettingObservations);
        Assert.IsNull(result.Value.EffectivePolicy.SelectedConsumption);
        return result.Value;
    }

    private sealed class HistoricalOnlyCompletionReader : IOwnerBoundCharacterCreationCompletionReader
    {
        public CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputs(
            Chummer.Application.Owners.OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request,
            CancellationToken cancellationToken = default)
            => throw new AssertFailedException("A historical read must not be reused as source-consumption authority.");
    }

    private static void AssertEffectReceiptTamperingRefused(ReadyContext context, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request)
    {
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capture.Outcome, string.Join(",", capture.Blockers));
        var witness = capture.Value!;
        var proof = witness.EffectivePolicy!.SelectedConsumption!;
        Assert.IsTrue(proof.Effects.Count > 0);
        foreach (var effects in new[] { proof.Effects.Skip(1).ToArray(), proof.Effects.Append(proof.Effects[0]).ToArray(),
                     proof.Effects.Select((row, index) => index == 0 ? row with
                         { SourceOccurrenceDigest = CharacterCreationFinalizationDigest.Compute("changed occurrence") } : row).ToArray() })
        {
            var changed = witness with { EffectivePolicy = witness.EffectivePolicy with { SelectedConsumption = proof with { Effects = effects } } };
            changed = changed with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changed) };
            Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, changed, request).Value);
        }
    }
}
